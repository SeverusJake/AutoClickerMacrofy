# Task 3 report — global hotkeys and suspend lifecycle

Implemented files:
- src/Macrofy.Platform/Models/HotkeySet.cs
- src/Macrofy.Platform.Windows/WindowsGlobalHotkeys.cs
- src/Macrofy.Platform.Windows/Interop/HotkeyNative.cs
- tests/Macrofy.Platform.Windows.Tests/GlobalHotkeyTests.cs

## Interfaces

Public model: `HotkeyCommand { Run, Pause, Stop }`; `HotkeySet(HotkeyBinding Run, HotkeyBinding Pause, HotkeyBinding Stop)`. HotkeyBinding and HotkeyRegistrationResult signatures unchanged. Existing persisted workspace F1–F12 schema unchanged. Production F12 registration rejected with explicit Windows debugger diagnostic.

Public `WindowsGlobalHotkeys()` implements unchanged `IGlobalHotkeys.Configure(HotkeySet)`, `Triggered`, `ISystemEvents.Suspended`, and IDisposable. Configure returns `HotkeyRegistrationResult(false, PlatformError("HotkeyRegistrationFailed", diagnostic))` for invalid/conflicting/startup/disposed/timeout cases.

Internal injectable `WindowsGlobalHotkeys(IHotkeyNative)`; seam: Initialize(), Register(int id,uint modifiers,uint key,out int error), Unregister(int id), Pump(Action<uint,nuint>), Dispose(). Tests use deterministic fake seam; no user's actual hotkey registrations.

## Behavior

Dedicated background native thread owns unique registered window class and hidden top-level window. Parent zero deliberately avoids message-only windows, so power broadcasts can reach WndProc. Registration and reconfiguration run on owning thread. Default F9/F8/F10 VKs use MOD_NOREPEAT. Complete set validation precedes registration. Existing combinations reused including command swaps; additions staged while old Stop remains registered. Failed addition releases only staged additions, leaving old full set and command map. Success commits command map then removes obsolete registrations. Win32 diagnostics include command, function key, numeric native error and Windows error text. Probe F10 service untouched.

WM_HOTKEY dispatches mapped commands. WM_POWERBROADCAST PBT_APMSUSPEND emits once until PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND resets deduplication. Each event subscriber isolated from other subscriber exceptions. Reentrant Configure from native-thread subscriber runs inline, avoiding self-wait. Registration IDs stay in application range.

Startup wait capped at 2 seconds. Configure lock acquisition capped at 2 seconds and command completion at 2 seconds (worst combined synchronous wait 4 seconds). Configuration timeout disables service, rather than allowing a later successful set to silently enable playback. Dispose idempotently requests exit and joins at most 2 seconds; same-thread Dispose avoids joining itself. Finally attempts all unregisters and native teardown even after initialization/message-loop failures.

## TDD evidence

Read test-driven-development SKILL.md and writing-good-tests.md before implementation.

First RED command: `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~GlobalHotkeyTests`.
Observed expected missing-implementation compile failure: CS0246 IHotkeyNative not found. This is the task brief's requested missing-implementation RED, not a runtime assertion failure.

First GREEN same command: Failed 0, Passed 9, Skipped 0, Total 9, duration 344 ms.

Additional runtime RED: `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~HotkeySubscriberCanReconfigure`.
Observed HotkeySubscriberCanReconfigureWithoutDeadlock failed Assert.True, expected True actual False, 2 seconds. Subscriber's synchronous Configure queued work to its own blocked thread. Fixed by executing configure inline on owner thread.

Final targeted GREEN: Failed 0, Passed 10, Skipped 0, Total 10, duration 383 ms.
Windows suite: `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release`: Failed 0, Passed 70, Skipped 0, Total 70, duration 2 seconds.
Full suite: `dotnet test Macrofy.sln -c Release`: Core 32/32 (27 ms), Windows 70/70 (2 s), App 31/31 (5 s). Total 133 passed, zero failed/skipped, no build warnings/errors.

Tests cover default registration flags/routing; duplicates/range/F12 debugger errors and old Stop retention; failed staged Stop conflict and full rollback; swap reuse; complete disposal; suspend deduplication/resume and subscriber exceptions; startup exception/disposed errors; subscriber reentrancy.

## Primary-source validation

Read Microsoft official docs during implementation:
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey — HWND/thread ownership, BOOL/int/UINT signatures, MOD_NOREPEAT 0x4000, F12 debugger reservation, GetLastError, application ID range 0..0xBFFF.
- https://learn.microsoft.com/en-us/windows/win32/power/wm-powerbroadcast — WndProc delivery, message 0x218, suspend 4, resume 7/18, TRUE return.

## Limits / integration requirements

Native callbacks execute on native thread. App must marshal UI-sensitive Run/Pause commands; initiate Stop cancellation promptly. Subscriber work must return promptly. Disposal returns boundedly even if arbitrary subscriber/native seam stalls; OS/native resource cleanup then occurs when that work returns. No live key press or physical OS suspend performed. Fake seam validates registration/lifecycle behavior without occupying actual user keys. Controller owns production composition, UI error gating, suspend-to-StopAll wiring, and final real native/manual evidence.
