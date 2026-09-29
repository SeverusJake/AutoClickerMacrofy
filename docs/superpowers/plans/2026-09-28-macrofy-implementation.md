# Macrofy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Native execution is recommended for this plan because its platform contracts, recorder, and scheduler share tightly connected state. Execution method remains the user's choice.

**Goal:** Deliver a portable Windows Macrofy app that records and edits macros and replays them on a fixed interval. Window mode preserves the user's cursor and foreground focus; the user-added default Screen mode operates on the visible desktop.

**2026-09-29 scope update:** User requested Screen as default with no chosen window and three UI previews. Implemented in the current probe: explicit default Screen option, desktop coordinate capture, one SendInput click after a three-second countdown, and mode-switch coordinate clearing. Window loss never triggers screen fallback. Recorder/scheduler/full UI work stays pending; UI choice is represented by `docs/ui/macrofy-ui-options.html`.

**2026-09-29 UI approval:** User approved the current preview after the icon update. Use desktop tabs, profile macro playback controls, Design 2 macro workspace, multicolor Light/Dark modes, concise copy and icon controls with tooltips/accessibility labels. The next implementation prerequisite remains Task 3's actual CookieRun click compatibility test.

**2026-09-29 execution steering:** User requested the approved preview UI in the EXE first. The UI/publishing subset of Tasks 10–11 is authorized before the actual-game gate as a clearly labeled native UI preview. This does not authorize claiming verified game input or completing Tasks 4–9. Use programmatic Avalonia views, consistent with the existing probe, with a separate local UI-workspace model/store and input-free preview sessions.

**Architecture:** Avalonia presents a desktop horizontal tab bar (Profiles/Apps/Macros/Compatibility/Settings/Log/About), as requested in the user's DS4Windows reference on 2026-09-29. Common profile context and Stop/status persist across tabs. A platform-neutral core owns macros, editing, persistence, and scheduling; a separate Windows backend owns hooks, hotkeys, window discovery, and targeted message delivery. Actual Google Play Games compatibility is checked before building the full UI; future Mac support reuses contracts but is not part of this release.

**Tech Stack:** C#, .NET 10, Avalonia 12.1.3, CommunityToolkit.Mvvm 8.4.2, Windows user32 interop, versioned JSON, xUnit 2.9.3 for Core/Windows tests, xunit.v3 3.2.2 for App headless tests (required by Avalonia 12.1.3), xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.10.1. Pin SDK 10.0.302, already installed locally, with `rollForward=latestPatch`. Pin Avalonia/Desktop/Fluent/Headless.Xunit packages together at 12.1.3. Exact package references and transitive dependencies are recorded in lock files in Task 1; no floating versions.

**Spec:** `docs/superpowers/specs/2026-09-28-macrofy-design.md`

## Approved execution scope (2026-09-28)

- Full macro features remain planned for future games. CookieRun requires clicks only at the early gate.
- Minimized playback is preferred when supported. Non-minimized background playback (visible, partly covered, fully covered) is the acceptable fallback; confirm each observed state separately.
- Implement Tasks 1-3 in this phase. If CookieRun rejects both minimized and background clicks, pause and review options with the user. Do not proceed to the full UI without actual-game evidence.
- Execute inline in a managed worktree. A deliberate game test still requires a user-selected harmless client position and observed confirmation; do not invent game coordinates.

## Accepted review fixes

1. Window lifetime: bind tokens to top-level and input-surface generations; observe destruction and invalidate permanently even if the same process recreates a window with the same HWND. Check before every send, document the unavoidable external check/send race, and test same-process recreation as well as restart.
2. Compatibility gestures: a test is a validated ordered sequence of commands and delays, not one command. Acquire an exclusive activity lease, reject concurrent recording/playback/testing, and release target-held input after cancellation or partial delivery. Cover click, key, text, wheel, drag and hold independently; ordinary key tests do not prove shortcuts or sustained holds.
3. Live target context: add ITargetContext.GetAsync(TargetToken, CancellationToken) returning current state, geometry, and surface fingerprint or an error. Refresh before every pointer action and at run boundaries; recheck state/fingerprint compatibility before every command. Stop on changed/unconfirmed context; never retarget.
4. Overlapping groups: canonical actions are a flat ordered timeline. ActionGroup is editor metadata referencing stable action IDs, not nested executable children. Crossing mouse/key holds preserve exact raw order through group edits and serialization.
5. Posted-input limits: use a serial sender with no application send-ahead queue, maximum 100 native messages/second and burst of one. Cancellation stops new posts; already-posted Win32 messages cannot be withdrawn. Cleanup gets a separate 500ms budget. Report best-effort release, never guaranteed target processing. Test a stalled receiver and partial sends; native quota exhaustion is a delivery error.

## Global Constraints

- Source folder: `H:\MyProjects\Apps\AutoClickerMacrofy`; all paths below are relative to that repository.
- "The first release targets Windows 10 and Windows 11, x64."
- Multiple different macros may play concurrently, including on the same app. Recording and compatibility tests remain exclusive. A macro cannot have duplicate active sessions.
- Top-level tabs are Profiles, Apps, Macros, Compatibility, Settings, Log and About, with common profile context and Pause all/Resume all/Stop all controls.
- Profiles shows all macros with per-row Run/Pause/Resume/Stop. Macros displays Design 2's workspace and the selected macro's editor directly.
- "The default interval is 60 seconds and is editable per macro. Manual single-run playback is also available."
- Iterations of one macro never overlap. If its scheduled boundary arrives while its run is active, skip that boundary; other macros remain independent.
- "Skipped runs are never queued or replayed as a catch-up burst."
- "The target must be active when recording begins."
- "Data resides in `MacrofyData` beside the executable."
- Target closes, becomes ambiguous before start, or delivery/geometry/access fails: stop; never silently retarget.
- Window mode uses targeted messages and never switches to physical input or activates a window as a fallback. Explicit Screen mode uses physical input and remains Screen when no app is assigned.
- Windows package is self-contained and portable; no installer. Mac implementation requires a later real-Mac milestone.
- Platform/game compatibility cannot be inferred from PostMessage success. Require user-confirmed compatibility tests by target state and required action capabilities.
- First real target: CookieRun: Crumble - Idle RPG in Google Play Games. Use picker-discovered executable identity and title pattern `*CookieRun: Crumble - Idle RPG*`; do not persist a nickname, PID, or HWND as identity.
- Dependency versions are a verified snapshot, not a requirement to install obsolete packages later. Resolve an unavailable or incompatible pin explicitly before executing dependent tasks.

