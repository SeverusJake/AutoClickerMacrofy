using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.App.Views;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.App.Tests;

public class PlaybackUiTests
{
    [AvaloniaFact]
    public void LoadedPercentageDraftIsValidatedBeforeAnyEdit()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        state.Macro.Coordinates = "Percentage";
        var window = HarmlessUi.Create(state); window.Show();
        try
        {
            Click(window, "Tab_Macros");
            Assert.Contains("0–100", Find<TextBlock>(window, "ActionError").Text);
            Assert.False(Find<Button>(window, "RunSelected").IsEnabled);
            Assert.False(Find<Button>(window, "TestSelected").IsEnabled);
        }
        finally { window.Close(); }
    }
    internal static T Find<T>(Window window, string name) where T : Control
    { window.UpdateLayout(); return window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name); }
    internal static void Click(Window window, string name) => Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}

public class PlaybackControllerTests
{
    [Fact]
    public async Task AcceptedStartKeepsGuardThroughCountdownAndCompletion()
    {
        var document = WorkspaceDocument.CreateDefault(); var profile = document.Profiles[0]; var macro = profile.Macros[0]; macro.AppId = null;
        var executor = new HarmlessExecutor(); var guard = new InputActivityGuard(); var hotkeys = new HarmlessHotkeys();
        await using var controller = new WorkspacePlaybackController(document, new(executor, TimeProvider.System), guard, hotkeys, hotkeys);
        Assert.True((await controller.StartAsync(profile, macro)).Queued);
        Assert.False(guard.TryEnterTest(out _));
        Assert.Equal(3000, Assert.Single(executor.Requests).StartDelayMs);
        controller.StopAll();
        await controller.DisposeAsync();
        Assert.True(guard.TryEnterTest(out var lease)); lease!.Dispose();
        Assert.Empty(executor.Actions);
    }
    [Fact]
    public async Task SelectedActionIsSingleAndUsesOwningProfileAndCurrentReservedKeys()
    {
        var document = WorkspaceDocument.CreateDefault(); var profile = document.Profiles[0]; var macro = profile.Macros[0];
        macro.Steps = [new("Key", "Space", 0), new("Text", "hello", 0)];
        PlaybackEligibilityTests.AddEvidence(document, profile.Apps[1], InputCapability.Text, TargetState.Minimized);
        var executor = new HarmlessExecutor(); var hotkeys = new HarmlessHotkeys();
        await using var controller = new WorkspacePlaybackController(document, new(executor, TimeProvider.System), new(), hotkeys, hotkeys);
        document.ActiveProfileId = document.Profiles[1].Id;
        Assert.True((await controller.StartAsync(profile, macro, true, 1)).Queued);
        var request = Assert.Single(executor.Requests);
        Assert.Equal("Daily games", request.ProfileName); Assert.Equal(1, request.Repeat); Assert.Equal(0, request.IntervalMs);
        Assert.Equal(profile.Apps[1].Executable, request.Target.Rule!.App.ExecutablePath);
        Assert.IsType<CompiledAction.Text>(Assert.Single(request.Actions));
        await controller.DisposeAsync();
        macro.Steps = [new("Key", "F9", 0)];
        Assert.False(controller.CanStart(profile, macro, out _));
    }
    [Fact]
    public async Task CompletedMacroReleasesOwnGuardWhileOtherMacroWaits()
    {
        var document = WorkspaceDocument.CreateDefault(); var profile = document.Profiles[0];
        var first = profile.Macros[0]; first.Repeat = 1; first.Steps = [new("Wait", "0", 0)];
        var other = profile.Macros[1]; other.Steps = [new("Wait", "600000", 0)];
        var guard = new InputActivityGuard(); var hotkeys = new HarmlessHotkeys();
        await using var controller = new WorkspacePlaybackController(document, new(new HarmlessExecutor(), TimeProvider.System), guard, hotkeys, hotkeys);
        Assert.True((await controller.StartAsync(profile, other)).Queued);
        Assert.True((await controller.StartAsync(profile, first)).Queued);
        await Until(() => controller.Sessions[first.Id].State == PlaybackState.Completed);
        IDisposable? lease = null;
        await Until(() => guard.TryEnterPlayback(first.Id, out lease)); lease!.Dispose();
        Assert.True(controller.Sessions[other.Id].IsActive);
        Assert.False(guard.TryEnterTest(out _));
    }
    [Fact]
    public async Task HealthLossAndSuspendCancelActiveSessionsAndHealthLossBlocksRestart()
    {
        var document = WorkspaceDocument.CreateDefault(); var profile = document.Profiles[0]; var macro = profile.Macros[0]; macro.Steps = [new("Wait", "600000", 0)];
        var hotkeys = new HarmlessHotkeys();
        await using var controller = new WorkspacePlaybackController(document, new(new HarmlessExecutor(), TimeProvider.System), new(), hotkeys, hotkeys);
        Assert.True((await controller.StartAsync(profile, macro)).Queued);
        hotkeys.Suspend(); await Until(() => !controller.Sessions[macro.Id].IsActive);
        await Until(() => controller.CanStart(profile, macro, out _));
        Assert.True((await controller.StartAsync(profile, macro)).Queued);
        hotkeys.LoseHealth(); await Until(() => !controller.Sessions[macro.Id].IsActive);
        Assert.False((await controller.StartAsync(profile, macro)).Queued);
        Assert.False(controller.HotkeysReady);
    }
    internal static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
}

