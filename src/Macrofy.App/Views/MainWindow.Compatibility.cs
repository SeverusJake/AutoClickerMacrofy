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
    private bool compatibilityBusy;
    private Task compatibilityWork = Task.CompletedTask;
    private Control? compatibilityPane;
    private Guid compatibilityProfile;
    private Action? refreshCompatibility;
    private Guid? compatibilitySelectionApp;
    private void ResetCompatibilityContext()
    {
        if (compatibilitySelectionApp is { } appId) playback.SelectSurface(appId, null);
        compatibilitySelectionApp = null; compatibilityPane = null; refreshCompatibility = null; compatibilityProfile = Guid.Empty;
    }
    private bool CompatibilityLocked => compatibilityBusy;
    private void CancelCompatibilityTest()
    {
        try { Volatile.Read(ref compatibilityCancellation)?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
    private Control CompatibilityPane()
    {
        if (compatibility is null) return Panel(Text("Window compatibility testing is unavailable on this platform."));
        if (compatibilityPane is not null && compatibilityProfile == Workspace.Profile.Id)
        { if (compatibilityPane.Parent is Avalonia.Controls.Panel parent) parent.Children.Remove(compatibilityPane); if (refreshCompatibility is not null) refreshPlayback.Add(refreshCompatibility); return compatibilityPane; }
        compatibilityProfile = Workspace.Profile.Id;
        var app = new ComboBox { Name = "CompatibilityApp", ItemsSource = Workspace.Profile.Apps, SelectedIndex = -1, MinWidth = 220 };
        var window = new ComboBox { Name = "CompatibilityWindow", SelectedIndex = -1, MinWidth = 220 };
        var surface = new ComboBox { Name = "CompatibilitySurface", SelectedIndex = -1, MinWidth = 220 };
        var action = new ComboBox { Name = "CompatibilityAction", ItemsSource = new[] { "Click", "Key", "Shortcut", "Text", "Wheel" }, SelectedItem = "Click", MinWidth = 140 };
        var value = new TextBox { Name = "CompatibilityValue", Text = "", MinWidth = 220, PlaceholderText = "Harmless test value / client X, Y" };
        var status = Wrap("Select an app, live window, input surface and a harmless action. Nothing is sent automatically.", "muted", 13); status.Name = "CompatibilityStatus";
        var actionHelp = Wrap("", "muted", 12);
        var capture = RecordPointButton("CompatibilityRecordPoint", () => surface.SelectedItem is WindowOption && action.SelectedItem as string == "Click",
            () => Task.FromResult(surface.SelectedItem is WindowOption option ? compatibility.ReadPointer(option.Window.Token) : new ScreenPointResult(null, new("NoSurface", "Select an input surface."))),
            point => { value.Text = FormatPoint(point); status.Text = $"Recorded {value.Text}. Arrange the window, then send the test."; },
            text => status.Text = text);
        var test = TextButton("Click test", () => { }); test.Name = "TestCompatibilityAction";
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
            foreach (var control in new Control[] { app, window, surface, action, value }) control.IsEnabled = !locked;
            var ready = !locked && Workspace.ActiveCount == 0 && playback.HotkeysReady;
            var valid = TryAction(out _, out var problem);
            test.Content = (action.SelectedItem as string ?? "Click") + " test";
            test.IsEnabled = ready && app.SelectedItem is SavedApp && surface.SelectedItem is WindowOption && valid;
            ToolTip.SetTip(test, !playback.HotkeysReady ? playback.HotkeyError?.Message ?? "Global emergency Stop unavailable." : valid ? "Send exactly one selected action; watch the app yourself." : problem);
        }
        refreshCompatibility = UpdateControls; refreshPlayback.Add(UpdateControls);
        void ClearValue() { value.Text = ""; status.Text = "Selection changed. Enter or record a fresh test value."; RefreshPlayback(); }
        app.SelectionChanged += async (_, _) =>
        {
            if (CompatibilityLocked) return;
            if (selectedAppId is { } previous) playback.SelectSurface(previous, null);
            selectedAppId = (app.SelectedItem as SavedApp)?.Id; compatibilitySelectionApp = selectedAppId;
            ClearValue(); window.ItemsSource = null; surface.ItemsSource = null; var version = ++generation;
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
            var version = ++generation; ClearValue(); surface.ItemsSource = null;
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
            ClearValue();
            if (selectedAppId is { } id) playback.SelectSurface(id, (surface.SelectedItem as WindowOption)?.Window.Token);
        };
        action.SelectionChanged += (_, _) => { if (!CompatibilityLocked) ClearValue(); };
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
        test.Click += (_, _) =>
        {
            if (!test.IsEnabled || app.SelectedItem is not SavedApp selectedApp || surface.SelectedItem is not WindowOption selected || !TryAction(out var compiled, out _)) return;
            compatibilityWork = RunOperation(async ct =>
            {
                var sent = await compatibility.Service.SendTestAsync(selectedApp.Id, selected.Window.Token, compiled!, ct);
                var delivery = sent.Result.Delivery;
                var error = delivery.Error ?? delivery.CleanupError ?? sent.Result.CleanupError;
                status.Text = delivery.Queued && error is null ? $"Sent one {sent.Capability.ToString().ToLowerInvariant()}. Watch the app." : "Test failed: " + (error?.Message ?? "input was not delivered.");
            });
        };
        compatibilityPane = Panel(Scroll(Stack(Text("Window compatibility", size: 16),
            Wrap("Optional tool. Send one harmless action and watch whether the app reacts. Nothing is saved; Run does not need it. Macrofy does not activate or minimize the target; arrange the window yourself.", "muted", 13),
            Field("Saved app", app), Field("Live window", window), Field("Input surface", surface),
            Row(Field("Action", action), Field("Client position / value", value)), actionHelp, Row(capture, test), status,
            Wrap("Screen actions are tested from the macro editor, with a 3 second countdown.", "muted", 12))));
        UpdateControls(); return compatibilityPane;
    }
    private sealed record WindowOption(TargetWindow Window)
    { public override string ToString() => $"{Window.Title} · {Window.Token.Id.ToString("N")[..8]}"; }
}
