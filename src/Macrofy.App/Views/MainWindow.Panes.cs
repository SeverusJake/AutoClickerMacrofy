using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Macrofy.App.Models;
using Macrofy.App.Services;
#if WINDOWS
using Macrofy.Platform.Models;
#endif

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
        main.Children.Add(TableRow("Enabled", "Macro", "Target app", "Steps", "Status", "Controls"));
        foreach (var macro in Workspace.Profile.Macros)
        {
            var enabled = new ToggleSwitch { Name = "Enable_" + macro.Id.ToString("N"), IsChecked = macro.Enabled,
                OnContent = null, OffContent = null, MinWidth = 0, Width = 48, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(enabled, "Include in Run all: " + macro.Name);
            ToolTip.SetTip(enabled, "Include in Run all · current runs keep their state");
            enabled.IsCheckedChanged += (_, _) => { macro.Enabled = enabled.IsChecked == true; Save(); RefreshPlayback(); };
            var status = Text(Workspace.Status(macro), StateRole(Workspace.Status(macro)), 12); status.Name = "Status_" + macro.Id.ToString("N");
            var run = IconButton("play", "Run preview: " + macro.Name, () => Start(macro), "success"); run.Name = "Run_" + macro.Id.ToString("N");
            var pause = IconButton("pause", "Pause macro: " + macro.Name, () => { Workspace.TogglePause(macro.Id); RefreshPlayback(); }, "warning"); pause.Name = "Pause_" + macro.Id.ToString("N");
            var stop = IconButton("stop", "Stop macro: " + macro.Name, () => { Workspace.Stop(macro.Id); RefreshPlayback(); }, "danger"); stop.Name = "Stop_" + macro.Id.ToString("N");
            var edit = IconButton("edit", "Edit macro: " + macro.Name, () => Edit(macro), "tertiary"); edit.Name = "Edit_" + macro.Id.ToString("N");
            var macroName = Text(macro.Name, "secondary", 13);
            var targetName = Text(Workspace.TargetName(macro), "tertiary", 13);
            macroName.TextTrimming = targetName.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(macroName, macro.Name); ToolTip.SetTip(targetName, Workspace.TargetName(macro));
            var row = TableRow(enabled, macroName, targetName, Text(macro.Steps.Count.ToString(), "accent", 13), status, Row(run, pause, stop, edit));
            row.Name = "MacroRow_" + macro.Id.ToString("N"); row.Background = Avalonia.Media.Brushes.Transparent;
            row.PointerEntered += (_, _) => row.Background = palette.Tint("accent", .08);
            row.PointerExited += (_, _) => row.Background = Avalonia.Media.Brushes.Transparent;
            AutomationProperties.SetHelpText(row, "Double-click to edit macro");
            row.DoubleTapped += (_, e) =>
            {
                if (e.Source is Visual source && source.GetVisualAncestors().Prepend(source)
                    .TakeWhile(v => v != row).Any(v => v is Button or ToggleSwitch)) return;
                e.Handled = true;
                Edit(macro);
            };
            main.Children.Add(row);
            refreshPlayback.Add(() =>
            {
                var active = Workspace.IsActive(macro); run.IsEnabled = !HasDraft(macro) && Workspace.CanStartPreview(macro);
                pause.IsEnabled = stop.IsEnabled = active; status.Text = Workspace.Status(macro); status.Foreground = palette.Brush(StateRole(status.Text));
                var paused = status.Text == "Paused"; SetIcon(pause, paused ? "play" : "pause", (paused ? "Resume macro: " : "Pause macro: ") + macro.Name, "warning");
            });
        }
        Add(grid, Panel(Scroll(main)), 0, 1); return grid;
    }
    private Control TableRow(params string[] cells) => TableRow(cells.Select(c => (Control)Text(c, "muted", 12)).ToArray());
    private Border TableRow(params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new("64,*,*,55,85,172"), MinWidth = 674, Margin = new Thickness(0, 8) };
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
#if WINDOWS
        return ScreenCompatibilityPane();
#else
        return Panel(Text("Screen click test requires Windows."));
#endif
    }

