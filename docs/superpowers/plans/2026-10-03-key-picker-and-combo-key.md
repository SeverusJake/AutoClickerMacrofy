# Key Picker and Combo Key Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One-key Key actions picked or recorded, a new Combo key action recorded from the keyboard, and no descriptive text in the macro editor.

**Architecture:** Core compiler splits single keys from combos; the App migrates old chord Key steps on load; the editor replaces the Key text box with a dropdown plus recorders driven by the window's tunnel KeyDown handler.

**Tech Stack:** .NET 10, Avalonia 12.1.3, xUnit.

Spec: `docs/superpowers/specs/2026-10-03-key-picker-and-combo-key-design.md`

## Global Constraints
- Control names and copy exactly as in the spec. Tests run with `-m:1`; commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

### Task 1: Compiler
- [ ] Tests (`tests/Macrofy.Core.Tests/KeyComboCompilerTests.cs`): `Key "Shift"` compiles to one key; `Key "Ctrl + C"` fails with `Use Combo key`; `Combo key "Ctrl + Shift + S"` compiles to `Key` with three keys and hold; `Combo key "C"` fails (needs a modifier); `Key down "Shift + W"` fails. Update existing compiler/session/UI tests that used chord `Key` values to `Combo key`.
- [ ] `KeyParser.TryParse(value, allowLoneModifier)`; compiler cases.
- [ ] Commit `feat: single-key Key actions and a Combo key action`.

### Task 2: Data migration
- [ ] Test (`WorkspaceStateTests`): a stored Key `Ctrl + K` loads as Combo key; default workspace uses Combo key.
- [ ] `WorkspaceStore.Load` migration; `CreateDefault`; Compatibility Shortcut → `Combo key`.
- [ ] Commit `feat: migrate chord Key steps to Combo key`.

### Task 3: Editor
- [ ] Tests (`KeyPickerUiTests.cs`): dropdown saves; Record key captures `Q`; Record combo captures Ctrl+Shift+S; reserved F9 reported; Cancel restores; `Add_ComboKey`; `ActionWarning`/`ActionHelp`/`MacroModeNote` absent.
- [ ] Implement in `MainWindow.Macros.cs` + `MainWindow.KeyRecording.cs`; icon; remove descriptions; update tests that referenced removed texts.
- [ ] Commit `feat: key dropdown with key and combo recording; quieter editor`.

### Task 4: Release
- [ ] README; `verify-probe.ps1`; count; commit; publish; push.
