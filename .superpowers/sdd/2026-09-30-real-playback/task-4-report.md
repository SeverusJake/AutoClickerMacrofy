# Task 4 — asynchronous playback sessions and shared gesture gate

## Delivered

- Added neutral playback contracts, `PlaybackCoordinator`, and internal `PlaybackSession` in Core. App remains unchanged.
- `StartAsync` reserves the macro in Starting before asynchronous preparation, freezes the action list, then returns after preparation and session launch. Its cancellation token remains linked for the complete session lifetime. `WaitForIdleAsync` waits for the complete lifetime (including cleanup) of sessions active when called.
- A macro remains reserved until its previous lifetime finishes; duplicate starts and restart during cleanup return `Busy`. Disposed coordinators reject starts with `Disposed`.
- All validation/gesture dispatch uses one shared cancellable semaphore. Target preparation remains independent per session, outside the gesture gate. Wait actions, countdowns, Wait-after, and inter-loop intervals never acquire the gate except for prerequisite revalidation on resume.
- Executor owns balancing native input and independent bounded cleanup before returning. The gate remains held until the executor returns. Stop cancels that session only; StopAll and disposal cancel preparation, countdown, waits, paused sessions, and gate waiters across profiles.
- Monotonic waits use injected `TimeProvider`, timestamp arithmetic, and cancellable timers. Pause freezes the remaining wait immediately. Gesture pause reports Pausing until its safe boundary. Resume revalidates the existing binding; it never resolves a replacement target.
- Wait-after starts after the action returns. Final Wait-after finishes before Completed. Interval starts after the entire preceding loop. Repeat zero remains cancellable and cannot block the Start caller even with synchronous executors and zero delays.
- Immutable snapshots freeze action lists and use the immutable nested compiled-action records. Snapshot reads calculate elapsed/remaining time independently of UI refresh or dispatch. Historical snapshots remain unchanged.
- Delivery and cleanup errors remain separate. Unexpected executor exceptions become a terminal error without exposing potentially sensitive action text. Throwing Changed subscribers cannot stop cleanup or other subscribers.

## Integration API

`PlaybackCoordinator(IPlaybackExecutor, TimeProvider)` exposes:

- `Task<DeliveryResult> StartAsync(PlaybackRequest, CancellationToken = default)`
- `TogglePause(Guid)`, `TogglePauseAll()`, `Stop(Guid)`, `StopAll()`
- `Task WaitForIdleAsync(CancellationToken = default)`
- `IReadOnlyDictionary<Guid, PlaybackSnapshot> Snapshots`
- `event EventHandler? Changed`
- `IAsyncDisposable`

`Changed` runs outside state locks, on the calling/worker thread. UI consumers must marshal updates. TogglePauseAll pauses all if any active session is unpaused; otherwise it resumes all.

Snapshot fields: MacroId, MacroName, ProfileName, State, Actions, CurrentStep, CompletedSteps, CompletedLoops, ActiveElapsed, RemainingWait, DeliveryError, CleanupError. `TotalSteps` and `IsActive` are convenience properties. CurrentStep is 1-based (0 before first action); CompletedSteps is cumulative across loops; TotalSteps is per-loop action count. A completed action counts before its Wait-after finishes.

Target/binding/request/preparation/gesture contracts and IPlaybackExecutor match the task brief. Preparation Error defaults to null. Screen requests require null saved app, rule, target state, and surface. Invalid timings or empty action lists are rejected without launching.

## TDD evidence

1. Wrote scheduler tests and fake TimeProvider first. Initial compilation identified missing playback API.
2. Added compile-only API skeleton with unimplemented operations. Required scheduler command then ran and failed **11/11** with NotImplementedException from StartAsync, confirming executable RED.
3. Implemented scheduler/session/gate. Same command passed **11/11**.
4. Added an adversarial regression for pause during asynchronous dispatch validation. Observed RED: `Assert.Empty` failed because one native gesture was dispatched after the pause. Added the safe-boundary check between validation and dispatch; scheduler tests passed **12/12**.

Test coverage: 49/50 ms wait boundary; Wait plus Wait-after; exact repeat/interval action times; final Wait-after completion; pause remainder and active elapsed; safe gesture pause without cancelling its native token; pause during validation before undelivered input; frozen actions/historical snapshots; infinite zero-delay synchronous executors; duplicate/restart cleanup exclusion; shared dispatch plus cancelled gate waiter and unrelated surviving session; preparation cancellation across profiles; disposal of countdown/paused/repeating sessions; delivery+cleanup error retention and throwing observers.

## Verification output

`dotnet test tests/Macrofy.Core.Tests -c Release --filter FullyQualifiedName~PlaybackCoordinatorTests --no-restore`

Passed: **12**, failed: **0**, skipped: **0**.

`dotnet test -c Release --no-restore`

- Core: **44 passed**
- App: **31 passed**
- Windows platform: **76 passed**
- Solution total: **151 passed**, **0 failed**, **0 skipped**; no build warnings/errors.

`git diff --check`: clean.

## Limits and follow-up responsibilities

- Bounded shutdown depends on the executor honoring cancellation and its independent bounded cleanup contract. Coordinator intentionally never abandons a still-running gesture or releases its gate early. Windows executor/app composition implements the native cleanup deadline.
- Changed notifications indicate transitions/progress; elapsed and remaining values update on snapshot reads. No execution is driven by UI timers.
- Target preparation can run concurrently across sessions. Native validation and gestures are serialized. Preparation must not emit native input.
- Task 4 sends no native input itself and provides no game-compatibility claims. Controlled target and app integration remain later tasks.
