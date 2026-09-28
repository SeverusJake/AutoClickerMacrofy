using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface ITargetContext
{
    ValueTask<TargetContextResult> GetAsync(TargetToken target, CancellationToken cancellationToken = default);
}
