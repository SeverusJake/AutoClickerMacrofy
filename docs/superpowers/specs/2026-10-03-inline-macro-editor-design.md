# Inline-table macro editor — design (Part 1)

Date: 2026-10-03. Mockups: `docs/ui/macro-editor-options.html` (Option A), `docs/ui/action-button-colors.html` (Option 1, tinted). Part 2 (hold, buttons, press/release) is a separate design.

## Layout (Macros tab, right of the macro list)

1. Header unchanged: name, rename/duplicate/delete, Playback target, Window state, mode note.
2. Top bar: Run (`RunSelected`), Test selected step (`TestSelected`), Pause (`PauseSelected`), Stop (`StopSelected`); **Loop** toggle (`MacroLoop`) with **Times** box (`MacroTimes`, visible when Loop is on; blank = until stopped, 2–1000000 = that many runs); **Interval (s)** (`MacroInterval`, 0–600, step 0.5); **Pixels / %** buttons (`Coord_Pixels`, `Coord_Percent`); status text (`MacroProgress`).
3. Step table, one row per step:
   - `⋮⋮` drag handle (`StepHandle_{i}`), number button (`SelectStep_{i}`), Action dropdown (`StepKind_{i}`) tinted in the action color, value cell, **Wait after** ms box (`StepDelay_{i}`), Duplicate (`StepDuplicate_{i}`), Delete (`StepDelete_{i}`).
   - Value cell: Click → `X` (`StepX_{i}`), `Y` (`StepY_{i}`), **Record** (`StepRecord_{i}`); Key / Text / Wheel → text box (`StepValue_{i}`); Wait → ms box (`StepValue_{i}`) + "ms".
   - The selected row is highlighted; focusing any control in a row selects it.
4. Below the table: tinted **+ Click / + Key / + Text / + Wait / + Wheel** buttons (`Add_{Kind}`) with icons; record status (`RecordStatus`); help for the selected step's action (`ActionHelp`); errors (`ActionError`).

Removed: right-side Edit action panel, Apply/Discard buttons, up/down buttons, add-type dropdown, Repeat dropdown, ms interval box, coordinate dropdown, disabled "recording not connected" icon.

## Action colors (Option 1, tinted)

Click = `secondary`, Key = `tertiary`, Text = `success`, Wait = `warning`, Wheel = `info` palette roles. Add buttons and the row Action dropdown use `Tint(role, .14)` background, `Brush(role)` border and text, and an action icon (pointer, keyboard, T, clock, mouse wheel).

## Editing rules

- Edits save as soon as the row is valid (no Apply). Each row keeps an `ActionDraft` only while its text is invalid; the error line reads `Step {n}: {error}`. Run and Test stay blocked while any draft is invalid (`Fix or undo step edits first (Esc).`).
- **Esc** in a row's field restores the saved step.
- Changing the Action kind replaces the value with that kind's default unless the current value is valid for the new kind, saves, and re-renders the row.
- Record in a row saves the recorded `X, Y` to that step immediately.
- Add buttons append a step with the kind's default value and 100 ms wait and select it. Duplicate inserts a copy below; Delete removes the row.
- Reorder: drag a row's handle onto another row; or **Alt+↑ / Alt+↓** in any field of the selected row. Reordering, adding, duplicating and deleting are blocked while the macro runs or a draft is invalid.
- Loop off → `Repeat = 1`; on → `Repeat = 0` unless Times holds a number. Times blank → 0; invalid Times text is marked and ignored. Interval stores `IntervalMs = round(seconds × 1000)`.
- All editing controls are disabled while the macro runs or a compatibility test is busy.

## Testing

Headless UI tests for: live save and invalid draft + Esc, kind change, Click X/Y and Wait/Key value cells, add/duplicate/delete, Alt+arrow and drag reorder, Loop/Times/Interval, coordinate buttons, row Record saving, colors applied (button background differs per kind), selected-step Test, and existing behaviors moved to the new control names.