#if WINDOWS
    private Control ScreenCompatibilityPane()
    {
        var x = new TextBox { Name = "ScreenX", IsReadOnly = true, Width = 130, MinHeight = 32, PlaceholderText = "X" };
        var y = new TextBox { Name = "ScreenY", IsReadOnly = true, Width = 130, MinHeight = 32, PlaceholderText = "Y" };
        var status = Wrap("No click sent.", "muted", 13); status.Name = "ScreenTestStatus";
        var capture = TextButton("Capture pointer position in 5 seconds", () => { }); capture.Name = "CaptureScreenPosition";
        var test = TextButton("Test one click", () => { }); test.Name = "TestScreenClick";
        var body = Stack(Text("Screen click test", size: 16),
            Wrap("Screen is the default target. Keep the intended app visible and capture a harmless point. This sends one real left click and moves the pointer.", "muted", 13),
            Text("Screen position (desktop pixels)"), Row(x, y), Row(capture, test), status,
            Wrap("Test one click starts a 3 second countdown. The footer Stop button or the configured stop key cancels the countdown. A successful send confirms that Windows accepted input; check the visible app to see its response. This does not test background or minimized clicks.", "muted", 12));

        bool HasPosition() => double.TryParse(x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) && double.IsFinite(px) &&
                              double.TryParse(y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var py) && double.IsFinite(py);
        void UpdateControls()
        {
            capture.IsEnabled = !screenTestActive && Workspace.ActiveCount == 0;
            test.IsEnabled = !screenTestActive && Workspace.ActiveCount == 0 && HasPosition();
            x.IsEnabled = y.IsEnabled = !screenTestActive;
        }
        refreshPlayback.Add(UpdateControls);
        UpdateControls();

        async Task CaptureScreenPoint()
        {
            if (screenTestActive || Workspace.ActiveCount > 0) return;
            var cancellation = BeginScreenTest();
            try
            {
                for (var seconds = 5; seconds > 0; seconds--)
                {
                    status.Text = $"Hover over a harmless point. Position captured in {seconds}s. No click will be sent.";
                    await Task.Delay(1000, cancellation.Token);
                }
                var result = screenClicker.ReadPointerPosition();
                if (result.Point is { } point)
                {
                    x.Text = point.X.ToString(CultureInfo.InvariantCulture); y.Text = point.Y.ToString(CultureInfo.InvariantCulture);
                    status.Text = $"Captured ({point.X}, {point.Y}). No click sent.";
                }
                else status.Text = result.Error?.Message ?? "Could not read pointer position.";
            }
            catch (OperationCanceledException) { status.Text = "Position capture cancelled."; }
            finally { EndScreenTest(cancellation); UpdateControls(); }
        }

        async Task TestScreenClick()
        {
            if (screenTestActive || Workspace.ActiveCount > 0 || !HasPosition() ||
                !double.TryParse(x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) ||
                !double.TryParse(y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var py)) return;
            var cancellation = BeginScreenTest();
            try
            {
                for (var seconds = 3; seconds > 0; seconds--)
                {
                    status.Text = $"One screen click at ({px}, {py}) in {seconds}s. Stop or press {Workspace.Document.Shortcuts.Stop} to cancel.";
                    await Task.Delay(1000, cancellation.Token);
                }
                var result = await screenClicker.ClickAsync(new(px, py), cancellation.Token);
                status.Text = result.Queued ? "Windows accepted one click. Check the visible app response." : "Click not sent: " + (result.Error?.Message ?? "unknown error");
                AddLog(result.Queued ? $"Screen click accepted at ({px}, {py})" : "Screen click rejected: " + (result.Error?.Message ?? "unknown error"));
            }
            catch (OperationCanceledException) { status.Text = "Screen click cancelled before injection."; }
            finally { EndScreenTest(cancellation); UpdateControls(); }
        }

        capture.Click += async (_, _) => await CaptureScreenPoint();
        test.Click += async (_, _) => await TestScreenClick();
        return Panel(Scroll(body));
    }

    private CancellationTokenSource BeginScreenTest()
    {
        screenTestActive = true;
        screenTestCancellation = new CancellationTokenSource();
        RefreshPlayback();
        return screenTestCancellation;
    }

    private void EndScreenTest(CancellationTokenSource cancellation)
    {
        if (screenTestCancellation == cancellation) screenTestCancellation = null;
        screenTestActive = false;
        cancellation.Dispose();
        RefreshPlayback();
    }
#endif

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
        var shortcuts = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 8 };
        var run = ShortcutInput("Run", Workspace.Document.Shortcuts.Run);
        var pause = ShortcutInput("Pause", Workspace.Document.Shortcuts.Pause);
        var stop = ShortcutInput("Stop", Workspace.Document.Shortcuts.Stop);
        Add(shortcuts, Field("Run all enabled macros", run), 0); Add(shortcuts, Field("Pause / resume all", pause), 1); Add(shortcuts, Field("Stop all", stop), 2);
        var body = Stack(Text("Settings", size: 16), Field("Color theme", theme), chips, Field("Appearance", mode), Text("Shortcuts", size: 15), shortcuts,
            Text("F1–F12 · Works while Macrofy is focused", "muted", 12), Text("Run and Pause control previews in this build.", "muted", 12));
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
