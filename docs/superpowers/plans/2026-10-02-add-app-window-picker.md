# Add App Window Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users add a target app by picking an open window that fills App name, Executable path, and Title rule, with live rule-match counts.

**Architecture:** A new `TitleRule` service owns wildcard matching and suggestion text, and replaces three duplicated regex matchers in the App project. A new `MainWindow.AppPicker.cs` partial builds the picker from the existing `IWindowCatalog` (`compatibility?.Catalog`) and is embedded in `AppDialog`.

**Tech Stack:** .NET 10 (SDK 10.0.302), C#, Avalonia 12.1.3, xUnit v3 with Avalonia.Headless.XUnit in `tests/Macrofy.App.Tests`.

Spec: `docs/superpowers/specs/2026-10-02-add-app-window-picker-design.md`

## Global Constraints

- Title rule semantics: anchored whole-title match, `*` any run, `?` one character, case-insensitive, culture-invariant, single-line, 100 ms regex timeout; timeout counts as no match.
- Executable comparison: case-insensitive full path equality.
- Suggestion order and labels: `Without last part` (title without last ` - ` segment + `*`), `Exact title` (full title), `Program name` (`*<exe name without extension>*`); duplicates hidden case-insensitively.
- Match line copy: `✓ Matches 1 open window`; `No open window matches. The app may be closed, or the rule is wrong.`; `Matches N open windows. Narrow the rule so playback can pick one.` Warnings never block Save.
- Unavailable list copy: `Window list unavailable. Enter details manually.`
- Run tests one assembly at a time (`-m:1`); native tests use the real cursor.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: TitleRule service

**Files:**
- Create: `src/Macrofy.App/Services/TitleRule.cs`
- Test: `tests/Macrofy.App.Tests/TitleRuleTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record TitleRuleSuggestion(string Label, string Rule);`
  - `public static class TitleRule` in `Macrofy.App.Services` with
    - `bool Matches(string rule, string title)`
    - `bool MatchesWindow(string executable, string rule, TargetWindow window)`
    - `string SuggestedName(string title)`
    - `IReadOnlyList<TitleRuleSuggestion> Suggestions(string title, string executablePath)`

- [ ] **Step 1: Write the failing tests**

`tests/Macrofy.App.Tests/TitleRuleTests.cs`:

