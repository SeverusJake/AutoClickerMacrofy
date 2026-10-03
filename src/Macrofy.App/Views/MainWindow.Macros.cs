using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Macrofy.App.Models;

namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    private static readonly string[] ActionKinds = ["Click", "Key", "Text", "Wait", "Wheel", "Mouse down", "Mouse up", "Key down", "Key up"];
    private static readonly string[] MouseButtons = ["Left", "Right", "Middle"];
    private int? dragFrom;

    private Control MacrosPane()
    {
        var macro = Workspace.Macro;
        selectedStep = macro.Steps.Count == 0 ? -1 : Math.Clamp(selectedStep, 0, macro.Steps.Count - 1);
        var layout = new Grid { ColumnDefinitions = new("190,*") };
        var library = new StackPanel { Spacing = 8 };
        library.Children.Add(Row(Text("Macros", size: 16), IconButton("plus", "New macro", NewMacro)));
        foreach (var item in Workspace.Profile.Macros)
        {
            var button = TextButton(item.Name, () => Edit(item));
            button.Content = Stack(Text(item.Name), Text(Workspace.TargetName(item), "muted", 12));
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (item.Id == macro.Id) { button.Background = palette.Tint("accent", .18); button.BorderBrush = palette.Brush("accent"); }
            library.Children.Add(button);
        }
        Add(layout, new Border { Background = palette.Brush("subtle"), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 20), Child = Scroll(library) }, 0);

        var main = new Grid { RowDefinitions = new("Auto,Auto,Auto,*") };
        var rename = IconButton("edit", "Rename macro", () => RenameMacro(macro), "tertiary");
        var clone = IconButton("copy", "Duplicate macro", () => DuplicateMacro(macro));
        var delete = IconButton("trash", "Delete macro", () => DeleteMacro(macro), "danger");
        Add(main, Panel(Row(Text(macro.Name, "secondary", 16), rename, clone, delete), padding: new Thickness(20, 14)), 0);
        var targets = Workspace.Profile.Apps.Select(a => new TargetChoice(a.Id, a.Name)).Prepend(new TargetChoice(null, "Screen (default)")).ToList();
        if (macro.AppId is { } missing && targets.All(t => t.Id != missing)) targets.Add(new(missing, "Missing app"));
        var target = new ComboBox { Name = "MacroTarget", ItemsSource = targets, SelectedItem = targets.Single(t => t.Id == macro.AppId), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 32 };
        target.SelectionChanged += (_, _) => { if (target.SelectedItem is TargetChoice choice && !Workspace.IsActive(macro) && !CompatibilityLocked) { macro.AppId = choice.Id; Save(); Render(); } };
        var state = Choice(["Minimized", "Background"], macro.WindowState, value => { macro.WindowState = value; Save(); }); state.Name = "WindowState"; state.IsVisible = macro.AppId is not null;
        var targetGrid = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 16 };
        targetGrid.Children.Add(Field("Playback target", target)); Add(targetGrid, Field("Window state", state), 0, 1);
        var modeNote = Text(macro.AppId is null ? "Visible desktop" : "Background clicks don't work in every game. Watch the first run.", "muted", 12); modeNote.Name = "MacroModeNote";
        Add(main, new Border { Background = palette.Tint("secondary", .07), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(20, 12), Child = Stack(targetGrid, modeNote) }, 1);

        var editable = new List<Control> { target, state, rename };
        var structural = new List<Control>();

        // Top bar: playback controls, loop/times, interval in seconds, coordinate mode, progress.
        var run = IconButton("play", "Run: " + macro.Name, () => Start(macro), "success"); run.Name = "RunSelected";
        var test = IconButton("test", "Test selected step once", async () => await StartMacroAsync(macro, true)); test.Name = "TestSelected";
        var pause = IconButton("pause", "Pause macro", () => { Workspace.TogglePause(macro.Id); RefreshPlayback(); }, "warning"); pause.Name = "PauseSelected";
        var stop = IconButton("stop", "Stop macro", () => { Workspace.Stop(macro.Id); RefreshPlayback(); }, "danger"); stop.Name = "StopSelected";
        var times = new TextBox { Name = "MacroTimes", Text = macro.Repeat is 0 or 1 ? "" : macro.Repeat.ToString(CultureInfo.InvariantCulture), Width = 64, MinHeight = 30, PlaceholderText = "∞", IsVisible = macro.Repeat != 1 };
        ToolTip.SetTip(times, "Number of runs; empty = until stopped");
        var loop = new ToggleSwitch { Name = "MacroLoop", IsChecked = macro.Repeat != 1, OnContent = null, OffContent = null, MinWidth = 0, Width = 48, VerticalAlignment = VerticalAlignment.Center };
        loop.IsCheckedChanged += (_, _) =>
        {
            if ((loop.IsChecked == true) == (macro.Repeat != 1)) return;
            macro.Repeat = loop.IsChecked == true ? 0 : 1; times.Text = ""; times.IsVisible = loop.IsChecked == true; Save(); RefreshPlayback();
        };
        times.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || loop.IsChecked != true) return;
            var valid = TryParseTimes(times.Text, out var repeat);
            times.BorderBrush = palette.Brush(valid ? "line" : "danger");
            if (!valid) return;
            macro.Repeat = repeat; Save();
        };
        var interval = new NumericUpDown { Name = "MacroInterval", Value = macro.IntervalMs / 1000m, Minimum = 0, Maximum = 600, Increment = 0.5m, FormatString = "0.##", Width = 72, MinHeight = 30, ShowButtonSpinner = false };
        ToolTip.SetTip(interval, "Seconds between runs");
        interval.ValueChanged += (_, _) => { macro.IntervalMs = (int)Math.Round((interval.Value ?? 0) * 1000); Save(); };
        Button CoordinateButton(string name, string label, string mode)
        {
            var button = TextButton(label, () => { if (macro.Coordinates == mode) return; macro.Coordinates = mode; Save(); Render(); }); button.Name = name;
            if (macro.Coordinates == mode) { button.Background = palette.Tint("secondary", .2); button.BorderBrush = palette.Brush("secondary"); }
            ToolTip.SetTip(button, mode == "Percentage" ? "Click positions as 0–100 % of the target" : macro.AppId is null ? "Click positions in desktop pixels" : "Click positions in window client pixels");
            return button;
        }
        var pixels = CoordinateButton("Coord_Pixels", "Pixels", "Fixed pixels");
        var percent = CoordinateButton("Coord_Percent", "%", "Percentage");
        var progress = Text("Ready · " + macro.Steps.Count + " steps · " + Workspace.TargetName(macro), "muted", 12); progress.Name = "MacroProgress";
        var topBar = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 8 };
        foreach (var control in new Control[] { run, test, pause, stop, Row(loop, Text("Loop", size: 13), times), Row(Text("Interval", size: 13), interval, Text("s", "muted", 13)), Row(pixels, percent), progress })
        { control.VerticalAlignment = VerticalAlignment.Center; topBar.Children.Add(control); }
        editable.AddRange([loop, times, interval, pixels, percent]);
        Add(main, new Border { Background = palette.Tint("warning", .06), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(20, 10), Child = topBar }, 2);

        // Step table.
        var table = new StackPanel { Spacing = 0 };
        var rows = new List<Border>();
        var errors = Wrap("", "danger", 12); errors.Name = "ActionError";
        var help = Wrap("", "muted", 12); help.Name = "ActionHelp";
        var warning = Wrap("", "warning", 12); warning.Name = "ActionWarning";
        var recordStatus = Wrap(recordMessage, "muted", 12); recordStatus.Name = "RecordStatus";
        var recordApp = macro.AppId is { } recordAppId ? Workspace.Profile.Apps.SingleOrDefault(a => a.Id == recordAppId) : null;
        table.Children.Add(StepRow(Text("", size: 12), Text("#", "muted", 12), Text("Action", "muted", 12), Text("Position / value", "muted", 12), Text("Wait after (ms)", "muted", 12), Text("", size: 12)));

        void UpdateErrors()
        {
            var lines = new List<string>();
            for (var i = 0; i < macro.Steps.Count; i++)
            {
                if (drafts.TryGetValue((macro.Id, i), out var draft) && draft.Changed) { if (draft.Error.Length > 0) lines.Add($"Step {i + 1}: {draft.Error}"); }
                else if (!Workspace.ValidateStep(macro, macro.Steps[i], out var problem)) lines.Add($"Step {i + 1}: {problem}");
            }
            errors.Text = string.Join(Environment.NewLine, lines.Take(3));
            warning.Text = string.Join(Environment.NewLine, HeldUntilEnd(macro.Steps).Take(3));
        }
        void Select(int index)
        {
            selectedStep = index;
            for (var i = 0; i < rows.Count; i++) rows[i].Background = i == index ? palette.Tint("accent", .14) : Avalonia.Media.Brushes.Transparent;
            help.Text = index >= 0 && index < macro.Steps.Count ? ActionHelp(macro.Steps[index].Kind) : "";
            RefreshPlayback();
        }
        ActionDraft Draft(int index)
        {
            var key = (macro.Id, index);
            if (!drafts.TryGetValue(key, out var draft)) drafts[key] = draft = new(macro.Steps[index]);
            return draft;
        }
        void Commit(int index)
        {
            var draft = Draft(index);
            string problem;
            var holdText = draft.HoldText.Trim();
            if (!int.TryParse(draft.DelayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay)) problem = "Wait after must be whole milliseconds.";
            else if (!int.TryParse(holdText.Length == 0 ? "0" : holdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hold)) problem = "Hold must be whole milliseconds.";
            else if (Workspace.ApplyStep(macro, index, new(draft.Kind, draft.Value, delay, draft.Button, hold), out problem)) { drafts.Remove((macro.Id, index)); Save(); problem = ""; }
            draft.Error = problem;
            UpdateErrors(); RefreshPlayback();
        }
        void Edited(TextBox box, int index, Action<ActionDraft, string> apply)
        {
            box.GotFocus += (_, _) => { if (selectedStep != index) Select(index); };
            box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { apply(Draft(index), box.Text ?? ""); Commit(index); } };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { drafts.Remove((macro.Id, index)); e.Handled = true; Render(); }
                else if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down) { e.Handled = true; MoveStep(index, index + (e.Key == Key.Up ? -1 : 1)); }
            };
        }

        for (var i = 0; i < macro.Steps.Count; i++)
        {
            var index = i; var step = macro.Steps[i];
            var shown = drafts.TryGetValue((macro.Id, i), out var pending) && pending.Changed ? pending : new ActionDraft(step);
            var role = KindRole(shown.Kind);
            var handle = new Border { Name = "StepHandle_" + i, Background = Avalonia.Media.Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = Text("⋮⋮", "muted", 14) };
            ToolTip.SetTip(handle, "Drag to reorder (or Alt+↑ / Alt+↓)");
            handle.PointerPressed += (_, e) => { if (e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed && CanReorder(macro)) { dragFrom = index; e.Pointer.Capture(handle); e.Handled = true; } };
            handle.PointerReleased += (_, e) =>
            {
                if (dragFrom != index) return;
                dragFrom = null;
                var y = e.GetPosition(table).Y;
                var to = rows.FindIndex(r => y >= r.Bounds.Top && y < r.Bounds.Bottom);
                if (to >= 0 && to != index) MoveStep(index, to);
            };
            var number = TextButton((i + 1).ToString(CultureInfo.InvariantCulture), () => Select(index)); number.Name = "SelectStep_" + i; number.Padding = new Thickness(6, 2);
            var kind = new ComboBox { Name = "StepKind_" + i, ItemsSource = ActionKinds, SelectedItem = shown.Kind, MinHeight = 30, Width = 128,
                Background = palette.Tint(role, .14), BorderBrush = palette.Brush(role), Foreground = palette.Brush(role) };
            kind.GotFocus += (_, _) => { if (selectedStep != index) Select(index); };
            kind.SelectionChanged += (_, _) =>
            {
                if (kind.SelectedItem is not string next || next == macro.Steps[index].Kind) return;
                var current = macro.Steps[index];
                var value = Workspace.ValidateStep(macro, current with { Kind = next }, out _) ? current.Value : DefaultValue(next);
                if (!Workspace.ApplyStep(macro, index, current with { Kind = next, Value = value }, out var problem)) { errors.Text = $"Step {index + 1}: {problem}"; return; }
                drafts.Remove((macro.Id, index)); selectedStep = index; Save(); Render();
            };
            var kindCell = ActionKinds.Contains(shown.Kind) ? Row(UiIcons.Create(shown.Kind, palette.Brush(role)), kind) : (Control)kind;
            Control value;
            Control HoldBox()
            {
                var hold = new TextBox { Name = "StepHold_" + index, Text = shown.HoldText, Width = 64, MinHeight = 30 };
                ToolTip.SetTip(hold, "Keep it pressed this long; 0 = normal press");
                Edited(hold, index, (draft, text) => draft.HoldText = text);
                editable.Add(hold);
                return Row(Text("Hold", "muted", 12), hold, Text("ms", "muted", 12));
            }
            if (shown.Kind is "Click" or "Mouse down" or "Mouse up")
            {
                var parts = shown.Value.Split(',', 2);
                var x = new TextBox { Name = "StepX_" + i, Text = parts[0].Trim(), Width = 72, MinHeight = 30 };
                var y = new TextBox { Name = "StepY_" + i, Text = parts.Length > 1 ? parts[1].Trim() : "", Width = 72, MinHeight = 30 };
                Edited(x, index, (draft, text) => draft.Value = $"{text}, {y.Text}");
                Edited(y, index, (draft, text) => draft.Value = $"{x.Text}, {text}");
                var record = RecordPointButton("StepRecord_" + i,
                    () => macro.Coordinates != "Percentage" && !Workspace.IsActive(macro) && compatibility is not null && (macro.AppId is null || recordApp is not null),
                    () => ReadMacroPointAsync(recordApp),
                    point =>
                    {
                        if (index >= macro.Steps.Count || Workspace.IsActive(macro)) return;
                        macro.Steps[index] = macro.Steps[index] with { Value = FormatPoint(point) }; drafts.Remove((macro.Id, index));
                        recordMessage = $"Step {index + 1} recorded at {macro.Steps[index].Value}."; selectedStep = index; Save(); Render();
                    },
                    text => { recordMessage = text; recordStatus.Text = text; });
                record.IsVisible = compatibility is not null;
                ToolTip.SetTip(record, macro.Coordinates == "Percentage" ? "Recording gives pixels. Switch to Pixels." : "Then left-click the spot; that click is not sent to the app.");
                var button = new ComboBox { Name = "StepButton_" + i, ItemsSource = MouseButtons, SelectedItem = shown.Button, Width = 92, MinHeight = 30 };
                ToolTip.SetTip(button, "Mouse button");
                button.GotFocus += (_, _) => { if (selectedStep != index) Select(index); };
                button.SelectionChanged += (_, _) => { if (button.SelectedItem is string chosen && chosen != Draft(index).Button) { Draft(index).Button = chosen; Commit(index); } };
                var cells = new List<Control> { Text("X", "muted", 12), x, Text("Y", "muted", 12), y, button };
                if (shown.Kind == "Click") cells.Add(HoldBox());
                cells.Add(record);
                value = Row([.. cells]);
                editable.AddRange([x, y, button]);
            }
            else
            {
                var box = new TextBox { Name = "StepValue_" + i, Text = shown.Value, MinHeight = 30, Width = shown.Kind is "Wait" or "Wheel" ? 96 : 200 };
                Edited(box, index, (draft, text) => draft.Value = text);
                value = shown.Kind == "Wait" ? Row(box, Text("ms", "muted", 12)) : shown.Kind == "Key" ? Row(box, HoldBox()) : box;
                editable.Add(box);
            }
            var delay = new TextBox { Name = "StepDelay_" + i, Text = shown.DelayText, Width = 80, MinHeight = 30 };
            Edited(delay, index, (draft, text) => draft.DelayText = text);
            var duplicate = IconButton("copy", "Duplicate step " + (i + 1), () =>
            {
                if (!CanReorder(macro)) return;
                macro.Steps.Insert(index + 1, macro.Steps[index]); ClearDrafts(macro); selectedStep = index + 1; Save(); Render();
            }); duplicate.Name = "StepDuplicate_" + i;
            var remove = IconButton("trash", "Delete step " + (i + 1), () =>
            {
                if (!CanReorder(macro)) return;
                macro.Steps.RemoveAt(index); ClearDrafts(macro); selectedStep = Math.Min(index, macro.Steps.Count - 1); Save(); Render();
            }, "danger"); remove.Name = "StepDelete_" + i;
            editable.AddRange([kind, delay]); structural.AddRange([duplicate, remove]);
            var row = StepRow(handle, number, kindCell, value, delay, Row(duplicate, remove));
            rows.Add(row); table.Children.Add(row);
        }
        if (macro.Steps.Count == 0) table.Children.Add(new Border { Padding = new Thickness(8, 12), Child = Text("No steps yet. Add one below.", "muted") });

        var addBar = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var kind in ActionKinds)
        {
            var role = KindRole(kind);
            var add = new Button { Name = "Add_" + kind.Replace(" down", "Down").Replace(" up", "Up"), Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
                Background = palette.Tint(role, .14), BorderBrush = palette.Brush(role), Content = Row(UiIcons.Create(kind, palette.Brush(role)), Text("+ " + kind, role, 13)) };
            Avalonia.Automation.AutomationProperties.SetName(add, "Add " + kind + " step");
            add.Click += (_, _) =>
            {
                if (!CanReorder(macro)) return;
                macro.Steps.Add(new(kind, DefaultValue(kind), 100)); selectedStep = macro.Steps.Count - 1; Save(); Render();
            };
            structural.Add(add); addBar.Children.Add(add);
        }
        var body = new StackPanel { Spacing = 8, Children = { new Border { BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(1), Background = palette.Brush("surface"), Child = table }, addBar, recordStatus, help, warning, errors } };
        // Width stays bounded so the add buttons wrap instead of scrolling sideways.
        Add(main, new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Border { Background = palette.Tint("info", .04), Padding = new Thickness(20, 14), Child = body } }, 3);
        Select(selectedStep); UpdateErrors();

        refreshPlayback.Add(() =>
        {
            var active = Workspace.IsActive(macro);
            foreach (var control in editable) control.IsEnabled = !active && !CompatibilityLocked;
            foreach (var control in structural) control.IsEnabled = CanReorder(macro);
            delete.IsEnabled = !active && !CompatibilityLocked && Workspace.Profile.Macros.Count > 1;
            var pending = HasDraft(macro);
            test.IsEnabled = CanRun(macro, out var testReason, selectedAction: true) && !pending && selectedStep >= 0;
            ToolTip.SetTip(test, test.IsEnabled ? "Send the selected step once; Screen starts after 3 seconds" : testReason);
            run.IsEnabled = CanRun(macro, out var runReason);
            ToolTip.SetTip(run, run.IsEnabled ? "Run: " + macro.Name : runReason);
            pause.IsEnabled = stop.IsEnabled = active;
            var paused = Workspace.Status(macro) == "Paused"; SetIcon(pause, paused ? "play" : "pause", paused ? "Resume macro" : "Pause macro", "warning");
            if (Workspace.Sessions.TryGetValue(macro.Id, out var session)) progress.Text = $"{session.State} · Step {session.CurrentStep}/{session.TotalSteps} · {session.CompletedLoops} loops · {session.ActiveElapsed.TotalSeconds:F1}s elapsed · {session.RemainingWait.TotalSeconds:F1}s wait · {session.DeliveryError?.Message} {session.CleanupError?.Message}";
        });
        Add(layout, main, 0, 1); return layout;
    }

    private Border StepRow(params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new("26,40,174,*,96,80"), ColumnSpacing = 8, Margin = new Thickness(8, 6) };
        for (var i = 0; i < cells.Length; i++) { cells[i].VerticalAlignment = VerticalAlignment.Center; Add(grid, cells[i], 0, i); }
        return new Border { BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 0, 0, 1), Background = Avalonia.Media.Brushes.Transparent, Child = grid };
    }

    /// <summary>Times box text: empty = until stopped (0); 2–1,000,000 = that many runs.</summary>
    private static bool TryParseTimes(string? text, out int repeat)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0) { repeat = 0; return true; }
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out repeat) && repeat is >= 2 and <= 1_000_000;
    }

    private bool CanReorder(Macro macro) => !Workspace.IsActive(macro) && !CompatibilityLocked && !HasDraft(macro);

    private void MoveStep(int from, int to)
    {
        var macro = Workspace.Macro; var steps = macro.Steps;
        if (from < 0 || from >= steps.Count || to < 0 || to >= steps.Count || from == to || !CanReorder(macro)) return;
        var step = steps[from]; steps.RemoveAt(from); steps.Insert(to, step);
        ClearDrafts(macro); selectedStep = to; Save(); Render();
    }

    private static string KindRole(string kind) => kind switch
    {
        "Click" or "Mouse down" or "Mouse up" => "secondary", "Key" or "Key down" or "Key up" => "tertiary",
        "Text" => "success", "Wait" => "warning", "Wheel" => "info", _ => "muted"
    };
    private static string DefaultValue(string kind) => kind switch
    {
        "Click" or "Mouse down" or "Mouse up" => "480, 640", "Key" => "Space", "Key down" or "Key up" => "W", "Text" => "Hello", "Wheel" => "120", _ => "1000"
    };

    /// <summary>Downs with no later matching up: still valid, but held until the macro ends.</summary>
    private static IEnumerable<string> HeldUntilEnd(IReadOnlyList<MacroStep> steps)
    {
        static string[] Keys(string value) => Macrofy.Core.Actions.KeyParser.TryParse(value, out var keys, out _) ? keys.Select(k => k.LogicalKey).ToArray() : [];
        for (var i = 0; i < steps.Count; i++)
        {
            var later = steps.Skip(i + 1);
            if (steps[i].Kind == "Mouse down" && !later.Any(s => s.Kind == "Mouse up" && s.Button == steps[i].Button))
                yield return $"Step {i + 1} holds the {steps[i].Button} button until the macro ends.";
            else if (steps[i].Kind == "Key down")
            {
                var released = later.Where(s => s.Kind == "Key up").SelectMany(s => Keys(s.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (Keys(steps[i].Value).Any(k => !released.Contains(k))) yield return $"Step {i + 1} holds {steps[i].Value} until the macro ends.";
            }
        }
    }
    private sealed record TargetChoice(Guid? Id, string Name) { public override string ToString() => Name; }
    private static string ActionHelp(string? kind) => kind switch
    {
        "Wheel" => "Wheel value is a signed vertical delta (+ up, − down), sent at the current pointer. Window mode requires the pointer inside current client bounds; minimized targets may reject it with OutsideClient.",
        "Key" or "Shortcut" => "Received key messages do not establish working keyboard-state-based shortcuts. Key and Shortcut each need their own observed response in Compatibility.",
        _ => ""
    };
}
