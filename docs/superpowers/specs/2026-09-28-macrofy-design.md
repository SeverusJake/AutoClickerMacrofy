# Macrofy design

## 1. Purpose and agreed scope

Macrofy is a simple desktop app for recording, editing, and repeatedly replaying mouse and keyboard actions against one chosen app or game window. The user must remain able to use their physical mouse and keyboard in other applications during playback.

The first release targets Windows 10 and Windows 11, x64. Google Play Games on PC is the primary real-world compatibility target. A window list also allows selecting other running applications and games. Compatibility depends on the target accepting background input; universal game support is not a promise.

The user's first selected test game is CookieRun: Crumble - Idle RPG, observed as a running Google Play Games window during planning. No background/minimized compatibility is claimed until the actual input test.

The selected stack is C# with Avalonia. UI, macro data, editing, scheduling, and storage are shared components. macOS support for native Mac apps and games will be implemented later, when Mac hardware is available for building and testing. The Windows release does not include an untested Mac input engine. Google Play Games on PC is a Windows target, not a Mac target.

Final source workspace selected by the user: `H:\MyProjects\Apps\AutoClickerMacrofy`. Design began in `C:\Users\Zero\Documents\AutoClickerMacrofy`; its committed history was copied into the new, empty repository. Project paths must not be embedded into macro files.

### Approved clarification (2026-09-28)

Full macro features remain in the application scope for future games. The current CookieRun workflow needs clicks only. Minimized playback is preferred if compatible; non-minimized background playback is the acceptable fallback. Distinguish visible, partly covered and fully covered background observations. If both minimized and background clicks fail, pause after the compatibility probe and review options before full application implementation. Hidden/tray and locked-screen operation are outside the initial scope.

## 2. Delivery

- Windows delivery is a self-contained portable executable, with no installer or separate .NET installation required. Packaging must include required native dependencies; any runtime extraction is tested on a clean machine.
- Data resides in `MacrofyData` beside the executable. If that location is not writable, show a clear storage error rather than silently save elsewhere.
- Native dependencies and publish settings are pinned and verified during implementation planning. The runtime is a supported .NET LTS release compatible with the agreed Windows versions.
- Later macOS delivery is a separate application bundle, with platform permissions and signing addressed and tested during the Mac phase.
- No account, cloud synchronization, automatic launch at login, driver installation, or game-process injection is part of this release.

## 3. Architecture

### Projects

| Project | Responsibility |
| --- | --- |
| `Macrofy.App` | Avalonia views, view models, UI composition, user commands |
| `Macrofy.Core` | Profile/macro model, editor operations, recorder coordination, scheduler, validation, JSON persistence |
| `Macrofy.Platform` | OS-neutral contracts and capability/error types |
| `Macrofy.Platform.Windows` | Window enumeration, matching, recording hooks, hotkeys, geometry, and targeted message playback |
| `Macrofy.Core.Tests` | Data, editing, recording coordination, persistence, and scheduling tests |
| `Macrofy.Platform.Windows.Tests` | Windows integration checks against a controlled test target |

A future `Macrofy.Platform.MacOS` implements the platform contracts. Shared code must not depend on Windows handles, virtual-key definitions, screen-coordinate conventions, or Windows executable paths.

### Platform contracts

- `IWindowCatalog`: enumerate selectable windows, expose title/application identity/state, and resolve a saved target rule.
- `IInputRecorder`: observe physical input and emit platform-neutral events with target-relative coordinates and monotonic timestamps.
- `IInputPlayer`: send supported actions to the selected target without changing foreground focus or physical cursor position; report delivery errors and unsupported action types.
- `IGlobalHotkeys`: register configurable record, replay, and emergency-stop bindings, with explicit collision/failure results.
- `IPermissionService`: report missing access or incompatible privilege levels; provide a future boundary for macOS permissions.
- Geometry/capability information is exposed through these contracts, not inferred by UI code from OS-specific structures.

Each macro playback session uses an injectable monotonic clock, its own cancellation/pause state, and a frozen macro snapshot. A session manager supports concurrent macros, including macros bound to the same app, as requested on 2026-09-29. Platform callbacks enqueue input events without blocking the Windows hook thread or UI thread.

## 4. Profiles, macros, and target selection

