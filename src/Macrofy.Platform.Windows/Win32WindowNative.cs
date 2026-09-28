using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows.Interop;

namespace Macrofy.Platform.Windows;

internal sealed class Win32WindowNative : IWindowNative
{
    private readonly WindowLifetimeMonitor monitor;
    public int CurrentProcessId => Environment.ProcessId;
    public event Action<nint>? Destroyed;
    public Win32WindowNative() { monitor = new(); monitor.Destroyed += Forward; }
    private void Forward(nint handle) => Destroyed?.Invoke(handle);

    public IReadOnlyList<NativeWindow> EnumerateWindows()
    {
        var windows = new List<NativeWindow>();
        WindowNative.EnumWindows((top, _) =>
        {
            if (!WindowNative.IsWindowVisible(top)) return true;
            var surface = FindSurface(top);
            if (ReadWindow(top, surface) is { } window && !string.IsNullOrWhiteSpace(window.Title)) windows.Add(window);
            return true;
        }, 0);
        return windows;
    }

    private static nint FindSurface(nint top)
    {
        nint best = top;
        long bestArea = 0;
        WindowNative.GetWindowThreadProcessId(top, out var pid);
        WindowNative.EnumChildWindows(top, (child, _) =>
        {
            WindowNative.GetWindowThreadProcessId(child, out var childPid);
            if (pid == childPid && WindowNative.IsWindowVisible(child) && WindowNative.GetClientRect(child, out var r))
            {
                var area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea) { best = child; bestArea = area; }
            }
            return true;
        }, 0);
        return best;
    }

    public NativeWindow? ReadWindow(nint top, nint surface)
    {
        if (!WindowNative.IsWindow(top) || !WindowNative.IsWindow(surface)) return null;
        WindowNative.GetWindowThreadProcessId(top, out var pid);
        WindowNative.GetWindowThreadProcessId(surface, out var surfacePid);
        if (pid == 0 || surfacePid != pid) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var path = process.MainModule?.FileName;
            if (path is null) return null;
            var title = new StringBuilder(4096); WindowNative.GetWindowText(top, title, title.Capacity);
            var cls = new StringBuilder(512); WindowNative.GetClassName(surface, cls, cls.Capacity);
            var minimized = WindowNative.IsIconic(top);
            ClientGeometry? geometry = null;
            var previous = WindowNative.SetThreadDpiAwarenessContext(-4);
            try
            {
                if (!minimized && WindowNative.GetClientRect(surface, out var r) && r.Right > r.Left && r.Bottom > r.Top)
                {
                    var dpi = WindowNative.GetDpiForWindow(surface);
                    geometry = new(r.Right - r.Left, r.Bottom - r.Top, dpi > 0 ? dpi / 96.0 : 1);
                }
            }
            finally { if (previous != 0) WindowNative.SetThreadDpiAwarenessContext(previous); }
            var file = new FileInfo(path);
            var identity = $"{file.Length}:{file.LastWriteTimeUtc.Ticks}:{FileVersionInfo.GetVersionInfo(path).FileVersion}";
            return new(top, surface, (int)pid, process.StartTime.ToUniversalTime().Ticks, path, title.ToString(), cls.ToString(), geometry, minimized, WindowNative.GetForegroundWindow() == top, identity);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }
    public void Dispose() { monitor.Destroyed -= Forward; monitor.Dispose(); }
}

internal sealed class WindowLifetimeMonitor : IDisposable
{
    private readonly Thread thread;
    private readonly WindowNative.WinEventProc callback;
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint threadId;
    private int disposed;
    public event Action<nint>? Destroyed;
    public WindowLifetimeMonitor()
    {
        callback = (_, _, hwnd, objectId, childId, _, _) =>
        {
            // Never let a managed subscriber exception cross the native callback boundary.
            if (objectId == 0 && childId == 0) { try { Destroyed?.Invoke(hwnd); } catch { } }
        };
        thread = new Thread(Run) { IsBackground = true, Name = "Macrofy window lifetimes" };
        thread.Start();
        started.Task.GetAwaiter().GetResult();
    }
    private void Run()
    {
        threadId = WindowNative.GetCurrentThreadId();
        WindowNative.PeekMessage(out _, 0, 0, 0, 0);
        var hook = WindowNative.SetWinEventHook(0x8001, 0x8001, 0, callback, 0, 0, 0);
        if (hook == 0) { started.SetException(new Win32Exception(Marshal.GetLastWin32Error())); return; }
        started.SetResult();
        try
        {
            while (WindowNative.GetMessage(out var message, 0, 0, 0) > 0)
            {
                WindowNative.TranslateMessage(ref message); WindowNative.DispatchMessage(ref message);
            }
        }
        finally { WindowNative.UnhookWinEvent(hook); GC.KeepAlive(callback); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        WindowNative.PostThreadMessage(threadId, 0x12, 0, 0);
        if (Thread.CurrentThread != thread) thread.Join(TimeSpan.FromSeconds(2));
    }
}
