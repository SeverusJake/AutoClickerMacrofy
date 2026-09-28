using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Macrofy.TestTarget;

internal static class Program
{
    private static readonly ConcurrentQueue<Receipt> receipts = new();
    private static readonly ConcurrentQueue<(string Command, TaskCompletionSource Completion)> commands = new();
    private static readonly Native.WndProc callback = WindowProcedure;
    private static string pipeName = "";
    private static nint first, second;
    private static readonly CancellationTokenSource shutdown = new();
    private sealed record Receipt(uint Message, ulong WParam, long LParam);

    public static void Main(string[] args)
    {
        pipeName = args.Length == 2 && args[0] == "--pipe" ? args[1] : "MacrofyTest-" + Guid.NewGuid().ToString("N");
        Native.SetThreadDpiAwarenessContext(-4);
        var cls = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = callback, ClassName = "MacrofyControlledSurface", Instance = Native.GetModuleHandle(null) };
        if (Native.RegisterClassEx(ref cls) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        first = Create(" A"); second = Create(" B");
        var server = Task.Run(ServeAsync);
        while (Native.GetMessage(out var message, 0, 0, 0) > 0) { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
        shutdown.Cancel();
        Native.DestroyWindow(first); Native.DestroyWindow(second);
        try { server.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        GC.KeepAlive(callback);
    }

    private static nint Create(string suffix)
    {
        // Offscreen, non-activating fixtures cannot steal focus or receive physical pointer input.
        var top = Native.CreateWindowEx(0x08000080, "MacrofyControlledSurface", pipeName + suffix, 0x00CF0000, -10000, 0, 640, 480, 0, 0, Native.GetModuleHandle(null), 0);
        if (top == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var child = Native.CreateWindowEx(0, "MacrofyControlledSurface", "input surface", 0x50000000, 0, 0, 500, 350, top, 0, Native.GetModuleHandle(null), 0);
        if (child == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        Native.ShowWindow(top, 4);
        return top;
    }

    private static nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (message is >= 0x100 and <= 0x109 or >= 0x200 and <= 0x20E)
        {
            receipts.Enqueue(new(message, (ulong)wParam, (long)lParam));
            return 0;
        }
        if (message == 0x8001)
        {
            while (commands.TryDequeue(out var item))
            {
                try
                {
                    switch (item.Command)
                    {
                        case "minimize": Native.ShowWindow(first, 7); break;
                        case "restore": Native.ShowWindow(first, 4); break;
                        case "recreate": Native.DestroyWindow(first); first = Create(" A"); break;
                        case "clear": while (receipts.TryDequeue(out _)) { } break;
                        case "stall": Thread.Sleep(500); break;
                        case "quit": Native.PostQuitMessage(0); break;
                        default: throw new ArgumentException("Unknown fixture command.");
                    }
                    item.Completion.SetResult();
                }
                catch (Exception e) { item.Completion.SetException(e); }
            }
            return 0;
        }
        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static async Task ServeAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(shutdown.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var command = await reader.ReadLineAsync(shutdown.Token);
            if (command != "snapshot")
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                commands.Enqueue((command ?? "", completion));
                Native.PostMessage(first, 0x8001, 0, 0);
                await completion.Task.WaitAsync(shutdown.Token);
            }
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { Events = receipts.ToArray() }));
        }
    }
}

internal static class Native
{
    internal delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass
    {
        public uint Size, Style; public WndProc Procedure; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background; public string? MenuName; public string ClassName; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Message
    {
        public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint extended, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
}
