# Add app window picker — design

Date: 2026-10-02

## Problem

Adding a target app requires typing an executable path and a wildcard title rule by hand. For CookieRun that means knowing `C:\Program Files\Google\Play Games\current\emulator\crosvm.exe` and writing a rule narrow enough to skip the other ~10 untitled `crosvm` processes. Users get this wrong or give up.

## Goal

Pick a running window and have Macrofy fill App name, Executable path, and Title rule. Show how many open windows the rule matches before saving. Manual entry keeps working.

## UI

Add app and Edit app dialogs gain a section above the existing fields:

1. **Pick an open window** list plus **Refresh** button. Rows show window title and executable file name (for example `crosvm.exe`). Source: `IWindowCatalog.ListAsync()` — visible top-level windows with a title, excluding Macrofy. The list loads when the dialog opens; Refresh reloads it.
2. Selecting a row fills:
   - **App name**: the title without its last ` - ` segment (whole title when it has none).
   - **Executable path**: the window's full executable path.
   - **Title rule**: the selected suggestion (default: the first).
3. **Title rule suggestions**: radio choices, each labelled with its live match count:
   - *Without last part*: title without its last ` - ` segment, plus `*`. `CookieRun: Crumble - Idle RPG - SeverusJake` → `CookieRun: Crumble - Idle RPG*`. A title without ` - ` gives `Title*`.
   - *Exact title*: the full title.
   - *Program name*: `*<exe file name without extension>*`, for example `*crosvm*`.
   Duplicates (case-insensitive) are hidden. Choosing a suggestion copies it into the Title rule box, which stays editable. Editing the box clears the radio selection.
4. **Match line** under the fields, updated as the executable path or title rule changes, against the last loaded window list:
   - 1 match: `✓ Matches 1 open window`.
   - 0 matches: warning `No open window matches. The app may be closed, or the rule is wrong.`
   - More than 1: warning `Matches N open windows. Narrow the rule so playback can pick one.`
   Warnings never block Save; the app may legitimately be closed.

Existing validation (non-empty name, executable, rule) and the active-macro and compatibility locks stay unchanged.

## Code

- New `Macrofy.App.Services.TitleRule` static class:
  - `bool Matches(string rule, string title)`: anchored, case-insensitive, culture-invariant, single-line wildcard match (`*` any run, `?` one character) with the existing 100 ms regex timeout.
  - `bool MatchesWindow(string executable, string rule, TargetWindow window)`: case-insensitive executable path equality plus `Matches`.
  - `IReadOnlyList<TitleRuleSuggestion> Suggestions(string title, string executablePath)`: ordered, de-duplicated suggestions as above.
  - `string SuggestedName(string title)`.
- Replace the duplicated wildcard regexes in `WorkspacePlaybackController`, `CompatibilityService`, and `MainWindow.Compatibility` with `TitleRule.Matches`/`MatchesWindow`. `WindowsWindowCatalog.Matches` lives in the platform project and keeps its own implementation, which already uses the same anchors, options, and timeout.
- `MainWindow.Dialogs.AppDialog` builds the picker using `compatibility?.Catalog`.

## Errors

- No catalog (non-Windows or no compatibility services): the picker shows `Window list unavailable. Enter details manually.`; manual fields work.
- `ListAsync` throws: same message plus the error text; Refresh retries.
- Regex timeout in `Matches`: treated as no match.
- A stale load (dialog closed or a newer Refresh started) is discarded.

## Testing

- Unit tests for `TitleRule`: suggestion text and order, ` - ` handling, no-separator title, duplicate removal, wildcard semantics, case-insensitivity, anchoring, executable comparison.
- Headless UI test with a fake `IWindowCatalog`: open Add app, select a window row, assert filled fields, suggestion labels with counts, match line text for 1/0/many, and that Save stores the values.
- Existing full suite via `scripts/verify-probe.ps1` (`-m:1`).

## Out of scope

Point-and-capture window selection, Browse button, window icons, filtering the list.
