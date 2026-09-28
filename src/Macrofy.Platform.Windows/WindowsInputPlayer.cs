using System.Diagnostics;
using Macrofy.Platform;
using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

internal interface IInputClock
{
    TimeSpan Elapsed { get; }
    Task DelayUntilAsync(TimeSpan deadline, CancellationToken cancellationToken);
}
internal sealed class InputClock : IInputClock
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    public TimeSpan Elapsed => stopwatch.Elapsed;
    public async Task DelayUntilAsync(TimeSpan deadline, CancellationToken cancellationToken)
    {
        while (deadline > Elapsed) await Task.Delay(deadline - Elapsed, cancellationToken);
    }
}

public sealed class WindowsInputPlayer : IInputPlayer, IDisposable
{
    private readonly WindowsWindowCatalog catalog;
    private readonly IInputNative native;
    private readonly IInputClock clock;
    private readonly IPermissionService permission;
    private readonly SemaphoreSlim sender = new(1, 1);
    private readonly Dictionary<TargetToken, HeldInputTracker> held = [];
    private TimeSpan nextPost;
    public WindowsInputPlayer(WindowsWindowCatalog catalog) : this(catalog, new Win32InputNative(), new InputClock(), new WindowsPermissionService(catalog)) { }
    internal WindowsInputPlayer(WindowsWindowCatalog catalog, IInputNative native, IInputClock clock, IPermissionService permission)
    { this.catalog = catalog; this.native = native; this.clock = clock; this.permission = permission; }

    public async ValueTask<DeliveryResult> SendAsync(TargetToken target, InputCommand command, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Failure("Cancelled", "Input was cancelled before posting.");
        // Reject concurrent callers instead of building an application send-ahead queue.
        if (!await sender.WaitAsync(0, cancellationToken)) return Failure("Busy", "Another input operation is active.");
        try { return await SendLockedAsync(target, command, cancellationToken); }
        catch (OperationCanceledException) { return Failure("Cancelled", "Future input cancelled; already-posted Windows messages cannot be withdrawn."); }
        finally { sender.Release(); }
    }

    private async ValueTask<DeliveryResult> SendLockedAsync(TargetToken target, InputCommand command, CancellationToken cancellationToken)
    {
        var context = await catalog.GetAsync(target, cancellationToken);
        if (context.Context is null) return new(false, context.Error);
        var access = await permission.CheckAsync(target, cancellationToken);
        if (!access.Allowed) return new(false, access.Error);
        if (!catalog.Registry.TryResolve(target, out var window)) return Failure("TargetLost", "Original target disappeared.");
        window = window with { Geometry = context.Context.Window.Geometry };
        if (!held.TryGetValue(target, out var state)) held[target] = state = new();
        IReadOnlyList<NativeMessage> messages;
        try { messages = MessageEncoder.Encode(command, window, state, native); }
        catch (ArgumentException e) { return Failure("InvalidInput", e.Message); }
        foreach (var message in messages)
        {
            await clock.DelayUntilAsync(nextPost, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalog.Registry.TryResolve(target, out var live)) return Failure("TargetLost", "Original input surface disappeared before posting.");
            // Pointer geometry is refreshed after pacing too; a resize during the delay must not send a stale point.
            if (command is PointerCommand)
            {
                var fresh = await catalog.GetAsync(target, cancellationToken);
                if (fresh.Context is null || fresh.Context.Window.Geometry != window.Geometry || fresh.Context.Window.IsMinimized != window.IsMinimized)
                    return Failure("GeometryChanged", "Target geometry/state changed while preparing input. Restart after checking coordinates and compatibility.");
            }
            var result = native.Post(live.SurfaceHandle, message);
            nextPost = clock.Elapsed + TimeSpan.FromMilliseconds(10);
            if (!result.Queued) return Failure(result.Error == 5 ? "PermissionDenied" : result.Error == 1816 ? "QueueFull" : "DeliveryFailed", $"Windows rejected input (error {result.Error}).");
        }
        state.Apply(command);
        return new(true);
    }

    public async ValueTask<DeliveryResult> ReleaseHeldAsync(TargetToken target, CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMilliseconds(500));
        bool acquired = false;
        try
        {
            await sender.WaitAsync(budget.Token); acquired = true;
            if (!held.TryGetValue(target, out var state)) return new(true);
            if (!catalog.Registry.TryResolve(target, out _)) { held.Remove(target); return Failure("TargetLost", "Cleanup skipped: original target disappeared."); }
            var context = (await catalog.GetAsync(target, budget.Token)).Context!;
            var point = state.LastPoint;
            if (context.Window.Geometry is { } g) point = new(Math.Clamp(point.X, 0, g.Width - 1), Math.Clamp(point.Y, 0, g.Height - 1));
            foreach (var button in state.Buttons.ToArray())
            {
                var result = await SendLockedAsync(target, new PointerCommand(PointerKind.Up, point, button), budget.Token);
                if (!result.Queued) return result;
            }
            foreach (var key in state.Keys.Values.Reverse().ToArray())
            {
                var result = await SendLockedAsync(target, new KeyCommand(KeyKind.Up, key), budget.Token);
                if (!result.Queued) return result;
            }
            held.Remove(target);
            return new(true);
        }
        catch (OperationCanceledException) { return Failure("CleanupTimeout", "Best-effort input release exceeded its 500ms budget; target may still consider input held."); }
        finally { if (acquired) sender.Release(); }
    }

    private static DeliveryResult Failure(string code, string message) => new(false, new(code, message));
    public void Dispose() => sender.Dispose();
}
