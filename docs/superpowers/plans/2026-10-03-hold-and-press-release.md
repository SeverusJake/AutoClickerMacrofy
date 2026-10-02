# Hold and Press/Release Implementation Plan (Part 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add mouse button choice, hold durations, and Mouse/Key down/up steps with session-owned held input that is always released.

**Architecture:** Core compiles new actions and the session tracks held input; the App sender maps them to commands and pins held input in the Windows players so gesture cleanup skips it; the editor exposes the fields.

**Tech Stack:** .NET 10, C#, Avalonia 12.1.3, xUnit.

Spec: `docs/superpowers/specs/2026-10-03-hold-and-press-release-design.md`

## Global Constraints

- Kinds: `Click`, `Key`, `Text`, `Wait`, `Wheel`, `Mouse down`, `Mouse up`, `Key down`, `Key up`.
- Buttons `Left`/`Right`/`Middle`; HoldMs 0–600000.
- New interface members have default implementations so existing fakes compile.
- Tests run with `-m:1`; commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Core model and compiler
- [ ] Tests in `tests/Macrofy.Core.Tests/ActionCompilerTests.cs`: right-button click with hold compiles to `Click(point, FixedPixels, delay, MouseButton.Right, 250)`; `Mouse down`/`Mouse up` with `Middle`; `Key down`/`Key up` chords; bad button and hold > 600000 rejected; Text ignores Button/Hold.
- [ ] Implement `ActionDefinition`, `CompiledAction` records, compiler cases.
- [ ] Commit `feat: compile mouse buttons, holds and press/release actions`.

### Task 2: Session held input
- [ ] Tests in `tests/Macrofy.Core.Tests/HeldInputPlaybackTests.cs` with the test clock: hold sends press at t, release at t+hold; stop during hold calls `ReleaseHeldAsync` with the held button; pause during a Key down releases and resume does not re-press; second Key down for a held key is skipped; Mouse up without a held button is skipped; loop keeps a Key down held and releases once at completion.
- [ ] Implement in `PlaybackSession` (held list, press/release helpers, pause release in `ReadyAsync`, end release in `finally`) and `IPlaybackExecutor.ReleaseHeldAsync` default.
- [ ] Commit `feat: sessions own held input and release it on pause and end`.

### Task 3: Platform pins and sender
- [ ] Tests: `WindowsInputPlayer`/`WindowsScreenInputPlayer` keep pinned inputs on `ReleaseHeldAsync` (existing fakes in Windows tests); sender `MouseDown` pins and later `ReleaseHeldAsync` sends up at the stored point; failure cleanup after a pinned press keeps it.
- [ ] Implement `HeldInput`, interface defaults, player pin sets, sender command mapping and pin registry, `WindowsPlaybackExecutor.ReleaseHeldAsync` and capability check for new actions.
- [ ] Commit `feat: pin held input in players and release it through the sender`.

### Task 4: Editor
- [ ] Tests in `InlineEditorUiTests`: Click row Button/Hold save; Key hold; `Add_Mouse down` row has X/Y/Button; `Key down` without `Key up` shows `ActionWarning`; JSON without Button/HoldMs loads defaults.
- [ ] Implement `MacroStep` fields, `ActionDraft` Button/HoldText, controller/state compile with new fields, editor cells, icons, add buttons, warning.
- [ ] Commit `feat: edit mouse buttons, holds and press/release steps`.

### Task 5: Real delivery, docs, release
- [ ] Integration test in `PlaybackIntegrationTests`: window right-click with 60 ms hold → `0x204` then `0x205` ≥ 50 ms later; Key down A, Wait, Key up A → `0x100` … `0x101`.
- [ ] README; full `verify-probe.ps1`; count; commit; publish; push.
