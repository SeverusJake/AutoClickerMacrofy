# Real Macro Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver all six approved improvements in the Windows EXE: real actions, accurate scheduling, global hotkeys, validation, concurrent macros, and execution feedback.

**Architecture:** Compile immutable actions in Core and execute asynchronous sessions through one shared gesture dispatcher. Windows adapters provide screen input, existing targeted messages, hotkeys, and suspend notifications. App services bind saved targets and enforce user-confirmed compatibility; views observe session snapshots rather than drive execution.

**Tech Stack:** .NET SDK 10.0.302, C#, Avalonia 12.1.3, existing xUnit v2/v3 projects, Windows user32 APIs. No new NuGet packages.

**Spec:** `docs/superpowers/specs/2026-09-30-real-playback-design.md` (approved 2026-09-30).

## Global Constraints

- Keep approved tabs, profile controls, action editor, themes, saved workspace, and per-macro target choices.
- Recording, independent hold/drag editing, Mac playback, drivers, injection, and foreground-stealing fallbacks are outside this change.
- Wait/Wait-after/interval: 0–600000 ms; Repeat 0 means until stopped; positive values are finite loop counts.
- Text is capped at 4096 UTF-16 units; Wheel is a nonzero signed 16-bit vertical delta.
- Preserve **Interval between runs**: final Wait-after finishes, then interval, then next loop. No fixed-cadence catch-up.
- Global defaults: F9 Run all enabled in current profile, F8 Pause/resume all, F10 Stop all. Unique F1–F12 settings remain persisted.
- Atomic Click/Key gestures share one dispatch gate. Window-message pacing stays 100 posts/second, burst one.
- Cleanup uses an independent cancellation token and a 500 ms budget. Record cleanup failures independently.
- Screen Run/Test has a cancellable three-second countdown. Window playback never activates/minimizes its target and never falls back to Screen.
- Saved unsupported actions remain visible for correction. Preserve corrupt/newer files, stale-instance detection, and atomic saves.
- Controlled message receipt and user-observed app response are distinct. CookieRun compatibility remains unconfirmed until deliberate observation.
- Headless tests inject services and must never send desktop input or register user's hotkeys.

## Review Focus

- Old saved Key values unsupported by the new compiler must remain visible without replacing the workspace: Task 1 and Task 5 regression tests.
- Stop followed immediately by Run must not overlap old cleanup and new input: Task 4 lifecycle tests.
- A chord cancelled after modifier down must release only its own inserted modifiers: Task 2 partial-insertion and Task 6 dispatch tests.
- Hotkey reconfiguration failure must retain the old emergency Stop binding and clearly block invalid configurations: Task 3 tests.
- A target title/surface/geometry change between preparation and first post must fail without rebinding or Screen fallback: Task 6 tests.

## Files and responsibility map

- `src/Macrofy.Core/Actions/`: typed actions, key parser, compiler.
- `src/Macrofy.Core/Playback/`: request/binding/snapshot contracts, asynchronous session coordinator, monotonic waits and shared dispatcher.
- `src/Macrofy.Platform/IScreenInputPlayer.cs`, `Models/ScreenGeometry.cs`: neutral screen delivery contracts.
- `src/Macrofy.Platform.Windows/WindowsScreenInputPlayer.cs`, `Interop/ScreenInput.cs`: generalized SendInput encoding and held-state cleanup; keep the existing screen clicker API working.
- `src/Macrofy.Platform.Windows/WindowsGlobalHotkeys.cs`, `Interop/HotkeyNative.cs`: hidden native window/message loop, registration, suspend, disposal.
- `src/Macrofy.App/Services/`: compatibility store/workflow, Windows gesture executor, workspace playback facade and composition.
- `src/Macrofy.App/Views/MainWindow.Playback.cs`, `MainWindow.Compatibility.cs`: focused production wiring and compatibility UI; keep common shell/layout in existing partials.
- Existing test projects: Core deterministic timing/compiler tests, Windows seam/integration tests, App headless tests.

### Task 1: Compile and validate editable actions

**Files:** Create `src/Macrofy.Core/Actions/{ActionDefinition,CompiledAction,ActionCompiler,KeyParser}.cs`; create `tests/Macrofy.Core.Tests/ActionCompilerTests.cs`; modify `src/Macrofy.App/Services/WorkspaceState.cs`, `tests/Macrofy.App.Tests/WorkspaceStateTests.cs`.

