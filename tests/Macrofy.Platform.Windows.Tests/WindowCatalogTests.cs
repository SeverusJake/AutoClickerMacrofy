using Macrofy.Platform.Models;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Macrofy.Platform.Windows.Tests;

public class WindowCatalogTests
{
    private static readonly TargetRule Rule = new(new AppIdentity("game", @"C:\game.exe"), "*Game*");
    [Fact]
    public async Task TwoMatchingTitlesRequireSelectionAndNoMatchIsMissing()
    {
        using var native = new FakeWindows();
        native.Windows.Add(FakeWindows.Window(11));
        native.Windows.Add(FakeWindows.Window(12));
        using var catalog = new WindowsWindowCatalog(native);
        var result = Assert.IsType<ResolutionResult.Ambiguous>(await catalog.ResolveAsync(Rule));
        Assert.Equal(2, result.Candidates.Count);
        var selected = Assert.IsType<ResolutionResult.Matched>(await catalog.ResolveAsync(Rule, result.Candidates[1].Token));
        Assert.Equal(result.Candidates[1].Token, selected.Window.Token);
        native.Windows.Clear();
        Assert.IsType<ResolutionResult.Missing>(await catalog.ResolveAsync(Rule));
    }
    [Fact]
    public async Task ProcessRestartInvalidatesOldToken()
    {
        using var native = new FakeWindows(); native.Windows.Add(FakeWindows.Window(11));
        using var catalog = new WindowsWindowCatalog(native);
        var old = Assert.Single(await catalog.ListAsync()).Token;
        native.Windows[0] = native.Windows[0] with { ProcessStartTicks = 2 };
        Assert.Null((await catalog.GetAsync(old)).Context);
        Assert.NotEqual(old, Assert.Single(await catalog.ListAsync()).Token);
    }
    [Fact]
    public async Task SameProcessWindowDestructionCannotResurrectToken()
    {
        using var native = new FakeWindows(); native.Windows.Add(FakeWindows.Window(11));
        using var catalog = new WindowsWindowCatalog(native);
        var old = Assert.Single(await catalog.ListAsync()).Token;
        var lost = new List<TargetToken>(); catalog.TargetLost += lost.Add;
        native.Destroy(11);
        // Same handle and same process reappear; destruction must still make old token unusable.
        Assert.Null((await catalog.GetAsync(old)).Context);
        Assert.NotEqual(old, Assert.Single(await catalog.ListAsync()).Token);
        Assert.Single(lost, old);
    }
    [Fact]
    public async Task ChildSurfaceDestructionInvalidatesParentToken()
    {
        using var native = new FakeWindows(); native.Windows.Add(FakeWindows.Window(11) with { SurfaceHandle = 111 });
        using var catalog = new WindowsWindowCatalog(native);
        var old = Assert.Single(await catalog.ListAsync()).Token;
        native.Destroy(111);
        Assert.Null((await catalog.GetAsync(old)).Context);
    }
    [Fact]
    public async Task LiveGeometryUpdatesAndMinimizedUsesOnlyBoundTokensCache()
    {
        using var native = new FakeWindows(); native.Windows.Add(FakeWindows.Window(11) with { Geometry = new(800,600,1.5) });
        using var catalog = new WindowsWindowCatalog(native);
        var token = Assert.Single(await catalog.ListAsync()).Token;
        Assert.Equal(1.5, (await catalog.GetAsync(token)).Context!.Window.Geometry!.DpiScale);
        native.Windows[0] = native.Windows[0] with { Geometry = new(1200,900,2) };
        Assert.Equal(1200, (await catalog.GetAsync(token)).Context!.Window.Geometry!.Width);
        native.Windows[0] = native.Windows[0] with { Geometry = null, IsMinimized = true };
        Assert.Equal(1200, (await catalog.GetAsync(token)).Context!.Window.Geometry!.Width);
        native.Destroy(11);
        var fresh = Assert.Single(await catalog.ListAsync());
        Assert.Null(fresh.Geometry);
    }
    [Fact]
    public async Task ZeroGeometryIsNotUsedForConversion()
    {
        using var native = new FakeWindows(); native.Windows.Add(FakeWindows.Window(11) with { Geometry = new(0,0,1) });
        using var catalog = new WindowsWindowCatalog(native);
        Assert.Null(Assert.Single(await catalog.ListAsync()).Geometry);
    }
    [Fact]
    public async Task ChangedTitleChangesFingerprintWithoutSilentlyRetargeting()
    {
        using var native = new FakeWindows(); native.Windows.Add(FakeWindows.Window(11));
        using var catalog = new WindowsWindowCatalog(native);
        var token = Assert.Single(await catalog.ListAsync()).Token;
        var before = (await catalog.GetAsync(token)).Context!;
        native.Windows[0] = native.Windows[0] with { Title = "Game — changed" };
        Assert.NotEqual(before.SurfaceFingerprint, (await catalog.GetAsync(token)).Context!.SurfaceFingerprint);
    }
    [Fact]
    public async Task PermissionDeniedIsReportedWithoutElevation()
    {
        using var native = new FakeWindows(); native.Windows.Add(FakeWindows.Window(11));
        using var catalog = new WindowsWindowCatalog(native);
        var token = Assert.Single(await catalog.ListAsync()).Token;
        var permission = new WindowsPermissionService(catalog.Registry, new FakeAccess());
        var result = await permission.CheckAsync(token);
        Assert.False(result.Allowed);
        Assert.Equal("PermissionDenied", result.Error!.Code);
    }
}
internal sealed class FakeWindows : IWindowNative
{
    public List<NativeWindow> Windows { get; } = [];
    public int CurrentProcessId => 999;
    public event Action<nint>? Destroyed;
    public IReadOnlyList<NativeWindow> EnumerateWindows() => Windows.ToArray();
    public NativeWindow? ReadWindow(nint top, nint surface) => Windows.FirstOrDefault(w => w.TopHandle == top && w.SurfaceHandle == surface);
    public void Destroy(nint handle) => Destroyed?.Invoke(handle);
    public void Dispose() { }
    public static NativeWindow Window(int handle) => new(handle,handle,42,1,@"C:\game.exe","Game","Surface",new(800,600,1),false,false,"file-v1");
}
internal sealed class FakeAccess : IProcessAccess
{
    public int? GetIntegrityLevel(int processId) => processId == Environment.ProcessId ? 0x2000 : 0x3000;
}