## Review Focus

1. Window-handle reuse after game restart: stop old session instead of sending to replacement (Tasks 2 and 3).
2. Shortcut/drag held during focus loss or emergency stop: close recorded pairs and release playback-held input only to original target (Tasks 6 and 7).
3. Game ignores messages while minimized despite successful API return: show delivery versus user confirmation distinctly (Tasks 3 and 9).
4. DPI change, client resize, or minimized geometry: deterministic conversion or an actionable geometry error (Tasks 2 and 4).
5. Partial save, read-only portable folder, or newer JSON schema: preserve last-good/original data and show unsaved state (Task 5).

## File and interface map

```text
src/Macrofy.Platform/             neutral target/input/hotkey contracts
src/Macrofy.Platform.Windows/     native services, isolated interop declarations
src/Macrofy.Core/
  Macros/                        immutable model, validation, grouping/edit operations
  Recording/                     focus-aware capture timeline
  Playback/                      scheduler, cancellation, session state
  Profiles/                      profile selection and capability records
  Storage/                       versioned atomic JSON persistence
src/Macrofy.App/
  Views/                         tabs, target picker, macro editor
  ViewModels/                    display state and commands
  Services/                      UI composition, dialogs, theme
tests/Macrofy.Core.Tests/         fake-clock/platform/filesystem behavior
tests/Macrofy.Platform.Windows.Tests/ message/target/hook integration
tests/Macrofy.App.Tests/          headless view-model and disclosure checks
tools/Macrofy.TestTarget/         controlled receiving window and acknowledgements
tools/Macrofy.CompatibilityProbe/ early interactive test against chosen game
scripts/                        reproducible verify and publish commands
```

Neutral value types in `Macrofy.Platform/Models` are defined once:

- `TargetToken(Guid Id)` is opaque session identity; Windows maps it to HWND plus process ID/start identity. Never persist handles or reuse an old token after restart.
- `AppIdentity(string Name, string? ExecutablePath, string? BundleId)`; Windows fills executable metadata, future Mac fills bundle identity.
- `TargetRule(AppIdentity App, string TitlePattern)`.
- `ClientGeometry(int Width, int Height, double DpiScale)` and `PointerPoint(double X, double Y)`.
- `TargetWindow(TargetToken Token, AppIdentity App, string Title, bool IsMinimized, ClientGeometry? Geometry)`.
- `ResolutionResult` distinguishes Missing, Ambiguous with candidates, and Matched with window.
- `InputCommand` is an immutable hierarchy: `PointerCommand(PointerKind Kind, PointerPoint Point, MouseButton? Button, int WheelDelta = 0)`, `KeyCommand(KeyKind Kind, KeyIdentity Key)`, `TextCommand(string Text)`. PointerKind includes Move, Down, Up, VerticalWheel, HorizontalWheel; KeyKind includes Down and Up; MouseButton includes Left, Right, Middle, X1, X2.
- `KeyIdentity(string LogicalKey, int? NativeScanCode, bool IsExtended)` retains physical information without exposing Windows constants to core logic.
- `DeliveryResult(bool Queued, PlatformError? Error)` reports queuing, not gameplay acknowledgement.
- `TargetState` is BackgroundVisible, BackgroundPartlyCovered, BackgroundCovered, or Minimized (coverage is user-observed during compatibility tests); `InputCapability` separates Click, Key, Text, Wheel, Drag, Hold. Pointer points inside commands are client pixels at the platform boundary; normalized positions are mapped by core before delivery.

All Windows-targeted tests are serialized. A test may skip only when its explicit OS/hardware prerequisite is unavailable, and must state the reason in the verification report.

## Task 1: Solution foundation and neutral contracts

**Files:** Create `Macrofy.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`; project files `src/Macrofy.Platform/Macrofy.Platform.csproj`, `src/Macrofy.Platform.Windows/Macrofy.Platform.Windows.csproj`, `src/Macrofy.Core/Macrofy.Core.csproj`, `src/Macrofy.App/Macrofy.App.csproj`, `tests/Macrofy.Core.Tests/Macrofy.Core.Tests.csproj`, `tests/Macrofy.Platform.Windows.Tests/Macrofy.Platform.Windows.Tests.csproj`, `tests/Macrofy.App.Tests/Macrofy.App.Tests.csproj`. Create one file per neutral model above under `src/Macrofy.Platform/Models/`, the interface files below under `src/Macrofy.Platform/`, and `tests/Macrofy.Core.Tests/ContractsTests.cs`. Commit generated `packages.lock.json` beside each project file.

**Interfaces:**
- `IWindowCatalog.ListAsync(CancellationToken) -> Task<IReadOnlyList<TargetWindow>>`; `ResolveAsync(TargetRule, TargetToken?, CancellationToken) -> Task<ResolutionResult>`; event `Action<TargetToken> TargetLost`.
- `IInputPlayer.SendAsync(TargetToken, InputCommand, CancellationToken) -> ValueTask<DeliveryResult>`; `ReleaseHeldAsync(TargetToken, CancellationToken) -> ValueTask<DeliveryResult>`.
- `IInputRecorder.StartAsync(TargetToken, CancellationToken) -> Task`; `StopAsync(CancellationToken) -> Task`; event `Action<CaptureMessage> Captured`, where messages are Input, FocusLost, FocusGained, TargetLost with monotonic timestamp and injected flag.
- `IGlobalHotkeys.Configure(HotkeySet) -> HotkeyRegistrationResult`; event `Action<HotkeyCommand> Triggered`, with RecordToggle, PlayToggle, Stop.
- `IPermissionService.CheckAsync(TargetToken, CancellationToken) -> Task<PermissionResult>`.
- `ITargetContext.GetAsync(TargetToken, CancellationToken) -> ValueTask<TargetContextResult>` exposes live geometry, foreground/minimized state, and executable/input-surface fingerprint without native handles.
- `ISystemEvents.Suspended` event signals machine sleep; native services implement disposal.

