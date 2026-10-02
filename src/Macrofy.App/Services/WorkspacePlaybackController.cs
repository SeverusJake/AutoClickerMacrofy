using Macrofy.App.Models;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
namespace Macrofy.App.Services;

public interface IWorkspacePlaybackController : IAsyncDisposable
{
    Task<DeliveryResult> StartAsync(Profile profile, Macro macro, bool selectedAction = false, int selectedIndex = -1);
    bool CanStart(Profile profile, Macro macro, out string reason, bool selectedAction = false, int selectedIndex = -1);
    void TogglePause(Guid id);
    void TogglePauseAll();
    void Stop(Guid id);
    void StopAll();
    void SelectSurface(Guid savedAppId, TargetToken? surface);
    IReadOnlyDictionary<Guid, PlaybackSnapshot> Sessions { get; }
    bool HotkeysReady { get; }
    PlatformError? HotkeyError { get; }
    event EventHandler? Changed;
}

public sealed class WorkspacePlaybackController : IWorkspacePlaybackController
{
    private readonly WorkspaceDocument document;
    private readonly PlaybackCoordinator coordinator;
    private readonly InputActivityGuard activity;
    private readonly IHotkeyHealth health;
    private readonly ISystemEvents? systemEvents;
    private readonly object sync = new();
    private readonly Dictionary<Guid, Task> lifetimes = [];
    private readonly Dictionary<Guid, (string Executable, string Title, TargetToken Token)> selectedSurfaces = [];
    private bool disposed;
    public WorkspacePlaybackController(WorkspaceDocument document, PlaybackCoordinator coordinator,
        InputActivityGuard activity, IGlobalHotkeys hotkeys, IHotkeyHealth health)
    {
        this.document = document; this.coordinator = coordinator; this.activity = activity; this.health = health;
        systemEvents = hotkeys as ISystemEvents;
        coordinator.Changed += OnChanged;
        health.HealthChanged += OnHealthChanged;
        if (systemEvents is not null) systemEvents.Suspended += StopAll;
    }
    public IReadOnlyDictionary<Guid, PlaybackSnapshot> Sessions => coordinator.Snapshots;
    public bool HotkeysReady => health.IsOperational;
    public PlatformError? HotkeyError => health.OperationalError;
    public event EventHandler? Changed;
    public bool CanStart(Profile profile, Macro macro, out string reason, bool selectedAction = false, int selectedIndex = -1)
    {
        lock (sync)
        {
            if (disposed) { reason = "Playback is shutting down."; return false; }
            if (!HotkeysReady) { reason = HotkeyError?.Message ?? "Global emergency Stop is unavailable."; return false; }
            if (lifetimes.ContainsKey(macro.Id) || Sessions.GetValueOrDefault(macro.Id)?.IsActive == true)
            { reason = "Stop this macro before starting it again."; return false; }
            return TryBuild(profile, macro, selectedAction, selectedIndex, out _, out reason);
        }
    }
    public Task<DeliveryResult> StartAsync(Profile profile, Macro macro, bool selectedAction = false, int selectedIndex = -1)
    {
        lock (sync)
        {
            if (disposed || !HotkeysReady) return Task.FromResult(Failure(HotkeyError?.Message ?? "Global emergency Stop is unavailable."));
            if (lifetimes.ContainsKey(macro.Id)) return Task.FromResult(Failure("This macro is already active or finishing cleanup."));
            if (!TryBuild(profile, macro, selectedAction, selectedIndex, out var request, out var reason)) return Task.FromResult(Failure(reason));
            if (!activity.TryEnterPlayback(macro.Id, out var lease)) return Task.FromResult(Failure("Input is busy with playback or a compatibility test."));
            try
            {
                var started = coordinator.StartAsync(request!);
                var completion = coordinator.WaitForCompletionAsync(macro.Id);
                // Install the owner before releasing the lock; completion may already have finished.
                var lifetime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lifetimes.Add(macro.Id, lifetime.Task);
                _ = ReleaseWhenComplete(macro.Id, completion, lease!, lifetime);
                return started;
            }
            catch { lease!.Dispose(); throw; }
        }
    }
    private async Task ReleaseWhenComplete(Guid id, Task completion, IDisposable lease, TaskCompletionSource released)
    {
        try { await completion.ConfigureAwait(false); }
        finally
        {
            lock (sync) { lease.Dispose(); lifetimes.Remove(id); }
            released.TrySetResult(); OnChanged(this, EventArgs.Empty);
        }
    }
    private bool TryBuild(Profile profile, Macro macro, bool selectedAction, int selectedIndex, out PlaybackRequest? request, out string reason)
    {
        request = null; reason = "";
        if (!document.Profiles.Contains(profile) || !profile.Macros.Contains(macro)) { reason = "Macro does not belong to this profile."; return false; }
        if (macro.Repeat < 0 || macro.IntervalMs is < 0 or > 600000) { reason = "Invalid repeat or interval."; return false; }
        if (macro.Coordinates is not ("Fixed pixels" or "Percentage")) { reason = "Unknown coordinate mode."; return false; }
        if (macro.Steps.Count == 0) { reason = "Add an action first."; return false; }
        if (selectedAction && (selectedIndex < 0 || selectedIndex >= macro.Steps.Count)) { reason = "Select an action."; return false; }
        var actions = new List<CompiledAction>();
        var reserved = new HashSet<string>([document.Shortcuts.Run, document.Shortcuts.Pause, document.Shortcuts.Stop], StringComparer.OrdinalIgnoreCase);
        foreach (var step in selectedAction ? new[] { macro.Steps[selectedIndex] } : macro.Steps.ToArray())
        {
            if (!ActionCompiler.TryCompile(new(step.Kind, step.Value, step.DelayMs), macro.Coordinates == "Percentage" ? CoordinateMode.Percentage : CoordinateMode.FixedPixels, reserved, out var action, out reason)) return false;
            actions.Add(action!);
        }
        PlaybackTarget target;
        if (macro.AppId is { } appId)
        {
            var app = profile.Apps.SingleOrDefault(a => a.Id == appId);
            if (app is null) { reason = "Saved target app is missing."; return false; }
            if (macro.WindowState is not ("Minimized" or "Background")) { reason = "Unknown target state."; return false; }
            target = new(appId, new(new(app.Name, app.Executable), app.TitleRule), macro.WindowState == "Minimized" ? TargetState.Minimized : TargetState.BackgroundVisible, null);
            if (selectedSurfaces.TryGetValue(appId, out var selected))
            {
                if (selected.Executable == app.Executable && selected.Title == app.TitleRule) target = target with { SelectedSurface = selected.Token };
                else selectedSurfaces.Remove(appId);
            }
        }
        else target = new(null, null, null, null);
        request = new(macro.Id, macro.Name, profile.Name, target, actions, selectedAction ? 1 : macro.Repeat,
            selectedAction ? 0 : macro.IntervalMs, macro.AppId is null ? 3000 : 0);
        return true;
    }
    public void TogglePause(Guid id) => coordinator.TogglePause(id);
    public void TogglePauseAll() => coordinator.TogglePauseAll();
    public void Stop(Guid id) { lock (sync) coordinator.Stop(id); }
    public void StopAll() { lock (sync) coordinator.StopAll(); }
    public void SelectSurface(Guid savedAppId, TargetToken? surface)
    {
        lock (sync)
        {
            var app = document.Profiles.SelectMany(p => p.Apps).SingleOrDefault(a => a.Id == savedAppId);
            if (surface is { } token && app is not null) selectedSurfaces[savedAppId] = (app.Executable, app.TitleRule, token);
            else selectedSurfaces.Remove(savedAppId);
        }
    }
    private void OnHealthChanged() { if (!HotkeysReady) StopAll(); OnChanged(this, EventArgs.Empty); }
    private void OnChanged(object? sender, EventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (sync) { disposed = true; coordinator.StopAll(); pending = lifetimes.Values.ToArray(); }
        await coordinator.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(pending).ConfigureAwait(false);
        coordinator.Changed -= OnChanged; health.HealthChanged -= OnHealthChanged;
        if (systemEvents is not null) systemEvents.Suspended -= StopAll;
    }
    private static DeliveryResult Failure(string message) => new(false, new("CannotStart", message));
}
