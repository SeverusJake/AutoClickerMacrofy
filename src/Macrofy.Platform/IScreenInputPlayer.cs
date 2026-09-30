using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface IScreenInputPlayer
{
    ScreenGeometry ReadGeometry();
    ScreenPointResult ReadPointer();
    ValueTask<DeliveryResult> SendAsync(InputCommand command, CancellationToken cancellationToken = default);
    ValueTask<DeliveryResult> ReleaseHeldAsync(CancellationToken cancellationToken = default);
}
