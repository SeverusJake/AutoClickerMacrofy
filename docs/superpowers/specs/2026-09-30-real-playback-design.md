# Real macro playback and reliability

Status: user approved on 2026-09-30. Supersedes UI-only execution for the six improvements requested on 2026-09-30. It does not claim actual CookieRun compatibility.

## Intent and scope

The user approved implementing all six improvements from the code review: real Click/Key actions, accurate scheduling, global hotkeys, complete validation, concurrent macro handling, and useful execution results. Keep the approved tabs, profile controls, action editor, themes, saved workspace, and per-macro target choices.

Success means a saved macro can deliver real Click, Key, Text, Wait, and Wheel actions; run once, a finite number of times, or until stopped; pause/resume; stop promptly with cleanup; and report exact progress or failure. Multiple macros may run together, including on one target. Controlled Windows targets establish delivery evidence; only user observation establishes game compatibility.

Recording, low-level recording hooks, arbitrary hold/drag editing, new visual layouts, Mac playback, drivers, injection, and foreground-stealing fallbacks are outside this change.

## Chosen approach

1. Recommended: a neutral action compiler and asynchronous playback coordinator in Core, shared serialized gesture dispatch, and Windows input/hotkey adapters. The app supplies saved macro snapshots and observes progress. This keeps timing independent of rendering and allows deterministic tests.
2. Extending WorkspaceState.Tick would be smaller but couples delivery to the UI thread and its coarse timer. Reject for real execution.
3. Building the complete recording/hold engine first would cover more of the original long-term specification but adds unrequested work before delivering the current actions. Defer.

Atomic gestures are appropriate because the current editor exposes Click and Key actions rather than independent down/up events. Execute a complete click or key chord without interleaving another gesture. Release held inputs before unlocking dispatch. Thus stopping one macro cannot release another macro's keys. If later independent hold/drag actions are introduced, this model will need explicit per-session ownership.

## Action compilation and validation

Compile a frozen snapshot before starting. Both editor validation and playback use the same compiler so an accepted value cannot acquire different meanings at runtime.

- Click: parse invariant-culture `X, Y`, require finite values, and create a left-button click. Fixed screen pixels support negative monitor coordinates. Validate connected monitor membership at dispatch. Window coordinates must be within current client geometry.
- Percentage coordinates: both components are in 0–100. Screen percentages use the virtual desktop bounding rectangle and then validate monitor membership, including monitor gaps. Window percentages use current client dimensions. Zero maps to the first pixel; 100 maps to the last pixel. Never reinterpret a missing window as Screen.
- Key: case-insensitive supported logical keys and modifier chords, for example `Space`, `Enter`, `Ctrl + K`, and `Ctrl + Shift + A`. Canonicalize aliases. Reject unknown tokens, empty components, duplicate modifiers, multiple nonmodifier keys, and all-modifier chords. Press modifiers in declared order, press/release the final key, then release modifiers in reverse order.
- Text: preserve content, validate Unicode surrogate pairs, and cap each action at 4096 UTF-16 units. Screen text uses Unicode input. Window text uses the existing character-message path and requires confirmed Text compatibility.
- Wait: integer duration from 0 to 600000 ms. No native input.
- Wheel: nonzero signed 16-bit vertical delta. Window mode uses a current valid client point, with client-to-screen conversion in the backend; Screen uses the current physical pointer after monitor validation. Add explicit concise editor help for this behavior.
- Wait after: 0–600000 ms. Repeat: 0 means until stopped, otherwise positive integer. Interval: 0–600000 ms.

Configured control hotkeys are reserved. Reject a Key action containing a reserved function key, and Screen Text containing the matching character for an unmodified printable hotkey if printable hotkeys are added later. Current settings remain unique F1–F12 bindings.

Validate target identity and required capabilities before input and revalidate live target/geometry/state at dispatch. A paused run keeps its original live binding; resume never resolves a replacement window silently.

Stronger action validation must not make an older workspace disappear. Preserve structurally valid stored actions with unsupported values, show their validation error, and block their execution until corrected. Preserve existing corrupt/newer-file and stale-instance safeguards.

## Playback and scheduling

