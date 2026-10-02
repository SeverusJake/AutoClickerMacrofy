# Record Point Hotkey and Simple Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture click points with a configurable record hotkey and turn the Compatibility tab into a send-one-test tool with no saved observations.

**Architecture:** Extend the platform hotkey set with an optional Capture binding registered only during recording. A `MainWindow.Recording.cs` partial owns one active recording and completes it on `HotkeyCommand.Capture`. `CompatibilityService` shrinks to `SendTestAsync`; evidence types and persistence are deleted.

**Tech Stack:** .NET 10, C#, Avalonia 12.1.3, xUnit v2 (Windows tests), xUnit v3 + Avalonia.Headless (App tests).

Spec: `docs/superpowers/specs/2026-10-02-record-point-and-simple-tests-design.md`

## Global Constraints

- Default record key `F7`; allowed F1–F11; distinct from Run/Pause/Stop.
- Copy: `Record point ({key})`; `Cancel recording`; `Hover over the spot and press {key}. Press the button again to cancel.`; `{Action} test`; `Sent one {action}. Watch the app.`
- Capture key registered only while recording.
- Old workspace files load; `CompatibilityEvidence` is ignored.
- Tests run one assembly at a time (`-m:1`). Commits on `main` end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Platform Capture binding

**Files:** Modify `src/Macrofy.Platform/Models/HotkeySet.cs`, `src/Macrofy.Platform.Windows/WindowsGlobalHotkeys.cs:46`; Test `tests/Macrofy.Platform.Windows.Tests/GlobalHotkeyTests.cs`.

**Produces:** `HotkeyCommand.Capture`; `HotkeySet(HotkeyBinding Run, HotkeyBinding Pause, HotkeyBinding Stop, HotkeyBinding? Capture = null)`.

- [ ] **Step 1: Failing test** (append to `GlobalHotkeyTests`):

```csharp
    [Fact] public void OptionalCaptureRegistersRoutesAndUnregisters()
    {
        var native = new FakeNative(); using var service = new WindowsGlobalHotkeys(native);
        Assert.True(service.Configure(Defaults with { Capture = new("F7") }).Registered);
        Assert.Contains(native.Keys.Values, k => k.Key == 118);
        var commands = new ConcurrentQueue<HotkeyCommand>(); service.Triggered += commands.Enqueue;
        native.Messages.Enqueue((0x312, (nuint)native.Keys.Single(x => x.Value.Key == 118).Key));
        Assert.True(SpinWait.SpinUntil(() => commands.Contains(HotkeyCommand.Capture), 1000));
        Assert.True(service.Configure(Defaults).Registered);
        Assert.DoesNotContain(native.Keys.Values, k => k.Key == 118);
        Assert.False(service.Configure(Defaults with { Capture = new("F9") }).Registered);
    }
```

- [ ] **Step 2:** `dotnet test tests/Macrofy.Platform.Windows.Tests --filter OptionalCapture` → build FAIL (no `Capture`).
- [ ] **Step 3: Implement.** `HotkeySet.cs`: `public enum HotkeyCommand { Run, Pause, Stop, Capture }` and `public sealed record HotkeySet(HotkeyBinding Run, HotkeyBinding Pause, HotkeyBinding Stop, HotkeyBinding? Capture = null);`. In `WindowsGlobalHotkeys.Configure`, replace the binding array with:

```csharp
            var bindings = new List<(HotkeyBinding Binding, HotkeyCommand Command)> { (hotkeys.Run, HotkeyCommand.Run), (hotkeys.Pause, HotkeyCommand.Pause), (hotkeys.Stop, HotkeyCommand.Stop) };
            if (hotkeys.Capture is { } capture) bindings.Add((capture, HotkeyCommand.Capture));
            foreach(var (binding,command) in bindings)
```

- [ ] **Step 4:** Run `dotnet test tests/Macrofy.Platform.Windows.Tests --filter GlobalHotkeyTests` → PASS (FakeNative keys keyed by id; `native.Keys` reflects unregister).
- [ ] **Step 5:** Commit `feat: optional capture hotkey binding`.

---

### Task 2: Record-point shortcut setting

