using Macrofy.Platform.Models;
namespace Macrofy.Platform;

public interface IHotkeyHealth
{
    bool IsOperational { get; }
    PlatformError? OperationalError { get; }
    event Action? HealthChanged;
}
