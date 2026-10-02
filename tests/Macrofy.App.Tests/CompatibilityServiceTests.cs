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
        public ValueTask<TargetContextResult> GetAsync(TargetToken target, CancellationToken cancellationToken = default) => ValueTask.FromResult(new TargetContextResult(Context));
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Fixture
    {
        public WorkspaceDocument Document = WorkspaceDocument.CreateDefault();
        public ContextSource Source = new();
        public Guid AppId => Document.Profiles[0].Apps[0].Id;
        public TargetToken Token = new(Guid.NewGuid());
        public bool Busy, Occupied;
        public List<PlaybackBinding> Sent = [];
        public Fixture() => Source.Context = new(new(Token, new("CookieRun", Document.Profiles[0].Apps[0].Executable), "CookieRun: Crumble - Idle RPG", true, new(800, 600, 1)), false, "surface");
        public CompatibilityService Service() => new(Document, Source, () => { if (Busy) return null; Occupied = true; return new Lease(() => Occupied = false); },
            (binding, _, _) => { Assert.True(Occupied); Sent.Add(binding); return ValueTask.FromResult(new GestureResult(new(true))); });
        public CompiledAction Click = new CompiledAction.Click(new(20, 30), CoordinateMode.FixedPixels, 0);
    }
    [Fact]
    public async Task SendsOneActionWithWindowStateAndReleasesInput()
    {
        var f = new Fixture(); var service = f.Service();
        var result = await service.SendTestAsync(f.AppId, f.Token, f.Click, TestContext.Current.CancellationToken);
        Assert.True(result.Result.Delivery.Queued); Assert.Equal(TargetState.Minimized, result.State); Assert.Equal(InputCapability.Click, result.Capability);
        Assert.Equal(f.Token, Assert.Single(f.Sent).Token); Assert.False(f.Occupied);
        f.Source.Context = f.Source.Context! with { Window = f.Source.Context.Window with { IsMinimized = false } };
        var shortcut = await service.SendTestAsync(f.AppId, f.Token, new CompiledAction.Key([new("Ctrl"), new("A")], 0), TestContext.Current.CancellationToken);
        Assert.Equal(InputCapability.Shortcut, shortcut.Capability); Assert.Equal(TargetState.BackgroundVisible, shortcut.State);
        Assert.Equal(2, f.Sent.Count); Assert.False(f.Occupied);
    }
    [Fact]
    public async Task BusyWrongAppOutsidePointCancelledAndClosedTestsSendNothing()
    {
        var f = new Fixture(); var service = f.Service(); f.Busy = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(f.AppId, f.Token, f.Click, TestContext.Current.CancellationToken));
        f.Busy = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(Guid.NewGuid(), f.Token, f.Click, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(f.AppId, f.Token, new CompiledAction.Click(new(double.NaN, 20), CoordinateMode.FixedPixels, 0), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(f.AppId, f.Token, new CompiledAction.Click(new(900, 20), CoordinateMode.FixedPixels, 0), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendTestAsync(f.AppId, f.Token, f.Click, new(true)));
        f.Source.Context = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(f.AppId, f.Token, f.Click, TestContext.Current.CancellationToken));
        Assert.Empty(f.Sent); Assert.False(f.Occupied);
    }
}
