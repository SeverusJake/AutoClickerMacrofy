using Macrofy.Platform.Models;

namespace Macrofy.Platform;

public interface IGlobalHotkeys
{
    HotkeyRegistrationResult Configure(HotkeySet hotkeys);
    event Action<HotkeyCommand>? Triggered;
}