internal sealed class HarmlessHotkeys : IGlobalHotkeys, IHotkeyHealth, ISystemEvents
{
    public bool IsOperational { get; private set; } = true;
    public PlatformError? OperationalError => IsOperational ? null : new("HotkeyUnavailable", "Emergency Stop unavailable");
    public event Action? HealthChanged;
    public event Action<HotkeyCommand>? Triggered;
    public event Action? Suspended;
    public HotkeyRegistrationResult Configure(HotkeySet set) => new(IsOperational, OperationalError);
    public void Trigger(HotkeyCommand command) => Triggered?.Invoke(command);
    public void LoseHealth() { IsOperational = false; HealthChanged?.Invoke(); }
    public void Suspend() => Suspended?.Invoke();
}
internal sealed class HarmlessExecutor : IPlaybackExecutor
{
    public System.Collections.Concurrent.ConcurrentQueue<PlaybackRequest> Requests { get; } = new();
    public System.Collections.Concurrent.ConcurrentQueue<CompiledAction> Actions { get; } = new();
    public ValueTask<PlaybackPreparation> PrepareAsync(PlaybackRequest request, CancellationToken ct)
    { Requests.Enqueue(request); return ValueTask.FromResult(new PlaybackPreparation(new(request.Target.SavedAppId, null, "harmless", null, null, request.Target.State))); }
    public ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken ct) => ValueTask.FromResult(new DeliveryResult(true));
    public ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, CompiledAction action, CancellationToken ct)
    { Actions.Enqueue(action); return ValueTask.FromResult(new GestureResult(new(true))); }
}


