namespace Macrofy.Platform.Models;

public sealed record KeyIdentity(string LogicalKey, int? NativeScanCode = null, bool IsExtended = false);
