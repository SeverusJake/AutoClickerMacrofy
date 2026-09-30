using System.Collections.Concurrent;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows.Interop;
namespace Macrofy.Platform.Windows;

public sealed class WindowsGlobalHotkeys : IGlobalHotkeys, ISystemEvents, IDisposable
{
    readonly IHotkeyNative native;
    readonly Thread thread;
    readonly ConcurrentQueue<Action> work = new();
    readonly ManualResetEventSlim ready = new();
    readonly object synchronization = new();
    readonly Dictionary<(uint Modifiers,uint Key),int> registrations = new();
    Dictionary<int,HotkeyCommand> commands = new();
    volatile bool disposed;
    Exception? failure;
    int nextId;
    bool suspended;
    public event Action<HotkeyCommand>? Triggered;
    public event Action? Suspended;
    public WindowsGlobalHotkeys() : this(new HotkeyNative()) { }
    internal WindowsGlobalHotkeys(IHotkeyNative native)
    {
        this.native = native;
        thread = new Thread(Loop) { IsBackground = true, Name = "Macrofy global controls" };
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(2))) { failure = new TimeoutException("Hotkey window startup timed out."); disposed = true; }
    }
    public HotkeyRegistrationResult Configure(HotkeySet hotkeys)
    {
        lock(synchronization)
        {
            if(disposed || failure is not null) return Error(failure?.Message ?? "Hotkey service is disposed.");
            var parsed = new List<((uint Modifiers,uint Key) Key,HotkeyCommand Command,HotkeyBinding Binding)>();
            foreach(var (binding,command) in new[] {(hotkeys.Run,HotkeyCommand.Run),(hotkeys.Pause,HotkeyCommand.Pause),(hotkeys.Stop,HotkeyCommand.Stop)})
            {
                var text = binding.Key;
                if(text is null || text.Length < 2 || char.ToUpperInvariant(text[0]) != 'F' || !int.TryParse(text.AsSpan(1),out var number) || number < 1 || number > 12)
                    return Error($"{command} binding {text}: expected F1–F12.");
                if(number == 12) return Error($"{command} binding F12 is reserved for the Windows debugger.");
                var key = (0x4000u | (binding.Alt ? 1u:0) | (binding.Control ? 2u:0) | (binding.Shift ? 4u:0), (uint)(111+number));
                if(parsed.Any(x => x.Key == key)) return Error($"{command} binding {text} duplicates another control binding.");
                parsed.Add((key,command,binding));
            }
            var completion = new TaskCompletionSource<HotkeyRegistrationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action configure = () =>
            {
                var added = new List<((uint,uint) Key,int Id)>();
                try
                {
                    foreach(var entry in parsed)
                    {
                        if(registrations.ContainsKey(entry.Key)) continue;
                        if(nextId == 0xBFFF) nextId = 0;
                        var id = ++nextId;
                        while(registrations.ContainsValue(id) || added.Any(a => a.Id == id)) id = ++nextId;
                        if(!native.Register(id,entry.Key.Modifiers,entry.Key.Key,out var error))
                        {
                            foreach(var addition in added) native.Unregister(addition.Id);
                            completion.TrySetResult(Error($"{entry.Command} binding {entry.Binding.Key}: RegisterHotKey failed, Win32 error {error} ({new System.ComponentModel.Win32Exception(error).Message}).")); return;
                        }
                        added.Add((entry.Key,id));
                    }
                    foreach(var addition in added) registrations.Add(addition.Key,addition.Id);
                    commands = parsed.ToDictionary(x => registrations[x.Key],x => x.Command);
                    foreach(var old in registrations.Keys.Where(k => !parsed.Any(p => p.Key == k)).ToArray())
                    { native.Unregister(registrations[old]); registrations.Remove(old); }
                    completion.TrySetResult(new(true));
                }
                catch(Exception ex)
                {
                    foreach(var addition in added) { native.Unregister(addition.Id); registrations.Remove(addition.Key); }
                    completion.TrySetResult(Error(ex.Message));
                }
            };
            if(Thread.CurrentThread == thread) configure(); else work.Enqueue(configure);
            if(!completion.Task.Wait(TimeSpan.FromSeconds(2))) { disposed = true; return Error("Hotkey configuration timed out; service disabled."); }
            return completion.Task.Result;
        }
    }
    static HotkeyRegistrationResult Error(string message) => new(false,new("HotkeyRegistrationFailed",message));
    void Loop()
    {
        try
        {
            native.Initialize(); ready.Set();
            while(!disposed)
            {
                while(work.TryDequeue(out var action)) action();
                native.Pump(OnMessage); Thread.Sleep(5);
            }
        }
        catch(Exception ex) { failure = ex; }
        finally
        {
            ready.Set();
            foreach(var id in registrations.Values) { try { native.Unregister(id); } catch { } }
            registrations.Clear();
            try { native.Dispose(); } catch { }
        }
    }
    void OnMessage(uint message,nuint value)
    {
        if(message == 0x312 && commands.TryGetValue((int)value,out var command))
            foreach(var handler in Triggered?.GetInvocationList() ?? []) { try { ((Action<HotkeyCommand>)handler)(command); } catch { } }
        if(message != 0x218) return;
        if(value is 7 or 18) suspended = false;
        if(value != 4 || suspended) return;
        suspended = true;
        foreach(var handler in Suspended?.GetInvocationList() ?? []) { try { ((Action)handler)(); } catch { } }
    }
    public void Dispose()
    {
        disposed = true;
        if(Thread.CurrentThread != thread) thread.Join(TimeSpan.FromSeconds(2));
    }
}