public class RealPlaybackUiTests
{
    [AvaloniaFact]
    public async Task RowAndEditorRunUseControllerAndCloseCancelsScreenCountdown()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault()); state.Macro.AppId = null;
        var executor = new HarmlessExecutor(); var guard = new InputActivityGuard(); var keys = new HarmlessHotkeys();
        var controller = new WorkspacePlaybackController(state.Document, new(executor, TimeProvider.System), guard, keys, keys);
        var window = new MainWindow(state, controller, keys); window.Show();
        PlaybackUiTests.Click(window, "Run_" + state.Macro.Id.ToString("N"));
        await PlaybackControllerTests.Until(() => executor.Requests.Count == 1);
        Assert.False(guard.TryEnterTest(out _));
        PlaybackUiTests.Click(window, "Edit_" + state.Macro.Id.ToString("N"));
        Assert.False(PlaybackUiTests.Find<TextBox>(window, "ActionValue").IsEnabled);
        Assert.False(PlaybackUiTests.Find<Button>(window, "RunSelected").IsEnabled);
        window.Close(); await PlaybackControllerTests.Until(() => !window.IsVisible);
        Assert.Empty(executor.Actions);
        Assert.True(guard.TryEnterTest(out var lease)); lease!.Dispose();
    }
    [AvaloniaFact]
    public async Task NativeRunAndFocusedKeyRouteOnceAndRegisteredKeyCanBeCaptured()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        foreach (var macro in state.Profile.Macros) { macro.AppId = null; macro.Steps = [new("Wait", "600000", 0)]; }
        var executor = new HarmlessExecutor(); var keys = new HarmlessHotkeys();
        var controller = new WorkspacePlaybackController(state.Document, new(executor, TimeProvider.System), new(), keys, keys);
        var window = new MainWindow(state, controller, keys); window.Show();
        try
        {
            window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.F9 });
            Assert.Empty(executor.Requests);
            keys.Trigger(HotkeyCommand.Run);
            await PlaybackControllerTests.Until(() => executor.Requests.Count == 2);
            keys.Trigger(HotkeyCommand.Pause);
            await PlaybackControllerTests.Until(() => controller.Sessions.Values.All(s => s.State == PlaybackState.Paused));
            keys.Trigger(HotkeyCommand.Stop);
            await PlaybackControllerTests.Until(() => controller.Sessions.Values.All(s => !s.IsActive));
            PlaybackUiTests.Click(window, "Tab_Settings");
            PlaybackUiTests.Find<TextBox>(window, "Shortcut_Run").Focus();
            keys.Trigger(HotkeyCommand.Run);
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(2, executor.Requests.Count); Assert.Equal("F9", state.Document.Shortcuts.Run);
        }
        finally { window.Close(); await controller.DisposeAsync(); }
    }
    [AvaloniaFact]
    public void RegistrationFailureDisablesRunAndSelectedTest()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault()); state.Macro.AppId = null;
        var keys = new HarmlessHotkeys(); keys.LoseHealth();
        var controller = new WorkspacePlaybackController(state.Document, new(new HarmlessExecutor(), TimeProvider.System), new(), keys, keys);
        var window = new MainWindow(state, controller, keys); window.Show();
        try
        {
            Assert.False(PlaybackUiTests.Find<Button>(window, "RunAllEnabled").IsEnabled);
            PlaybackUiTests.Click(window, "Tab_Macros");
            Assert.False(PlaybackUiTests.Find<Button>(window, "RunSelected").IsEnabled);
            Assert.False(PlaybackUiTests.Find<Button>(window, "TestSelected").IsEnabled);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void CoordinateModeChangeAndReservedLoadedKeyRefreshValidation()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault()); var window = HarmlessUi.Create(state); window.Show();
        try
        {
            PlaybackUiTests.Click(window, "Tab_Macros");
            PlaybackUiTests.Find<ComboBox>(window, "CoordinateMode").SelectedItem = "Percentage";
            Assert.Contains("0–100", PlaybackUiTests.Find<TextBlock>(window, "ActionError").Text);
            PlaybackUiTests.Click(window, "ApplyAction"); Assert.Equal("480, 640", state.Macro.Steps[0].Value);
            state.Macro.Steps[0] = new("Key", "F10", 0);
            PlaybackUiTests.Click(window, "DiscardDraft");
            Assert.Contains("reserved", PlaybackUiTests.Find<TextBlock>(window, "ActionError").Text);
            Assert.False(PlaybackUiTests.Find<Button>(window, "RunSelected").IsEnabled);
        }
        finally { window.Close(); }
    }
}

