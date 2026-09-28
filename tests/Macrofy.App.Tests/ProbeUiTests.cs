using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Macrofy.CompatibilityProbe;
using Macrofy.Platform.Windows;
using Xunit;
[assembly: AvaloniaTestApplication(typeof(Macrofy.App.Tests.TestAppBuilder))]
namespace Macrofy.App.Tests;
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
public class ProbeUiTests
{
    [AvaloniaFact]
    public void ProbeRequiresExplicitTargetAndPositionBeforeSending()
    {
        using var catalog=new WindowsWindowCatalog();using var player=new WindowsInputPlayer(catalog);
        var window=new ProbeWindow(catalog,player,true);window.Show();
        var controls=window.GetVisualDescendants().ToArray();
        Assert.False(controls.OfType<Button>().Single(b=>b.Name=="TestClick").IsEnabled);
        Assert.False(controls.OfType<Button>().Single(b=>b.Name=="ObservedWorking").IsEnabled);
        Assert.False(controls.OfType<Button>().Single(b=>b.Name=="ObservedIgnored").IsEnabled);
        Assert.Null(controls.OfType<TextBox>().Single(t=>t.Name=="ClientX").Text);
        Assert.Null(controls.OfType<TextBox>().Single(t=>t.Name=="ClientY").Text);
        Assert.Equal(-1,controls.OfType<ComboBox>().Single(c=>c.Name=="TargetPicker").SelectedIndex);
        window.Close();
    }
    [AvaloniaFact]
    public void MissingEmergencyHotkeyBlocksTest()
    {
        using var catalog=new WindowsWindowCatalog();using var player=new WindowsInputPlayer(catalog);
        var window=new ProbeWindow(catalog,player,false);window.Show();
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text!=null && t.Text.Contains("F10"));
        Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="TestClick").IsEnabled);
        window.Close();
    }
}
