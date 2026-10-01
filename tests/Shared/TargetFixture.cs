using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
namespace Macrofy.IntegrationTests;
internal sealed class TargetFixture : IDisposable
{
    private readonly Process process; private readonly Semaphore desktopLease;
    private int disposed;
    public string PipeName { get; }
    public string ExecutablePath { get; }
    private TargetFixture(Process process, string pipe, string executable, Semaphore lease) { desktopLease = lease; this.process = process; PipeName = pipe; ExecutablePath = executable; }
    public static async Task<TargetFixture> StartAsync(CancellationToken cancellationToken = default)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"Macrofy.sln"))) root = root.Parent;
        var configuration = AppContext.BaseDirectory.Contains("Release",StringComparison.Ordinal) ? "Release" : "Debug";
        var executable = Path.Combine(root!.FullName,"tools","Macrofy.TestTarget","bin",configuration,"net10.0-windows","Macrofy.TestTarget.exe");
        var pipe = "MacrofyTest-" + Guid.NewGuid().ToString("N");
        var lease = new Semaphore(1, 1, @"Local\Macrofy.ControlledNativeIntegration");
        var acquired = false;
        TargetFixture? result = null;
        try
        {
            var signalled = await Task.Run(() => WaitHandle.WaitAny([lease, cancellationToken.WaitHandle], TimeSpan.FromSeconds(30)), CancellationToken.None);
            acquired = signalled == 0;
            cancellationToken.ThrowIfCancellationRequested();
            if (!acquired) throw new TimeoutException("Native integration lock timed out.");
            var process = Process.Start(new ProcessStartInfo(executable, "--pipe " + pipe) { UseShellExecute = false, CreateNoWindow = true })!;
            result = new TargetFixture(process,pipe,executable,lease);
            await result.RequestAsync("snapshot"); return result;
        }
        catch
        {
            if (result is not null) result.Dispose();
            else { if (acquired) lease.Release(); lease.Dispose(); }
            throw;
        }
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
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
        finally { process.Dispose(); try { desktopLease.Release(); } finally { desktopLease.Dispose(); } }
    }
}