public class CompatibilityUiTests
{
    [AvaloniaFact]
    public async Task ExplicitTargetSurfaceStateAndActionAreRequiredAndObservationIsNeverAutomatic()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        state.Profile.Apps[0].Executable = @"C:\test.exe"; state.Profile.Apps[0].TitleRule = "Test *"; state.Document.ShowAdvancedTools = true;
        using var catalog = new Macrofy.Platform.Windows.WindowsWindowCatalog(new UiWindows());
        var guard = new InputActivityGuard(); var keys = new HarmlessHotkeys(); var sends = 0;
        var service = new CompatibilityService(state.Document, catalog, () => guard.TryEnterTest(out var lease) ? lease : null,
            (_, _, _) => { sends++; return ValueTask.FromResult(new GestureResult(new(true))); },
            (snapshot, publish, ct) => { _ = snapshot(); publish(); return Task.CompletedTask; });
        var executor = new HarmlessExecutor();
        var controller = new WorkspacePlaybackController(state.Document, new(executor, TimeProvider.System), guard, keys, keys);
        var ui = new CompatibilityUiServices(service, catalog, async (t, ct) => (await catalog.ListInputSurfacesAsync(t, ct)).Select(s => s.Window).ToArray(), _ => new(new(20, 30)));
        var window = new MainWindow(state, controller, keys, compatibility: ui); window.Show();
        try
        {
            PlaybackUiTests.Click(window, "Tab_Compatibility");
            window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Run does not need them") == true);
            Assert.False(PlaybackUiTests.Find<Button>(window, "TestCompatibilityAction").IsEnabled);
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilityApp").SelectedIndex = 0;
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilityWindow").SelectedIndex = 0;
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilitySurface").SelectedIndex = 0;
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilityState").SelectedItem = TargetState.BackgroundVisible;
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilityAction").SelectedItem = "Key";
            PlaybackUiTests.Find<TextBox>(window, "CompatibilityValue").Text = "Space";
            Assert.True(PlaybackUiTests.Find<Button>(window, "TestCompatibilityAction").IsEnabled);
            Assert.False(PlaybackUiTests.Find<Button>(window, "ObservedWorking").IsEnabled);
            PlaybackUiTests.Click(window, "TestCompatibilityAction");
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<Button>(window, "ObservedWorking").IsEnabled);
            Assert.Equal(1, sends); Assert.Empty(state.Document.CompatibilityEvidence);
            Assert.False(guard.TryEnterPlayback(Guid.NewGuid(), out _));
            Assert.False(PlaybackUiTests.Find<ComboBox>(window, "CompatibilityApp").IsEnabled);
            PlaybackUiTests.Click(window, "Tab_Apps"); window.UpdateLayout();
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Edit app: " + state.Profile.Apps[0].Name).IsEnabled);
            PlaybackUiTests.Click(window, "Tab_Compatibility");
            PlaybackUiTests.Click(window, "ObservedWorking");
            await PlaybackControllerTests.Until(() => state.Document.CompatibilityEvidence.Count == 1);
            var evidence = Assert.Single(state.Document.CompatibilityEvidence);
            Assert.Equal(InputCapability.Key, evidence.Capability); Assert.Equal(TargetState.BackgroundVisible, evidence.State); Assert.True(evidence.ObservedSuccess);
            Assert.False(PlaybackUiTests.Find<Button>(window, "ObservedWorking").IsEnabled);
            var macro = state.Macro; macro.AppId = state.Profile.Apps[0].Id; macro.WindowState = "Background"; macro.Steps = [new("Key", "Space", 0)];
            Assert.True(controller.CanStart(state.Profile, macro, out _));
            Assert.True((await controller.StartAsync(state.Profile, macro)).Queued);
            Assert.Equal((await catalog.ListAsync())[0].Token, Assert.Single(executor.Requests).Target.SelectedSurface);
            controller.StopAll(); await PlaybackControllerTests.Until(() => !controller.Sessions[macro.Id].IsActive);
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilitySurface").SelectedIndex = 1;
            Assert.Equal("", PlaybackUiTests.Find<TextBox>(window, "CompatibilityValue").Text);
            Assert.False(PlaybackUiTests.Find<Button>(window, "TestCompatibilityAction").IsEnabled);
        }
        finally { window.Close(); await controller.DisposeAsync(); }
    }
}


