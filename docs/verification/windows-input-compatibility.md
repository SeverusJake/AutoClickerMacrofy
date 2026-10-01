# Windows input compatibility — 2026-09-30

Macrofy sends real Screen and window macro actions. Controlled Windows receiver tests verify delivery; actual CookieRun and other game response remains unconfirmed. A queued Windows message or inserted input does not prove the app acted on it.

## Automated evidence

Locked Release verification on 2026-09-30: **236 passed** (Core 44, App 110, Windows 82), **0 failed, 0 skipped**, and build **0 warnings, 0 errors**. This is dated Task 8 evidence before the final review fixes; it is not a current full-suite pass. Earlier 2026-09-29 probe/UI-preview counts are historical. Full output is in the ignored `.superpowers/sdd/2026-09-30-real-playback/task-8-verify.log` development log.

- Core tests cover action compilation, key chords, timing, repeats, pause/stop, cleanup, immutable runs, and concurrent gesture dispatch. Click and Key gestures stay balanced under cancellation; other sessions are not released by another macro's Stop.
- Windows seam tests cover target identity/destruction, geometry/DPI, signed screen coordinates, virtual-desktop normalization, physical-input ownership, partial insertion, paced window posts, hotkey registration rollback/lifecycle, and cleanup failure reporting.
- Real controlled background/minimized receiver tests received ordered Click, Key, Unicode Text, Wait-separated delivery, and Wheel messages. Concurrent same-target macros kept each key chord and text action contiguous. Native posts left foreground and pointer unchanged at immediate before/after samples. A stalled receiver demonstrates already queued input can arrive after Stop.
- A dedicated, harmless exposed/focused receiver received one finite Screen Click/Key/Text/Wait/Wheel sequence. Exact Unicode/emoji and wheel receipts were verified, then cursor and foreground restored. The test checks receiver identity, focus, and exposure before each injection. It never targets a game. The minimized Wheel coordinator test reported `InvalidInput` with no sends when the current pointer was outside the client; low-level message encoding with a supplied valid point is a separate result.
- Headless app tests exercise explicit selection, per-state/per-capability evidence, no automatic observed confirmation, cross-profile controls, and input exclusivity. Native hotkey tests use fakes; physical out-of-app keys and OS suspend remain manual.

The controlled receiver records raw messages. Its Key and Shortcut receipts do not establish that another application's keyboard-state-based commands work. Screen Unicode receipt uses the fixture's limited `VK_PACKET` translation. These tests do not create confirmed game evidence.

The final review fixes add fake-clock regressions for original-token loss while paused, waiting, and preparing, unrelated-target survival, cleanup ownership, and subscription disposal. Headless regressions cover selected-action eligibility, action-specific help, and asynchronous workspace-owner reads while unrelated profile/app collections change. Production compatibility reads run on the UI dispatcher with cancellation; candidate/save/publish remains one dispatcher transaction. These focused checks supplement the dated Task 8 count above; fresh whole-solution verification is reported separately.

## Latest run evidence — 2026-10-01

The fresh locked Release build after final fixes at `7aa086b` had **0 warnings and 0 errors**, but its full `scripts/verify-probe.ps1` run failed one native Screen test: Core 51/51 and Windows 82/82 passed; App 116/117 passed. The dedicated Screen receiver was exposed but not focused at the send boundary. The follow-up readiness test at `74fa9c3` waited five seconds for focus and exposure, then reported a blocked environment **before injection** and restored cursor/foreground exactly. See ignored `final-verify.log` and `task-8-readiness-fix.log`.

An explicit run excluding that Screen test passed Core 51/51 and Windows 82/82, while App passed 115/116. The remaining background-window integration case failed at loop 2 Wheel with `InvalidInput`: the live pointer no longer mapped inside the selected client geometry. The log does not establish its cause (`final-available-tests.log`). Thus current physical Screen and full native integration verification remain open; these failures do not establish game behavior.

The latest portable EXE publish and launch-only smoke succeeded (`final-publish.log`, `final-smoke.log`): **102,077,060 bytes**, SHA-256 `3FDD10706E4B9702FD07DBDDD3AD2DA5CD4EA38DC824B72BDBC1B3525FA01C52`, main window created, graceful close, exit code 0. No macro input was started by smoke. The branch remains unmerged while a reliable full suite and manual observations are pending.

## Target rules and deliberate game observation

Screen mode is the default without an assigned app. It uses real [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) and may move the desktop pointer; visible content must be intended. Fixed pixels can be negative across monitors. Percentage 0 maps to the first pixel, 100 to the last pixel of the virtual desktop rectangle, and points in monitor gaps fail validation. Screen cannot reach minimized or covered content. A three-second cancellable countdown precedes Screen Run/Test.

Window mode requires a saved executable/title rule, one matching live input surface, target state, and a successful user-observed test for each needed capability: Click, Key, Shortcut, Text, or Wheel. Ambiguous live matches require explicit surface selection. Background and minimized evidence are separate. Compatibility sends one chosen harmless action; only successful delivery and cleanup can be marked Observed working or Observed ignored. Observations persist with app/title/surface fingerprint, state, capability, geometry and timestamp, without HWND/PID. Current identity/surface/state is rechecked before sends. Fixed-pixel geometry changes invalidate coordinates; percentage coordinates recalculate against current client dimensions. Window Wheel uses the current pointer within the client area, which can make minimized Wheel unavailable. A failed window target never switches to Screen.

Read-only discovery on 2026-09-29 found a title matching `*CookieRun: Crumble - Idle RPG*`, executable `C:\Program Files\Google\Play Games\current\emulator\crosvm.exe`, and client geometry 696 × 1237 at DPI scale 1.25. This identifies a candidate only. No arbitrary game input was sent during automated tests.

| CookieRun target state | Input delivery | User-observed response |
| --- | --- | --- |
| Minimized | Untested | Unconfirmed |
| Background visible | Untested | Unconfirmed |
| Background partly covered | Untested | Unconfirmed |
| Background fully covered | Untested | Unconfirmed |

For a deliberate check, choose a harmless control with obvious response, select the live game window/surface in Compatibility, choose its state and one action, then send that single test. Mark the result only after seeing whether the game reacted. Repeat separately for each capability and state needed by a macro. The standalone probe offers a separate explicit click-only workflow and results file; its records do not grant the native app's playback capability evidence.

## Boundaries and remaining checks

Window delivery uses targeted messages, refreshed geometry/identity checks, and pacing of 100 native posts per second. Windows still has an external check/send race and asynchronous receiver queues. Stop prevents future actions and waits; it cannot retract already posted messages. Owned release uses an independent 500 ms budget and may fail, with separate cleanup error. A synchronous native send can defer a Stop boundary. System suspend and app close request Stop rather than resume input automatically.

Global F9/F8/F10 registration uses Windows [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) with repeat suppression. A conflicting binding is reported; F12 is reserved by Windows for debugger use. Test actual physical shortcuts outside the app and suspend/resume manually. Clean Windows 10/11 launch, alternate monitor/DPI layouts, accessibility interaction, and actual game behavior also remain manual evidence. No functional Mac playback, hidden/tray or locked-screen operation is claimed.