```csharp
using Macrofy.App.Services;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class TitleRuleTests
{
    private const string Crosvm = @"C:\Program Files\Google\Play Games\current\emulator\crosvm.exe";

    [Theory]
    [InlineData("CookieRun*", "CookieRun: Crumble - Idle RPG - SeverusJake", true)]
    [InlineData("cookierun*", "CookieRun: Crumble", true)]
    [InlineData("CookieRun", "CookieRun: Crumble", false)]
    [InlineData("*Crumble*", "CookieRun: Crumble - Idle RPG", true)]
    [InlineData("Crumble*", "CookieRun: Crumble", false)]
    [InlineData("Game ?", "Game 7", true)]
    [InlineData("Game ?", "Game 77", false)]
    [InlineData("A.B", "AxB", false)]
    [InlineData("*", "", true)]
    public void MatchesWholeTitleWithWildcards(string rule, string title, bool expected) =>
        Assert.Equal(expected, TitleRule.Matches(rule, title));

    [Fact]
    public void MatchesWindowRequiresSameExecutableIgnoringCase()
    {
        var window = Window("CookieRun: Crumble", Crosvm);
        Assert.True(TitleRule.MatchesWindow(Crosvm.ToUpperInvariant(), "CookieRun*", window));
        Assert.False(TitleRule.MatchesWindow(@"C:\Other\crosvm.exe", "CookieRun*", window));
        Assert.False(TitleRule.MatchesWindow(Crosvm, "CookieRun*", Window("CookieRun: Crumble", null)));
    }

    [Theory]
    [InlineData("CookieRun: Crumble - Idle RPG - SeverusJake", "CookieRun: Crumble - Idle RPG")]
    [InlineData("Untitled - Notepad", "Untitled")]
    [InlineData("Discord", "Discord")]
    [InlineData(" - Leading", " - Leading")]
    public void SuggestedNameDropsLastSegment(string title, string expected) =>
        Assert.Equal(expected, TitleRule.SuggestedName(title));

    [Fact]
    public void SuggestionsAreOrderedWithLabels()
    {
        var suggestions = TitleRule.Suggestions("CookieRun: Crumble - Idle RPG - SeverusJake", Crosvm);
        Assert.Equal(new[]
        {
            new TitleRuleSuggestion("Without last part", "CookieRun: Crumble - Idle RPG*"),
            new TitleRuleSuggestion("Exact title", "CookieRun: Crumble - Idle RPG - SeverusJake"),
            new TitleRuleSuggestion("Program name", "*crosvm*")
        }, suggestions);
    }

    [Fact]
    public void SuggestionsHideDuplicatesAndMissingProgram()
    {
        Assert.Equal(new[] { "*crosvm**", "*crosvm*" }, TitleRule.Suggestions("*CROSVM*", Crosvm).Select(s => s.Rule.ToLowerInvariant()));
        Assert.Equal(new[] { "Discord*", "Discord" }, TitleRule.Suggestions("Discord", "").Select(s => s.Rule));
    }

    private static TargetWindow Window(string title, string? executable) =>
        new(new TargetToken(Guid.NewGuid()), new AppIdentity("app", executable), title, false, new ClientGeometry(800, 600, 1));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Macrofy.App.Tests --filter "FullyQualifiedName~TitleRuleTests"`
Expected: build FAILS with `CS0103: The name 'TitleRule' does not exist` (and `TitleRuleSuggestion` not found).

- [ ] **Step 3: Write the implementation**

`src/Macrofy.App/Services/TitleRule.cs`:

```csharp
using System.Text.RegularExpressions;
using Macrofy.Platform.Models;
namespace Macrofy.App.Services;

public sealed record TitleRuleSuggestion(string Label, string Rule);

/// <summary>Saved-app title rules: whole-title, case-insensitive; <c>*</c> matches any run and <c>?</c> one character.</summary>
public static class TitleRule
{
    private const string Separator = " - ";

    public static bool Matches(string rule, string title)
    {
        var pattern = "\\A" + Regex.Escape(rule).Replace("\\*", ".*").Replace("\\?", ".") + "\\z";
        try { return Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static bool MatchesWindow(string executable, string rule, TargetWindow window) =>
        string.Equals(executable, window.App.ExecutablePath, StringComparison.OrdinalIgnoreCase) && Matches(rule, window.Title);

    /// <summary>Drops a trailing " - account/document" part; titles without one are kept whole.</summary>
    public static string SuggestedName(string title)
    {
        var cut = title.LastIndexOf(Separator, StringComparison.Ordinal);
        return cut > 0 ? title[..cut] : title;
    }

    public static IReadOnlyList<TitleRuleSuggestion> Suggestions(string title, string executablePath)
    {
        var suggestions = new List<TitleRuleSuggestion> { new("Without last part", SuggestedName(title) + "*"), new("Exact title", title) };
        var program = Path.GetFileNameWithoutExtension(executablePath);
        if (program.Length > 0) suggestions.Add(new("Program name", "*" + program + "*"));
        return suggestions.DistinctBy(s => s.Rule, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Macrofy.App.Tests --filter "FullyQualifiedName~TitleRuleTests"`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/Macrofy.App/Services/TitleRule.cs tests/Macrofy.App.Tests/TitleRuleTests.cs
