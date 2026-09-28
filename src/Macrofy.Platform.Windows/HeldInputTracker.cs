using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

internal sealed class HeldInputTracker
{
    public Dictionary<int, KeyIdentity> Keys { get; } = [];
    public HashSet<MouseButton> Buttons { get; } = [];
    public PointerPoint LastPoint { get; private set; }
    public bool IsAltHeld => Keys.Keys.Any(k => k is 0x12 or 0xA4 or 0xA5);
    public uint PointerMask => (Buttons.Contains(MouseButton.Left) ? 1u : 0) |
        (Buttons.Contains(MouseButton.Right) ? 2u : 0) | (Buttons.Contains(MouseButton.Middle) ? 16u : 0) |
        (Buttons.Contains(MouseButton.X1) ? 32u : 0) | (Buttons.Contains(MouseButton.X2) ? 64u : 0) |
        (Keys.Keys.Any(k => k is 0x10 or 0xA0 or 0xA1) ? 4u : 0) |
        (Keys.Keys.Any(k => k is 0x11 or 0xA2 or 0xA3) ? 8u : 0);

    public void Apply(InputCommand command)
    {
        if (command is KeyCommand key)
        {
            var vk = MessageEncoder.VirtualKey(key.Key.LogicalKey);
            if (key.Kind == KeyKind.Down) Keys[vk] = key.Key; else Keys.Remove(vk);
        }
        if (command is PointerCommand pointer)
        {
            LastPoint = pointer.Point;
            if (pointer.Kind == PointerKind.Down) Buttons.Add(pointer.Button!.Value);
            if (pointer.Kind == PointerKind.Up) Buttons.Remove(pointer.Button!.Value);
        }
    }
}
