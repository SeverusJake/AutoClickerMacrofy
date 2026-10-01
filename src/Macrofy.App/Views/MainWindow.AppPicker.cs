using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Macrofy.App.Services;
using Macrofy.Platform.Models;
namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    /// <summary>Open-window list for the Add/Edit app dialog. Fills fields only; nothing is saved until Save app.</summary>
    private (Control Picker, TextBlock MatchLine) AppWindowPicker(TextBox name, TextBox executable, TextBox title)
    {
        IReadOnlyList<TargetWindow>? windows = null;
        var generation = 0; var applying = false;
        var list = new ListBox { Name = "AppWindowList", MaxHeight = 160 };
        var status = Wrap("", "muted", 12); status.Name = "AppWindowStatus";
        var suggestions = new StackPanel { Name = "AppRuleSuggestions", Spacing = 4 };
        var match = Wrap("", "muted", 12); match.Name = "AppRuleMatch";
        int Count(string exe, string rule) => windows?.Count(w => TitleRule.MatchesWindow(exe, rule, w)) ?? 0;
        void UpdateMatch()
        {
            if (windows is null) { match.Text = ""; return; }
            var count = Count(executable.Text?.Trim() ?? "", title.Text?.Trim() ?? "");
            (match.Text, var role) = count switch
            {
                1 => ("✓ Matches 1 open window", "success"),
                0 => ("No open window matches. The app may be closed, or the rule is wrong.", "warning"),
                _ => ($"Matches {count} open windows. Narrow the rule so playback can pick one.", "warning")
            };
            match.Foreground = palette.Brush(role);
        }
        void SetField(TextBox box, string value) { applying = true; box.Text = value; applying = false; }
        void ShowSuggestions(TargetWindow window)
        {
            suggestions.Children.Clear();
            var exe = window.App.ExecutablePath ?? "";
            var items = TitleRule.Suggestions(window.Title, exe);
            for (var i = 0; i < items.Count; i++)
            {
                var rule = items[i].Rule;
                var radio = new RadioButton { Name = "AppRule_" + i, Content = $"{rule}   ({items[i].Label} · matches {Count(exe, rule)})" };
                radio.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty && radio.IsChecked == true) { SetField(title, rule); UpdateMatch(); } };
                suggestions.Children.Add(radio);
            }
            ((RadioButton)suggestions.Children[0]).IsChecked = true;
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not PickedWindow picked) return;
            SetField(name, TitleRule.SuggestedName(picked.Window.Title));
            SetField(executable, picked.Window.App.ExecutablePath ?? "");
            ShowSuggestions(picked.Window);
        };
        title.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || applying) return;
            foreach (var radio in suggestions.Children.OfType<RadioButton>()) radio.IsChecked = false;
            UpdateMatch();
        };
        executable.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !applying) UpdateMatch(); };
        async Task LoadAsync()
        {
            var version = ++generation;
            list.ItemsSource = null; suggestions.Children.Clear();
            if (compatibility is null) { status.Text = "Window list unavailable. Enter details manually."; return; }
            status.Text = "Loading open windows…";
            try
            {
                var listed = await compatibility.Catalog.ListAsync();
                if (version != generation) return;
                windows = listed;
                list.ItemsSource = listed.Select(w => new PickedWindow(w)).ToArray();
                status.Text = listed.Count == 0 ? "No open windows found." : "Pick a window to fill the fields below.";
            }
            catch (Exception error)
            {
                if (version != generation) return;
                windows = null; status.Text = "Window list unavailable. Enter details manually. " + error.Message;
            }
            UpdateMatch();
        }
        var refresh = TextButton("Refresh", () => _ = LoadAsync()); refresh.Name = "AppWindowRefresh";
        _ = LoadAsync();
        var picker = Stack(Row(Text("Pick an open window", "muted", 12), refresh), list, status, suggestions);
        return (picker, match);
    }

    private sealed record PickedWindow(TargetWindow Window)
    { public override string ToString() => $"{Window.Title}   ·   {Path.GetFileName(Window.App.ExecutablePath)}"; }
}
