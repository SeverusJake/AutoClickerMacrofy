namespace Macrofy.Platform.Models;

public sealed record DeliveryResult(bool Queued, PlatformError? Error = null);
