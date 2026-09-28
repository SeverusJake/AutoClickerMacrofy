using Macrofy.Platform;
using Macrofy.Platform.Models;

namespace Macrofy.CompatibilityProbe;

public sealed record ProbeAttempt(TargetToken Token, TargetWindow? Window, TargetState State, PointerPoint Point,
    string? Fingerprint, int QueuedCommands, PlatformError? Error, PlatformError? CleanupError)
{
    public bool CanConfirm => Window is not null && QueuedCommands == 2 && Error is null && CleanupError is null;
}
public sealed record ProbeConfirmation(AppIdentity App, string Title, string Fingerprint, TargetState State,
    PointerPoint Point, DateTimeOffset TestedAt, bool ObservedSuccess);

public sealed class ClickProbeSession(ITargetContext context, IInputPlayer player)
{
    private int active;
    public async Task<ProbeAttempt> TestAsync(TargetToken token, PointerPoint point, TargetState state, CancellationToken cancellationToken = default)
    {
        var attempt = new ProbeAttempt(token, null, state, point, null, 0, null, null);
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) return attempt with { Error = new("Busy", "Another compatibility test is active.") };
        bool startedInput = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await context.GetAsync(token, cancellationToken);
            if (current.Context is not { } target) return attempt with { Error = current.Error ?? new("TargetLost", "Target unavailable.") };
            attempt = attempt with { Window = target.Window, Fingerprint = target.SurfaceFingerprint };
            if (target.IsForeground) return attempt with { Error = new("Foreground", "Put the game behind the probe or minimize it before testing.") };
            if (target.Window.IsMinimized != (state == TargetState.Minimized)) return attempt with { Error = new("WrongState", "Set the target to the selected state yourself, then test.") };
            startedInput = true;
            var down = await player.SendAsync(token, new PointerCommand(PointerKind.Down, point, MouseButton.Left), cancellationToken);
            if (!down.Queued) attempt = attempt with { Error = down.Error ?? new("DeliveryFailed", "Mouse down was rejected.") };
            else
            {
                attempt = attempt with { QueuedCommands = 1 };
                await Task.Delay(80, cancellationToken);
                var afterDelay = await context.GetAsync(token, cancellationToken);
                if (afterDelay.Context is not { } live || live.SurfaceFingerprint != target.SurfaceFingerprint || live.Window.IsMinimized != target.Window.IsMinimized || live.Window.Geometry != target.Window.Geometry || live.IsForeground)
                    attempt = attempt with { Error = new("TargetChanged", "Target state, geometry or input surface changed during the test.") };
                else
                {
                    var up = await player.SendAsync(token, new PointerCommand(PointerKind.Up, point, MouseButton.Left), cancellationToken);
                    attempt = up.Queued ? attempt with { QueuedCommands = 2 } : attempt with { Error = up.Error ?? new("DeliveryFailed", "Mouse up was rejected.") };
                }
            }
        }
        catch (OperationCanceledException) { attempt = attempt with { Error = new("Cancelled", "Test cancelled. Already-posted input cannot be withdrawn.") }; }
        finally
        {
            if (startedInput)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                try
                {
                    var result = await player.ReleaseHeldAsync(token, cleanup.Token);
                    if (!result.Queued) attempt = attempt with { CleanupError = result.Error ?? new("CleanupFailed", "Target-held release could not be posted.") };
                }
                catch (OperationCanceledException) { attempt = attempt with { CleanupError = new("CleanupTimeout", "Target-held release exceeded its cleanup budget.") }; }
            }
            Volatile.Write(ref active, 0);
        }
        return attempt;
    }

    public async Task<ProbeConfirmation> ConfirmAsync(ProbeAttempt attempt, bool observedSuccess)
    {
        if (!attempt.CanConfirm) throw new InvalidOperationException("Only a fully queued and cleaned-up test can be confirmed.");
        var current = (await context.GetAsync(attempt.Token)).Context;
        // Restoring a minimized game to observe the result does not change the state in which input was tested.
        if (current is null || current.SurfaceFingerprint != attempt.Fingerprint || current.Window.Geometry != attempt.Window!.Geometry)
            throw new InvalidOperationException("Target changed after the test. Run a new test before confirming.");
        return new(attempt.Window!.App, attempt.Window.Title, attempt.Fingerprint!, attempt.State, attempt.Point, DateTimeOffset.UtcNow, observedSuccess);
    }
}