git commit -m "feat: add shared title rule matching and suggestions"
```

---

### Task 2: Use TitleRule in existing matchers

**Files:**
- Modify: `src/Macrofy.App/Services/WorkspacePlaybackController.cs:6,129-130`
- Modify: `src/Macrofy.App/Services/CompatibilityService.cs:6,124`
- Modify: `src/Macrofy.App/Views/MainWindow.Compatibility.cs:2,95-96`

**Interfaces:**
- Consumes: `TitleRule.Matches(string rule, string title)`, `TitleRule.MatchesWindow(string executable, string rule, TargetWindow window)` from Task 1.
- Produces: no new API. Behavior unchanged except Compatibility matching now also uses the 100 ms timeout and `\A…\z` single-line anchors.

- [ ] **Step 1: Replace the playback evidence check**

In `WorkspacePlaybackController.cs`, replace:

```csharp
                if (evidence is not { ObservedSuccess: true } || !string.Equals(evidence.App.ExecutablePath, app.Executable, StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(evidence.Title, "\\A" + Regex.Escape(app.TitleRule).Replace("\\*", ".*").Replace("\\?", ".") + "\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100)))
```

with:

```csharp
                if (evidence is not { ObservedSuccess: true } || !string.Equals(evidence.App.ExecutablePath, app.Executable, StringComparison.OrdinalIgnoreCase) ||
                    !TitleRule.Matches(app.TitleRule, evidence.Title))
```

Delete line 6 `using System.Text.RegularExpressions;`.

- [ ] **Step 2: Replace the compatibility context check**

In `CompatibilityService.cs` `ValidContext`, replace:

```csharp
            string.Equals(app.Executable, context.Window.App.ExecutablePath, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(context.Window.Title, "^" + Regex.Escape(app.TitleRule).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
```

with:

```csharp
            TitleRule.MatchesWindow(app.Executable, app.TitleRule, context.Window) &&
```

Delete line 6 `using System.Text.RegularExpressions;`.

- [ ] **Step 3: Replace the Compatibility pane window filter**

In `MainWindow.Compatibility.cs`, replace:

```csharp
                window.ItemsSource = windows.Where(w => string.Equals(w.App.ExecutablePath, selected.Executable, StringComparison.OrdinalIgnoreCase) &&
                    Regex.IsMatch(w.Title, "^" + Regex.Escape(selected.TitleRule).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Select(w => new WindowOption(w)).ToArray();
```

with:

```csharp
                window.ItemsSource = windows.Where(w => TitleRule.MatchesWindow(selected.Executable, selected.TitleRule, w)).Select(w => new WindowOption(w)).ToArray();
```

Replace line 2 `using System.Text.RegularExpressions;` with `using Macrofy.App.Services;` (skip if that using already exists).

- [ ] **Step 4: Verify no duplicated wildcard regex remains in the App project**

Run: `git grep -n "Replace(\"\\\\\\\\\*\"" -- src/Macrofy.App`
Expected: only `src/Macrofy.App/Services/TitleRule.cs`.

- [ ] **Step 5: Build and run App plus Core tests**

Run: `dotnet build Macrofy.sln` then `dotnet test tests/Macrofy.App.Tests --no-build` and `dotnet test tests/Macrofy.Core.Tests --no-build`
Expected: build 0 warnings, 0 errors; all tests pass (App 117 + Task 1 tests). Keep hands off the mouse during App tests.

- [ ] **Step 6: Commit**

```bash
git add src/Macrofy.App/Services/WorkspacePlaybackController.cs src/Macrofy.App/Services/CompatibilityService.cs src/Macrofy.App/Views/MainWindow.Compatibility.cs
git commit -m "refactor: share title rule matching across app services"
```

---

### Task 3: Window picker in Add/Edit app dialog

**Files:**
- Create: `src/Macrofy.App/Views/MainWindow.AppPicker.cs`
- Modify: `src/Macrofy.App/Views/MainWindow.Dialogs.cs:41-62` (`AppDialog`)
- Modify: `src/Macrofy.App/Views/MainWindow.Panes.cs:89` (name the Add app button)
- Test: `tests/Macrofy.App.Tests/AppWindowPickerUiTests.cs`

**Interfaces:**
- Consumes: `TitleRule.SuggestedName`, `TitleRule.Suggestions`, `TitleRule.MatchesWindow` (Task 1); `compatibility?.Catalog` (`IWindowCatalog.ListAsync`); helpers `Text`, `Wrap`, `Field`, `Row`, `Stack`, `TextButton`, `IconButton`, `palette.Brush(role)`.
- Produces: `private (Control Picker, TextBlock MatchLine) AppWindowPicker(TextBox name, TextBox executable, TextBox title)`; control names `AddApp`, `AppWindowList`, `AppWindowRefresh`, `AppWindowStatus`, `AppRule_0..2`, `AppRuleMatch`, `AppName`, `AppExecutable`, `AppTitleRule`, `SaveApp`.

- [ ] **Step 1: Write the failing UI tests**

`tests/Macrofy.App.Tests/AppWindowPickerUiTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.App.Views;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class AppWindowPickerUiTests
{
    private const string Crosvm = @"C:\Program Files\Google\Play Games\current\emulator\crosvm.exe";

    [AvaloniaFact]
    public async Task PickingWindowFillsFieldsCountsMatchesAndSaves()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var catalog = new ListedCatalog([Window("CookieRun: Crumble - Idle RPG - SeverusJake", Crosvm),
            Window("Google Play Games", Crosvm), Window("Untitled - Notepad", @"C:\Windows\System32\notepad.exe")]);
        var (window, dialog) = await OpenAddApp(state, catalog);
        try
        {
            var list = PlaybackUiTests.Find<ListBox>(dialog, "AppWindowList");
            await PlaybackControllerTests.Until(() => list.ItemCount == 3);
            list.SelectedIndex = 0;
            var title = PlaybackUiTests.Find<TextBox>(dialog, "AppTitleRule");
            var match = PlaybackUiTests.Find<TextBlock>(dialog, "AppRuleMatch");
            Assert.Equal("CookieRun: Crumble - Idle RPG", PlaybackUiTests.Find<TextBox>(dialog, "AppName").Text);
            Assert.Equal(Crosvm, PlaybackUiTests.Find<TextBox>(dialog, "AppExecutable").Text);
            Assert.Equal("CookieRun: Crumble - Idle RPG*", title.Text);
            Assert.Equal("CookieRun: Crumble - Idle RPG*   (Without last part · matches 1)", PlaybackUiTests.Find<RadioButton>(dialog, "AppRule_0").Content);
            Assert.Equal("CookieRun: Crumble - Idle RPG - SeverusJake   (Exact title · matches 1)", PlaybackUiTests.Find<RadioButton>(dialog, "AppRule_1").Content);
            Assert.Equal("*crosvm*   (Program name · matches 0)", PlaybackUiTests.Find<RadioButton>(dialog, "AppRule_2").Content);
            Assert.True(PlaybackUiTests.Find<RadioButton>(dialog, "AppRule_0").IsChecked);
            Assert.Equal("✓ Matches 1 open window", match.Text);

            PlaybackUiTests.Find<RadioButton>(dialog, "AppRule_2").IsChecked = true;
            Assert.Equal("*crosvm*", title.Text);
            Assert.Equal("No open window matches. The app may be closed, or the rule is wrong.", match.Text);

            title.Text = "*";
            Assert.Equal("Matches 2 open windows. Narrow the rule so playback can pick one.", match.Text);
            Assert.All(new[] { 0, 1, 2 }, i => Assert.False(PlaybackUiTests.Find<RadioButton>(dialog, "AppRule_" + i).IsChecked));

            title.Text = "CookieRun*";
            PlaybackUiTests.Click(dialog, "SaveApp");
            var saved = state.Profile.Apps.Last();
            Assert.Equal(("CookieRun: Crumble - Idle RPG", Crosvm, "CookieRun*"), (saved.Name, saved.Executable, saved.TitleRule));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task UnavailableCatalogKeepsManualEntry()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var (window, dialog) = await OpenAddApp(state, new ListedCatalog([]) { Fail = true });
        try
        {
            var status = PlaybackUiTests.Find<TextBlock>(dialog, "AppWindowStatus");
            await PlaybackControllerTests.Until(() => status.Text?.StartsWith("Window list unavailable. Enter details manually.") == true);
            Assert.Equal("", PlaybackUiTests.Find<TextBlock>(dialog, "AppRuleMatch").Text);
            PlaybackUiTests.Find<TextBox>(dialog, "AppName").Text = "Manual";
            PlaybackUiTests.Find<TextBox>(dialog, "AppExecutable").Text = @"C:\Games\manual.exe";
            PlaybackUiTests.Find<TextBox>(dialog, "AppTitleRule").Text = "Manual*";
            PlaybackUiTests.Click(dialog, "SaveApp");
            Assert.Equal("Manual", state.Profile.Apps.Last().Name);
        }
        finally { window.Close(); }
    }

    private static async Task<(MainWindow Window, Window Dialog)> OpenAddApp(WorkspaceState state, ListedCatalog catalog)
    {
        var keys = new HarmlessHotkeys();
        var service = new CompatibilityService(state.Document, catalog, () => null,
            (_, _, _) => throw new InvalidOperationException("Picker must not send input"), (_, _, _) => Task.CompletedTask);
        var ui = new CompatibilityUiServices(service, catalog, (_, _) => Task.FromResult<IReadOnlyList<TargetWindow>>([]), _ => new(null));
        var window = new MainWindow(state, new UiPlayback(state.Document), keys, compatibility: ui); window.Show();
        PlaybackUiTests.Click(window, "Tab_Apps");
        PlaybackUiTests.Click(window, "AddApp");
        await PlaybackControllerTests.Until(() => window.OwnedWindows.Count == 1);
        return (window, window.OwnedWindows[0]);
    }

    private static TargetWindow Window(string title, string executable) =>
        new(new TargetToken(Guid.NewGuid()), new AppIdentity(Path.GetFileNameWithoutExtension(executable), executable), title, false, new ClientGeometry(800, 600, 1));

    private sealed class ListedCatalog(IReadOnlyList<TargetWindow> windows) : IWindowCatalog, ITargetContext
    {
        public bool Fail { get; init; }
        public event Action<TargetToken>? TargetLost { add { } remove { } }
        public Task<IReadOnlyList<TargetWindow>> ListAsync(CancellationToken ct = default) =>
            Fail ? Task.FromException<IReadOnlyList<TargetWindow>>(new InvalidOperationException("listing failed")) : Task.FromResult(windows);
        public Task<ResolutionResult> ResolveAsync(TargetRule rule, TargetToken? selected = null, CancellationToken ct = default) => Task.FromResult<ResolutionResult>(new ResolutionResult.Missing());
        public ValueTask<TargetContextResult> GetAsync(TargetToken token, CancellationToken ct = default) => ValueTask.FromResult(new TargetContextResult(null));
    }
}
```

`HarmlessHotkeys` (`PlaybackUiTests.cs`) and `UiPlayback` (`HarmlessUi.cs`) already exist in the App test project. `CompatibilityService` takes an `ITargetContext` and `CompatibilityUiServices` takes an `IWindowCatalog`, so `ListedCatalog` implements both.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Macrofy.App.Tests --filter "FullyQualifiedName~AppWindowPickerUiTests"`
Expected: FAIL — `Find` throws `Sequence contains no matching element` for `AddApp`.

- [ ] **Step 3: Name the Add app button**

In `MainWindow.Panes.cs` `AppsPane`, replace:

```csharp
        body.Children.Add(Row(Text("Saved apps", size: 16), IconButton("plus", "Add app", () => AppDialog(null))));
```

with:

```csharp
        var addApp = IconButton("plus", "Add app", () => AppDialog(null)); addApp.Name = "AddApp";
        body.Children.Add(Row(Text("Saved apps", size: 16), addApp));
```

- [ ] **Step 4: Create the picker**

`src/Macrofy.App/Views/MainWindow.AppPicker.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Macrofy.App.Services;
using Macrofy.Platform.Models;
namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    /// <summary>Open-window list for the Add/Edit app dialog. Fills fields only; nothing is saved until Save app.</summary>
    private (Control Picker, TextBlock MatchLine) AppWindowPicker(TextBox name, TextBox executable, TextBox title)
    {
        IReadOnlyList<TargetWindow>? windows = null;
        var generation = 0; var applying = false;
        var list = new ListBox { Name = "AppWindowList", MaxHeight = 160 };
        var status = Wrap("", "muted", 12); status.Name = "AppWindowStatus";
        var suggestions = new StackPanel { Name = "AppRuleSuggestions", Spacing = 4 };
        var match = Wrap("", "muted", 12); match.Name = "AppRuleMatch";
        int Count(string exe, string rule) => windows?.Count(w => TitleRule.MatchesWindow(exe, rule, w)) ?? 0;
        void UpdateMatch()
        {
            if (windows is null) { match.Text = ""; return; }
            var count = Count(executable.Text?.Trim() ?? "", title.Text?.Trim() ?? "");
            (match.Text, var role) = count switch
            {
                1 => ("✓ Matches 1 open window", "success"),
                0 => ("No open window matches. The app may be closed, or the rule is wrong.", "warning"),
                _ => ($"Matches {count} open windows. Narrow the rule so playback can pick one.", "warning")
            };
            match.Foreground = palette.Brush(role);
        }
        void SetField(TextBox box, string value) { applying = true; box.Text = value; applying = false; }
        void ShowSuggestions(TargetWindow window)
        {
            suggestions.Children.Clear();
            var exe = window.App.ExecutablePath ?? "";
            var items = TitleRule.Suggestions(window.Title, exe);
            for (var i = 0; i < items.Count; i++)
            {
                var rule = items[i].Rule;
                var radio = new RadioButton { Name = "AppRule_" + i, Content = $"{rule}   ({items[i].Label} · matches {Count(exe, rule)})" };
                radio.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty && radio.IsChecked == true) { SetField(title, rule); UpdateMatch(); } };
                suggestions.Children.Add(radio);
            }
            ((RadioButton)suggestions.Children[0]).IsChecked = true;
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not PickedWindow picked) return;
            SetField(name, TitleRule.SuggestedName(picked.Window.Title));
            SetField(executable, picked.Window.App.ExecutablePath ?? "");
            ShowSuggestions(picked.Window);
        };
        title.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || applying) return;
            foreach (var radio in suggestions.Children.OfType<RadioButton>()) radio.IsChecked = false;
            UpdateMatch();
        };
        executable.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && !applying) UpdateMatch(); };
        async Task LoadAsync()
        {
            var version = ++generation;
            list.ItemsSource = null; suggestions.Children.Clear();
            if (compatibility is null) { status.Text = "Window list unavailable. Enter details manually."; return; }
            status.Text = "Loading open windows…";
            try
            {
                var listed = await compatibility.Catalog.ListAsync();
                if (version != generation) return;
                windows = listed;
                list.ItemsSource = listed.Select(w => new PickedWindow(w)).ToArray();
                status.Text = listed.Count == 0 ? "No open windows found." : "Pick a window to fill the fields below.";
            }
            catch (Exception error)
            {
                if (version != generation) return;
                windows = null; status.Text = "Window list unavailable. Enter details manually. " + error.Message;
            }
            UpdateMatch();
        }
        var refresh = TextButton("Refresh", () => _ = LoadAsync()); refresh.Name = "AppWindowRefresh";
        _ = LoadAsync();
        var picker = Stack(Row(Text("Pick an open window", "muted", 12), refresh), list, status, suggestions);
        return (picker, match);
    }

    private sealed record PickedWindow(TargetWindow Window)
    { public override string ToString() => $"{Window.Title}   ·   {Path.GetFileName(Window.App.ExecutablePath)}"; }
}
```

- [ ] **Step 5: Embed the picker in AppDialog**

In `MainWindow.Dialogs.cs`, replace the body of `AppDialog` from `var name = new TextBox` through `_ = dialog.ShowDialog(this);` with:

```csharp
        var name = new TextBox { Name = "AppName", Text = app?.Name ?? "", MinHeight = 34 };
        var executable = new TextBox { Name = "AppExecutable", Text = app?.Executable ?? "", MinHeight = 34 };
        var title = new TextBox { Name = "AppTitleRule", Text = app?.TitleRule ?? "*", MinHeight = 34 };
        var error = Text("", "danger", 12);
        var (picker, match) = AppWindowPicker(name, executable, title);
        var body = Stack(picker, Field("App name", name), Field("Executable path", executable), Field("Window title rule", title), match, error);
        var dialog = Dialog(app is null ? "Add app" : "Edit app", body);
        var save = IconButton("check", "Save app", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(executable.Text) || string.IsNullOrWhiteSpace(title.Text)) { error.Text = "Enter an app name, executable path and title rule."; return; }
            if (CompatibilityLocked || (app is not null && profile.Macros.Any(m => m.AppId == app.Id && Workspace.IsActive(m)))) return;
            var item = app ?? new SavedApp(); playback.SelectSurface(item.Id, null); item.Name = name.Text.Trim(); item.Executable = executable.Text.Trim(); item.TitleRule = title.Text.Trim();
            if (app is null) profile.Apps.Add(item);
            ResetCompatibilityContext();
            Save(); Render(); dialog.Close();
        }, "success"); save.Name = "SaveApp";
        body.Children.Add(Row(save, IconButton("close", "Cancel", dialog.Close)));
        _ = dialog.ShowDialog(this);
```

- [ ] **Step 6: Run the new UI tests**

Run: `dotnet test tests/Macrofy.App.Tests --filter "FullyQualifiedName~AppWindowPickerUiTests"`
Expected: PASS, 2 tests.

- [ ] **Step 7: Run the whole App test project**

Run: `dotnet test tests/Macrofy.App.Tests` (hands off the mouse)
Expected: 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/Macrofy.App/Views/MainWindow.AppPicker.cs src/Macrofy.App/Views/MainWindow.Dialogs.cs src/Macrofy.App/Views/MainWindow.Panes.cs tests/Macrofy.App.Tests/AppWindowPickerUiTests.cs
git commit -m "feat: pick an open window when adding an app"
```

---

### Task 4: Full verification and docs

**Files:**
- Modify: `README.md` (paragraph starting "The seven tabs are")

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Document the picker**

In `README.md`, after the sentence `Each macro can use one assigned app or **Screen (default)**.`, insert:

```markdown
Add app lists open windows: pick one to fill the app name, executable path, and title rule, choose a rule suggestion (without last title part, exact title, or program name), and check the live open-window match count before saving. Manual entry still works.
```

- [ ] **Step 2: Run locked Release verification**

Run: `powershell -NoProfile -File scripts/verify-probe.ps1` (popups appear; hands off the mouse)
Expected: build 0 warnings, 0 errors; Core 51/51, Windows 82/82, App all passed (117 + new tests).

- [ ] **Step 3: Update the README test count**

Replace the counts in the sentence `The 2026-10-01 locked Release verification passed **250 tests** (Core 51, App 117, Windows 82)` with the date and counts from Step 2's output.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: describe add-app window picker"
```
