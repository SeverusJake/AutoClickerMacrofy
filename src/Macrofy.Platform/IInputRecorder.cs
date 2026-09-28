using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface IInputRecorder
{
    Task StartAsync(TargetToken target, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    event Action<CaptureMessage>? Captured;
}
