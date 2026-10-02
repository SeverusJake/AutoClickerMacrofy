# Optional compatibility — design

Date: 2026-10-02

## Problem

Window-mode macros cannot run until the user confirms each action type and window state on the Compatibility tab. Users watch the first run anyway, so the gate adds setup work without new information. The tab's vocabulary (input surface, BackgroundVisible, Observed working/ignored) reads as a developer tool. Its one everyday feature, capturing a client point, is only reachable there.

## Decisions

1. **No Run gate.** `WorkspacePlaybackController` and `WindowsPlaybackExecutor` stop requiring compatibility evidence. Saved evidence stays in the workspace file and is ignored by playback. Unsupported action types still fail before input (`UnsupportedCapability`).
2. **Default input surface.** Without an explicit surface selection, playback binds the single matching window's catalog token. That token already targets the window's largest visible client surface. Several matching windows without a selection still fail (`TargetAmbiguous`). An explicit selection from the Compatibility tab still wins for the session.
3. **Advanced tools setting.** `WorkspaceDocument.ShowAdvancedTools` (bool, default false, persisted; old files load as false). Settings shows a checkbox `Show advanced tools (Compatibility tab)`. Off hides the Compatibility tab; if it is the selected tab, the app shows Settings. The checkbox is disabled while a compatibility test is busy or pending; turning it off discards the pending test context.
4. **Compatibility tab copy.** States that results are notes and Run does not need them. Behavior otherwise unchanged.
5. **Capture point in the macro editor.** For a selected Click step, a `Capture point in 5 s` button under Position / value:
   - Screen macro: reads the physical desktop pointer (`WindowsScreenInputPlayer.ReadPointer`).
   - Window macro: resolves the macro's saved app rule; one match → reads the pointer in that window's client coordinates (`ReadPointer(token)`, which requires a restored window and a pointer inside the client).
   - Writes `X, Y` into the step draft; the user applies it. No input is sent.
   - Disabled in Percentage mode (tooltip explains), while the macro runs, while capturing, or while a compatibility test is locked. Hidden for non-Click steps or without native services.
   - Errors: missing app (`Saved target app is missing.`), no window (`No open window matches {name}. Open it first.`), several windows (`Several windows match {name}. Narrow its title rule on the Apps tab.`), pointer errors pass through.
   - `MainWindow.CaptureCountdownSeconds` (public, default 5) lets tests use 0.
6. **Soft note.** Window macros show `Background clicks don't work in every game. Watch the first run.` in place of `Capability confirmation required`. About text drops the confirmation requirement.

## Out of scope

Showing saved results on the Compatibility tab; persisting surface selection; percentage capture; changing background state rules (Background still requires the target to be visible, not minimized and not foreground).

## Testing

- Executor: unselected surface binds the parent token; no evidence needed for window actions; unsupported actions still fail; existing geometry/identity checks unchanged.
- Controller: window macros start without evidence; invalid state or missing app still blocks.
- UI: tab hidden by default (6 tabs), Settings toggle shows/hides it and persists; capture fills window and screen points; capture errors; Percentage disables capture; soft note text.
- Existing compatibility UI tests enable advanced tools first. Evidence-gate tests are removed or rewritten.
- Full suite via `scripts/verify-probe.ps1`.
