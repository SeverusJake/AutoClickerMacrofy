using Macrofy.Platform.Models;
namespace Macrofy.Core.Actions;
public abstract record CompiledAction(int DelayMs)
{
    public sealed record Click(PointerPoint Point, CoordinateMode Mode, int DelayMs) : CompiledAction(DelayMs);
    public sealed record Key : CompiledAction
    {
        public IReadOnlyList<KeyIdentity> Keys { get; }
        public Key(IReadOnlyList<KeyIdentity> keys, int delayMs) : base(delayMs) =>
            Keys = Array.AsReadOnly(keys.ToArray());
    }
    public sealed record Text(string Content, int DelayMs) : CompiledAction(DelayMs);
    public sealed record Wait(int DurationMs, int DelayMs) : CompiledAction(DelayMs);
    public sealed record Wheel(int Delta, int DelayMs) : CompiledAction(DelayMs);
}

