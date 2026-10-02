using Macrofy.App.Models;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
namespace Macrofy.App.Services;

public sealed record CompatibilityTestResult(GestureResult Result, TargetState State, InputCapability Capability);

/// <summary>Sends one deliberate test action to a selected live surface. Nothing is recorded.</summary>
public sealed class CompatibilityService(WorkspaceDocument document, ITargetContext targets, Func<IDisposable?> acquireTestLease,
    Func<PlaybackBinding, CompiledAction, CancellationToken, ValueTask<GestureResult>> sendGesture)
{
    public async Task<CompatibilityTestResult> SendTestAsync(Guid appId, TargetToken token, CompiledAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = acquireTestLease() ?? throw new InvalidOperationException("Input is busy.");
        var context = (await targets.GetAsync(token, cancellationToken)).Context;
        if (context is null || context.Window.Token != token || !ValidContext(appId, context)) throw new InvalidOperationException("Selected target identity, surface or geometry is invalid.");
        var capability = action switch
        {
            CompiledAction.Click => InputCapability.Click,
            CompiledAction.Key key => key.Keys.Count > 1 ? InputCapability.Shortcut : InputCapability.Key,
            CompiledAction.Text => InputCapability.Text,
            CompiledAction.Wheel => InputCapability.Wheel,
            _ => throw new InvalidOperationException("Select an input action to test.")
        };
        if (action is CompiledAction.Click click && (!double.IsFinite(click.Point.X) || !double.IsFinite(click.Point.Y) || click.Point.X < 0 || click.Point.Y < 0 ||
            (click.Mode == CoordinateMode.FixedPixels ? click.Point.X >= context.Window.Geometry!.Width || click.Point.Y >= context.Window.Geometry.Height : click.Point.X > 100 || click.Point.Y > 100)))
            throw new InvalidOperationException("Test point is outside the window's client area.");
        var state = context.Window.IsMinimized ? TargetState.Minimized : TargetState.BackgroundVisible;
        var binding = new PlaybackBinding(appId, token, context.Window.Title, context.SurfaceFingerprint, context.Window.Geometry, state);
        var result = await sendGesture(binding, action, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(result, state, capability);
    }

    private bool ValidContext(Guid appId, TargetContext context)
    {
        var app = document.Profiles.SelectMany(p => p.Apps).SingleOrDefault(a => a.Id == appId);
        return app is not null && context.Window.Token.Id != Guid.Empty && !string.IsNullOrWhiteSpace(context.SurfaceFingerprint) &&
            context.Window.Geometry is { Width: > 0, Height: > 0, DpiScale: > 0 } geometry && double.IsFinite(geometry.DpiScale) &&
            TitleRule.MatchesWindow(app.Executable, app.TitleRule, context.Window);
    }
}
