using Avalonia;
using Avalonia.Themes.Fluent;
using Macrofy.App.Views;
namespace Macrofy.App;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<MacrofyApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}
internal sealed class MacrofyApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