- [x] Create SDK/project/package configuration, keeping core and contracts on `net10.0`, Windows backend/tests on `net10.0-windows`, App referencing Windows backend only in Windows builds. Use SDK-style XML and preserve existing `.gitignore`.
- [x] Add failing `NeutralModelsExposeNoNativeHandles`: assert `TargetToken` contains GUID identity and `TargetWindow` exposes no `IntPtr`, `HWND`, or Win32 virtual-key public property.

```csharp
Assert.Equal(typeof(Guid), typeof(TargetToken).GetProperty("Id")!.PropertyType);
Assert.DoesNotContain(typeof(TargetWindow).GetProperties(), p => p.PropertyType == typeof(IntPtr));
```

- [x] Run `dotnet test tests/Macrofy.Core.Tests --filter ContractsTests`; expect missing contract types until implemented.
- [x] Define the contracts/value types above; create App composition entry point with unsupported-platform status until a backend is registered. No Mac stub claims functional recording.
- [x] Restore with lock files and run `dotnet build Macrofy.sln` plus contract tests; expect build/test success. Check pinned package target-framework and xUnit adapter compatibility before continuing; report any required version change.
- [x] Commit foundation and tests as `build: establish Macrofy solution and platform contracts`.

## Task 2: Windows window discovery, identity, and geometry

**Files:** Create `src/Macrofy.Platform.Windows/Interop/WindowNative.cs`, `WindowsWindowCatalog.cs`, `WindowsPermissionService.cs`, `WindowIdentityRegistry.cs`, `WindowGeometryProvider.cs`, `tests/Macrofy.Platform.Windows.Tests/WindowCatalogTests.cs`.

**Consumes:** Task 1 window and permission contracts.
**Produces:** `WindowsWindowCatalog`, `WindowsPermissionService`; `WindowIdentityRegistry.TryResolve(TargetToken, out NativeTarget) -> bool`, internal only; `WindowGeometryProvider.Get(TargetToken) -> GeometryResult`.

- [x] Add failing assertions: two matching titles yield Ambiguous; no match yields Missing; explicit token selects exactly one candidate; reused HWND with changed process start identity invalidates token; same-process window destruction/recreation also permanently invalidates the original token, including its child surface. `PermissionDeniedIsReported` asserts access-denied result without elevation.

```csharp
// Separate catalog fixtures with two matching windows, then zero windows.
Assert.IsType<ResolutionResult.Ambiguous>(ambiguousResult);
Assert.IsType<ResolutionResult.Missing>(missingResult);
Assert.False(registry.TryResolve(oldToken, out _)); // Reused HWND, new process identity.
```

- [x] Run `dotnet test tests/Macrofy.Platform.Windows.Tests --filter WindowCatalogTests`; expect failures from missing implementation, not unrelated tooling errors.
- [x] Enumerate titled user windows including minimized windows, excluding Macrofy itself. Match application identity plus case-insensitive glob (`*`/`?`), cache only geometry belonging to live token, and check identity before each lookup. Resolve child input surface without changing focus; choose a surface deterministically and expose it in the probe. Monitor top-level and child destruction with out-of-context WinEvent hooks on a message thread; never silently substitute a recreated child.
- [x] Add geometry assertions for non-100% DPI, resize, and minimized cached geometry; unknown geometry returns error, zero size never scales coordinates. Verify list refresh leaves foreground window unchanged.
- [x] Run window tests on Windows and a controlled two-window process. Record which checks are real integration versus fakes.
- [x] Commit as `feat: discover and resolve Windows targets`.

## Task 3: Targeted playback and early game compatibility gate

**Files:** Create `src/Macrofy.Platform.Windows/Interop/InputMessages.cs`, `WindowsInputPlayer.cs`, `MessageEncoder.cs`, `HeldInputTracker.cs`, two tool projects `tools/Macrofy.TestTarget/` and `tools/Macrofy.CompatibilityProbe/`, `tests/Macrofy.Platform.Windows.Tests/InputPlayerTests.cs`, `docs/verification/windows-input-compatibility.md`.

**Consumes:** Task 1 player contract; Task 2 token/geometry registry.
**Produces:** `WindowsInputPlayer`; internal `MessageEncoder.Encode(InputCommand, NativeTarget, HeldState) -> IReadOnlyList<NativeMessage>`. Test target exposes received-event acknowledgements over a local named pipe; live game probe displays Queued/Failed and solicits observed result separately.

- [x] Add failing encoder/delivery assertions: ordered down/up, signed client coordinates, correct wheel screen-coordinate conversion, modifier/held-button masks, key repeat/transition bits, surrogate-pair text, access denial, cancellation, and token invalidation. `CancelledSendDoesNotDeliver` calls the actual player with a pre-cancelled token and asserts zero native sends through the test interop seam. Assert queued=true does not set user-confirmed compatibility.

```csharp
Assert.True(successfulDelivery.Queued);
Assert.Empty(nativeSendsAfterPreCancelledCall);
Assert.Equal(cursorBefore, cursorAfter);
Assert.Equal(foregroundBefore, foregroundAfter);
```

