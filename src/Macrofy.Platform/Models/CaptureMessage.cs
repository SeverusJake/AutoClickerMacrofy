namespace Macrofy.Platform.Models;

public enum CaptureKind { Input, FocusLost, FocusGained, TargetLost }
public sealed record CaptureMessage(CaptureKind Kind, TimeSpan Timestamp, InputCommand? Input = null, bool IsInjected = false);