Introduce a playback service with Start, Pause/Resume, Stop, and StopAll, plus immutable observable session snapshots. The UI refresh timer reads snapshots only; it never drives execution.

For this change, preserve the current UI meaning of **Interval between runs**: finish all actions and their Wait-after delays, then wait the configured interval before the next loop. This explicitly supersedes the fixed-cadence scheduling section of the original 2026-09-28 design for this release. The first loop starts immediately. There is no catch-up burst and no overlap within a session.

Use monotonic deadlines for waits. Dispatch latency is not subtracted from a configured Wait-after delay: that delay begins when its action finishes. Paused time is excluded from remaining waits and active elapsed time. No hard real-time guarantees are advertised; Windows and receiver queues introduce latency.

States: Starting, Running, Pausing, Paused, Waiting, Completed, Stopped, Error. Starting includes target resolution and validation and counts as active. Each macro has at most one active session; stopping/restarting waits for its previous cleanup to finish. A run uses immutable actions and settings even if browsing changes elsewhere.

Pause cancels the remaining wait immediately and preserves its remaining duration. During a native gesture it transitions to Pausing until a balanced safe boundary, then Paused. Resume revalidates prerequisites and continues at the next undelivered action or interrupted wait without replaying a completed gesture.

Stop cancels future dispatch and waits immediately. Already delivered input cannot be withdrawn. Release inputs acquired by the current gesture with an independent bounded cleanup token; target cleanup remains limited to 500 ms. Retain delivery and cleanup errors independently. StopAll applies across profiles, including starting, pausing, paused, and interval-waiting sessions.

Window closure stops sessions bound to that original token; unrelated sessions continue. System suspend and app shutdown stop all sessions and dispose native services after bounded cleanup. Resuming Windows never resumes old input automatically.

## Dispatch and Windows adapters

One coordinator owns a shared gesture-dispatch gate across Screen and window playback. Waiting for this gate is cancellable. It does not build a precomputed send-ahead queue. Different sessions may interleave between gestures while preserving their own sequence order.

Retain window-message pacing at 100 posts/second with burst one. Existing WindowsInputPlayer stays a low-level serialized adapter; every app playback call reaches it through shared dispatch, avoiding its Busy error. The standalone compatibility probe retains exclusive ownership of its own player.

Extend the screen adapter beyond one-click testing to support click, key chords, Unicode text, wheel, and cleanup with correctly sized SendInput structures. Record partial insertion and release only downs inserted by that gesture. Fail if required physical modifiers/buttons are already held rather than releasing physical input the user owns. Screen mode deliberately moves the pointer for clicks and requires visible intended content.

App playback and deliberate compatibility tests share an exclusive-activity guard. Playback cannot start during a test, and tests cannot start while any session is active or paused. No screen or window fallback on error.

## Global hotkeys and lifecycle

Implement a Windows hotkey service on a dedicated native message loop with RegisterHotKey and repeat suppression. Use the current persisted defaults: F9 Run all enabled in current profile, F8 Pause/resume all, F10 Stop all. Hotkeys work while another app is focused.

Marshal UI-sensitive commands to the Avalonia dispatcher. Stop cancellation is initiated promptly without waiting for a UI rendering tick. Avoid duplicate actions from both native hotkeys and the existing focused key handler; the focused handler remains for key-binding capture only when production hotkeys are active.

Register the complete unique set before enabling playback. Show the binding and native reason for any conflict. A failed Stop registration blocks playback and compatibility input. Applying changes during input is disabled; reconfiguration either installs a full valid set or retains the previous set with visible feedback. Dispose unregisters bindings and joins the message loop.

Add Windows suspend notification to the native lifecycle adapter. Headless tests inject hotkey/lifecycle services and never register user's bindings.

## Targets and compatibility

Screen is usable without game compatibility evidence. Window playback resolves the owning profile's saved executable/title rule, requires a unique explicit input surface, and binds its opaque live token. Expose ambiguity/missing-target errors in the app; do not guess a window.

Keep background and minimized capabilities independent. Integrate a deliberate compatibility workflow in the native app using existing target catalog/probe logic: choose saved app, live window and surface, target state, and a specific harmless Click/Key/Text/Wheel test; send only that selected test; then ask the user whether the target visibly responded. Capture pointer coordinates while target is restored where necessary. Unsupported or rejected delivery cannot be confirmed as success.

