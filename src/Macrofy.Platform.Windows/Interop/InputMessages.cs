using System.Runtime.InteropServices;
using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

internal readonly record struct NativeMessage(uint Id, nuint WParam, nint LParam);
internal readonly record struct NativeSendResult(bool Queued, int Error);
internal interface IInputNative
{
    NativeSendResult Post(nint target, NativeMessage message);
    PointerPoint? ToScreen(nint target, PointerPoint point);
    int GetScanCode(nint target, int virtualKey);
}

internal sealed class Win32InputNative : IInputNative
{
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)] private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")] private static extern uint MapVirtualKeyEx(uint code, uint kind, nint layout);
    public NativeSendResult Post(nint target, NativeMessage message)
    {
        var previous = Interop.WindowNative.SetThreadDpiAwarenessContext(-4);
        try
        {
            var ok = PostMessage(target, message.Id, message.WParam, message.LParam);
            return new(ok, ok ? 0 : Marshal.GetLastWin32Error());
        }
        finally { if (previous != 0) Interop.WindowNative.SetThreadDpiAwarenessContext(previous); }
    }
    public PointerPoint? ToScreen(nint target, PointerPoint point)
    {
        var previous = Interop.WindowNative.SetThreadDpiAwarenessContext(-4);
        try
        {
            var p = new Interop.WindowNative.Point { X = (int)Math.Floor(point.X), Y = (int)Math.Floor(point.Y) };
            return Interop.WindowNative.ClientToScreen(target, ref p) ? new(p.X, p.Y) : null;
        }
        finally { if (previous != 0) Interop.WindowNative.SetThreadDpiAwarenessContext(previous); }
    }
    public int GetScanCode(nint target, int virtualKey)
    {
        var thread = Interop.WindowNative.GetWindowThreadProcessId(target, out _);
        return (int)MapVirtualKeyEx((uint)virtualKey, 4, GetKeyboardLayout(thread));
    }
}
