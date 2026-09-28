namespace Macrofy.Platform.Models;

public sealed record PermissionResult(bool Allowed, PlatformError? Error = null);