**Files:** Modify `src/Macrofy.App/Models/WorkspaceDocument.cs` (`ShortcutSettings`), `src/Macrofy.App/Services/WorkspaceStore.cs` (load repair), `src/Macrofy.App/Views/MainWindow.cs` (`ShortcutInput`), `src/Macrofy.App/Views/MainWindow.Playback.cs` (`CaptureShortcut`, `GlobalCommand` mapping), `src/Macrofy.App/Views/MainWindow.Panes.cs` (Settings field); Tests `tests/Macrofy.App.Tests/RecordPointUiTests.cs`, `tests/Macrofy.App.Tests/WorkspaceStateTests.cs`.

**Produces:** `ShortcutSettings.Capture` (string, default `"F7"`); `WorkspaceStore` repairs Capture on load; Settings control `Shortcut_Capture`.

- [ ] **Step 1: Failing tests.** In `WorkspaceStateTests` add:

```csharp
    [Theory]
    [InlineData(null, "F9", "F7")]
    [InlineData("F9", "F9", "F7")]
    [InlineData("F7", "F7", "F1")]
    public void LoadRepairsMissingOrDuplicateRecordKey(string? capture, string run, string expected)
    {
        var folder = Path.Combine(Path.GetTempPath(), "macrofy-capture-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(folder);
            var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(WorkspaceDocument.CreateDefault()))!;
            json["Shortcuts"]!["Run"] = run; json["Shortcuts"]!["Capture"] = capture;
            json["CompatibilityEvidence"] = new System.Text.Json.Nodes.JsonArray();
            File.WriteAllText(Path.Combine(folder, "ui-workspace.json"), json.ToJsonString());
            var store = new WorkspaceStore(folder); var document = store.Load();
            Assert.Null(store.LoadError); Assert.Equal(expected, document.Shortcuts.Capture);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
```

Delete the two `CompatibilityEvidence` rows from `StructurallyInvalidDocumentsRemainPreserved` and the `VersionOneWithoutEvidenceLoadsEmpty` test.

`tests/Macrofy.App.Tests/RecordPointUiTests.cs` (first test; Task 4 adds more):

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class RecordPointUiTests
{
    [AvaloniaFact]
    public void SettingsShowsAndChangesRecordKey()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var keys = new HarmlessHotkeys();
        var window = new Macrofy.App.Views.MainWindow(state, new UiPlayback(state.Document), keys); window.Show();
        try
        {
            PlaybackUiTests.Click(window, "Tab_Settings");
            var input = PlaybackUiTests.Find<TextBox>(window, "Shortcut_Capture");
            Assert.Equal("F7", input.Text);
            input.Focus(); keys.Trigger(HotkeyCommand.Run);
            Assert.Equal("F7", state.Document.Shortcuts.Capture);
            window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.F6, Source = input });
            Assert.Equal("F6", state.Document.Shortcuts.Capture);
            Assert.Null(keys.Last!.Capture);
        }
        finally { window.Close(); }
    }
}
```

`HarmlessHotkeys` (in `PlaybackUiTests.cs`) records the last set: `public HotkeySet? Last { get; private set; }` and `Configure(HotkeySet set) { Last = set; return new(IsOperational, OperationalError); }`.

- [ ] **Step 2:** Run the two tests → build FAIL (no `Capture`).
- [ ] **Step 3: Implement.**
  - `ShortcutSettings`: `public string Capture { get; set; } = "F7";`
  - `WorkspaceStore.Load`, after the Run/Pause/Stop shortcut check:

```csharp
            var used = new[] { result.Shortcuts.Run, result.Shortcuts.Pause, result.Shortcuts.Stop };
            if (result.Shortcuts.Capture is not { } capture || !System.Text.RegularExpressions.Regex.IsMatch(capture, "^F(?:[1-9]|1[01])$") || used.Contains(capture, StringComparer.OrdinalIgnoreCase))
                result.Shortcuts.Capture = new[] { "F7" }.Concat(Enumerable.Range(1, 11).Select(n => "F" + n)).First(key => !used.Contains(key, StringComparer.OrdinalIgnoreCase));
