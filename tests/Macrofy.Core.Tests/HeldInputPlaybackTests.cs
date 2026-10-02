using System.Collections.Concurrent;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.Core.Tests;

public sealed class HeldInputPlaybackTests
{
    private static readonly KeyIdentity W = new("W");

    [Fact]
    public async Task HoldClickPressesThenReleasesAfterHold()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([new CompiledAction.Click(new(10, 20), CoordinateMode.FixedPixels, 0, MouseButton.Right, 100)]);
        Assert.True((await coordinator.StartAsync(request)).Queued);
        await Until(() => executor.Sent.Count == 1 && clock.PendingTimers == 1);
        Assert.Equal(PlaybackState.Waiting, coordinator.Snapshots[request.MacroId].State);
        clock.Advance(99); Assert.Single(executor.Sent);
        clock.Advance(1);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { "MouseDown Right 0", "MouseUp Right 100" }, executor.Sent.Select(s => s.Text));
        Assert.Empty(executor.Released);
        Assert.Equal(PlaybackState.Completed, coordinator.Snapshots[request.MacroId].State);
    }

    [Fact]
    public async Task StopDuringHoldReleasesHeldButtonThroughExecutor()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([new CompiledAction.Click(new(10, 20), CoordinateMode.FixedPixels, 0, MouseButton.Left, 1000)]);
        await coordinator.StartAsync(request);
        await Until(() => executor.Sent.Count == 1 && clock.PendingTimers == 1);
        coordinator.Stop(request.MacroId);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(MouseButton.Left, Assert.Single(Assert.Single(executor.Released)).Button);
        Assert.Single(executor.Sent);
        Assert.Equal(PlaybackState.Stopped, coordinator.Snapshots[request.MacroId].State);
    }

    [Fact]
    public async Task PauseReleasesHeldKeyAndResumeDoesNotRepress()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([new CompiledAction.KeyDown([W], 0), new CompiledAction.Wait(100, 0), new CompiledAction.KeyUp([W], 0)]);
        await coordinator.StartAsync(request);
        await Until(() => executor.Sent.Count == 1 && clock.PendingTimers == 1);
        coordinator.TogglePause(request.MacroId);
        await Until(() => executor.Released.Count == 1 && coordinator.Snapshots[request.MacroId].State == PlaybackState.Paused);
        Assert.Equal("W", Assert.Single(executor.Released.Single()).Key!.LogicalKey);
        coordinator.TogglePause(request.MacroId);
        await Until(() => clock.PendingTimers == 1);
        clock.Advance(100);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { "KeyDown W 0" }, executor.Sent.Select(s => s.Text));
        Assert.Single(executor.Released);
        Assert.Equal(PlaybackState.Completed, coordinator.Snapshots[request.MacroId].State);
    }

    [Fact]
    public async Task DuplicateDownAndUnmatchedUpAreSkippedAndHoldSurvivesLoopsUntilEnd()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock);
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([new CompiledAction.KeyDown([W], 0), new CompiledAction.KeyDown([W], 0), new CompiledAction.MouseUp(new(1, 1), CoordinateMode.FixedPixels, MouseButton.Left, 0)], repeat: 2);
        await coordinator.StartAsync(request);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { "KeyDown W 0" }, executor.Sent.Select(s => s.Text));
        Assert.Equal("W", Assert.Single(Assert.Single(executor.Released)).Key!.LogicalKey);
        Assert.Equal(2, coordinator.Snapshots[request.MacroId].CompletedLoops);
        Assert.Equal(PlaybackState.Completed, coordinator.Snapshots[request.MacroId].State);
    }

    [Fact]
    public async Task FailedReleaseAtEndReportsCleanupError()
    {
        var clock = new PlaybackTestClock(); var executor = new Executor(clock) { ReleaseResult = new(new(false, new("CleanupFailed", "stuck"))) };
        await using var coordinator = new PlaybackCoordinator(executor, clock);
        var request = Request([new CompiledAction.KeyDown([W], 0)]);
        await coordinator.StartAsync(request);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var snapshot = coordinator.Snapshots[request.MacroId];
        Assert.Equal(PlaybackState.Error, snapshot.State); Assert.Equal("CleanupFailed", snapshot.CleanupError!.Code);
    }

    private static PlaybackRequest Request(IReadOnlyList<CompiledAction> actions, int repeat = 1) =>
        new(Guid.NewGuid(), "Macro", "Profile", new(null, null, null, null), actions, repeat, 0, 0);
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(1, timeout.Token);
    }

    private sealed class Executor(PlaybackTestClock clock) : IPlaybackExecutor
    {
        public ConcurrentQueue<(string Text, CompiledAction Action)> Sent { get; } = new();
        public ConcurrentQueue<IReadOnlyList<HeldInput>> Released { get; } = new();
        public GestureResult ReleaseResult { get; init; } = new(new(true));
        public ValueTask<PlaybackPreparation> PrepareAsync(PlaybackRequest request, CancellationToken token) => ValueTask.FromResult(new PlaybackPreparation(new(null, null, "Screen", null, null, null)));
        public ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken token) => ValueTask.FromResult(new DeliveryResult(true));
        public ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, CompiledAction action, CancellationToken token)
        {
            var at = clock.GetTimestamp() / TimeSpan.TicksPerMillisecond;
            var text = action switch
            {
                CompiledAction.MouseDown d => $"MouseDown {d.Button} {at}",
                CompiledAction.MouseUp u => $"MouseUp {u.Button} {at}",
                CompiledAction.KeyDown k => $"KeyDown {string.Join("+", k.Keys.Select(x => x.LogicalKey))} {at}",
                CompiledAction.KeyUp k => $"KeyUp {string.Join("+", k.Keys.Select(x => x.LogicalKey))} {at}",
                _ => $"{action.GetType().Name} {at}"
            };
            Sent.Enqueue((text, action));
            return ValueTask.FromResult(new GestureResult(new(true)));
        }
        public ValueTask<GestureResult> ReleaseHeldAsync(PlaybackBinding binding, IReadOnlyList<HeldInput> inputs, CancellationToken token)
        { Released.Enqueue(inputs); return ValueTask.FromResult(ReleaseResult); }
    }
}
