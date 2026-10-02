using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class RecordPointUiTests
{
    [AvaloniaFact]
    public void SettingsShowsAndChangesRecordKey()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var keys = new HarmlessHotkeys();
        var window = new Macrofy.App.Views.MainWindow(state, new UiPlayback(state.Document), keys); window.Show();
        try
        {
            PlaybackUiTests.Click(window, "Tab_Settings");
            var input = PlaybackUiTests.Find<TextBox>(window, "Shortcut_Capture");
            Assert.Equal("F7", input.Text);
            input.Focus(); keys.Trigger(HotkeyCommand.Run);
            Assert.Equal("F7", state.Document.Shortcuts.Capture);
            window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.F6, Source = input });
            Assert.Equal("F6", state.Document.Shortcuts.Capture);
            Assert.Null(keys.Last!.Capture);
        }
        finally { window.Close(); }
    }
}
