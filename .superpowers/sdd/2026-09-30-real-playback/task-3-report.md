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

## Review fix round 1

Addressed both Important findings in task-3-review.md. This supersedes the initial report's bounded-lock assertion: reviewed f554738 actually retained an unbounded monitor across Configure completion waits. The review reproduced a case absent from the original tests.

Configure now validates/enqueues without holding any caller monitor. Native thread serializes transactions, including inline callback reconfiguration. A short state gate covers only commit versus shutdown (no native calls, event callbacks, or completion waits inside it); every gate acquisition uses Monitor.TryEnter with a 2-second bound. External completion waits remain 2 seconds; timeout gate acquisition can add at most 2 seconds. Dispose uses the same bounded state gate then bounded 2-second join (maximum combined bound 4 seconds).

Transactions check disabled/disposed state on entry, after each successful native Register, and under state gate before committing mappings. Shutdown during a stalled native call rolls back that addition when native returns; no later bindings register. Loop checks shutdown before each queued action and before Pump. OnMessage checks shutdown on entry and between subscribers, suppressing both hotkey and suspend events after shutdown. Already executing external/native code cannot be forcibly interrupted; cleanup resumes when it returns, while synchronous caller/disposal waits remain bounded.

Deterministic tests use native Pump/Register gates and explicit caller Thread state/join observations. Fake seam still registers no actual user keys.

RED command: `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~GlobalHotkeyTests`.
Observed: Failed 4, Passed 10, Skipped 0, Total 14, duration 9 seconds.
- ExternalConfigureDoesNotBlockReentrantNativeCallback: Assert.True failed (external configuration timed out).
- ConcurrentConfigureReturnsBoundedlyWhileNativeOwnerIsStalled: Assert.True failed (concurrent caller did not finish within 3 seconds).
- ShutdownDuringRegistrationRollsBackWithoutPumpingOrDispatch(dispose: False): callbacks expected 0, actual 2 after timeout.
- ShutdownDuringRegistrationRollsBackWithoutPumpingOrDispatch(dispose: True): callbacks expected 0, actual 2 after Dispose.

Intermediate rerun while editing remained RED (4 failures); callback suppression worked but extra Pump and monitor failures remained. Final targeted GREEN: Failed 0, Passed 14, Skipped 0, Total 14, duration 6 seconds.

Final suite command: `dotnet test Macrofy.sln -c Release`.
Observed GREEN: Core 32/32 (29 ms), App 31/31 (5 s), Windows 74/74 (8 s), total 137 passed, zero failed/skipped. No build warnings/errors.

Native declarations, storage schema, probe, and unrelated files unchanged. Physical keypress/suspend and app composition remain controller-owned evidence.

## Review fix round 2

Addressed new Important finding from task-3-rereview-1.md. Configuration distinguishes staging from committed state; once the new complete set/mapping commits, cleanup exceptions never roll back its replacement registrations. Completion publication is deferred until obsolete cleanup results are known, and final completion checks shutdown under the existing bounded state gate. Timeout or Dispose still suppresses callbacks and disables service.

Exact result contract: successful full registration with obsolete-cleanup failure returns `HotkeyRegistrationResult(Registered: true, Error: new PlatformError("HotkeyCleanupFailed", diagnostic))`. New full set remains active, including replacement Stop. Diagnostic identifies obsolete function key and numeric native error/message. Obsolete failed registrations remain tracked for retry/disposal but do not route commands through the new map. Reconfiguring the same successful new set retries obsolete cleanup. Caller must display Error even when Registered is true and retain the newly committed configuration (controller explicitly accepted this contract).

Native `HotkeyNative.Unregister` now checks UnregisterHotKey's BOOL; false throws Win32Exception(Marshal.GetLastWin32Error()). Diagnostic formatting preserves NativeErrorCode rather than flattening it to only localized Message. Primary docs read: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-unregisterhotkey (BOOL failure and GetLastError).

Tests:
- ObsoleteUnregisterFailurePreservesReplacementStopAndReportsNativeError injects Win32Exception(5) while removing obsolete F10 after replacing Stop with F6. Verifies Registered=true plus HotkeyCleanupFailed diagnostic containing F10/native code; replacement F6 routes Stop, obsolete F10 does not map; retry succeeds without warning after removing failure; final Dispose empties registrations.
- NativeUnregisterFailurePreservesWin32ErrorWithoutRegisteringKeys creates/destroys a unique hidden native window and calls Unregister on never-registered ID1234. It registers no keys and asserts preserved nonzero Win32 error.

RED command: `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~ObsoleteUnregisterFailure`.
Observed 1 failed: Assert.NotNull failure, Error was null; duration 43 ms.

Additional native RED: restored pre-change ignored-return implementation, then `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~NativeUnregisterFailure`.
Observed 1 failed: Assert.Throws failure, no Win32Exception thrown; duration 17 ms. Reimplemented BOOL/error handling after RED.

GREEN command: `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~GlobalHotkeyTests`.
Observed Failed 0, Passed 16, Skipped 0, Total 16, duration 6 seconds.

Full GREEN: `dotnet test Macrofy.sln -c Release`: Core 32/32 (29 ms), App 31/31 (5 s), Windows 76/76 (8 s); total 139 passed, zero failed/skipped, no build warnings/errors.

No model/storage schema or probe changes. Remaining OS/user/game evidence unchanged.
