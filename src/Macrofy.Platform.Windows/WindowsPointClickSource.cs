using System.Runtime.InteropServices;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows.Interop;

namespace Macrofy.Platform.Windows;

internal enum ClickDecision { Pass, Record, Swallow }

/// <summary>Decides per low-level mouse message: record and swallow one left click outside this app, pass everything else.</summary>
internal sealed class ClickFilter(Func<int, int, bool> ownsPoint)
{
    private bool swallowUp;
    public bool Finished { get; private set; }
    public ClickDecision Handle(uint message, int x, int y)
    {
        if (Finished) return ClickDecision.Pass;
        if (message == 0x201 && !ownsPoint(x, y)) { swallowUp = true; return ClickDecision.Record; }
        if (message == 0x202 && swallowUp) { swallowUp = false; Finished = true; return ClickDecision.Swallow; }
        return ClickDecision.Pass;
    }
}

/// <summary>Temporary WH_MOUSE_LL hook on its own message-loop thread, installed only while listening for one click.</summary>
public sealed class WindowsPointClickSource : IPointClickSource, IDisposable
{
    private delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public WindowNative.Point Point; public uint Data, Flags, Time; public nuint Extra; }
    private readonly object sync = new();
    private uint threadId;
    private Thread? thread;

    public PlatformError? Start(Action clicked)
    {
        lock (sync)
        {
            if (thread is not null) return new("RecordingBusy", "A point is already being recorded.");
            var ready = new TaskCompletionSource<PlatformError?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = (uint)Environment.ProcessId;
            var filter = new ClickFilter((x, y) =>
            {
                var root = GetAncestor(WindowFromPoint(new WindowNative.Point { X = x, Y = y }), 2);
                return root != 0 && WindowNative.GetWindowThreadProcessId(root, out var process) != 0 && process == current;
            });
            thread = new Thread(() => Run(filter, clicked, ready)) { IsBackground = true, Name = "Macrofy point recorder" };
            thread.Start();
            var error = ready.Task.GetAwaiter().GetResult();
            if (error is not null) thread = null;
            return error;
        }
    }

    private void Run(ClickFilter filter, Action clicked, TaskCompletionSource<PlatformError?> ready)
    {
        HookProc proc = (code, wParam, lParam) =>
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<MouseData>(lParam);
                switch (filter.Handle((uint)wParam, data.Point.X, data.Point.Y))
                {
                    // Hook callbacks must return quickly; the app reads the pointer on its own thread.
                    case ClickDecision.Record: ThreadPool.QueueUserWorkItem(_ => clicked()); return 1;
                    case ClickDecision.Swallow: PostQuitMessage(0); return 1;
                }
            }
            return CallNextHookEx(0, code, wParam, lParam);
        };
        threadId = WindowNative.GetCurrentThreadId();
        var hook = SetWindowsHookEx(14, proc, GetModuleHandle(null), 0);
        if (hook == 0) { ready.SetResult(new("HookFailed", $"Mouse hook failed, Win32 error {Marshal.GetLastWin32Error()}.")); return; }
        ready.SetResult(null);
        try { while (GetMessage(out _, 0, 0, 0) > 0) { } }
        finally
        {
            UnhookWindowsHookEx(hook); GC.KeepAlive(proc);
            lock (sync) { if (thread == Thread.CurrentThread) thread = null; }
        }
    }

    public void Cancel() { lock (sync) { if (thread is not null) PostThreadMessage(threadId, 0x12, 0, 0); } }
    public void Dispose() => Cancel();

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out WindowNative.Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(WindowNative.Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
