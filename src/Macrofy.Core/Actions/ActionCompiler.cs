using System.Globalization;
using Macrofy.Platform.Models;
namespace Macrofy.Core.Actions;
public static class ActionCompiler
{
    private static readonly IReadOnlySet<string> NoReservedKeys = new HashSet<string>();
    public static bool TryValidateEditor(ActionDefinition source, out string error) => TryCompile(source, CoordinateMode.FixedPixels, NoReservedKeys, out _, out error);
    public static bool TryCompile(ActionDefinition source, CoordinateMode coordinates, IReadOnlySet<string> reservedKeys, out CompiledAction? action, out string error)
    {
        action = null; error = "";
        if (source.DelayMs is < 0 or > 600000) error = "Wait after must be 0–600000 ms.";
        else if (source.Value is null) error = "Enter a value.";
        else if (!Enum.IsDefined(coordinates)) error = "Unknown coordinate mode.";
        else switch (source.Kind)
        {
            case "Click":
                var parts = source.Value.Split(',');
                if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(x) || !double.IsFinite(y)) error = "Enter finite X, Y coordinates (for example 480, 640).";
                else if (coordinates == CoordinateMode.Percentage && (x is < 0 or > 100 || y is < 0 or > 100)) error = "Percentage coordinates must be 0–100.";
                else action = new CompiledAction.Click(new PointerPoint(x, y), coordinates, source.DelayMs);
                break;
            case "Key":
                if (!KeyParser.TryParse(source.Value, out var keys, out error)) break;
                if (keys.Any(key => reservedKeys.Any(reserved => string.Equals(reserved, key.LogicalKey, StringComparison.OrdinalIgnoreCase)))) error = "Key chord contains a reserved control hotkey.";
                else action = new CompiledAction.Key(keys, source.DelayMs);
                break;
            case "Text":
                if (source.Value.Length > 4096) error = "Text is limited to 4096 UTF-16 units.";
                else
                {
                    for (var i = 0; i < source.Value.Length; i++)
                    {
                        if (char.IsHighSurrogate(source.Value[i])) { if (++i >= source.Value.Length || !char.IsLowSurrogate(source.Value[i])) { error = "Text contains an unpaired surrogate."; break; } }
                        else if (char.IsLowSurrogate(source.Value[i])) { error = "Text contains an unpaired surrogate."; break; }
                    }
                    if (error.Length == 0) action = new CompiledAction.Text(source.Value, source.DelayMs);
                }
                break;
            case "Wait":
                if (!int.TryParse(source.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration) || duration is < 0 or > 600000) error = "Wait must be 0–600000 ms.";
                else action = new CompiledAction.Wait(duration, source.DelayMs);
                break;
            case "Wheel":
                if (!int.TryParse(source.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta) || delta == 0 || delta is < short.MinValue or > short.MaxValue) error = "Wheel delta must be a nonzero signed 16-bit value.";
                else action = new CompiledAction.Wheel(delta, source.DelayMs);
                break;
            default: error = "Unknown action type."; break;
        }
        return action is not null;
    }
}
