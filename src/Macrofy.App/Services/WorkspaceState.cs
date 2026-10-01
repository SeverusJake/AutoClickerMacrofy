using Macrofy.App.Models;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform.Models;
namespace Macrofy.App.Services;

public sealed class WorkspaceState(WorkspaceDocument document)
{
    public WorkspaceDocument Document { get; } = document;
    public IWorkspacePlaybackController? Playback { private get; set; }
    public Profile Profile => Document.Profiles.FirstOrDefault(p => p.Id == Document.ActiveProfileId) ?? Document.Profiles[0];
    public Macro Macro => Profile.Macros.FirstOrDefault(m => m.Id == Profile.SelectedMacroId) ?? Profile.Macros[0];
    public IReadOnlyDictionary<Guid, PlaybackSnapshot> Sessions => Playback?.Sessions ?? new Dictionary<Guid, PlaybackSnapshot>();
    public bool IsActive(Macro macro) => Sessions.GetValueOrDefault(macro.Id)?.IsActive == true;
    public string Status(Macro macro) => Sessions.GetValueOrDefault(macro.Id)?.State.ToString() ?? "Idle";
    public string TargetName(Macro macro) => macro.AppId is null ? "Screen" : Owner(macro)?.Apps.FirstOrDefault(a => a.Id == macro.AppId)?.Name ?? "Missing app";
    public bool HasTarget(Macro macro) => macro.AppId is null || Owner(macro)?.Apps.Any(a => a.Id == macro.AppId) == true;
    public Profile? Owner(Macro macro) => Document.Profiles.FirstOrDefault(p => p.Macros.Contains(macro));
    public int ActiveCount => Sessions.Values.Count(s => s.IsActive);
    public string AggregateStatus => ActiveCount == 0 ? "Idle" : $"{ActiveCount} active · {Sessions.Values.Count(s => s.State == PlaybackState.Paused)} paused";
    public void SelectProfile(Guid id) { if (Document.Profiles.Any(p => p.Id == id)) Document.ActiveProfileId = id; }
    public void SelectMacro(Guid id) { if (Profile.Macros.Any(m => m.Id == id)) Profile.SelectedMacroId = id; }
    public void TogglePause(Guid id) => Playback?.TogglePause(id);
    public void Stop(Guid id) => Playback?.Stop(id);
    public void StopAll() => Playback?.StopAll();
    public void TogglePauseAll() => Playback?.TogglePauseAll();
    public bool ApplyStep(Macro macro, int index, MacroStep draft, out string error)
    {
        if (IsActive(macro)) { error = "Stop this macro before editing."; return false; }
        if (!ValidateStep(macro, draft, out error)) return false;
        if (index < 0 || index >= macro.Steps.Count) { error = "Select a step."; return false; }
        macro.Steps[index] = draft; return true;
    }
    public bool ValidateStep(Macro macro, MacroStep step, out string error) =>
        ActionCompiler.TryCompile(new(step.Kind, step.Value, step.DelayMs), macro.Coordinates == "Percentage" ? CoordinateMode.Percentage : CoordinateMode.FixedPixels,
            new HashSet<string>([Document.Shortcuts.Run, Document.Shortcuts.Pause, Document.Shortcuts.Stop], StringComparer.OrdinalIgnoreCase), out _, out error);
    public static bool ValidateStep(MacroStep step, out string error) => ActionCompiler.TryValidateEditor(new(step.Kind, step.Value, step.DelayMs), out error);
}
