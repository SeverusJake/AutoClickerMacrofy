using System.Runtime.InteropServices;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.Platform.Windows.Tests;

public class ScreenInputPlayerTests
{
    [Fact]
    public async Task ClickerCleanupFailureAddsIndependentCleanupError()
    {
        var native = new FailingClickerNative();
        var result = await new WindowsScreenClicker(native).ClickAsync(new(10, 10));
        Assert.Equal("CleanupFailed", result.Error!.Code);
        Assert.NotNull(result.CleanupError); Assert.Equal("CleanupFailed", result.CleanupError.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PartialPointerWithoutInsertedDownHasNoRelease(int inserted)
    {
        var native = new ScreenPlayerNative(); native.Insertions.Enqueue(inserted);
        var result = await new WindowsScreenInputPlayer(native).SendAsync(new PointerCommand(PointerKind.Down, new(0, 0), MouseButton.Left));
        Assert.Equal("DeliveryFailed", result.Error!.Code); Assert.Null(result.CleanupError);
        Assert.Single(native.Batches);
    }
    [Fact]
    public void NativeInputUsesFullUnionSize()
    {
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<Win32ScreenInputNative.Input>());
        Assert.Equal(IntPtr.Size == 8 ? 8 : 4, Marshal.OffsetOf<Win32ScreenInputNative.Input>("Union").ToInt32());
    }

    [Fact]
    public async Task ClickCommandsNormalizeNegativeDesktopEndpointsAndKeepOrder()
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        Assert.True((await player.SendAsync(new PointerCommand(PointerKind.Down, new(-1920, 0), MouseButton.Left))).Queued);
        Assert.True((await player.SendAsync(new PointerCommand(PointerKind.Up, new(1919, 1079), MouseButton.Left))).Queued);
        var events = native.Batches.SelectMany(x => x).ToArray();
        Assert.Equal(new uint[] { 0xC001, 2, 0xC001, 4 }, events.Select(x => x.Flags));
        Assert.Equal((0, 0), (events[0].X, events[0].Y));
        Assert.Equal((65535, 65535), (events[2].X, events[2].Y));
        Assert.True((await player.ReleaseHeldAsync()).Queued); Assert.Equal(2, native.Batches.Count);
    }

