using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface IPermissionService
{
    Task<PermissionResult> CheckAsync(TargetToken target, CancellationToken cancellationToken = default);
}
