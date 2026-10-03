using Avalonia.Controls;
using Avalonia.Input;
using Macrofy.Core.Actions;

namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    /// <summary>Keys offered by the Key dropdown; every name is accepted by <see cref="KeyParser"/>.</summary>
    private static readonly string[] SingleKeys =
    [
        .. "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(c => c.ToString()), .. Enumerable.Range(1, 24).Select(n => "F" + n),
        "Space", "Enter", "Escape", "Tab", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
        "Up", "Down", "Left", "Right", "Shift", "Ctrl", "Alt", "LeftWin", "RightWin", "CapsLock", "NumLock", "ScrollLock",
        "OemPlus", "OemMinus", "OemComma", "OemPeriod"
    ];

    private sealed record KeyRecording(bool Combo, Action<string> Captured, Action<string> Report);
    private KeyRecording? keyRecording;

    /// <summary>Record key / Record combo button: listens to the next key press in this window.</summary>
    private Button RecordKeyButton(string name, string label, bool combo, Func<bool> available, Action<string> captured, Action<string> report)
    {
        var button = TextButton(label, () => { }); button.Name = name;
        KeyRecording? mine = null;
        bool Active() => mine is not null && ReferenceEquals(keyRecording, mine);
        bool CanStart() => keyRecording is null && recording is null && available();
        void Update() { button.Content = Active() ? "Cancel" : label; button.IsEnabled = Active() || CanStart(); }
        button.Click += (_, _) =>
        {
            if (Active()) { keyRecording = null; report(""); }
            else if (CanStart())
            {
                mine = new(combo, captured, report); keyRecording = mine;
                report(combo ? "Hold modifiers and press a key." : "Press a key.");
            }
            RefreshPlayback();
        };
        refreshPlayback.Add(Update); Update(); return button;
    }

    /// <summary>Consumes a key press while recording. Combo recording waits through modifier presses.</summary>
    private bool HandleKeyRecording(Key key, KeyModifiers modifiers)
    {
        if (keyRecording is not { } current) return false;
        var name = KeyName(key);
        if (name is null) { current.Report("That key isn't supported."); return true; }
        if (IsReservedKey(name)) { current.Report($"{name} is reserved for Macrofy."); return true; }
        var modifier = name is "Shift" or "Ctrl" or "Alt" or "LeftWin" or "RightWin";
        string value;
        if (current.Combo)
        {
            if (modifier) return true;
            var held = new List<string>();
            if (modifiers.HasFlag(KeyModifiers.Control)) held.Add("Ctrl");
            if (modifiers.HasFlag(KeyModifiers.Shift)) held.Add("Shift");
            if (modifiers.HasFlag(KeyModifiers.Alt)) held.Add("Alt");
            if (modifiers.HasFlag(KeyModifiers.Meta)) held.Add("LeftWin");
            if (held.Count == 0) { current.Report("Hold Ctrl, Shift or Alt, then press a key."); return true; }
            value = string.Join(" + ", held.Append(name));
        }
        else value = name;
        keyRecording = null;
        current.Captured(value);
        RefreshPlayback();
        return true;
    }

    private bool IsReservedKey(string name) =>
        new[] { Workspace.Document.Shortcuts.Run, Workspace.Document.Shortcuts.Pause, Workspace.Document.Shortcuts.Stop }.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Space => "Space", Key.Enter => "Enter", Key.Escape => "Escape", Key.Tab => "Tab", Key.Back => "Backspace",
        Key.Delete => "Delete", Key.Insert => "Insert", Key.Home => "Home", Key.End => "End", Key.PageUp => "PageUp", Key.PageDown => "PageDown",
        Key.Up => "Up", Key.Down => "Down", Key.Left => "Left", Key.Right => "Right",
        Key.LeftShift or Key.RightShift => "Shift", Key.LeftCtrl or Key.RightCtrl => "Ctrl", Key.LeftAlt or Key.RightAlt => "Alt",
        Key.LWin => "LeftWin", Key.RWin => "RightWin", Key.CapsLock => "CapsLock", Key.NumLock => "NumLock", Key.Scroll => "ScrollLock",
        Key.OemPlus => "OemPlus", Key.OemMinus => "OemMinus", Key.OemComma => "OemComma", Key.OemPeriod => "OemPeriod",
        _ => null
    };

    /// <summary>Dropdown entry for a saved single-key value (aliases such as Control map to Ctrl).</summary>
    private static string? SingleKeyItem(string value)
    {
        if (!KeyParser.TryParse(value, out var keys, out _) || keys.Count != 1) return null;
        return SingleKeys.FirstOrDefault(item => KeyParser.TryParse(item, out var parsed, out _) && string.Equals(parsed[0].LogicalKey, keys[0].LogicalKey, StringComparison.OrdinalIgnoreCase));
    }
}
