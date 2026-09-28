# Macrofy design

## 1. Purpose and agreed scope

Macrofy is a simple desktop app for recording, editing, and repeatedly replaying mouse and keyboard actions against one chosen app or game window. The user must remain able to use their physical mouse and keyboard in other applications during playback.

The first release targets Windows 10 and Windows 11, x64. Google Play Games on PC is the primary real-world compatibility target. A window list also allows selecting other running applications and games. Compatibility depends on the target accepting background input; universal game support is not a promise.

The user's first selected test game is CookieRun: Crumble - Idle RPG, observed as a running Google Play Games window during planning. No background/minimized compatibility is claimed until the actual input test.

The selected stack is C# with Avalonia. UI, macro data, editing, scheduling, and storage are shared components. macOS support for native Mac apps and games will be implemented later, when Mac hardware is available for building and testing. The Windows release does not include an untested Mac input engine. Google Play Games on PC is a Windows target, not a Mac target.

Final source workspace selected by the user: `H:\MyProjects\Apps\AutoClickerMacrofy`. Design began in `C:\Users\Zero\Documents\AutoClickerMacrofy`; its committed history was copied into the new, empty repository. Project paths must not be embedded into macro files.

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

The core scheduler uses an injectable monotonic clock, cancellation, and a frozen macro snapshot. Platform callbacks enqueue input events without blocking the Windows hook thread or UI thread.

## 4. Profiles, macros, and target selection

Each profile contains a name, application identity, saved window-title pattern, and multiple named macros. Macros can be created, renamed, duplicated, deleted, and edited.

On Windows, the target rule combines process/executable identity with a simple case-insensitive window-title wildcard pattern. The picker shows actual process and window names; example names in mockups are illustrative.

The selectable list includes user-facing top-level windows, including minimized windows. Refreshing the list does not activate windows. Child input surfaces can be resolved internally by the Windows backend when required.

- No match: playback cannot start; show target unavailable.
- Multiple matches: require selecting one matching window before playback; never silently choose the first.
- A profile can resolve a new window after an app restart for a new manual run.
- A running session stays bound to its resolved target identity. If the window/process disappears, stop immediately; do not transfer input to a replacement or reused handle.
- Only one macro can record or play across the entire application at a time.

## 5. Action model and editing

The canonical ordered event sequence supports:

- Delay, in milliseconds.
- Mouse button down and up, with button and coordinates.
- Mouse move, with coordinates.
- Vertical/horizontal wheel action, with delta and coordinates.
- Key down and up, retaining logical key and platform-specific physical information where available.
- Type Text, an explicit editable string action.

Click, hold, drag, key press, and shortcut are editable groups over these events. Groups expand into their constituent events, allowing independent down/up timing and overlapping key/button holds. Recording initially produces raw key events; it does not automatically replace gameplay key presses with text.

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

Windows playback sends targeted mouse/keyboard/character messages to the selected input window. It does not use global `SendInput`, cursor movement, foreground activation, focus stealing, drivers, or injection as fallbacks.

Background and minimized operation are attempted only for compatible targets. A target may ignore messages, use a different input path, or stop updating while minimized. These conditions cannot always be detected automatically.

### Compatibility test

Each profile has a Test Background Input workflow. The user selects a deliberate test action in a harmless target context. The test sends that action while the user observes whether it worked, without activating the target. The user then confirms success or marks the target unsupported. Background and minimized results are recorded separately, and the UI identifies which action types were checked.

Playback requires a successful user-confirmed test for the requested target state and required action capabilities. A process/title/input-surface change invalidates the corresponding result; a target update can also require retesting. A confirmed test is compatibility evidence, not an indefinite guarantee.

- Delivery error, missing permission, unsupported action, lost target, or missing geometry: stop and show a specific reason.
- Messages accepted but ignored: Macrofy cannot claim successful gameplay. The user can stop and mark compatibility unsupported.
- No automatic foreground or physical-input fallback is offered.

