using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.IntegrationTests;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows;
using Macrofy.Platform.Windows.Interop;
using Xunit;

namespace Macrofy.App.Tests;

public class PlaybackIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiniteCoordinatorLoopsDeliverOrderedClickKeyUnicodeAndWait(bool minimized)
    {
        using var target = await TargetFixture.StartAsync(TestContext.Current.CancellationToken);
        if (!minimized) await target.RequestAsync("place-background");
        await using var f = await Composition.CreateAsync(target, minimized);
        if (!minimized) Assert.NotNull(f.Catalog.ReadPlaybackPointerPosition(f.Window.Token).Point);
        var actions = new List<CompiledAction> {
            new CompiledAction.Click(new(30, 40), CoordinateMode.FixedPixels, 0),
            new CompiledAction.Key([new("A")], 0), new CompiledAction.Text("零😀", 0), new CompiledAction.Wait(60, 0)
        };
        if (!minimized) actions.Add(new CompiledAction.Wheel(120, 0));
        var request = f.Request(actions, repeat: 2);
        await f.RunAsync(request);
        var receipts = await EventsAsync(target, minimized ? 14 : 16);
        var sequence = minimized ? new uint[] { 0x201, 0x202, 0x100, 0x101, 0x102, 0x102, 0x102 }
            : [0x201, 0x202, 0x100, 0x101, 0x102, 0x102, 0x102, 0x20A];
        Assert.Equal(sequence.Concat(sequence), receipts.Select(Message));
        Assert.Equal(new ulong[] { 0x96F6, 0xD83D, 0xDE00, 0x96F6, 0xD83D, 0xDE00 }, receipts.Where(e => Message(e) == 0x102).Select(WParam));
        Assert.Equal(30, unchecked((short)receipts[0].GetProperty("LParam").GetInt64()));
        Assert.Equal(40, unchecked((short)(receipts[0].GetProperty("LParam").GetInt64() >> 16)));
        var afterWait = receipts[7].GetProperty("Timestamp").GetInt64();
        var beforeWait = receipts[6].GetProperty("Timestamp").GetInt64();
        Assert.True(Stopwatch.GetElapsedTime(beforeWait, afterWait) >= TimeSpan.FromMilliseconds(50), "Wait must delay subsequent native delivery.");
        if (!minimized) Assert.All(receipts.Where(e => Message(e) == 0x20A), e => Assert.Equal(120, unchecked((short)(WParam(e) >> 16))));
        var snapshot = f.Coordinator.Snapshots[request.MacroId];
        Assert.Equal(2, snapshot.CompletedLoops);
        Assert.Equal(minimized ? 8 : 10, snapshot.CompletedSteps);
        f.Native.AssertUnchangedDesktop();
    }

    [Fact]
    public async Task MinimizedWheelRejectsCurrentPointerOutsideClientWithoutFallback()
    {
        using var target = await TargetFixture.StartAsync(TestContext.Current.CancellationToken);
        await using var f = await Composition.CreateAsync(target, true);
        var pointer = f.Catalog.ReadPlaybackPointerPosition(f.Window.Token);
        Assert.Null(pointer.Point); Assert.Equal("OutsideClient", pointer.Error!.Code);
        var request = f.Request([new CompiledAction.Wheel(120, 0)]);
        Assert.True((await f.Coordinator.StartAsync(request, TestContext.Current.CancellationToken)).Queued);
        await f.CompleteAsync(request);
        Assert.Equal(PlaybackState.Error, f.Coordinator.Snapshots[request.MacroId].State);
        Assert.Equal("InvalidInput", f.Coordinator.Snapshots[request.MacroId].DeliveryError!.Code);
        Assert.Empty((await target.RequestAsync("snapshot")).GetProperty("Events").EnumerateArray());
        Assert.Empty(f.Native.Observations);
    }

    [Fact]
    public async Task ConcurrentMacrosKeepSharedTargetChordsAndTextContiguous()
    {
        using var target = await TargetFixture.StartAsync(TestContext.Current.CancellationToken);
        await using var f = await Composition.CreateAsync(target, false);
        var first = f.Request([new CompiledAction.Key([new("Control"), new("A")], 0), new CompiledAction.Text("ab", 0), new CompiledAction.Wait(30, 0)], repeat: 2);
        var second = f.Request([new CompiledAction.Key([new("Shift"), new("B")], 0), new CompiledAction.Text("XY", 0), new CompiledAction.Wait(30, 0)], repeat: 2);
        var results = await Task.WhenAll(f.Coordinator.StartAsync(first, TestContext.Current.CancellationToken), f.Coordinator.StartAsync(second, TestContext.Current.CancellationToken));
        Assert.All(results, r => Assert.True(r.Queued, r.Error?.Message));
        await Task.WhenAll(f.CompleteAsync(first), f.CompleteAsync(second));
        var receipts = await EventsAsync(target, 24);
        var groups = new List<string>();
        for (var i = 0; i < receipts.Length;)
        {
            if (Message(receipts[i]) == 0x100)
            {
                Assert.Equal(new uint[] { 0x100, 0x100, 0x101, 0x101 }, receipts.Skip(i).Take(4).Select(Message));
                var keys = receipts.Skip(i).Take(4).Select(WParam).ToArray();
                Assert.True(keys.SequenceEqual(new ulong[] { 17, 65, 65, 17 }) || keys.SequenceEqual(new ulong[] { 16, 66, 66, 16 }));
                groups.Add(keys[0] == 17 ? "ctrl" : "shift"); i += 4;
            }
            else
            {
                Assert.Equal(new uint[] { 0x102, 0x102 }, receipts.Skip(i).Take(2).Select(Message));
                var text = new string(receipts.Skip(i).Take(2).Select(e => (char)WParam(e)).ToArray());
                Assert.Contains(text, new[] { "ab", "XY" }); groups.Add(text); i += 2;
            }
        }
        Assert.All(new[] { "ctrl", "shift", "ab", "XY" }, value => Assert.Equal(2, groups.Count(g => g == value)));
        Assert.All(f.Coordinator.Snapshots.Values, s => { Assert.Equal(PlaybackState.Completed, s.State); Assert.Equal(2, s.CompletedLoops); Assert.Null(s.CleanupError); });
        f.Native.AssertUnchangedDesktop();
    }

    [Fact]
    public async Task CancellationDuringWaitStopsFutureInputPromptly()
    {
        using var target = await TargetFixture.StartAsync(TestContext.Current.CancellationToken);
        await using var f = await Composition.CreateAsync(target, false);
        var request = f.Request([new CompiledAction.Text("a", 0), new CompiledAction.Wait(5000, 0), new CompiledAction.Text("NEVER", 0)]);
        Assert.True((await f.Coordinator.StartAsync(request, TestContext.Current.CancellationToken)).Queued);
        await EventsAsync(target, 1);
        await WaitAsync(() => Task.FromResult(f.Coordinator.Snapshots[request.MacroId].State == PlaybackState.Waiting));
        f.Coordinator.Stop(request.MacroId);
        await f.Coordinator.WaitForCompletionAsync(request.MacroId, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal(PlaybackState.Stopped, f.Coordinator.Snapshots[request.MacroId].State);
        Assert.Null(f.Coordinator.Snapshots[request.MacroId].CleanupError);
        Assert.Single((await target.RequestAsync("snapshot")).GetProperty("Events").EnumerateArray());
        f.Native.AssertUnchangedDesktop();
    }

    [Fact]
    public async Task CancellationAfterNativeChordDownReleasesOnlyOwnedOriginalKey()
    {
        using var target = await TargetFixture.StartAsync(TestContext.Current.CancellationToken);
        await using var f = await Composition.CreateAsync(target, false);
        using var cancellation = new CancellationTokenSource();
        f.Native.AfterPost = message => { if (message.Id == 0x100) cancellation.Cancel(); };
        var request = f.Request([new CompiledAction.Key([new("Control"), new("A")], 0), new CompiledAction.Text("NEVER", 0)]);
        Assert.True((await f.Coordinator.StartAsync(request, cancellation.Token)).Queued);
        await f.CompleteAsync(request);
        var receipts = await EventsAsync(target, 2);
        Assert.Equal(new uint[] { 0x100, 0x101 }, receipts.Select(Message));
        Assert.Equal(new ulong[] { 17, 17 }, receipts.Select(WParam));
        Assert.Equal(PlaybackState.Stopped, f.Coordinator.Snapshots[request.MacroId].State);
        Assert.Null(f.Coordinator.Snapshots[request.MacroId].CleanupError);
        Assert.Single(receipts.Select(e => e.GetProperty("Hwnd").GetInt64()).Distinct());
        f.Native.AssertUnchangedDesktop();
    }

    [Fact]
    public async Task DestroyedOriginalDuringChordCannotSendOrCleanupToReplacement()
    {
        using var target = await TargetFixture.StartAsync(TestContext.Current.CancellationToken);
        await using var f = await Composition.CreateAsync(target, false);
        f.Native.AfterPost = message => { if (message.Id == 0x100) target.RequestAsync("recreate").GetAwaiter().GetResult(); };
        var request = f.Request([new CompiledAction.Key([new("Control"), new("A")], 0), new CompiledAction.Text("NEVER", 0)]);
        Assert.True((await f.Coordinator.StartAsync(request, TestContext.Current.CancellationToken)).Queued);
        await f.CompleteAsync(request);
        var receipts = await EventsAsync(target, 1);
        Assert.Equal(0x100u, Message(receipts[0])); Assert.Equal(17ul, WParam(receipts[0]));
        var snapshot = f.Coordinator.Snapshots[request.MacroId];
        Assert.Equal(PlaybackState.Error, snapshot.State);
        Assert.NotNull(snapshot.DeliveryError); Assert.NotNull(snapshot.CleanupError);
        Assert.Null((await f.Catalog.GetAsync(f.Window.Token, TestContext.Current.CancellationToken)).Context);
        var replacement = (await f.Catalog.ListAsync(TestContext.Current.CancellationToken)).Single(w => w.Title == f.Window.Title);
        Assert.NotEqual(f.Window.Token, replacement.Token);
        Assert.Single(f.Native.Observations);
        f.Native.AssertUnchangedDesktop();
    }

    [Fact]
    public async Task ScreenCoordinatorDeliversFiniteSequenceOnlyToExposedFocusedHarmlessSurface()
    {
        using var target = await TargetFixture.StartAsync(TestContext.Current.CancellationToken);
        await using var desktop = new DesktopRestoration();
        // Every check precedes the first injection. Failure here sends no physical input.
        var diagnostics = await target.RequestAsync("screen");
        var surface = diagnostics.GetProperty("Surface");
        Assert.True(surface.GetProperty("Focused").GetBoolean(), diagnostics.GetRawText());
        Assert.True(surface.GetProperty("Exposed").GetBoolean(), diagnostics.GetRawText());
        var safe = new PointerPoint(surface.GetProperty("SafeX").GetInt32(), surface.GetProperty("SafeY").GetInt32());
        var screen = new ControlledScreenPlayer(surface);
        await using var f = await Composition.CreateAsync(target, false, screen);
        await target.RequestAsync("clear");
        var request = new PlaybackRequest(Guid.NewGuid(), "Controlled screen", "Integration", new(null, null, null, null),
            [new CompiledAction.Click(safe, CoordinateMode.FixedPixels, 0), new CompiledAction.Key([new("A")], 0),
             new CompiledAction.Text("零😀", 0), new CompiledAction.Wait(60, 0), new CompiledAction.Wheel(120, 0)], 1, 0, 0);
        await f.RunAsync(request);
        try { await WaitAsync(async () =>
        {
            var events = (await target.RequestAsync("snapshot")).GetProperty("Events").EnumerateArray().ToArray();
            return events.Count(e => Message(e) == 0x102) == 3 && events.Count(e => Message(e) == 0x20A) == 1;
        }); }
        catch (TimeoutException e) { throw new InvalidOperationException((await target.RequestAsync("snapshot")).GetRawText(), e); }
        var receipts = (await target.RequestAsync("snapshot")).GetProperty("Events").EnumerateArray().ToArray();
        var delivered = receipts.Where(e => Message(e) is 0x201 or 0x202 or 0x20A || Message(e) == 0x102 || Message(e) is 0x100 or 0x101 && WParam(e) == 65).ToArray();
        Assert.Equal(new uint[] { 0x201, 0x202, 0x100, 0x101, 0x102, 0x102, 0x102, 0x20A }, delivered.Select(Message));
        Assert.Equal(new ulong[] { 0x96F6, 0xD83D, 0xDE00 }, delivered.Where(e => Message(e) == 0x102).Select(WParam));
        Assert.All(delivered, e => Assert.Equal(surface.GetProperty("Child").GetInt64(), e.GetProperty("Hwnd").GetInt64()));
        Assert.Equal(80, unchecked((short)delivered[0].GetProperty("LParam").GetInt64()));
        Assert.Equal(80, unchecked((short)(delivered[0].GetProperty("LParam").GetInt64() >> 16)));
        Assert.Equal(120, unchecked((short)(WParam(delivered[^1]) >> 16)));
        Assert.Equal((int)safe.X, unchecked((short)delivered[^1].GetProperty("LParam").GetInt64()));
        Assert.Equal((int)safe.Y, unchecked((short)(delivered[^1].GetProperty("LParam").GetInt64() >> 16)));
        Assert.Equal(6, receipts.Count(e => Message(e) is 0x100 or 0x101 && WParam(e) == 0xE7));
        Assert.Equal(1, f.Coordinator.Snapshots[request.MacroId].CompletedLoops);
        Assert.Empty(f.Native.Observations);
        TestContext.Current.TestOutputHelper?.WriteLine("Screen delivery receipts: " + JsonSerializer.Serialize(delivered));
        // Disposal order: coordinator cleanup, then exact cursor/foreground restoration, then receiver close.
    }

    private static uint Message(JsonElement receipt) => receipt.GetProperty("Message").GetUInt32();
    private static ulong WParam(JsonElement receipt) => receipt.GetProperty("WParam").GetUInt64();
    private static async Task<JsonElement[]> EventsAsync(TargetFixture target, int count)
    {
        JsonElement snapshot = default;
        try { await WaitAsync(async () => { snapshot = await target.RequestAsync("snapshot"); return snapshot.GetProperty("Events").GetArrayLength() >= count; }); }
        catch (TimeoutException e) { throw new InvalidOperationException(snapshot.GetRawText(), e); }
        var receipts = snapshot.GetProperty("Events").EnumerateArray().ToArray();
        Assert.Equal(count, receipts.Length); return receipts;
    }
    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!await condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Controlled receiver condition timed out."); await Task.Delay(10); }
    }

    private sealed class Composition : IAsyncDisposable
    {
        public WindowsWindowCatalog Catalog { get; } = new();
        public ReceiptNative Native { get; } = new();
        public TargetWindow Window { get; private set; } = null!;
        public PlaybackCoordinator Coordinator { get; private set; } = null!;
        private WindowsInputPlayer player = null!;
        private SavedApp app = null!;
        private TargetState state;
        public static async Task<Composition> CreateAsync(TargetFixture target, bool minimized, IScreenInputPlayer? screen = null)
        {
            var f = new Composition();
            try
            {
                f.Window = (await f.Catalog.ListAsync(TestContext.Current.CancellationToken)).Single(w => w.Title == target.PipeName + " A");
                if (minimized)
                {
                    await target.RequestAsync("minimize");
                    await WaitAsync(async () => (await f.Catalog.GetAsync(f.Window.Token, TestContext.Current.CancellationToken)).Context!.Window.IsMinimized);
                }
                var context = (await f.Catalog.GetAsync(f.Window.Token, TestContext.Current.CancellationToken)).Context!;
                f.state = minimized ? TargetState.Minimized : TargetState.BackgroundVisible;
                f.app = new SavedApp { Name = context.Window.App.Name, Executable = target.ExecutablePath, TitleRule = f.Window.Title };
                // Seeded test evidence simulates the user's observation. Receipts below prove delivery only.
                var document = new WorkspaceDocument { Profiles = [new Profile { Apps = [f.app] }], CompatibilityEvidence =
                    Enum.GetValues<InputCapability>().Select(capability => new CompatibilityEvidence(f.app.Id, context.Window.App,
                        context.Window.Title, context.SurfaceFingerprint, f.state, capability, context.Window.Geometry!, DateTimeOffset.UtcNow, true)).ToList() };
                f.player = new WindowsInputPlayer(f.Catalog, f.Native, new InputClock(), new WindowsPermissionService(f.Catalog));
                var guard = new InputActivityGuard();
                var sender = new WindowsGestureSender(f.Catalog, f.player, screen ?? new RejectScreenPlayer(), () => true);
                var compatibility = new CompatibilityService(document, f.Catalog, () => guard.TryEnterTest(out var lease) ? lease : null,
                    sender.SendGestureAsync, (_, _, _) => throw new InvalidOperationException("Integration fixture never persists observations."));
                f.Coordinator = new PlaybackCoordinator(new WindowsPlaybackExecutor(f.Catalog, sender, compatibility), TimeProvider.System);
                await target.RequestAsync("clear"); return f;
            }
            catch { f.player?.Dispose(); f.Catalog.Dispose(); throw; }
        }
        public PlaybackRequest Request(IReadOnlyList<CompiledAction> actions, int repeat = 1) => new(Guid.NewGuid(), "Controlled window", "Integration",
            new(app.Id, new(Window.App, Window.Title), state, Window.Token), actions, repeat, 0, 0);
        public async Task CompleteAsync(PlaybackRequest request) => await Coordinator.WaitForCompletionAsync(request.MacroId).WaitAsync(TimeSpan.FromSeconds(5));
        public async Task RunAsync(PlaybackRequest request)
        {
            var started = await Coordinator.StartAsync(request); Assert.True(started.Queued, started.Error?.ToString());
            await CompleteAsync(request);
            var snapshot = Coordinator.Snapshots[request.MacroId];
            Assert.True(snapshot.State == PlaybackState.Completed, snapshot.ToString()); Assert.Null(snapshot.DeliveryError); Assert.Null(snapshot.CleanupError);
        }
        public async ValueTask DisposeAsync() { await Coordinator.DisposeAsync(); player.Dispose(); Catalog.Dispose(); }
    }

    private sealed class ReceiptNative : IInputNative
    {
        private readonly Win32InputNative inner = new();
        public Action<NativeMessage>? AfterPost { get; set; }
        public List<(WindowNative.Point Before, WindowNative.Point After, nint ForegroundBefore, nint ForegroundAfter)> Observations { get; } = [];
        public NativeSendResult Post(nint target, NativeMessage message)
        {
            Assert.True(WindowNative.GetCursorPos(out var before)); var foreground = WindowNative.GetForegroundWindow();
            var result = inner.Post(target, message);
            Assert.True(WindowNative.GetCursorPos(out var after));
            Observations.Add((before, after, foreground, WindowNative.GetForegroundWindow()));
            AfterPost?.Invoke(message); return result;
        }
        public PointerPoint? ToScreen(nint target, PointerPoint point) => inner.ToScreen(target, point);
        public int GetScanCode(nint target, int key) => inner.GetScanCode(target, key);
        public void AssertUnchangedDesktop() => Assert.All(Observations, o => { Assert.Equal(o.Before.X, o.After.X); Assert.Equal(o.Before.Y, o.After.Y); Assert.Equal(o.ForegroundBefore, o.ForegroundAfter); });
    }
    private sealed class RejectScreenPlayer : IScreenInputPlayer
    {
        public ScreenGeometry ReadGeometry() => throw new InvalidOperationException("Window integration must never use Screen.");
        public ScreenPointResult ReadPointer() => throw new InvalidOperationException("Window integration must never use Screen.");
        public ValueTask<DeliveryResult> SendAsync(InputCommand command, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Window integration must never inject Screen.");
        public ValueTask<DeliveryResult> ReleaseHeldAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Window integration must never clean Screen.");
    }
    private sealed class ControlledScreenPlayer(JsonElement surface) : IScreenInputPlayer
    {
        private readonly WindowsScreenInputPlayer inner = new();
        public ScreenGeometry ReadGeometry() => inner.ReadGeometry();
        public ScreenPointResult ReadPointer() => inner.ReadPointer();
        public ValueTask<DeliveryResult> SendAsync(InputCommand command, CancellationToken cancellationToken = default)
        {
            var hwnd = (nint)surface.GetProperty("Hwnd").GetInt64();
            var child = (nint)surface.GetProperty("Child").GetInt64();
            var safe = new WindowNative.Point { X = surface.GetProperty("SafeX").GetInt32(), Y = surface.GetProperty("SafeY").GetInt32() };
            var gui = new DesktopNative.GuiInfo { Size = (uint)Marshal.SizeOf<DesktopNative.GuiInfo>() };
            var ready = WindowNative.IsWindowVisible(hwnd) && !WindowNative.IsIconic(hwnd) && WindowNative.GetForegroundWindow() == hwnd &&
                DesktopNative.WindowFromPoint(safe) == child && DesktopNative.GetGUIThreadInfo(WindowNative.GetWindowThreadProcessId(hwnd, out _), ref gui) && gui.Focus == child;
            if (!ready) return ValueTask.FromResult(new DeliveryResult(false, new("UnsafeFixture", "Dedicated receiver lost focus or exposure before input insertion.")));
            if (command is PointerCommand pointer && pointer.Point != new PointerPoint(safe.X, safe.Y))
                return ValueTask.FromResult(new DeliveryResult(false, new("UnsafeFixture", "Pointer left the verified harmless safe point.")));
            return inner.SendAsync(command, cancellationToken);
        }
        // Owned releases must still run if focus/exposure changes after an inserted down.
        public ValueTask<DeliveryResult> ReleaseHeldAsync(CancellationToken cancellationToken = default) => inner.ReleaseHeldAsync(cancellationToken);
    }
    private sealed class DesktopRestoration : IAsyncDisposable
    {
        private readonly WindowNative.Point cursor;
        private readonly nint foreground = WindowNative.GetForegroundWindow();
        public DesktopRestoration() { Assert.True(WindowNative.GetCursorPos(out cursor)); }
        public async ValueTask DisposeAsync()
        {
            try { Assert.True(DesktopNative.SetCursorPos(cursor.X, cursor.Y)); }
            finally { if (foreground != 0 && WindowNative.IsWindow(foreground)) Assert.True(DesktopNative.SetForegroundWindow(foreground), "Original foreground restoration failed."); }
            Assert.True(WindowNative.GetCursorPos(out var restored)); Assert.Equal(cursor.X, restored.X); Assert.Equal(cursor.Y, restored.Y);
            // Cross-thread foreground activation completes asynchronously after SetForegroundWindow returns.
            if (foreground != 0 && WindowNative.IsWindow(foreground)) await WaitAsync(() => Task.FromResult(WindowNative.GetForegroundWindow() == foreground));
            TestContext.Current.TestOutputHelper?.WriteLine($"Desktop restored: cursor=({restored.X},{restored.Y}), foreground={WindowNative.GetForegroundWindow()}, original={foreground}.");
        }
    }
    private static class DesktopNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo { public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public WindowNative.Rect Rect; }
        [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        [DllImport("user32.dll")] internal static extern nint WindowFromPoint(WindowNative.Point point);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    }
}