Each profile contains a name, multiple saved target apps, and multiple named macros. Each saved app has a stable ID, display name, application identity, and window-title pattern. User clarification (2026-09-29): a profile must support many apps and remember its target-app configuration after reopening. Macros can be created, renamed, duplicated, deleted, and edited.

Saved app entries persist locally with the profile, even while those apps are closed. At each new manual run, resolve a saved rule to the current live window. Persist executable/application identity and title rules, never process IDs, HWNDs, or session target tokens. Keep separate compatibility observations for each saved app and input surface. No chosen app means explicit Screen mode; a configured app that is missing does not become Screen mode.

User-approved assignment (2026-09-29): each macro uses one saved app from its profile. Persist that binding with the macro and restore it when the macro is selected. No assigned app means Screen mode. A macro does not switch apps between actions. Multiple macros can use the same saved app; different macros in the profile can use different apps. A missing saved-app reference is a validation error, never a Screen fallback. The user subsequently requested concurrent playback, including on the same app; recording remains exclusive.

On Windows, the target rule combines process/executable identity with a simple case-insensitive window-title wildcard pattern. The picker shows actual process and window names; example names in mockups are illustrative.

The selectable list includes user-facing top-level windows, including minimized windows. Refreshing the list does not activate windows. Child input surfaces can be resolved internally by the Windows backend when required.

- No match: playback cannot start; show target unavailable.
- Multiple matches: require selecting one matching window before playback; never silently choose the first.
- A profile can resolve a new window after an app restart for a new manual run.
- A running session stays bound to its resolved target identity. If the window/process disappears, stop immediately; do not transfer input to a replacement or reused handle.
- Multiple distinct macros can play concurrently, including on one app. Recording and compatibility tests require exclusive input activity. Each macro has at most one active playback session.

## 5. Action model and editing

The canonical ordered event sequence supports:

- Delay, in milliseconds.
- Mouse button down and up, with button and coordinates.
- Mouse move, with coordinates.
- Vertical/horizontal wheel action, with delta and coordinates.
- Key down and up, retaining logical key and platform-specific physical information where available.
- Type Text, an explicit editable string action.

Click, hold, drag, key press, and shortcut are editable metadata groups referencing stable IDs in the canonical flat event sequence. Groups expose their referenced events without changing raw order, allowing independent down/up timing and overlapping key/button holds. Recording initially produces raw key events; it does not automatically replace gameplay key presses with text.

The editor shows action order, action type, applicable coordinates/button/key/text, and timing. Users can add, remove, duplicate, reorder, and change actions. Recorded gaps become Delay actions; timings shown elsewhere are derived from the same sequence, avoiding two competing sources of timing truth. Drag movements are grouped into a collapsed row and can be expanded and edited.

Validation rejects negative/non-finite delays, invalid coordinates, invalid keys/buttons, and unbalanced press/release sequences before playback. A malformed group is shown with an actionable error rather than repaired silently. Empty macros cannot run.

### Coordinates

Each macro chooses one coordinate mode:

1. Fixed pixels relative to the target client area.
2. Percentage positions relative to the target client area.

Recording saves client dimensions and the geometry needed for conversion. Switching modes converts existing coordinates using recorded dimensions. Playback rejects points outside the current client area. Drag events captured outside that area remain visible in the recording but require coordinate correction before playback. Percentage mode scales points to current client dimensions; it does not recognize moved UI elements, letterboxing, or game-layout changes.

DPI and screen-to-client conversions are handled in the platform backend. Minimized playback uses reliable current or cached client geometry for the bound target; if reliable geometry cannot be established, stop with a geometry error.

## 6. Recording

The target must be active when recording begins. Recording observes input while the user's actions continue to reach the target normally. UI buttons and configurable global hotkeys can start and stop recording.

A UI start may arm recording while Macrofy has focus. Capture and its timing begin only after the user activates the chosen target; no foreground activation is performed by Macrofy. Stop also cancels the armed state.

- Capture mouse clicks, holds, drags, scrolling, keyboard down/up events, and timing.
- Ignore Macrofy control hotkeys and distinguish injected events from physical events to avoid self-recording.
- Capture mouse input within the selected client area, plus continued movement/release belonging to a drag begun there. Keyboard capture requires selected target focus.
- Pause recording when target loses focus. Other applications' input is excluded, and paused time is excluded from macro delays.
- At a focus transition or recording end, close any recorded held states in the macro with paired release events. This balances the saved sequence without injecting releases into the user's live input.
- Resume capture when the same target regains focus, starting from a fresh held-state baseline.
- If target disappears, end capture and keep already captured, balanced actions for review.
- New recordings create a new macro or explicitly replace the selected macro after an overwrite confirmation. Existing recordings are not silently lost.

