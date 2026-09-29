# Native UI preview — 2026-09-29

User asked to bring the approved browser design into the EXE before continuing game tests. The executable identifies itself as **UI preview**. Recording is disabled, and every Run/Pause/Resume/Stop control operates an input-free preview session. Actual CookieRun compatibility remains unconfirmed.

## Delivered

- Seven native desktop tabs, icon controls with tooltips/accessible names, shared profile context and Stop/status.
- Profile and macro create/rename/duplicate/delete flows; multiple saved app rules per profile and independent app bindings per macro.
- Macro workspace, validated action inspector, reorder/add/remove actions, coordinate setting, repeat and interval.
- Ten color palettes and independent Light/Dark, with local persistence.
- Versioned UI workspace with atomic replacement/backup; corrupt/newer files and edits from another instance are protected. Invalid/unapplied action drafts remain in memory through tab/theme changes, and cannot run or replace valid stored actions.
- Simulated concurrent sessions on shared or different targets; pause/stop one or all across profiles. Session state is not persisted. Focused-window F10 stops previews.

## Verification

- Native-shell assertion first failed because the main window was missing. UI state/storage assertions initially failed because their types were missing.
- Headless tests cover tab/profile/target selection, valid/invalid action editing, active editor locks, cross-tab Stop, settings preservation, narrow layout and rendered views. Isolated store tests cover reopen, corruption/newer schema, and stale-instance conflicts.
- Independent code review found stale-instance overwrite, final-Wait timing, and draft-loss issues. Regression tests reproduced all three, then passed after fixes. Stale control state and display-name-based target validation were also corrected.
- Captures under `artifacts/native-ui-captures/` are rendered by Avalonia's headless Skia host; they are not screenshots of the user's desktop.
- Self-contained single-file Windows publish uses dedicated locked RID graphs and includes the separate click probe.
- Final Release verification: locked restore succeeded, build had zero warnings/errors, and all 64 tests passed (2 Core, 40 Windows, 22 App). Locked portable publish succeeded; `Macrofy.exe` is 101,906,052 bytes.

## Remaining

Real recording, native macro replay, global hotkeys, production scheduler/storage integration, and actual game response are not delivered by this UI slice. Preview data is separate from browser storage. Clean-machine Windows checks, native screen-reader/DPI/manual interaction checks and compatibility confirmation remain open.
