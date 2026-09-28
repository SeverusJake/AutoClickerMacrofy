using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface IWindowCatalog
{
    Task<IReadOnlyList<TargetWindow>> ListAsync(CancellationToken cancellationToken = default);
    Task<ResolutionResult> ResolveAsync(TargetRule rule, TargetToken? selected = null, CancellationToken cancellationToken = default);
    event Action<TargetToken>? TargetLost;
}
