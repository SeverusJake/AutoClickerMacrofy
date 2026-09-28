namespace Macrofy.Platform.Models;

public sealed record AppIdentity(string Name, string? ExecutablePath = null, string? BundleId = null);
