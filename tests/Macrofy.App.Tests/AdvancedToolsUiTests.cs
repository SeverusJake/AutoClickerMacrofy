using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class AdvancedToolsUiTests
{
    [AvaloniaFact]
    public void CompatibilityTabHiddenUntilAdvancedToolsEnabled()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var window = HarmlessUi.Create(state); window.Show();
        try
        {
            Assert.False(state.Document.ShowAdvancedTools);
            Assert.DoesNotContain(TabNames(window), n => n == "Tab_Compatibility");
            Assert.Equal(6, TabNames(window).Count);
            PlaybackUiTests.Click(window, "Tab_Settings");
            PlaybackUiTests.Find<CheckBox>(window, "ShowAdvancedTools").IsChecked = true;
            Assert.True(state.Document.ShowAdvancedTools);
            Assert.Contains(TabNames(window), n => n == "Tab_Compatibility");
            PlaybackUiTests.Click(window, "Tab_Compatibility");
            PlaybackUiTests.Click(window, "Tab_Settings");
            PlaybackUiTests.Find<CheckBox>(window, "ShowAdvancedTools").IsChecked = false;
            Assert.False(state.Document.ShowAdvancedTools);
            Assert.DoesNotContain(TabNames(window), n => n == "Tab_Compatibility");
        }
        finally { window.Close(); }
    }

    private static List<string> TabNames(Window window)
    {
        window.UpdateLayout();
        return window.GetVisualDescendants().OfType<Button>().Select(b => b.Name).Where(n => n?.StartsWith("Tab_") == true).Select(n => n!).ToList();
    }
}
