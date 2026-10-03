using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.App.Views;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class KeyPickerUiTests
{
    [AvaloniaFact]
    public void DropdownSavesOneKey()
    {
        var (state, window, _) = Open();
        try
        {
            Assert.Equal("Space", PlaybackUiTests.Find<ComboBox>(window, "StepKey_0").SelectedItem);
            PlaybackUiTests.Find<ComboBox>(window, "StepKey_0").SelectedItem = "Q";
            Assert.Equal("Q", state.Macro.Steps[0].Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RecordKeyCapturesOneKeyIncludingAModifier()
    {
        var (state, window, _) = Open();
        try
        {
            PlaybackUiTests.Click(window, "StepRecordKey_0");
            Assert.Equal("Cancel", PlaybackUiTests.Find<Button>(window, "StepRecordKey_0").Content);
            Assert.Equal("Press a key.", Status(window));
            Press(window, Key.Q);
            Assert.Equal("Q", state.Macro.Steps[0].Value);
            Assert.Equal("Record key", PlaybackUiTests.Find<Button>(window, "StepRecordKey_0").Content);
            PlaybackUiTests.Click(window, "StepRecordKey_0"); Press(window, Key.LeftShift, KeyModifiers.Shift);
            Assert.Equal("Shift", state.Macro.Steps[0].Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RecordComboWaitsThroughModifiersAndSavesTheCombination()
    {
        var (state, window, _) = Open();
        try
        {
            PlaybackUiTests.Click(window, "StepRecordCombo_1");
            Assert.Equal("Hold modifiers and press a key.", Status(window));
            Press(window, Key.LeftCtrl, KeyModifiers.Control);
            Press(window, Key.S);
            Assert.Equal("Ctrl + C", state.Macro.Steps[1].Value);
            Assert.Equal("Hold Ctrl, Shift or Alt, then press a key.", Status(window));
            Press(window, Key.S, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.Equal(new MacroStep("Combo key", "Ctrl + Shift + S", 0), state.Macro.Steps[1]);
            Assert.Equal("Ctrl + Shift + S", PlaybackUiTests.Find<TextBlock>(window, "StepCombo_1").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReservedKeysAreReportedAndCancelKeepsTheValue()
    {
        var (state, window, keys) = Open();
        try
        {
            PlaybackUiTests.Click(window, "StepRecordKey_0");
            Press(window, Key.F10);
            Assert.Equal("F10 is reserved for Macrofy.", Status(window));
            keys.Trigger(HotkeyCommand.Run);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("F9 is reserved for Macrofy.", Status(window));
            Assert.Equal("Cancel", PlaybackUiTests.Find<Button>(window, "StepRecordKey_0").Content);
            PlaybackUiTests.Click(window, "StepRecordKey_0");
            Assert.Equal("Record key", PlaybackUiTests.Find<Button>(window, "StepRecordKey_0").Content);
            Assert.Equal("Space", state.Macro.Steps[0].Value);
            Assert.Empty(state.Sessions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ComboKeyAddButtonAndNoDescriptions()
    {
        var (state, window, _) = Open();
        try
        {
            PlaybackUiTests.Click(window, "Add_ComboKey");
            Assert.Equal(new MacroStep("Combo key", "Ctrl + C", 100), state.Macro.Steps[^1]);
            window.UpdateLayout();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "ActionWarning" or "ActionHelp" or "MacroModeNote");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RecordButtonsSitRightBeforeWaitAfter()
    {
        var (_, window, _) = Open();
        try
        {
            foreach (var (button, row) in new[] { ("StepRecordKey_0", 0), ("StepRecordCombo_1", 1) })
            {
                var record = PlaybackUiTests.Find<Button>(window, button);
                var delay = PlaybackUiTests.Find<TextBox>(window, "StepDelay_" + row);
                var recordRight = record.TranslatePoint(new Avalonia.Point(record.Bounds.Width, 0), window)!.Value.X;
                var delayLeft = delay.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X;
                Assert.InRange(delayLeft - recordRight, 0, 24);
            }
        }
        finally { window.Close(); }
    }

    private static string? Status(Window window) => PlaybackUiTests.Find<TextBlock>(window, "RecordStatus").Text;
    private static void Press(Window window, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = window });

    private static (WorkspaceState State, MainWindow Window, HarmlessHotkeys Keys) Open()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        state.Macro.Steps = [new("Key", "Space", 0), new("Combo key", "Ctrl + C", 0)];
        var keys = new HarmlessHotkeys();
        var window = new MainWindow(state, new UiPlayback(state.Document), keys); window.Show();
        PlaybackUiTests.Click(window, "Tab_Macros");
        return (state, window, keys);
    }
}
