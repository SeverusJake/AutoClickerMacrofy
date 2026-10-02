# Optional Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Window macros run without compatibility confirmation, the Compatibility tab hides behind a Settings switch, and the macro editor can capture click points.

**Architecture:** Remove the evidence checks from `WorkspacePlaybackController.TryBuild` and `WindowsPlaybackExecutor`, defaulting an unselected surface to the single matching window token. Add a persisted `ShowAdvancedTools` flag that filters the tab list. Add a `MainWindow.Capture.cs` partial that reads the screen or window-client pointer via `CompatibilityUiServices`.

**Tech Stack:** .NET 10, C#, Avalonia 12.1.3, xUnit v3 + Avalonia.Headless.XUnit.

Spec: `docs/superpowers/specs/2026-10-02-optional-compatibility-design.md`

## Global Constraints

- Copy (exact): `Show advanced tools (Compatibility tab)`; `Background clicks don't work in every game. Watch the first run.`; `Capture point in 5 s`; `Saved target app is missing.`; `No open window matches {name}. Open it first.`; `Several windows match {name}. Narrow its title rule on the Apps tab.`; `Capture gives pixels. Switch Coordinates to Fixed pixels.`
- `ShowAdvancedTools` defaults to false; workspace `Version` stays 1.
- Unsupported actions still fail with `UnsupportedCapability`; Screen/window geometry and identity checks unchanged.
- Run tests one assembly at a time (`-m:1`); native tests need the mouse untouched.
- Commits on `main`, messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Playback without evidence

**Files:**
- Modify: `src/Macrofy.App/Services/WindowsPlaybackExecutor.cs`
- Modify: `src/Macrofy.App/Services/WorkspacePlaybackController.cs:116-132`
- Modify: `src/Macrofy.App/Services/WorkspaceRuntime.cs:38`
- Modify tests: `tests/Macrofy.App.Tests/WindowsPlaybackExecutorTests.cs`, `tests/Macrofy.App.Tests/PlaybackUiTests.cs` (`PlaybackEligibilityTests`), `tests/Macrofy.App.Tests/FinalReviewUiTests.cs`, `tests/Macrofy.App.Tests/PlaybackIntegrationTests.cs:273`

**Interfaces:**
- Produces: `WindowsPlaybackExecutor(IWindowCatalog catalog, ITargetContext targets, Func<TargetToken, CancellationToken, Task<IReadOnlyList<TargetWindow>>> listSurfaces, WindowsGestureSender sender)` and `WindowsPlaybackExecutor(WindowsWindowCatalog catalog, WindowsGestureSender sender)`. `CompatibilityService.IsConfirmedAsync` stays (Compatibility tab records), unused by playback.

- [ ] **Step 1: Rewrite executor tests to the new behavior**

In `WindowsPlaybackExecutorTests.cs`:
- `ScreenNeedsNoEvidenceAndPreparationNeverSends`: change `new Fixture { Evidence = false }` to `new Fixture()`.
- Replace `UnselectedSurfaceNeedsUniqueEvidenceMatch` with:

```csharp
    [Fact]
    public async Task UnselectedSurfaceBindsMatchedWindowMainSurface()
    {
        var f = new Fixture();
        f.Surfaces[f.Window.Token] = [f.Window, f.Child];
        f.Contexts[f.Child.Token] = new(f.Child, false, "child-fingerprint");
        var result = await f.Executor.PrepareAsync(f.Request(selected: null), TestContext.Current.CancellationToken);
        Assert.Equal(f.Window.Token, result.Binding!.Token);
    }
```

- Replace `SuppliedRuleAndAppIdentityUsedAcrossProfilesAndAllCapabilitiesChecked` with:

```csharp
    [Fact]
    public async Task SuppliedRuleAndAppIdentityUsedAcrossProfilesWithoutEvidence()
    {
        var f = new Fixture();
        var appId = Guid.NewGuid(); var rule = new TargetRule(new("Other", "other.exe"), "Other*");
        f.Contexts[f.Window.Token] = f.Contexts[f.Window.Token] with { Window = f.Window with { App = rule.App, Title = "Other Game" } };
        var request = f.Request() with { Target = new(appId, rule, TargetState.BackgroundCovered, f.Window.Token), Actions = [new CompiledAction.Key([new("Ctrl"), new("A")], 0), new CompiledAction.Text("x", 0)] };
        var result = await f.Executor.PrepareAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(rule, f.Catalog.Rule);
        Assert.Equal(appId, result.Binding!.SavedAppId);
    }
```