Keyboard shortcuts are editable down/up sequences. Type Text can be added manually; it is supported only where the target accepts character messages. Input methods and arbitrary game text fields are not guaranteed compatible.

## 7. Background playback and compatibility

Windows window playback sends targeted mouse/keyboard/character messages to the selected input window. It does not use global `SendInput`, cursor movement, foreground activation, focus stealing, drivers, or injection as fallbacks.

User scope update (2026-09-29): no chosen window means Screen mode by default. The early probe now offers an explicit Screen selection, screen pointer capture and one left click after a three-second countdown. This mode uses SendInput and moves the real pointer; the intended content must be visible. Selecting a window uses the existing targeted backend. A lost/unavailable selected window never falls back to Screen mode. Switching modes clears coordinates. Screen injection results do not establish minimized/background compatibility. Full screen macros/recording remain later work after the user chooses a UI concept.

Background and minimized operation are attempted only for compatible targets. A target may ignore messages, use a different input path, or stop updating while minimized. These conditions cannot always be detected automatically.

### Compatibility test

Each profile has a Test Background Input workflow. The user selects a deliberate test action in a harmless target context. The test sends that action while the user observes whether it worked, without activating the target. The user then confirms success or marks the target unsupported. Background and minimized results are recorded separately, and the UI identifies which action types were checked.

Playback requires a successful user-confirmed test for the requested target state and required action capabilities. A process/title/input-surface change invalidates the corresponding result; a target update can also require retesting. A confirmed test is compatibility evidence, not an indefinite guarantee.

- Delivery error, missing permission, unsupported action, lost target, or missing geometry: stop and show a specific reason.
- Messages accepted but ignored: Macrofy cannot claim successful gameplay. The user can stop and mark compatibility unsupported.
- No automatic foreground or physical-input fallback is offered.

The emergency-stop handler cancels future actions for every playback session; already-posted Windows messages cannot be withdrawn. A shared serial dispatcher uses no send-ahead queue and limits posts to 100 native messages/second in aggregate with a burst of one. Concurrent sessions preserve their own action order while actions from different macros may interleave on the same target. Track held-input ownership per session and live target; stopping one session releases only its own ownership and must not release a key/button still owned by another session. A bounded 500ms best-effort cleanup targets the same live identity. It never sends cleanup to another window if the original disappeared. Failed cleanup is reported; window-mode cleanup does not modify the physical keyboard/mouse state. Screen sessions share a global dispatcher for physical input, so pointer movement and keyboard actions from those sessions may also interleave.

## 8. Scheduling

The default interval is 60 seconds and is editable per macro. Manual single-run playback is also available.

For interval `I`, session start defines `t0`. Planned loop starts occur at `t0`, `t0 + I`, `t0 + 2I`, and so on. The first run starts immediately.

- Each run replays the frozen action snapshot and its recorded/customized delays.
- Runs of the same macro never overlap, and a macro cannot start a second session while running or paused. Different macros can run concurrently, including on the same app. If a scheduled boundary arrives while that macro's run is active, skip that boundary.
- Example: a 70-second run on a 60-second interval starts at 0 seconds, skips 60, and next starts at 120.
- Skipped runs are never queued or replayed as a catch-up burst.
- Use monotonic elapsed time, not wall-clock time, to avoid clock adjustments affecting the schedule.
- Machine sleep cancels the current run. On resume, stop the session and require a new manual start; do not burst overdue input into the target.
- Stop cancels both active playback and future loops. A new start establishes a fresh `t0`.
- Each macro has independent Run, Pause/Resume and Stop controls. Pause completes at a safe action boundary after the session has no held inputs; show Pausing until that boundary. Paused time is excluded from remaining delays and the session's scheduling clock. Resume preserves sequence position and the original live target binding; revalidate it and stop on target loss rather than rebinding.
- Stop all and F10 cancel every running, pausing, paused or waiting session across profiles. Machine sleep also stops every session. Closing a target stops all sessions bound to that identity while unrelated targets continue.
- Recording and deliberate compatibility tests remain exclusive activities: they cannot start while any playback session is active or paused, and playback cannot start while either exclusive activity owns input.

