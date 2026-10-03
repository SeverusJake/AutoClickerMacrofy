# Key picker, Combo key action, quieter macro editor — design

Date: 2026-10-03.

## Actions

- **Key**, **Key down**, **Key up**: exactly one key. A lone modifier (Shift, Ctrl, Alt, Win) is valid. A chord is rejected: `Key takes one key. Use Combo key for combinations.`
- **Combo key** (new): one or more modifiers plus one final key (current chord rules), optional Hold. Compiles to the existing `CompiledAction.Key`.
- Role `tertiary` (Key family), icon keyboard with "+", add button `Add_ComboKey`, default `Ctrl + C`.

## Editor

- Key / Key down / Key up value: dropdown `StepKey_{i}` of single keys (A–Z, 0–9, F1–F24, Space, Enter, Escape, Tab, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Up, Down, Left, Right, Shift, Ctrl, Alt, LeftWin, RightWin, CapsLock, NumLock, ScrollLock, OemPlus, OemMinus, OemComma, OemPeriod) and **Record key** (`StepRecordKey_{i}`). Key keeps Hold.
- Combo key value: read-only text `StepCombo_{i}` and **Record combo** (`StepRecordCombo_{i}`) plus Hold.
- Recording listens to key presses in the Macrofy window; the button reads `Cancel` while waiting and clicking it cancels. Status `RecordStatus`: `Press a key.` / `Hold modifiers and press a key.` Record key saves the first key (modifier keys alone included). Record combo saves held Ctrl/Shift/Alt/Win plus the first non-modifier key; modifier presses alone keep waiting. Run all/Pause all hotkeys pressed while recording report `{key} is reserved for Macrofy.` and do nothing else; Stop still stops and cancels recording. Unsupported keys report `That key isn't supported.`
- Removed descriptions: `ActionWarning`, `ActionHelp`, `MacroModeNote`. Errors (`ActionError`) and `RecordStatus` stay.

## Data

- On load, a **Key** step whose value parses as a chord becomes **Combo key**. The default workspace's "Open search" step is `Combo key` `Ctrl + K`.
- Compatibility "Shortcut" tests compile as Combo key.

## Testing

Compiler (single key, lone modifier, chord rejected for Key/Key down/Key up, Combo key), load migration, dropdown save, Record key / Record combo / reserved / cancel, add button, removed descriptions.