**Interfaces:** Produce `ActionDefinition(string Kind, string Value, int DelayMs)`, `CoordinateMode { FixedPixels, Percentage }`, `CompiledAction` immutable typed records carrying `DelayMs`, and `ActionCompiler.TryCompile(ActionDefinition source, CoordinateMode coordinates, IReadOnlySet<string> reservedKeys, out CompiledAction? action, out string error) : bool`. Key actions expose ordered canonical `KeyIdentity` values; Click exposes `PointerPoint` and mode; Wait exposes duration; Wheel exposes delta; Text exposes content. `KeyParser.TryParse(string value, out IReadOnlyList<KeyIdentity> keys, out string error) : bool` is the single key vocabulary source.

- [ ] Write `ActionCompilerTests`: valid `Ctrl + Shift + A` produces Control/Shift/A; invalid unknown key, duplicate modifier, `Ctrl +`, all-modifier chord, F10 reserved key, NaN, percentage 101, Wheel 0/32768, unpaired surrogate and Text length 4097 fail with specific nonempty errors. Values 0 and 600000 pass for waits; -1/600001 fail. Percentage 0/100 pass. Unicode `零😀` remains unchanged.
- [ ] Run `dotnet test tests/Macrofy.Core.Tests -c Release --filter FullyQualifiedName~ActionCompilerTests`; require observed compilation failure/missing compiler first.
- [ ] Implement the contracts/compiler, canonical aliases, invariant coordinates, supported current backend vocabulary, and strict chord ordering. Expose coordinate-independent editor validation and apply actual mode limits at macro preflight. Route WorkspaceState.ValidateStep through this shared compiler.
- [ ] Run compiler tests plus `dotnet test tests/Macrofy.App.Tests -c Release --filter FullyQualifiedName~WorkspaceStateTests`; require all pass. Add unsupported existing Key action preservation test in Task 5 when load policy changes.
- [ ] Commit only Task 1 files: `feat: compile and validate macro actions`.

### Task 2: Deliver screen clicks, keys, text, and wheel

**Files:** Create `src/Macrofy.Platform/IScreenInputPlayer.cs`, `Models/ScreenGeometry.cs`; create `src/Macrofy.Platform.Windows/WindowsScreenInputPlayer.cs`; modify `Interop/ScreenInput.cs`, `WindowsScreenClicker.cs`; create `tests/Macrofy.Platform.Windows.Tests/ScreenInputPlayerTests.cs`; retain `ScreenClickerTests.cs`.

**Interfaces:** Produce `ScreenGeometry(int Left, int Top, int Width, int Height)`, `ScreenPointResult(PointerPoint? Point, PlatformError? Error)`, and `IScreenInputPlayer` with `ReadGeometry() : ScreenGeometry`, `ReadPointer() : ScreenPointResult`, `SendAsync(InputCommand command, CancellationToken cancellationToken = default) : ValueTask<DeliveryResult>`, `ReleaseHeldAsync(CancellationToken cancellationToken = default) : ValueTask<DeliveryResult>`. Produce `WindowsScreenInputPlayer : IScreenInputPlayer`; an injectable native seam supplies monitor checks, physical key/button state, SendInput insertion counts, and physical-pixel geometry. Consumes existing InputCommand and DeliveryResult.

- [ ] Write `ScreenInputPlayerTests`: correct x64 INPUT size 40; move/down/up, key-down/up flags, Unicode surrogate units and Wheel encoding; negative monitor positions and 0/100 normalization endpoints; gap/outside points rejected; physically held required key/button rejected; cancellation before send injects nothing; partial down insertion gets one release; failed cleanup returns a separate error; missing pointer fails Wheel.
- [ ] Run `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~ScreenInputPlayerTests`; watch missing implementation fail.
- [ ] Implement full INPUT union, keyboard/mouse flags, inserted-down tracking, validation, release budget, and physical ownership checks. Keep `WindowsScreenClicker`'s existing one-click contract and tests compatible; do not invoke real SendInput from seam tests.
- [ ] Run `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter "FullyQualifiedName~ScreenInputPlayerTests|FullyQualifiedName~ScreenClickerTests"`; require all pass.
- [ ] Commit Task 2: `feat: add screen keyboard text and wheel input`.

### Task 3: Global controls and suspend lifecycle

