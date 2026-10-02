namespace Macrofy.Core.Actions;
/// <summary>Editor step source. Button applies to Click and Mouse down/up; HoldMs applies to Click and Key.</summary>
public sealed record ActionDefinition(string Kind, string Value, int DelayMs, string? Button = "Left", int HoldMs = 0);
public enum CoordinateMode { FixedPixels, Percentage }