- [x] Run `dotnet test tests/Macrofy.Platform.Windows.Tests --filter InputPlayerTests`; expect specific unimplemented-player failures.
- [x] Implement targeted PostMessage delivery with the serial sender/rate/cleanup bounds in Accepted review fixes, key-state tracking, scan/extended bits, validated pointer coordinates, WM_CHAR text, and target-only cleanup. Stop at first failed send; do not call global input/focus APIs.
- [x] Launch controlled TestTarget and use named-pipe acknowledgement to verify actual received clicks/keys/text/wheel/drag, background and minimized where the test target supports them. Compare foreground-window handle and physical cursor before/after.
- [ ] Run `dotnet run --project tools/Macrofy.CompatibilityProbe -- --interactive`. List targets, select the user-identified CookieRun: Crumble - Idle RPG window through the picker, and ask for a harmless test action/position. Test Minimized first and non-minimized background visible/partly covered/fully covered separately. CookieRun needs only confirmed click capability; other gestures stay future-target capabilities. Record mouse/key capabilities and user observations in verification document. Do not send to an unspecified window or arbitrary position. The window title was observed read-only during planning; input compatibility remains unknown.
- [ ] **Milestone gate:** minimized clicks are preferred; confirmed background clicks are the accepted fallback. If both fail, pause and review options with the user. Proceed to full app only after the intended game accepts required click input. If game is unavailable, retain probe and report awaiting actual test; controlled-window success cannot substitute for game success.
- [ ] Commit verified backend/probe as `feat: add targeted Windows input and compatibility probe`.

## Task 4: Macro model, validation, coordinates, and grouped editing

**Files:** Create `src/Macrofy.Core/Profiles/ProfileDefinition.cs`, `SavedAppDefinition.cs`, `src/Macrofy.Core/Macros/MacroDefinition.cs`, `MacroAction.cs`, `MacroValidator.cs`, `MacroEditor.cs`, `CoordinateMapper.cs`, `tests/Macrofy.Core.Tests/MacroEditorTests.cs`, `CoordinateMapperTests.cs`.

**Consumes:** Neutral commands and geometry from Task 1.
**Produces:** immutable `ProfileDefinition(Guid Id, string Name, ImmutableArray<SavedAppDefinition> Apps, ImmutableArray<MacroDefinition> Macros)` and `SavedAppDefinition(Guid Id, string Name, TargetRule Rule)`. Each profile persists many target apps, per the user's 2026-09-29 clarification. `MacroDefinition(Guid Id, string Name, Guid? SavedAppId, CoordinateMode Mode, ClientGeometry RecordedGeometry, TimeSpan Interval, bool LoopEnabled, ImmutableArray<MacroAction> Actions)` binds one macro to one saved app in its profile, as the user selected. A null SavedAppId explicitly represents Screen; a dangling non-null reference fails validation and never falls back. Multiple macros may share an app. Do not implement the old single profile target field or action-level app switching.
- `MacroAction` hierarchy: `DelayAction(Guid Id, TimeSpan Duration)`, `InputAction(Guid Id, InputCommand Command)`, `ActionGroup(Guid Id, GroupKind Kind, ImmutableArray<Guid> ActionIds)` is separate editor metadata over the flat sequence.
- `CoordinateMode` is FixedPixels or Percentage. Percentage coordinates are stored as normalized fractions (0..1), displayed as 0..100%; both modes require mapped points within `[0, Width)` and `[0, Height)`.
- `MacroSnapshot(Guid MacroId, Guid? SavedAppId, CoordinateMode Mode, ClientGeometry RecordedGeometry, TimeSpan Interval, bool LoopEnabled, ImmutableArray<MacroAction> Actions)` copies immutable state for a running session. Resolve the selected macro's saved app rule to one live token before recording/playback; changing the profile's other saved apps cannot retarget an active session.
- `MacroValidator.Validate(MacroDefinition, ClientGeometry?) -> ValidationResult`; `ValidateBinding(MacroDefinition, ProfileDefinition) -> ValidationResult` rejects dangling app IDs and verifies the macro belongs to the profile; `CoordinateMapper.Map(PointerPoint, CoordinateMode, ClientGeometry) -> PointerPoint`; `MacroEditor.Apply(MacroDefinition, EditOperation) -> EditResult`; `MacroDefinition.Snapshot() -> MacroSnapshot`.

- [ ] Add failing `ReorderPreservesPairedEvents`, `UnmatchedUpRejected`, `NegativeDelayRejected`, `EmptyMacroCannotRun`, `PercentScalesFromRecordedGeometry`, and `DragOutsideClientRequiresCorrection`. Assert 50% width/height of 800x600 maps to (400,300); changing mode retains equivalent recorded position. Snapshot remains unchanged after edit.

```csharp
Assert.Equal(new PointerPoint(400, 300), CoordinateMapper.Map(
    new PointerPoint(0.5, 0.5), CoordinateMode.Percentage, new ClientGeometry(800, 600, 1)));
Assert.Equal(new PointerPoint(400, 300), CoordinateMapper.Map(
    new PointerPoint(400, 300), CoordinateMode.FixedPixels, new ClientGeometry(800, 600, 1)));
```

- [ ] Run `dotnet test tests/Macrofy.Core.Tests --filter 'FullyQualifiedName~MacroEditorTests|FullyQualifiedName~CoordinateMapperTests'`; expect unimplemented model/editor failures.
- [ ] Implement immutable flat actions and group-reference metadata, preserving order without duplicate delays, group expansion, add/remove/duplicate/reorder/change operations, and mode conversion. Millisecond delay values are the sole timing source. Maintain paired events and multiple held keys for shortcuts.
- [ ] Run tests; expect deterministic valid groups and exact action order/timing. Editing invalid draft returns errors plus draft rather than replacing last valid persisted definition.
- [ ] Commit as `feat: model and edit recorded macros`.

## Task 5: Portable atomic profile/settings storage

**Files:** Create `src/Macrofy.Core/Storage/JsonMacroStore.cs`, `StorageEnvelope.cs`, `AtomicFileStore.cs`, `AppSettings.cs`, `tests/Macrofy.Core.Tests/StorageTests.cs`.