```

  - Remove the `CompatibilityEvidence` validation block (Task 3 deletes the property; do this edit there if it does not compile yet).
  - `MainWindow.ShortcutInput` text restore: `input.Text = action switch { "Run" => ...Run, "Pause" => ...Pause, "Capture" => Workspace.Document.Shortcuts.Capture, _ => ...Stop };`
  - `CaptureShortcut`: copy `Capture = current.Capture` into `candidate`; set `candidate.Capture = key` when `action == "Capture"`; distinct check over all four keys (`.Distinct().Count() != 4`); skip `ConfigureHotkeys` for a Capture-only change (it registers during recording).
  - `GlobalCommand` capturing-shortcut mapping adds `HotkeyCommand.Capture => keys?.Capture?.Key`.
  - Settings: add row 3 to the shortcuts grid — `Field("Record point (while recording)", ShortcutInput("Capture", Workspace.Document.Shortcuts.Capture))`; grid `RowDefinitions = new("Auto,Auto,Auto,Auto")`.
- [ ] **Step 4:** Run App tests (non-native) → PASS.
- [ ] **Step 5:** Commit `feat: configurable record-point shortcut`.

---

### Task 3: Compatibility tab sends tests only

**Files:** Delete `src/Macrofy.App/Models/CompatibilityEvidence.cs`; rewrite `src/Macrofy.App/Services/CompatibilityService.cs`; modify `WorkspaceDocument.cs`, `WorkspaceRuntime.cs`, `WorkspaceStore.cs`, `MainWindow.Compatibility.cs`, `MainWindow.Playback.cs`, `MainWindow.Dialogs.cs`, `MainWindow.Panes.cs` (callers of `DiscardCompatibility`); rewrite `tests/Macrofy.App.Tests/CompatibilityServiceTests.cs`; update `PlaybackUiTests.cs`, `FinalReviewUiTests.cs`, `AppWindowPickerUiTests.cs`, `CapturePointUiTests.cs`.

**Produces:**

```csharp
public sealed record CompatibilityTestResult(GestureResult Result, TargetState State, InputCapability Capability);
public sealed class CompatibilityService(WorkspaceDocument document, ITargetContext targets, Func<IDisposable?> acquireTestLease,
    Func<PlaybackBinding, CompiledAction, CancellationToken, ValueTask<GestureResult>> sendGesture)
{
    public Task<CompatibilityTestResult> SendTestAsync(Guid appId, TargetToken token, CompiledAction action, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Failing service tests.** Replace `CompatibilityServiceTests.cs` with:

```csharp
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Xunit;
namespace Macrofy.App.Tests;
public class CompatibilityServiceTests
{
    private sealed class ContextSource : ITargetContext
    {
        public TargetContext? Context;
        public ValueTask<TargetContextResult> GetAsync(TargetToken target, CancellationToken cancellationToken = default) => ValueTask.FromResult(new TargetContextResult(Context));
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Fixture
    {
        public WorkspaceDocument Document = WorkspaceDocument.CreateDefault();
        public ContextSource Source = new();
        public Guid AppId => Document.Profiles[0].Apps[0].Id;
        public TargetToken Token = new(Guid.NewGuid());
        public bool Busy, Occupied;
        public List<PlaybackBinding> Sent = [];
        public Func<GestureResult> Result = () => new(new(true));
        public Fixture() => Source.Context = new(new(Token, new("CookieRun", Document.Profiles[0].Apps[0].Executable), "CookieRun: Crumble - Idle RPG", true, new(800, 600, 1)), false, "surface");
        public CompatibilityService Service() => new(Document, Source, () => { if (Busy) return null; Occupied = true; return new Lease(() => Occupied = false); },
            (binding, _, _) => { Assert.True(Occupied); Sent.Add(binding); return ValueTask.FromResult(Result()); });
        public CompiledAction Click = new CompiledAction.Click(new(20, 30), CoordinateMode.FixedPixels, 0);
    }
    [Fact]
    public async Task SendsOneActionWithWindowStateAndReleasesInput()
    {
        var f = new Fixture(); var service = f.Service();
        var result = await service.SendTestAsync(f.AppId, f.Token, f.Click, TestContext.Current.CancellationToken);
        Assert.True(result.Result.Delivery.Queued); Assert.Equal(TargetState.Minimized, result.State); Assert.Equal(InputCapability.Click, result.Capability);
        Assert.Equal(f.Token, Assert.Single(f.Sent).Token); Assert.False(f.Occupied);
        f.Source.Context = f.Source.Context! with { Window = f.Source.Context.Window with { IsMinimized = false } };
        Assert.Equal(TargetState.BackgroundVisible, (await service.SendTestAsync(f.AppId, f.Token, new CompiledAction.Key([new("Ctrl"), new("A")], 0), TestContext.Current.CancellationToken)) is { Capability: InputCapability.Shortcut } r ? r.State : default);
    }
    [Fact]
    public async Task BusyWrongAppOutsidePointAndCancelledTestsSendNothing()
    {
        var f = new Fixture(); var service = f.Service(); f.Busy = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(f.AppId, f.Token, f.Click, TestContext.Current.CancellationToken));
        f.Busy = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(Guid.NewGuid(), f.Token, f.Click, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(f.AppId, f.Token, new CompiledAction.Click(new(double.NaN, 20), CoordinateMode.FixedPixels, 0), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(f.AppId, f.Token, new CompiledAction.Click(new(900, 20), CoordinateMode.FixedPixels, 0), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendTestAsync(f.AppId, f.Token, f.Click, new(true)));
        f.Source.Context = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestAsync(f.AppId, f.Token, f.Click, TestContext.Current.CancellationToken));
        Assert.Empty(f.Sent); Assert.False(f.Occupied);
    }
}
```

- [ ] **Step 2:** Build → FAIL (`SendTestAsync` missing).
- [ ] **Step 3: Implement service.** Rewrite `CompatibilityService.cs`:

```csharp
using Macrofy.App.Models;
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
namespace Macrofy.App.Services;

public sealed record CompatibilityTestResult(GestureResult Result, TargetState State, InputCapability Capability);

/// <summary>Sends one deliberate test action to a selected live surface. Nothing is recorded.</summary>
public sealed class CompatibilityService(WorkspaceDocument document, ITargetContext targets, Func<IDisposable?> acquireTestLease,
    Func<PlaybackBinding, CompiledAction, CancellationToken, ValueTask<GestureResult>> sendGesture)
{
    public async Task<CompatibilityTestResult> SendTestAsync(Guid appId, TargetToken token, CompiledAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = acquireTestLease() ?? throw new InvalidOperationException("Input is busy.");
        var context = (await targets.GetAsync(token, cancellationToken)).Context;
        if (context is null || context.Window.Token != token || !ValidContext(appId, context)) throw new InvalidOperationException("Selected target identity, surface or geometry is invalid.");
        var capability = action switch { CompiledAction.Click => InputCapability.Click, CompiledAction.Key k => k.Keys.Count > 1 ? InputCapability.Shortcut : InputCapability.Key, CompiledAction.Text => InputCapability.Text, CompiledAction.Wheel => InputCapability.Wheel, _ => throw new InvalidOperationException("Select an input action to test.") };
        if (action is CompiledAction.Click click && (!double.IsFinite(click.Point.X) || !double.IsFinite(click.Point.Y) || click.Point.X < 0 || click.Point.Y < 0 ||
            (click.Mode == CoordinateMode.FixedPixels ? click.Point.X >= context.Window.Geometry!.Width || click.Point.Y >= context.Window.Geometry.Height : click.Point.X > 100 || click.Point.Y > 100)))
            throw new InvalidOperationException("Test point is outside the window's client area.");
        var state = context.Window.IsMinimized ? TargetState.Minimized : TargetState.BackgroundVisible;
        var binding = new PlaybackBinding(appId, token, context.Window.Title, context.SurfaceFingerprint, context.Window.Geometry, state);
        var result = await sendGesture(binding, action, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(result, state, capability);
    }
    private bool ValidContext(Guid appId, TargetContext context)
    {
        var app = document.Profiles.SelectMany(p => p.Apps).SingleOrDefault(a => a.Id == appId);
        return app is not null && context.Window.Token.Id != Guid.Empty && !string.IsNullOrWhiteSpace(context.SurfaceFingerprint) &&
            context.Window.Geometry is { Width: > 0, Height: > 0, DpiScale: > 0 } geometry && double.IsFinite(geometry.DpiScale) &&
            TitleRule.MatchesWindow(app.Executable, app.TitleRule, context.Window);
    }
}
```

  Delete `CompatibilityEvidence.cs`, `WorkspaceDocument.CompatibilityEvidence`, store evidence validation, `WorkspaceRuntime.ReadAsync`/`PersistAsync`, and the persist/read arguments in `WorkspaceRuntime.Create`: `new CompatibilityService(document, catalog, () => activity.TryEnterTest(out var lease) ? lease : null, sender.SendGestureAsync)`.

- [ ] **Step 4: Rewrite the Compatibility pane.** In `MainWindow.Compatibility.cs`: remove `compatibilityAttempt`, `DiscardCompatibility`, the state combo, `yes`/`no`/`discard` buttons, `ConfirmObservation`. `CompatibilityLocked => compatibilityBusy`. Default `action.SelectedItem = "Click"`. Test button: `var test = TextButton("Click test", ...)`, `test.Content = (action.SelectedItem as string ?? "Click") + " test"` in `UpdateControls`; enabled when `!locked && Workspace.ActiveCount == 0 && playback.HotkeysReady && app/surface selected && TryAction`. Click handler:

```csharp
            compatibilityWork = RunOperation(async ct =>
            {
                var sent = await compatibility.Service.SendTestAsync(selectedApp.Id, selected.Window.Token, compiled!, ct);
                var delivery = sent.Result.Delivery;
                var error = delivery.Error ?? delivery.CleanupError ?? sent.Result.CleanupError;
                status.Text = delivery.Queued && error is null ? $"Sent one {sent.Capability.ToString().ToLowerInvariant()}. Watch the app." : "Test failed: " + (error?.Message ?? "input was not delivered.");
            });
```

  The capture button is replaced by the shared Record point button in Task 4 (keep the 5-second capture until then). Update callers: replace `DiscardCompatibility()` calls in `StopAll`, `Suspend`, `GlobalCommand`, `PlaybackChanged`, `OnClosing`, `ResetCompatibilityContext`, `SettingsPane` with `refreshCompatibility?.Invoke()` or delete where a refresh follows. `CanRun` message becomes `Wait for the compatibility test to finish.`
- [ ] **Step 5: Update tests.**
  - `PlaybackUiTests.cs`: rewrite `CompatibilityUiTests` test as `TestButtonSendsOneSelectedActionAndSavesNothing` (select app/window/surface, Action `Key`, value `Space`; button content `Key test`; click → 1 send, status `Sent one key. Watch the app.`; `ObservedWorking`, `CompatibilityState` absent; then Run uses the selected surface token as before). Delete `CandidateSaveAndPublishShareDispatcherTurnAndRetainEarlierUnrelatedSave` and `ClosingDuringCommittedObservationWaitsForSaveAndKeepsExclusiveOwner`. Rewrite `FailedCleanupNeverEnablesObservationAndReleasesExclusiveOwner` as `FailedCleanupReportsErrorAndReleasesInput` (status contains `Key release failed`; guard free). `CompatibilityFixture`: drop `persist` and state selection. Remove `AddEvidence` and its callers in `PlaybackUiTests.cs`/`FinalReviewUiTests.cs`. Delete `FinalReviewUiTests.WorkerEvidenceReadWaitsForUiWorkspaceMutationsAndCancellation`.
  - Construction sites `new CompatibilityService(..., persist[, read])` in `AppWindowPickerUiTests`, `CapturePointUiTests`, `FinalReviewUiTests`: drop the persistence argument.
- [ ] **Step 6:** Build + non-native App tests → PASS.
- [ ] **Step 7:** Commit `refactor: compatibility tab sends single tests without saving observations`.

---

### Task 4: Record point in macro editor and Compatibility tab

**Files:** Delete `src/Macrofy.App/Views/MainWindow.Capture.cs` and `tests/Macrofy.App.Tests/CapturePointUiTests.cs`; create `src/Macrofy.App/Views/MainWindow.Recording.cs`; modify `MainWindow.Macros.cs`, `MainWindow.Compatibility.cs`, `MainWindow.Playback.cs`; tests in `RecordPointUiTests.cs`.

**Produces:** control names `RecordPoint`, `RecordStatus` (macro editor) and `CompatibilityRecordPoint` (Compatibility); `private Control RecordPointControl(string name, Func<bool> available, Func<Task<ScreenPointResult>> read, Action<PointerPoint> captured, TextBlock status)`.

- [ ] **Step 1: Failing tests** (append to `RecordPointUiTests`; reuse the `CaptureCatalog` fake from the deleted `CapturePointUiTests`, moved here):

```csharp
    [AvaloniaFact]
    public async Task HotkeyRecordsWindowAndScreenPointsAndReportsMissingWindow()
    {
        var (state, window, catalog, keys) = Open();
        try
        {
            var macro = state.Profile.Macros.Single(m => m.Name == "Collect rewards");
            PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "RecordPoint");
            Assert.Equal("F7", keys.Last!.Capture!.Key);
            Assert.Equal("Hover over the spot and press F7. Press the button again to cancel.", PlaybackUiTests.Find<TextBlock>(window, "RecordStatus").Text);
            keys.Trigger(HotkeyCommand.Capture);
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBox>(window, "ActionValue").Text == "123, 45");
            Assert.Null(keys.Last!.Capture);
            PlaybackUiTests.Click(window, "ApplyAction"); Assert.Equal("123, 45", macro.Steps[0].Value);
            var cookie = macro.AppId; macro.AppId = null;
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "RecordPoint"); keys.Trigger(HotkeyCommand.Capture);
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBox>(window, "ActionValue").Text == "-10, 20");
            macro.AppId = cookie; catalog.Resolution = new ResolutionResult.Missing();
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "RecordPoint"); keys.Trigger(HotkeyCommand.Capture);
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBlock>(window, "RecordStatus").Text == "No open window matches CookieRun. Open it first.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CancelUnregistersAndPercentageDisablesRecording()
    {
        var (state, window, _, keys) = Open();
        try
        {
            var macro = state.Profile.Macros.Single(m => m.Name == "Collect rewards");
            PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "RecordPoint");
            Assert.Equal("Cancel recording", PlaybackUiTests.Find<Button>(window, "RecordPoint").Content);
            PlaybackUiTests.Click(window, "RecordPoint");
            Assert.Null(keys.Last!.Capture); Assert.Equal("Record point (F7)", PlaybackUiTests.Find<Button>(window, "RecordPoint").Content);
            keys.Trigger(HotkeyCommand.Capture); Assert.Equal("600, 420", PlaybackUiTests.Find<TextBox>(window, "ActionValue").Text);
            macro.Coordinates = "Percentage";
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            Assert.False(PlaybackUiTests.Find<Button>(window, "RecordPoint").IsEnabled);
        }
        finally { window.Close(); }
    }
```

with `Open()` returning `(state, window, catalog, keys)` built like the deleted `CapturePointUiTests.Open` (no `CaptureCountdownSeconds`).

- [ ] **Step 2:** Build → FAIL (`RecordPoint` not found / `MainWindow.Capture.cs` still present).
- [ ] **Step 3: Implement `MainWindow.Recording.cs`:**

```csharp
using System.Globalization;
using Avalonia.Controls;
using Macrofy.App.Models;
using Macrofy.Platform.Models;
namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    private sealed record PointRecording(Func<Task<ScreenPointResult>> Read, Action<PointerPoint> Captured, Action<string> Report);
    private PointRecording? recording;

    /// <summary>Record point button: registers the record key only while waiting, reads the pointer on that key, never sends input.</summary>
    private Button RecordPointButton(string name, Func<bool> available, Func<Task<ScreenPointResult>> read, Action<PointerPoint> captured, Action<string> report)
    {
        var button = TextButton("", () => { });
        button.Name = name;
        PointRecording? mine = null;
        void Update()
        {
            var active = mine is not null && ReferenceEquals(recording, mine);
            button.Content = active ? "Cancel recording" : $"Record point ({Workspace.Document.Shortcuts.Capture})";
            button.IsEnabled = active || (recording is null && available() && Workspace.ActiveCount == 0 && !CompatibilityLocked && playback.HotkeysReady);
        }
        button.Click += (_, _) =>
        {
            if (mine is not null && ReferenceEquals(recording, mine)) { EndRecording(); report(""); }
            else if (recording is null)
            {
                mine = new(read, captured, report);
                if (StartRecording(mine)) report($"Hover over the spot and press {Workspace.Document.Shortcuts.Capture}. Press the button again to cancel.");
            }
            RefreshPlayback();
        };
        refreshPlayback.Add(Update); Update(); return button;
    }

    private bool StartRecording(PointRecording next)
    {
        var shortcuts = Workspace.Document.Shortcuts;
        var set = new HotkeySet(new(shortcuts.Run), new(shortcuts.Pause), new(shortcuts.Stop), new(shortcuts.Capture));
        var result = hotkeys.Configure(set);
        if (!result.Registered) { next.Report("Record key unavailable: " + (result.Error?.Message ?? "registration failed.")); hotkeys.Configure(set with { Capture = null }); return false; }
        registeredKeys = set; recording = next; return true;
    }

    private void EndRecording()
    {
        if (recording is null) return;
        recording = null;
        var shortcuts = Workspace.Document.Shortcuts;
        var set = new HotkeySet(new(shortcuts.Run), new(shortcuts.Pause), new(shortcuts.Stop));
        if (hotkeys.Configure(set).Registered) registeredKeys = set;
    }

    private async Task CompleteRecordingAsync()
    {
        if (recording is not { } current) return;
        EndRecording();
        try
        {
            var point = await current.Read();
            if (point.Point is { } p) current.Captured(p); else current.Report(point.Error?.Message ?? "Pointer unavailable.");
        }
        catch (Exception error) { current.Report("Record failed: " + error.Message); }
        RefreshPlayback();
    }

    private async Task<ScreenPointResult> ReadMacroPointAsync(SavedApp? app)
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

    private static string FormatPoint(PointerPoint point) => string.Create(CultureInfo.InvariantCulture, $"{point.X}, {point.Y}");
}
```

  Macro editor (`MainWindow.Macros.cs`): replace `CapturePointControl(macro, draft)` with:

```csharp
            var recordStatus = Wrap(recordMessage, "muted", 12); recordStatus.Name = "RecordStatus";
            var recordApp = macro.AppId is { } recordAppId ? Workspace.Profile.Apps.SingleOrDefault(a => a.Id == recordAppId) : null;
            var record = RecordPointButton("RecordPoint",
                () => draft.Kind == "Click" && macro.Coordinates != "Percentage" && !Workspace.IsActive(macro) && compatibility is not null && (macro.AppId is null || recordApp is not null),
                () => ReadMacroPointAsync(recordApp),
                point => { draft.Value = FormatPoint(point); recordMessage = $"Recorded {draft.Value}. Apply change to save it."; Render(); },
                text => { recordMessage = text; recordStatus.Text = text; });
            var recordPanel = new StackPanel { Spacing = 4, Children = { record, recordStatus } };
            refreshPlayback.Add(() => recordPanel.IsVisible = draft.Kind == "Click" && compatibility is not null);
            inspector.Children.Add(recordPanel);
```

  plus field `private string recordMessage = "";` in `MainWindow.Recording.cs`. Tooltip for the disabled Percentage case: `Recording gives pixels. Switch Coordinates to Fixed pixels.`

  Compatibility pane: replace the 5-second capture button with `RecordPointButton("CompatibilityRecordPoint", () => surface.SelectedItem is WindowOption && action.SelectedItem as string == "Click", () => Task.FromResult(surface.SelectedItem is WindowOption option ? compatibility.ReadPointer(option.Window.Token) : new ScreenPointResult(null, new("NoSurface", "Select an input surface."))), point => value.Text = FormatPoint(point), text => status.Text = text)`.

  `GlobalCommand`: on `HotkeyCommand.Capture` post `_ = CompleteRecordingAsync()`; on `Stop` also post `EndRecording()`. `StopAll`, `Suspend`, `OnClosing`: call `EndRecording()`. `Render` of a new profile/tab keeps the recording (its callbacks update drafts and re-render).
- [ ] **Step 4:** Build + non-native App tests → PASS; delete `CapturePointUiTests.cs` and `MainWindow.Capture.cs`.
- [ ] **Step 5:** Commit `feat: record click points with a hotkey`.

---

### Task 5: Docs and verification

- [ ] README: replace "**Capture point in 5 s** reads the pointer" with "**Record point (F7)** then pressing the record key over the spot reads the pointer"; Settings lists Record point; Compatibility paragraph: optional tab sends one Click/Key/Shortcut/Text/Wheel test, nothing saved. Verification doc: dated 2026-10-02 note that observations are no longer recorded.
- [ ] `powershell -NoProfile -File scripts/verify-probe.ps1` → 0 warnings, all pass. Update README count.
- [ ] Commit `docs: record point hotkey and single tests`, publish exe, push.