public class PlaybackEligibilityTests
{
    [Fact]
    public async Task WindowRunNeedsNoEvidenceButRequiresAppAndState()
    {
        var document = WorkspaceDocument.CreateDefault(); var profile = document.Profiles[0]; var macro = profile.Macros[0];
        macro.Steps = [new("Key", "Space", 0), new("Text", "hello", 0)];
        var keys = new HarmlessHotkeys();
        await using var controller = new WorkspacePlaybackController(document, new(new HarmlessExecutor(), TimeProvider.System), new(), keys, keys);
        Assert.Empty(document.CompatibilityEvidence);
        Assert.True(controller.CanStart(profile, macro, out _));
        macro.WindowState = "Sideways"; Assert.False(controller.CanStart(profile, macro, out var reason)); Assert.Contains("state", reason);
        macro.WindowState = "Background"; macro.AppId = Guid.NewGuid();
        Assert.False(controller.CanStart(profile, macro, out reason)); Assert.Contains("missing", reason);
    }
    internal static void AddEvidence(WorkspaceDocument document, SavedApp app, InputCapability capability, TargetState state) =>
        document.CompatibilityEvidence.Add(new(app.Id, new("process", app.Executable), "Example game", "surface", state, capability, new(800, 600, 1), DateTimeOffset.UtcNow, true));
}




public class WorkspacePersistenceUiTests
{
    [AvaloniaFact]
    public async Task CandidateSaveAndPublishShareDispatcherTurnAndRetainEarlierUnrelatedSave()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-transaction-" + Guid.NewGuid());
        try
        {
            var store = new WorkspaceStore(folder); var document = store.Load(); store.Save(document);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = false; var trace = new List<string>();
            var confirmation = Task.Run(async () =>
            {
                entered.SetResult(); await proceed.Task;
                await WorkspaceRuntime.PersistAsync(store, () =>
                {
                    Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess()); trace.Add("candidate");
                    return new WorkspaceDocument { Profiles = document.Profiles, ActiveProfileId = document.ActiveProfileId, Theme = "synthwave", Mode = document.Mode, Shortcuts = document.Shortcuts };
                }, () => { Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess()); Assert.Equal("synthwave", new WorkspaceStore(folder).Load().Theme); trace.Add("publish"); published = true; }, TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken);
            await entered.Task;
            document.Mode = "dark"; document.Profiles[0].Name = "Unrelated edit"; store.Save(document);
            proceed.SetResult(); await confirmation;
            Assert.True(published); Assert.Equal(new[] { "candidate", "publish" }, trace);
            var saved = new WorkspaceStore(folder).Load(); Assert.Equal("dark", saved.Mode); Assert.Equal("Unrelated edit", saved.Profiles[0].Name);
            var other = new WorkspaceStore(folder); var external = other.Load(); external.Mode = "light"; other.Save(external);
            published = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceRuntime.PersistAsync(store, () => document, () => published = true, TestContext.Current.CancellationToken));
            Assert.False(published);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}