**Consumes:** Task 4 definitions; Task 1 hotkey configuration.
**Produces:** `JsonMacroStore.LoadAsync(CancellationToken) -> Task<StoreLoadResult>`; `SaveAsync(StoreDocument, CancellationToken) -> Task<SaveResult>`; `RecoverBackupAsync(CancellationToken) -> Task<StoreLoadResult>`. `StoreDocument` includes schema=1, profiles, settings, capability-test records. Constructor takes base-directory and file-I/O abstraction for failure tests.
- Define `src/Macrofy.Core/Profiles/CompatibilityRecord.cs` here for serialization: `CompatibilityRecord(Guid Id, Guid SavedAppId, TargetRule Rule, string SurfaceFingerprint, TargetState State, InputCapability Capability, DateTimeOffset TestedAt, bool ObservedSuccess)`. Scope evidence to the saved app's stable ID and exact rule/fingerprint; different apps do not inherit each other's compatibility. Records do not contain live target tokens; Task 9 adds workflow over this type. Fingerprint includes executable version/file identity and input-surface class, not a persisted HWND.

- [ ] Add failing round-trip assertions for Unicode names/text, multiple saved apps in one profile, app identities/title rules and bindings, grouped actions, coordinate mode, hotkeys, and capability records. Saved app settings must survive reopening while apps are closed; startup must resolve new windows without persisting live handles or process IDs. Add `SavedAppsAndMacroBindingsSurviveReload`, `DanglingSavedAppDoesNotBecomeScreen`, `FailedReplacementKeepsLastGood`, `NewerSchemaIsPreserved`, `ReadOnlyFolderShowsUnsaved`, `BackupRecoveryPreservesCorruptOriginal` with temporary test directories.

```csharp
// Compare bytes through the file-I/O fixture before/after injected replacement failure.
Assert.Equal(lastGoodBytes, stateBytesAfterFailedSave);
Assert.Equal(corruptOriginalBytes, preservedOriginalBytesAfterRecovery);
Assert.Equal("CookieRun é›¶", reloadedProfile.Name);
```

- [ ] Run `dotnet test tests/Macrofy.Core.Tests --filter StorageTests`; expect missing-store failures.
- [ ] Write `MacrofyData/state.json` beneath `AppContext.BaseDirectory`, versioned serializer with explicit action discriminators, temp-write/flush/replace and last-good backup. Preserve unknown/corrupt original on load/recovery. Never fall back to home/AppData silently.
- [ ] Run storage tests and inspect temporary-directory cleanup. On save failure keep dirty draft in memory and expose error; only successful save clears dirty state.
- [ ] Commit as `feat: persist portable profiles and settings`.

## Task 6: Concurrent macro sessions, pause controls and fixed-boundary scheduling

**Files:** Create `src/Macrofy.Core/Playback/MacroRunner.cs`, `IntervalScheduler.cs`, `ActivityCoordinator.cs`, `PlaybackSessionManager.cs`, `InputDispatcher.cs`, `PlaybackStatus.cs`, `IMonotonicClock.cs`, `tests/Macrofy.Core.Tests/SchedulerTests.cs`, `PlaybackCancellationTests.cs`, `PlaybackConcurrencyTests.cs`, `PlaybackPauseTests.cs`. Adapt platform delivery/session cleanup contracts and their Windows implementation where required; the initial probe's single-caller player does not itself provide concurrent session support.

**Consumes:** Task 4 snapshot, Task 1 player/system events, Task 2 target events/live context and Task 9 compatibility checks (inject a contract during Task 6, compose implementation after Task 9).
**Produces:** `ActivityCoordinator.TryBegin(ActivityKind, Guid? macroId) -> ActivityLease?` allows distinct playback sessions while keeping recording/compatibility tests exclusive. `PlaybackSessionManager.StartAsync(ProfileId, MacroSnapshot, TargetWindow, CancellationToken)`, `PauseAsync(MacroId)`, `ResumeAsync(MacroId)`, `StopAsync(MacroId)` and `StopAllAsync()` coordinate per-macro frozen state. `MacroRunner.RunAsync(MacroSnapshot, TargetWindow, CancellationToken) -> Task<RunResult>`; `IntervalScheduler.StartAsync(MacroSnapshot, TargetWindow, CancellationToken) -> Task<SessionResult>`; event `Action<PlaybackStatus> StatusChanged` includes macro/session identity.
- `IMonotonicClock.Elapsed -> TimeSpan`, `DelayUntilAsync(TimeSpan deadline, CancellationToken) -> Task`. Real clock uses Stopwatch; fake clock advances explicitly.

- [ ] Add failing tests asserting run starts [0,60,120] seconds for 10-second macro, [0,120] for 70-second macro, and no overlapping iterations of the same macro. Exact-boundary completion permits next run only if that macro's prior run completed. Loop-off yields one run.

Use a test-local fake implementing IMonotonicClock and a player that logs run-start timestamps. Assertion excerpts from separate 10-second and 70-second fixtures:

```csharp
// Starts are measured relative to each fixture's t0; advance clock through 120s.
Assert.Equal(new[] { 0d, 60d, 120d }, starts.Select(t => t.TotalSeconds));
// Separate 70-second fixture: 60s boundary was busy, never queued.
Assert.Equal(new[] { 0d, 120d }, longRunStarts.Select(t => t.TotalSeconds));
Assert.Equal(1, maximumConcurrentRunsForOneMacro);
```

