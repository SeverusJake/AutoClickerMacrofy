using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows.Interop;
using Xunit;
namespace Macrofy.Platform.Windows.Tests;
public class WindowIntegrationTests
{
    [Fact]
    public async Task ControlledWindowsAreDiscoveredWithoutFocusChangeAndClosureInvalidatesToken()
    {
        using var target = await TargetFixture.StartAsync();
        using var catalog = new WindowsWindowCatalog();
        var before = WindowNative.GetForegroundWindow();
        var rule = new TargetRule(new("Macrofy.TestTarget", target.ExecutablePath), $"*{target.PipeName}*");
        var result = Assert.IsType<ResolutionResult.Ambiguous>(await catalog.ResolveAsync(rule));
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(before, WindowNative.GetForegroundWindow());
        var first = result.Candidates.First(w => w.Title.EndsWith(" A", StringComparison.Ordinal));
        var context = (await catalog.GetAsync(first.Token)).Context!;
        Assert.True(context.Window.Geometry!.Width > 0);
        Assert.True((await new WindowsPermissionService(catalog).CheckAsync(first.Token)).Allowed);
        await target.RequestAsync("minimize");
        await WaitUntilAsync(async () => (await catalog.GetAsync(first.Token)).Context!.Window.IsMinimized);
        Assert.Equal(context.Window.Geometry, (await catalog.GetAsync(first.Token)).Context!.Window.Geometry);
        var lost = new TaskCompletionSource<TargetToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.TargetLost += token => { if (token == first.Token) lost.TrySetResult(token); };
        await target.RequestAsync("recreate");
        Assert.Equal(first.Token, await lost.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null((await catalog.GetAsync(first.Token)).Context);
    }
    [Fact]
    public async Task InputSurfacePickerExposesParentAndChildWithoutRetargetingOldToken()
    {
        using var target=await TargetFixture.StartAsync();using var catalog=new WindowsWindowCatalog();
        var parent=(await catalog.ListAsync()).Single(w=>w.Title==target.PipeName+" A");
        var surfaces=await catalog.ListInputSurfacesAsync(parent.Token);
        Assert.Equal(2,surfaces.Count);
        Assert.Contains(surfaces,s=>s.Window.Token==parent.Token);
        Assert.Equal(2,surfaces.Select(s=>s.Window.Token).Distinct().Count());
        Assert.All(surfaces,s=>Assert.NotNull(s.Window.Geometry));
    }
    internal static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!await condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Controlled target did not reach expected state."); await Task.Delay(20); }
    }
}
internal sealed class TargetFixture : IDisposable
{
    private readonly Process process;
    public string PipeName { get; }
    public string ExecutablePath { get; }
    private TargetFixture(Process process, string pipe, string executable) { this.process = process; PipeName = pipe; ExecutablePath = executable; }
    public static async Task<TargetFixture> StartAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"Macrofy.sln"))) root = root.Parent;
        var configuration = AppContext.BaseDirectory.Contains("Release",StringComparison.Ordinal) ? "Release" : "Debug";
        var executable = Path.Combine(root!.FullName,"tools","Macrofy.TestTarget","bin",configuration,"net10.0-windows","Macrofy.TestTarget.exe");
        var pipe = "MacrofyTest-" + Guid.NewGuid().ToString("N");
        var process = Process.Start(new ProcessStartInfo(executable, "--pipe " + pipe) { UseShellExecute = false, CreateNoWindow = true })!;
        var result = new TargetFixture(process,pipe,executable);
        try { await result.RequestAsync("snapshot"); return result; } catch { result.Dispose(); throw; }
    }
    public async Task<JsonElement> RequestAsync(string command)
    {
        using var client = new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client,leaveOpen:true) { AutoFlush = true };
        using var reader = new StreamReader(client,leaveOpen:true);
        await writer.WriteLineAsync(command);
        var response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var document = JsonDocument.Parse(response!);
        return document.RootElement.Clone();
    }
    public void Dispose() { if (!process.HasExited) process.Kill(true); process.Dispose(); }
}
