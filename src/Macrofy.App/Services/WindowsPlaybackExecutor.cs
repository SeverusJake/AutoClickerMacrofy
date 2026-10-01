#if WINDOWS
using System.Text.RegularExpressions;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows;

namespace Macrofy.App.Services;

public sealed class WindowsPlaybackExecutor(IWindowCatalog catalog, ITargetContext targets,
    Func<TargetToken, CancellationToken, Task<IReadOnlyList<TargetWindow>>> listSurfaces,
    WindowsGestureSender sender, Func<Guid, TargetContext, TargetState, InputCapability, bool, CancellationToken, Task<bool>> isConfirmed) : IPlaybackExecutor
{
    public WindowsPlaybackExecutor(WindowsWindowCatalog catalog, WindowsGestureSender sender, CompatibilityService compatibility)
        : this(catalog, catalog, async (token, ct) => (await catalog.ListInputSurfacesAsync(token, ct)).Select(s => s.Window).ToArray(), sender, compatibility.IsConfirmedAsync) { }

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
        var candidates = new List<TargetWindow>();
        foreach (var parent in parents)
        {
            var surfaces = await listSurfaces(parent.Token, cancellationToken);
            candidates.AddRange(surfaces.Where(s => request.Target.SelectedSurface is null || s.Token == request.Target.SelectedSurface));
        }
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
            var requirementsMet = true;
            foreach (var action in request.Actions)
            {
                var evidence = await CheckEvidenceAsync(binding, context, action, cancellationToken);
                if (!evidence.Queued) { failure = evidence.Error; requirementsMet = false; }
            }
            if (requirementsMet) matches.Add(binding);
        }
        return matches.Count switch
        {
            1 => new(matches[0]),
            > 1 => Failed("SurfaceAmbiguous", "Several input surfaces match. Choose one explicitly."),
            _ => new(null, failure ?? new("CompatibilityRequired", "Confirm compatibility for this surface and every required action."))
        };
    }

    public ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken cancellationToken) => sender.ValidateAsync(binding, false, cancellationToken);

    public async ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, CompiledAction action, CancellationToken cancellationToken)
    {
        var check = await sender.CheckAsync(binding, action is CompiledAction.Click { Mode: CoordinateMode.FixedPixels }, cancellationToken);
        if (!check.Result.Queued) return new(check.Result, check.Result.CleanupError);
        if (check.Context is { } context)
        {
            var evidence = await CheckEvidenceAsync(binding, context, action, cancellationToken);
            if (!evidence.Queued) return new(evidence, evidence.CleanupError);
        }
        return await sender.SendGestureAsync(binding, action, cancellationToken);
    }

    private async Task<DeliveryResult> CheckEvidenceAsync(PlaybackBinding binding, TargetContext context, CompiledAction action, CancellationToken cancellationToken)
    {
        if (action is CompiledAction.Wait) return new(true);
        InputCapability? capability = action switch
        {
            CompiledAction.Click => InputCapability.Click,
            CompiledAction.Key { Keys.Count: > 1 } => InputCapability.Shortcut,
            CompiledAction.Key { Keys.Count: 1 } => InputCapability.Key,
            CompiledAction.Text => InputCapability.Text,
            CompiledAction.Wheel => InputCapability.Wheel,
            _ => null
        };
        if (capability is null) return WindowsGestureSender.Fail("UnsupportedCapability", "Window playback does not support this action.");
        return binding.SavedAppId is { } id && binding.State is { } state &&
            await isConfirmed(id, context, state, capability.Value, action is CompiledAction.Click { Mode: CoordinateMode.FixedPixels }, cancellationToken)
            ? new(true) : WindowsGestureSender.Fail("CompatibilityRequired", $"Confirm {capability} compatibility for the selected surface and target state.");
    }

    private static bool Matches(TargetRule rule, TargetWindow window)
    {
        var appMatches = rule.App.ExecutablePath is { Length: > 0 } executable
            ? string.Equals(executable, window.App.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            : string.Equals(rule.App.Name, window.App.Name, StringComparison.OrdinalIgnoreCase);
        return appMatches && Regex.IsMatch(window.Title, "\\A" + Regex.Escape(rule.TitlePattern).Replace("\\*", ".*").Replace("\\?", ".") + "\\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
    }
    private static PlaybackPreparation Failed(string code, string message) => new(null, new(code, message));
}
#endif
