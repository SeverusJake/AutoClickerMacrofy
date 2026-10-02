namespace Macrofy.Platform.Models;

/// <summary>A mouse button or key intentionally kept down across gestures by one playback session.</summary>
public sealed record HeldInput(MouseButton? Button = null, KeyIdentity? Key = null);
