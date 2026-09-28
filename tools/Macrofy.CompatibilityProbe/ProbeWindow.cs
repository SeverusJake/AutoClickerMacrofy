using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows;

namespace Macrofy.CompatibilityProbe;

public sealed class ProbeWindow : Window
{
    private readonly WindowsWindowCatalog catalog;
    private readonly ClickProbeSession session;
    private readonly bool stopRegistered;
    private readonly ComboBox targets = new() { Name = "TargetPicker", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox surfaces = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox states = new() { ItemsSource = new[] { "Minimized", "Background — fully covered", "Background — partly covered", "Background — visible" }, SelectedIndex = 0 };
    private readonly TextBox x = new() { Name = "ClientX", PlaceholderText = "X in client pixels", Width = 200 };
    private readonly TextBox y = new() { Name = "ClientY", PlaceholderText = "Y in client pixels", Width = 200 };
    private readonly TextBlock status = Text("Select a target and a harmless client position. Input starts only with Test one click.");
    private readonly TextBlock details = Text("");
    private readonly Button test = new() { Name = "TestClick", Content = "Test one click", IsEnabled = false };
    private readonly Button observed = new() { Name = "ObservedWorking", Content = "Observed working", IsEnabled = false };
    private readonly Button ignored = new() { Name = "ObservedIgnored", Content = "Observed ignored", IsEnabled = false };
    private readonly Button stop = new() { Content = "Stop (F10)", IsEnabled = false };
    private readonly Button refresh = new() { Content = "Refresh windows" };
    private readonly Button capture = new() { Content = "Capture pointer position in 5 seconds", IsEnabled = false };
    private IReadOnlyList<TargetWindow> listed = [];
    private IReadOnlyList<InputSurfaceOption> options = [];
    private ProbeAttempt? attempt;
    private CancellationTokenSource? cancellation;
    private Task? active;
    private bool busy;

    public ProbeWindow(WindowsWindowCatalog catalog, WindowsInputPlayer player, bool stopRegistered)
    {
        this.catalog = catalog; this.stopRegistered = stopRegistered; session = new(catalog, player);
        Title = "Macrofy — CookieRun compatibility probe"; Width = 680; Height = 750; MinWidth = 500; MinHeight = 540;
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = "Test background clicks", FontSize = 22 });
        body.Children.Add(Text("Select the game while restored so geometry can be read. Choose a harmless client position. Then minimize the game or put it behind another app and choose that state."));
        body.Children.Add(Text("Target window")); body.Children.Add(targets); body.Children.Add(refresh);
        body.Children.Add(Text("Input surface — try main window or another surface if the game ignores a queued click")); body.Children.Add(surfaces); body.Children.Add(details);
        body.Children.Add(Text("Client position (relative to selected input surface)")); body.Children.Add(Row(x, y));
        body.Children.Add(capture);
        body.Children.Add(Text("State to test — change game state yourself")); body.Children.Add(states);
        body.Children.Add(Row(test, stop)); body.Children.Add(status);
        body.Children.Add(Text("Queued means Windows accepted messages. Confirm only what you observed in the game.")); body.Children.Add(Row(observed, ignored));
        body.Children.Add(Text("CookieRun needs clicks only. Minimized is preferred; background is the fallback. If neither works, pause and review options."));
        body.Children.Add(Text("Stop prevents future posts and attempts release for up to 500ms. Already-posted messages may execute. F10 must be available."));
        Content = new ScrollViewer { Content = body };
        if (!stopRegistered) status.Text = "F10 emergency stop could not register. Test clicks are disabled. Release the other app's F10 binding, then reopen this probe.";
        refresh.Click += async (_, _) => await RefreshAsync();
        capture.Click += async (_, _) => { active = CaptureAsync(); await active; };
        targets.SelectionChanged += async (_, _) => await SelectTargetAsync();
        surfaces.SelectionChanged += (_, _) => { ClearAttempt(); UpdateControls(); UpdateDetails(); };
        states.SelectionChanged += (_, _) => { ClearAttempt(); UpdateControls(); };
        x.TextChanged += (_, _) => { ClearAttempt(); UpdateControls(); };
        y.TextChanged += (_, _) => { ClearAttempt(); UpdateControls(); };
        test.Click += async (_, _) => { active = TestAsync(); await active; };
        stop.Click += (_, _) => Stop();
        observed.Click += async (_, _) => await ConfirmAsync(true);
        ignored.Click += async (_, _) => await ConfirmAsync(false);
        Opened += async (_, _) => await RefreshAsync();
        Closing += async (_, e) => { if (active is { IsCompleted: false } running) { e.Cancel = true; Stop(); await running; Close(); } };
    }
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var control in controls) row.Children.Add(control); return row;
    }
    private TargetWindow? Selected => surfaces.SelectedIndex >= 0 && surfaces.SelectedIndex < options.Count ? options[surfaces.SelectedIndex].Window : null;
    private TargetState State => states.SelectedIndex switch { 0 => TargetState.Minimized, 1 => TargetState.BackgroundCovered, 2 => TargetState.BackgroundPartlyCovered, _ => TargetState.BackgroundVisible };
    private bool TryPoint(out PointerPoint point)
    {
        point = default;
        if (!double.TryParse(x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) || !double.TryParse(y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var py) || !double.IsFinite(px) || !double.IsFinite(py)) return false;
        point = new(px, py); return px >= 0 && py >= 0;
    }
    private void ClearAttempt() { attempt = null; observed.IsEnabled = ignored.IsEnabled = false; }
    private void UpdateControls()
    {
        test.IsEnabled = stopRegistered && !busy && Selected is not null && TryPoint(out _);
        targets.IsEnabled = surfaces.IsEnabled = states.IsEnabled = refresh.IsEnabled = x.IsEnabled = y.IsEnabled = !busy;
        stop.IsEnabled = busy;
        capture.IsEnabled = !busy && Selected is not null;
    }
    private void UpdateDetails()
    {
        var window = Selected;
        details.Text = window is null ? "" : $"{window.App.ExecutablePath}\nClient: {(window.Geometry is { } g ? $"{g.Width} × {g.Height}, DPI scale {g.DpiScale:0.##}" : "unknown — restore and refresh before minimizing")}";
    }
    private async Task RefreshAsync()
    {
        try
        {
            ClearAttempt(); listed = await catalog.ListAsync();
            targets.ItemsSource = listed.Select(w => $"{w.Title} — {w.App.Name}{(w.IsMinimized ? " [minimized]" : "")}").ToArray();
            targets.SelectedIndex = -1; options = []; surfaces.ItemsSource = Array.Empty<string>(); UpdateControls(); UpdateDetails();
        }
        catch (Exception e) { status.Text = "Window discovery failed: " + e.Message; }
    }
    private async Task SelectTargetAsync()
    {
        ClearAttempt(); options = []; surfaces.ItemsSource = Array.Empty<string>();
        if (targets.SelectedIndex >= 0 && targets.SelectedIndex < listed.Count)
        {
            var target = listed[targets.SelectedIndex]; options = await catalog.ListInputSurfacesAsync(target.Token);
            surfaces.ItemsSource = options.Select(o => $"{(o.IsTopLevel ? "Main window" : "Child surface")} — {o.ClassName}").ToArray();
            surfaces.SelectedIndex = options.Select((o, i) => (o, i)).FirstOrDefault(p => p.o.Window.Token == target.Token).i;
        }
        UpdateControls(); UpdateDetails();
    }
    private async Task TestAsync()
    {
        if (Selected is not { } target || !TryPoint(out var point) || busy || !stopRegistered) return;
        ClearAttempt(); busy = true; UpdateControls(); cancellation = new(); status.Text = "Testing one left click… F10 stops future input.";
        try
        {
            attempt = await session.TestAsync(target.Token, point, State, cancellation.Token);
            status.Text = attempt.CanConfirm ? "Queued: mouse down and up. Restore the game if needed to observe, then confirm working or ignored." : $"Test stopped: {attempt.Error?.Message ?? "delivery incomplete"}{(attempt.CleanupError is { } error ? " Cleanup: " + error.Message : "")}";
            observed.IsEnabled = ignored.IsEnabled = attempt.CanConfirm;
        }
        catch (Exception e) { status.Text = "Test error: " + e.Message; }
        finally { cancellation.Dispose(); cancellation = null; busy = false; UpdateControls(); }
    }
    private async Task CaptureAsync()
    {
        if (Selected is not { } target || busy) return;
        ClearAttempt(); busy = true; cancellation = new(); UpdateControls();
        try
        {
            for (var seconds = 5; seconds > 0; seconds--)
            {
                status.Text = $"Hover over a harmless point in the restored game. Position captured in {seconds}s. No click will be sent.";
                await Task.Delay(1000, cancellation.Token);
            }
            var result = catalog.ReadPointerPosition(target.Token);
            if (result.Point is { } point)
            {
                x.Text = point.X.ToString(CultureInfo.InvariantCulture); y.Text = point.Y.ToString(CultureInfo.InvariantCulture);
                status.Text = $"Position captured: ({point.X}, {point.Y}). Minimize or cover the game, choose state, then Test one click.";
            }
            else status.Text = result.Error!.Message;
        }
        catch (OperationCanceledException) { status.Text = "Position capture cancelled."; }
        finally { cancellation.Dispose(); cancellation = null; busy = false; UpdateControls(); }
    }
    private async Task ConfirmAsync(bool success)
    {
        if (attempt is not { CanConfirm: true } current || busy) return;
        observed.IsEnabled = ignored.IsEnabled = false;
        try
        {
            var confirmation = await session.ConfirmAsync(current, success);
            var folder = Path.Combine(AppContext.BaseDirectory, "MacrofyData"); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "compatibility-probe-results.json");
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            var results = File.Exists(path) ? JsonSerializer.Deserialize<List<ProbeConfirmation>>(await File.ReadAllTextAsync(path), jsonOptions) ?? [] : [];
            results.Add(confirmation); if (results.Count > 100) results.RemoveRange(0, results.Count - 100);
            var temporary = path + ".tmp"; await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(results, jsonOptions)); File.Move(temporary, path, true);
            status.Text = $"Observed {(success ? "working" : "ignored")} for {current.State}. Saved: {path}"; ClearAttempt();
        }
        catch (Exception e) { status.Text = "Confirmation was not saved: " + e.Message; observed.IsEnabled = ignored.IsEnabled = true; }
    }
    public void Stop() => Dispatcher.UIThread.Post(() => cancellation?.Cancel());
}
