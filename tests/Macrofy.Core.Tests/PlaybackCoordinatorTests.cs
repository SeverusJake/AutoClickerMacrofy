using System.Collections.Concurrent;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.Core.Tests;

public sealed class PlaybackCoordinatorTests
{
    [Fact]
    public async Task FiftyMillisecondWaitDoesNotFinishAtFortyNine()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([new CompiledAction.Wait(50, 0), Key()]);
        Assert.True((await coordinator.StartAsync(request)).Queued);
        await Until(() => clock.PendingTimers == 1);
        clock.Advance(49);
        Assert.Empty(executor.Sent);
        Assert.Equal(TimeSpan.FromMilliseconds(1), coordinator.Snapshots[request.MacroId].RemainingWait);
        clock.Advance(1);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(50, Assert.Single(executor.Sent).At);
        Assert.Equal(PlaybackState.Completed, coordinator.Snapshots[request.MacroId].State);
    }

    [Fact]
    public async Task WaitAfterAndIntervalsBeginAfterActionCompletionAndFinalDelayCompletes()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([new CompiledAction.Wait(50, 20), Key(30)], repeat: 2, interval: 100);
        await coordinator.StartAsync(request);
        await AdvanceWait(clock, coordinator, request, 50);
        await AdvanceWait(clock, coordinator, request, 20);
        await Until(() => executor.Sent.Count == 1);
        Assert.Equal(70, executor.Sent.First().At);
        await AdvanceWait(clock, coordinator, request, 30);
        await AdvanceWait(clock, coordinator, request, 100);
        await AdvanceWait(clock, coordinator, request, 50);
        await AdvanceWait(clock, coordinator, request, 20);
        await Until(() => executor.Sent.Count == 2);
        Assert.Equal(new long[] { 70, 270 }, executor.Sent.Select(s => s.At));
        Assert.NotEqual(PlaybackState.Completed, coordinator.Snapshots[request.MacroId].State);
        await AdvanceWait(clock, coordinator, request, 30);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, coordinator.Snapshots[request.MacroId].CompletedLoops);
        Assert.Equal(4, coordinator.Snapshots[request.MacroId].CompletedSteps);
    }

    [Fact]
    public async Task PausePreservesWaitRemainderAndExcludesPausedTime()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([new CompiledAction.Wait(50, 0), Key()]);
        await coordinator.StartAsync(request); await Until(() => clock.PendingTimers == 1);
        clock.Advance(20); coordinator.TogglePause(request.MacroId);
        await Until(() => coordinator.Snapshots[request.MacroId].State == PlaybackState.Paused);
        clock.Advance(1000);
        Assert.Equal(TimeSpan.FromMilliseconds(30), coordinator.Snapshots[request.MacroId].RemainingWait);
        Assert.Equal(TimeSpan.FromMilliseconds(20), coordinator.Snapshots[request.MacroId].ActiveElapsed);
        coordinator.TogglePause(request.MacroId); await Until(() => clock.PendingTimers == 1);
        clock.Advance(29); Assert.Empty(executor.Sent);
        clock.Advance(1); await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1050, Assert.Single(executor.Sent).At);
        Assert.Equal(TimeSpan.FromMilliseconds(50), coordinator.Snapshots[request.MacroId].ActiveElapsed);
    }

    [Fact]
    public async Task PauseDuringGestureWaitsForBalancedBoundaryWithoutCancellingGesture()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        var release = Signal(); CancellationToken gestureToken = default;
        executor.Execute = async (_, _, token) => { gestureToken = token; await release.Task; return new(new(true)); };
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([Key(), Key()]); await coordinator.StartAsync(request);
        await Until(() => executor.Sent.Count == 1); coordinator.TogglePause(request.MacroId);
        Assert.Equal(PlaybackState.Pausing, coordinator.Snapshots[request.MacroId].State);
        Assert.False(gestureToken.IsCancellationRequested);
        release.SetResult(); await Until(() => coordinator.Snapshots[request.MacroId].State == PlaybackState.Paused);
        Assert.Single(executor.Sent); coordinator.TogglePause(request.MacroId);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(2, executor.Sent.Count);
    }

    [Fact]
    public async Task PauseDuringValidationStopsBeforeUndeliveredGesture()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        var entered = Signal(); var release = Signal();
        executor.Validate = async (_, _) => { entered.TrySetResult(); await release.Task; return new(true); };
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([Key()]); await coordinator.StartAsync(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        coordinator.TogglePause(request.MacroId); release.SetResult();
        await Until(() => coordinator.Snapshots[request.MacroId].State == PlaybackState.Paused);
        Assert.Empty(executor.Sent);
        coordinator.TogglePause(request.MacroId);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Single(executor.Sent);
    }

    [Fact]
    public async Task FrozenActionsAndOldSnapshotsSurviveCallerMutation()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var keys = new List<KeyIdentity> { new("Ctrl"), new("K") };
        var actions = new List<CompiledAction> { new CompiledAction.Key(keys, 0) };
        var request = Request(actions, start: 50); await coordinator.StartAsync(request);
        var snapshot = coordinator.Snapshots[request.MacroId];
        actions.Clear(); keys.Clear();
        Assert.Single(snapshot.Actions); Assert.Equal(2, ((CompiledAction.Key)snapshot.Actions[0]).Keys.Count);
        await AdvanceWait(clock, coordinator, request, 50);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Single(executor.Sent); Assert.Equal(0, snapshot.CompletedSteps);
        Assert.Throws<NotSupportedException>(() => ((IList<CompiledAction>)snapshot.Actions).Clear());
    }

    [Fact]
    public async Task InfiniteZeroDelaySynchronousExecutorReturnsStartAndStops()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([Key()], repeat: 0);
        await Task.Run(() => coordinator.StartAsync(request)).WaitAsync(TimeSpan.FromSeconds(3));
        await Until(() => executor.Sent.Count > 2); coordinator.Stop(request.MacroId);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(PlaybackState.Stopped, coordinator.Snapshots[request.MacroId].State);
    }

    [Fact]
    public async Task DuplicateStartAndImmediateRestartDuringCleanupAreRejected()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock); var cleanup = Signal(); var cleaning = Signal();
        executor.Execute = async (_, _, token) => { try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { cleaning.SetResult(); } await cleanup.Task; return new(new(false, new("Cancelled", "Stopped"))); };
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([Key()]); await coordinator.StartAsync(request); await Until(() => executor.Sent.Count == 1);
        Assert.False((await coordinator.StartAsync(request)).Queued);
        coordinator.Stop(request.MacroId); await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False((await coordinator.StartAsync(request)).Queued); Assert.False(coordinator.WaitForIdleAsync().IsCompleted);
        cleanup.SetResult(); await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        executor.Execute = null; Assert.True((await coordinator.StartAsync(request)).Queued);
    }

    [Fact]
    public async Task SharedGateCancelsWaiterAndKeepsOtherSessionAliveDuringCleanup()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock); var cleanup = Signal(); var cleaning = Signal();
        var first = Request([Key()]); var waiter = Request([Key()]); var survivor = Request([Key()]);
        executor.Execute = async (binding, _, token) => {
            if (binding.Name != first.MacroName) return new(new(true));
            try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { cleaning.SetResult(); }
            await cleanup.Task; return new(new(false, new("Cancelled", "Stopped")));
        };
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        await coordinator.StartAsync(first); await Until(() => executor.Sent.Count == 1);
        await coordinator.StartAsync(waiter); await coordinator.StartAsync(survivor);
        coordinator.Stop(waiter.MacroId); coordinator.Stop(first.MacroId);
        await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Until(() => coordinator.Snapshots[waiter.MacroId].State == PlaybackState.Stopped);
        Assert.Single(executor.Sent); Assert.True(coordinator.Snapshots[survivor.MacroId].IsActive);
        cleanup.SetResult(); await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, executor.MaximumConcurrency);
        Assert.Equal(new[] { first.MacroName, survivor.MacroName }, executor.Sent.Select(s => s.Name));
    }

    [Fact]
    public async Task StopAllCancelsPreparationAcrossProfiles()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        executor.Prepare = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(null); };
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var first = Request([Key()]); var second = Request([Key()]) with { ProfileName = "Other profile" };
        var start1 = coordinator.StartAsync(first); var start2 = coordinator.StartAsync(second);
        Assert.Equal(2, coordinator.Snapshots.Values.Count(s => s.State == PlaybackState.Starting));
        coordinator.StopAll(); Assert.False((await start1).Queued); Assert.False((await start2).Queued);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.All(coordinator.Snapshots.Values, s => Assert.Equal(PlaybackState.Stopped, s.State));
    }

    [Fact]
    public async Task ShutdownCancelsCountdownPausedAndRepeatingSessions()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        var coordinator = new PlaybackCoordinator(executor, clock);
        var first = Request([Key()], start: 3000); var second = Request([new CompiledAction.Wait(100, 0)], repeat: 0);
        await coordinator.StartAsync(first); await coordinator.StartAsync(second); coordinator.TogglePauseAll();
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.All(coordinator.Snapshots.Values, s => Assert.Equal(PlaybackState.Stopped, s.State));
        Assert.False((await coordinator.StartAsync(Request([Key()]))).Queued);
    }

    [Fact]
    public async Task ErrorsRetainDeliveryAndCleanupAndThrowingObserversCannotBreakPlayback()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        executor.Execute = (_, _, _) => ValueTask.FromResult(new GestureResult(new(false, new("Delivery", "failed")), new("Cleanup", "failed")));
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        coordinator.Changed += (_, _) => throw new InvalidOperationException("observer");
        var request = Request([Key(), Key()]); Assert.True((await coordinator.StartAsync(request)).Queued);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var snapshot = coordinator.Snapshots[request.MacroId];
        Assert.Equal(PlaybackState.Error, snapshot.State); Assert.Equal("Delivery", snapshot.DeliveryError!.Code);
        Assert.Equal("Cleanup", snapshot.CleanupError!.Code); Assert.Single(executor.Sent);
    }

    private static CompiledAction.Key Key(int delay = 0) => new([new("K")], delay);
    private static PlaybackRequest Request(IReadOnlyList<CompiledAction> actions, int repeat = 1, int interval = 0, int start = 0) =>
        new(Guid.NewGuid(), Guid.NewGuid().ToString(), "Profile", new(null, null, null, null), actions, repeat, interval, start);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(1, timeout.Token);
    }
    private static async Task AdvanceWait(PlaybackTestClock clock, PlaybackCoordinator coordinator, PlaybackRequest request, int milliseconds)
    {
        await Until(() => clock.PendingTimers == 1 && coordinator.Snapshots[request.MacroId].RemainingWait == TimeSpan.FromMilliseconds(milliseconds));
        clock.Advance(milliseconds);
    }
    private sealed class Executor(PlaybackTestClock clock) : IPlaybackExecutor
    {
        public ConcurrentQueue<(string Name, long At)> Sent { get; } = new();
        public Func<PlaybackRequest, CancellationToken, ValueTask<PlaybackPreparation>>? Prepare { get; set; }
        public Func<PlaybackBinding, CancellationToken, ValueTask<DeliveryResult>>? Validate { get; set; }
        public Func<PlaybackBinding, CompiledAction, CancellationToken, ValueTask<GestureResult>>? Execute { get; set; }
        private int concurrent;
        public int MaximumConcurrency { get; private set; }
        public ValueTask<PlaybackPreparation> PrepareAsync(PlaybackRequest request, CancellationToken token) => Prepare?.Invoke(request, token) ?? ValueTask.FromResult(new PlaybackPreparation(new(null, null, request.MacroName, null, null, null)));
        public ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken token) => Validate?.Invoke(binding, token) ?? ValueTask.FromResult(new DeliveryResult(true));
        public async ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, CompiledAction action, CancellationToken token)
        {
            MaximumConcurrency = Math.Max(MaximumConcurrency, Interlocked.Increment(ref concurrent));
            Sent.Enqueue((binding.Name, clock.GetTimestamp() / TimeSpan.TicksPerMillisecond));
            try { return Execute is null ? new(new(true)) : await Execute(binding, action, token); }
            finally { Interlocked.Decrement(ref concurrent); }
        }
    }
}