## 9. UI design

User UI update (2026-09-29): the main window uses a horizontal desktop tab bar like the provided DS4Windows screenshot, with adjacent bordered tabs rather than a sidebar. Top-level tabs are Profiles, Apps, Macros, Compatibility, Settings, Log, and About. Selected profile context stays visible above each tab; shared activity status and Stop remain visible below every tab. Switching tabs does not switch the selected profile, macro or target app.

### Profiles tab

Create, select, rename, duplicate and manage profiles. Each profile owns a collection of saved apps and named macros. Choosing a profile immediately shows all of its macros, including their assigned app, step count and independent playback status. Label the count column Steps, as approved by the user; it counts the macro sequence once, regardless of repeat count, and does not count native message posts. Each row provides Run, Pause/Resume, Stop and Edit macro. Multiple rows may run together, even on the same app; no profile-wide run is required. Opening a macro from this list selects it and opens the Macros tab. A common profile picker preserves this context across tabs.

### Apps tab

Show the selected profile's remembered app identities, executable paths, title patterns and individual connection status. Add/Edit app controls configure multiple apps for the same profile. Closing an app leaves its configuration saved; a new run resolves the current window from its saved rule.

### Macros tab

Use the previously presented Design 2 macro workspace within this tab: the selected profile's macro list on the left, selected macro name and target above the action sequence, action details on the right, and repeat/interval/playback controls below. Screen is the default for no assigned app. Provide Record, Run selected, Stop, loop settings, coordinate mode, action list and action-detail editing. Show the selected editor directly; choosing another macro restores that macro's sequence and target assignment and resets action selection. At narrow widths, place the macro list above the editor and action details below the sequence.

### Compatibility tab

Show separate user-observed click/key/etc. results for the selected saved app and window state. Test only a deliberate harmless sequence. Screen input does not qualify as background or minimized compatibility evidence.

The Record and Run controls are disabled where prerequisites are missing, with a short reason. Stop remains prominent during recording/playback. A macro's actions, target and run settings are read-only while its session is active or paused. Profile/macro browsing and starting other eligible macros remain available during playback. Prevent deleting active macros or changing/deleting their referenced app rules. Recording and compatibility tests lock their relevant context. Shared Pause all/Resume all and Stop all controls remain visible on every tab, with aggregate running/paused counts; each row's Stop affects only that macro.

Per-session status shows Idle, Recording, Recording paused, Running, Pausing, Paused, Waiting for next loop, Stopped or Error, with macro name, current action, next-loop countdown and skipped-run count where relevant. Aggregate status includes sessions in other profiles.

### Settings tab

Configure global hotkeys and select light, dark, or system theme. Proposed defaults: F8 recording, F9 playback, F10 emergency stop. A conflicting/unregistered emergency-stop binding disables playback until corrected. Hotkeys are customizable and persist locally.

Color palette review (2026-09-29): the user requested five options. The browser preview offers Windows Blue (light), Graphite (dark neutral gray), Emerald (dark teal green), Violet (dark purple) and Warm Amber (light cream). Previewing a palette recolors title bar, tabs, selected rows, controls and the workspace without changing navigation or profile data. Use this theme confirms and remembers a browser preview choice. These are candidates; the final native palette is pending the user's selection.

The user then requested five new cyberpunk-style candidates: Neon District (electric yellow/cyan), Synthwave (hot pink/purple), Matrix (terminal green/black), Tron (ice cyan/orange) and Redline (neon red/amber). All use dark surfaces with contrasting accent colors, a title-bar accent line and selected-tab indicators. The preview displays these five choices first and retains the earlier themes in Settings. Browsing the new palettes does not overwrite a previously confirmed choice; Use this theme explicitly records the new choice. The native palette remains pending selection.

The user requested richer palettes rather than only two or three colors. Each cyberpunk candidate now includes seven accent roles plus dark surface tints: title/selection accent, secondary cyan/green/orange, tertiary pink/purple, Run green, Pause amber, Stop red and informational blue. Tabs use distinct accents, and target/action/detail/settings regions use related tinted surfaces. Status retains readable text alongside color, and palette samples show every accent. These expanded palettes remain review candidates.

### About tab

Show app name/version, Windows-first release scope, data-folder location/open control, and dependency/license acknowledgements. Mac support is described as planned until delivered and tested.

