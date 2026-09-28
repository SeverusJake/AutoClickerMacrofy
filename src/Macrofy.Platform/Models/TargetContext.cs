namespace Macrofy.Platform.Models;

public sealed record TargetContext(TargetWindow Window, bool IsForeground, string SurfaceFingerprint);
public sealed record TargetContextResult(TargetContext? Context, PlatformError? Error = null);
