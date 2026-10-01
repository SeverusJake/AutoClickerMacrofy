using Macrofy.IntegrationTests;
using System.Runtime.InteropServices;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows.Interop;
using Xunit;
namespace Macrofy.Platform.Windows.Tests;
public class InputIntegrationTests
{
    [Fact]
    public async Task ControlledReceiverReportsOriginalSurfaceIdentityAndSafeClientGeometry()
    {
        using var target = await TargetFixture.StartAsync();
        var snapshot = await target.RequestAsync("snapshot");
        Assert.True(snapshot.TryGetProperty("Surface", out var surface), "Controlled receiver must report its own HWND and client geometry before exposure.");
        Assert.True(surface.GetProperty("Hwnd").GetInt64() != 0);
        Assert.True(surface.GetProperty("Width").GetInt32() >= 100);
        Assert.True(surface.GetProperty("Height").GetInt32() >= 100);
        Assert.False(surface.GetProperty("Focused").GetBoolean());
        Assert.False(surface.GetProperty("Exposed").GetBoolean());
    }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out WindowNative.Point point);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualTargetReceivesOrderedGesturesWithoutCursorOrFocusChange(bool minimized)
    {
        using var target=await TargetFixture.StartAsync(); using var catalog=new WindowsWindowCatalog();
        var native=new ObservedInputNative();using var player=new WindowsInputPlayer(catalog,native,new InputClock(),new WindowsPermissionService(catalog));
        var window=(await catalog.ListAsync()).Single(w=>w.Title==target.PipeName+" A");
        if(minimized){await target.RequestAsync("minimize"); await WindowIntegrationTests.WaitUntilAsync(async()=>(await catalog.GetAsync(window.Token)).Context!.Window.IsMinimized);}
        await target.RequestAsync("clear");
        InputCommand[] actions=[new PointerCommand(PointerKind.Down,new(30,40),MouseButton.Left),new PointerCommand(PointerKind.Move,new(35,45)),new PointerCommand(PointerKind.Up,new(35,45),MouseButton.Left),
            new KeyCommand(KeyKind.Down,new("Control",29)),new KeyCommand(KeyKind.Down,new("A",30)),new KeyCommand(KeyKind.Up,new("A",30)),new KeyCommand(KeyKind.Up,new("Control",29)),
            new TextCommand("零😀"),new PointerCommand(PointerKind.VerticalWheel,new(35,45),WheelDelta:120),new PointerCommand(PointerKind.HorizontalWheel,new(35,45),WheelDelta:-120)];
        foreach(var action in actions) Assert.True((await player.SendAsync(window.Token,action)).Queued);
        try { await WindowIntegrationTests.WaitUntilAsync(async()=> (await target.RequestAsync("snapshot")).GetProperty("Events").GetArrayLength()==12); } catch(Exception e) { throw new InvalidOperationException((await target.RequestAsync("snapshot")).GetRawText(),e); }
        var events=(await target.RequestAsync("snapshot")).GetProperty("Events");
        Assert.Equal(new uint[]{0x201,0x200,0x202,0x100,0x100,0x101,0x101,0x102,0x102,0x102,0x20A,0x20E},events.EnumerateArray().Select(e=>e.GetProperty("Message").GetUInt32()));
        Assert.Equal(new ulong[]{0x96F6,0xD83D,0xDE00},events.EnumerateArray().Skip(7).Take(3).Select(e=>e.GetProperty("WParam").GetUInt64()));
        Assert.Equal(30,unchecked((short)events[0].GetProperty("LParam").GetInt64()));
        // Users remain free to move physical input between posts. Measure the synchronous native send itself.
        Assert.Equal(12,native.Observations.Count);
        Assert.All(native.Observations,o=>{Assert.Equal(o.Before.X,o.After.X);Assert.Equal(o.Before.Y,o.After.Y);Assert.Equal(o.ForegroundBefore,o.ForegroundAfter);});
    }
    [Fact]
    public async Task StopCannotWithdrawAlreadyPostedInputButCleanupFollowsIt()
    {
        using var target=await TargetFixture.StartAsync(); using var catalog=new WindowsWindowCatalog(); using var player=new WindowsInputPlayer(catalog);
        var token=(await catalog.ListAsync()).Single(w=>w.Title==target.PipeName+" A").Token;
        await target.RequestAsync("clear"); await target.RequestAsync("stall-async");
        await WindowIntegrationTests.WaitUntilAsync(async()=> (await target.RequestAsync("snapshot")).GetProperty("Stalled").GetBoolean());
        Assert.True((await player.SendAsync(token,new PointerCommand(PointerKind.Down,new(20,30),MouseButton.Left))).Queued);
        using var cts=new CancellationTokenSource(); cts.Cancel();
        Assert.False((await player.SendAsync(token,new TextCommand("cancelled"),cts.Token)).Queued);
        Assert.True((await player.ReleaseHeldAsync(token)).Queued);
        Assert.Empty((await target.RequestAsync("snapshot")).GetProperty("Events").EnumerateArray());
        await WindowIntegrationTests.WaitUntilAsync(async()=> (await target.RequestAsync("snapshot")).GetProperty("Events").GetArrayLength()==2);
        Assert.Equal(new uint[]{0x201,0x202},(await target.RequestAsync("snapshot")).GetProperty("Events").EnumerateArray().Select(e=>e.GetProperty("Message").GetUInt32()));
    }
}

internal sealed class ObservedInputNative : IInputNative
{
    private readonly Win32InputNative inner=new();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out WindowNative.Point point);
    public List<(WindowNative.Point Before,WindowNative.Point After,nint ForegroundBefore,nint ForegroundAfter)> Observations {get;}=[];
    public NativeSendResult Post(nint target,NativeMessage message)
    {
        GetCursorPos(out var before);var foregroundBefore=WindowNative.GetForegroundWindow();
        var result=inner.Post(target,message);
        GetCursorPos(out var after);var foregroundAfter=WindowNative.GetForegroundWindow();
        Observations.Add((before,after,foregroundBefore,foregroundAfter));return result;
    }
    public PointerPoint? ToScreen(nint target,PointerPoint point)=>inner.ToScreen(target,point);
    public int GetScanCode(nint target,int key)=>inner.GetScanCode(target,key);
}
