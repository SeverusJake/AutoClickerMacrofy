# Task 5 report — capability-specific observed compatibility

Completed in `C:/Users/Zero/.codex/worktrees/real-playback/AutoClickerMacrofy`, branch `codex/real-playback`. Owned changes only: App evidence/document/store/service and compatibility/workspace tests. Read task-5 brief, approved design, TDD skill and writing-good-tests reference. No views, native input implementation, production guard, or subagents.

## API

`CompatibilityEvidence` persists SavedAppId, AppIdentity, title, surface fingerprint, TargetState, InputCapability, ClientGeometry, DateTimeOffset TestedAt and ObservedSuccess. WorkspaceDocument version remains 1; missing evidence defaults to empty. No live token, HWND, or PID is serialized.

`CompatibilityService(WorkspaceDocument, ITargetContext, Func<IDisposable?> acquireTestLease, Func<PlaybackBinding, CompiledAction, CancellationToken, ValueTask<GestureResult>> sendGesture, Func<WorkspaceDocument, CancellationToken, Task> persist)`.

- `IsConfirmed(Guid, TargetContext, TargetState, InputCapability, bool fixedCoordinates)` checks selected saved executable/title rule, live state/surface/geometry, exact identity/title/fingerprint/state/capability evidence and positive observation. Fixed coordinates require matching geometry; percentage coordinates can use current dimensions.
- `BeginAttemptAsync(Guid, TargetToken, TargetState, CompiledAction, CancellationToken)` acquires exclusive lease before live preflight. It bypasses prior evidence deliberately, binds only selected live token and validated context, validates click client bounds/finite values, maps chord to Shortcut and single key to Key, and rejects Wait tests. Failed delivery/cleanup or cancellation releases ownership and cannot establish evidence.
- `ConfirmAsync(CompatibilityAttempt, bool, CancellationToken)` permits restoration after minimized testing, rejects closed/replaced/changed target identity/title/fingerprint/geometry, and persists explicit observation. New confirmation replaces prior evidence for saved app/state/capability regardless of timestamps, so negative observation supersedes future-dated success. Failed persistence restores prior in-memory collection and propagates original error.
- `CompatibilityAttempt : IDisposable` owns successful test lease through user observation and awaited persistence. Dispose discards pending observation, with release deferred while confirmation/save runs. Successful confirmation releases. Failed save preserves lease for retry unless discarded. Target-change rejection discards. Attempt belongs to originating service; repeated/foreign/discarded confirmation is rejected. UI must dispose attempt on close/context change. Shared Task 6 guard must supply exclusive acquisition across playback and tests.

## Workspace preservation

Load validation now separates structure/settings from executable action semantics. Structurally valid legacy unsupported action values remain available for editor validation; existing shared compiler rejects their playback. Null structure, bad delays/settings/identities, corrupt and newer documents remain protected. Evidence structure is validated. Existing atomic replacement, backup, exclusive file lock and stale-instance hash guard remain intact. Existing invalid-draft tests continue to protect saved action.

## RED → GREEN evidence

1. Wrote compatibility tests before service existed. Command `dotnet test tests/Macrofy.App.Tests -c Release --filter "FullyQualifiedName~CompatibilityServiceTests|FullyQualifiedName~WorkspaceStateTests" --no-restore` failed CS0246 missing CompatibilityService (API discovery RED specified in brief). Corrected xUnit cancellation-token analyzer errors before behavioral verification.
2. LegacyUnsupportedActionSurvivesLoadAndCannotRun failed Assert.Null(LoadError), demonstrating old loader reset on unsupported legacy Key. After structural load validation: passes.
3. PendingObservationAndSaveKeepExclusiveLeaseUntilConfirmationOrDiscard failed Assert.True(occupied) after disposal during save. Added deferred-release confirmation ownership: passes.
4. StructurallyInvalidDocumentsRemainPreserved failed for null evidence collection and null evidence element (2 failed / 1 passed). Added evidence structure validation: all pass.
5. NonFiniteTestPointCannotBeDelivered failed because NaN click produced no exception. Added finite/mode preflight: passes.
6. Additional coverage verifies restored minimized observation, independent capabilities, geometry/title/fingerprint/state/identity matching, closure, partial/failed/unclean delivery, busy/cancelled preflight, cancellation after sender return, persistence rollback, timestamp-independent negative supersession, actual WorkspaceStore evidence roundtrip and serialization without live token.

## Final verification

Targeted command above: **32 passed, 0 failed, 0 skipped**.

`dotnet test Macrofy.sln -c Release --no-restore`: **167 passed** (Core 44, App 47, Windows 76), **0 failed, 0 skipped**, exit 0. No build warnings/errors. Earlier full run before final cancellation coverage: 166 passed.

## Concerns / integration obligations

No game compatibility claim or arbitrary game input. Native sender must honor balanced cleanup contract and revalidate at dispatch; injected callback must reject unavailable hotkey/input prerequisites through Task 6 composition. Coverage levels BackgroundVisible/PartlyCovered/Covered are explicitly user-selected states; platform supplies foreground/minimized facts, not visual occlusion proof. Keep pending attempt disposed on workflow close/selection changes; do not replace saved app bindings silently. Saved executable and title wildcard rule are preflight; captured identity/fingerprint are exact evidence checks. Persistence callback receives full workspace and must retain normal WorkspaceStore save conflict behavior.
