# Macrofy

Windows-first macro recorder/editor project. Foundation, window discovery and the CookieRun compatibility probe are implemented. The approved design is now also available as a native UI preview executable. Real recording, macro scheduling and game-compatible playback remain pending.

## Open the native UI

Double-click `artifacts/win-x64/Macrofy.exe`. It uses the approved desktop tabs, profile macro overview, macro workspace, icons and multicolor Light/Dark themes. Profiles, saved app rules, macro assignments, valid action edits and appearance choices are remembered beside the executable in `MacrofyData/ui-workspace.json`.

**This build is a UI preview.** Run/Pause/Resume/Stop simulate independent macro sequences without desktop input. Recording is disabled. The Compatibility tab opens the separate click probe for deliberate real input tests. Closing Macrofy stops preview sessions; active/paused sessions are not restored after restart. F10 stops previews while this window has focus; global hotkeys remain pending.

Rebuild the self-contained Windows executable with `powershell -NoProfile -File scripts/publish-ui.ps1`. No .NET installation is required to run the published EXE. Clean-machine verification remains pending.

## Scope

Full macros are planned for future games. CookieRun currently requires clicks only. Minimized window playback is preferred; non-minimized background clicks are the fallback. If neither works in the actual game, pause and review options before building the full application. Screen mode is separately available as the default when no window is chosen.

## Build and verify

Requires .NET SDK 10.0.302; `global.json` allows later patches in that feature band. The App headless test project uses xUnit v3 because Avalonia 12.1.3 requires it; Core/Windows tests use v2. Packages are pinned with lock files.

```powershell
powershell -File scripts/verify-probe.ps1
powershell -File scripts/publish-probe.ps1
dotnet run --project tools/Macrofy.CompatibilityProbe -- --list
dotnet run --project tools/Macrofy.CompatibilityProbe -- --interactive
```

Published self-contained probe: `artifacts/compatibility-probe/win-x64/Macrofy.CompatibilityProbe.exe --interactive`. This is a compatibility tool, not the final Macrofy release. Clean-machine Windows 10/11 verification remains pending.

## Screen mode (default)

Double-click the published executable. Leave **Screen (default — visible desktop)** selected, capture a harmless screen position, then click **Test one click**. A three-second countdown lets you uncover the intended app. Screen mode moves the real pointer and sends one left click through Windows SendInput. Coordinates use desktop pixels across connected monitors, including negative coordinates. Screen mode cannot reach minimized or covered content.

Selecting a window switches to client coordinates and targeted messages; choose the position again. An unavailable selected window stops the test and never switches to screen input. Screen results are not saved as game background/minimized compatibility evidence.

## UI concepts

Open `docs/ui/macrofy-ui-options.html` in a browser to try the desktop tab layout requested in the user's DS4Windows reference: Profiles, Apps, Macros, Compatibility, Settings, Log and About. Profiles shows its macros with individual Run, Pause/Resume, Stop and Edit controls. Multiple preview macros can run together, including on the same app. Shared Pause all/Resume all, Stop all and aggregate status remain visible across tabs and include other profiles. Macros uses Design 2's workspace. Earlier compact/guided concepts remain in the source. The native EXE now follows this design; real concurrent macro playback is still planned.

Updated profile design: each profile has many saved app rules and many macros. Each macro remembers one assigned app from that profile; no assignment means Screen. The browser preview's **Manage apps** control persists sample app identities/title rules and per-macro assignments in browser storage. The native UI uses its own versioned workspace file; it does not import browser storage. Live window handles/process IDs are never saved.

The Profiles count column is **Steps**: sequence length for one run, independent of repeats. Five new cyberpunk theme candidates appear above the preview: Neon District (yellow/cyan), Synthwave (pink/purple), Matrix (green/black), Tron (cyan/orange) and Redline (red/amber). Click one to preview it throughout the app; **Use this theme** remembers the confirmed choice in this browser. The earlier Windows Blue, Graphite, Emerald, Violet and Warm Amber themes remain available through Settings. The native palette is pending selection.

Each cyberpunk theme now uses seven accent colors across tabs, macro/app labels, Run/Pause/Stop/Edit controls and status text, with related tints in the workspace panels. The palette strips show all seven colors; status labels remain readable without relying on color alone.

Light/Dark mode is separate from the palette and works with all ten candidate themes. The top switch and Settings selector stay synchronized, and mode persists in browser storage. The preview uses concise labels and feedback instead of explanatory paragraphs.

Navigation tabs and common controls now use icons with tooltips and accessible names. Pause changes to Resume while a macro is paused. Profile/macro names, form labels and status remain text.

User approved the current UI design on 2026-09-29: desktop tabs, profile macro controls, Design 2 workspace, multicolor Light/Dark modes and icon controls. Native implementation still follows the actual-game compatibility gate.

## Deliberate CookieRun click test

1. Choose a harmless button with an obvious visible response.
2. Open the probe with `--interactive`. Select CookieRun while restored. The picker shows actual executable identity; nicknames, PIDs and HWNDs are not saved as profile identity.
3. Keep the selected input surface, or choose the main window/another child surface deliberately. Read its client dimensions.
4. Click **Capture pointer position in 5 seconds**. Hover over the chosen button in the restored game until capture completes. This reads the physical cursor; it does not move it or send a click. Coordinates can also be entered manually.
5. Minimize the game yourself. Choose **Minimized**, then **Test one click**. F10 or Stop cancels future posts. The test sends only a left-button down/up pair at your chosen point.
6. Restore the game if necessary to observe the result. Choose **Observed working** or **Observed ignored**. Windows accepting a message is not proof the game reacted.
7. If minimized is ignored, test background visible, partly covered and fully covered separately. Changing surfaces requires choosing the point again.
8. If both minimized and background clicks fail, pause and review options. Do not switch to cursor movement, foreground activation, global input, drivers or injection automatically.

Confirmed observations are bounded and saved in `MacrofyData/compatibility-probe-results.json` beside the probe executable. Storage errors are shown; no fallback data folder is used. These probe observations will feed the later full compatibility workflow.

## Implementation boundaries

Window mode uses targeted messages. Geometry is refreshed per pointer command, input is paced to 100 native posts/second, and concurrent sends are rejected. Stop cannot retract already-posted Windows messages. Target-only cleanup has a 500ms budget and may fail; its outcome is reported. Screen mode uses an explicit ordered move/down/up batch; cancellation before injection prevents the batch, and a partially inserted down triggers one release attempt. Already-inserted screen input cannot be withdrawn.

Window/process and child-surface identities are checked before sends; destruction invalidates session tokens. Windows still has an unavoidable external check/send race and asynchronous destruction notifications. Controlled test success does not prove any game's compatibility.

See `docs/verification/windows-input-compatibility.md` for evidence and pending checks; see `docs/superpowers/plans/2026-09-28-macrofy-implementation.md` for the revised plan.
