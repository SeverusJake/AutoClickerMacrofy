using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Macrofy.Platform.Windows;

namespace Macrofy.CompatibilityProbe;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("This probe requires Windows."); return; }
        if (args.Contains("--list", StringComparer.Ordinal))
        {
            using var catalog = new WindowsWindowCatalog();
            foreach (var target in catalog.ListAsync().GetAwaiter().GetResult())
                Console.WriteLine($"{target.Title} | {target.App.ExecutablePath} | {(target.IsMinimized ? "minimized" : "not minimized")} | {target.Geometry}");
            return;
        }
        if (args.Length > 0 && !args.Contains("--interactive", StringComparer.Ordinal))
        { Console.WriteLine("Use --list (read only) or --interactive (explicit single-click tests)."); return; }
        AppBuilder.Configure<ProbeApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class ProbeApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var catalog = new WindowsWindowCatalog(); var player = new WindowsInputPlayer(catalog);
            var hotkey = new ProbeStopHotkey(); var window = new ProbeWindow(catalog, player, hotkey.Registered);
            hotkey.StopRequested += window.Stop; desktop.MainWindow = window;
            desktop.Exit += (_, _) => { hotkey.StopRequested -= window.Stop; hotkey.Dispose(); player.Dispose(); catalog.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
