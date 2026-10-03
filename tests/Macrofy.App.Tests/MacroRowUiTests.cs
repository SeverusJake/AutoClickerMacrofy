using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class MacroRowUiTests
{
    [AvaloniaFact]
    public void RowLoopToggleAndIntervalSecondsEditMacro()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var macro = state.Profile.Macros[0]; macro.Repeat = 1; macro.IntervalMs = 1000;
        var window = HarmlessUi.Create(state); window.Show();
        try
        {
            var id = macro.Id.ToString("N");
            var loop = PlaybackUiTests.Find<ToggleSwitch>(window, "Loop_" + id);
            var interval = PlaybackUiTests.Find<NumericUpDown>(window, "Interval_" + id);
            Assert.False(loop.IsChecked); Assert.Equal(1m, interval.Value);
            loop.IsChecked = true; Assert.Equal(0, macro.Repeat);
            loop.IsChecked = false; Assert.Equal(1, macro.Repeat);
            interval.Value = 2.5m; Assert.Equal(2500, macro.IntervalMs);
            macro.Repeat = 100; PlaybackUiTests.Click(window, "Tab_Settings"); PlaybackUiTests.Click(window, "Tab_Profiles");
            Assert.True(PlaybackUiTests.Find<ToggleSwitch>(window, "Loop_" + id).IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RowTimesBoxSetsRepeatCount()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var macro = state.Profile.Macros[0]; macro.Repeat = 1;
        var window = HarmlessUi.Create(state); window.Show();
        try
        {
            var id = macro.Id.ToString("N");
            var times = PlaybackUiTests.Find<TextBox>(window, "Times_" + id);
            Assert.False(times.IsVisible);
            PlaybackUiTests.Find<ToggleSwitch>(window, "Loop_" + id).IsChecked = true;
            Assert.True(times.IsVisible); Assert.Equal(0, macro.Repeat);
            times.Text = "25"; Assert.Equal(25, macro.Repeat);
            times.Text = "1"; Assert.Equal(25, macro.Repeat);
            times.Text = ""; Assert.Equal(0, macro.Repeat);
            macro.Repeat = 100; PlaybackUiTests.Click(window, "Tab_Settings"); PlaybackUiTests.Click(window, "Tab_Profiles");
            Assert.Equal("100", PlaybackUiTests.Find<TextBox>(window, "Times_" + id).Text);
        }
        finally { window.Close(); }
    }
}
