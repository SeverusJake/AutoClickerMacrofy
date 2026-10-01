using Macrofy.App.Services;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.App.Tests;

public class WindowsPlaybackExecutorTests
{
    [Fact]
    public void TestLeaseExcludesAllPlaybackUntilReleasedAndPlaybackAllowsDistinctMacros()
    {
        var guard = new InputActivityGuard();
        Assert.True(guard.TryEnterTest(out var test));
        Assert.False(guard.TryEnterPlayback(Guid.NewGuid(), out _));
        Assert.False(guard.TryEnterTest(out _));
        test!.Dispose(); test.Dispose();
        var id = Guid.NewGuid();
        Assert.True(guard.TryEnterPlayback(id, out var first));
        Assert.False(guard.TryEnterPlayback(id, out _));
        Assert.True(guard.TryEnterPlayback(Guid.NewGuid(), out var second));
        Assert.False(guard.TryEnterTest(out _));
        first!.Dispose(); Assert.False(guard.TryEnterTest(out _));
        second!.Dispose(); Assert.True(guard.TryEnterTest(out test)); test!.Dispose();
    }

    [Fact]
    public async Task ScreenNeedsNoEvidenceAndPreparationNeverSends()
    {
        var f = new Fixture { Evidence = false };
        var result = await f.Executor.PrepareAsync(f.Request(screen: true), TestContext.Current.CancellationToken);
        Assert.NotNull(result.Binding); Assert.Null(result.Binding.Token);
        Assert.Empty(f.Input.Commands); Assert.Equal(0, f.Input.Cleanups);
        Assert.True((await f.Executor.ExecuteAsync(result.Binding, new CompiledAction.Text("hello", 0), TestContext.Current.CancellationToken)).Delivery.Queued);
    }

