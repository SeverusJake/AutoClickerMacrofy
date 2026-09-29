using System.Globalization;
using Macrofy.App.Models;

namespace Macrofy.App.Services;

public sealed class WorkspaceState(WorkspaceDocument document)
{
    public WorkspaceDocument Document { get; } = document;
    public Profile Profile => Document.Profiles.FirstOrDefault(p => p.Id == Document.ActiveProfileId) ?? Document.Profiles[0];
    public Macro Macro => Profile.Macros.FirstOrDefault(m => m.Id == Profile.SelectedMacroId) ?? Profile.Macros[0];
    public Dictionary<Guid, PreviewSession> Sessions { get; } = [];
    public bool IsActive(Macro macro) => Sessions.TryGetValue(macro.Id, out var session) && session.State is "Running" or "Paused";
    public string Status(Macro macro) => Sessions.TryGetValue(macro.Id, out var session) ? session.State : "Idle";
    public string TargetName(Macro macro) => macro.AppId is null ? "Screen" : Profile.Apps.FirstOrDefault(a => a.Id == macro.AppId)?.Name ?? "Missing app";
    public bool HasTarget(Macro macro) => macro.AppId is null || Profile.Apps.Any(a => a.Id == macro.AppId);
    public int ActiveCount => Sessions.Values.Count(s => s.State is "Running" or "Paused");
    public string AggregateStatus => ActiveCount == 0 ? "Idle · UI preview" : $"{Sessions.Values.Count(s => s.State == "Running")} running · {Sessions.Values.Count(s => s.State == "Paused")} paused · UI preview";
    public void SelectProfile(Guid id) { if (Document.Profiles.Any(p => p.Id == id)) Document.ActiveProfileId = id; }
    public void SelectMacro(Guid id) { if (Profile.Macros.Any(m => m.Id == id)) Profile.SelectedMacroId = id; }
    public bool StartPreview(Macro macro)
    {
        if (IsActive(macro) || macro.Steps.Count == 0 || macro.Steps.Any(s => !ValidateStep(s, out _)) ||
            (macro.AppId is { } id && !Profile.Apps.Any(a => a.Id == id))) return false;
        Sessions[macro.Id] = new PreviewSession(macro.Name, Profile.Name, TargetName(macro), macro.Steps.ToArray(), macro.Repeat, macro.IntervalMs);
        return true;
    }
    public void TogglePause(Guid id)
    {
        if (Sessions.TryGetValue(id, out var s) && s.State is "Running" or "Paused") s.State = s.State == "Paused" ? "Running" : "Paused";
    }
    public void Stop(Guid id) { if (Sessions.TryGetValue(id, out var s)) s.State = "Stopped"; }
    public void StopAll() { foreach (var s in Sessions.Values) s.State = "Stopped"; }
    public void TogglePauseAll()
    {
        var next = Sessions.Values.Any(s => s.State == "Running") ? "Paused" : "Running";
        foreach (var s in Sessions.Values.Where(s => s.State is "Running" or "Paused")) s.State = next;
    }
    public void Tick(int elapsedMs = 600)
    {
        foreach (var s in Sessions.Values.Where(s => s.State == "Running"))
        {
            s.RemainingMs -= elapsedMs;
            if (s.RemainingMs > 0) continue;
            if (s.FinishPending) { s.State = "Stopped"; continue; }
            var step = s.Steps[s.CompletedSteps % s.Steps.Length];
            s.CompletedSteps++;
            var loopFinished = s.CompletedSteps % s.Steps.Length == 0;
            var final = loopFinished && s.Repeat > 0 && s.CompletedSteps / s.Steps.Length >= s.Repeat;
            s.RemainingMs = step.DelayMs;
            if (final) { s.FinishPending = true; if (s.RemainingMs == 0) s.State = "Stopped"; }
            else
            {
                if (loopFinished) s.RemainingMs += s.IntervalMs;
                var next = s.Steps[s.CompletedSteps % s.Steps.Length];
                if (next.Kind == "Wait") s.RemainingMs += int.Parse(next.Value, CultureInfo.InvariantCulture);
            }
        }
    }
    public bool ApplyStep(Macro macro, int index, MacroStep draft, out string error)
    {
        if (IsActive(macro)) { error = "Stop this macro before editing."; return false; }
        if (!ValidateStep(draft, out error)) return false;
        if (index < 0 || index >= macro.Steps.Count) { error = "Select a step."; return false; }
        macro.Steps[index] = draft; return true;
    }
    public static bool ValidateStep(MacroStep step, out string error)
    {
        error = "";
        if (step.Value is null) { error = "Enter a value."; return false; }
        if (step.DelayMs is < 0 or > 600000) { error = "Wait after must be 0–600000 ms."; return false; }
        switch (step.Kind)
        {
            case "Click":
                var parts = step.Value.Split(',');
                if (parts.Length != 2 || parts.Any(p => !double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))) error = "Enter X, Y (for example 480, 640).";
                break;
            case "Wait":
                if (!int.TryParse(step.Value, out var wait) || wait is < 0 or > 600000) error = "Wait must be 0–600000 ms.";
                break;
            case "Wheel": if (!int.TryParse(step.Value, out _)) error = "Enter a whole number for wheel movement."; break;
            case "Key": case "Text": if (string.IsNullOrWhiteSpace(step.Value)) error = "Enter a value."; break;
            default: error = "Unknown action type."; break;
        }
        return error.Length == 0;
    }
}

// Deliberately has no input-player dependency: this executable first delivers the approved UI.
public sealed class PreviewSession(string name, string profile, string target, MacroStep[] steps, int repeat, int intervalMs)
{
    public string Name { get; } = name;
    public string Profile { get; } = profile;
    public string Target { get; } = target;
    public MacroStep[] Steps { get; } = steps;
    public int Repeat { get; } = repeat;
    public int IntervalMs { get; } = intervalMs;
    public string State { get; set; } = "Running";
    public int CompletedSteps { get; set; }
    public int RemainingMs { get; set; } = steps[0].Kind == "Wait" ? int.Parse(steps[0].Value, CultureInfo.InvariantCulture) : 0;
    public bool FinishPending { get; set; }
}
