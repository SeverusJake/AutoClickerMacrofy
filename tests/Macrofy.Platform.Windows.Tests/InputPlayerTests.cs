using Macrofy.Platform;
using Macrofy.Platform.Models;
using Xunit;
namespace Macrofy.Platform.Windows.Tests;
public class InputPlayerTests
{
    [Fact]
    public void SignedCoordinatePackingDoesNotLoseNegativeMonitorCoordinates()
    {
        var packed = MessageEncoder.PackPoint(new(-3,-2));
        Assert.Equal(-3, unchecked((short)((long)packed & 0xffff)));
        Assert.Equal(-2, unchecked((short)(((long)packed >> 16) & 0xffff)));
    }
    [Fact]
    public async Task ButtonMasksAndWheelCoordinatesFollowHeldState()
    {
        using var f = new PlayerFixture();
        await f.Player.SendAsync(f.Token,new KeyCommand(KeyKind.Down,new("Control",29)));
        await f.Player.SendAsync(f.Token,new PointerCommand(PointerKind.Down,new(10,20),MouseButton.Left));
        await f.Player.SendAsync(f.Token,new PointerCommand(PointerKind.Move,new(15,25)));
        await f.Player.SendAsync(f.Token,new PointerCommand(PointerKind.VerticalWheel,new(15,25),WheelDelta:120));
        await f.Player.SendAsync(f.Token,new PointerCommand(PointerKind.Up,new(15,25),MouseButton.Left));
        Assert.Equal((uint)0x201,f.Native.Sends[1].Message.Id);
        Assert.Equal((nuint)9,f.Native.Sends[1].Message.WParam);
        Assert.Equal((nuint)9,f.Native.Sends[2].Message.WParam);
        Assert.Equal((uint)0x20A,f.Native.Sends[3].Message.Id);
        Assert.Equal((nuint)((120u << 16)|9),f.Native.Sends[3].Message.WParam);
        Assert.Equal(-285,unchecked((short)(long)f.Native.Sends[3].Message.LParam));
        Assert.Equal(225,unchecked((short)((long)f.Native.Sends[3].Message.LParam >> 16)));
        Assert.Equal((nuint)8,f.Native.Sends[4].Message.WParam);
    }
    [Fact]
    public async Task KeyRepeatTransitionExtendedAndAltContextBitsAreCorrect()
    {
        using var f = new PlayerFixture();
        var key = new KeyIdentity("RightControl",29,true);
        await f.Player.SendAsync(f.Token,new KeyCommand(KeyKind.Down,key));
        await f.Player.SendAsync(f.Token,new KeyCommand(KeyKind.Down,key));
        await f.Player.SendAsync(f.Token,new KeyCommand(KeyKind.Up,key));
        Assert.Equal(0x011D0001u,unchecked((uint)(long)f.Native.Sends[0].Message.LParam));
        Assert.Equal(0x411D0001u,unchecked((uint)(long)f.Native.Sends[1].Message.LParam));
        Assert.Equal(0xC11D0001u,unchecked((uint)(long)f.Native.Sends[2].Message.LParam));
        await f.Player.SendAsync(f.Token,new KeyCommand(KeyKind.Down,new("Alt",56)));
        await f.Player.SendAsync(f.Token,new KeyCommand(KeyKind.Down,new("A",30)));
        Assert.Equal((uint)0x104,f.Native.Sends[4].Message.Id);
        Assert.Equal(0x201E0001u,unchecked((uint)(long)f.Native.Sends[4].Message.LParam));
    }
    [Fact]
    public async Task TextPostsUtf16SurrogatePairInOrder()
    {
        using var f = new PlayerFixture();
        Assert.True((await f.Player.SendAsync(f.Token,new TextCommand("零😀"))).Queued);
        Assert.Equal(new nuint[] {0x96F6,0xD83D,0xDE00},f.Native.Sends.Select(s=>s.Message.WParam));
        Assert.All(f.Native.Sends,s=>Assert.Equal((uint)0x102,s.Message.Id));
    }
    [Fact]
    public async Task CancelledSendDoesNotDeliver()
    {
        using var f = new PlayerFixture(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await f.Player.SendAsync(f.Token,new TextCommand("x"),cancellation.Token);
        Assert.False(result.Queued); Assert.Equal("Cancelled",result.Error!.Code); Assert.Empty(f.Native.Sends);
    }
    [Fact]
    public async Task DestroyedTargetAndOutsideCoordinatesAreRejected()
    {
        using var f = new PlayerFixture();
        Assert.False((await f.Player.SendAsync(f.Token,new PointerCommand(PointerKind.Down,new(800,0),MouseButton.Left))).Queued);
        Assert.Empty(f.Native.Sends);
        f.Windows.Destroy(11);
        Assert.False((await f.Player.SendAsync(f.Token,new KeyCommand(KeyKind.Down,new("A")))).Queued);
        Assert.Empty(f.Native.Sends);
    }
    [Fact]
    public async Task CleanupReleasesOnlySuccessfulDownsToOriginalTargetOnce()
    {
        using var f = new PlayerFixture();
        await f.Player.SendAsync(f.Token,new KeyCommand(KeyKind.Down,new("A")));
        await f.Player.SendAsync(f.Token,new PointerCommand(PointerKind.Down,new(20,30),MouseButton.Left));
        var cleanup = await f.Player.ReleaseHeldAsync(f.Token);
        Assert.True(cleanup.Queued);
        Assert.Equal(new uint[] {0x100,0x201,0x202,0x101},f.Native.Sends.Select(s=>s.Message.Id));
        Assert.All(f.Native.Sends,s=>Assert.Equal((nint)11,s.Target));
        await f.Player.ReleaseHeldAsync(f.Token);
        Assert.Equal(4,f.Native.Sends.Count);
    }
    [Fact]
    public async Task CleanupNeverSendsToRecreatedWindow()
    {
        using var f = new PlayerFixture();
        await f.Player.SendAsync(f.Token,new KeyCommand(KeyKind.Down,new("A")));
        f.Windows.Destroy(11);
        Assert.False((await f.Player.ReleaseHeldAsync(f.Token)).Queued);
        Assert.Single(f.Native.Sends);
    }
    [Fact]
    public async Task AccessDenialAndPartialTextSendStopAtFirstFailure()
    {
        using var f = new PlayerFixture(); f.Native.FailAt=1;
        var result=await f.Player.SendAsync(f.Token,new TextCommand("abc"));
        Assert.False(result.Queued); Assert.Equal("PermissionDenied",result.Error!.Code);
        Assert.Single(f.Native.Sends);
    }
    [Fact]
    public async Task RateLimitHasNoBurstAndCancellationDuringTextStopsFuturePosts()
    {
        using var f = new PlayerFixture();
        await f.Player.SendAsync(f.Token,new TextCommand("abc"));
        Assert.Equal(new[]{0d,10d,20d},f.Native.Sends.Select(s=>s.At.TotalMilliseconds));
        using var cts = new CancellationTokenSource();
        f.Clock.OnDelay = ()=>cts.Cancel();
        Assert.False((await f.Player.SendAsync(f.Token,new TextCommand("de"),cts.Token)).Queued);
        Assert.Equal(3,f.Native.Sends.Count);
    }
}
internal sealed class PlayerFixture : IDisposable
{
    public FakeWindows Windows {get;}=new();
    public WindowsWindowCatalog Catalog {get;}
    public FakeInputClock Clock {get;}=new();
    public FakeInputNative Native {get;}
    public WindowsInputPlayer Player {get;}
    public TargetToken Token {get;}
    public PlayerFixture()
    {
        Windows.Windows.Add(FakeWindows.Window(11)); Catalog=new(Windows);
        Token=Catalog.ListAsync().Result.Single().Token;
        Native=new(Clock); Player=new(Catalog,Native,Clock,new AllowedPermission());
    }
    public void Dispose(){Player.Dispose();Catalog.Dispose();}
}
internal sealed class AllowedPermission : IPermissionService
{
    public Task<PermissionResult> CheckAsync(TargetToken target,CancellationToken cancellationToken=default)=>Task.FromResult(new PermissionResult(true));
}
internal sealed class FakeInputClock : IInputClock
{
    public TimeSpan Elapsed {get;private set;}
    public Action? OnDelay {get;set;}
    public Task DelayUntilAsync(TimeSpan deadline,CancellationToken token)
    {
        OnDelay?.Invoke(); token.ThrowIfCancellationRequested(); Elapsed=deadline>Elapsed?deadline:Elapsed; return Task.CompletedTask;
    }
}
internal sealed class FakeInputNative(FakeInputClock clock) : IInputNative
{
    public List<(nint Target, NativeMessage Message, TimeSpan At)> Sends {get;}=[];
    public int FailAt {get;set;}=-1;
    public NativeSendResult Post(nint target,NativeMessage message)
    {
        if(Sends.Count==FailAt) return new(false,5);
        Sends.Add((target,message,clock.Elapsed)); return new(true,0);
    }
    public PointerPoint? ToScreen(nint target,PointerPoint point)=>new(point.X-300,point.Y+200);
    public int GetScanCode(nint target,int virtualKey)=>30;
}
