using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Macrofy.Platform;
using Macrofy.Platform.Models;
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
    public async Task ChangingTargetOrSurfaceRequiresFreshCoordinates()
    {
        using var catalog=new WindowsWindowCatalog(new UiWindows());
        var window=new ProbeWindow(catalog,new UiPlayer(),true);window.Show();
        var target=Find<ComboBox>(window,"TargetPicker");target.SelectedIndex=0;
        Find<TextBox>(window,"ClientX").Text="20";Find<TextBox>(window,"ClientY").Text="30";
        await UntilAsync(()=>Find<Button>(window,"TestClick").IsEnabled);
        target.SelectedIndex=1;
        Assert.True(string.IsNullOrEmpty(Find<TextBox>(window,"ClientX").Text));
        Assert.True(string.IsNullOrEmpty(Find<TextBox>(window,"ClientY").Text));
        Assert.False(Find<Button>(window,"TestClick").IsEnabled);
        Find<TextBox>(window,"ClientX").Text="20";Find<TextBox>(window,"ClientY").Text="30";
        Find<ComboBox>(window,"SurfacePicker").SelectedIndex=1;
        Assert.True(string.IsNullOrEmpty(Find<TextBox>(window,"ClientX").Text));
        Assert.False(Find<Button>(window,"TestClick").IsEnabled);window.Close();
    }
    [AvaloniaFact]
    public async Task ConfirmationBlocksNewInputAndCloseWaitsForSave()
    {
        using var catalog=new WindowsWindowCatalog(new UiWindows());
        var started=new TaskCompletionSource();var saved=new TaskCompletionSource<string>();
        var window=new ProbeWindow(catalog,new UiPlayer(),true,_=>{started.SetResult();return saved.Task;});window.Show();
        Find<ComboBox>(window,"TargetPicker").SelectedIndex=0;
        Find<ComboBox>(window,"StatePicker").SelectedIndex=3;
        Find<TextBox>(window,"ClientX").Text="20";Find<TextBox>(window,"ClientY").Text="30";
        Find<Button>(window,"TestClick").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(()=>Find<Button>(window,"ObservedWorking").IsEnabled);
        Find<Button>(window,"ObservedWorking").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.False(Find<Button>(window,"TestClick").IsEnabled);
            Assert.False(Find<ComboBox>(window,"TargetPicker").IsEnabled);
            window.Close();Assert.True(window.IsVisible);
        }
        finally {saved.TrySetResult("test-results.json");}
        await UntilAsync(()=>!window.IsVisible);
    }
    private static T Find<T>(Window window,string name) where T:Control=>window.GetVisualDescendants().OfType<T>().Single(c=>c.Name==name);
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while(!condition())await Task.Delay(10,timeout.Token);
    }
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
internal sealed class UiWindows : IWindowNative
{
    private static NativeWindow Window(int top,int surface)=>new(top,surface,42,1,@"C:\test.exe","Test "+top,"Surface "+surface,new(800,600,1),false,false,"v1");
    public int CurrentProcessId=>999;
    public event Action<nint>? Destroyed {add{} remove{}}
    public IReadOnlyList<NativeWindow> EnumerateWindows()=>[Window(11,11),Window(12,12)];
    public NativeWindow? ReadWindow(nint top,nint surface)=>Window((int)top,(int)surface);
    public IReadOnlyList<NativeWindow> ReadSurfaces(NativeWindow window)=>[window,Window((int)window.TopHandle,(int)window.TopHandle+100)];
    public void Dispose(){}
}
internal sealed class UiPlayer : IInputPlayer
{
    public ValueTask<DeliveryResult> SendAsync(TargetToken target,InputCommand command,CancellationToken cancellationToken=default)=>ValueTask.FromResult(new DeliveryResult(true));
    public ValueTask<DeliveryResult> ReleaseHeldAsync(TargetToken target,CancellationToken cancellationToken=default)=>ValueTask.FromResult(new DeliveryResult(true));
}