Persist user-confirmed evidence with app identity, title, surface fingerprint, target state, tested capability, timestamp, and observed result; never persist HWNDs or PIDs. Require current identity/title/fingerprint and requested state/capabilities to match before a macro can run in window mode. Geometry changes require coordinates to be checked again before fixed-pixel actions; percentage actions recalculate against validated dimensions.

Window key delivery proves receipt of messages, not that an app's keyboard-state-based shortcuts work. Display this distinction and require independent Key observation. Existing click confirmations cannot establish Key/Text/Wheel support.

CookieRun evidence remains unconfirmed until deliberate user-observed testing. Build and verify the engine against controlled targets without sending arbitrary game input or asserting game success.

## UI integration and reporting

Keep existing control names and navigation where practical. Wire row Run/Pause/Stop, editor Run/Test/Pause/Stop, Run all, and global controls to the real coordinator. Test selected executes one real compiled action under the same prerequisites and exclusive guard.

Screen starts and Screen Test use a cancellable three-second countdown so the user can expose the intended content. Window playback does not activate its target and waits for the requested background/minimized state; it never minimizes a target automatically.

Show current step and total steps, completed loops, active elapsed time, remaining wait/countdown, and exact error with macro/action context. Pausing remains visible until cleanup completes. Completed is distinct from Stopped. Label delivered input as sent/queued; do not label it observed gameplay success.

Retain editing/deletion locks for active sessions and drafts. Browsing profiles must not affect current sessions. Show global activity counts across profiles. Keep logs bounded and omit typed text contents. Remove UI-preview wording only from flows that actually use production playback; recording remains explicitly unavailable.

## Verification and delivery

Baseline: Release verification on 2026-09-30 passed 68 tests, no skips, zero build warnings/errors.

Add meaningful tests before production changes:

1. Compiler acceptance/rejection, aliases/chords, valid Unicode, bounds, percentage endpoints, action settings, and reserved hotkeys.
2. Fake-clock delay accuracy below 600 ms, repeats/interval semantics, paused wait remainder, safe gesture pause, cancellation during waits and gate acquisition, shutdown/suspend, and immutable snapshots.
3. Concurrent macros on one/different targets: ordered gestures, aggregate pacing, no Busy failures, stopping one does not cancel/release another, StopAll spans profiles.
4. Native seams: screen encoding, partial SendInput failure/cleanup, physically held input rejection, target loss, permissions, geometry/state changes, and hotkey registration rollback/conflicts/disposal.
5. Controlled external Windows receiver: real click/key/text/wheel delivery, background/minimized delivery, balanced gesture cleanup, and window-mode cursor/focus preservation. For real screen injection, create a dedicated harmless target surface, explicitly place/focus it, send only to that surface, verify its events, and restore cursor/foreground where possible. Never run a desktop-wide loop.
6. Headless app: real controls route to injected service, exclusive tests/countdowns, preflight errors, compatibility confirmation gates, cross-profile controls, progress, saved workspace preservation, and global-command routing.

Run locked restore, Release build, all solution tests, and self-contained publish. Update README and verification notes with exact automated evidence and remaining manual game/OS limits. Deliver rebuilt `artifacts/win-x64/Macrofy.exe`; do not start automatic real input merely by launching the app.

## Implementation boundaries

- Core: compiled actions, validation, scheduler/session state, shared dispatch contracts and coordination.
- Platform: execution/hotkey/lifecycle contracts, progress/errors, target/capability data.
- Windows: existing targeted player, generalized screen adapter, global hotkey/lifecycle message loop.
- App services: workspace-to-core mapping, live target resolution, compatibility persistence, production service composition.
- App views: control wiring, editor feedback, progress, compatibility workflow, concise production labels.
- Tests: deterministic Core and adapter tests, controlled Windows integration, injected headless UI tests.

Self-review: scope covers all six accepted improvements; recording is excluded; scheduling explicitly preserves current UI semantics; atomic gestures resolve current shared-target ownership; compatibility evidence and automated delivery remain separate; old saved actions remain reviewable.
