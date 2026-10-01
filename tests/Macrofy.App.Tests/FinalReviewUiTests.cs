using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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

public sealed class FinalReviewUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowPlaybackSerializesEvidenceWithUiEditsAndHonorsStop(bool stopDuringRead)
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault()); var app = state.Profile.Apps.Single(a => a.Id == state.Macro.AppId);
        state.Macro.Steps = [new("Text", "harmless", 0)]; state.Macro.Repeat = 1;
        PlaybackEligibilityTests.AddEvidence(state.Document, app, InputCapability.Text, TargetState.Minimized);
        var context = new TargetContext(new(new(Guid.NewGuid()), new("process", app.Executable), "Example game", true, new(800, 600, 1)), false, "surface");
        var catalog = new ContextCatalog { Context = context }; var input = new HarmlessInput(); var keys = new HarmlessHotkeys();
        var reads = 0; var dispatchRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CompatibilityService(state.Document, catalog, () => null,
            (_, _, _) => throw new InvalidOperationException("No compatibility test expected"), (_, _, _) => Task.CompletedTask,
            async (read, ct) =>
            {
                if (Interlocked.Increment(ref reads) == 2) { dispatchRead.SetResult(); await release.Task.WaitAsync(ct); }
                return await WorkspaceRuntime.ReadAsync(() => { Assert.True(Dispatcher.UIThread.CheckAccess()); return read(); }, ct);
            });
        var sender = new WindowsGestureSender(catalog, input, input, input, _ => new(new(10, 10)), () => true);
        var executor = new WindowsPlaybackExecutor(catalog, catalog, (_, _) => Task.FromResult<IReadOnlyList<TargetWindow>>([context.Window]), sender, service.IsConfirmedAsync);
        var coordinator = new PlaybackCoordinator(executor, TimeProvider.System, catalog);
        await using var controller = new WorkspacePlaybackController(state.Document, coordinator, new(), keys, keys);
        Assert.True((await controller.StartAsync(state.Profile, state.Macro)).Queued);
        await dispatchRead.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            Assert.True(controller.Sessions[state.Macro.Id].IsActive);
            for (var i = 0; i < 50; i++)
            {
                var added = new Profile { Name = "Unrelated", Apps = [new SavedApp()] };
                state.Document.Profiles.Add(added); state.Profile.Apps.Add(new SavedApp());
                state.Document.Profiles.Remove(added); state.Profile.Apps.RemoveAt(state.Profile.Apps.Count - 1);
            }
            if (stopDuringRead) controller.StopAll();
        }
        finally { release.TrySetResult(); }
        await coordinator.WaitForCompletionAsync(state.Macro.Id).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(stopDuringRead ? PlaybackState.Stopped : PlaybackState.Completed, controller.Sessions[state.Macro.Id].State);
        Assert.Null(controller.Sessions[state.Macro.Id].DeliveryError);
        if (stopDuringRead) Assert.Empty(input.Commands);
        else Assert.IsType<TextCommand>(Assert.Single(input.Commands));
    }

    [AvaloniaFact]
    public async Task TargetLossKeepsControllerLeaseUntilIndependentCleanupFinishes()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault()); var app = state.Profile.Apps.Single(a => a.Id == state.Macro.AppId);
        state.Macro.Steps = [new("Text", "harmless", 0)]; state.Macro.Repeat = 1;
        PlaybackEligibilityTests.AddEvidence(state.Document, app, InputCapability.Text, TargetState.Minimized);
        var token = new TargetToken(Guid.NewGuid()); var catalog = new ContextCatalog(); var executor = new LossCleanupExecutor(token);
        var guard = new InputActivityGuard(); var keys = new HarmlessHotkeys();
        await using var controller = new WorkspacePlaybackController(state.Document, new(executor, TimeProvider.System, catalog), guard, keys, keys);
        try
        {
            Assert.True((await controller.StartAsync(state.Profile, state.Macro)).Queued);
            await executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); catalog.Lose(token);
            await executor.Cleaning.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(guard.TryEnterTest(out _));
            Assert.False((await controller.StartAsync(state.Profile, state.Macro, true, 0)).Queued);
            Assert.Equal("TargetLost", controller.Sessions[state.Macro.Id].DeliveryError?.Code);
        }
        finally { executor.Release.TrySetResult(); }
        await controller.DisposeAsync();
        Assert.True(guard.TryEnterTest(out var lease)); lease!.Dispose();
        Assert.Equal(PlaybackState.Stopped, controller.Sessions[state.Macro.Id].State);
        Assert.Equal("TargetLost", controller.Sessions[state.Macro.Id].DeliveryError?.Code);
    }

    [AvaloniaFact]
    public async Task WorkerEvidenceReadWaitsForUiWorkspaceMutationsAndCancellation()
    {
        var document = WorkspaceDocument.CreateDefault(); var app = document.Profiles[0].Apps[1];
        PlaybackEligibilityTests.AddEvidence(document, app, InputCapability.Text, TargetState.Minimized);
        var context = new TargetContext(new(new(Guid.NewGuid()), new("process", app.Executable), "Example game", true, new(800, 600, 1)), false, "surface");
        var catalog = new ContextCatalog { Context = context };
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var service = new CompatibilityService(document, catalog, () => null,
            (_, _, _) => throw new InvalidOperationException("Evidence reads must not send input"), (_, _, _) => Task.CompletedTask,
            (read, ct) =>
            {
                queued.TrySetResult();
                return WorkspaceRuntime.ReadAsync(() => { Assert.True(Dispatcher.UIThread.CheckAccess()); reads++; return read(); }, ct);
            });
        // Hold this dispatcher turn while the worker requests evidence. All mutable lists stay UI-owned.
        var check = Task.Run(() => service.IsConfirmedAsync(app.Id, context, TargetState.Minimized, InputCapability.Text, false));
        Assert.True(queued.Task.Wait(TimeSpan.FromSeconds(3)));
        Assert.False(check.IsCompleted);
        for (var i = 0; i < 50; i++)
        {
            var unrelated = new Profile { Apps = [new SavedApp { Executable = "other.exe" }] };
            document.Profiles.Add(unrelated); document.Profiles[1].Apps.Add(new SavedApp());
            document.Profiles.Remove(unrelated); document.Profiles[1].Apps.RemoveAt(document.Profiles[1].Apps.Count - 1);
        }
        Assert.True(await check); Assert.Equal(1, reads);
        queued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var cancelled = Task.Run(() => service.IsConfirmedAsync(app.Id, context, TargetState.Minimized, InputCapability.Text, false, cancellation.Token));
        Assert.True(queued.Task.Wait(TimeSpan.FromSeconds(3)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, reads);
        // The next check sees the current rule, rather than a stale constructor snapshot.
        app.Executable = "changed.exe";
        Assert.False(await Task.Run(() => service.IsConfirmedAsync(app.Id, context, TargetState.Minimized, InputCapability.Text, false)));
    }

    [AvaloniaTheory]
    [InlineData("Space")]
    [InlineData("legacy-unknown-key")]
    public async Task SelectedTextTestIgnoresOtherUnconfirmedOrInvalidAction(string otherKey)
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        state.Macro.Steps = [new("Text", "harmless", 0), new("Key", otherKey, 0)];
        var app = state.Profile.Apps.Single(a => a.Id == state.Macro.AppId);
        PlaybackEligibilityTests.AddEvidence(state.Document, app, InputCapability.Text, TargetState.Minimized);
        var executor = new HarmlessExecutor(); var keys = new HarmlessHotkeys(); var guard = new InputActivityGuard();
        var controller = new WorkspacePlaybackController(state.Document, new(executor, TimeProvider.System), guard, keys, keys);
        var window = new MainWindow(state, controller, keys); window.Show();
        try
        {
            PlaybackUiTests.Click(window, "Tab_Macros");
            Assert.False(PlaybackUiTests.Find<Button>(window, "RunSelected").IsEnabled);
            Assert.True(PlaybackUiTests.Find<Button>(window, "TestSelected").IsEnabled);
            PlaybackUiTests.Click(window, "TestSelected");
            await PlaybackControllerTests.Until(() => controller.Sessions.GetValueOrDefault(state.Macro.Id)?.State == PlaybackState.Completed);
            Assert.IsType<CompiledAction.Text>(Assert.Single(executor.Actions));
            PlaybackUiTests.Find<TextBox>(window, "ActionValue").Text = "unapplied";
            Assert.False(PlaybackUiTests.Find<Button>(window, "TestSelected").IsEnabled);
            PlaybackUiTests.Click(window, "TestSelected"); Assert.Single(executor.Requests);
            PlaybackUiTests.Click(window, "DiscardDraft");
            PlaybackUiTests.Click(window, "SelectStep_1");
            Assert.False(PlaybackUiTests.Find<Button>(window, "TestSelected").IsEnabled);
            PlaybackUiTests.Click(window, "SelectStep_0");
            keys.LoseHealth(); await PlaybackControllerTests.Until(() => !PlaybackUiTests.Find<Button>(window, "TestSelected").IsEnabled);
            PlaybackUiTests.Click(window, "TestSelected"); Assert.Single(executor.Requests);
        }
        finally { window.Close(); await controller.DisposeAsync(); }
    }

    [AvaloniaFact]
    public void ActionSpecificHelpFollowsEditorAndCompatibilitySelection()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        state.Macro.Steps = [new("Wheel", "120", 0), new("Text", "harmless", 0)];
        var catalog = new ContextCatalog(); var keys = new HarmlessHotkeys();
        var controller = new UiPlayback(state.Document);
        var service = new CompatibilityService(state.Document, catalog, () => null,
            (_, _, _) => throw new InvalidOperationException("Help must not send input"), (_, _, _) => Task.CompletedTask);
        var ui = new CompatibilityUiServices(service, catalog, (_, _) => Task.FromResult<IReadOnlyList<TargetWindow>>([]), _ => new(null));
        var window = new MainWindow(state, controller, keys, compatibility: ui); window.Show();
        try
        {
            PlaybackUiTests.Click(window, "Tab_Macros");
            AssertWheelHelp(VisibleText(window));
            PlaybackUiTests.Find<ComboBox>(window, "ActionKind").SelectedItem = "Text";
            Assert.DoesNotContain("signed vertical", VisibleText(window), StringComparison.OrdinalIgnoreCase);
            PlaybackUiTests.Click(window, "Tab_Compatibility");
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilityAction").SelectedItem = "Wheel";
            AssertWheelHelp(VisibleText(window));
            foreach (var kind in new[] { "Key", "Shortcut" })
            {
                PlaybackUiTests.Find<ComboBox>(window, "CompatibilityAction").SelectedItem = kind;
                var text = VisibleText(window);
                Assert.Contains("keyboard-state", text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("observed response", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("signed vertical", text, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { window.Close(); }
    }

    private static string VisibleText(Window window)
    {
        window.UpdateLayout();
        return string.Join(" ", window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
    }
    private static void AssertWheelHelp(string text)
    {
        Assert.Contains("signed vertical", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current pointer", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client bounds", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minimized", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OutsideClient", text, StringComparison.Ordinal);
    }

    private sealed class ContextCatalog : IWindowCatalog, ITargetContext
    {
        public TargetContext? Context { get; set; }
        public event Action<TargetToken>? TargetLost;
        public void Lose(TargetToken token) => TargetLost?.Invoke(token);
        public Task<IReadOnlyList<TargetWindow>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TargetWindow>>([]);
        public Task<ResolutionResult> ResolveAsync(TargetRule rule, TargetToken? selected = null, CancellationToken ct = default) => Task.FromResult<ResolutionResult>(Context is { } context ? new ResolutionResult.Matched(context.Window) : new ResolutionResult.Missing());
        public ValueTask<TargetContextResult> GetAsync(TargetToken token, CancellationToken ct = default) => ValueTask.FromResult(new TargetContextResult(Context));
    }

    private sealed class HarmlessInput : IInputPlayer, IScreenInputPlayer, IPermissionService
    {
        public System.Collections.Concurrent.ConcurrentQueue<InputCommand> Commands { get; } = new();
        public Task<PermissionResult> CheckAsync(TargetToken target, CancellationToken ct = default) => Task.FromResult(new PermissionResult(true));
        public ValueTask<DeliveryResult> SendAsync(TargetToken target, InputCommand command, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); Commands.Enqueue(command); return ValueTask.FromResult(new DeliveryResult(true)); }
        public ValueTask<DeliveryResult> ReleaseHeldAsync(TargetToken target, CancellationToken ct = default) => ValueTask.FromResult(new DeliveryResult(true));
        public ScreenGeometry ReadGeometry() => throw new InvalidOperationException("No Screen fallback");
        public ScreenPointResult ReadPointer() => throw new InvalidOperationException("No Screen fallback");
        public ValueTask<DeliveryResult> SendAsync(InputCommand command, CancellationToken ct = default) => throw new InvalidOperationException("No Screen fallback");
        public ValueTask<DeliveryResult> ReleaseHeldAsync(CancellationToken ct = default) => throw new InvalidOperationException("No Screen cleanup");
    }
    private sealed class LossCleanupExecutor(TargetToken token) : IPlaybackExecutor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cleaning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<PlaybackPreparation> PrepareAsync(PlaybackRequest request, CancellationToken ct) => ValueTask.FromResult(new PlaybackPreparation(new(request.Target.SavedAppId, token, "original", "surface", new(800, 600, 1), request.Target.State)));
        public ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken ct) => ValueTask.FromResult(new DeliveryResult(true));
        public async ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, CompiledAction action, CancellationToken ct)
        {
            Entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            Cleaning.SetResult(); await Release.Task;
            return new(new(false, new("Cancelled", "cancelled")));
        }
    }
}
