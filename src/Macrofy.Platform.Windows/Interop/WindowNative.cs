using System.Runtime.InteropServices;
using System.Text;

namespace Macrofy.Platform.Windows.Interop;

internal static class WindowNative
{
    internal delegate bool EnumProc(nint hwnd, nint data);
    internal delegate void WinEventProc(nint hook, uint evt, nint hwnd, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumProc callback, nint data);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", EntryPoint="GetMessageW")] internal static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll", EntryPoint="PeekMessageW")] internal static extern bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint="DispatchMessageW")] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint="PostThreadMessageW", SetLastError = true)] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
}
