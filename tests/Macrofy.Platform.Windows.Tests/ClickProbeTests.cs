using Macrofy.CompatibilityProbe;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Xunit;
namespace Macrofy.Platform.Windows.Tests;
public class ClickProbeTests
{
    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task ForegroundOrWrongStateDoesNotSend(bool foreground,bool minimized)
    {
        var context=new FakeContext(foreground,minimized);var player=new ProbePlayer();var probe=new ClickProbeSession(context,player);
        var attempt=await probe.TestAsync(context.Token,new(10,20),TargetState.BackgroundCovered);
        Assert.False(attempt.CanConfirm);Assert.Empty(player.Inputs);
    }
    [Fact]
    public async Task QueuedClickDoesNotCreateConfirmationAutomatically()
    {
        var context=new FakeContext(false,false);var player=new ProbePlayer();var probe=new ClickProbeSession(context,player);
        var attempt=await probe.TestAsync(context.Token,new(10,20),TargetState.BackgroundCovered);
        Assert.True(attempt.CanConfirm);Assert.Equal(new[]{PointerKind.Down,PointerKind.Up},player.Inputs.Cast<PointerCommand>().Select(p=>p.Kind));
        var confirmed=await probe.ConfirmAsync(attempt,true);
        Assert.True(confirmed.ObservedSuccess);Assert.Equal(TargetState.BackgroundCovered,confirmed.State);
        Assert.Equal(1,player.Cleanups);
    }
    [Fact]
    public async Task CancelledClickClosesHeldStateUsingFreshCleanupToken()
    {
        var context=new FakeContext(false,false);using var cts=new CancellationTokenSource();var player=new ProbePlayer{AfterSend=()=>cts.Cancel()};
        var probe=new ClickProbeSession(context,player);var attempt=await probe.TestAsync(context.Token,new(10,20),TargetState.BackgroundCovered,cts.Token);
        Assert.False(attempt.CanConfirm);Assert.Single(player.Inputs);Assert.Equal(1,player.Cleanups);Assert.False(player.CleanupTokenWasCancelled);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>probe.ConfirmAsync(attempt,true));
    }
    [Fact]
    public async Task MinimizedTestCanBeConfirmedAfterRestoreForObservation()
    {
        var context=new FakeContext(false,true);var probe=new ClickProbeSession(context,new ProbePlayer());
        var attempt=await probe.TestAsync(context.Token,new(10,20),TargetState.Minimized);
        context.Minimized=false;
        var confirmation=await probe.ConfirmAsync(attempt,true);
        Assert.Equal(TargetState.Minimized,confirmation.State);Assert.True(confirmation.ObservedSuccess);
    }
    [Fact]
    public async Task ChangedStateOrFingerprintCannotBeConfirmed()
    {
        var context=new FakeContext(false,false);var probe=new ClickProbeSession(context,new ProbePlayer());
        var attempt=await probe.TestAsync(context.Token,new(10,20),TargetState.BackgroundCovered);
        context.Fingerprint="changed";await Assert.ThrowsAsync<InvalidOperationException>(()=>probe.ConfirmAsync(attempt,true));
    }
    [Fact]
    public async Task FailedDownStillReportsCleanupFailure()
    {
        var context=new FakeContext(false,false);var player=new ProbePlayer{Fail=true,FailCleanup=true};var probe=new ClickProbeSession(context,player);
        var attempt=await probe.TestAsync(context.Token,new(10,20),TargetState.BackgroundCovered);
        Assert.Equal("CleanupFailed",attempt.CleanupError!.Code);
    }
    [Fact]
    public async Task FailedDownNeverSendsUpAndCannotBeConfirmed()
    {
        var context=new FakeContext(false,false);var player=new ProbePlayer{Fail=true};var probe=new ClickProbeSession(context,player);
        var attempt=await probe.TestAsync(context.Token,new(10,20),TargetState.BackgroundCovered);
        Assert.False(attempt.CanConfirm);Assert.Single(player.Inputs);Assert.Equal(1,player.Cleanups);
    }
}
internal sealed class FakeContext(bool foreground,bool minimized) : ITargetContext
{
    public TargetToken Token {get;}=new(Guid.NewGuid());
    public string Fingerprint {get;set;}="v1";
    public bool Minimized {get;set;}=minimized;
    public ValueTask<TargetContextResult> GetAsync(TargetToken target,CancellationToken cancellationToken=default)
    {
        var window=new TargetWindow(Token,new("game",@"C:\game.exe"),"Game",Minimized,new(800,600,1));
        return ValueTask.FromResult(new TargetContextResult(new(window,foreground,Fingerprint)));
    }
}
internal sealed class ProbePlayer : IInputPlayer
{
    public List<InputCommand> Inputs {get;}=[];public int Cleanups {get;private set;}public bool CleanupTokenWasCancelled {get;private set;}
    public Action? AfterSend {get;set;}public bool Fail {get;set;}public bool FailCleanup {get;set;}
    public ValueTask<DeliveryResult> SendAsync(TargetToken target,InputCommand command,CancellationToken cancellationToken=default)
    {Inputs.Add(command);AfterSend?.Invoke();return ValueTask.FromResult(Fail?new DeliveryResult(false,new("DeliveryFailed","test rejection")):new DeliveryResult(true));}
    public ValueTask<DeliveryResult> ReleaseHeldAsync(TargetToken target,CancellationToken cancellationToken=default)
    {Cleanups++;CleanupTokenWasCancelled=cancellationToken.IsCancellationRequested;return ValueTask.FromResult(FailCleanup?new DeliveryResult(false,new("CleanupFailed","test cleanup rejection")):new DeliveryResult(true));}
}
