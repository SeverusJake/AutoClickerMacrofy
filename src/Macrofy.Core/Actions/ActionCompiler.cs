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
            case "Click" or "Mouse down" or "Mouse up":
                if (!TryPoint(source.Value, coordinates, out var point, out error) || !TryButton(source.Button, out var button, out error)) break;
                if (source.Kind == "Click" && !TryHold(source.HoldMs, out error)) break;
                action = source.Kind switch
                {
                    "Click" => new CompiledAction.Click(point, coordinates, source.DelayMs, button, source.HoldMs),
                    "Mouse down" => new CompiledAction.MouseDown(point, coordinates, button, source.DelayMs),
                    _ => new CompiledAction.MouseUp(point, coordinates, button, source.DelayMs)
                };
                break;
            case "Key" or "Combo key" or "Key down" or "Key up":
                if (!KeyParser.TryParse(source.Value, out var keys, out error)) break;
                if (keys.Any(key => reservedKeys.Any(reserved => string.Equals(reserved, key.LogicalKey, StringComparison.OrdinalIgnoreCase)))) { error = "Key chord contains a reserved control hotkey."; break; }
                if (source.Kind == "Combo key" ? keys.Count < 2 : keys.Count > 1)
                { error = source.Kind == "Combo key" ? "Combo key needs at least one modifier plus one key." : $"{source.Kind} takes one key. Use Combo key for combinations."; break; }
                if (source.Kind is "Key" or "Combo key" && !TryHold(source.HoldMs, out error)) break;
                action = source.Kind switch
                {
                    "Key" or "Combo key" => new CompiledAction.Key(keys, source.DelayMs, source.HoldMs),
                    "Key down" => new CompiledAction.KeyDown(keys, source.DelayMs),
                    _ => new CompiledAction.KeyUp(keys, source.DelayMs)
                };
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

    private static bool TryPoint(string value, CoordinateMode coordinates, out PointerPoint point, out string error)
    {
        point = default; error = "";
        var parts = value.Split(',');
        if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(x) || !double.IsFinite(y))
        { error = "Enter finite X, Y coordinates (for example 480, 640)."; return false; }
        if (coordinates == CoordinateMode.Percentage && (x is < 0 or > 100 || y is < 0 or > 100)) { error = "Percentage coordinates must be 0–100."; return false; }
        point = new(x, y); return true;
    }

    private static bool TryButton(string? text, out MouseButton button, out string error)
    {
        error = "";
        button = text switch { "Left" or null => MouseButton.Left, "Right" => MouseButton.Right, "Middle" => MouseButton.Middle, _ => (MouseButton)(-1) };
        if (Enum.IsDefined(button)) return true;
        error = "Button must be Left, Right or Middle."; return false;
    }

    private static bool TryHold(int holdMs, out string error)
    {
        error = holdMs is < 0 or > 600000 ? "Hold must be 0–600000 ms." : "";
        return error.Length == 0;
    }
}