- Replace `EvidenceRevocationStopsDispatchAndFixedEvidenceIsPerAction` with:

```csharp
    [Fact]
    public async Task WindowActionsDispatchWithoutCompatibilityEvidence()
    {
        var f = new Fixture();
        var binding = (await f.Executor.PrepareAsync(f.Request(), TestContext.Current.CancellationToken)).Binding!;
        Assert.True((await f.Executor.ExecuteAsync(binding, new CompiledAction.Key([new("Ctrl"), new("A")], 0), TestContext.Current.CancellationToken)).Delivery.Queued);
        Assert.True((await f.Executor.ExecuteAsync(binding, new CompiledAction.Click(new(10, 10), CoordinateMode.FixedPixels, 0), TestContext.Current.CancellationToken)).Delivery.Queued);
    }
```

- `SenderAllowsExplicitCompatibilityTestWithoutPriorEvidence`: change `new Fixture { Evidence = false }` to `new Fixture()`.
- Delete `FixedAndPercentageClicksUseIndependentEvidenceGeometryRequirements` (fixed-pixel geometry stays covered by `ResizedWindowRejectsFixedPixelsButRecalculatesPercentage`).
- In `Fixture`: change `public bool Ready = true, Evidence = true, PermissionAllowed = true, FixedEvidence = true;` to `public bool Ready = true, PermissionAllowed = true;`; delete the `EvidenceToken`, `AllowedCapabilities`, and `EvidenceQueries` fields; change the executor construction to:

```csharp
            Executor = new(Catalog, this, (token, _) => Task.FromResult(Surfaces.GetValueOrDefault(token) ?? []), Sender);
```

In `PlaybackUiTests.cs`, replace `PlaybackEligibilityTests.WindowRunRequiresEveryExactCapabilityStateAndCurrentRule` with:

```csharp
    [Fact]
    public async Task WindowRunNeedsNoEvidenceButRequiresAppAndState()
    {
        var document = WorkspaceDocument.CreateDefault(); var profile = document.Profiles[0]; var macro = profile.Macros[0];
        macro.Steps = [new("Key", "Space", 0), new("Text", "hello", 0)];
        var keys = new HarmlessHotkeys();
        await using var controller = new WorkspacePlaybackController(document, new(new HarmlessExecutor(), TimeProvider.System), new(), keys, keys);
        Assert.Empty(document.CompatibilityEvidence);
        Assert.True(controller.CanStart(profile, macro, out _));
        macro.WindowState = "Sideways"; Assert.False(controller.CanStart(profile, macro, out var reason)); Assert.Contains("state", reason);
        macro.WindowState = "Background"; macro.AppId = Guid.NewGuid();
        Assert.False(controller.CanStart(profile, macro, out reason)); Assert.Contains("missing", reason);
    }
```

In `FinalReviewUiTests.cs`:
- Delete `WindowPlaybackSerializesEvidenceWithUiEditsAndHonorsStop` (it asserted evidence reads during playback, which no longer happen).
- In `SelectedTextTestIgnoresOtherUnconfirmedOrInvalidAction`, rename to `SelectedTextTestIgnoresOtherInvalidAction` and replace `Assert.False(PlaybackUiTests.Find<Button>(window, "RunSelected").IsEnabled);` with `Assert.Equal(otherKey == "Space", PlaybackUiTests.Find<Button>(window, "RunSelected").IsEnabled);`, and the `SelectStep_1` assertion `Assert.False(PlaybackUiTests.Find<Button>(window, "TestSelected").IsEnabled);` with `Assert.Equal(otherKey == "Space", PlaybackUiTests.Find<Button>(window, "TestSelected").IsEnabled);`.

