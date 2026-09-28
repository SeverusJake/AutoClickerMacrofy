namespace Macrofy.Platform.Models;

public sealed record TargetWindow(TargetToken Token, AppIdentity App, string Title, bool IsMinimized, ClientGeometry? Geometry);