- [ ] Add cancellation assertions: stop during delay sends no next action; cleanup releases only the stopped session's held-input ownership; target lost never retargets; suspend stops every session; edit after start cannot alter a snapshot. Recording/compatibility lease requests fail while any playback session is running, pausing, paused or waiting.
- [ ] Add concurrency assertions: two different macros run together on different targets and on the same resolved live target; a second start for the same macro is rejected; pausing/stopping one leaves others running; profile browsing does not retarget sessions; Stop all/F10 cancels all profiles. A shared dispatcher serializes actual native calls, preserves each session's event order and enforces the aggregate post budget. Shared key/button ownership prevents one session's cleanup from releasing another's input. Screen batches preserve their own move/down/up ordering while multiple Screen macro sessions can interleave batches.
- [ ] Add pause assertions: Pausing reaches Paused at a boundary with no session-owned held inputs; no action sends while paused; resume preserves position, remaining delay and live target identity; paused duration shifts only that session's scheduling clock; stopping a paused session cancels future loops; target loss while paused cannot rebind on resume.
- [ ] Run `dotnet test tests/Macrofy.Core.Tests --filter 'FullyQualifiedName~SchedulerTests|FullyQualifiedName~PlaybackCancellationTests|FullyQualifiedName~PlaybackConcurrencyTests|FullyQualifiedName~PlaybackPauseTests'`; expect scheduler/coordinator failures.
- [ ] Implement per-session origin=t0, sequential replay within each macro, independent pause/cancel controls, finite monotonic deadlines, missed-boundary skip, next-loop countdown/skipped count and bounded cleanup cancellation tokens independent of cancelled run tokens. Use shared native dispatch and held-input ownership without a send-ahead queue. Sleep stops every session and requires manual restart.
- [ ] Run fake-clock tests without real minute waits. Verify independent state transitions and cleanup, same-app concurrency, pause/resume, and target closure releasing only affected leases.
- [ ] Commit as `feat: schedule concurrent macros with pause and stop controls`.

## Task 7: Physical-input recording and focus transitions

**Files:** Create `src/Macrofy.Platform.Windows/Interop/HookNative.cs`, `WindowsInputRecorder.cs`, `CaptureThread.cs`, `src/Macrofy.Core/Recording/RecordingSession.cs`, `CapturedActionBuilder.cs`, `tests/Macrofy.Core.Tests/RecordingTests.cs`, `tests/Macrofy.Platform.Windows.Tests/RecorderHookTests.cs`.

**Consumes:** Task 1 recorder/capture contracts, Task 4 model, Task 6 exclusive lease.
**Produces:** `RecordingSession.StartAsync(TargetWindow, CancellationToken) -> Task<RecordingStartResult>`; `FinishAsync(CancellationToken) -> Task<RecordingResult>`; event `Action<RecordingStatus> StatusChanged`; `CapturedActionBuilder.Accept(CaptureMessage) -> void`, `Finish() -> ImmutableArray<MacroAction>`.

- [ ] Add failing capture-stream assertions: selected-target only; injected/hotkey events omitted; 1000ms gap becomes exactly one DelayAction; focus pause of 20 seconds adds no delay; held Ctrl/mouse at focus loss creates balanced saved releases without physical input injection; resumed held-up without a recorded down is ignored; target closure keeps balanced partial macro.

```csharp
// Fixture captures two target clicks separated by 1000ms; focus-pause case is separate.
Assert.Equal(TimeSpan.FromMilliseconds(1000), Assert.Single(actions.OfType<DelayAction>()).Duration);
Assert.Empty(physicalInputInjections);
Assert.Equal(beforePauseDelayTotal, afterPauseDelayTotal); // 20s pause excluded.
```

- [ ] Run `dotnet test tests/Macrofy.Core.Tests --filter RecordingTests`; expect missing recording coordinator/builder failures.
- [ ] Implement Windows low-level mouse/keyboard hooks on a dedicated message thread, fast nonblocking callback queues, injected flags, foreground-target filtering, DPI-aware point conversion, drag capture started in target, and overflow handling that stops capture with recoverable partial recording. Always pass physical input through.
- [ ] Group recorded click/hold/drag/shortcut sequences while retaining raw event order. Bind capture to same token throughout and exclude registered control-key sequences including their releases. UI start enters an Armed state until user activates chosen target; no actions or elapsed delays are captured while armed. Stop cancels arming. Hotkey start requires target foreground.
- [ ] Run recorder tests. In controlled target manually record clicks, wheel, drag, Ctrl+A, held key, focus switch, and stop. Inspect recorded pairs/timing and verify real target still receives input normally.
- [ ] Commit as `feat: record target input and editable timing`.

## Task 8: Global hotkeys, access, and system lifecycle

**Files:** Create `src/Macrofy.Platform.Windows/Interop/HotkeyNative.cs`, `WindowsGlobalHotkeys.cs`, `WindowsSystemEvents.cs`, `src/Macrofy.Core/Playback/ControlCommands.cs`, `tests/Macrofy.Platform.Windows.Tests/HotkeyTests.cs`, `tests/Macrofy.Core.Tests/ControlCommandTests.cs`.

**Consumes:** Task 1 hotkey/system/permission contracts, Tasks 6/7 session controls.
**Produces:** Windows hotkey/system implementations; `ControlCommands.ExecuteAsync(HotkeyCommand, CancellationToken) -> Task<CommandResult>` shared by UI buttons and hotkeys.

- [ ] Add failing assertions for conflicting bindings, failed emergency-stop registration blocking playback, proposed F8/F9/F10 defaults, atomic reconfiguration preserving old bindings after failure, selected-macro F9 control and F10 stopping all sessions from every active state across profiles.

```csharp
Assert.Equal(new[] { "F8", "F9", "F10" }, defaultBindingDisplayNames);
Assert.Equal(previousBindings, activeBindingsAfterFailedReconfigure);
Assert.Empty(nativeSendsWhenEmergencyStopUnavailable);
```

- [ ] Run both filtered hotkey/control test sets; expect missing implementations.
- [ ] Register global shortcuts on dedicated/message-window thread, marshal commands into coordinator, disable repeat-trigger duplication, diagnose permission mismatch, and release registrations/hooks on disposal. Receive Windows power-suspend notifications through the lifecycle service.
- [ ] Verify hotkeys work while another app is foreground, recordings omit control sequences, and unexpected shutdown best-effort cancels/cleans active session before services dispose.
- [ ] Commit as `feat: add configurable hotkeys and lifecycle control`.

