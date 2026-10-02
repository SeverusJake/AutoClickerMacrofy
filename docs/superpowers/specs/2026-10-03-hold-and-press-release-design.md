# Hold, mouse buttons and press/release steps — design (Part 2)

Date: 2026-10-03. Builds on `2026-10-03-inline-macro-editor-design.md`.

## Editor

- `MacroStep(Kind, Value, DelayMs, Button = "Left", HoldMs = 0)`; old files load with the defaults.
- Click row: X, Y, **Button** (`StepButton_{i}`: Left / Right / Middle), **Hold** ms (`StepHold_{i}`), Record.
- Key row: value + **Hold** ms (`StepHold_{i}`).
- New kinds: **Mouse down** (X, Y, Button, Record), **Mouse up** (X, Y, Button, Record), **Key down** (key or chord), **Key up** (key or chord). Defaults: `480, 640` / `W`.
- Colors: Mouse down/up use Click's role (`secondary`), Key down/up use Key's role (`tertiary`); icons show ↓ / ↑.
- Add buttons for all nine kinds.
- Non-blocking warning (`ActionWarning`): `Step {n} holds {input} until the macro ends.` when a Mouse down / Key down has no later matching Mouse up / Key up.

## Compilation (Core)

- `ActionDefinition(Kind, Value, DelayMs, Button = "Left", HoldMs = 0)`.
- Button must be `Left`, `Right` or `Middle` for Click / Mouse down / Mouse up; HoldMs 0–600000 for Click and Key; other kinds ignore both.
- `CompiledAction.Click(Point, Mode, DelayMs, Button = Left, HoldMs = 0)`, `Key(keys, delay, holdMs = 0)`, new `MouseDown(Point, Mode, Button, DelayMs)`, `MouseUp(Point, Mode, Button, DelayMs)`, `KeyDown(keys, delay)`, `KeyUp(keys, delay)`.

## Playback (Core session)

- The session keeps its own held inputs (buttons with their press action, keys by logical name).
- Click/Key with HoldMs > 0: dispatch the press (MouseDown / KeyDown), wait HoldMs (state Waiting, pause-aware), then dispatch the release if still held. Other sessions run during the wait.
- Mouse down / Key down: press only inputs not already held. Mouse up / Key up: release only inputs this session holds; otherwise skip.
- Pause releases all held inputs before pausing; resume does not re-press.
- Finish, stop, error and target loss release all held inputs (best effort, shared gate, 2 s wait for the gate). Release failures set the cleanup error unless the target was lost.
- New executor method `ReleaseHeldAsync(binding, inputs, ct)` with a default implementation returning success.

## Delivery (App sender, Windows players)

- `HeldInput(MouseButton? Button, KeyIdentity? Key)` in Platform models. `IInputPlayer.Pin/Unpin(target, input)` and `IScreenInputPlayer.Pin/Unpin(input)` default to no-ops; Windows players exclude pinned inputs from `ReleaseHeldAsync` and failure cleanup.
- Sender: MouseDown → pointer Down; MouseUp → pointer Up (screen moves first, so down at A + up at B is a drag); KeyDown → key downs; KeyUp → key ups (reverse). After a successful press gesture its inputs are pinned (with the converted point for buttons); release gestures unpin before sending. `ReleaseHeldAsync` unpins and sends ups using the stored point, without foreground/minimized state checks.
- Click with a non-left button sends that button's down/up.

## Caveat

Background (window-message) holds do not change the system's physical key state; games that poll key state may ignore them. Screen mode uses real input.

## Testing

- Core: compiler (kinds, button, hold ranges); session hold timing with the test clock; pause/stop release; duplicate down skipped; unmatched up skipped; loop keeps hold.
- Platform: pinned inputs survive `ReleaseHeldAsync` for window and screen players.
- App: sender pin/unpin and release; editor fields, new kinds, warning; JSON default loading.
- Integration: real background window receives right-button down/up separated by the hold, and key down/up across steps.
