using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.Platform.Windows.Tests;

public class ScreenClickerTests
{
    [Fact]
    public async Task ScreenClickUsesVirtualDesktopCoordinatesAndOneOrderedBatch()
    {
        var native=new FakeScreenNative();var clicker=new WindowsScreenClicker(native);
        Assert.True((await clicker.ClickAsync(new(-1920,0))).Queued);
        var batch=Assert.Single(native.Batches);
        Assert.Equal(new uint[]{0xC001,2,4},batch.Select(i=>i.Flags));
        Assert.Equal(0,batch[0].X);Assert.Equal(0,batch[0].Y);
        Assert.True((await clicker.ClickAsync(new(1919,1079))).Queued);
        Assert.Equal(65535,native.Batches[1][0].X);Assert.Equal(65535,native.Batches[1][0].Y);
    }
    [Fact]
    public async Task CancelledOutsideGapAndHeldMouseDoNotInject()
    {
        var native=new FakeScreenNative();var clicker=new WindowsScreenClicker(native);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Equal("Cancelled",(await clicker.ClickAsync(new(20,30),cancelled.Token)).Error!.Code);
        Assert.Equal("InvalidInput",(await clicker.ClickAsync(new(double.NaN,0))).Error!.Code);
        Assert.Equal("InvalidInput",(await clicker.ClickAsync(new(1920,0))).Error!.Code);
        native.OnMonitor=false;
        Assert.Equal("InvalidInput",(await clicker.ClickAsync(new(0,0))).Error!.Code);
        native.OnMonitor=true;native.LeftHeld=true;
        Assert.Equal("MouseHeld",(await clicker.ClickAsync(new(0,0))).Error!.Code);
        Assert.Empty(native.Batches);
    }
    [Theory]
    [InlineData(0,1)]
    [InlineData(1,1)]
    [InlineData(2,2)]
    public async Task PartialInsertionIsFailureAndOnlyInsertedDownGetsRelease(int inserted,int batches)
    {
        var native=new FakeScreenNative{Inserted=inserted};var clicker=new WindowsScreenClicker(native);
        Assert.False((await clicker.ClickAsync(new(20,30))).Queued);
        Assert.Equal(batches,native.Batches.Count);
        if(inserted==2)Assert.Equal((uint)4,Assert.Single(native.Batches[1]).Flags);
    }
    [Fact]
    public void PointerCaptureReadsScreenCoordinatesWithoutInjection()
    {
        var native=new FakeScreenNative();var clicker=new WindowsScreenClicker(native);
        Assert.Equal(new PointerPoint(-800,250),clicker.ReadPointerPosition().Point);
        Assert.Empty(native.Batches);
    }
}
internal sealed class FakeScreenNative : IScreenInputNative
{
    public List<ScreenMouseInput[]> Batches {get;}=[];
    public int Inserted {get;set;}=3;
    public bool LeftHeld {get;set;}
    public bool OnMonitor {get;set;}=true;
    public ScreenBounds ReadBounds()=>new(-1920,0,3840,1080);
    public PointerPoint? ReadPointer()=>new(-800,250);
    public bool IsPointOnMonitor(PointerPoint point)=>OnMonitor;
    public bool IsLeftButtonHeld()=>LeftHeld;
    public int Send(ScreenMouseInput[] inputs){Batches.Add(inputs);return Math.Min(Inserted,inputs.Length);}
}