## Task 9: Background/minimized compatibility workflow

**Files:** Create `src/Macrofy.Core/Profiles/CompatibilityService.cs`, `tests/Macrofy.Core.Tests/CompatibilityTests.cs`; consume `src/Macrofy.Core/Profiles/CompatibilityRecord.cs` from Task 5.

**Consumes:** Task 1 delivery/target/live-context contracts, Task 3 observed-probe results, Task 5 storage, Task 6 exclusive activity lease.
**Produces:** `CompatibilityService.TestAsync(TargetWindow, CompatibilityTestSequence, TargetState, CancellationToken) -> Task<CompatibilityAttempt>`; `Confirm(CompatibilityAttempt, bool observedSuccess) -> CompatibilityRecord`; `Check(MacroSnapshot, TargetWindow) -> CompatibilityCheck`.

- [ ] Add failing `QueuedMessageNeedsUserConfirmation`, `BackgroundResultDoesNotEnableMinimized`, `ClickTestDoesNotProveKeyboard`, `ChangedTargetInvalidatesConfirmation`, and `RejectedDeliveryCannotBeConfirmed` assertions. Status wording distinguishes Sent from Observed working.

```csharp
Assert.True(queuedDelivery.Queued);
Assert.Empty(confirmedRecordsBeforeUserConfirmation);
Assert.DoesNotContain(recordsAfterBackgroundClickTest, r => r.State == TargetState.Minimized);
Assert.DoesNotContain(recordsAfterBackgroundClickTest, r => r.Capability == InputCapability.Key);
```

- [ ] Run `dotnet test tests/Macrofy.Core.Tests --filter CompatibilityTests`; expect unimplemented workflow failures.
- [ ] Implement user-selected validated test sequence with exclusive lease, bounded target-only cleanup, per-state/per-capability records, target identity/surface fingerprint, explicit observed confirmation, and invalidation on change. Do not activate/restore/minimize target automatically during test; user puts it in desired state.
- [ ] Run tests and reuse actual-game observations from Task 3. Explain ignored-but-queued commands as unsupported or unconfirmed, not proof of success. Persist only confirmed results plus bounded last attempt/error metadata.
- [ ] Commit as `feat: confirm per-target background compatibility`.

## Task 10: Desktop tabs, profile macro overview and macro workspace

### Delivered UI-first subset (2026-09-29)

- [x] Add saved Enabled switches and hover feedback in Profiles, plus Run all for enabled macros in the selected profile. Batch preview skips active/paused sessions, invalid steps/targets and pending drafts. Toggling changes batch inclusion without stopping existing sessions.
- [x] Group Run all, Pause/Resume all and Stop all in the footer; add persisted, configurable F1–F12 shortcuts (F9/F8/F10 defaults) while Macrofy is focused. System-wide hotkeys remain in Task 8.

- [x] Replace the foundation placeholder with native Profiles, Apps, Macros, Compatibility, Settings, Log and About tabs; use icon controls with tooltips/accessibility names and shared profile/Stop/status context.
- [x] Implement profile/macro creation, rename/duplicate/delete confirmation; per-profile saved app add/edit/delete protection; remembered per-macro app bindings or explicit Screen default; action editing/validation/reordering and independent repeat/interval settings.
- [x] Persist valid UI workspace and independent appearance/palette choices atomically, keep backups, preserve unsupported/corrupt files and reject overwrites by stale app instances. Keep unapplied/invalid drafts through navigation/theme changes in the current window.
- [x] Demonstrate concurrent independent preview sessions, cross-profile Pause/Resume/Stop all, active editor locks and session snapshots. These sessions send no input. Real recording/playback/global hotkeys remain pending.
- [x] Publish self-contained `artifacts/win-x64/Macrofy.exe` with ten palettes, Light/Dark and the separate compatibility probe bundled. Full Task 10/11 backend integration and release checks below remain open.

**Files:** Create `src/Macrofy.App/App.axaml`, `App.axaml.cs`, `Program.cs`, `Views/MainWindow.axaml`, `ProfilesView.axaml`, `TargetPickerView.axaml`, `MacroEditorView.axaml`, matching view models, `Services/AppComposition.cs`, `DialogService.cs`, `tests/Macrofy.App.Tests/ProfilesUiTests.cs`, `EditorViewModelTests.cs`.

**Consumes:** Tasks 2/4/5/6/7/8/9 services and command results.
**Produces:** `MainWindowViewModel` with Profiles/Apps/Macros/Compatibility/Settings/Log/About tabs and selected-tab state; `ProfilesViewModel.SelectedProfile`, `.SelectedMacro`, `.ActivityStatus`; `MacroEditorViewModel.Draft`, `.Errors`, `.ApplyEdit(EditOperation)`; common Command service from Task 8. Profiles shows every macro in the selected profile. Macros uses Design 2's workspace with a macro list, action sequence, action inspector and playback settings. Shared profile selection and Stop/status survive tab changes.

- [ ] Add failing headless assertions: Profiles shows all selected-profile macros with app assignments, action counts and session status; row Run/Pause/Resume/Stop commands address only that macro; multiple rows can run concurrently, including on one app; shared Pause all/Resume all/Stop all controls include sessions in other profiles. Opening a macro selects it and opens Macros; workspace displays the selected editor; switching selected macro restores its actions and saved app binding and resets action selection; profile selection updates visible macros and saved app list; reopening restores multiple app rules and per-macro assignments. Active macros are read-only while browsing and starting other eligible macros remain available; invalid draft cannot run or overwrite saved version; save failure keeps dirty indicator. View model recorder-start waits for target focus if initiated through UI.

