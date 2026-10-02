using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Macrofy.App.Models;

namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
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
        var main = new Grid { RowDefinitions = new("Auto,Auto,*,Auto") };
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

        var sequence = new StackPanel { Spacing = 12 };
        var record = IconButton("record", "Recording is not connected in this UI build", () => { }, "danger"); record.IsEnabled = false;
        sequence.Children.Add(Row(Text("Action sequence", "info", 16), record));
        sequence.Children.Add(ActionRow(Text("#", "muted", 12), Text("Action", "muted", 12), Text("Value", "muted", 12), Text("Wait after", "muted", 12)));
        for (var i = 0; i < macro.Steps.Count; i++)
        {
            var index = i; var step = macro.Steps[i];
            var choose = TextButton(step.Kind, () => { selectedStep = index; Render(); }); choose.Name = "SelectStep_" + i;
            var value = Text(step.Value, size: 12); value.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis; ToolTip.SetTip(value, step.Value);
            var row = ActionRow(Text((i + 1).ToString(), "muted", 12), choose, value, Text(step.DelayMs + " ms", "muted", 12));
            if (i == selectedStep) row.Background = palette.Tint("accent", .14);
            sequence.Children.Add(row);
        }
        if (macro.Steps.Count == 0) sequence.Children.Add(Text("No steps", "muted"));
        var addType = new ComboBox { Name = "NewActionType", ItemsSource = new[] { "Click", "Key", "Text", "Wait", "Wheel" }, SelectedIndex = 0, Width = 110, MinHeight = 32 };
        var add = IconButton("plus", "Add action", () =>
        {
            var kind = addType.SelectedItem as string ?? "Click";
            var value = kind switch { "Click" => "480, 640", "Key" => "Space", "Text" => "Hello", "Wheel" => "120", _ => "1000" };
            ClearDrafts(macro); macro.Steps.Add(new(kind, value, 100)); selectedStep = macro.Steps.Count - 1; Save(); Render();
        }); add.Name = "AddAction";
        var remove = IconButton("trash", "Remove selected action", () => { if (selectedStep >= 0) { ClearDrafts(macro); macro.Steps.RemoveAt(selectedStep); selectedStep = Math.Max(0, selectedStep - 1); Save(); Render(); } }, "danger"); remove.Name = "RemoveAction";
        var up = IconButton("up", "Move selected action up", () => MoveStep(-1)); var down = IconButton("down", "Move selected action down", () => MoveStep(1));
        sequence.Children.Add(Row(addType, add, remove, up, down));
        var inspector = new StackPanel { Spacing = 12 };
        var editable = new List<Control> { target, state, rename, addType, add };
        inspector.Children.Add(Text("Edit action", "tertiary", 16));
        if (selectedStep >= 0)
        {
            var step = macro.Steps[selectedStep]; var key = (macro.Id, selectedStep);
            if (!drafts.TryGetValue(key, out var draft)) drafts[key] = draft = new(step);
            Workspace.ValidateStep(macro, new(draft.Kind, draft.Value, draft.Delay), out var initialError); draft.Error = initialError;
            var error = Wrap(draft.Error, "danger", 12); error.Name = "ActionError";
            var help = Wrap(ActionHelp(draft.Kind), "muted", 12);
            void DraftChanged()
            {
                Workspace.ValidateStep(macro, new(draft.Kind, draft.Value, draft.Delay), out var problem);
                draft.Error = problem; error.Text = problem; RefreshPlayback();
                help.Text = ActionHelp(draft.Kind);
            }
            var kindPicker = Choice(["Click", "Key", "Text", "Wait", "Wheel"], draft.Kind, value => { draft.Kind = value; DraftChanged(); }); kindPicker.Name = "ActionKind";
            var valueInput = new TextBox { Name = "ActionValue", Text = draft.Value, MinHeight = 32, AcceptsReturn = draft.Kind == "Text", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            valueInput.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) { draft.Value = valueInput.Text ?? ""; DraftChanged(); } };
            var delayInput = new NumericUpDown { Name = "ActionDelay", Value = draft.Delay, Minimum = 0, Maximum = 600000, Increment = 50, FormatString = "0", MinHeight = 32 };
            delayInput.ValueChanged += (_, _) => { draft.Delay = (int)(delayInput.Value ?? 0); DraftChanged(); };
            var apply = IconButton("check", "Apply change", () =>
            {
                if (!Workspace.ApplyStep(macro, selectedStep, new(draft.Kind, draft.Value, draft.Delay), out var problem)) { draft.Error = problem; error.Text = problem; return; }
                drafts.Remove(key); Save(); Render();
            }, "tertiary"); apply.Name = "ApplyAction";
            var discard = IconButton("close", "Discard unapplied action edit", () => { drafts.Remove(key); Render(); }); discard.Name = "DiscardDraft";
            refreshPlayback.Add(() => discard.IsEnabled = !Workspace.IsActive(macro) && draft.Changed);
            inspector.Children.Add(Field("Action", kindPicker)); inspector.Children.Add(Field("Position / value", valueInput));
            inspector.Children.Add(CapturePointControl(macro, draft)); inspector.Children.Add(help); inspector.Children.Add(Field("Wait after (ms)", delayInput)); inspector.Children.Add(error); inspector.Children.Add(Row(apply, discard));
            editable.AddRange([kindPicker, valueInput, delayInput, apply]);
        }
        var coordinate = Choice(["Fixed pixels", "Percentage"], macro.Coordinates, value => { macro.Coordinates = value; Save(); Render(); }); coordinate.Name = "CoordinateMode";
        inspector.Children.Add(Field("Coordinates", coordinate)); editable.Add(coordinate);
        inspector.Children.Add(Text(macro.AppId is null ? "Desktop coordinates" : "Window client coordinates", "muted", 12));
        responsiveEditor = new Grid { ColumnDefinitions = new("*,230"), RowDefinitions = new("Auto") };
        responsiveSequence = new Border { Background = palette.Tint("info", .06), Padding = new Thickness(20), Child = sequence };
        responsiveInspector = new Border { Background = palette.Tint("tertiary", .09), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(16, 20), Child = inspector };
        Add(responsiveEditor, responsiveSequence, 0); Add(responsiveEditor, responsiveInspector, 0, 1); Add(main, Scroll(responsiveEditor), 2);
        var repeatLabel = macro.Repeat == 1 ? "Once" : macro.Repeat == 100 ? "100 times" : "Until stopped";
        var repeat = Choice(["Until stopped", "100 times", "Once"], repeatLabel, value => { macro.Repeat = value == "Once" ? 1 : value == "100 times" ? 100 : 0; Save(); }); repeat.Name = "Repeat";
        var interval = new NumericUpDown { Name = "Interval", Value = macro.IntervalMs, Minimum = 0, Maximum = 600000, Increment = 100, FormatString = "0", MinHeight = 32 };
        interval.ValueChanged += (_, _) => { macro.IntervalMs = (int)(interval.Value ?? 0); Save(); };
        var settings = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 16 };
        settings.Children.Add(Field("Repeat", repeat)); Add(settings, Field("Interval between runs (ms)", interval), 0, 1); editable.AddRange([repeat, interval]);
        var run = IconButton("play", "Run: " + macro.Name, () => Start(macro), "success"); run.Name = "RunSelected";
        var test = IconButton("test", "Test selected action once", async () => await StartMacroAsync(macro, true)); test.Name = "TestSelected";
        var pause = IconButton("pause", "Pause macro", () => { Workspace.TogglePause(macro.Id); RefreshPlayback(); }, "warning"); pause.Name = "PauseSelected";
        var stop = IconButton("stop", "Stop macro", () => { Workspace.Stop(macro.Id); RefreshPlayback(); }, "danger"); stop.Name = "StopSelected";
        var progress = Text("Ready · " + macro.Steps.Count + " steps · " + Workspace.TargetName(macro), "muted", 12); progress.Name = "MacroProgress";
        Add(main, new Border { Background = palette.Tint("warning", .06), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12), Child = Stack(settings, Row(run, test, pause, stop), progress) }, 3);
        refreshPlayback.Add(() =>
        {
            var active = Workspace.IsActive(macro);
            foreach (var control in editable) control.IsEnabled = !active && !CompatibilityLocked;
            var pending = HasDraft(macro);
            add.IsEnabled = addType.IsEnabled = !active && !CompatibilityLocked && !pending;
            delete.IsEnabled = !active && !CompatibilityLocked && Workspace.Profile.Macros.Count > 1;
            remove.IsEnabled = !active && !CompatibilityLocked && !pending && selectedStep >= 0; up.IsEnabled = !active && !CompatibilityLocked && !pending && selectedStep > 0; down.IsEnabled = !active && !CompatibilityLocked && !pending && selectedStep >= 0 && selectedStep < macro.Steps.Count - 1;
            test.IsEnabled = CanRun(macro, out var testReason, selectedAction: true) && !pending && selectedStep >= 0;
            ToolTip.SetTip(test, test.IsEnabled ? "Send selected action once; Screen starts after 3 seconds" : testReason);
            run.IsEnabled = CanRun(macro, out _);
            ToolTip.SetTip(run, CanRun(macro, out var runReason) ? "Run: " + macro.Name : runReason);
            pause.IsEnabled = stop.IsEnabled = active;
            var paused = Workspace.Status(macro) == "Paused"; SetIcon(pause, paused ? "play" : "pause", paused ? "Resume macro" : "Pause macro", "warning");
            if (Workspace.Sessions.TryGetValue(macro.Id, out var session)) progress.Text = $"{session.State} · Step {session.CurrentStep}/{session.TotalSteps} · {session.CompletedLoops} loops · {session.ActiveElapsed.TotalSeconds:F1}s elapsed · {session.RemainingWait.TotalSeconds:F1}s wait · {session.DeliveryError?.Message} {session.CleanupError?.Message}";
        });
        Add(layout, main, 0, 1); return layout;
    }
    private Border ActionRow(params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new("28,85,*,75"), ColumnSpacing = 6, Margin = new Thickness(0, 6) };
        for (var i = 0; i < cells.Length; i++) Add(grid, cells[i], 0, i);
        return new Border { BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }
    private void MoveStep(int direction)
    {
        var steps = Workspace.Macro.Steps; var next = selectedStep + direction;
        if (selectedStep < 0 || next < 0 || next >= steps.Count || Workspace.IsActive(Workspace.Macro) || HasDraft(Workspace.Macro)) return;
        ClearDrafts(Workspace.Macro);
        (steps[selectedStep], steps[next]) = (steps[next], steps[selectedStep]); selectedStep = next; Save(); Render();
    }
    private sealed record TargetChoice(Guid? Id, string Name) { public override string ToString() => Name; }
    private static string ActionHelp(string? kind) => kind switch
    {
        "Wheel" => "Wheel value is a signed vertical delta (+ up, − down), sent at the current pointer. Window mode requires the pointer inside current client bounds; minimized targets may reject it with OutsideClient.",
        "Key" or "Shortcut" => "Received key messages do not establish working keyboard-state-based shortcuts. Key and Shortcut each need their own observed response in Compatibility.",
        _ => ""
    };
}
