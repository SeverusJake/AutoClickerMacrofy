using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface IInputPlayer
{
    ValueTask<DeliveryResult> SendAsync(TargetToken target, InputCommand command, CancellationToken cancellationToken = default);
    ValueTask<DeliveryResult> ReleaseHeldAsync(TargetToken target, CancellationToken cancellationToken = default);
    /// <summary>Keeps a held input down through release-all cleanup until unpinned.</summary>
    void Pin(TargetToken target, HeldInput input) { }
    void Unpin(TargetToken target, HeldInput input) { }
}