    [Theory]
    [InlineData(false, "TargetMissing")]
    [InlineData(true, "TargetAmbiguous")]
    public async Task MissingOrAmbiguousParentCannotChooseArbitrarily(bool ambiguous, string error)
    {
        var f = new Fixture();
        f.Catalog.Resolution = ambiguous ? new ResolutionResult.Ambiguous([f.Window, f.OtherWindow]) : new ResolutionResult.Missing();
        var result = await f.Executor.PrepareAsync(f.Request(selected: null), TestContext.Current.CancellationToken);
        Assert.Null(result.Binding); Assert.Equal(error, result.Error!.Code); Assert.Empty(f.Input.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitChildResolvesThroughOwningParentEvenWithAmbiguousParents(bool ambiguous)
    {
        var f = new Fixture();
        f.Catalog.Resolution = ambiguous ? new ResolutionResult.Ambiguous([f.OtherWindow, f.Window]) : new ResolutionResult.Matched(f.Window);
        f.Surfaces[f.OtherWindow.Token] = [f.OtherWindow];
        f.Surfaces[f.Window.Token] = [f.Window, f.Child];
        f.Contexts[f.Child.Token] = new(f.Child, false, "child-fingerprint");
        var result = await f.Executor.PrepareAsync(f.Request(selected: f.Child.Token), TestContext.Current.CancellationToken);
        Assert.Equal(f.Child.Token, result.Binding!.Token); Assert.Null(f.Catalog.Selected);
    }

    [Fact]
    public async Task LostSelectedChildNeverRebindsToParent()
    {
        var f = new Fixture();
        var result = await f.Executor.PrepareAsync(f.Request(selected: f.Child.Token), TestContext.Current.CancellationToken);
        Assert.Null(result.Binding); Assert.Equal("TargetMissing", result.Error!.Code); Assert.Empty(f.Input.Commands);
    }

    [Fact]
    public async Task UnselectedSurfaceNeedsUniqueEvidenceMatch()
    {
        var f = new Fixture();
        f.Surfaces[f.Window.Token] = [f.Window, f.Child];
        f.Contexts[f.Child.Token] = new(f.Child, false, "child-fingerprint");
        var ambiguous = await f.Executor.PrepareAsync(f.Request(selected: null), TestContext.Current.CancellationToken);
        Assert.Equal("SurfaceAmbiguous", ambiguous.Error!.Code);
        f.EvidenceToken = f.Child.Token;
        var matched = await f.Executor.PrepareAsync(f.Request(selected: null), TestContext.Current.CancellationToken);
        Assert.Equal(f.Child.Token, matched.Binding!.Token);
    }

    [Fact]
    public async Task SuppliedRuleAndAppIdentityUsedAcrossProfilesAndAllCapabilitiesChecked()
    {
        var f = new Fixture();
        var appId = Guid.NewGuid(); var rule = new TargetRule(new("Other", "other.exe"), "Other*");
        f.Contexts[f.Window.Token] = f.Contexts[f.Window.Token] with { Window = f.Window with { App = rule.App, Title = "Other Game" } };
        var request = f.Request() with { Target = new(appId, rule, TargetState.BackgroundCovered, f.Window.Token), Actions = [new CompiledAction.Key([new("Ctrl"), new("A")], 0), new CompiledAction.Text("x", 0)] };
        f.AllowedCapabilities.Remove(InputCapability.Text);
        var result = await f.Executor.PrepareAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(rule, f.Catalog.Rule); Assert.Contains(f.EvidenceQueries, q => q.Id == appId && q.Capability == InputCapability.Shortcut);
        Assert.Contains(f.EvidenceQueries, q => q.Capability == InputCapability.Text);
        Assert.Null(result.Binding); Assert.Equal("CompatibilityRequired", result.Error!.Code);
    }

    [Fact]
    public async Task StopReadinessIsDynamicAndBlocksPreparationAndDispatch()
    {
        var f = new Fixture { Ready = false };
        Assert.Equal("StopUnavailable", (await f.Executor.PrepareAsync(f.Request(screen: true), TestContext.Current.CancellationToken)).Error!.Code);
        f.Ready = true;
        var binding = (await f.Executor.PrepareAsync(f.Request(screen: true), TestContext.Current.CancellationToken)).Binding!;
        f.Ready = false;
        Assert.Equal("StopUnavailable", (await f.Executor.ExecuteAsync(binding, new CompiledAction.Text("x", 0), TestContext.Current.CancellationToken)).Delivery.Error!.Code);
        Assert.Empty(f.Input.Commands);
    }

    [Theory]
    [InlineData("lost")]
    [InlineData("title")]
    [InlineData("fingerprint")]
    [InlineData("foreground")]
    [InlineData("minimized")]
    [InlineData("permission")]
    public async Task ChangedBindingFailsResumeAndFirstPostWithoutResolutionOrFallback(string change)
    {
        var f = new Fixture();
        var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        var original = f.Contexts[f.Window.Token];
        switch (change)
        {
            case "lost": f.Contexts.Clear(); break;
            case "title": f.Contexts[f.Window.Token] = original with { Window = original.Window with { Title = "Changed" } }; break;
            case "fingerprint": f.Contexts[f.Window.Token] = original with { SurfaceFingerprint = "changed" }; break;
            case "foreground": f.Contexts[f.Window.Token] = original with { IsForeground = true }; break;
            case "minimized": f.Contexts[f.Window.Token] = original with { Window = original.Window with { IsMinimized = true } }; break;
            case "permission": f.PermissionAllowed = false; break;
        }
        Assert.False((await f.Executor.ValidateAsync(binding, TestContext.Current.CancellationToken)).Queued);
        Assert.False((await f.Executor.ExecuteAsync(binding, new CompiledAction.Text("x", 0), TestContext.Current.CancellationToken)).Delivery.Queued);
        Assert.Equal(1, f.Catalog.Resolves); Assert.Empty(f.Input.Commands);
    }

    [Fact]
    public async Task EvidenceRevocationStopsDispatchAndFixedEvidenceIsPerAction()
    {
        var f = new Fixture();
        var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        f.Evidence = false;
        Assert.Equal("CompatibilityRequired", (await f.Executor.ExecuteAsync(binding, new CompiledAction.Key([new("Ctrl"), new("A")], 0), TestContext.Current.CancellationToken)).Delivery.Error!.Code);
        Assert.Empty(f.Input.Commands);
    }

    [Theory]
    [InlineData(false, 0, 0, 0)]
    [InlineData(false, 100, 799, 599)]
    [InlineData(true, 0, -100, -50)]
    [InlineData(true, 100, 1899, 949)]
    public async Task PercentageEndpointsProduceBalancedClick(bool screen, double percent, double x, double y)
    {
        var f = new Fixture();
        var binding = (await f.Executor.PrepareAsync(f.Request(screen: screen), TestContext.Current.CancellationToken)).Binding!;
        var result = await f.Executor.ExecuteAsync(binding, new CompiledAction.Click(new(percent, percent), CoordinateMode.Percentage, 0), TestContext.Current.CancellationToken);
        Assert.True(result.Delivery.Queued);
        Assert.Equal(new InputCommand[] { new PointerCommand(PointerKind.Down, new(x, y), MouseButton.Left), new PointerCommand(PointerKind.Up, new(x, y), MouseButton.Left) }, f.Input.Commands);
    }

    [Fact]
    public async Task ResizedWindowRejectsFixedPixelsButRecalculatesPercentage()
    {
        var f = new Fixture();
        var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        var original = f.Contexts[f.Window.Token];
        f.Contexts[f.Window.Token] = original with { Window = original.Window with { Geometry = new(1000, 500, 1) } };
        Assert.Equal("GeometryChanged", (await f.Executor.ExecuteAsync(binding, new CompiledAction.Click(new(10, 10), CoordinateMode.FixedPixels, 0), TestContext.Current.CancellationToken)).Delivery.Error!.Code);
        Assert.Empty(f.Input.Commands);
        Assert.True((await f.Executor.ExecuteAsync(binding, new CompiledAction.Click(new(100, 100), CoordinateMode.Percentage, 0), TestContext.Current.CancellationToken)).Delivery.Queued);
        Assert.Equal(new PointerPoint(999, 499), Assert.IsType<PointerCommand>(f.Input.Commands[0]).Point);
    }

    [Fact]
    public async Task InvalidCurrentGeometryRejectsPercentageBeforeInput()
    {
        var f = new Fixture(); var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        var original = f.Contexts[f.Window.Token]; f.Contexts[f.Window.Token] = original with { Window = original.Window with { Geometry = new(0, 500, 1) } };
        Assert.False((await f.Executor.ExecuteAsync(binding, new CompiledAction.Click(new(50, 50), CoordinateMode.Percentage, 0), TestContext.Current.CancellationToken)).Delivery.Queued); Assert.Empty(f.Input.Commands);
    }

    [Fact]
    public async Task WindowWheelUsesCurrentClientPointer()
    {
        var f = new Fixture(); var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        f.ClientPointer = new(27, 31);
        Assert.True((await f.Executor.ExecuteAsync(binding, new CompiledAction.Wheel(-120, 0), TestContext.Current.CancellationToken)).Delivery.Queued);
        Assert.Equal(new PointerCommand(PointerKind.VerticalWheel, new(27, 31), WheelDelta: -120), Assert.Single(f.Input.Commands));
    }

    [Fact]
    public async Task ChordPressesInOrderAndReleasesReverseOrder()
    {
        var f = new Fixture(); var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        Assert.True((await f.Executor.ExecuteAsync(binding, new CompiledAction.Key([new("Ctrl"), new("Shift"), new("A")], 0), TestContext.Current.CancellationToken)).Delivery.Queued);
        Assert.Equal(new[] { "Down:Ctrl", "Down:Shift", "Down:A", "Up:A", "Up:Shift", "Up:Ctrl" }, f.Input.Commands.Cast<KeyCommand>().Select(c => $"{c.Kind}:{c.Key.LogicalKey}"));
        Assert.Empty(f.Input.Held);
    }

    [Fact]
    public async Task CancellationOrStopLossAfterCtrlDownUsesIndependentCleanup()
    {
        var f = new Fixture(); var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        f.Input.AfterSend = () => { f.Ready = false; cancellation.Cancel(); };
        var result = await f.Executor.ExecuteAsync(binding, new CompiledAction.Key([new("Ctrl"), new("A")], 0), cancellation.Token);
        Assert.Equal("Cancelled", result.Delivery.Error!.Code); Assert.Single(f.Input.Commands); Assert.Empty(f.Input.Held);
        Assert.True(f.Input.CleanupTokenCanCancel); Assert.False(f.Input.CleanupTokenCancelled); Assert.Equal(1, f.Input.Cleanups);
    }

    [Fact]
    public async Task TextFailurePreservesIndependentAdapterCleanupFailure()
    {
        var f = new Fixture(); var binding = (await f.Executor.PrepareAsync(f.Request(screen: true), TestContext.Current.CancellationToken)).Binding!;
        f.Input.Next = new(false, new("DeliveryFailed", "partial"), new("CleanupFailed", "first cleanup"));
        var result = await f.Executor.ExecuteAsync(binding, new CompiledAction.Text("abc", 0), TestContext.Current.CancellationToken);
        Assert.Equal("DeliveryFailed", result.Delivery.Error!.Code); Assert.Equal("CleanupFailed", result.CleanupError!.Code);
        Assert.Equal(1, f.Input.Cleanups);
    }

    [Fact]
    public async Task SenderAllowsExplicitCompatibilityTestWithoutPriorEvidence()
    {
        var f = new Fixture { Evidence = false };
        var binding = new PlaybackBinding(f.AppId, f.Window.Token, f.Window.Title, "fingerprint", f.Window.Geometry, TargetState.BackgroundCovered);
        Assert.True((await f.Sender.SendGestureAsync(binding, new CompiledAction.Text("x", 0), TestContext.Current.CancellationToken)).Delivery.Queued);
    }

    [Fact]
    public async Task UnsupportedActionBlocksPreflightBeforeAnyInput()
    {
        var f = new Fixture();
        var preparation = await f.Executor.PrepareAsync(f.Request() with { Actions = [new UnsupportedAction()] }, TestContext.Current.CancellationToken);
        Assert.Null(preparation.Binding); Assert.Equal("UnsupportedCapability", preparation.Error!.Code); Assert.Empty(f.Input.Commands);
    }

    [Fact]
    public async Task FixedAndPercentageClicksUseIndependentEvidenceGeometryRequirements()
    {
        var f = new Fixture();
        var fixedClick = new CompiledAction.Click(new(10, 10), CoordinateMode.FixedPixels, 0);
        var percentageClick = new CompiledAction.Click(new(50, 50), CoordinateMode.Percentage, 0);
        var preparation = await f.Executor.PrepareAsync(f.Request() with { Actions = [fixedClick, percentageClick] }, TestContext.Current.CancellationToken);
        Assert.NotNull(preparation.Binding);
        Assert.Contains(f.EvidenceQueries, q => q.Capability == InputCapability.Click && q.Fixed);
        Assert.Contains(f.EvidenceQueries, q => q.Capability == InputCapability.Click && !q.Fixed);
        f.FixedEvidence = false;
        Assert.False((await f.Executor.ExecuteAsync(preparation.Binding, fixedClick, TestContext.Current.CancellationToken)).Delivery.Queued);
        Assert.True((await f.Executor.ExecuteAsync(preparation.Binding, percentageClick, TestContext.Current.CancellationToken)).Delivery.Queued);
    }

    [Fact]
    public async Task StopLossBetweenChordDownsReleasesOwnedModifier()
    {
        var f = new Fixture();
        var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        f.Input.AfterSend = () => f.Ready = false;
        var result = await f.Executor.ExecuteAsync(binding, new CompiledAction.Key([new("Ctrl"), new("A")], 0), TestContext.Current.CancellationToken);
        Assert.Equal("StopUnavailable", result.Delivery.Error!.Code); Assert.Single(f.Input.Commands); Assert.Empty(f.Input.Held); Assert.Equal(1, f.Input.Cleanups);
    }

    [Fact]
    public async Task PercentageResizeBetweenDownAndUpStopsGestureAndRunsCleanup()
    {
        var f = new Fixture();
        var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        f.Input.AfterSend = () => f.Contexts[f.Window.Token] = f.Contexts[f.Window.Token] with { Window = f.Window with { Geometry = new(100, 100, 1) } };
        var result = await f.Executor.ExecuteAsync(binding, new CompiledAction.Click(new(50, 50), CoordinateMode.Percentage, 0), TestContext.Current.CancellationToken);
        Assert.Equal("GeometryChanged", result.Delivery.Error!.Code); Assert.Single(f.Input.Commands); Assert.Equal(1, f.Input.Cleanups);
    }

    [Fact]
    public async Task FailedDeliveryAndCleanupKeepBothErrors()
    {
        var f = new Fixture();
        var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        f.Input.Next = new(false, new("DeliveryFailed", "post failed"));
        f.Input.CleanupResult = new(false, new("CleanupFailed", "release failed"));
        var result = await f.Executor.ExecuteAsync(binding, new CompiledAction.Text("abc", 0), TestContext.Current.CancellationToken);
        Assert.Equal("DeliveryFailed", result.Delivery.Error!.Code); Assert.Equal("CleanupFailed", result.CleanupError!.Code);
    }

    [Fact]
    public async Task CleanupUsesIndependentBoundedBudget()
    {
        var f = new Fixture();
        var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        f.Input.DelayCleanup = true;
        var result = await f.Executor.ExecuteAsync(binding, new CompiledAction.Text("x", 0), TestContext.Current.CancellationToken);
        Assert.True(result.Delivery.Queued); Assert.Equal("CleanupTimeout", result.CleanupError!.Code);
    }

    private sealed record UnsupportedAction() : CompiledAction(0);

    private sealed class Fixture : ITargetContext, IPermissionService
    {
        public Guid AppId { get; } = Guid.NewGuid();
        public TargetWindow Window { get; } = new(new(Guid.NewGuid()), new("Game", "game.exe"), "Game", false, new(800, 600, 1));
        public TargetWindow OtherWindow { get; } = new(new(Guid.NewGuid()), new("Game", "game.exe"), "Game", false, new(800, 600, 1));
        public TargetWindow Child { get; } = new(new(Guid.NewGuid()), new("Game", "game.exe"), "Game", false, new(800, 600, 1));
        public Dictionary<TargetToken, TargetContext> Contexts { get; } = [];
        public Dictionary<TargetToken, IReadOnlyList<TargetWindow>> Surfaces { get; } = [];
        public bool Ready = true, Evidence = true, PermissionAllowed = true, FixedEvidence = true;
        public TargetToken? EvidenceToken;
        public PointerPoint ClientPointer = new(20, 30);
        public HashSet<InputCapability> AllowedCapabilities = [InputCapability.Click, InputCapability.Key, InputCapability.Shortcut, InputCapability.Text, InputCapability.Wheel];
        public List<(Guid Id, InputCapability Capability, bool Fixed)> EvidenceQueries = [];
        public Catalog Catalog { get; } = new();
        public Player Input { get; } = new();
        public WindowsGestureSender Sender { get; }
        public WindowsPlaybackExecutor Executor { get; }
        public Fixture()
        {
            Contexts[Window.Token] = new(Window, false, "fingerprint"); Surfaces[Window.Token] = [Window];
            Catalog.Resolution = new ResolutionResult.Matched(Window);
            Sender = new(this, this, Input, Input, _ => new(ClientPointer), () => Ready);
            Executor = new(Catalog, this, (token, _) => Task.FromResult(Surfaces.GetValueOrDefault(token) ?? []), Sender,
                (id, context, state, capability, fixedCoordinates, _) => { EvidenceQueries.Add((id, capability, fixedCoordinates)); return Task.FromResult(Evidence && (!fixedCoordinates || FixedEvidence) && AllowedCapabilities.Contains(capability) && (EvidenceToken is null || EvidenceToken == context.Window.Token)); });
        }
        public PlaybackRequest Request(bool screen = false, TargetToken? selected = null) => new(Guid.NewGuid(), "Macro", "Profile", screen ? new(null, null, null, null) : new(AppId, new(Window.App, "Game"), TargetState.BackgroundCovered, selected), [new CompiledAction.Text("x", 0)], 1, 0, 0);
        public ValueTask<TargetContextResult> GetAsync(TargetToken token, CancellationToken ct = default) => ValueTask.FromResult(new TargetContextResult(Contexts.GetValueOrDefault(token), Contexts.ContainsKey(token) ? null : new("TargetLost", "gone")));
        public Task<PermissionResult> CheckAsync(TargetToken token, CancellationToken ct = default) => Task.FromResult(new PermissionResult(PermissionAllowed, PermissionAllowed ? null : new("PermissionDenied", "blocked")));
    }
    private sealed class Catalog : IWindowCatalog
    {
        public ResolutionResult Resolution = new ResolutionResult.Missing();
        public TargetRule? Rule; public TargetToken? Selected; public int Resolves;
        public event Action<TargetToken>? TargetLost { add { } remove { } }
        public Task<IReadOnlyList<TargetWindow>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ResolutionResult> ResolveAsync(TargetRule rule, TargetToken? selected = null, CancellationToken ct = default) { Rule = rule; Selected = selected; Resolves++; return Task.FromResult(Resolution); }
    }
    private sealed class Player : IInputPlayer, IScreenInputPlayer
    {
        public List<InputCommand> Commands = []; public List<KeyIdentity> Held = [];
        public Action? AfterSend; public DeliveryResult Next = new(true); public int Cleanups;
        public DeliveryResult CleanupResult = new(true); public bool DelayCleanup;
        public bool CleanupTokenCanCancel, CleanupTokenCancelled;
        public ScreenGeometry ReadGeometry() => new(-100, -50, 2000, 1000);
        public ScreenPointResult ReadPointer() => new(new(42, 43));
        public ValueTask<DeliveryResult> SendAsync(TargetToken target, InputCommand command, CancellationToken ct = default) => SendAsync(command, ct);
        public ValueTask<DeliveryResult> SendAsync(InputCommand command, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Commands.Add(command);
            if (Next.Queued && command is KeyCommand key) { if (key.Kind == KeyKind.Down) Held.Add(key.Key); else Held.Remove(key.Key); }
            AfterSend?.Invoke(); return ValueTask.FromResult(Next);
        }
        public ValueTask<DeliveryResult> ReleaseHeldAsync(TargetToken target, CancellationToken ct = default) => ReleaseHeldAsync(ct);
        public async ValueTask<DeliveryResult> ReleaseHeldAsync(CancellationToken ct = default)
        {
            Cleanups++; CleanupTokenCanCancel = ct.CanBeCanceled; CleanupTokenCancelled = ct.IsCancellationRequested;
            if (DelayCleanup) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            Held.Clear(); return CleanupResult;
        }
    }
}