The emergency-stop handler cancels queued future actions, then makes a bounded best-effort release of macro-held keys/buttons to the same target. It never sends cleanup to another window if the original disappeared. Failed cleanup is reported; the physical keyboard/mouse state is not modified.

## 8. Scheduling

The default interval is 60 seconds and is editable per macro. Manual single-run playback is also available.

For interval `I`, session start defines `t0`. Planned loop starts occur at `t0`, `t0 + I`, `t0 + 2I`, and so on. The first run starts immediately.

- Each run replays the frozen action snapshot and its recorded/customized delays.
- Runs never overlap. If a scheduled boundary arrives while a run is active, skip that boundary.
- Example: a 70-second run on a 60-second interval starts at 0 seconds, skips 60, and next starts at 120.
- Skipped runs are never queued or replayed as a catch-up burst.
- Use monotonic elapsed time, not wall-clock time, to avoid clock adjustments affecting the schedule.
- Machine sleep cancels the current run. On resume, stop the session and require a new manual start; do not burst overdue input into the target.
- Stop cancels both active playback and future loops. A new start establishes a fresh `t0`.

## 9. UI design

The main window uses a vertical layout. Top-level tabs are Profiles, Settings, and About.

### Profiles tab

From top to bottom:

1. Profile dropdown and Manage profiles control.
2. Target identity and connection/compatibility status.
3. Visible list of the selected profile's macros.
4. Record, Run selected, and Stop controls with current state.
5. Selected macro's Edit section, collapsible and collapsed by default.

The editor expands to show loop settings, coordinate mode, action list, action-detail editing, and background-input test access. Selecting another macro resets the editor to collapsed. Profile and macro lists remain visible when editing expands; no sidebar is required.

The Record and Run controls are disabled where prerequisites are missing, with a short reason. Stop remains prominent during recording/playback. During activity, switching target/profile/macro and changing the active sequence are disabled to avoid applying edits to a running session.

Status shows Idle, Recording, Recording paused, Running, Waiting for next loop, or Error, with the active macro, current action, next-loop countdown, and skipped-run count where relevant.

### Settings tab

Configure global hotkeys and select light, dark, or system theme. Proposed defaults: F8 recording, F9 playback, F10 emergency stop. A conflicting/unregistered emergency-stop binding disables playback until corrected. Hotkeys are customizable and persist locally.

### About tab

Show app name/version, Windows-first release scope, data-folder location/open control, and dependency/license acknowledgements. Mac support is described as planned until delivered and tested.

### Persistence in UI

Valid edits save automatically using atomic writes. Show save failures and unsaved state clearly. Invalid drafts remain visible for correction and do not replace the last valid persisted macro. Deleting a profile or macro requires confirmation; duplicating provides a simple way to preserve a version before editing.

The approved preview direction is a vertical profile dropdown and macro list, with the selected editor collapsed initially. The browser companion is a design artifact, not the eventual application runtime.

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
- Vertical UI, dropdown, macro list, collapsed editor, editing, and status/countdown behavior.
- Portable package starts without installed .NET, persists data beside executable, and reports a read-only folder clearly.

Mac verification is a separate future milestone on real Mac hardware. The Windows release must not advertise functional Mac recording/replay.

## 12. Reference material

- Microsoft PostMessageW: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew
- Google Play Games mouse input: https://developer.android.com/games/playgames/input-mouse
- Google Play Games PC requirements: https://support.google.com/googleplay/answer/11358071?hl=en
- Avalonia supported platforms: https://docs.avaloniaui.net/docs/supported-platforms
- .NET macOS publishing: https://learn.microsoft.com/en-us/dotnet/core/deploying/macos

## 13. Design status and next step

User approved architecture, recording/replay model, vertical tabs/dropdown/list layout with collapsed editor, and reliability/testing direction. This written document consolidates those decisions for review.

The user approved the written spec and selected the final source folder. Create the implementation plan there. Application implementation follows plan review and execution-method selection.