**Files:** Modify `src/Macrofy.Platform/Models/HotkeySet.cs`; create `src/Macrofy.Platform.Windows/WindowsGlobalHotkeys.cs`, `Interop/HotkeyNative.cs`; create `tests/Macrofy.Platform.Windows.Tests/GlobalHotkeyTests.cs`.

**Interfaces:** Change unused production hotkey model to `HotkeyCommand { Run, Pause, Stop }` and `HotkeySet(HotkeyBinding Run, HotkeyBinding Pause, HotkeyBinding Stop)`. Produce `WindowsGlobalHotkeys : IGlobalHotkeys, ISystemEvents, IDisposable`, preserving Configure and Triggered contracts plus Suspended. An injectable native seam emits hotkey and power messages and exposes registration failures. Configure is synchronous but bounded and reports failed binding via PlatformError message.

- [ ] Write `GlobalHotkeyTests`: F9/F8/F10 use MOD_NOREPEAT, emit correct commands, reject duplicate/out-of-range bindings, rollback failed reconfiguration to old Stop registration, Stop failure reports conflict, suspend fires once, startup failure/disposal cannot deadlock, dispose unregisters all bindings.
- [ ] Run `dotnet test tests/Macrofy.Platform.Windows.Tests -c Release --filter FullyQualifiedName~GlobalHotkeyTests`; watch missing implementation fail.
- [ ] Implement dedicated thread and hidden top-level native window so power broadcasts arrive. Execute registration/reconfiguration on that thread, handle native error paths, isolate event exceptions, and join bounded on disposal. Keep probe-specific F10 service independent.
- [ ] Run hotkey tests and existing Windows suite; require no failed tests. Validate Win32 declarations against official Microsoft documentation during implementation.
- [ ] Commit Task 3: `feat: add global run pause stop hotkeys and suspend handling`.

### Task 4: Accurate sessions and shared atomic dispatch

**Files:** Create `src/Macrofy.Core/Playback/{PlaybackContracts,PlaybackCoordinator,PlaybackSession}.cs`; create `tests/Macrofy.Core.Tests/PlaybackCoordinatorTests.cs`, `PlaybackTestClock.cs`.

**Interfaces:** Consume CompiledAction. Produce `PlaybackTarget(Guid? SavedAppId, TargetRule? Rule, TargetState? State, TargetToken? SelectedSurface)` (null Rule means Screen; Screen also requires null SavedAppId/State/surface), `PlaybackRequest(Guid MacroId, string MacroName, string ProfileName, PlaybackTarget Target, IReadOnlyList<CompiledAction> Actions, int Repeat, int IntervalMs, int StartDelayMs)`, `PlaybackBinding(Guid? SavedAppId, TargetToken? Token, string Name, string? Fingerprint, ClientGeometry? InitialGeometry, TargetState? State)`, `PlaybackPreparation(PlaybackBinding? Binding, PlatformError? Error)`, `GestureResult(DeliveryResult Delivery, PlatformError? CleanupError = null)` and immutable `PlaybackSnapshot` with state/current step/completed steps/completed loops/active elapsed/remaining wait/delivery error/cleanup error. State enum follows approved spec.

Produce `IPlaybackExecutor.PrepareAsync(PlaybackRequest, CancellationToken) : ValueTask<PlaybackPreparation>`, `ValidateAsync(PlaybackBinding, CancellationToken) : ValueTask<DeliveryResult>`, `ExecuteAsync(PlaybackBinding, CompiledAction, CancellationToken) : ValueTask<GestureResult>`. Executor balances native gestures before returning. Produce `PlaybackCoordinator(IPlaybackExecutor executor, TimeProvider timeProvider)` with `StartAsync(PlaybackRequest, CancellationToken = default) : Task<DeliveryResult>`, `TogglePause(Guid)`, `TogglePauseAll()`, `Stop(Guid)`, `StopAll()`, `WaitForIdleAsync(CancellationToken = default) : Task`, `Snapshots : IReadOnlyDictionary<Guid, PlaybackSnapshot>`, `Changed` event, and `IAsyncDisposable`.

