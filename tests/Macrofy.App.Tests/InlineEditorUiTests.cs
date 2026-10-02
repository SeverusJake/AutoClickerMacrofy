using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.App.Views;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class InlineEditorUiTests
{
    [AvaloniaFact]
    public void ValidEditsSaveImmediatelyAndInvalidDraftBlocksRunUntilEsc()
    {
        var (state, window) = Open();
        try
        {
            var macro = state.Macro;
            PlaybackUiTests.Find<TextBox>(window, "StepX_0").Text = "30";
            Assert.Equal("30, 640", macro.Steps[0].Value);
            Assert.True(PlaybackUiTests.Find<Button>(window, "RunSelected").IsEnabled);
            PlaybackUiTests.Find<TextBox>(window, "StepX_0").Text = "x";
            Assert.Equal("30, 640", macro.Steps[0].Value);
            Assert.StartsWith("Step 1:", PlaybackUiTests.Find<TextBlock>(window, "ActionError").Text);
            Assert.False(PlaybackUiTests.Find<Button>(window, "RunSelected").IsEnabled);
            Press(PlaybackUiTests.Find<TextBox>(window, "StepX_0"), Key.Escape);
            Assert.Equal("30", PlaybackUiTests.Find<TextBox>(window, "StepX_0").Text);
            Assert.Equal("", PlaybackUiTests.Find<TextBlock>(window, "ActionError").Text);
            Assert.True(PlaybackUiTests.Find<Button>(window, "RunSelected").IsEnabled);
            PlaybackUiTests.Find<TextBox>(window, "StepDelay_0").Text = "250";
            Assert.Equal(250, macro.Steps[0].DelayMs);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void KindChangeAddDuplicateDeleteAndReorder()
    {
        var (state, window) = Open();
        try
        {
            var macro = state.Macro;
            PlaybackUiTests.Click(window, "Add_Wait");
            Assert.Equal(new MacroStep("Wait", "1000", 100), macro.Steps[1]);
            Assert.Equal("1000", PlaybackUiTests.Find<TextBox>(window, "StepValue_1").Text);
            PlaybackUiTests.Find<ComboBox>(window, "StepKind_1").SelectedItem = "Key";
            Assert.Equal(new MacroStep("Key", "Space", 100), macro.Steps[1]);
            PlaybackUiTests.Click(window, "StepDuplicate_0");
            Assert.Equal(3, macro.Steps.Count); Assert.Equal(macro.Steps[0], macro.Steps[1]);
            PlaybackUiTests.Find<TextBox>(window, "StepX_1").Text = "1";
            Press(PlaybackUiTests.Find<TextBox>(window, "StepX_1"), Key.Down, KeyModifiers.Alt);
            Assert.Equal(new[] { "480, 640", "Space", "1, 640" }, macro.Steps.Select(s => s.Value));
            Press(PlaybackUiTests.Find<TextBox>(window, "StepX_2"), Key.Up, KeyModifiers.Alt);
            Assert.Equal(new[] { "480, 640", "1, 640", "Space" }, macro.Steps.Select(s => s.Value));
            PlaybackUiTests.Click(window, "StepDelete_0");
            Assert.Equal(new[] { "1, 640", "Space" }, macro.Steps.Select(s => s.Value));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LoopTimesIntervalAndCoordinatesEditMacro()
    {
        var (state, window) = Open();
        try
        {
            var macro = state.Macro; macro.Repeat = 1;
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Tab_Macros");
            var loop = PlaybackUiTests.Find<ToggleSwitch>(window, "MacroLoop");
            Assert.False(loop.IsChecked); Assert.False(PlaybackUiTests.Find<TextBox>(window, "MacroTimes").IsVisible);
            loop.IsChecked = true; Assert.Equal(0, macro.Repeat);
            var times = PlaybackUiTests.Find<TextBox>(window, "MacroTimes");
            Assert.True(times.IsVisible);
            times.Text = "100"; Assert.Equal(100, macro.Repeat);
            times.Text = "abc"; Assert.Equal(100, macro.Repeat);
            times.Text = ""; Assert.Equal(0, macro.Repeat);
            PlaybackUiTests.Find<ToggleSwitch>(window, "MacroLoop").IsChecked = false; Assert.Equal(1, macro.Repeat);
            PlaybackUiTests.Find<NumericUpDown>(window, "MacroInterval").Value = 1.5m; Assert.Equal(1500, macro.IntervalMs);
            PlaybackUiTests.Click(window, "Coord_Percent");
            Assert.Equal("Percentage", macro.Coordinates);
            Assert.Contains("0–100", PlaybackUiTests.Find<TextBlock>(window, "ActionError").Text);
            PlaybackUiTests.Click(window, "Coord_Pixels");
            Assert.Equal("Fixed pixels", macro.Coordinates);
            Assert.Equal("", PlaybackUiTests.Find<TextBlock>(window, "ActionError").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ActionColorsDifferPerKind()
    {
        var (_, window) = Open();
        try
        {
            static Color Of(IBrush? brush) => Assert.IsType<SolidColorBrush>(brush).Color;
            var click = Of(PlaybackUiTests.Find<Button>(window, "Add_Click").Background);
            Assert.NotEqual(click, Of(PlaybackUiTests.Find<Button>(window, "Add_Key").Background));
            Assert.NotEqual(Of(PlaybackUiTests.Find<Button>(window, "Add_Wait").Background), Of(PlaybackUiTests.Find<Button>(window, "Add_Text").Background));
            Assert.Equal(click, Of(PlaybackUiTests.Find<ComboBox>(window, "StepKind_0").Background));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DragHandleMovesRowAndWaitValueCellIsMs()
    {
        var (state, window) = Open();
        try
        {
            var macro = state.Macro;
            PlaybackUiTests.Click(window, "Add_Wait"); PlaybackUiTests.Click(window, "Add_Key");
            Assert.Equal(new[] { "Click", "Wait", "Key" }, macro.Steps.Select(s => s.Kind));
            var handle = PlaybackUiTests.Find<Control>(window, "StepHandle_0");
            var target = PlaybackUiTests.Find<Control>(window, "StepHandle_2");
            var from = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
            var to = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to);
            window.MouseUp(to, MouseButton.Left);
            Assert.Equal(new[] { "Wait", "Key", "Click" }, macro.Steps.Select(s => s.Kind));
            Assert.Equal("1000", PlaybackUiTests.Find<TextBox>(window, "StepValue_0").Text);
        }
        finally { window.Close(); }
    }

    private static (WorkspaceState State, MainWindow Window) Open()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var window = HarmlessUi.Create(state); window.Width = 1200; window.Show();
        PlaybackUiTests.Click(window, "Tab_Macros");
        return (state, window);
    }

    internal static void Press(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = control });
}