### Log tab

Show bounded local activity/error history with timestamps, profile/macro/app context and delivery or cleanup failures. Log messages must distinguish queued input from a user-confirmed game response. Do not record typed text content, credentials, or native handle/session identities in persistent logs.

### Persistence in UI

Valid edits save automatically using atomic writes. Show save failures and unsaved state clearly. Invalid drafts remain visible for correction and do not replace the last valid persisted macro. Deleting a profile or macro requires confirmation; duplicating provides a simple way to preserve a version before editing.

The requested preview direction now uses desktop tabs like the user's DS4Windows example. Profile selection and shared Stop/status remain available across tab changes. The browser companion is a design artifact, not the eventual application runtime.

## 10. Storage and errors

Versioned JSON stores profiles/macros, app settings, and bounded compatibility-test metadata. Persistent identifiers are stable IDs; live window handles are session data only. Macros retain optional OS-specific key/target metadata so future Mac support can identify fields that require remapping rather than silently misinterpret them.

Use atomic temporary-file replacement and retain a last-known-good backup. Corrupt or incompatible data is preserved and reported; offer recovery from backup without overwriting the original. Loaded or edited data is validated before playback.

Error presentation includes the affected target/macro/action and a short corrective step. Errors include permission/integrity mismatch, unavailable/ambiguous target, rejected message delivery, unsupported input, invalid sequence, geometry unavailable, hotkey conflict, and storage failure.

Windows privilege mismatch is diagnosed; normal operation does not automatically elevate the application.

## 11. Verification and delivery criteria

### Early compatibility check

Implement a minimal Windows target-message path before investing in full UI. Use a controlled receiving window and the user's actual Google Play Games game when available. Check clicks, keys, scrolling/drag where applicable, background and minimized states, and unchanged physical cursor/focus. Record actual results; do not generalize from one game to all games.

If the intended game ignores the permitted input path, report incompatibility and revisit scope with the user before expanding the input method. A functioning editor/scheduler alone is not success for the user's game.

### Core tests

- Exact 0/60/120-second scheduling with a fake clock; missed-boundary skipping and cancellation.
- Held-key/button balancing, recording focus changes, hotkey exclusions, and delay preservation.
- Coordinate conversion, grouped-action edits, validation, and frozen playback snapshots.
- Profile/macro persistence, atomic-save failure, corruption, backup recovery, and schema version handling.
- Target resolution ambiguity and disappearance behavior through fake platform contracts.

### Windows integration and manual checks

- Controlled target receives ordered mouse/key/character messages and down/up state.
- Cursor position and foreground window remain unchanged during replay.
- Background/minimized compatibility test records user-confirmed results correctly.
- Emergency stop interrupts a delay/drag/hold and attempts target-specific cleanup.
- Multi-monitor/DPI geometry, resized/minimized windows, target closure, and hotkey conflicts.
- Desktop tabs, profile macro list, Design 2 workspace, editing and status/countdown behavior.
- Concurrent macros on distinct and shared targets; independent Pause/Resume/Stop, safe held-input ownership and Stop all/F10 across profiles.
- Portable package starts without installed .NET, persists data beside executable, and reports a read-only folder clearly.

Mac verification is a separate future milestone on real Mac hardware. The Windows release must not advertise functional Mac recording/replay.

## 12. Reference material

- Microsoft PostMessageW: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew
- Google Play Games mouse input: https://developer.android.com/games/playgames/input-mouse
- Google Play Games PC requirements: https://support.google.com/googleplay/answer/11358071?hl=en
- Avalonia supported platforms: https://docs.avaloniaui.net/docs/supported-platforms
- .NET macOS publishing: https://learn.microsoft.com/en-us/dotnet/core/deploying/macos

## 13. Design status and next step

User approved architecture, recording/replay model and reliability/testing direction. Subsequent 2026-09-29 UI requests supersede the original vertical tabs and collapsed editor: desktop tabs, all macros in Profiles, Design 2's workspace in Macros and per-macro Run/Pause/Resume/Stop. The user explicitly chose simultaneous macros even on the same app. The preview demonstrates these controls; native recording/scheduling/UI implementation remains behind the actual-game compatibility gate.

The user approved the written spec and selected the final source folder. Create the implementation plan there. Application implementation follows plan review and execution-method selection.
