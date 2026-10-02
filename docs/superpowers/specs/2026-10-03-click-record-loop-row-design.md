# Click-to-record, macro row loop/interval, surface labels — design

Date: 2026-10-03

Supersedes the record-key part of `2026-10-02-record-point-and-simple-tests-design.md`.

## 1. Record a point with a left click

- **Record point** starts a recording (macro editor Click step; Compatibility tab). Status: `Click the spot to record it. That click is not sent to the app. Click Cancel recording to stop.` The button reads `Cancel recording` while active.
- `IPointClickSource` (platform): `PlatformError? Start(Action clicked)` begins listening for one left click; `void Cancel()` stops listening.
- `WindowsPointClickSource` installs a low-level mouse hook (`WH_MOUSE_LL`) on its own message-loop thread only while listening:
  - Left button down outside Macrofy's own windows: report `clicked`, swallow the down, then swallow the matching up and uninstall the hook.
  - Clicks on Macrofy's windows (root window owned by this process) pass through, so Cancel recording stays clickable.
  - All other messages pass through.
  - The decision logic is a pure `ClickFilter` class, unit-tested.
- On `clicked`, Macrofy reads the pointer the same way as before (window client pixels via the macro's saved app rule or the selected Compatibility surface; desktop pixels for Screen macros), fills `X, Y`, and ends the recording. Errors are shown.
- Recording ends on success, error, Cancel recording, Stop all / Stop hotkey, suspend, or close (cancel uninstalls the hook).
- Removed: record key (F7) setting and hotkey (`ShortcutSettings.Capture`, `HotkeyCommand.Capture`, `HotkeySet.Capture`, load repair, Settings row). Old files with a `Capture` field load; it is ignored.

## 2. Profiles tab macro row

- New columns **Loop** (ToggleSwitch `Loop_{id}`) and **Interval (s)** (NumericUpDown `Interval_{id}`).
- Loop on → `Repeat = 0` (Until stopped); off → `Repeat = 1` (Once). Shown on when `Repeat != 1`.
- Interval in seconds, 0–600, step 0.5, format `0.##`; stores `IntervalMs = round(seconds × 1000)`.
- Both disabled while the macro runs; changes save.

## 3. Input surface labels

- Compatibility surface items: `Main · {W}×{H} (default)` for the surface whose token equals the live window token, else `Child · {W}×{H} · {id8}` (`?×?` without geometry). Main listed first and pre-selected.

## Testing

- `ClickFilter` decisions; App tests use a fake `IPointClickSource` for macro-editor and Compatibility recording, cancel, and errors; Profiles row loop/interval edits; surface labels and preselection. Real hook behavior is a manual check.
