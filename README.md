# Macrofy

Macrofy is a Windows desktop macro editor and player. Saved macros send real Click, Key, Text, Wait, and Wheel actions through the selected Screen or window target. Recording remains unavailable. Controlled Windows delivery is verified; response from CookieRun or any other game is unconfirmed.

## Run the Windows app

Double-click `artifacts/win-x64/Macrofy.exe` after publishing. Opening the app does not start input. The self-contained `win-x64` build needs no separate .NET installation. Clean Windows 10/11 launch remains a manual check. A companion compatibility probe is copied to `artifacts/win-x64/CompatibilityProbe/` when its separately published EXE exists.

The six tabs are Profiles, Apps, Macros, Settings, Log, and About. Settings → **Show advanced tools** adds the optional Compatibility tab, which sends one Click, Key, Shortcut, Text, or Wheel test to a chosen window surface so you can watch the response; nothing is saved. Profiles holds multiple saved app rules and macros. Each macro can use one assigned app or **Screen (default)**. Add app lists open windows: pick one to fill the app name, executable path, and title rule, choose a rule suggestion (without last title part, exact title, or program name), and check the live open-window match count before saving. Manual entry still works. Run, Pause/Resume, and Stop work per macro. Run all starts enabled, valid macros in the current profile. Global Pause/Resume all and Stop all cover active macros across profiles. Multiple macros can run concurrently, including on one target; each macro keeps its step order, and complete input gestures are serialized.

Default global shortcuts are **F9 Run all**, **F8 Pause/Resume all**, and **F10 Stop all**, including while another app has focus. Settings accepts distinct F1–F11 bindings; Windows reserves F12 for debugger use. A registration conflict is shown in the app. Playback and compatibility input require an operational global Stop shortcut. Shortcut changes are available while input is idle. System suspend and app shutdown stop sessions; they do not resume automatically.

## Edit and play a macro

Select a macro on the Macros tab. Add or edit actions, then apply valid edits:

| Action | Value | Behavior |
| --- | --- | --- |
| Click | `X, Y` | Left click at fixed desktop pixels or window client pixels; Percentage mode uses `0–100, 0–100`. |
| Key | `Space`, `Enter`, `Ctrl + K` | One supported key or modifier chord. Control shortcut keys are reserved. |
| Text | Text up to 4096 UTF-16 units | Sends Unicode text. |
| Wait | Integer `0–600000` ms | Delays without native input. |
| Wheel | Nonzero signed 16-bit integer, such as `120` or `-120` | Vertical scroll at current pointer; window mode requires pointer inside current client area. |

Each step has **Wait after** (`0–600000` ms). Repeat can be Once, 100 times, or Until stopped; each Profiles row also has a **Loop** toggle (on = until stopped, off = once) and an **Interval (s)** field. **Interval between runs** (`0–600000` ms) starts after the last action and its Wait after finish; the next run starts after that interval. The first run starts immediately apart from the Screen countdown. Waits use a monotonic clock; pause preserves remaining wait time. The UI shows current/total step, completed loops, active elapsed time, remaining wait/countdown, and delivery or cleanup errors. Sent/queued input is not an observed game response.

**Test selected action** validates and runs only that saved action once, even if another action has an unsupported value. Full Run still requires every action. Apply or discard drafts first; target, global Stop, and input-ownership checks apply to both controls.

Percentage `0` maps to the first pixel and `100` to the last pixel. Screen percentages span the virtual desktop rectangle, then validate that the point belongs to a connected monitor; gaps are invalid. Fixed Screen coordinates can be negative on monitors left or above the primary screen. Screen Click moves the physical pointer and needs visible intended content. Screen Run and Test selected action start after a cancellable three-second countdown. Screen mode cannot reach minimized or covered content.

Window playback targets a saved executable/title rule and the one matching live window; several matches fail until the rule is narrowed. It uses the window's main input surface unless the optional Compatibility tab selected another for the session. It never brings the target forward, minimizes it, or falls back to Screen on failure. No compatibility confirmation is required: background clicks don't work in every game, so watch the first run. For a Click step, press **Record point**, then left-click the spot: that one click is blocked from reaching the app, and Macrofy reads its position (window client pixels for window macros, desktop pixels for Screen macros). Clicks on Macrofy itself pass through, so **Cancel recording** still works. The Compatibility tab labels input surfaces as `Main · W×H (default)` or `Child · W×H · id` and preselects the main one. Changed executable/title/surface/state or geometry can invalidate playback or require coordinates to be checked again. Window Wheel uses the current pointer in the target client area; this can be unavailable for minimized windows.

Stop cancels future actions and waits promptly. Native input already sent cannot be withdrawn. Cleanup releases input owned by the current gesture using an independent 500 ms budget; errors are reported separately. Native synchronous delivery can delay a Stop boundary. Recording and independent hold/drag editing are unavailable.

Closing the original window stops its sessions even while paused or waiting, with a target-loss diagnostic. Other targets keep running; input ownership remains held until cleanup finishes. Wheel help in the editor and Compatibility explains the current-pointer requirement and possible minimized `OutsideClient` rejection. Key/Shortcut help explains that received messages alone do not establish working keyboard-state-based shortcuts; each needs an observed response.

## Data and build

Profiles, saved app rules, macro assignments/actions, appearance, shortcuts, and confirmed compatibility evidence live beside the EXE in `MacrofyData/ui-workspace.json`. Live HWNDs/PIDs and active sessions are not saved. Older structurally valid workspaces remain visible even if a saved action now fails validation; correct the action before running. Corrupt or newer workspace files are preserved with saving blocked, and edits from another app instance are detected. The browser UI concepts in `docs/ui/macrofy-ui-options.html` use separate browser storage.

Build requires .NET SDK 10.0.302; `global.json` allows later patches in that feature band. NuGet packages are pinned with lock files. App headless tests use xUnit v3 for Avalonia 12.1.3; Core and Windows tests use xUnit v2.

```powershell
powershell -NoProfile -File scripts/verify-probe.ps1
powershell -NoProfile -File scripts/publish-ui.ps1
```

`verify-probe.ps1` runs test projects one at a time (`-m:1`) because native tests share the real cursor. Native tests move the mouse and type into dedicated test windows, so the script shows a popup before tests start (OK starts now, Cancel aborts, auto-start after 10 seconds) and another with the result; do not touch the mouse or keyboard in between. Pass `-NoPopup` for unattended runs.

The 2026-10-03 locked Release verification passed **255 tests** (Core 51, App 118, Windows 86), with zero failures, skips, build warnings, or build errors. Controlled receiver tests covered real background/minimized window messages and a bounded Screen sequence on a dedicated harmless surface. See [native playback verification](docs/verification/native-ui-preview.md) and [Windows input compatibility](docs/verification/windows-input-compatibility.md). Physical global-key behavior outside Macrofy, suspend behavior, clean Windows 10/11 launch, and actual game response remain manual checks.

The standalone compatibility probe can also be built and run with `powershell -NoProfile -File scripts/publish-probe.ps1` and `dotnet run --project tools/Macrofy.CompatibilityProbe -- --interactive`. Its separate results file is `MacrofyData/compatibility-probe-results.json` beside the probe EXE. Probe observations do not replace the native app's per-capability confirmation workflow.