In `PlaybackIntegrationTests.cs:273`, change `new WindowsPlaybackExecutor(f.Catalog, sender, compatibility)` to `new WindowsPlaybackExecutor(f.Catalog, sender)`. Remove the now-unused `compatibility` local and the evidence seeding only if the compiler reports them unused; keep them otherwise.

- [ ] **Step 2: Build tests to verify they fail**

Run: `dotnet build tests/Macrofy.App.Tests`
Expected: FAIL with `CS1729` (no executor constructor takes 4/2 arguments).

- [ ] **Step 3: Implement executor changes**

In `WindowsPlaybackExecutor.cs`:
- Primary constructor: remove the `isConfirmed` parameter.
- Secondary constructor:

```csharp
    public WindowsPlaybackExecutor(WindowsWindowCatalog catalog, WindowsGestureSender sender)
        : this(catalog, catalog, async (token, ct) => (await catalog.ListInputSurfacesAsync(token, ct)).Select(s => s.Window).ToArray(), sender) { }
```

- In `PrepareAsync`, right after the `appId`/`state` check, add:

```csharp
        foreach (var action in request.Actions)
            if (CheckCapability(action) is { Queued: false } unsupported) return new(null, unsupported.Error);
```

- Replace the candidate collection with:

```csharp
        var candidates = new List<TargetWindow>();
        if (request.Target.SelectedSurface is null) candidates.AddRange(parents);
        else
            foreach (var parent in parents)
                candidates.AddRange((await listSurfaces(parent.Token, cancellationToken)).Where(s => s.Token == request.Target.SelectedSurface));
```

- Remove the `requirementsMet` evidence loop: after `validation` succeeds, call `matches.Add(binding);`.
- Final fallback error: `_ => new(null, failure ?? new("TargetMissing", "No usable input surface matches this macro's saved app rule."))`.
- In `ExecuteAsync`, replace the `if (check.Context is { } context) { ... }` block with:

```csharp
        if (check.Context is not null && CheckCapability(action) is { Queued: false } unsupported) return new(unsupported);
```

- Replace `CheckEvidenceAsync` with:

```csharp
    private static DeliveryResult CheckCapability(CompiledAction action) => action is CompiledAction.Wait or CompiledAction.Click or CompiledAction.Key or CompiledAction.Text or CompiledAction.Wheel
        ? new(true) : WindowsGestureSender.Fail("UnsupportedCapability", "Window playback does not support this action.");
```

In `WorkspaceRuntime.cs`, change `new WindowsPlaybackExecutor(catalog, sender, compatibility)` to `new WindowsPlaybackExecutor(catalog, sender)`.

In `WorkspacePlaybackController.cs`, delete the `foreach (var action in actions) { ... }` evidence loop inside `if (macro.AppId is { } appId)`.

- [ ] **Step 4: Build and run non-native App tests**