    [Fact]
    public async Task KeysUseVirtualKeysAndExtendedUpFlags()
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        Assert.True((await player.SendAsync(new KeyCommand(KeyKind.Down, new("Right", IsExtended: true)))).Queued);
        Assert.True((await player.SendAsync(new KeyCommand(KeyKind.Up, new("Right", IsExtended: true)))).Queued);
        Assert.Equal(new uint[] { 1, 3 }, native.Batches.SelectMany(x => x).Select(x => x.Flags));
        Assert.All(native.Batches.SelectMany(x => x), x => { Assert.Equal(1u, x.Type); Assert.Equal((ushort)0x27, x.VirtualKey); });
    }

    [Fact]
    public async Task TextPreservesSurrogateUnitsAndBalancedUnicodePairs()
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        Assert.True((await player.SendAsync(new TextCommand("A\U0001F600"))).Queued);
        var batch = Assert.Single(native.Batches);
        Assert.Equal(new ushort[] { 65, 65, 0xD83D, 0xD83D, 0xDE00, 0xDE00 }, batch.Select(x => x.ScanCode));
        Assert.Equal(new uint[] { 4, 6, 4, 6, 4, 6 }, batch.Select(x => x.Flags));
        Assert.All(batch, x => { Assert.Equal(1u, x.Type); Assert.Equal((ushort)0, x.VirtualKey); });
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public async Task InvalidUnicodeNeverInjects(int unit)
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        Assert.Equal("InvalidInput", (await player.SendAsync(new TextCommand(new string((char)unit, 1)))).Error!.Code);
        Assert.Empty(native.Batches);
    }

    [Fact]
    public async Task TextLimitNeverInjects()
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        Assert.Equal("InvalidInput", (await player.SendAsync(new TextCommand(new string('a', 4097)))).Error!.Code);
        Assert.Empty(native.Batches);
    }

    [Fact]
    public async Task WheelUsesCurrentPointerAndSignedDataWithoutMoving()
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        Assert.True((await player.SendAsync(new PointerCommand(PointerKind.VerticalWheel, new(double.NaN, 0), WheelDelta: -120))).Queued);
        var input = Assert.Single(Assert.Single(native.Batches));
        Assert.Equal(0x800u, input.Flags); Assert.Equal(unchecked((uint)-120), input.Data);
        Assert.Equal((0, 0), (input.X, input.Y));
        native.Pointer = null;
        Assert.Equal("PointerUnavailable", (await player.SendAsync(new PointerCommand(PointerKind.VerticalWheel, new(0, 0), WheelDelta: 120))).Error!.Code);
        Assert.Single(native.Batches);
    }

    [Fact]
    public async Task OutsideAndMonitorGapsNeverInject()
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        Assert.Equal("InvalidInput", (await player.SendAsync(new PointerCommand(PointerKind.Move, new(1920, 0)))).Error!.Code);
        native.OnMonitor = false;
        Assert.Equal("InvalidInput", (await player.SendAsync(new PointerCommand(PointerKind.Move, new(0, 0)))).Error!.Code);
        Assert.Empty(native.Batches);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0x11)]
    public async Task PhysicallyHeldRequiredInputIsNeverReleased(int virtualKey)
    {
        var native = new ScreenPlayerNative(); native.Held.Add(virtualKey); var player = new WindowsScreenInputPlayer(native);
        InputCommand command = virtualKey == 1 ? new PointerCommand(PointerKind.Down, new(0, 0), MouseButton.Left) : new KeyCommand(KeyKind.Down, new("Ctrl"));
        Assert.False((await player.SendAsync(command)).Queued);
        Assert.True((await player.ReleaseHeldAsync()).Queued); Assert.Empty(native.Batches);
    }

    [Fact]
    public async Task CancelledBeforeSendNeverInjects()
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal("Cancelled", (await player.SendAsync(new TextCommand("hello"), cancelled.Token)).Error!.Code);
        Assert.Empty(native.Batches);
    }

    [Fact]
    public async Task CancellationAfterInsertedDownReleasesExactlyOnce()
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        using var cancelled = new CancellationTokenSource(); native.AfterSend = () => cancelled.Cancel();
        var result = await player.SendAsync(new KeyCommand(KeyKind.Down, new("A")), cancelled.Token);
        Assert.Equal("Cancelled", result.Error!.Code);
        Assert.Equal(new uint[] { 0, 2 }, native.Batches.SelectMany(x => x).Select(x => x.Flags));
        Assert.True((await player.ReleaseHeldAsync()).Queued); Assert.Equal(2, native.Batches.Count);
    }

    [Fact]
    public async Task PartialUnicodeInsertionReleasesOnlyInsertedDown()
    {
        var native = new ScreenPlayerNative(); native.Insertions.Enqueue(1); var player = new WindowsScreenInputPlayer(native);
        var result = await player.SendAsync(new TextCommand("ab"));
        Assert.Equal("DeliveryFailed", result.Error!.Code); Assert.Null(result.CleanupError);
        var cleanup = Assert.Single(native.Batches[1]); Assert.Equal((ushort)'a', cleanup.ScanCode); Assert.Equal(6u, cleanup.Flags);
        Assert.True((await player.ReleaseHeldAsync()).Queued); Assert.Equal(2, native.Batches.Count);
    }

    [Fact]
    public async Task FailedCleanupRetainsDeliveryErrorAndRetriesOwnedRelease()
    {
        var native = new ScreenPlayerNative(); native.Insertions.Enqueue(1); native.Insertions.Enqueue(0);
        var player = new WindowsScreenInputPlayer(native); var result = await player.SendAsync(new TextCommand("a"));
        Assert.Equal("DeliveryFailed", result.Error!.Code); Assert.NotNull(result.CleanupError); Assert.Equal("CleanupFailed", result.CleanupError.Code);
        Assert.True((await player.ReleaseHeldAsync()).Queued); Assert.Equal(3, native.Batches.Count);
    }

    [Fact]
    public async Task UnownedUpNeverReleasesPhysicalInput()
    {
        var native = new ScreenPlayerNative(); native.Held.Add(65); var player = new WindowsScreenInputPlayer(native);
        Assert.False((await player.SendAsync(new KeyCommand(KeyKind.Up, new("A")))).Queued);
        Assert.Empty(native.Batches);
    }

    [Fact]
    public async Task ReleasesKeysInReverseAcquisitionOrder()
    {
        var native = new ScreenPlayerNative(); var player = new WindowsScreenInputPlayer(native);
        await player.SendAsync(new KeyCommand(KeyKind.Down, new("Ctrl")));
        await player.SendAsync(new KeyCommand(KeyKind.Down, new("Shift")));
        await player.SendAsync(new KeyCommand(KeyKind.Down, new("A")));
        Assert.True((await player.ReleaseHeldAsync()).Queued);
        Assert.Equal(new ushort[] { 65, 0x10, 0x11 }, native.Batches[3].Select(x => x.VirtualKey));
    }
}

internal sealed class ScreenPlayerNative : IScreenPlayerNative
{
    public List<ScreenNativeInput[]> Batches { get; } = [];
    public Queue<int> Insertions { get; } = new();
    public HashSet<int> Held { get; } = [];
    public bool OnMonitor { get; set; } = true;
    public PointerPoint? Pointer { get; set; } = new(-800, 250);
    public Action? AfterSend { get; set; }
    public ScreenGeometry ReadGeometry() => new(-1920, 0, 3840, 1080);
    public PointerPoint? ReadPointer() => Pointer;
    public bool IsPointOnMonitor(PointerPoint point) => OnMonitor;
    public bool IsKeyHeld(int key) => Held.Contains(key);
    public int Send(ScreenNativeInput[] inputs) { Batches.Add(inputs); var count = Insertions.Count == 0 ? inputs.Length : Insertions.Dequeue(); AfterSend?.Invoke(); return count; }
}


internal sealed class FailingClickerNative : IScreenInputNative
{
    private bool first = true;
    public ScreenBounds ReadBounds() => new(0, 0, 100, 100);
    public PointerPoint? ReadPointer() => new(10, 10);
    public bool IsPointOnMonitor(PointerPoint point) => true;
    public bool IsLeftButtonHeld() => false;
    public int Send(ScreenMouseInput[] inputs) { if (first) { first = false; return 2; } return 0; }
}

