using System.Collections.Concurrent;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows.Interop;
namespace Macrofy.Platform.Windows;

public sealed class WindowsGlobalHotkeys : IGlobalHotkeys, ISystemEvents, IHotkeyHealth, IDisposable
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
    volatile bool operational;
    PlatformError? operationalError;
    public bool IsOperational => operational && !disposed && failure is null;
    public PlatformError? OperationalError => Volatile.Read(ref operationalError);
    public event Action? HealthChanged;
    void PublishHealth(bool available, string? error = null)
    {
        operational = available;
        Volatile.Write(ref operationalError, error is null ? null : new PlatformError("HotkeyUnavailable", error));
        foreach (var handler in HealthChanged?.GetInvocationList() ?? [])
            try { ((Action)handler)(); } catch { }
    }
    public event Action<HotkeyCommand>? Triggered;
    public event Action? Suspended;
    public WindowsGlobalHotkeys() : this(new HotkeyNative()) { }
    internal WindowsGlobalHotkeys(IHotkeyNative native)
    {
        this.native = native;
        thread = new Thread(Loop) { IsBackground = true, Name = "Macrofy global controls" };
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(2))) { failure = new TimeoutException("Hotkey window startup timed out."); disposed = true; PublishHealth(false, failure.Message); }
    }
    public HotkeyRegistrationResult Configure(HotkeySet hotkeys)
    {
        {
            if(disposed || failure is not null) return Error(failure?.Message ?? "Hotkey service is disposed.");
            var parsed = new List<((uint Modifiers,uint Key) Key,HotkeyCommand Command,HotkeyBinding Binding)>();
            var bindings = new List<(HotkeyBinding Binding,HotkeyCommand Command)> {(hotkeys.Run,HotkeyCommand.Run),(hotkeys.Pause,HotkeyCommand.Pause),(hotkeys.Stop,HotkeyCommand.Stop)};
            if(hotkeys.Capture is { } capture) bindings.Add((capture,HotkeyCommand.Capture));
            foreach(var (binding,command) in bindings)
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
                var committed = false;
                try
                {
                    if(disposed) { completion.TrySetResult(Error("Hotkey service is disposed.")); return; }
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
                        if(disposed) throw new ObjectDisposedException(nameof(WindowsGlobalHotkeys));
                    }
                    if(!Monitor.TryEnter(synchronization,TimeSpan.FromSeconds(2))) throw new TimeoutException("Hotkey state commit timed out.");
                    try
                    {
                        if(disposed) throw new ObjectDisposedException(nameof(WindowsGlobalHotkeys));
                        foreach(var addition in added) registrations.Add(addition.Key,addition.Id);
                        commands = parsed.ToDictionary(x => registrations[x.Key],x => x.Command);
                        committed = true; operational = true;
                    }
                    finally { Monitor.Exit(synchronization); }
                    var cleanupErrors = new List<string>();
                    foreach(var old in registrations.Keys.Where(k => !parsed.Any(p => p.Key == k)).ToArray())
                    {
                        try { native.Unregister(registrations[old]); registrations.Remove(old); }
                        catch(Exception ex) { cleanupErrors.Add($"F{old.Key - 111}: UnregisterHotKey failed, {Describe(ex)}"); }
                    }
                    if(!Monitor.TryEnter(synchronization,TimeSpan.FromSeconds(2))) throw new TimeoutException("Hotkey state completion timed out.");
                    try
                    {
                        if(disposed) completion.TrySetResult(Error("Hotkey service was disabled during configuration."));
                        else completion.TrySetResult(new(true,cleanupErrors.Count == 0 ? null : new("HotkeyCleanupFailed", "New hotkey set is active; obsolete registration cleanup failed: " + string.Join("; ",cleanupErrors))));
                    }
                    finally { Monitor.Exit(synchronization); }
                }
                catch(Exception ex)
                {
                    if(!committed)
                        foreach(var addition in added) { try { native.Unregister(addition.Id); } catch { } registrations.Remove(addition.Key); }
                    completion.TrySetResult(committed && !disposed
                        ? new(true,new("HotkeyCleanupFailed", "New hotkey set is active; cleanup failed: " + Describe(ex)))
                        : Error(Describe(ex)));
                }
            };
            if(Thread.CurrentThread == thread) configure(); else work.Enqueue(configure);
            if(!completion.Task.Wait(TimeSpan.FromSeconds(2)))
            {
                if(!Monitor.TryEnter(synchronization,TimeSpan.FromSeconds(2))) { disposed = true; PublishHealth(false, "Hotkey state shutdown timed out; service disabled."); return Error("Hotkey state shutdown timed out; service disabled."); }
                try
                {
                    if(!completion.Task.IsCompleted) { disposed = true; PublishHealth(false, "Hotkey configuration timed out; service disabled."); return Error("Hotkey configuration timed out; service disabled."); }
                }
                finally { Monitor.Exit(synchronization); }
            }
            var result = completion.Task.Result; if (result.Registered) PublishHealth(IsOperational); return result;
        }
    }
    static string Describe(Exception ex) => ex is System.ComponentModel.Win32Exception win32 ? $"Win32 error {win32.NativeErrorCode} ({win32.Message})" : ex.Message;
    static HotkeyRegistrationResult Error(string message) => new(false,new("HotkeyRegistrationFailed",message));
    void Loop()
    {
        try
        {
            native.Initialize(); ready.Set();
            while(!disposed)
            {
                while(!disposed && work.TryDequeue(out var action)) action();
                if(disposed) break;
                native.Pump(OnMessage); Thread.Sleep(5);
            }
        }
        catch(Exception ex) { failure = ex; }
        finally
        {
            PublishHealth(false, failure?.Message ?? "Hotkey service stopped.");
            ready.Set();
            foreach(var id in registrations.Values) { try { native.Unregister(id); } catch { } }
            registrations.Clear();
            try { native.Dispose(); } catch { }
        }
    }
    void OnMessage(uint message,nuint value)
    {
        if(disposed) return;
        if(message == 0x312 && commands.TryGetValue((int)value,out var command))
            foreach(var handler in Triggered?.GetInvocationList() ?? []) { if(disposed) return; try { ((Action<HotkeyCommand>)handler)(command); } catch { } }
        if(message != 0x218) return;
        if(value is 7 or 18) suspended = false;
        if(value != 4 || suspended) return;
        suspended = true;
        foreach(var handler in Suspended?.GetInvocationList() ?? []) { if(disposed) return; try { ((Action)handler)(); } catch { } }
    }
    public void Dispose()
    {
        var entered = Monitor.TryEnter(synchronization,TimeSpan.FromSeconds(2));
        try { disposed = true; PublishHealth(false, "Hotkey service disposed."); }
        finally { if(entered) Monitor.Exit(synchronization); }
        if(Thread.CurrentThread != thread) thread.Join(TimeSpan.FromSeconds(2));
    }
}
