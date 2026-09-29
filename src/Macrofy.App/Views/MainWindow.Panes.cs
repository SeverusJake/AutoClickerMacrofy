using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Macrofy.App.Models;
using Macrofy.App.Services;

namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    private Control ProfilesPane()
    {
        var grid = new Grid { ColumnDefinitions = new("220,*") };
        var library = new StackPanel { Spacing = 8 };
        library.Children.Add(Text("Profiles", size: 16));
        foreach (var profile in Workspace.Document.Profiles)
        {
            var button = TextButton(profile.Name, () => { Workspace.SelectProfile(profile.Id); selectedStep = 0; Save(); Render(); });
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Content = Stack(Text(profile.Name), Text($"{profile.Apps.Count} saved apps", "muted", 12));
            if (profile.Id == Workspace.Profile.Id) { button.Background = palette.Tint("accent", .18); button.BorderBrush = palette.Brush("accent"); }
            library.Children.Add(button);
        }
        library.Children.Add(Row(IconButton("plus", "New profile", NewProfile), IconButton("edit", "Rename profile", () => RenameProfile(Workspace.Profile)), IconButton("copy", "Duplicate profile", DuplicateProfile)));
        var delete = IconButton("trash", "Delete profile", DeleteProfile, "danger");
        delete.IsEnabled = Workspace.Document.Profiles.Count > 1 && !Workspace.Profile.Macros.Any(Workspace.IsActive);
        refreshPlayback.Add(() => delete.IsEnabled = Workspace.Document.Profiles.Count > 1 && !Workspace.Profile.Macros.Any(Workspace.IsActive));
        library.Children.Add(delete);
        Add(grid, new Border { Background = palette.Brush("subtle"), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(16, 20), Child = Scroll(library) }, 0);
        var main = new StackPanel { Spacing = 16 };
        main.Children.Add(Row(Text(Workspace.Profile.Name + " — macros", size: 16), IconButton("plus", "New macro", NewMacro)));
        main.Children.Add(TableRow("Macro", "Target app", "Steps", "Status", "Controls"));
        foreach (var macro in Workspace.Profile.Macros)
        {
            var status = Text(Workspace.Status(macro), StateRole(Workspace.Status(macro)), 12); status.Name = "Status_" + macro.Id.ToString("N");
            var run = IconButton("play", "Run preview: " + macro.Name, () => Start(macro), "success"); run.Name = "Run_" + macro.Id.ToString("N");
            var pause = IconButton("pause", "Pause macro: " + macro.Name, () => { Workspace.TogglePause(macro.Id); RefreshPlayback(); }, "warning"); pause.Name = "Pause_" + macro.Id.ToString("N");
            var stop = IconButton("stop", "Stop macro: " + macro.Name, () => { Workspace.Stop(macro.Id); RefreshPlayback(); }, "danger"); stop.Name = "Stop_" + macro.Id.ToString("N");
            var edit = IconButton("edit", "Edit macro: " + macro.Name, () => Edit(macro), "tertiary"); edit.Name = "Edit_" + macro.Id.ToString("N");
            main.Children.Add(TableRow(Text(macro.Name, "secondary", 13), Text(Workspace.TargetName(macro), "tertiary", 13), Text(macro.Steps.Count.ToString(), "accent", 13), status, Row(run, pause, stop, edit)));
            refreshPlayback.Add(() =>
            {
                var active = Workspace.IsActive(macro); run.IsEnabled = !active && !HasDraft(macro) && macro.Steps.Count > 0 && Workspace.HasTarget(macro);
                pause.IsEnabled = stop.IsEnabled = active; status.Text = Workspace.Status(macro); status.Foreground = palette.Brush(StateRole(status.Text));
                var paused = status.Text == "Paused"; SetIcon(pause, paused ? "play" : "pause", (paused ? "Resume macro: " : "Pause macro: ") + macro.Name, "warning");
            });
        }
        Add(grid, Panel(Scroll(main)), 0, 1); return grid;
    }
    private Control TableRow(params string[] cells) => TableRow(cells.Select(c => (Control)Text(c, "muted", 12)).ToArray());
    private Control TableRow(params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new("*,*,55,85,172"), MinWidth = 610, Margin = new Thickness(0, 8) };
        for (var i = 0; i < cells.Length; i++) { cells[i].Margin = new Thickness(4, 0); Add(grid, cells[i], 0, i); }
        return new Border { BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6), Child = grid };
    }
    private static string StateRole(string? status) => status switch { "Running" => "success", "Paused" => "warning", "Stopped" => "danger", _ => "info" };

    private Control AppsPane()
    {
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(Row(Text("Saved apps", size: 16), IconButton("plus", "Add app", () => AppDialog(null))));
        foreach (var app in Workspace.Profile.Apps)
        {
            var used = Workspace.Profile.Macros.Any(m => m.AppId == app.Id);
            var active = Workspace.Profile.Macros.Any(m => m.AppId == app.Id && Workspace.IsActive(m));
            var edit = IconButton("edit", "Edit app: " + app.Name, () => AppDialog(app), "tertiary"); edit.IsEnabled = !active;
            refreshPlayback.Add(() => edit.IsEnabled = !Workspace.Profile.Macros.Any(m => m.AppId == app.Id && Workspace.IsActive(m)));
            var delete = IconButton("trash", used ? "Unassign this app from macros before deleting" : "Delete app: " + app.Name, () => Confirm("Delete " + app.Name + "?", () => { Workspace.Profile.Apps.Remove(app); Save(); Render(); }), "danger"); delete.IsEnabled = !used && !active;
            var details = Stack(Text(app.Name, "secondary", 15), Wrap(app.Executable, "muted", 12), Text(app.TitleRule, "tertiary", 12));
            var grid = new Grid { ColumnDefinitions = new("*,Auto") }; grid.Children.Add(details); Add(grid, Row(edit, delete), 0, 1);
            body.Children.Add(new Border { BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 10), Child = grid });
        }
        if (Workspace.Profile.Apps.Count == 0) body.Children.Add(Text("No saved apps", "muted"));
        return Panel(Scroll(body));
    }

    private Control CompatibilityPane()
    {
        var macro = Workspace.Macro;
        var body = Stack(Text("Compatibility", size: 16), Text(Workspace.TargetName(macro), "secondary"));
        foreach (var state in new[] { "Minimized", "Background — visible", "Background — partly covered", "Background — covered" })
            body.Children.Add(Row(Text(state), Text("Unconfirmed", "warning", 12)));
        body.Children.Add(Wrap("Use the click probe to test a chosen game button. Preview playback sends no desktop input.", "muted", 13));
        var probe = TextButton("Open click probe", OpenProbe); probe.IsEnabled = Workspace.ActiveCount == 0;
        refreshPlayback.Add(() => probe.IsEnabled = Workspace.ActiveCount == 0); body.Children.Add(probe);
        return Panel(Scroll(body));
    }

    private Control SettingsPane()
    {
        var theme = new ComboBox { Name = "ColorTheme", ItemsSource = ThemePalette.Choices.Select(t => t.Name).ToArray(), SelectedItem = palette.Choice.Name, MinHeight = 34, Width = 300 };
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedItem is not string name) return;
            Workspace.Document.Theme = ThemePalette.Choices.Single(t => t.Name == name).Id; Save(); Render();
        };
        var mode = Choice(["Light", "Dark"], palette.Dark ? "Dark" : "Light", value => SetAppearance(value.ToLowerInvariant())); mode.Name = "Appearance"; mode.Width = 300;
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var role in new[] { "accent", "secondary", "tertiary", "success", "warning", "danger", "info" }) chips.Children.Add(new Border { Width = 32, Height = 12, Background = palette.Brush(role) });
        var body = Stack(Text("Settings", size: 16), Field("Color theme", theme), chips, Field("Appearance", mode), Text("Shortcuts", size: 15), Text("Stop previews in this window: F10", "muted", 13), Text("Recording / global shortcuts: pending", "muted", 13));
        return Panel(Scroll(body));
    }
    private Control LogPane()
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Row(Text("Log", size: 16), IconButton("trash", "Clear preview log", () => { log.Clear(); Render(); })));
        if (log.Count == 0) body.Children.Add(Text("No events", "muted"));
        foreach (var entry in log) body.Children.Add(Row(Text(entry.Time, "muted", 12), Wrap(entry.Event, size: 13)));
        return Panel(Scroll(body));
    }
    private Control AboutPane()
    {
        var folder = store?.Folder ?? "Not saved in this session";
        var body = Stack(Text("Macrofy", size: 18), Text("Windows macro workspace · UI preview", "muted"), Text("Version " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"), "muted", 12),
            Wrap("Run controls preview the sequence only. Recording and real playback are not connected in this build.", "muted", 13), Text("Data folder", size: 15), Wrap(folder, "muted", 12),
            Text("Avalonia · MIT license", "muted", 12), Text(".NET · MIT license", "muted", 12));
        if (store is not null) body.Children.Add(TextButton("Open data folder", () =>
        {
            try { System.IO.Directory.CreateDirectory(folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); }
            catch (Exception error) { messageText.Text = "Could not open folder: " + error.Message; }
        }));
        return Panel(Scroll(body));
    }
    private TextBlock Wrap(string value, string role = "ink", double size = 14)
    { var text = Text(value, role, size); text.TextWrapping = Avalonia.Media.TextWrapping.Wrap; return text; }
}
