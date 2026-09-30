using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Xunit;
namespace Macrofy.App.Tests;
public class CompatibilityServiceTests
{
    private sealed class ContextSource : ITargetContext
    {
        public TargetContext? Context;
        public ValueTask<TargetContextResult> GetAsync(TargetToken target, CancellationToken cancellationToken = default) => ValueTask.FromResult(new TargetContextResult(Context));
    }
    private sealed class Lease(Action? release = null) : IDisposable { public void Dispose() => release?.Invoke(); }
    private sealed class Fixture
    {
        public WorkspaceDocument Document = WorkspaceDocument.CreateDefault();
        public ContextSource Source = new();
        public Guid AppId => Document.Profiles[0].Apps[0].Id;
        public TargetToken Token = new(Guid.NewGuid());
        public GestureResult Result = new(new(true));
        public bool Busy;
        public bool SaveFails;
        public Fixture() => Source.Context = new(new(Token, new("CookieRun", Document.Profiles[0].Apps[0].Executable), "CookieRun: Crumble - Idle RPG", true, new(800, 600, 1)), false, "surface");
        public CompatibilityService Service() => new(Document, Source, () => Busy ? null : new Lease(), (_, _, _) => ValueTask.FromResult(Result), (_, _) => SaveFails ? Task.FromException(new IOException("save conflict")) : Task.CompletedTask);
        public CompiledAction Click = new CompiledAction.Click(new(20, 30), CoordinateMode.FixedPixels, 0);
    }
    [Fact]
    public async Task PendingObservationAndSaveKeepExclusiveLeaseUntilConfirmationOrDiscard()
    {
        var f = new Fixture(); var occupied = false;
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CompatibilityService(f.Document, f.Source,
            () => { if (occupied) return null; occupied = true; return new Lease(() => occupied = false); },
            (_, _, _) => ValueTask.FromResult(new GestureResult(new(true))), (_, _) => saving.Task);
        var attempt = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        Assert.True(occupied);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken));
        var confirmation = service.ConfirmAsync(attempt, true, TestContext.Current.CancellationToken);
        Assert.True(occupied); attempt.Dispose(); Assert.True(occupied);
        saving.SetResult(); await confirmation; Assert.False(occupied);
        var discarded = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        discarded.Dispose(); Assert.False(occupied);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(discarded, true, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task CancelledDeliveryReleasesExclusiveOwnershipWithoutEvidence()
    {
        var f = new Fixture(); var occupied = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var service = new CompatibilityService(f.Document, f.Source,
            () => { occupied = true; return new Lease(() => occupied = false); },
            (_, _, _) => { cancellation.Cancel(); return ValueTask.FromResult(new GestureResult(new(true))); }, (_, _) => Task.CompletedTask);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, cancellation.Token));
        Assert.False(occupied); Assert.Empty(f.Document.CompatibilityEvidence);
    }
    [Fact]
    public async Task NonFiniteTestPointCannotBeDelivered()
    {
        var f = new Fixture(); var service = f.Service();
        var invalid = new CompiledAction.Click(new(double.NaN, 20), CoordinateMode.FixedPixels, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, invalid, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task ObservedEvidencePersistsAndReopensThroughWorkspaceStore()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-evidence-" + Guid.NewGuid());
        try
        {
            var f = new Fixture(); var store = new WorkspaceStore(folder);
            var service = new CompatibilityService(f.Document, f.Source, () => new Lease(), (_, _, _) => ValueTask.FromResult(new GestureResult(new(true))),
                (document, _) => { store.Save(document); return Task.CompletedTask; });
            var attempt = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
            await service.ConfirmAsync(attempt, true, TestContext.Current.CancellationToken);
            var reopened = new WorkspaceStore(folder).Load();
            var saved = Assert.Single(reopened.CompatibilityEvidence); Assert.True(saved.ObservedSuccess); Assert.Equal(InputCapability.Click, saved.Capability);
            var restored = new CompatibilityService(reopened, f.Source, () => new Lease(), (_, _, _) => ValueTask.FromResult(f.Result), (_, _) => Task.CompletedTask);
            Assert.True(restored.IsConfirmed(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task DeliberateTestNeedsNoEvidenceAndRestoredTargetCanConfirmMinimizedObservation()
    {
        var f = new Fixture(); var service = f.Service();
        var attempt = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        f.Source.Context = f.Source.Context! with { Window = f.Source.Context.Window with { IsMinimized = false } };
        var evidence = await service.ConfirmAsync(attempt, true, TestContext.Current.CancellationToken);
        f.Source.Context = f.Source.Context with { Window = f.Source.Context.Window with { IsMinimized = true } };
        Assert.True(service.IsConfirmed(f.AppId, f.Source.Context, TargetState.Minimized, InputCapability.Click, true));
        Assert.Equal(TargetState.Minimized, evidence.State);
        foreach (var capability in new[] { InputCapability.Key, InputCapability.Shortcut, InputCapability.Text, InputCapability.Wheel })
            Assert.False(service.IsConfirmed(f.AppId, f.Source.Context, TargetState.Minimized, capability, true));
        var json = System.Text.Json.JsonSerializer.Serialize(f.Document);
        Assert.DoesNotContain(f.Token.Id.ToString(), json); Assert.DoesNotContain("HWND", json); Assert.DoesNotContain("PID", json);
    }
    [Fact]
    public async Task NegativeConfirmationSupersedesFutureDatedSuccessAndFailedSaveRestoresEvidence()
    {
        var f = new Fixture(); var service = f.Service();
        var first = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        var previous = await service.ConfirmAsync(first, true, TestContext.Current.CancellationToken);
        f.Document.CompatibilityEvidence[0] = previous with { TestedAt = DateTimeOffset.MaxValue };
        var second = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        f.SaveFails = true;
        await Assert.ThrowsAsync<IOException>(() => service.ConfirmAsync(second, false, TestContext.Current.CancellationToken));
        Assert.True(service.IsConfirmed(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true));
        f.SaveFails = false; await service.ConfirmAsync(second, false, TestContext.Current.CancellationToken);
        Assert.False(service.IsConfirmed(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true));
    }
    [Theory]
    [InlineData(false, false, false)] [InlineData(true, true, false)] [InlineData(true, false, true)]
    public async Task FailedPartialOrUncleanAttemptsCannotConfirm(bool queued, bool deliveryError, bool cleanupError)
    {
        var f = new Fixture { Result = new(new(queued, deliveryError ? new("partial", "partial") : null), cleanupError ? new("cleanup", "cleanup") : null) };
        var service = f.Service(); var attempt = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(attempt, true, TestContext.Current.CancellationToken)); Assert.Empty(f.Document.CompatibilityEvidence);
    }
    [Fact]
    public async Task BusyCancelledWrongIdentityAndWrongStateTestsAreRejected()
    {
        var f = new Fixture(); var service = f.Service(); f.Busy = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken));
        f.Busy = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, new(true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BeginAttemptAsync(Guid.NewGuid(), f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BeginAttemptAsync(f.AppId, f.Token, TargetState.BackgroundVisible, f.Click, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task ChangedSurfaceAndClosedTargetRejectConfirmationAndEvidenceMatchesExactly()
    {
        var f = new Fixture(); var service = f.Service(); var context = f.Source.Context!;
        var attempt = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        await service.ConfirmAsync(attempt, true, TestContext.Current.CancellationToken);
        foreach (var changed in new[] { context with { SurfaceFingerprint = "other" }, context with { Window = context.Window with { Title = "other" } }, context with { Window = context.Window with { App = new("other", "other.exe") } } })
            Assert.False(service.IsConfirmed(f.AppId, changed, TargetState.Minimized, InputCapability.Click, true));
        var resized = context with { Window = context.Window with { Geometry = new(900, 600, 1) } };
        Assert.False(service.IsConfirmed(f.AppId, resized, TargetState.Minimized, InputCapability.Click, true));
        Assert.True(service.IsConfirmed(f.AppId, resized, TargetState.Minimized, InputCapability.Click, false));
        Assert.False(service.IsConfirmed(f.AppId, context, TargetState.BackgroundVisible, InputCapability.Click, true));
        var pending = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        f.Source.Context = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(pending, true, TestContext.Current.CancellationToken));
    }
}

