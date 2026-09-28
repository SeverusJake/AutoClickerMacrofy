namespace Macrofy.Platform.Models;

public enum HotkeyCommand { RecordToggle, PlayToggle, Stop }
public sealed record HotkeyBinding(string Key, bool Control = false, bool Alt = false, bool Shift = false);
public sealed record HotkeySet(HotkeyBinding Record, HotkeyBinding Play, HotkeyBinding Stop);
public sealed record HotkeyRegistrationResult(bool Registered, PlatformError? Error = null);
