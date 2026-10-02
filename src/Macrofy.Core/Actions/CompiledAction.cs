using Macrofy.Platform.Models;
namespace Macrofy.Core.Actions;
public abstract record CompiledAction(int DelayMs)
{
    /// <summary>A complete click; HoldMs above zero keeps the button down that long before releasing.</summary>
    public sealed record Click(PointerPoint Point, CoordinateMode Mode, int DelayMs, MouseButton Button = MouseButton.Left, int HoldMs = 0) : CompiledAction(DelayMs);
    public sealed record Key : CompiledAction
    {
        public IReadOnlyList<KeyIdentity> Keys { get; }
        public int HoldMs { get; }
        public Key(IReadOnlyList<KeyIdentity> keys, int delayMs, int holdMs = 0) : base(delayMs)
        { Keys = Array.AsReadOnly(keys.ToArray()); HoldMs = holdMs; }
    }
    public sealed record Text(string Content, int DelayMs) : CompiledAction(DelayMs);
    public sealed record Wait(int DurationMs, int DelayMs) : CompiledAction(DelayMs);
    public sealed record Wheel(int Delta, int DelayMs) : CompiledAction(DelayMs);
    /// <summary>Presses a button and keeps it held until a matching release or the end of the session.</summary>
    public sealed record MouseDown(PointerPoint Point, CoordinateMode Mode, MouseButton Button, int DelayMs) : CompiledAction(DelayMs);
    public sealed record MouseUp(PointerPoint Point, CoordinateMode Mode, MouseButton Button, int DelayMs) : CompiledAction(DelayMs);
    public sealed record KeyDown : CompiledAction
    {
        public IReadOnlyList<KeyIdentity> Keys { get; }
        public KeyDown(IReadOnlyList<KeyIdentity> keys, int delayMs) : base(delayMs) => Keys = Array.AsReadOnly(keys.ToArray());
    }
    public sealed record KeyUp : CompiledAction
    {
        public IReadOnlyList<KeyIdentity> Keys { get; }
        public KeyUp(IReadOnlyList<KeyIdentity> keys, int delayMs) : base(delayMs) => Keys = Array.AsReadOnly(keys.ToArray());
    }
}
