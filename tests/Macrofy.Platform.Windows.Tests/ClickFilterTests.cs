using Macrofy.Platform.Windows;
using Xunit;

namespace Macrofy.Platform.Windows.Tests;
public class ClickFilterTests
{
    [Fact] public void OutsideDownRecordsAndSwallowsMatchingUp()
    {
        var filter = new ClickFilter((_, _) => false);
        Assert.Equal(ClickDecision.Pass, filter.Handle(0x200, 5, 5));
        Assert.Equal(ClickDecision.Record, filter.Handle(0x201, 5, 5)); Assert.False(filter.Finished);
        Assert.Equal(ClickDecision.Swallow, filter.Handle(0x202, 5, 5)); Assert.True(filter.Finished);
        Assert.Equal(ClickDecision.Pass, filter.Handle(0x201, 5, 5));
    }
    [Fact] public void OwnWindowClicksPassThrough()
    {
        var filter = new ClickFilter((x, _) => x < 100);
        Assert.Equal(ClickDecision.Pass, filter.Handle(0x201, 10, 10));
        Assert.Equal(ClickDecision.Pass, filter.Handle(0x202, 10, 10)); Assert.False(filter.Finished);
        Assert.Equal(ClickDecision.Record, filter.Handle(0x201, 500, 10));
    }
    [Fact] public void RealHookInstallsOnceAndCancelsWithoutInput()
    {
        using var source = new WindowsPointClickSource();
        var clicks = 0;
        Assert.Null(source.Start(() => clicks++));
        Assert.Equal("RecordingBusy", source.Start(() => { })!.Code);
        source.Cancel();
        Assert.True(SpinWait.SpinUntil(() => source.Start(() => { }) is null, 2000));
        source.Cancel();
        Assert.Equal(0, clicks);
    }
    [Fact] public void RightClicksPass() => Assert.Equal(ClickDecision.Pass, new ClickFilter((_, _) => false).Handle(0x204, 1, 1));
}
