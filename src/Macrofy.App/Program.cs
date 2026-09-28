using Avalonia;
using Avalonia.Controls;
namespace Macrofy.App;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<FoundationApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}
internal sealed class FoundationApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Window { Title = "Macrofy — foundation", Width = 500, Height = 180,
                Content = new TextBlock { Text = "Macrofy foundation. Recording and playback UI are not implemented. Use CompatibilityProbe for target tests.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(20) } };
        base.OnFrameworkInitializationCompleted();
    }
}
