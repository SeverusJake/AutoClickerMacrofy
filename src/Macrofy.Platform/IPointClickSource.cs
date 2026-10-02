using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface IPointClickSource
{
    /// <summary>Listens for one left click outside this app; that click is not delivered. Returns null on success.</summary>
    PlatformError? Start(Action clicked);
    void Cancel();
}
