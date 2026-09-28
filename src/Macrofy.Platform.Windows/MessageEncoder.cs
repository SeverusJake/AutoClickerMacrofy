using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

internal static class MessageEncoder
{
    public static IReadOnlyList<NativeMessage> Encode(InputCommand command, NativeWindow target, HeldInputTracker held, IInputNative native)
    {
        switch (command)
        {
            case PointerCommand pointer:
                if (!Enum.IsDefined(pointer.Kind)) throw new ArgumentException("Unknown pointer action.");
                var g = target.Geometry ?? throw new ArgumentException("Target client geometry is unavailable.");
                if (!double.IsFinite(pointer.Point.X) || !double.IsFinite(pointer.Point.Y) || pointer.Point.X < 0 || pointer.Point.Y < 0 || pointer.Point.X >= g.Width || pointer.Point.Y >= g.Height)
                    throw new ArgumentException("Pointer point is outside the current target client area.");
                uint mask = held.PointerMask;
                uint id = 0x200;
                uint high = 0;
                if (pointer.Kind is PointerKind.Down or PointerKind.Up)
                {
                    if (pointer.Button is not { } button || !Enum.IsDefined(button)) throw new ArgumentException("Pointer down/up needs a valid button.");
                    var buttonMask = button switch { MouseButton.Left => 1u, MouseButton.Right => 2u, MouseButton.Middle => 16u, MouseButton.X1 => 32u, MouseButton.X2 => 64u, _ => 0u };
                    mask = pointer.Kind == PointerKind.Down ? mask | buttonMask : mask & ~buttonMask;
                    id = button switch { MouseButton.Left => 0x201u, MouseButton.Right => 0x204u, MouseButton.Middle => 0x207u, _ => 0x20Bu };
                    if (pointer.Kind == PointerKind.Up) id++;
                    if (button is MouseButton.X1 or MouseButton.X2) high = button == MouseButton.X1 ? 1u : 2u;
                }
                var point = pointer.Point;
                if (pointer.Kind is PointerKind.VerticalWheel or PointerKind.HorizontalWheel)
                {
                    if (pointer.WheelDelta == 0 || pointer.WheelDelta is < short.MinValue or > short.MaxValue) throw new ArgumentException("Wheel delta must be a nonzero signed 16-bit value.");
                    id = pointer.Kind == PointerKind.VerticalWheel ? 0x20Au : 0x20Eu;
                    high = unchecked((ushort)(short)pointer.WheelDelta);
                    point = native.ToScreen(target.SurfaceHandle, point) ?? throw new ArgumentException("Target screen-coordinate conversion failed.");
                }
                return [new(id, (nuint)(mask | (high << 16)), PackPoint(point))];
            case KeyCommand key:
                if (!Enum.IsDefined(key.Kind)) throw new ArgumentException("Unknown key action.");
                var vk = VirtualKey(key.Key.LogicalKey);
                var scan = key.Key.NativeScanCode ?? native.GetScanCode(target.SurfaceHandle, vk);
                if (scan < 0 || scan > 0xE1FF) throw new ArgumentException("Invalid native scan code.");
                bool extended = key.Key.IsExtended || (scan & 0xFF00) is 0xE000 or 0xE100;
                var isUp = key.Kind == KeyKind.Up;
                uint bits = 1u | ((uint)(scan & 0xFF) << 16) | (extended ? 1u << 24 : 0) |
                    (held.IsAltHeld ? 1u << 29 : 0) | (isUp || held.Keys.ContainsKey(vk) ? 1u << 30 : 0) | (isUp ? 1u << 31 : 0);
                bool system = held.IsAltHeld || vk is 0x12 or 0xA4 or 0xA5 or 0x79;
                return [new(system ? (isUp ? 0x105u : 0x104u) : (isUp ? 0x101u : 0x100u), (nuint)vk, unchecked((nint)(int)bits))];
            case TextCommand text:
                if (text.Text.Length > 4096) throw new ArgumentException("A single text action is limited to 4096 UTF-16 units.");
                for (var i = 0; i < text.Text.Length; i++)
                {
                    if (char.IsHighSurrogate(text.Text[i]))
                    {
                        if (++i >= text.Text.Length || !char.IsLowSurrogate(text.Text[i])) throw new ArgumentException("Text contains an unpaired surrogate.");
                    }
                    else if (char.IsLowSurrogate(text.Text[i])) throw new ArgumentException("Text contains an unpaired surrogate.");
                }
                return text.Text.Select(c => new NativeMessage(0x102, c, 1)).ToArray();
            default: throw new ArgumentException("Unsupported input action.");
        }
    }

    public static nint PackPoint(PointerPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X < short.MinValue || point.Y < short.MinValue || point.X >= short.MaxValue + 1d || point.Y >= short.MaxValue + 1d)
            throw new ArgumentException("Coordinates exceed the Windows message signed 16-bit range.");
        var x = (short)Math.Floor(point.X); var y = (short)Math.Floor(point.Y);
        return unchecked((nint)(int)((uint)(ushort)x | ((uint)(ushort)y << 16)));
    }

    public static int VirtualKey(string logical)
    {
        var key = logical.ToUpperInvariant();
        if (key.Length == 1 && (key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')) return key[0];
        if (key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1])) return key[1];
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24) return 0x70 + f - 1;
        return key switch
        {
            "SHIFT" => 0x10, "LEFTSHIFT" => 0xA0, "RIGHTSHIFT" => 0xA1,
            "CONTROL" or "CTRL" => 0x11, "LEFTCONTROL" or "LEFTCTRL" => 0xA2, "RIGHTCONTROL" or "RIGHTCTRL" => 0xA3,
            "ALT" => 0x12, "LEFTALT" => 0xA4, "RIGHTALT" => 0xA5,
            "ENTER" or "RETURN" => 0x0D, "ESCAPE" or "ESC" => 0x1B, "SPACE" => 0x20, "TAB" => 9,
            "BACKSPACE" => 8, "DELETE" => 0x2E, "INSERT" => 0x2D, "HOME" => 0x24, "END" => 0x23,
            "PAGEUP" => 0x21, "PAGEDOWN" => 0x22, "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "CAPSLOCK" => 0x14, "NUMLOCK" => 0x90, "SCROLLLOCK" => 0x91, "LEFTWIN" => 0x5B, "RIGHTWIN" => 0x5C,
            "OEMPLUS" => 0xBB, "OEMMINUS" => 0xBD, "OEMCOMMA" => 0xBC, "OEMPERIOD" => 0xBE,
            _ => throw new ArgumentException($"Unsupported logical key: {logical}. Remap it explicitly.")
        };
    }
}
