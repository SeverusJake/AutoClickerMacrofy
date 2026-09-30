namespace Macrofy.Platform.Models;

public readonly record struct ScreenGeometry(int Left, int Top, int Width, int Height);
public sealed record ScreenPointResult(PointerPoint? Point, PlatformError? Error = null);
