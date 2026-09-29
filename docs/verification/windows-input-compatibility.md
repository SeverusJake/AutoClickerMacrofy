# Windows input verification — phase 1

## Status

Backend and click probe implemented. Actual CookieRun input is unconfirmed. Full application work is gated on the user's observed click result.

## Automated evidence (2026-09-29)

Release verification including Screen mode: locked restore, zero-warning build, 48 tests passed (Core 2, Windows 40, App headless 6), no skips. Self-contained single-file probe published; read-only --list launched successfully on the current Windows desktop. The UI concept page was checked in the browser: all three layouts, mode changes, sample macro editing, guided steps, and no horizontal overflow at 320px and 1024px.

- Core contracts: opaque GUID tokens and delivery/observation separation.
- Windows native-seam tests: ambiguous/missing resolution; explicit selection; process restart; same-process and child destruction; DPI/resize/minimized geometry; permission denial; pointer capture bounds.
- Real external two-window fixture: discovery preserves foreground; real minimized geometry cache; real integrity check; WinEvent destruction invalidates original token; main/child surfaces can be selected explicitly.
- Input encoder/player: signed coordinates, held-button/modifier masks, wheel screen conversion, key repeat/scan/extended/Alt bits, UTF-16 surrogate pairs, pre-cancellation, invalid geometry, access-denied partial send, 100-posts/second pacing, target-only cleanup and no cleanup to recreated targets.
- Real receiving fixture: ordered mouse down/move/up, shortcut key sequence, Unicode/emoji, vertical/horizontal wheel, both non-minimized background and minimized. Cursor and foreground remain unchanged immediately around each native post; ordinary physical mouse use between posts is allowed. Current desktop reports DPI scale 1.25.
- Stalled fixture: stopped future input is absent; previously queued down/up arrives after receiver resumes. Cleanup is ordered behind input, not an acknowledgement of target processing.
- Click probe: foreground/wrong state blocked; cancellation releases with an independent token; rejected delivery is not confirmable; changed fingerprint invalidates confirmation; minimized observation can be confirmed after restoring the same target; cleanup errors are retained.
- Headless probe UI: no automatic target/position selection or observed confirmation; unavailable F10 disables testing; changing target/surface clears coordinates; confirmation disables new input and closing waits for saving.
- Cleanup regression checks: target loss between lifetime and context reads returns a failure; unexpected release exceptions are reported without leaving the probe session busy.

The controlled fixture logs raw received messages without TranslateMessage, so its receipt of Ctrl/A events is not proof a real application's keyboard-state-based shortcuts or holds work. Background/minimized and action capabilities require independent game observation.

## Actual game evidence

Screen mode was added at the user's request on 2026-09-29. Automated tests use a native seam and never inject screen input into the user's desktop. They cover negative monitor coordinates, virtual-desktop normalization, ordered move/down/up batching, invalid points/monitor gaps, pre-cancellation, held physical button rejection, partial insertion cleanup, and read-only capture. Headless UI verifies default Screen routing without invoking window playback. Real SendInput response remains a manual user check. [Microsoft SendInput documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) and [MOUSEINPUT coordinate documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput) define the native delivery and coordinate behavior.

Read-only discovery found title matching `*CookieRun: Crumble - Idle RPG*`, executable `C:\Program Files\Google\Play Games\current\emulator\crosvm.exe`, client geometry 696 × 1237 at DPI scale 1.25. This is executable/title evidence only, not input compatibility.

| Target state | Click delivery | User observed result | Gate |
| --- | --- | --- | --- |
| Minimized | Not tested | Unconfirmed | Preferred |
| Background visible | Not tested | Unconfirmed | Fallback |
| Background partly covered | Not tested | Unconfirmed | Fallback |
| Background fully covered | Not tested | Unconfirmed | Fallback |

A user-selected harmless button/position is required before game input. Actual test confirmation is stored by the probe beside its executable. No game-compatible release is claimed.

## Limits and pending checks

- Native lifecycle notifications and check-before-send cannot make HWND identity checking atomic with posting. Original tokens are never intentionally rebound; the residual external race is documented.
- Minimized geometry needs a valid cache from the restored bound surface. Unknown geometry fails; restore and refresh before testing.
- All controlled Windows tests run serially. Non-100% DPI is exercised on this desktop; other monitor arrangements, runtime DPI switches and Windows 10 clean-machine launch remain manual checks.
- Native quota exhaustion and cancellation-before-next-action are exercised through seams; OS messages already in a receiver queue cannot be cancelled.
- F10 registration and physical F10 response need a manual interactive check. Headless tests verify blocked testing when registration is unavailable.
- Clean machine portability, read-only folder behavior and full profile/settings atomic persistence belong to later release/storage tasks.
- Hidden/tray and locked-screen operation are outside initial scope. No functional Mac backend is implemented.
