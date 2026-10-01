# Native app and real playback — 2026-09-30

The approved desktop UI now runs production macro playback. Recording is disabled. Actual CookieRun or other game response has not been confirmed.

## Delivered behavior

- Seven native tabs, accessible icon controls, profiles with multiple apps/macros, per-macro target assignment, validated action editor, ten color palettes, and independent Light/Dark mode remain available.
- Run, Test selected action, Pause/Resume, Stop, Run all enabled, Pause/Resume all, and Stop all use the real playback coordinator. Multiple macro sessions can run concurrently, including on one target. Sessions and gestures retain their order; global controls affect active sessions across profiles.
- Click, Key, Text, Wait, and vertical Wheel execute from an immutable compiled snapshot. Each step's Wait after finishes before the interval between runs begins. Monotonic waits exclude paused time; no run overlaps its own previous run.
- Screen starts after a cancellable three-second countdown and may move the real pointer. Window mode stays bound to the original live surface and requires matching user-confirmed action/state capability evidence. No automatic target activation, minimization, or Screen fallback occurs.
- Default F9/F8/F10 shortcuts are global for Run all/Pause all/Stop all. Registration conflicts are shown; unavailable Stop blocks input. Settings accepts unique F1–F11 keys; Windows reserves F12 for debugger use. Native suspend and app shutdown stop input.
- Progress reports state, current/total step, completed loops, active elapsed and remaining wait, with separate delivery and cleanup errors. Delivery is labeled sent/queued, not observed game success.
- Versioned workspace save preserves old unsupported action values for correction, saved profiles/apps/actions/shortcuts/themes, and confirmed compatibility evidence. Corrupt/newer files and stale-instance writes retain their safeguards. Active/paused sessions and HWND/PID are never persisted.

## Automated evidence

The Task 8 locked Release run on 2026-09-30 of `powershell -NoProfile -File scripts/verify-probe.ps1` passed **236/236 tests**: Core 44, App 110, Windows 82; zero failures and skips. Release build had **0 warnings and 0 errors**. This result predates the final review fixes. Exact output is retained in the ignored development report `task-8-verify.log` under `.superpowers/sdd/2026-09-30-real-playback/`.

Headless app tests use injected harmless adapters and exercise validation, controls, compatibility confirmation and persistence, cross-profile activity, progress, and lifecycle. Native hotkey tests use injected registration/message-loop seams, so they do not occupy the user's actual keys. Controlled Windows integration tests send real input to dedicated receiver fixtures only. Background and minimized window tests received ordered click/key/Unicode/wheel messages; a minimized Wheel coordinator test correctly failed when the live pointer was outside its client area. A finite Screen test sent Click, Key, Text, Wait, and Wheel to a deliberately exposed/focused harmless child and verified exact receipts, cursor and foreground restoration. Fixture receipts establish delivery to that fixture, not behavior in other apps.

The earlier 2026-09-29 UI-preview tests and screenshots describe the prior input-free build. They are historical evidence, not the current Run/Test behavior. Browser concept storage remains separate from the native workspace.

## Latest verification and portable build — 2026-10-01

After the final review fixes at `7aa086b`, a fresh locked Release build had **0 warnings and 0 errors**, but `scripts/verify-probe.ps1` did **not** pass: Core 51/51 and Windows 82/82 passed; App passed 116/117. The dedicated Screen receiver was exposed but not focused at the send boundary. The failure and desktop restoration are recorded in the ignored `final-verify.log`.

The follow-up Screen readiness test at `74fa9c3` waited five seconds for both focus and exposure, then failed with **no input injected** when the receiver could not become ready. It restored the cursor and foreground exactly (`task-8-readiness-fix.log`). A separate run explicitly excluding that Screen test passed Core 51/51 and Windows 82/82; App passed 115/116. Its remaining background-window case stopped on loop 2 Wheel because the live pointer no longer mapped inside the selected client geometry (`final-available-tests.log`). The log does not establish why the pointer was there. These runs do not establish a fully passing current native suite or current Screen delivery.

The latest portable publish succeeded. `artifacts/win-x64/Macrofy.exe` is **102,077,060 bytes**, SHA-256 `3FDD10706E4B9702FD07DBDDD3AD2DA5CD4EA38DC824B72BDBC1B3525FA01C52`. A launch-only smoke created a main window, then `CloseMainWindow` exited cleanly with code 0; no macro input was started (`final-publish.log`, `final-smoke.log`). The branch remains unmerged pending a reliable full verification run and the manual checks below.

## Manual checks still needed

- Physical F9/F8/F10 presses while another app has focus, registration conflicts, and suspend/resume behavior on real Windows sessions.
- Launch and data persistence on clean Windows 10 and 11 machines, plus manual accessibility/DPI interaction checks.
- Deliberate user-observed Click/Key/Shortcut/Text/Wheel response for each chosen game, live surface, and target state. Background and minimized observations are independent. CookieRun remains unconfirmed.

See [Windows input compatibility](windows-input-compatibility.md) for target safety and the game evidence table.
