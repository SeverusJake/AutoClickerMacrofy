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
        public TaskCompletionSource? LookupGate;
        public async ValueTask<TargetContextResult> GetAsync(TargetToken target, CancellationToken cancellationToken = default)
        {
            if (LookupGate is not null) await LookupGate.Task.WaitAsync(cancellationToken);
            return new TargetContextResult(Context);
        }
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
        public CompatibilityService Service() => new(Document, Source, () => Busy ? null : new Lease(), (_, _, _) => ValueTask.FromResult(Result), (snapshot, publish, _) =>
        {
            if (SaveFails) return Task.FromException(new IOException("save conflict"));
            snapshot(); publish(); return Task.CompletedTask;
        });
        public CompiledAction Click = new CompiledAction.Click(new(20, 30), CoordinateMode.FixedPixels, 0);
    }
    [Fact]
    public async Task PendingSuccessNeverAuthorizesOrLeaksThroughUnrelatedWorkspaceSave()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-pending-" + Guid.NewGuid());
        try
        {
            var f = new Fixture(); var store = new WorkspaceStore(folder);
            var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new CompatibilityService(f.Document, f.Source, () => new Lease(),
                (_, _, _) => ValueTask.FromResult(f.Result), async (snapshot, publish, _) => { await saving.Task; snapshot(); publish(); });
            var attempt = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
            var confirmation = service.ConfirmAsync(attempt, true, TestContext.Current.CancellationToken);
            Assert.False(await service.IsConfirmedAsync(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
            f.Document.Mode = "dark";
            store.Save(f.Document);
            var saved = new WorkspaceStore(folder).Load();
            Assert.Equal("dark", saved.Mode);
            Assert.Empty(saved.CompatibilityEvidence);
            saving.SetException(new IOException("save conflict"));
            await Assert.ThrowsAsync<IOException>(() => confirmation);
            Assert.Empty(f.Document.CompatibilityEvidence);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task DiscardDuringTargetLookupAbortsBeforePersistence()
    {
        var f = new Fixture(); var saves = 0;
        var service = new CompatibilityService(f.Document, f.Source, () => new Lease(),
            (_, _, _) => ValueTask.FromResult(f.Result), (snapshot, publish, _) => { saves++; snapshot(); publish(); return Task.CompletedTask; });
        var attempt = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Source.LookupGate = gate;
        var confirmation = service.ConfirmAsync(attempt, true, TestContext.Current.CancellationToken);
        attempt.Dispose();
        gate.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => confirmation);
        Assert.Equal(0, saves);
        Assert.Empty(f.Document.CompatibilityEvidence);
        Assert.False(await service.IsConfirmedAsync(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task DelayedCandidateSaveKeepsUnrelatedWorkspaceEditsAndEvidence()
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-rebase-" + Guid.NewGuid());
        try
        {
            var f = new Fixture(); var store = new WorkspaceStore(folder); store.Save(f.Document);
            var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new CompatibilityService(f.Document, f.Source, () => new Lease(),
                (_, _, _) => ValueTask.FromResult(f.Result), async (snapshot, publish, _) =>
                {
                    await saving.Task;
                    store.Save(snapshot()); publish();
                });
            var attempt = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
            var confirmation = service.ConfirmAsync(attempt, true, TestContext.Current.CancellationToken);
            f.Document.Mode = "dark";
            f.Document.Profiles[0].Name = "Updated profile";
            store.Save(f.Document);
            saving.SetResult();
            await confirmation;
            var reopened = new WorkspaceStore(folder).Load();
            Assert.Equal("dark", reopened.Mode);
            Assert.Equal("Updated profile", reopened.Profiles[0].Name);
            Assert.True(Assert.Single(reopened.CompatibilityEvidence).ObservedSuccess);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task PendingNegativeObservationBlocksEarlierSuccessUntilFailedSaveRestoresIt()
    {
        var f = new Fixture(); var initial = f.Service();
        var first = await initial.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        await initial.ConfirmAsync(first, true, TestContext.Current.CancellationToken);
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CompatibilityService(f.Document, f.Source, () => new Lease(),
            (_, _, _) => ValueTask.FromResult(f.Result), async (snapshot, publish, _) => { await saving.Task; snapshot(); publish(); });
        var second = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        var confirmation = service.ConfirmAsync(second, false, TestContext.Current.CancellationToken);
        Assert.False(await service.IsConfirmedAsync(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
        saving.SetException(new IOException("save conflict"));
        await Assert.ThrowsAsync<IOException>(() => confirmation);
        Assert.True(await service.IsConfirmedAsync(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task KeyAndShortcutEvidenceDoNotAuthorizeEachOther()
    {
        var key = new CompiledAction.Key([new("K")], 0);
        var shortcut = new CompiledAction.Key([new("Ctrl"), new("K")], 0);
        var keyFixture = new Fixture(); var keyService = keyFixture.Service();
        var keyAttempt = await keyService.BeginAttemptAsync(keyFixture.AppId, keyFixture.Token, TargetState.Minimized, key, TestContext.Current.CancellationToken);
        await keyService.ConfirmAsync(keyAttempt, true, TestContext.Current.CancellationToken);
        Assert.True(await keyService.IsConfirmedAsync(keyFixture.AppId, keyFixture.Source.Context!, TargetState.Minimized, InputCapability.Key, true, TestContext.Current.CancellationToken));
        Assert.False(await keyService.IsConfirmedAsync(keyFixture.AppId, keyFixture.Source.Context!, TargetState.Minimized, InputCapability.Shortcut, true, TestContext.Current.CancellationToken));
        var shortcutFixture = new Fixture(); var shortcutService = shortcutFixture.Service();
        var shortcutAttempt = await shortcutService.BeginAttemptAsync(shortcutFixture.AppId, shortcutFixture.Token, TargetState.Minimized, shortcut, TestContext.Current.CancellationToken);
        await shortcutService.ConfirmAsync(shortcutAttempt, true, TestContext.Current.CancellationToken);
        Assert.True(await shortcutService.IsConfirmedAsync(shortcutFixture.AppId, shortcutFixture.Source.Context!, TargetState.Minimized, InputCapability.Shortcut, true, TestContext.Current.CancellationToken));
        Assert.False(await shortcutService.IsConfirmedAsync(shortcutFixture.AppId, shortcutFixture.Source.Context!, TargetState.Minimized, InputCapability.Key, true, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task PendingObservationAndSaveKeepExclusiveLeaseUntilConfirmationOrDiscard()
    {
        var f = new Fixture(); var occupied = false;
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CompatibilityService(f.Document, f.Source,
            () => { if (occupied) return null; occupied = true; return new Lease(() => occupied = false); },
            (_, _, _) => ValueTask.FromResult(new GestureResult(new(true))), async (snapshot, publish, _) => { await saving.Task; snapshot(); publish(); });
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
            (_, _, _) => { cancellation.Cancel(); return ValueTask.FromResult(new GestureResult(new(true))); }, (snapshot, publish, _) => { snapshot(); publish(); return Task.CompletedTask; });
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
                (snapshot, publish, _) => { store.Save(snapshot()); publish(); return Task.CompletedTask; });
            var attempt = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
            await service.ConfirmAsync(attempt, true, TestContext.Current.CancellationToken);
            var reopened = new WorkspaceStore(folder).Load();
            var saved = Assert.Single(reopened.CompatibilityEvidence); Assert.True(saved.ObservedSuccess); Assert.Equal(InputCapability.Click, saved.Capability);
            var restored = new CompatibilityService(reopened, f.Source, () => new Lease(), (_, _, _) => ValueTask.FromResult(f.Result), (snapshot, publish, _) => { snapshot(); publish(); return Task.CompletedTask; });
            Assert.True(await restored.IsConfirmedAsync(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
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
        Assert.True(await service.IsConfirmedAsync(f.AppId, f.Source.Context, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
        Assert.Equal(TargetState.Minimized, evidence.State);
        foreach (var capability in new[] { InputCapability.Key, InputCapability.Shortcut, InputCapability.Text, InputCapability.Wheel })
            Assert.False(await service.IsConfirmedAsync(f.AppId, f.Source.Context, TargetState.Minimized, capability, true, TestContext.Current.CancellationToken));
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
        Assert.True(await service.IsConfirmedAsync(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
        f.SaveFails = false; await service.ConfirmAsync(second, false, TestContext.Current.CancellationToken);
        Assert.False(await service.IsConfirmedAsync(f.AppId, f.Source.Context!, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
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
            Assert.False(await service.IsConfirmedAsync(f.AppId, changed, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
        var resized = context with { Window = context.Window with { Geometry = new(900, 600, 1) } };
        Assert.False(await service.IsConfirmedAsync(f.AppId, resized, TargetState.Minimized, InputCapability.Click, true, TestContext.Current.CancellationToken));
        Assert.True(await service.IsConfirmedAsync(f.AppId, resized, TargetState.Minimized, InputCapability.Click, false, TestContext.Current.CancellationToken));
        Assert.False(await service.IsConfirmedAsync(f.AppId, context, TargetState.BackgroundVisible, InputCapability.Click, true, TestContext.Current.CancellationToken));
        var pending = await service.BeginAttemptAsync(f.AppId, f.Token, TargetState.Minimized, f.Click, TestContext.Current.CancellationToken);
        f.Source.Context = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(pending, true, TestContext.Current.CancellationToken));
    }
}

