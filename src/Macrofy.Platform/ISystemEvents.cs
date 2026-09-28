using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface ISystemEvents
{
    event Action? Suspended;
}