public class PlaybackLifecycleUiTests
{
    [AvaloniaFact]
    public async Task SelectedActionTestSendsOnlyThatActionOnce()
    {
        var document = WorkspaceDocument.CreateDefault(); var state = new WorkspaceState(document); var macro = state.Macro;
        macro.Repeat = 100; macro.Steps = [new("Wait", "0", 0), new("Key", "Space", 0)];
        PlaybackEligibilityTests.AddEvidence(document, state.Profile.Apps.Single(a => a.Id == macro.AppId), InputCapability.Key, TargetState.Minimized);
        var executor = new HarmlessExecutor(); var keys = new HarmlessHotkeys();
        var controller = new WorkspacePlaybackController(document, new(executor, TimeProvider.System), new(), keys, keys);
        var window = new MainWindow(state, controller, keys); window.Show();
        try
        {
            PlaybackUiTests.Click(window, "Tab_Macros"); PlaybackUiTests.Click(window, "SelectStep_1"); PlaybackUiTests.Click(window, "TestSelected");
            await PlaybackControllerTests.Until(() => controller.Sessions.GetValueOrDefault(macro.Id)?.State == PlaybackState.Completed);
            Assert.IsType<CompiledAction.Key>(Assert.Single(executor.Actions));
            Assert.Equal(1, Assert.Single(executor.Requests).Repeat);
            Assert.Contains("1 loops", PlaybackUiTests.Find<TextBlock>(window, "MacroProgress").Text);
        }
        finally { window.Close(); await controller.DisposeAsync(); }
    }
    [AvaloniaFact]
    public async Task WindowCloseWaitsForCleanupBeforeDisposingOwnedServices()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var app = state.Profile.Apps.Single(a => a.Id == state.Macro.AppId);
        PlaybackEligibilityTests.AddEvidence(state.Document, app, InputCapability.Click, TargetState.Minimized);
        var executor = new CleanupExecutor(); var keys = new HarmlessHotkeys(); var guard = new InputActivityGuard();
        var controller = new WorkspacePlaybackController(state.Document, new(executor, TimeProvider.System), guard, keys, keys);
        var resources = new DisposalMarker();
        var window = new MainWindow(state, controller, keys, ownedServices: resources); window.Show();
        try
        {
            PlaybackUiTests.Click(window, "Run_" + state.Macro.Id.ToString("N"));
            await executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            window.Close();
            await executor.Cleaning.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(window.IsVisible); Assert.False(resources.Disposed); Assert.False(guard.TryEnterTest(out _));
        }
        finally { executor.Release.TrySetResult(); await controller.DisposeAsync(); }
        await PlaybackControllerTests.Until(() => !window.IsVisible);
        Assert.True(resources.Disposed);
    }
    private sealed class DisposalMarker : IAsyncDisposable
    { public bool Disposed; public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; } }
    private sealed class CleanupExecutor : IPlaybackExecutor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cleaning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<PlaybackPreparation> PrepareAsync(PlaybackRequest request, CancellationToken ct) => ValueTask.FromResult(new PlaybackPreparation(new(null, null, "fake", null, null, null)));
        public ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken ct) => ValueTask.FromResult(new DeliveryResult(true));
        public async ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, CompiledAction action, CancellationToken ct)
        {
            Entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { }
            Cleaning.SetResult(); await Release.Task;
            return new(new(true));
        }
    }
}

