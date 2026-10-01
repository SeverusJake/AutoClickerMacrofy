using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.Core.Tests;

public sealed class TargetLossTests
{
    [Theory]
    [InlineData("paused")]
    [InlineData("wait")]
    [InlineData("interval")]
    public async Task OriginalTargetLossCancelsIdleSessionAndKeepsOtherTargetAlive(string phase)
    {
        var clock = new PlaybackTestClock(); var catalog = new Catalog(); var executor = new Executor();
        await using var coordinator = new PlaybackCoordinator(executor, clock, catalog);
        var lost = new TargetToken(Guid.NewGuid()); var other = new TargetToken(Guid.NewGuid());
        var request = Request(lost, phase == "interval" ? 0 : 600000) with { Repeat = phase == "interval" ? 0 : 1, IntervalMs = 600000 };
        var survivor = Request(other, 600000);
        await coordinator.StartAsync(request); await coordinator.StartAsync(survivor);
        await Until(() => clock.PendingTimers == 2);
        if (phase == "paused") coordinator.TogglePause(request.MacroId);
        catalog.Lose(lost);
        await coordinator.WaitForCompletionAsync(request.MacroId).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(PlaybackState.Stopped, coordinator.Snapshots[request.MacroId].State);
        Assert.Equal("TargetLost", coordinator.Snapshots[request.MacroId].DeliveryError?.Code);
        Assert.True(coordinator.Snapshots[survivor.MacroId].IsActive);
        clock.Advance(600000);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(PlaybackState.Completed, coordinator.Snapshots[survivor.MacroId].State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LossDuringPreparationCannotPublishLostBinding(bool explicitlySelected)
    {
        var clock = new PlaybackTestClock(); var catalog = new Catalog(); var executor = new Executor();
        var entered = Signal(); var release = Signal(); var token = new TargetToken(Guid.NewGuid());
        CancellationToken preparationToken = default;
        executor.Prepare = async (request, ct) => { preparationToken = ct; entered.SetResult(); await release.Task; return new(Binding(request, token)); };
        await using var coordinator = new PlaybackCoordinator(executor, clock, catalog);
        var request = Request(token, 600000);
        if (!explicitlySelected) request = request with { Target = request.Target with { SelectedSurface = null } };
        var started = coordinator.StartAsync(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        catalog.Lose(token);
        try { if (explicitlySelected) Assert.True(preparationToken.IsCancellationRequested); }
        finally { release.SetResult(); }
        var result = await started.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(result.Queued); Assert.Equal("TargetLost", result.Error?.Code);
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, clock.PendingTimers);
    }

    [Fact]
    public async Task LossKeepsDispatchGateUntilCleanupAndPreservesBothDiagnostics()
    {
        var clock = new PlaybackTestClock(); var catalog = new Catalog(); var executor = new Executor();
        var lost = new TargetToken(Guid.NewGuid()); var cleaning = Signal(); var cleanup = Signal();
        var entered = Signal(); var sends = 0;
        executor.Execute = async (binding, ct) =>
        {
            Interlocked.Increment(ref sends);
            if (binding.Token != lost) return new(new(true));
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            cleaning.SetResult(); await cleanup.Task;
            return new(new(false, new("Cancelled", "delivery cancelled")), new("CleanupFailed", "release failed"));
        };
        await using var coordinator = new PlaybackCoordinator(executor, clock, catalog);
        var first = Request(lost, 0) with { Actions = [new CompiledAction.Text("x", 0)] };
        var other = Request(new(Guid.NewGuid()), 0) with { Actions = [new CompiledAction.Text("x", 0)] };
        try
        {
            await coordinator.StartAsync(first); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await coordinator.StartAsync(other); catalog.Lose(lost);
            await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(coordinator.WaitForCompletionAsync(first.MacroId).IsCompleted);
            Assert.False((await coordinator.StartAsync(first)).Queued);
            Assert.Equal(1, Volatile.Read(ref sends));
        }
        finally { cleanup.TrySetResult(); }
        await coordinator.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("TargetLost", coordinator.Snapshots[first.MacroId].DeliveryError?.Code);
        Assert.Equal("CleanupFailed", coordinator.Snapshots[first.MacroId].CleanupError?.Code);
        Assert.Equal(PlaybackState.Completed, coordinator.Snapshots[other.MacroId].State);
    }

    [Fact]
    public async Task ShutdownUnsubscribesTargetLoss()
    {
        var catalog = new Catalog(); var coordinator = new PlaybackCoordinator(new Executor(), new PlaybackTestClock(), catalog);
        Assert.Equal(1, catalog.Subscribers);
        await coordinator.DisposeAsync(); await coordinator.DisposeAsync();
        Assert.Equal(0, catalog.Subscribers);
    }

    private static PlaybackRequest Request(TargetToken token, int wait) => new(Guid.NewGuid(), "macro", "profile",
        new(Guid.NewGuid(), new(new("app", "app.exe"), "*"), TargetState.BackgroundVisible, token), [new CompiledAction.Wait(wait, 0)], 1, 0, 0);
    private static PlaybackBinding Binding(PlaybackRequest request, TargetToken token) => new(request.Target.SavedAppId, token, "app", "surface", new(100, 100, 1), request.Target.State);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(1, timeout.Token);
    }
    private sealed class Executor : IPlaybackExecutor
    {
        public Func<PlaybackRequest, CancellationToken, ValueTask<PlaybackPreparation>>? Prepare;
        public Func<PlaybackBinding, CancellationToken, ValueTask<GestureResult>>? Execute;
        public ValueTask<PlaybackPreparation> PrepareAsync(PlaybackRequest request, CancellationToken ct) => Prepare?.Invoke(request, ct) ?? ValueTask.FromResult(new PlaybackPreparation(Binding(request, request.Target.SelectedSurface!.Value)));
        public ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken ct) => ValueTask.FromResult(new DeliveryResult(true));
        public ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, CompiledAction action, CancellationToken ct) => Execute?.Invoke(binding, ct) ?? ValueTask.FromResult(new GestureResult(new(true)));
    }
    private sealed class Catalog : IWindowCatalog
    {
        private Action<TargetToken>? lost;
        public int Subscribers => lost?.GetInvocationList().Length ?? 0;
        public event Action<TargetToken>? TargetLost { add => lost += value; remove => lost -= value; }
        public void Lose(TargetToken token) => lost?.Invoke(token);
        public Task<IReadOnlyList<TargetWindow>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ResolutionResult> ResolveAsync(TargetRule rule, TargetToken? selected = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