- [ ] Write fake-clock tests: 50 ms delay does not advance at 49 ms; Wait plus Wait-after both honored; repeat 2 and interval 100 give exact action times; repeat 0 stops on cancellation; remaining delay preserved across pause; pause during chord reaches safe boundary; snapshots deeply freeze actions; duplicate Start rejected; Stop→immediate Start waits/rejects while old cleanup active; target preparation cancelled on StopAll.
- [ ] Add concurrency tests: two sessions share one gate with no overlapping executor calls; stopping waiter cancels its gate acquisition; stopping one during cleanup leaves other session untouched; cross-profile StopAll clears every active state; shutdown finishes within bounded cleanup behavior; failed cleanup retained next to delivery failure.
- [ ] Run `dotnet test tests/Macrofy.Core.Tests -c Release --filter FullyQualifiedName~PlaybackCoordinatorTests`; require observed RED.
- [ ] Implement monotonic waits using injected TimeProvider and timestamp arithmetic, cancellable pause/wait/gate signaling, immutable snapshots, duplicate-session guard, and one shared gesture gate. Treat Wait as scheduler work outside dispatch. Publish events outside locks; the UI timer does no execution. Use monotonic deadlines and exclude paused time. Final Wait-after completes before Completed.
- [ ] Run all Core tests; require GREEN. Commit Task 4: `feat: execute concurrent macros with accurate cancellable timing`.

### Task 5: Saved compatibility and deliberate testing service

**Files:** Create `src/Macrofy.App/Models/CompatibilityEvidence.cs`, `Services/CompatibilityService.cs`; modify `Models/WorkspaceDocument.cs`, `Services/WorkspaceStore.cs`; create `tests/Macrofy.App.Tests/CompatibilityServiceTests.cs`; extend `WorkspaceStateTests.cs`.

**Interfaces:** Produce persisted `CompatibilityEvidence` with saved app ID, AppIdentity, title, fingerprint, TargetState, InputCapability, ClientGeometry, tested timestamp, observed result. Add default empty evidence collection to existing version-1 WorkspaceDocument. Produce `CompatibilityService` with `IsConfirmed(Guid appId, TargetContext context, TargetState state, InputCapability capability, bool fixedCoordinates) : bool`, `BeginAttemptAsync(Guid appId, TargetToken token, TargetState state, CompiledAction action, CancellationToken cancellationToken) : Task<CompatibilityAttempt>`, `ConfirmAsync(CompatibilityAttempt attempt, bool observedSuccess, CancellationToken cancellationToken) : Task<CompatibilityEvidence>`. Attempt keeps saved app ID, ephemeral token, fingerprint, geometry, exact tested capability and GestureResult; only successful delivered-and-cleaned attempts are confirmable. Constructor receives target context, exclusive input owner, a `Func<PlaybackBinding, CompiledAction, CancellationToken, ValueTask<GestureResult>>` test gesture sender, and workspace persistence callback. The service validates selected app identity/state/surface before invoking the sender; deliberate tests do not require prior successful compatibility evidence.

- [ ] Write tests: restored-after-minimized test can be confirmed; failed/partial/cancelled tests cannot; Click evidence never permits Key/Shortcut/Text/Wheel; title/fingerprint/state/geometry mismatch invalidates relevant evidence; negative observation blocks playback; closure between send and confirmation rejects; save conflict reports error without losing prior evidence; serialized evidence contains no live token/HWND/PID.
- [ ] Add workspace tests: version-1 data without evidence loads empty; legacy unsupported Key remains present, editor shows invalid value, and playback is blocked; structurally invalid/null/newer/corrupt document still preserved; invalid draft never overwrites valid saved action.
- [ ] Run App tests filtered to CompatibilityServiceTests and WorkspaceStateTests; observe missing service/old load-policy RED.
- [ ] Implement exact evidence matching and attempted-input/observation separation. Refactor load validation to preserve structurally valid unsupported values while rejecting malformed structure/settings. Retain atomic save/conflict guard. No automatic game input or success confirmation.
- [ ] Run targeted App tests; require GREEN. Commit Task 5: `feat: persist capability-specific observed compatibility`.

### Task 6: Windows gesture executor and target preflight

**Files:** Create `src/Macrofy.App/Services/WindowsPlaybackExecutor.cs`, `InputActivityGuard.cs`; modify Windows low-level adapters only for proven issues; create `tests/Macrofy.App.Tests/WindowsPlaybackExecutorTests.cs`.

