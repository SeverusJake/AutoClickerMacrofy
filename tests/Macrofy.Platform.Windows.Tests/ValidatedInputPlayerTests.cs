using Macrofy.Platform.Models;
using Xunit;
namespace Macrofy.Platform.Windows.Tests;

public class ValidatedInputPlayerTests
{
    [Fact]
    public async Task PlaybackPointerAllowsCurrentValidMinimizedClientPointWhileCaptureRequiresRestore()
    {
        using var f = new PlayerFixture();
        // Establish valid restored geometry before minimization; minimized geometry is cached per live token.
        await f.Catalog.GetAsync(f.Token);
        f.Windows.Windows[0] = f.Windows.Windows[0] with { IsMinimized = true, Geometry = null };
        Assert.Equal("Minimized", f.Catalog.ReadPointerPosition(f.Token).Error!.Code);
        Assert.Equal(new PointerPoint(20, 30), f.Catalog.ReadPlaybackPointerPosition(f.Token).Point);
        f.Windows.Pointer = new(900, 30);
        Assert.Equal("OutsideClient", f.Catalog.ReadPlaybackPointerPosition(f.Token).Error!.Code);
        f.Windows.Destroy(11);
        Assert.Equal("TargetLost", f.Catalog.ReadPlaybackPointerPosition(f.Token).Error!.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedTitleOrForegroundDuringPacingBlocksFirstNativePost(bool foreground)
    {
        using var f = new PlayerFixture();
        var original = (await f.Catalog.GetAsync(f.Token, CancellationToken.None)).Context!;
        f.Clock.OnDelay = () => f.Windows.Windows[0] = foreground ? f.Windows.Windows[0] with { IsForeground = true } : f.Windows.Windows[0] with { Title = "Changed" };
        async ValueTask<DeliveryResult> Validate(CancellationToken ct)
        {
            var current = (await f.Catalog.GetAsync(f.Token, ct)).Context!;
            return current.IsForeground || current.SurfaceFingerprint != original.SurfaceFingerprint ? new(false, new("TargetChanged", "changed")) : new(true);
        }
        var result = await f.Player.SendValidatedAsync(f.Token, new TextCommand("abc"), Validate, CancellationToken.None);
        Assert.False(result.Queued); Assert.Equal("TargetChanged", result.Error!.Code); Assert.Empty(f.Native.Sends);
    }

    [Fact]
    public async Task ValidationRunsForEachTextPostAndPreservesPartialDeliveryFailure()
    {
        using var f = new PlayerFixture();
        var result = await f.Player.SendValidatedAsync(f.Token, new TextCommand("abc"),
            _ => ValueTask.FromResult(f.Native.Sends.Count == 0 ? new DeliveryResult(true) : new(false, new("StopUnavailable", "lost"))), CancellationToken.None);
        Assert.False(result.Queued); Assert.Equal("StopUnavailable", result.Error!.Code); Assert.Single(f.Native.Sends);
    }
}