public class CompatibilityLifetimeUiTests
{
    [AvaloniaFact]
    public async Task FailedCleanupNeverEnablesObservationAndReleasesExclusiveOwner()
    {
        using var fixture = new CompatibilityFixture((_, _, _) => ValueTask.FromResult(new GestureResult(new(true), new("CleanupFailed", "Key release failed"))));
        var window = fixture.Window; fixture.SelectTest();
        PlaybackUiTests.Click(window, "TestCompatibilityAction");
        await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBlock>(window, "CompatibilityStatus").Text?.Contains("Key release failed") == true);
        Assert.False(PlaybackUiTests.Find<Button>(window, "ObservedWorking").IsEnabled);
        Assert.False(PlaybackUiTests.Find<Button>(window, "ObservedIgnored").IsEnabled);
        Assert.Empty(fixture.State.Document.CompatibilityEvidence);
        Assert.True(fixture.Guard.TryEnterPlayback(Guid.NewGuid(), out var lease)); lease!.Dispose();
    }
    [AvaloniaFact]
    public async Task ClosingDuringCommittedObservationWaitsForSaveAndKeepsExclusiveOwner()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new CompatibilityFixture(persist: async (snapshot, publish, ct) =>
        { entered.SetResult(); await release.Task; _ = snapshot(); publish(); });
        fixture.SelectTest(); PlaybackUiTests.Click(fixture.Window, "TestCompatibilityAction");
        await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<Button>(fixture.Window, "ObservedWorking").IsEnabled);
        PlaybackUiTests.Click(fixture.Window, "ObservedWorking"); await entered.Task;
        try
        {
            fixture.Window.Close(); Assert.True(fixture.Window.IsVisible);
            Assert.False(fixture.Guard.TryEnterPlayback(Guid.NewGuid(), out _));
            Assert.False(PlaybackUiTests.Find<Button>(fixture.Window, "TestCompatibilityAction").IsEnabled);
        }
        finally { release.TrySetResult(); }
        await PlaybackControllerTests.Until(() => !fixture.Window.IsVisible);
        Assert.Single(fixture.State.Document.CompatibilityEvidence);
        Assert.True(fixture.Guard.TryEnterPlayback(Guid.NewGuid(), out var lease)); lease!.Dispose();
    }
    private sealed class CompatibilityFixture : IDisposable
    {
        public WorkspaceState State { get; } = new(WorkspaceDocument.CreateDefault());
        public InputActivityGuard Guard { get; } = new();
        public MainWindow Window { get; }
        private readonly Macrofy.Platform.Windows.WindowsWindowCatalog catalog = new(new UiWindows());
        public CompatibilityFixture(Func<PlaybackBinding, CompiledAction, CancellationToken, ValueTask<GestureResult>>? send = null, CompatibilityPersistence? persist = null)
        {
            State.Profile.Apps[0].Executable = @"C:\test.exe"; State.Profile.Apps[0].TitleRule = "Test *"; State.Document.ShowAdvancedTools = true;
            var keys = new HarmlessHotkeys();
            var service = new CompatibilityService(State.Document, catalog, () => Guard.TryEnterTest(out var lease) ? lease : null,
                send ?? ((_, _, _) => ValueTask.FromResult(new GestureResult(new(true)))),
                persist ?? ((snapshot, publish, ct) => { _ = snapshot(); publish(); return Task.CompletedTask; }));
            var controller = new WorkspacePlaybackController(State.Document, new(new HarmlessExecutor(), TimeProvider.System), Guard, keys, keys);
            var ui = new CompatibilityUiServices(service, catalog, async (t, ct) => (await catalog.ListInputSurfacesAsync(t, ct)).Select(s => s.Window).ToArray(), _ => new(new(20, 30)));
            Window = new(State, controller, keys, compatibility: ui); Window.Show();
        }
        public void SelectTest()
        {
            PlaybackUiTests.Click(Window, "Tab_Compatibility");
            PlaybackUiTests.Find<ComboBox>(Window, "CompatibilityApp").SelectedIndex = 0;
            PlaybackUiTests.Find<ComboBox>(Window, "CompatibilityWindow").SelectedIndex = 0;
            PlaybackUiTests.Find<ComboBox>(Window, "CompatibilitySurface").SelectedIndex = 0;
            PlaybackUiTests.Find<ComboBox>(Window, "CompatibilityState").SelectedItem = TargetState.BackgroundVisible;
            PlaybackUiTests.Find<ComboBox>(Window, "CompatibilityAction").SelectedItem = "Key";
            PlaybackUiTests.Find<TextBox>(Window, "CompatibilityValue").Text = "Space";
        }
        public void Dispose() { Window.Close(); catalog.Dispose(); }
    }
}

public class NativeHealthControllerTests
{
    [Fact]
    public async Task NativeLoopFailureStopsRunningMacroAndBlocksLaterRequests()
    {
        var native = new FailingHotkeyNative(); using var hotkeys = new Macrofy.Platform.Windows.WindowsGlobalHotkeys(native);
        Assert.True(hotkeys.Configure(new(new("F9"), new("F8"), new("F10"))).Registered);
        var document = WorkspaceDocument.CreateDefault(); var profile = document.Profiles[0]; var macro = profile.Macros[0]; macro.Steps = [new("Wait", "600000", 0)];
        await using var controller = new WorkspacePlaybackController(document, new(new HarmlessExecutor(), TimeProvider.System), new(), hotkeys, hotkeys);
        Assert.True((await controller.StartAsync(profile, macro)).Queued);
        native.Fail = true;
        await PlaybackControllerTests.Until(() => !controller.Sessions[macro.Id].IsActive);
        Assert.False(controller.HotkeysReady); Assert.False((await controller.StartAsync(profile, macro)).Queued);
        Assert.Contains("loop failed", controller.HotkeyError!.Message);
    }
    private sealed class FailingHotkeyNative : Macrofy.Platform.Windows.Interop.IHotkeyNative
    {
        public volatile bool Fail;
        public void Initialize() { }
        public bool Register(int id, uint modifiers, uint key, out int error) { error = 0; return true; }
        public void Unregister(int id) { }
        public void Pump(Action<uint, nuint> callback) { if (Fail) throw new InvalidOperationException("loop failed"); }
        public void Dispose() { }
    }
}