Run: `dotnet build Macrofy.sln` then `dotnet test tests/Macrofy.App.Tests --no-build --filter "FullyQualifiedName!~PlaybackIntegrationTests"`
Expected: 0 warnings, 0 errors; all pass. If `CS0219`/unused warnings appear in `PlaybackIntegrationTests`, remove the unused locals.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat: run window macros without compatibility confirmation"
```

---

### Task 2: Advanced tools switch

**Files:**
- Modify: `src/Macrofy.App/Models/WorkspaceDocument.cs:12`
- Modify: `src/Macrofy.App/Views/MainWindow.cs` (tab loop, key navigation)
- Modify: `src/Macrofy.App/Views/MainWindow.Panes.cs` (`SettingsPane`, `AboutPane`)
- Modify: `src/Macrofy.App/Views/MainWindow.Compatibility.cs:176` (intro copy)
- Create test: `tests/Macrofy.App.Tests/AdvancedToolsUiTests.cs`
- Modify tests: `NativeUiTests.cs:119`, `PlaybackUiTests.cs` (`CompatibilityUiTests`, `CompatibilityFixture`), `FinalReviewUiTests.cs` (`ActionSpecificHelpFollowsEditorAndCompatibilitySelection`)

**Interfaces:**
- Produces: `WorkspaceDocument.ShowAdvancedTools`; control name `ShowAdvancedTools` (CheckBox).

- [ ] **Step 1: Write the failing test**

`tests/Macrofy.App.Tests/AdvancedToolsUiTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class AdvancedToolsUiTests
{
    [AvaloniaFact]
    public void CompatibilityTabHiddenUntilAdvancedToolsEnabled()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var window = HarmlessUi.Create(state); window.Show();
        try
        {
            Assert.False(state.Document.ShowAdvancedTools);
            Assert.DoesNotContain(TabNames(window), n => n == "Tab_Compatibility");
            Assert.Equal(6, TabNames(window).Count);
            PlaybackUiTests.Click(window, "Tab_Settings");
            PlaybackUiTests.Find<CheckBox>(window, "ShowAdvancedTools").IsChecked = true;
            Assert.True(state.Document.ShowAdvancedTools);
            Assert.Contains(TabNames(window), n => n == "Tab_Compatibility");
            PlaybackUiTests.Click(window, "Tab_Compatibility");
            PlaybackUiTests.Click(window, "Tab_Settings");
            PlaybackUiTests.Find<CheckBox>(window, "ShowAdvancedTools").IsChecked = false;
            Assert.False(state.Document.ShowAdvancedTools);
            Assert.DoesNotContain(TabNames(window), n => n == "Tab_Compatibility");
        }
        finally { window.Close(); }
    }

    private static List<string> TabNames(Window window)
    {
        window.UpdateLayout();
        return window.GetVisualDescendants().OfType<Button>().Select(b => b.Name).Where(n => n?.StartsWith("Tab_") == true).Select(n => n!).ToList();
    }
}
```

`HarmlessUi.Create` passes no compatibility services, so the tab shows its unavailable message; this test only checks tab visibility and the persisted flag. The new intro copy is checked by `CompatibilityUiTests`, which already builds the pane with services.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Macrofy.App.Tests --filter "FullyQualifiedName~AdvancedToolsUiTests"`
Expected: FAIL to build — `WorkspaceDocument` has no `ShowAdvancedTools`.

- [ ] **Step 3: Implement**

`WorkspaceDocument.cs` after `CompatibilityEvidence`:

```csharp
    public bool ShowAdvancedTools { get; set; }
```

`MainWindow.cs`:
- Below `Tabs`, add:

```csharp
    private string[] VisibleTabs => Workspace.Document.ShowAdvancedTools ? Tabs : Tabs.Where(t => t != "Compatibility").ToArray();
```

- At the top of the tab-building block, before `var tabs = new StackPanel`, add `var visibleTabs = VisibleTabs; if (!visibleTabs.Contains(selectedTab)) selectedTab = "Settings";`.
- Loop over `visibleTabs` instead of `Tabs`; colour by original position: `var role = roles[Array.IndexOf(Tabs, tab)];` and use `role` where `roles[i]` was used.
- Key navigation: use `visibleTabs` instead of `Tabs` for `index`, `next`, and `selectedTab = visibleTabs[next]`.

`MainWindow.Panes.cs` `SettingsPane`, before `var body`:

```csharp
        var advanced = new CheckBox { Name = "ShowAdvancedTools", Content = "Show advanced tools (Compatibility tab)", IsChecked = Workspace.Document.ShowAdvancedTools, IsEnabled = !CompatibilityLocked };
        advanced.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty || CompatibilityLocked || (advanced.IsChecked == true) == Workspace.Document.ShowAdvancedTools) return;
            Workspace.Document.ShowAdvancedTools = advanced.IsChecked == true;
            if (!Workspace.Document.ShowAdvancedTools) ResetCompatibilityContext();
            Save(); Render();
        };
```

and append to the `Stack(...)` arguments: `Text("Advanced", size: 15), advanced, Text("Compatibility sends one test action and records what you saw. Run does not need it.", "muted", 12)`.

`AboutPane`: replace `Window playback requires confirmed capability tests.` with `Window playback sends input in the background; some games ignore it.`

