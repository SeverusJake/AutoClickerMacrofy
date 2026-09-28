using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface IInputPlayer
{
    ValueTask<DeliveryResult> SendAsync(TargetToken target, InputCommand command, CancellationToken cancellationToken = default);
    ValueTask<DeliveryResult> ReleaseHeldAsync(TargetToken target, CancellationToken cancellationToken = default);
}
