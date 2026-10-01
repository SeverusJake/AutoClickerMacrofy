using System.Globalization;
using Avalonia.Controls;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.Core.Actions;
using Macrofy.Platform.Models;
namespace Macrofy.App.Views;
public sealed partial class MainWindow
{
    private CancellationTokenSource? compatibilityCancellation;
    private CompatibilityAttempt? compatibilityAttempt;
    private bool compatibilityBusy;
    private Task compatibilityWork = Task.CompletedTask;
    private Control? compatibilityPane;
    private Guid compatibilityProfile;
    private Action? refreshCompatibility;
    private Guid? compatibilitySelectionApp;
    private void ResetCompatibilityContext()
    {
        DiscardCompatibility();
        if (compatibilitySelectionApp is { } appId) playback.SelectSurface(appId, null);
        compatibilitySelectionApp = null; compatibilityPane = null; refreshCompatibility = null; compatibilityProfile = Guid.Empty;
    }
    private bool CompatibilityLocked => compatibilityBusy || compatibilityAttempt is not null;
    private void CancelCompatibilityTest()
    {
        try { Volatile.Read(ref compatibilityCancellation)?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
    private void DiscardCompatibility()
    {
        compatibilityAttempt?.Dispose(); compatibilityAttempt = null;
        refreshCompatibility?.Invoke();
    }
    private Control CompatibilityPane()
    {
        if (compatibility is null) return Panel(Text("Window compatibility testing is unavailable on this platform."));
        if (compatibilityPane is not null && compatibilityProfile == Workspace.Profile.Id)
        { if (compatibilityPane.Parent is Avalonia.Controls.Panel parent) parent.Children.Remove(compatibilityPane); if (refreshCompatibility is not null) refreshPlayback.Add(refreshCompatibility); return compatibilityPane; }
        DiscardCompatibility(); compatibilityProfile = Workspace.Profile.Id;
        var app = new ComboBox { Name = "CompatibilityApp", ItemsSource = Workspace.Profile.Apps, SelectedIndex = -1, MinWidth = 220 };
        var window = new ComboBox { Name = "CompatibilityWindow", SelectedIndex = -1, MinWidth = 220 };
        var surface = new ComboBox { Name = "CompatibilitySurface", SelectedIndex = -1, MinWidth = 220 };
        var state = new ComboBox { Name = "CompatibilityState", ItemsSource = Enum.GetValues<TargetState>(), SelectedIndex = -1, MinWidth = 220 };
        var action = new ComboBox { Name = "CompatibilityAction", ItemsSource = new[] { "Click", "Key", "Shortcut", "Text", "Wheel" }, SelectedIndex = -1, MinWidth = 140 };
        var value = new TextBox { Name = "CompatibilityValue", Text = "", MinWidth = 220, PlaceholderText = "Harmless test value / client X, Y" };
        var status = Wrap("Select an app, live window, input surface, state and harmless action. Nothing is sent automatically.", "muted", 13); status.Name = "CompatibilityStatus";
        var actionHelp = Wrap("", "muted", 12);
        var capture = TextButton("Capture client point in 5 seconds", () => { }); capture.Name = "CaptureCompatibilityPoint";
        var test = TextButton("Test one action", () => { }); test.Name = "TestCompatibilityAction";
        var yes = TextButton("Observed working", () => { }); yes.Name = "ObservedWorking";
        var no = TextButton("Observed ignored", () => { }); no.Name = "ObservedIgnored";
        var discard = TextButton("Discard test", DiscardCompatibility); discard.Name = "DiscardCompatibility";
        var generation = 0;
        Guid? selectedAppId = null;
        bool TryAction(out CompiledAction? compiled, out string error)
        {
            compiled = null; error = "Choose an action and enter its value.";
            if (action.SelectedItem is not string kind || string.IsNullOrEmpty(value.Text)) return false;
            if (!ActionCompiler.TryCompile(new(kind == "Shortcut" ? "Key" : kind, value.Text, 0), CoordinateMode.FixedPixels,
                new HashSet<string>([Workspace.Document.Shortcuts.Run, Workspace.Document.Shortcuts.Pause, Workspace.Document.Shortcuts.Stop]), out compiled, out error)) return false;
            if (compiled is CompiledAction.Key key && ((kind == "Shortcut") != (key.Keys.Count > 1)))
            { error = kind == "Shortcut" ? "Enter a chord with more than one key." : "Choose Shortcut for a multi-key chord."; return false; }
            return true;
        }
        void UpdateControls()
        {
            actionHelp.Text = ActionHelp(action.SelectedItem as string);
            var locked = CompatibilityLocked || closing;
            foreach (var control in new Control[] { app, window, surface, state, action, value }) control.IsEnabled = !locked;
            var ready = !locked && Workspace.ActiveCount == 0 && playback.HotkeysReady;
            capture.IsEnabled = ready && surface.SelectedItem is WindowOption && action.SelectedItem as string == "Click";
            var valid = TryAction(out _, out var problem);
            test.IsEnabled = ready && app.SelectedItem is SavedApp && surface.SelectedItem is WindowOption && state.SelectedItem is TargetState && valid;
            ToolTip.SetTip(test, !playback.HotkeysReady ? playback.HotkeyError?.Message ?? "Global emergency Stop unavailable." : valid ? "Send exactly one selected action; observe the app yourself." : problem);
            yes.IsEnabled = no.IsEnabled = !compatibilityBusy && compatibilityAttempt is not null && !closing;
            discard.IsEnabled = compatibilityAttempt is not null && !compatibilityBusy;
        }
        refreshCompatibility = UpdateControls; refreshPlayback.Add(UpdateControls);
        void ClearObservation()
        {
            DiscardCompatibility(); value.Text = ""; status.Text = "Selection changed. Enter or capture a fresh test value."; UpdateControls();
        }
        app.SelectionChanged += async (_, _) =>
        {
            if (CompatibilityLocked) return;
            if (selectedAppId is { } previous) playback.SelectSurface(previous, null);
            selectedAppId = (app.SelectedItem as SavedApp)?.Id; compatibilitySelectionApp = selectedAppId;
            ClearObservation(); window.ItemsSource = null; surface.ItemsSource = null; var version = ++generation;
            if (app.SelectedItem is not SavedApp selected) return;
            try
            {
                var windows = await compatibility.Catalog.ListAsync();
                if (version != generation) return;
                window.ItemsSource = windows.Where(w => TitleRule.MatchesWindow(selected.Executable, selected.TitleRule, w)).Select(w => new WindowOption(w)).ToArray();
            }
            catch (Exception error) { status.Text = "Cannot list windows: " + error.Message; }
            UpdateControls();
        };
        window.SelectionChanged += async (_, _) =>
        {
            if (CompatibilityLocked) return;
            if (selectedAppId is { } id) playback.SelectSurface(id, null);
            var version = ++generation; ClearObservation(); surface.ItemsSource = null;
            if (window.SelectedItem is not WindowOption selected) return;
            try
            {
                var surfaces = await compatibility.ListSurfaces(selected.Window.Token, CancellationToken.None);
                if (version == generation) surface.ItemsSource = surfaces.Select(w => new WindowOption(w)).ToArray();
            }
            catch (Exception error) { status.Text = "Cannot list surfaces: " + error.Message; }
            UpdateControls();
        };
        surface.SelectionChanged += (_, _) =>
        {
            if (CompatibilityLocked) return;
            ClearObservation();
            if (selectedAppId is { } id) playback.SelectSurface(id, (surface.SelectedItem as WindowOption)?.Window.Token);
        };
        state.SelectionChanged += (_, _) => { if (!CompatibilityLocked) ClearObservation(); };
        action.SelectionChanged += (_, _) => { if (!CompatibilityLocked) ClearObservation(); };
        value.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) UpdateControls(); };
        async Task RunOperation(Func<CancellationToken, Task> operation)
        {
            compatibilityBusy = true;
            using var cancellation = new CancellationTokenSource(); compatibilityCancellation = cancellation;
            RefreshPlayback();
            try { await operation(cancellation.Token); }
            catch (OperationCanceledException) { status.Text = "Test cancelled."; }
            catch (Exception error) { status.Text = "Test stopped: " + error.Message; }
            finally { compatibilityCancellation = null; compatibilityBusy = false; RefreshPlayback(); }
        }
        capture.Click += (_, _) =>
        {
            if (!capture.IsEnabled || surface.SelectedItem is not WindowOption selected) return;
            compatibilityWork = RunOperation(async ct =>
            {
                for (var seconds = 5; seconds > 0; seconds--) { status.Text = $"Restore the selected window and hover over a harmless client point. Capture in {seconds}s; no input sent."; await Task.Delay(1000, ct); }
                var point = compatibility.ReadPointer(selected.Window.Token);
                if (point.Point is null) throw new InvalidOperationException(point.Error?.Message ?? "Pointer unavailable.");
                value.Text = string.Create(CultureInfo.InvariantCulture, $"{point.Point.Value.X}, {point.Point.Value.Y}");
                status.Text = "Point captured. Arrange the window in the selected state before testing.";
            });
        };
        test.Click += (_, _) =>
        {
            if (!test.IsEnabled || app.SelectedItem is not SavedApp selectedApp || surface.SelectedItem is not WindowOption selected || state.SelectedItem is not TargetState selectedState || !TryAction(out var compiled, out _)) return;
            compatibilityWork = RunOperation(async ct =>
            {
                var attempt = await compatibility.Service.BeginAttemptAsync(selectedApp.Id, selected.Window.Token, selectedState, compiled!, ct);
                if (ct.IsCancellationRequested) { attempt.Dispose(); ct.ThrowIfCancellationRequested(); }
                var result = attempt.Result;
                if (!result.Delivery.Queued || result.Delivery.Error is not null || result.Delivery.CleanupError is not null || result.CleanupError is not null)
                {
                    attempt.Dispose(); status.Text = "Test failed: " + (result.Delivery.Error ?? result.CleanupError ?? result.Delivery.CleanupError)?.Message; return;
                }
                compatibilityAttempt = attempt;
                status.Text = $"One {attempt.Capability} action sent and cleaned up. Restore if minimized, then explicitly report what happened. No observation has been saved.";
            });
        };
        void ConfirmObservation(bool worked)
        {
            if (compatibilityBusy || compatibilityAttempt is not { } attempt) return;
            compatibilityWork = RunOperation(async ct =>
            {
                var evidence = await compatibility.Service.ConfirmAsync(attempt, worked, ct);
                if (compatibilityAttempt == attempt) compatibilityAttempt = null;
                attempt.Dispose();
                status.Text = $"Saved: {evidence.Capability} / {evidence.State} — {(worked ? "Observed working" : "Observed ignored")}. Other actions and states remain unconfirmed.";
            });
        }
        yes.Click += (_, _) => { if (yes.IsEnabled) ConfirmObservation(true); };
        no.Click += (_, _) => { if (no.IsEnabled) ConfirmObservation(false); };
        compatibilityPane = Panel(Scroll(Stack(Text("Window compatibility", size: 16),
            Wrap("Choose a harmless action. Macrofy does not activate or minimize the target. Arrange the window yourself. Background macros use BackgroundVisible evidence; Minimized macros use Minimized evidence. Other coverage states are recorded separately.", "muted", 13),
            Field("Saved app", app), Field("Live window", window), Field("Input surface", surface), Field("Observed window state", state),
            Row(Field("Action capability", action), Field("Client position / value", value)), actionHelp, Row(capture, test), Row(yes, no, discard), status,
            Wrap("Screen actions are tested from the macro editor, with a 3 second countdown. Screen tests never establish window compatibility.", "muted", 12))));
        UpdateControls(); return compatibilityPane;
    }
    private sealed record WindowOption(TargetWindow Window)
    { public override string ToString() => $"{Window.Title} · {Window.Token.Id.ToString("N")[..8]}"; }
}