`MainWindow.Compatibility.cs` intro `Wrap(...)` text becomes:
`"Optional tool. Send one harmless action and note whether the app reacted. Results are notes; Run does not need them. Macrofy does not activate or minimize the target; arrange the window yourself."`

Tests:
- `NativeUiTests.cs:119`: `7` → `6`.
- `CompatibilityUiTests` test and `CompatibilityFixture` constructor: set `state.Document.ShowAdvancedTools = true;` / `State.Document.ShowAdvancedTools = true;` before creating the window.
- `FinalReviewUiTests.ActionSpecificHelpFollowsEditorAndCompatibilitySelection`: add `state.Document.ShowAdvancedTools = true;` after creating `state`.

- [ ] **Step 4: Run App tests**

Run: `dotnet test tests/Macrofy.App.Tests --filter "FullyQualifiedName!~PlaybackIntegrationTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat: hide compatibility tab behind advanced tools setting"
```

---

### Task 3: Capture point in macro editor and soft note

**Files:**
- Modify: `src/Macrofy.App/Services/WorkspaceRuntime.cs:12-14,39-41`
- Create: `src/Macrofy.App/Views/MainWindow.Capture.cs`
- Modify: `src/Macrofy.App/Views/MainWindow.Macros.cs:38,93`
- Create test: `tests/Macrofy.App.Tests/CapturePointUiTests.cs`

**Interfaces:**
- Produces: `CompatibilityUiServices(..., Func<TargetToken, ScreenPointResult> ReadPointer, Func<ScreenPointResult>? ReadScreenPointer = null)`; `MainWindow.CaptureCountdownSeconds` (public int, default 5); control names `CapturePoint`, `CaptureStatus`, `MacroModeNote`.

- [ ] **Step 1: Write the failing tests**

`tests/Macrofy.App.Tests/CapturePointUiTests.cs`:

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

