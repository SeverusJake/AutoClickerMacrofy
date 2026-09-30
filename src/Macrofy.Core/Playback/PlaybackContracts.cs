using Macrofy.Core.Actions;
using Macrofy.Platform.Models;

namespace Macrofy.Core.Playback;

public enum PlaybackState { Starting, Running, Pausing, Paused, Waiting, Completed, Stopped, Error }

public sealed record PlaybackTarget(Guid? SavedAppId, TargetRule? Rule, TargetState? State, TargetToken? SelectedSurface);
public sealed record PlaybackRequest(Guid MacroId, string MacroName, string ProfileName, PlaybackTarget Target,
    IReadOnlyList<CompiledAction> Actions, int Repeat, int IntervalMs, int StartDelayMs);
public sealed record PlaybackBinding(Guid? SavedAppId, TargetToken? Token, string Name, string? Fingerprint,
    ClientGeometry? InitialGeometry, TargetState? State);
public sealed record PlaybackPreparation(PlaybackBinding? Binding, PlatformError? Error = null);
public sealed record GestureResult(DeliveryResult Delivery, PlatformError? CleanupError = null);

public sealed record PlaybackSnapshot(Guid MacroId, string MacroName, string ProfileName, PlaybackState State,
    IReadOnlyList<CompiledAction> Actions, int CurrentStep, long CompletedSteps, long CompletedLoops,
    TimeSpan ActiveElapsed, TimeSpan RemainingWait, PlatformError? DeliveryError, PlatformError? CleanupError)
{
    public int TotalSteps => Actions.Count;
    public bool IsActive => State is PlaybackState.Starting or PlaybackState.Running or PlaybackState.Pausing or PlaybackState.Paused or PlaybackState.Waiting;
}

/// <summary>Every gesture must finish independent, bounded native cleanup before returning.</summary>
public interface IPlaybackExecutor
{
    ValueTask<PlaybackPreparation> PrepareAsync(PlaybackRequest request, CancellationToken cancellationToken);
    ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken cancellationToken);
    ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, CompiledAction action, CancellationToken cancellationToken);
}
