namespace Macrofy.Core.Actions;
public sealed record ActionDefinition(string Kind, string Value, int DelayMs);
public enum CoordinateMode { FixedPixels, Percentage }