public sealed class CapturePointUiTests
{
    [AvaloniaFact]
    public async Task CaptureFillsWindowClientPointForWindowMacro()
    {
        var (state, window, catalog) = Open();
        try
        {
            var macro = state.Profile.Macros.Single(m => m.Name == "Collect rewards");
            PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            Assert.Equal("Background clicks don't work in every game. Watch the first run.", PlaybackUiTests.Find<TextBlock>(window, "MacroModeNote").Text);
            PlaybackUiTests.Click(window, "CapturePoint");
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBox>(window, "ActionValue").Text == "123, 45");
            PlaybackUiTests.Click(window, "ApplyAction");
            Assert.Equal("123, 45", macro.Steps[0].Value);
            Assert.Equal(catalog.Window.Token, catalog.ReadToken);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CaptureReadsScreenPointerAndReportsMissingWindow()
    {
        var (state, window, catalog) = Open();
        try
        {
            var macro = state.Profile.Macros.Single(m => m.Name == "Collect rewards");
            var cookie = macro.AppId; macro.AppId = null;
            PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "CapturePoint");
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBox>(window, "ActionValue").Text == "-10, 20");
            macro.AppId = cookie; catalog.Resolution = new ResolutionResult.Missing();
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "CapturePoint");
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBlock>(window, "CaptureStatus").Text == "No open window matches CookieRun. Open it first.");
            macro.Coordinates = "Percentage";
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            Assert.False(PlaybackUiTests.Find<Button>(window, "CapturePoint").IsEnabled);
        }
        finally { window.Close(); }
    }

    private static (WorkspaceState State, MainWindow Window, CaptureCatalog Catalog) Open()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var catalog = new CaptureCatalog();
        var keys = new HarmlessHotkeys();
        var service = new CompatibilityService(state.Document, catalog, () => null,
            (_, _, _) => throw new InvalidOperationException("Capture must not send input"), (_, _, _) => Task.CompletedTask);
        var ui = new CompatibilityUiServices(service, catalog, (_, _) => Task.FromResult<IReadOnlyList<TargetWindow>>([]),
            token => { catalog.ReadToken = token; return new(new(123, 45)); }, () => new(new(-10, 20)));
        var window = new MainWindow(state, new UiPlayback(state.Document), keys, compatibility: ui) { CaptureCountdownSeconds = 0 };
        window.Show();
        return (state, window, catalog);
    }

    private sealed class CaptureCatalog : IWindowCatalog, ITargetContext
    {
        public TargetWindow Window { get; } = new(new TargetToken(Guid.NewGuid()), new AppIdentity("crosvm", @"C:\Program Files\Google\Play Games\current\emulator\crosvm.exe"), "CookieRun: Crumble - Idle RPG - SeverusJake", false, new ClientGeometry(696, 1237, 1.25));
        public ResolutionResult? Resolution;
        public TargetToken? ReadToken;
        public event Action<TargetToken>? TargetLost { add { } remove { } }
        public Task<IReadOnlyList<TargetWindow>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TargetWindow>>([Window]);
        public Task<ResolutionResult> ResolveAsync(TargetRule rule, TargetToken? selected = null, CancellationToken ct = default) => Task.FromResult(Resolution ?? new ResolutionResult.Matched(Window));
        public ValueTask<TargetContextResult> GetAsync(TargetToken token, CancellationToken ct = default) => ValueTask.FromResult(new TargetContextResult(null));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Macrofy.App.Tests --filter "FullyQualifiedName~CapturePointUiTests"`
Expected: FAIL to build — no `CaptureCountdownSeconds`, `CompatibilityUiServices` takes 4 arguments.

- [ ] **Step 3: Implement**

`WorkspaceRuntime.cs` record:

```csharp
public sealed record CompatibilityUiServices(CompatibilityService Service, IWindowCatalog Catalog,
    Func<TargetToken, CancellationToken, Task<IReadOnlyList<TargetWindow>>> ListSurfaces,
    Func<TargetToken, ScreenPointResult> ReadPointer, Func<ScreenPointResult>? ReadScreenPointer = null);
```

and in `Create`, pass `screen.ReadPointer` as the fifth argument after the `ReadPointer` lambda.

`src/Macrofy.App/Views/MainWindow.Capture.cs`:

```csharp
using System.Globalization;
using Avalonia.Controls;
using Macrofy.App.Models;
using Macrofy.Platform.Models;
namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    private bool captureBusy;
    private string captureMessage = "";
    private TextBlock? captureStatus;
    /// <summary>Seconds to hover before a point is read. Capturing only reads the pointer; it never sends input.</summary>
    public int CaptureCountdownSeconds { get; set; } = 5;

    private Control CapturePointControl(Macro macro, ActionDraft draft)
    {
        var button = TextButton("Capture point in 5 s", () => _ = CapturePointAsync(macro, draft)); button.Name = "CapturePoint";
        captureStatus = Wrap(captureMessage, "muted", 12); captureStatus.Name = "CaptureStatus";
        var panel = new StackPanel { Spacing = 4, Children = { button, captureStatus } };
        void Update()
        {
            panel.IsVisible = draft.Kind == "Click" && compatibility is not null;
            var pixels = macro.Coordinates != "Percentage";
            button.IsEnabled = pixels && !captureBusy && !Workspace.IsActive(macro) && !CompatibilityLocked;
            ToolTip.SetTip(button, pixels ? "Hover over the spot to click. Macrofy reads the pointer; no input is sent." : "Capture gives pixels. Switch Coordinates to Fixed pixels.");
        }
        refreshPlayback.Add(Update); Update(); return panel;
    }

    private async Task CapturePointAsync(Macro macro, ActionDraft draft)
    {
        if (captureBusy || compatibility is null) return;
        var app = macro.AppId is { } id ? Workspace.Profile.Apps.SingleOrDefault(a => a.Id == id) : null;
        if (macro.AppId is not null && app is null) { SetCaptureMessage("Saved target app is missing."); return; }
        captureBusy = true; RefreshPlayback();
        try
        {
            for (var seconds = CaptureCountdownSeconds; seconds > 0; seconds--)
            {
                SetCaptureMessage($"Hover over the spot to click{(app is null ? "" : " in " + app.Name)}… {seconds}s. No input is sent.");
                await Task.Delay(1000);
            }
            var point = await ReadCapturePointAsync(app);
            if (point.Point is not { } p) { SetCaptureMessage(point.Error?.Message ?? "Pointer unavailable."); return; }
            draft.Value = string.Create(CultureInfo.InvariantCulture, $"{p.X}, {p.Y}");
            SetCaptureMessage($"Captured {draft.Value}. Apply change to save it.");
        }
        catch (Exception error) { SetCaptureMessage("Capture failed: " + error.Message); }
        finally { captureBusy = false; Render(); }
    }

    private async Task<ScreenPointResult> ReadCapturePointAsync(SavedApp? app)
    {
        if (app is null) return compatibility!.ReadScreenPointer?.Invoke() ?? new(null, new("Unavailable", "Screen pointer reading is unavailable."));
        var resolution = await compatibility!.Catalog.ResolveAsync(new TargetRule(new AppIdentity(app.Name, app.Executable), app.TitleRule));
        return resolution switch
        {
            ResolutionResult.Matched match => compatibility.ReadPointer(match.Window.Token),
            ResolutionResult.Ambiguous => new(null, new("TargetAmbiguous", $"Several windows match {app.Name}. Narrow its title rule on the Apps tab.")),
            _ => new(null, new("TargetMissing", $"No open window matches {app.Name}. Open it first."))
        };
    }

    private void SetCaptureMessage(string text) { captureMessage = text; if (captureStatus is not null) captureStatus.Text = text; }
}
```

`MainWindow.Macros.cs`:
- Line 38 becomes:

```csharp
        var modeNote = Text(macro.AppId is null ? "Visible desktop" : "Background clicks don't work in every game. Watch the first run.", "muted", 12); modeNote.Name = "MacroModeNote";
