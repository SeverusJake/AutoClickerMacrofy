using Macrofy.Platform.Models;
namespace Macrofy.Core.Actions;
public static class KeyParser
{
    private static readonly Dictionary<string, string> Vocabulary = BuildVocabulary();
    public static bool TryParse(string value, out IReadOnlyList<KeyIdentity> keys, out string error)
    {
        keys = Array.Empty<KeyIdentity>(); error = "";
        if (string.IsNullOrWhiteSpace(value)) { error = "Enter a key."; return false; }
        var tokens = value.Split('+'); var parsed = new List<KeyIdentity>(); var modifiers = new HashSet<string>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim();
            if (token.Length == 0) { error = "Key chord contains an empty component."; return false; }
            if (!Vocabulary.TryGetValue(token, out var canonical)) { error = $"Unsupported logical key: {token}."; return false; }
            var family = ModifierFamily(canonical);
            if (family is not null)
            {
                if (!modifiers.Add(family)) { error = $"Duplicate modifier: {canonical}."; return false; }
                // A modifier alone is one valid key (e.g. Key down Shift); in a chord it needs a final key.
                if (i == tokens.Length - 1 && tokens.Length > 1) { error = "Key chord requires a final nonmodifier key."; return false; }
            }
            else if (i != tokens.Length - 1) { error = "Nonmodifier key must be last in the chord."; return false; }
            parsed.Add(new(canonical));
        }
        keys = parsed.AsReadOnly(); return true;
    }
    private static string? ModifierFamily(string key) => key switch
    {
        "Control" or "LeftControl" or "RightControl" => "Control",
        "Shift" or "LeftShift" or "RightShift" => "Shift",
        "Alt" or "LeftAlt" or "RightAlt" => "Alt",
        "LeftWin" or "RightWin" => "Win", _ => null
    };
    private static Dictionary<string, string> BuildVocabulary()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Shift", "LeftShift", "RightShift", "Control", "LeftControl", "RightControl", "Alt", "LeftAlt", "RightAlt", "Enter", "Escape", "Space", "Tab", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Left", "Up", "Right", "Down", "CapsLock", "NumLock", "ScrollLock", "LeftWin", "RightWin", "OemPlus", "OemMinus", "OemComma", "OemPeriod" }) result[name] = name;
        foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789") result[c.ToString()] = c.ToString();
        for (var i = 0; i <= 9; i++) result[$"D{i}"] = i.ToString();
        for (var i = 1; i <= 24; i++) result[$"F{i}"] = $"F{i}";
        result["Ctrl"] = "Control"; result["LeftCtrl"] = "LeftControl"; result["RightCtrl"] = "RightControl";
        result["Return"] = "Enter"; result["Esc"] = "Escape";
        return result;
    }
}
