using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.App.Views;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform.Models;
namespace Macrofy.App.Tests;

internal static class HarmlessUi
{
    public static MainWindow Create(WorkspaceState state, WorkspaceStore? store = null) => new(state, new UiPlayback(state.Document), new HarmlessHotkeys(), store);
}
internal sealed class UiPlayback(WorkspaceDocument document) : IWorkspacePlaybackController
{
    private readonly Dictionary<Guid, PlaybackSnapshot> sessions = [];
    public IReadOnlyDictionary<Guid, PlaybackSnapshot> Sessions => sessions;
    public bool HotkeysReady => true;
    public PlatformError? HotkeyError => null;
    public event EventHandler? Changed;
    public bool CanStart(Profile profile, Macro macro, out string reason)
    {
        reason = "";
        if (sessions.GetValueOrDefault(macro.Id)?.IsActive == true || macro.Steps.Count == 0 || (macro.AppId is { } id && !profile.Apps.Any(a => a.Id == id))) { reason = "Busy or missing target"; return false; }
        var state = new WorkspaceState(document);
        foreach (var step in macro.Steps) if (!state.ValidateStep(macro, step, out reason)) return false;
        return true;
    }
    public Task<DeliveryResult> StartAsync(Profile profile, Macro macro, bool selectedAction = false, int selectedIndex = -1)
    {
        if (!CanStart(profile, macro, out var reason)) return Task.FromResult(new DeliveryResult(false, new("CannotStart", reason)));
        sessions[macro.Id] = new(macro.Id, macro.Name, profile.Name, PlaybackState.Running, [new CompiledAction.Wait(600000, 0)], 1, 0, 0, TimeSpan.Zero, TimeSpan.Zero, null, null);
        Changed?.Invoke(this, EventArgs.Empty); return Task.FromResult(new DeliveryResult(true));
    }
    public void TogglePause(Guid id) { if (sessions.TryGetValue(id, out var s)) sessions[id] = s with { State = s.State == PlaybackState.Paused ? PlaybackState.Running : PlaybackState.Paused }; Changed?.Invoke(this, EventArgs.Empty); }
    public void TogglePauseAll() { foreach (var id in sessions.Where(p => p.Value.IsActive).Select(p => p.Key).ToArray()) TogglePause(id); }
    public void Stop(Guid id) { if (sessions.TryGetValue(id, out var s)) sessions[id] = s with { State = PlaybackState.Stopped }; Changed?.Invoke(this, EventArgs.Empty); }
    public void StopAll() { foreach (var id in sessions.Keys.ToArray()) Stop(id); }
    public void SelectSurface(Guid appId, TargetToken? surface) { }
    public ValueTask DisposeAsync() { StopAll(); return ValueTask.CompletedTask; }
}
