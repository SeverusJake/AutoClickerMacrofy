using Macrofy.App.Models;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using System.Text.RegularExpressions;
namespace Macrofy.App.Services;
public sealed class CompatibilityService(WorkspaceDocument document, ITargetContext targets,
    Func<IDisposable?> acquireTestLease,
    Func<PlaybackBinding, CompiledAction, CancellationToken, ValueTask<GestureResult>> sendGesture,
    Func<WorkspaceDocument, CancellationToken, Task> persist)
{
    private readonly SemaphoreSlim confirmationGate = new(1, 1);
    public bool IsConfirmed(Guid appId, TargetContext context, TargetState state, InputCapability capability, bool fixedCoordinates)
    {
        if (!ValidContext(appId, context, state, true)) return false;
        var evidence = document.CompatibilityEvidence.LastOrDefault(e => e.SavedAppId == appId && e.State == state && e.Capability == capability);
        return evidence is { ObservedSuccess: true } && evidence.App == context.Window.App && evidence.Title == context.Window.Title &&
            evidence.SurfaceFingerprint == context.SurfaceFingerprint && (!fixedCoordinates || evidence.Geometry == context.Window.Geometry);
    }
    public async Task<CompatibilityAttempt> BeginAttemptAsync(Guid appId, TargetToken token, TargetState state, CompiledAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lease = acquireTestLease() ?? throw new InvalidOperationException("Input is busy.");
        try
        {
            var context = (await targets.GetAsync(token, cancellationToken)).Context;
            if (context is null || context.Window.Token != token || !ValidContext(appId, context, state, true)) throw new InvalidOperationException("Selected target identity, surface, geometry or state is invalid.");
            var capability = action switch { CompiledAction.Click => InputCapability.Click, CompiledAction.Key k => k.Keys.Count > 1 ? InputCapability.Shortcut : InputCapability.Key, CompiledAction.Text => InputCapability.Text, CompiledAction.Wheel => InputCapability.Wheel, _ => throw new InvalidOperationException("Select an input action to test.") };
            if (action is CompiledAction.Click click && (!double.IsFinite(click.Point.X) || !double.IsFinite(click.Point.Y) || !Enum.IsDefined(click.Mode) || (click.Mode == CoordinateMode.FixedPixels ? click.Point.X < 0 || click.Point.Y < 0 || click.Point.X >= context.Window.Geometry!.Width || click.Point.Y >= context.Window.Geometry.Height : click.Point.X < 0 || click.Point.Y < 0 || click.Point.X > 100 || click.Point.Y > 100))) throw new InvalidOperationException("Test point is outside client geometry.");
            var binding = new PlaybackBinding(appId, token, context.Window.Title, context.SurfaceFingerprint, context.Window.Geometry, state);
            var result = await sendGesture(binding, action, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = new CompatibilityAttempt(appId, context, state, capability, result, Delivered(result) ? lease : null, this);
            if (!Delivered(result)) lease.Dispose();
            return attempt;
        }
        catch { lease.Dispose(); throw; }
    }
    public async Task<CompatibilityEvidence> ConfirmAsync(CompatibilityAttempt attempt, bool observedSuccess, CancellationToken cancellationToken)
    {
        await confirmationGate.WaitAsync(cancellationToken);
        var started = false;
        var completed = false;
        try
        {
            if (attempt.Owner != this || !Delivered(attempt.Result) || !(started = attempt.BeginConfirmation())) throw new InvalidOperationException("Only pending, delivered and cleaned tests can be confirmed.");
            var context = (await targets.GetAsync(attempt.Token, cancellationToken)).Context;
            if (context is null || context.Window.Token != attempt.Token || !ValidContext(attempt.SavedAppId, context, attempt.State, false) || context.Window.App != attempt.Context.Window.App || context.Window.Title != attempt.Context.Window.Title || context.SurfaceFingerprint != attempt.Fingerprint || context.Window.Geometry != attempt.Geometry)
            { attempt.Dispose(); throw new InvalidOperationException("Test target closed or changed before confirmation."); }
            var evidence = new CompatibilityEvidence(attempt.SavedAppId, context.Window.App, context.Window.Title, attempt.Fingerprint, attempt.State, attempt.Capability, attempt.Geometry!, DateTimeOffset.UtcNow, observedSuccess);
            var previous = document.CompatibilityEvidence.ToList();
            document.CompatibilityEvidence.RemoveAll(e => e.SavedAppId == attempt.SavedAppId && e.State == attempt.State && e.Capability == attempt.Capability);
            document.CompatibilityEvidence.Add(evidence);
            try { await persist(document, cancellationToken); }
            catch { document.CompatibilityEvidence = previous; throw; }
            completed = true; return evidence;
        }
        finally { if (started) attempt.EndConfirmation(completed); confirmationGate.Release(); }
    }
    private static bool Delivered(GestureResult result) => result.Delivery.Queued && result.Delivery.Error is null && result.Delivery.CleanupError is null && result.CleanupError is null;
    private bool ValidContext(Guid appId, TargetContext context, TargetState state, bool requireState)
    {
        var app = document.Profiles.SelectMany(p => p.Apps).SingleOrDefault(a => a.Id == appId);
        return app is not null && Enum.IsDefined(state) && context.Window.Token.Id != Guid.Empty && !string.IsNullOrWhiteSpace(context.SurfaceFingerprint) && context.Window.Geometry is { Width: > 0, Height: > 0, DpiScale: > 0 } geometry && double.IsFinite(geometry.DpiScale) &&
            string.Equals(app.Executable, context.Window.App.ExecutablePath, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(context.Window.Title, "^" + Regex.Escape(app.TitleRule).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            (!requireState || (state == TargetState.Minimized ? context.Window.IsMinimized : !context.Window.IsMinimized && !context.IsForeground));
    }
}

