using System.Runtime.InteropServices;

namespace Macrofy.CompatibilityProbe;

internal sealed class ProbeStopHotkey : IDisposable
{
    private readonly Thread thread;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint threadId;
    public bool Registered { get; }
    public event Action? StopRequested;
    public ProbeStopHotkey()
    {
        thread = new(Run) { IsBackground = true, Name = "Macrofy probe F10" };
        thread.Start(); Registered = ready.Task.GetAwaiter().GetResult();
    }
    private void Run()
    {
        threadId = Native.GetCurrentThreadId(); Native.PeekMessage(out _, 0, 0, 0, 0);
        var registered = Native.RegisterHotKey(0, 1, 0x4000, 0x79); ready.SetResult(registered);
        try
        {
            while (Native.GetMessage(out var message, 0, 0, 0) > 0)
                if (message.Id == 0x312 && message.WParam == 1) { try { StopRequested?.Invoke(); } catch { } }
        }
        finally { if (registered) Native.UnregisterHotKey(0, 1); }
    }
    public void Dispose() { Native.PostThreadMessage(threadId, 0x12, 0, 0); thread.Join(TimeSpan.FromSeconds(2)); }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
        [DllImport("user32.dll", EntryPoint = "PeekMessageW")] internal static extern bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll", EntryPoint = "GetMessageW")] internal static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
        [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    }
}