```csharp
Assert.Equal(selectedMacro.Id, editor.MacroId); // Workspace opens the selected macro directly.
Assert.NotEmpty(editor.Errors); // Invalid coordinate/delay draft fixture.
Assert.Equal(lastValidSavedActions, persistedMacro.Actions);
```

- [ ] Run `dotnet test tests/Macrofy.App.Tests --filter 'FullyQualifiedName~ProfilesUiTests|FullyQualifiedName~EditorViewModelTests'`; expect missing view-model failures.
- [ ] Implement the user-requested desktop tab bar resembling the DS4Windows reference, with separate Profiles, Apps, Macros, Compatibility, Settings, Log and About content. Profiles shows all macros in the selected profile and opens them in Macros. Use Design 2's workspace inside Macros: macro list left, selected macro/target above the sequence, action inspector right and playback settings below. Provide common profile selection, profile management, per-profile saved app list and Add/Edit app rules, persistent macro list, per-macro saved app assignment with Screen default and Record/Run/Stop controls. Restore saved targets after reopening; show unavailable/ambiguous app status without retargeting. Verify tab switches preserve selected profile/macro/app and shared Stop/status visibility during recording/playback.
- [ ] Add editor fields by action type, group expansion, move up/down or reorder controls, add/delete/duplicate actions, fixed/percentage selector, loop toggle/interval, text and raw key/shortcut input, and background-test confirmation dialog. Wire valid autosave, explicit replacement/deletion confirmations, and profile/macro rename/duplicate/new operations.
- [ ] Verify UI with headless tests and manual Windows session: profile macro row controls work independently during concurrent playback; same-app macros can run together; Pause/Resume retains session position; Stop all works across tabs/profiles; active macro edits are disabled while idle macro browsing remains available. Target dropdown/list works, recorded actions appear, edited values replay, status shows current action/next loop/skipped count and disabled control reasons are readable.
- [ ] Commit as `feat: build desktop tabs and macro workspace UI`.

## Task 11: Settings, About, and portable delivery

**Files:** Create `Views/SettingsView.axaml`, `AboutView.axaml`, associated view models, `Services/ThemeService.cs`, `src/Macrofy.App/Properties/PublishProfiles/PortableWindows.pubxml`, `scripts/verify.ps1`, `scripts/publish-windows.ps1`, `README.md`, `docs/verification/release-checklist.md`, `tests/Macrofy.App.Tests/SettingsTests.cs`.

**Consumes:** Task 5 settings/storage; Task 8 hotkey results; Task 10 UI composition.
**Produces:** reproducible self-contained `artifacts/win-x64/Macrofy.exe`; settings/theme persistence and About data-folder/version information.

- [ ] Add meaningful Settings tests: hotkey-registration failure retains previous saved config; appearance mode and palette survive restart as separate settings; switching Light/Dark preserves the palette and profile/macro selection; theme and defaults survive restart; permission/error state does not hide emergency Stop. Run to verify expected failures, then implement settings logic.

```csharp
Assert.Equal(previousSettingsBytes, settingsBytesAfterRejectedHotkeys);
Assert.Equal("Dark", reloadedThemeDisplayName);
Assert.True(stopControlVisibleDuringError);
```

- [ ] Implement editable hotkeys and appearance mode independently of the selected color palette. Provide Light/Dark switching as requested; retain System support where planned. Each approved palette supports both modes with legible foregrounds and synchronized controls. Keep interface copy concise, using labels and short status/error feedback rather than explanatory paragraphs. Implement About version/license/data-folder display and Open folder control. Disable unsupported-platform features and identify Mac support as planned in About.
- [ ] Use icons for navigation tabs and common controls, with tooltips and accessible names. Pause/Resume must update both glyph and name. Preserve text for profile/macro names, form labels, data and status.
- [ ] Add publish profile with `RuntimeIdentifier=win-x64`, `SelfContained=true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `PublishTrimmed=false`; ship native dependencies in the executable and keep profile data beside executable. Add supported-OS/DPI manifest using documented Windows settings.
- [ ] `verify.ps1` runs restore locked mode, Release build, core/App tests, and serial Windows integration checks; exits nonzero on real failures and reports explicit prerequisites/manual checks. `publish-windows.ps1` runs publish and validates produced file/version; no installer, startup task, or download helper.
- [ ] Run `dotnet test Macrofy.sln -c Release` and `powershell -File scripts/publish-windows.ps1`. Verify clean-machine Windows 10/11 launch without .NET, DPI/multimonitor/resize, read-only folder error, save/restart recovery, target close, emergency stop, and intended-game background/minimized input. Record results and unavailable hardware honestly.
- [ ] Commit release work as `feat: deliver portable Windows Macrofy`. Give user executable path and measured compatibility limits; do not label Mac functionality complete.

## Coverage and handoff

| Spec area | Owning tasks |
| --- | --- |
| Scope, architecture, future Mac separation | 1, 11 |
| Window list/process/title matching, permissions | 2, 8 |
| Actual background/minimized game feasibility | 3, 9 |
| Action editing, coordinates, grouping | 4, 10 |
| Persistence, recovery, save errors | 5, 10 |
| 0/60/120 scheduling, skip, sleep, cancellation | 6, 8 |
| Recording, focus loss, hotkey exclusion | 7, 8 |
| Desktop tabs, profile macro overview, macro workspace and status | 10, 11 |
| Portable executable and release verification | 11 |

Review fixes and phased inline execution approved. Tasks 1-3 are authorized now; full app follows the actual-game compatibility gate. The user named CookieRun and the running window was observed as CookieRun: Crumble - Idle RPG. Task 3 still requires a user-selected harmless input test; unknown compatibility does not prevent writing this plan but does prevent claiming a verified game-compatible release.

References: [Avalonia native interop](https://docs.avaloniaui.net/docs/app-development/native-interop), [Windows PostMessage](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew), [.NET lifecycle](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support). Direct dependency versions were checked against NuGet package indexes on 2026-09-28.
