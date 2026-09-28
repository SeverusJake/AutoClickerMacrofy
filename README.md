# Macrofy

Windows-first macro recorder/editor project. Current authorized phase is foundation, window discovery and the CookieRun compatibility probe. The full recorder, scheduler and Profiles UI are not implemented yet.

## Scope

Full macros are planned for future games. CookieRun currently requires clicks only. Minimized playback is preferred; non-minimized background clicks are the fallback. If neither works in the actual game, pause and review options before building the full application.

## Build and verify

Requires .NET SDK 10.0.302; `global.json` allows later patches in that feature band. The App headless test project uses xUnit v3 because Avalonia 12.1.3 requires it; Core/Windows tests use v2. Packages are pinned with lock files.

```powershell
powershell -File scripts/verify-probe.ps1
powershell -File scripts/publish-probe.ps1
dotnet run --project tools/Macrofy.CompatibilityProbe -- --list
dotnet run --project tools/Macrofy.CompatibilityProbe -- --interactive
```

Published self-contained probe: `artifacts/compatibility-probe/win-x64/Macrofy.CompatibilityProbe.exe --interactive`. This is a compatibility tool, not the final Macrofy release. Clean-machine Windows 10/11 verification remains pending.

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

Only targeted window messages are used. Geometry is refreshed per pointer command, input is paced to 100 native posts/second, and concurrent sends are rejected. Stop cannot retract already-posted Windows messages. Target-only cleanup has a 500ms budget and may fail; its outcome is reported.

Window/process and child-surface identities are checked before sends; destruction invalidates session tokens. Windows still has an unavoidable external check/send race and asynchronous destruction notifications. Controlled test success does not prove any game's compatibility.

See `docs/verification/windows-input-compatibility.md` for evidence and pending checks; see `docs/superpowers/plans/2026-09-28-macrofy-implementation.md` for the revised plan.
