# Inline Macro Editor Implementation Plan (Part 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the macro editor's list + inspector with an inline-editable step table, tinted action colors, and a top bar with Loop/Times/Interval(s)/Pixels-%.

**Architecture:** Rewrite `MainWindow.MacrosPane` as header + top bar + table + add bar. Per-row `ActionDraft`s exist only for invalid text. Shared helpers (`ActionKinds`, `KindRole`, `DefaultValue`, `ActionChip`) live in `MainWindow.Macros.cs`; icons in `UiIcons`.

**Tech Stack:** .NET 10, Avalonia 12.1.3, xUnit v3 + Avalonia.Headless.

Spec: `docs/superpowers/specs/2026-10-03-inline-macro-editor-design.md`

## Global Constraints

- Control names exactly as in the spec.
- Roles: Click `secondary`, Key `tertiary`, Text `success`, Wait `warning`, Wheel `info`.
- Error text format `Step {n}: {error}`; blocked-run reason `Fix or undo step edits first (Esc).`
- Tests run with `-m:1`; commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Icons and action helpers

**Files:** Modify `src/Macrofy.App/Views/UiIcons.cs`.

- [ ] Add icon paths: `Click` `"M6 3v15l4-4 3 7 3-1.3-3-6.7h6z"`, `Key` `"M2 6h20v12H2z M6 10h.01 M10 10h.01 M14 10h.01 M18 10h.01 M7 14h10"`, `Text` `"M5 5h14 M12 5v14 M9 19h6"`, `Wait` `"M20 13a8 8 0 1 1-16 0 8 8 0 0 1 16 0 M12 9v4l3 2 M9 2h6"`, `Wheel` `"M12 3a5 5 0 0 1 5 5v8a5 5 0 0 1-10 0V8a5 5 0 0 1 5-5 M12 7v4"`.

### Task 2: New editor (tests first)

**Files:** Rewrite `src/Macrofy.App/Views/MainWindow.Macros.cs`; modify `MainWindow.cs` (`ActionDraft` with `DelayText`, remove `responsive*` fields and `ReflowEditor` body), `MainWindow.Playback.cs` (`CanRun` draft message); tests `tests/Macrofy.App.Tests/InlineEditorUiTests.cs` plus updates in `NativeUiTests.cs`, `PlaybackUiTests.cs`, `FinalReviewUiTests.cs`, `RecordPointUiTests.cs`.

- [ ] **Step 1: Failing tests** `InlineEditorUiTests.cs` covering:
  1. `ValidEditsSaveImmediatelyAndInvalidDraftBlocksRunUntilEsc` — `StepX_0.Text = "30"` saves `"30, 640"`; `StepX_0.Text = "x"` keeps `"30, 640"`, `ActionError` = `Step 1: …`, `RunSelected` disabled; Esc on `StepX_0` restores `"30"` and enables Run.
  2. `KindChangeAddDuplicateDeleteAndReorder` — `Add_Wait` appends `Wait 1000`; `StepKind_1.SelectedItem = "Key"` → `Key Space`; `StepDuplicate_0` inserts copy at 1; Alt+Down on `StepX_0` moves step 0 to 1; `StepDelete_2` removes; values asserted.
  3. `LoopTimesIntervalAndCoordinatesEditMacro` — `MacroLoop` on → Repeat 0; `MacroTimes.Text = "100"` → 100; blank → 0; off → 1; `MacroInterval.Value = 1.5m` → 1500; `Coord_Percent` click → Coordinates `Percentage` and `StepX_0` error mentions `0–100`.
  4. `ActionColorsDifferPerKind` — add buttons `Add_Click` and `Add_Key` have different `Background` brush colors; `StepKind_0` background equals `Add_Click` background.
  5. `DragHandleMovesRowAndWaitValueCellIsMs` — `MoveStep(0, 2)` through `StepHandle_0` drag using headless `MouseDown/MouseMove/MouseUp` to row 2's center; Wait row shows `StepValue_{i}` with `ms` label.
- [ ] **Step 2:** Run → FAIL (controls missing).
- [ ] **Step 3: Implement** the pane per spec. Key pieces:
  - `selectedStep` clamp; `rows` list of `Border`; `Select(int i)` updates highlights, `ActionHelp` text, Test enabled.
  - `Draft(i)` returns existing `ActionDraft` or a fresh one from the saved step; `Commit(i)` builds `MacroStep(kind, value, delay)`, validates with `Workspace.ValidateStep` and `int.TryParse(DelayText)`; valid → `macro.Steps[i] = step`, remove draft, `Save()`, clear error; invalid → keep draft, show `Step {i+1}: {error}`; always `RefreshPlayback()`.
  - Text boxes call `Commit` on `TextProperty` change (attached after initial text) and handle `KeyDown`: Esc → remove draft, `Render()`; Alt+Up/Down → `MoveStep(i, i∓1)`.
  - `MoveStep(from, to)`: blocked when active/draft; `Steps.RemoveAt(from)`, `Insert(to, step)`, `selectedStep = to`, `Save()`, `Render()`.
  - Drag: `StepHandle_{i}` `PointerPressed` captures source; `PointerReleased` finds the row whose bounds contain the pointer (relative to the table) and calls `MoveStep`.
  - Row Record: `RecordPointButton("StepRecord_" + i, …, point => { macro.Steps[i] = macro.Steps[i] with { Value = FormatPoint(point) }; drafts.Remove(key); recordMessage = $"Step {i + 1} recorded at {value}."; Save(); Render(); }, …)`.
  - Top bar and add bar per spec; all edit controls in an `editable` list disabled when active or locked.
- [ ] **Step 4:** Update existing tests to the new names (`ActionValue` → `StepX_0`/`StepY_0`/`StepValue_i`, `ApplyAction` removed, `DiscardDraft` → Esc, `ActionKind` → `StepKind_i`, `CoordinateMode` → `Coord_*`, `RemoveAction` → `StepDelete_i`, `RecordPoint` → `StepRecord_0`). `NativeUiTests` draft macro uses invalid text so Run all still skips it.
- [ ] **Step 5:** Build + non-native App tests → PASS. Commit `feat: inline-table macro editor with tinted action colors`.

### Task 3: Docs, verify, publish, push

- [ ] README "Edit and play a macro": table editing, live save, Esc, Alt+arrows/drag, Loop/Times/Interval(s), Pixels/%, row Record.
- [ ] `scripts/verify-probe.ps1` → all pass; README count; commit; publish; push.
