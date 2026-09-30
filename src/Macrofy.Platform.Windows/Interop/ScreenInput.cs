using System.Runtime.InteropServices;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows.Interop;

namespace Macrofy.Platform.Windows;

internal readonly record struct ScreenBounds(int Left, int Top, int Width, int Height);
internal readonly record struct ScreenMouseInput(int X, int Y, uint Flags);
internal interface IScreenInputNative
{
    ScreenBounds ReadBounds();
    PointerPoint? ReadPointer();
    bool IsPointOnMonitor(PointerPoint point);
    bool IsLeftButtonHeld();
    int Send(ScreenMouseInput[] inputs);
}
internal sealed class Win32ScreenInputNative : IScreenInputNative, IScreenPlayerNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput
    { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct HardwareInput
    { public uint Message; public ushort LowParam, HighParam; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public HardwareInput Hardware; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input
    { public uint Type; public InputUnion Union; }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(WindowNative.Point point, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    public ScreenGeometry ReadGeometry() => WithPhysicalPixels(() => new ScreenGeometry(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)));
    public bool IsKeyHeld(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public int Send(ScreenNativeInput[] inputs) => WithPhysicalPixels(() => (int)SendInput((uint)inputs.Length, inputs.Select(Encode).ToArray(), Marshal.SizeOf<Input>()));
    internal static Input Encode(ScreenNativeInput input) => new()
    {
        Type = input.Type,
        Union = input.Type == 1
            ? new InputUnion { Keyboard = new KeyboardInput { VirtualKey = input.VirtualKey, ScanCode = input.ScanCode, Flags = input.Flags } }
            : new InputUnion { Mouse = new MouseInput { X = input.X, Y = input.Y, Data = input.Data, Flags = input.Flags } }
    };
    public ScreenBounds ReadBounds() => WithPhysicalPixels(() => new ScreenBounds(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)));
    public PointerPoint? ReadPointer() => WithPhysicalPixels<PointerPoint?>(() => WindowNative.GetCursorPos(out var point) ? new(point.X, point.Y) : null);
    public bool IsPointOnMonitor(PointerPoint point) => WithPhysicalPixels(() => MonitorFromPoint(new WindowNative.Point { X = (int)Math.Floor(point.X), Y = (int)Math.Floor(point.Y) }, 0) != 0);
    public bool IsLeftButtonHeld() => (GetAsyncKeyState(1) & 0x8000) != 0;
    public int Send(ScreenMouseInput[] inputs) => WithPhysicalPixels(() => (int)SendInput((uint)inputs.Length,
        inputs.Select(i => new Input { Type = 0, Union = new InputUnion { Mouse = new MouseInput { X = i.X, Y = i.Y, Flags = i.Flags } } }).ToArray(), Marshal.SizeOf<Input>()));

    private static T WithPhysicalPixels<T>(Func<T> action)
    {
        var previous = WindowNative.SetThreadDpiAwarenessContext(-4);
        try { return action(); }
        finally { if (previous != 0) WindowNative.SetThreadDpiAwarenessContext(previous); }
    }
}