**Interfaces:** Produce `WindowsPlaybackExecutor : IPlaybackExecutor` consuming WindowsWindowCatalog, IInputPlayer, IScreenInputPlayer, CompatibilityService/evidence query, and global-hotkey readiness. Extract `WindowsGestureSender.SendGestureAsync(PlaybackBinding binding, CompiledAction action, CancellationToken cancellationToken) : ValueTask<GestureResult>` as the shared low-level balanced gesture implementation; playback calls it after evidence validation, and Task 5's explicit test sender calls it after test preflight without prior evidence. It revalidates the original token, fingerprint, geometry/state/foreground and permission, but does not authorize compatibility itself. Produce `InputActivityGuard.TryEnterPlayback(Guid macroId, out IDisposable? lease) : bool` and `TryEnterTest(out IDisposable? lease) : bool`; tests and any active/paused/starting session exclude each other. Binding stores original live identity; selected live surfaces are session-only choices. Shared coordinator dispatch from Task 4 is the only production playback gesture caller. Construct the guard before either service; inject callbacks to avoid circular construction.

- [ ] Write preflight tests: Screen needs no game evidence; missing/ambiguous app resolution fails; only user-selected or uniquely evidence-matching surface binds; unregistered Stop blocks native input; unsupported capabilities block window playback; another profile's app IDs resolve via supplied request rule; window closure never falls back to Screen.
- [ ] Write gesture tests: Click down/up; chord modifiers pressed/released in order; cancellation after Ctrl down independently releases Ctrl; Text delivery failure preserved with cleanup failure; Wheel converts current pointer to window client coordinates; percentage 0/100 maps to endpoints; resized fixed-pixel target fails; percentage recalculates only for valid current geometry; foreground/wrong minimized state fails before posts; pause/resume validates original token without rebinding. Change title/fingerprint between Prepare and first post and assert zero delivery.
- [ ] Run targeted tests; require observed RED. Implement adapters, coordinate transformation, required capability mapping (multi-key chord needs Shortcut), validation before every gesture/native post, 500 ms independent cleanup, and partial delivery accounting. No automatic foreground changes or minimize calls.
- [ ] Run all App and Windows tests; require GREEN. Commit Task 6: `feat: bind real playback to validated screen and window targets`.

### Task 7: Connect app controls, progress, and compatibility UI

**Files:** Create `src/Macrofy.App/Services/WorkspacePlaybackController.cs`, `Views/MainWindow.Playback.cs`, `Views/MainWindow.Compatibility.cs`; modify WorkspaceState, MainWindow.cs, MainWindow.Panes.cs, MainWindow.Macros.cs, Program.cs; create `tests/Macrofy.App.Tests/PlaybackUiTests.cs`; adapt existing native UI tests to injected playback.

**Interfaces:** Produce `IWorkspacePlaybackController` and `WorkspacePlaybackController` with `StartAsync(Profile, Macro, bool selectedAction = false, int selectedIndex = -1) : Task<DeliveryResult>`, `CanStart(Profile, Macro, out string reason) : bool`, `TogglePause(Guid)`, `TogglePauseAll()`, `Stop(Guid)`, `StopAll()`, read-only session snapshots, global-hotkey readiness/error, `Changed` event and async shutdown. Map owning profile rules into PlaybackRequest; compiler gets current reserved shortcuts. MainWindow accepts injected controller/hotkey/compatibility services in test constructor. Default production construction owns Windows services; every headless test explicitly injects harmless fakes.

- [ ] Write UI tests: row/editor/global Run route real requests; action Test sends only selected action once; unsaved drafts block input; countdown cancels on Stop/close; registration conflict disables Run/Test; F9/F8/F10 events route once without focused-handler duplicates; Run all skips disabled/invalid/busy macros and preserves save-error message; editing/deletion locked during Starting/Paused/Waiting; progress shows current step/loops/elapsed/error; browsing profiles does not retarget running sessions; cleanup precedes service disposal; suspend stops all.
- [ ] Write compatibility UI tests: selected app/window/surface/state/action is explicit; switching clears test/coordinates; one harmless test uses exclusive ownership; confirmation remains disabled until successful delivery/cleanup; user response saves exact capability and enables matching window macro only; zero automatic observations.
- [ ] Run targeted headless tests and observe RED before control wiring changes.
- [ ] Replace PreviewSession/Tick execution and preview labels with production controller snapshots. Keep UI timer only for display. Move playback/compatibility responsibilities into focused partials. Add validation hints and reason tooltips, preserve icon controls/themes and existing names where possible. Start Screen input only after three-second countdown; hold activity lease during countdown. Register/reconfigure hotkeys while idle; suspend requests immediate cancellation. Recording stays unavailable with truthful label.
- [ ] Run all App tests; require GREEN with no native injection/registration. Commit Task 7: `feat: connect native UI to real macro execution`.

