# Native UI preview — 2026-09-29

User asked to bring the approved browser design into the EXE before continuing game tests. The executable identifies itself as **UI preview**. Recording is disabled, and every Run/Pause/Resume/Stop control operates an input-free preview session. Actual CookieRun compatibility remains unconfirmed.

## Delivered

- Seven native desktop tabs, icon controls with tooltips/accessible names, shared profile context and Stop/status.
- Profile and macro create/rename/duplicate/delete flows; multiple saved app rules per profile and independent app bindings per macro.
- Macro workspace, validated action inspector, reorder/add/remove actions, coordinate setting, repeat and interval.
- Ten color palettes and independent Light/Dark, with local persistence.
- Versioned UI workspace with atomic replacement/backup; corrupt/newer files and edits from another instance are protected. Invalid/unapplied action drafts remain in memory through tab/theme changes, and cannot run or replace valid stored actions.
- Simulated concurrent sessions on shared or different targets; pause/stop one or all across profiles. Session state is not persisted. Focused-window F10 stops previews.
- Profiles has a persistent Enabled switch per macro, with row hover feedback. Run all starts enabled, valid macros in the selected profile; skips unapplied drafts and missing targets; leaves existing running/paused sessions intact. Switching off excludes the macro from future Run all starts; manual Run and existing sessions remain available.
- Run all, Pause/Resume all and Stop all share the footer. Configurable F-key defaults are F9, F8 and F10 respectively; shortcuts work while Macrofy is focused and persist with workspace settings. They trigger UI preview actions; global system-wide hotkeys remain pending.
- Compatibility contains an in-app Screen test: capture the pointer without clicking, then explicitly request one real left-click after a 3 second countdown. The footer Stop control/configured stop key cancels the countdown. A queued click is not proof the visible app responded; this does not validate CookieRun, background input or minimized input.

## Verification

- Native-shell assertion first failed because the main window was missing. UI state/storage assertions initially failed because their types were missing.
- Headless tests cover tab/profile/target selection, valid/invalid action editing, active editor locks, cross-tab Stop, settings preservation, narrow layout and rendered views. Isolated store tests cover reopen, corruption/newer schema, and stale-instance conflicts.
- Independent code review found stale-instance overwrite, final-Wait timing, and draft-loss issues. Regression tests reproduced all three, then passed after fixes. Stale control state and display-name-based target validation were also corrected.
- Captures under `artifacts/native-ui-captures/` are rendered by Avalonia's headless Skia host; they are not screenshots of the user's desktop.
- Self-contained single-file Windows publish uses dedicated locked RID graphs and includes the separate click probe.
- Final Release verification: locked restore succeeded, build had zero warnings/errors, and all 68 tests passed (2 Core, 40 Windows, 26 App). New headless tests exercise actual switch hover/click, all-disabled availability, persistence, profile isolation, same-target batch starts, pending drafts, invalid targets/steps, and paused-session preservation. Existing documents without Enabled flags default to enabled. Locked portable publish succeeded.
- Review caught Run all hiding storage errors; a failing save-conflict regression reproduced it, and batch feedback now preserves unresolved load/save diagnostics. One full verification run failed `ActualTargetReceivesOrderedGesturesWithoutCursorOrFocusChange(minimized: True)` on a one-pixel live cursor difference (119 vs 118); an unchanged full-suite rerun passed all 68. This native integration assertion samples the live desktop and is sensitive to concurrent physical input.

## Remaining

Real recording, native macro replay, global hotkeys, production scheduler/storage integration, and actual game response are not delivered by this UI slice. Preview data is separate from browser storage. Clean-machine Windows checks, native screen-reader/DPI/manual interaction checks and compatibility confirmation remain open.
