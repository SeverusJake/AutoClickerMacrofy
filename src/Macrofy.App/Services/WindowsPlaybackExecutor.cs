#if WINDOWS
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows;

namespace Macrofy.App.Services;

public sealed class WindowsPlaybackExecutor(IWindowCatalog catalog, ITargetContext targets,
    Func<TargetToken, CancellationToken, Task<IReadOnlyList<TargetWindow>>> listSurfaces,
    WindowsGestureSender sender) : IPlaybackExecutor
{
    public WindowsPlaybackExecutor(WindowsWindowCatalog catalog, WindowsGestureSender sender)
        : this(catalog, catalog, async (token, ct) => (await catalog.ListInputSurfacesAsync(token, ct)).Select(s => s.Window).ToArray(), sender) { }

    // Preparation only reads metadata. Native input belongs exclusively to the coordinator's gesture gate.
    public async ValueTask<PlaybackPreparation> PrepareAsync(PlaybackRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Target.Rule is null)
        {
            if (request.Target.SavedAppId is not null || request.Target.State is not null || request.Target.SelectedSurface is not null)
                return Failed("InvalidTarget", "An assigned app must have its own saved target rule.");
            var screen = new PlaybackBinding(null, null, "Screen", null, null, null);
            var validation = await sender.ValidateAsync(screen, false, cancellationToken);
            return validation.Queued ? new(screen) : new(null, validation.Error);
        }
        if (request.Target.SavedAppId is not { } appId || request.Target.State is not { } state || !Enum.IsDefined(state))
            return Failed("InvalidTarget", "Choose a saved app and target state.");
        foreach (var action in request.Actions)
            if (CheckCapability(action) is { Queued: false } unsupported) return new(null, unsupported.Error);
        var resolution = await catalog.ResolveAsync(request.Target.Rule, null, cancellationToken);
        IReadOnlyList<TargetWindow> parents = resolution switch
        {
            ResolutionResult.Matched match => [match.Window],
            ResolutionResult.Ambiguous ambiguous => ambiguous.Candidates,
            _ => []
        };
        if (parents.Count == 0) return Failed("TargetMissing", "No live window matches this macro's saved app rule.");
        if (parents.Count > 1 && request.Target.SelectedSurface is null)
            return Failed("TargetAmbiguous", "Choose the intended live window and input surface.");
        // Without an explicit surface, bind the matched window's own token: it targets the largest visible client surface.
        var candidates = new List<TargetWindow>();
        if (request.Target.SelectedSurface is null) candidates.AddRange(parents);
        else
            foreach (var parent in parents)
                candidates.AddRange((await listSurfaces(parent.Token, cancellationToken)).Where(s => s.Token == request.Target.SelectedSurface));
        candidates = candidates.DistinctBy(w => w.Token).ToList();
        if (candidates.Count == 0) return Failed("TargetMissing", "Selected input surface disappeared. Choose a new surface explicitly.");
        var matches = new List<PlaybackBinding>();
        PlatformError? failure = null;
        foreach (var candidate in candidates)
        {
            var current = await targets.GetAsync(candidate.Token, cancellationToken);
            if (current.Context is not { } context) { failure = current.Error; continue; }
            if (context.Window.Token != candidate.Token || !Matches(request.Target.Rule, context.Window))
            { failure = new("TargetChanged", "Target identity changed during preparation."); continue; }
            var binding = new PlaybackBinding(appId, candidate.Token, context.Window.Title, context.SurfaceFingerprint, context.Window.Geometry, state);
            var validation = await sender.ValidateAsync(binding, false, cancellationToken);
            if (!validation.Queued) { failure = validation.Error; continue; }
            matches.Add(binding);
        }
        return matches.Count switch
        {
            1 => new(matches[0]),
            > 1 => Failed("SurfaceAmbiguous", "Several input surfaces match. Choose one explicitly."),
            _ => new(null, failure ?? new("TargetMissing", "No usable input surface matches this macro's saved app rule."))
        };
    }

    public ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken cancellationToken) => sender.ValidateAsync(binding, false, cancellationToken);

    public async ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, CompiledAction action, CancellationToken cancellationToken)
    {
        var check = await sender.CheckAsync(binding, action is CompiledAction.Click { Mode: CoordinateMode.FixedPixels } or CompiledAction.MouseDown { Mode: CoordinateMode.FixedPixels } or CompiledAction.MouseUp { Mode: CoordinateMode.FixedPixels }, cancellationToken);
        if (!check.Result.Queued) return new(check.Result, check.Result.CleanupError);
        if (check.Context is not null && CheckCapability(action) is { Queued: false } unsupported) return new(unsupported);
        return await sender.SendGestureAsync(binding, action, cancellationToken);
    }

    public ValueTask<GestureResult> ReleaseHeldAsync(PlaybackBinding binding, IReadOnlyList<HeldInput> inputs, CancellationToken cancellationToken) =>
        sender.ReleaseHeldAsync(binding, inputs, cancellationToken);

    private static DeliveryResult CheckCapability(CompiledAction action) =>
        action is CompiledAction.Wait or CompiledAction.Click or CompiledAction.Key { Keys.Count: > 0 } or CompiledAction.Text or CompiledAction.Wheel or CompiledAction.MouseDown or CompiledAction.MouseUp or CompiledAction.KeyDown { Keys.Count: > 0 } or CompiledAction.KeyUp { Keys.Count: > 0 }
            ? new(true) : WindowsGestureSender.Fail("UnsupportedCapability", "Window playback does not support this action.");

    private static bool Matches(TargetRule rule, TargetWindow window)
    {
        var appMatches = rule.App.ExecutablePath is { Length: > 0 } executable
            ? string.Equals(executable, window.App.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            : string.Equals(rule.App.Name, window.App.Name, StringComparison.OrdinalIgnoreCase);
        return appMatches && TitleRule.Matches(rule.TitlePattern, window.Title);
    }
    private static PlaybackPreparation Failed(string code, string message) => new(null, new(code, message));
}
#endif
