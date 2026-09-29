using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.App.Views;
using Xunit;

namespace Macrofy.App.Tests;

public class NativeUiTests
{
    [AvaloniaFact]
    public void RunAllPreservesUnresolvedSaveConflictMessage()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-ui-tests-" + Guid.NewGuid());
        var store = new WorkspaceStore(folder); var document = store.Load(); store.Save(document);
        var state = new WorkspaceState(document); var window = new MainWindow(state, store); window.Show();
        try
        {
            var other = new WorkspaceStore(folder); var external = other.Load(); external.Mode = "dark"; other.Save(external);
            Find<ToggleSwitch>(window, "Enable_" + state.Macro.Id.ToString("N")).IsChecked = false;
            var feedback = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("Changes not saved:") == true);
            var error = feedback.Text;
            Click(window, "RunAllEnabled");
            Assert.Equal(error, feedback.Text);
            Assert.Contains("Unsaved", window.Title);
            Assert.Equal(1, state.ActiveCount);
        }
        finally { window.Close(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void ProfileSwitchRespondsToHoverAndClickAndPersistsRunAllSelection()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-ui-tests-" + Guid.NewGuid());
        var store = new WorkspaceStore(folder);
        var state = new WorkspaceState(store.Load());
        var first = state.Macro;
        var second = state.Profile.Macros[1];
        var window = new MainWindow(state, store); window.Show();
        try
        {
            var toggle = Find<ToggleSwitch>(window, "Enable_" + first.Id.ToString("N"));
            Assert.True(toggle.IsChecked);
            var row = Find<Border>(window, "MacroRow_" + first.Id.ToString("N"));
            var background = row.Background;
            var point = toggle.TranslatePoint(new Point(20, toggle.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            Assert.True(toggle.IsPointerOver);
            Assert.NotEqual(background, row.Background);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.False(toggle.IsChecked);
            var secondToggle = Find<ToggleSwitch>(window, "Enable_" + second.Id.ToString("N"));
            secondToggle.IsChecked = false;
            Assert.False(Find<Button>(window, "RunAllEnabled").IsEnabled);
            secondToggle.IsChecked = true;
            Assert.True(Find<Button>(window, "RunAllEnabled").IsEnabled);
            Click(window, "RunAllEnabled");
            Assert.Equal("Idle", state.Status(first));
            Assert.Equal("Running", state.Status(second));
            Assert.Single(state.Sessions);
            Click(window, "Tab_Settings"); Click(window, "Tab_Profiles");
            Assert.False(Find<ToggleSwitch>(window, toggle.Name!).IsChecked);
            Assert.False(Find<Button>(window, "RunAllEnabled").IsEnabled);
            var reopened = new WorkspaceState(new WorkspaceStore(folder).Load());
            Assert.False(reopened.Profile.Macros[0].Enabled);
            Assert.True(reopened.Profile.Macros[1].Enabled);
        }
        finally { window.Close(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void RunAllKeepsExistingSessionsAndSkipsInvalidTargetsAndUnappliedDrafts()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var first = state.Macro; var second = state.Profile.Macros[1];
        second.AppId = first.AppId;
        var missing = new Macro { AppId = Guid.NewGuid(), Steps = [new("Click", "10, 20", 0)] };
        var invalid = new Macro { Steps = [new("Click", "bad", 0)] };
        var draft = new Macro { Steps = [new("Click", "10, 20", 0)] };
        state.Profile.Macros.AddRange([missing, invalid, draft]);
        Assert.True(state.StartPreview(first)); state.TogglePause(first.Id);
        var existing = state.Sessions[first.Id];
        var window = new MainWindow(state); window.Show();
        try
        {
            Click(window, "Edit_" + draft.Id.ToString("N"));
            Find<TextBox>(window, "ActionValue").Text = "30, 40";
            Click(window, "Tab_Profiles"); Click(window, "RunAllEnabled");
            Assert.Equal("Paused", state.Status(first)); Assert.Same(existing, state.Sessions[first.Id]);
            Assert.Equal("Running", state.Status(second));
            Assert.Equal(2, state.Sessions.Count);
            Click(window, "RunAllEnabled"); Assert.Same(existing, state.Sessions[first.Id]);
            var toggle = Find<ToggleSwitch>(window, "Enable_" + second.Id.ToString("N"));
            toggle.IsChecked = false;
            Assert.Equal("Running", state.Status(second));
            Find<ComboBox>(window, "ActiveProfile").SelectedItem = state.Document.Profiles[1];
            Click(window, "RunAllEnabled");
            Assert.All(state.Profile.Macros, m => Assert.Equal("Running", state.Status(m)));
            Assert.Equal(4, state.Sessions.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NativeShellProvidesApprovedTabsAndProfiles()
    {
        var window = new MainWindow(new WorkspaceState(WorkspaceDocument.CreateDefault()));
        try
        {
            window.Show();
            var controls = window.GetVisualDescendants().ToArray();
            Assert.Equal(7, controls.OfType<Button>().Count(b => b.Name?.StartsWith("Tab_") == true));
            Assert.Contains(controls.OfType<TextBlock>(), t => t.Text == "Auto click");
            Assert.Contains(controls.OfType<TextBlock>(), t => t.Text == "Collect rewards");
            Assert.Contains(controls.OfType<TextBlock>(), t => t.Text == "Steps");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OpeningMacroRestoresTargetAndInvalidDraftDoesNotReplaceSavedStep()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var window = new MainWindow(state); window.Show();
        try
        {
            var macro = state.Profile.Macros[1];
            Click(window, "Edit_" + macro.Id.ToString("N"));
            Assert.Equal(macro.Id, state.Macro.Id);
            Assert.Contains(Find<ComboBox>(window, "MacroTarget").SelectedItem!.ToString()!, "CookieRun");
            Find<TextBox>(window, "ActionValue").Text = "invalid point";
            Click(window, "ApplyAction");
            Assert.Equal("600, 420", macro.Steps[0].Value);
            Assert.Contains("X, Y", Find<TextBlock>(window, "ActionError").Text);
            Find<TextBox>(window, "ActionValue").Text = "10, 20";
            Click(window, "ApplyAction");
            Assert.Equal("10, 20", macro.Steps[0].Value);
            Click(window, "RunSelected");
            Assert.False(Find<TextBox>(window, "ActionValue").IsEnabled);
            Click(window, "Tab_Log");
            Assert.True(Find<Button>(window, "StopAll").IsEnabled);
            Click(window, "StopAll");
            Assert.Equal("Stopped", state.Status(macro));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ProfileAndAppearanceSwitchesPreserveWorkspaceContext()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var window = new MainWindow(state); window.Show();
        try
        {
            Find<ComboBox>(window, "ActiveProfile").SelectedItem = state.Document.Profiles[1];
            window.UpdateLayout();
            Assert.Equal("Desktop tasks", state.Profile.Name);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Open search");
            Click(window, "Tab_Settings");
            Find<ComboBox>(window, "ColorTheme").SelectedItem = "Synthwave";
            Find<ComboBox>(window, "Appearance").SelectedItem = "Dark";
            Assert.Equal("synthwave", state.Document.Theme); Assert.Equal("dark", state.Document.Mode);
            Assert.Equal("Desktop tasks", state.Profile.Name);
            Click(window, "Tab_Profiles");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Type note");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NativeViewsRenderWithVisibleFooterAndEditor()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var window = new MainWindow(state); window.Show();
        try
        {
            var capture = Environment.GetEnvironmentVariable("MACROFY_UI_CAPTURE_DIR");
            foreach (var tab in new[] { "Profiles", "Macros", "Apps", "Settings" })
            {
                Click(window, "Tab_" + tab);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                Assert.True(Find<Button>(window, "StopAll").Bounds.Height > 0);
                if (capture is not null) { Directory.CreateDirectory(capture); frame.Save(Path.Combine(capture, tab.ToLowerInvariant() + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            }
            window.Width = 800; Click(window, "Tab_Macros"); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(Find<TextBox>(window, "ActionValue").Bounds.Width > 100);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InvalidDraftSurvivesAppearanceAndTabChangesAndCannotRun()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault()); var window = new MainWindow(state); window.Show();
        try
        {
            Click(window, "Tab_Macros"); Find<TextBox>(window, "ActionValue").Text = "not a point"; Click(window, "ApplyAction");
            Click(window, "Tab_Settings"); Find<ComboBox>(window, "Appearance").SelectedItem = "Dark";
            Click(window, "Tab_Macros");
            Assert.Equal("not a point", Find<TextBox>(window, "ActionValue").Text);
            Assert.Contains("X, Y", Find<TextBlock>(window, "ActionError").Text);
            Assert.False(Find<Button>(window, "RunSelected").IsEnabled);
            Assert.Equal("480, 640", state.Macro.Steps[0].Value);
            Click(window, "DiscardDraft");
            Assert.Equal("480, 640", Find<TextBox>(window, "ActionValue").Text);
            Assert.True(Find<Button>(window, "RunSelected").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovingStepShowsNewSelectedStepsValueInsteadOfOldDraft()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault()); state.Macro.Steps.Add(new("Click", "10, 20", 50));
        var window = new MainWindow(state); window.Show();
        try
        {
            Click(window, "Tab_Macros"); Assert.Equal("480, 640", Find<TextBox>(window, "ActionValue").Text);
            Click(window, "RemoveAction"); Assert.Equal("10, 20", Find<TextBox>(window, "ActionValue").Text);
            Assert.Single(state.Macro.Steps);
        }
        finally { window.Close(); }
    }
    private static T Find<T>(Window window, string name) where T : Control
    { window.UpdateLayout(); return window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name); }
    private static void Click(Window window, string name)
    { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout(); }
}