### Task 8: Prove delivery and concurrent cleanup on Windows

**Files:** Extend `tools/Macrofy.TestTarget/Program.cs`, `tests/Macrofy.Platform.Windows.Tests/InputIntegrationTests.cs`; create `tests/Macrofy.App.Tests/PlaybackIntegrationTests.cs`; update TestTarget native input support only as needed.

**Interfaces:** Consume Tasks 1–7 contracts. Extend controlled target pipe commands for a harmless receiving surface, focused/client geometry/cursor diagnostics, and received key/text/wheel events. Keep existing pipe protocol and old tests intact. Controlled target receipts explicitly remain delivery evidence.

- [ ] Add background/minimized real coordinator tests for Click+Key+Text+Wait+Wheel, finite loops, concurrent shared-target macros, cancellation during a delay/chord, original target destruction, and unchanged physical cursor/foreground around window posts. Use only controlled fixture identities.
- [ ] Add real Screen integration test using an explicitly created isolated harmless target surface: expose/focus it, capture its safe point, run one finite Click+Key+Text+Wheel sequence, verify events, restore cursor/foreground in finally. If target cannot be safely focused/exposed, fail with diagnostic before injection. Never click arbitrary desktop coordinates or run infinite macros.
- [ ] Run targeted integration tests; observe RED where earlier seams did not prove real behavior, diagnose receiver/input differences before modifying code. Serialize real Windows integration to prevent foreground/cursor interference.
- [ ] Run `powershell -NoProfile -File scripts/verify-probe.ps1`; require locked restore, zero-warning Release build, all tests pass with no unexpected skips. Commit Task 8: `test: verify real screen and concurrent window macro delivery`.

### Task 9: Review, document, and build portable EXE

**Files:** Update README.md, docs/verification/native-ui-preview.md, docs/verification/windows-input-compatibility.md, scripts/publish-ui.ps1 only if naming/error output needs correction. Build artifact remains ignored.

- [ ] Review complete branch against approved spec and these interfaces. Review Stop/partial insertion, compatibility gates, target rebinding, clock/pause races, hotkey rollback, legacy storage, and headless safety. Use execution method's independent review process. Reproduce important findings with failing regression tests, fix, then run relevant suites.
- [ ] Update docs with exact automated counts/evidence, real supported actions, scheduling/percentage/Wheel semantics, global-key conflicts, and separate unconfirmed game response. Remove stale claims that macro playback is merely preview; retain recording and game compatibility limitations.
- [ ] Run `powershell -NoProfile -File scripts/verify-probe.ps1` and `powershell -NoProfile -File scripts/publish-ui.ps1`; require success, zero build warnings/errors, all tests pass, and `artifacts/win-x64/Macrofy.exe` exists. Perform read-only launch smoke verification; launching must not start input. Clean-machine Windows 10/11 and actual-game observations remain explicitly manual.
- [ ] Commit verified source/docs; report artifact path, tests, concrete behavior, and remaining manual evidence. Do not merge/push without requested workflow.

## Plan self-review

Coverage: Tasks 1/2/6 deliver real actions and validation; Task 4 accurate scheduling/concurrency; Task 3 global controls/lifecycle; Tasks 5/6/7 compatibility/target safety; Task 7 progress/UI; Tasks 8/9 evidence and published EXE. Current saved action syntax, interval semantics, text limits, percentage endpoints, cleanup budget, exclusive tests, reserved hotkeys, old-workspace visibility, and controlled-screen evidence all have explicit owning tests.

Dependency order: 1 → 2/4; 2/3/4 → 5/6; 5 → 6; 3/4/5/6 → 7; all → 8 → 9. Task 5's test sender/activity owner are injected delegates; tests supply fakes and do not require production implementations from Task 6. Production composition creates activity guard and WindowsGestureSender first, then CompatibilityService with their callbacks, then WindowsPlaybackExecutor with its evidence query. All share the same low-level adapters; the activity guard excludes tests from every active playback session.

Execution recommendation: **Subagent-driven**, because nine tasks cross scheduler, Win32 input, hotkey lifecycle, storage, and UI boundaries. Fresh review per task helps catch cancellation/cleanup and interface errors before later integration depends on them. Native execution remains available for lower context overhead.
