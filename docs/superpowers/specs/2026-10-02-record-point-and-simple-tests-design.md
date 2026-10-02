# Record-point hotkey and simple compatibility tests — design

Date: 2026-10-02

## Problem

Capturing a click point needs a 5-second hover countdown. The Compatibility tab still asks the user to mark Observed working/ignored and saves evidence that playback no longer uses (since `2026-10-02-optional-compatibility-design.md`).

## Decisions

### Record point hotkey
- `ShortcutSettings.Capture` (default `F7`), shown in Settings as **Record point**, F1–F11, distinct from Run/Pause/Stop. Loading a workspace whose Capture key is missing, invalid, or duplicates another control assigns F7, or the first free key F1–F11 when F7 is taken. Run/Pause/Stop validation is unchanged.
- `HotkeyCommand.Capture`; `HotkeySet` gains optional `HotkeyBinding? Capture = null`. `WindowsGlobalHotkeys.Configure` registers it when present, with the same validation and transactional rollback.
- The key is registered only while recording: starting a recording calls `Configure` with Capture; finishing or cancelling calls it without.
- Recording starts from **Record point (F7)** (macro editor Click step, Compatibility tab). Status: `Hover over the spot and press F7. Press the button again to cancel.` The button reads `Cancel recording` while active.
- On the Capture hotkey, Macrofy reads the pointer: macro editor window macro → resolve the saved app rule, read client pixels of the single match; Screen macro → physical desktop pixels; Compatibility tab → client pixels of the selected surface. Success writes `X, Y` (macro draft or Compatibility value). Errors are shown and end the recording.
- Recording ends on success, error, cancel button, Stop hotkey / Stop all, suspend, or window close. Recording can start only when no input is active and no test is sending; registration failure is shown.
- The 5-second countdown and `CaptureCountdownSeconds` are removed.

### Compatibility tab
- Fields: Saved app, Live window, Input surface, Action (default **Click**; Key, Shortcut, Text, Wheel), value, Record point, test button.
- The test button label is `{Action} test` (e.g. **Click test**). It sends exactly one action; status reports `Sent one {action}. Watch the app.` or the delivery/cleanup error. Nothing is saved.
- The window state picker is removed; the state is read from the window (`Minimized` if minimized, else `BackgroundVisible`).
- Removed: Observed working, Observed ignored, Discard test, `CompatibilityEvidence`, `CompatibilityAttempt`, `WorkspaceDocument.CompatibilityEvidence`, confirmation/persistence/read delegates, `WorkspaceRuntime.ReadAsync/PersistAsync`, evidence store validation. Old files with a `CompatibilityEvidence` field load; the field is ignored and dropped on next save.
- `CompatibilityService.SendTestAsync(appId, token, action, ct)` holds the input lease only while sending.

## Out of scope

The separate `tools/Macrofy.CompatibilityProbe` keeps its own confirmation workflow. Recording several points at once.

## Testing

- Platform: Capture binding registers, routes `HotkeyCommand.Capture`, and is unregistered when omitted.
- Store: missing/duplicate Capture key repaired; old evidence field ignored.
- Service: one send, lease released, state from window, invalid app/point/busy/cancel rejected.
- UI: Settings shows/changes Record point key; macro editor records window and screen points via the hotkey and reports errors; cancel unregisters; Compatibility tab sends `Click test` / `Key test` and shows no observation controls.
- Full suite via `scripts/verify-probe.ps1`.