```

- In the inspector line that adds `Field("Position / value", valueInput)`, insert `inspector.Children.Add(CapturePointControl(macro, draft));` immediately after that field is added (split the chained `Add` calls so the capture control sits between the value field and `help`).

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Macrofy.App.Tests --filter "FullyQualifiedName!~PlaybackIntegrationTests"`
Expected: all pass, including `CapturePointUiTests` (2).

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat: capture click points from the macro editor"
```

---

### Task 4: Docs and full verification

**Files:**
- Modify: `README.md`
- Modify: `docs/verification/windows-input-compatibility.md` (one dated note)

- [ ] **Step 1: README**

- Replace `The seven tabs are Profiles, Apps, Macros, Compatibility, Settings, Log, and About.` with `The six tabs are Profiles, Apps, Macros, Settings, Log, and About. Settings → **Show advanced tools** adds the optional Compatibility tab.`
- In the **Test selected action** paragraph, replace `even if another action has an unsupported value or lacks capability confirmation` with `even if another action has an unsupported value`.
- Replace the paragraph starting `Window playback targets a saved executable/title rule` with:

```markdown
Window playback targets a saved executable/title rule and the one matching live window; several matches fail until the rule is narrowed. It uses the window's main input surface unless the optional Compatibility tab selected another for the session. It never brings the target forward, minimizes it, or falls back to Screen on failure. No compatibility confirmation is required: background clicks don't work in every game, so watch the first run. For a Click step, **Capture point in 5 s** reads the pointer (window client pixels for window macros, desktop pixels for Screen macros) without sending input. Changed executable/title/surface/state or geometry can invalidate playback or require coordinates to be checked again. Window Wheel uses the current pointer in the target client area; this can be unavailable for minimized windows.
```

- [ ] **Step 2: Verification note**

Append to `docs/verification/windows-input-compatibility.md` under `## Target rules and deliberate game observation`:

```markdown
**2026-10-02 change:** window playback no longer requires saved compatibility evidence. The Compatibility tab is optional (Settings → Show advanced tools) and records observations only.
```

- [ ] **Step 3: Full verification**

Run: `powershell -NoProfile -File scripts/verify-probe.ps1` (hands off the mouse when the popup appears)
Expected: 0 warnings, 0 errors; Core 51/51, Windows 82/82, App all pass.

- [ ] **Step 4: Update README test count** with the date and counts from Step 3, then commit:

```bash
git add README.md docs/verification/windows-input-compatibility.md
git commit -m "docs: describe optional compatibility and point capture"
```
