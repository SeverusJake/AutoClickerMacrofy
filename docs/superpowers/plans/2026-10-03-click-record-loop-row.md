# Click Record, Loop Row, Surface Labels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record points with a blocked left click, edit loop/interval from the Profiles row, and label input surfaces clearly.

**Architecture:** A platform `IPointClickSource` backed by a temporary `WH_MOUSE_LL` hook with a pure `ClickFilter`; `MainWindow.Recording.cs` switches from the hotkey to the click source. Record-key code is removed. Profiles row and Compatibility surface list gain small UI changes.

**Tech Stack:** .NET 10, C#, Avalonia 12.1.3, xUnit.

Spec: `docs/superpowers/specs/2026-10-03-click-record-loop-row-design.md`

## Global Constraints

- Copy: `Record point`; `Cancel recording`; `Click the spot to record it. That click is not sent to the app. Click Cancel recording to stop.`; `Main · {W}×{H} (default)`; `Child · {W}×{H} · {id8}`; headers `Loop`, `Interval (s)`.
- Hook installed only while recording; clicks on Macrofy windows pass through.
- Tests run with `-m:1`; commits on `main` end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Platform click source

**Files:** Create `src/Macrofy.Platform/IPointClickSource.cs`, `src/Macrofy.Platform.Windows/WindowsPointClickSource.cs`; Test `tests/Macrofy.Platform.Windows.Tests/ClickFilterTests.cs`.

**Produces:**

```csharp
public interface IPointClickSource
{
    /// <summary>Listens for one left click outside this app; the click is not delivered. Null on success.</summary>
    PlatformError? Start(Action clicked);
    void Cancel();
}
internal enum ClickDecision { Pass, Record, Swallow }
internal sealed class ClickFilter(Func<int, int, bool> ownsPoint) { public ClickDecision Handle(uint message, int x, int y); public bool Finished { get; } }
```

- [ ] **Step 1: Failing tests** `ClickFilterTests.cs`:

```csharp
using Macrofy.Platform.Windows;
using Xunit;
namespace Macrofy.Platform.Windows.Tests;
public class ClickFilterTests
{
    [Fact] public void OutsideDownRecordsAndSwallowsMatchingUp()
    {
        var filter = new ClickFilter((_, _) => false);
        Assert.Equal(ClickDecision.Pass, filter.Handle(0x200, 5, 5));
        Assert.Equal(ClickDecision.Record, filter.Handle(0x201, 5, 5)); Assert.False(filter.Finished);
        Assert.Equal(ClickDecision.Swallow, filter.Handle(0x202, 5, 5)); Assert.True(filter.Finished);
    }
    [Fact] public void OwnWindowClicksPassThrough()
    {
        var filter = new ClickFilter((x, _) => x < 100);
        Assert.Equal(ClickDecision.Pass, filter.Handle(0x201, 10, 10));
        Assert.Equal(ClickDecision.Pass, filter.Handle(0x202, 10, 10)); Assert.False(filter.Finished);
        Assert.Equal(ClickDecision.Record, filter.Handle(0x201, 500, 10));
    }
    [Fact] public void RightClicksPass() => Assert.Equal(ClickDecision.Pass, new ClickFilter((_, _) => false).Handle(0x204, 1, 1));
}
```

- [ ] **Step 2:** Build fails (types missing).
- [ ] **Step 3: Implement** `IPointClickSource.cs` (above) and `WindowsPointClickSource.cs`:

```csharp
using System.Runtime.InteropServices;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows.Interop;
namespace Macrofy.Platform.Windows;

internal enum ClickDecision { Pass, Record, Swallow }

/// <summary>Decides per low-level mouse message: record and swallow one left click outside this app, pass everything else.</summary>
internal sealed class ClickFilter(Func<int, int, bool> ownsPoint)
{
    private bool swallowUp;
    public bool Finished { get; private set; }
    public ClickDecision Handle(uint message, int x, int y)
    {
        if (Finished) return ClickDecision.Pass;
        if (message == 0x201 && !ownsPoint(x, y)) { swallowUp = true; return ClickDecision.Record; }
        if (message == 0x202 && swallowUp) { swallowUp = false; Finished = true; return ClickDecision.Swallow; }
        return ClickDecision.Pass;
    }
}

/// <summary>Temporary WH_MOUSE_LL hook on its own message-loop thread, installed only while listening.</summary>
public sealed class WindowsPointClickSource : IPointClickSource, IDisposable
{
    private delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public WindowNative.Point Point; public uint MouseData_, Flags, Time; public nuint Extra; }
    private readonly object sync = new();
    private uint threadId;
    private Thread? thread;

    public PlatformError? Start(Action clicked)
    {
        lock (sync)
        {
            if (thread is not null) return new("RecordingBusy", "A point is already being recorded.");
            var ready = new TaskCompletionSource<PlatformError?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = Process.GetCurrentProcess().Id;
            var filter = new ClickFilter((x, y) =>
            {
                var root = GetAncestor(WindowFromPoint(new WindowNative.Point { X = x, Y = y }), 2);
                return root != 0 && WindowNative.GetWindowThreadProcessId(root, out var pid) != 0 && pid == current;
            });
            thread = new Thread(() => Run(filter, clicked, ready)) { IsBackground = true, Name = "Macrofy point recorder" };
            thread.Start();
            var error = ready.Task.GetAwaiter().GetResult();
            if (error is not null) thread = null;
            return error;
        }
    }

    private void Run(ClickFilter filter, Action clicked, TaskCompletionSource<PlatformError?> ready)
    {
        HookProc proc = (code, wParam, lParam) =>
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<MouseData>(lParam);
                switch (filter.Handle((uint)wParam, data.Point.X, data.Point.Y))
                {
                    case ClickDecision.Record: ThreadPool.QueueUserWorkItem(_ => clicked()); return 1;
                    case ClickDecision.Swallow: PostQuitMessage(0); return 1;
                }
            }
            return CallNextHookEx(0, code, wParam, lParam);
        };
        threadId = WindowNative.GetCurrentThreadId();
        var hook = SetWindowsHookEx(14, proc, GetModuleHandle(null), 0);
        if (hook == 0) { ready.SetResult(new("HookFailed", $"Mouse hook failed, Win32 error {Marshal.GetLastWin32Error()}.")); return; }
        ready.SetResult(null);
        try { while (GetMessage(out var message, 0, 0, 0) > 0) { } }
        finally
        {
            UnhookWindowsHookEx(hook); GC.KeepAlive(proc);
            lock (sync) { if (thread == Thread.CurrentThread) thread = null; }
        }
    }

    public void Cancel() { lock (sync) { if (thread is not null) PostThreadMessage(threadId, 0x12, 0, 0); } }
    public void Dispose() => Cancel();

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out WindowNative.Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(WindowNative.Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
```

(`Process` from `System.Diagnostics`.)
- [ ] **Step 4:** `dotnet test tests/Macrofy.Platform.Windows.Tests --filter ClickFilterTests` → PASS.
- [ ] **Step 5:** Commit `feat: one-shot left-click point source`.

---

### Task 2: Record with click, remove record key

**Files:** Modify `WorkspaceRuntime.cs` (add `IPointClickSource? Clicks = null` to `CompatibilityUiServices`, pass `new WindowsPointClickSource()` and dispose it), `MainWindow.Recording.cs`, `MainWindow.Playback.cs`, `MainWindow.Panes.cs`, `MainWindow.cs`, `WorkspaceDocument.cs`, `WorkspaceStore.cs`, `src/Macrofy.Platform/Models/HotkeySet.cs`, `WindowsGlobalHotkeys.cs`; tests `RecordPointUiTests.cs`, `WorkspaceStateTests.cs`, `GlobalHotkeyTests.cs`.

- [ ] **Step 1: Tests.** In `RecordPointUiTests`: delete `SettingsShowsAndChangesRecordKey`; add a `FakeClicks : IPointClickSource` (`Started`, `Cancelled`, `Click()` invokes the pending callback); pass it as `Clicks` in `Open()`; replace `keys.Trigger(HotkeyCommand.Capture)` with `clicks.Click()`; assert status `Click the spot to record it. That click is not sent to the app. Click Cancel recording to stop.`; button content `Record point`; cancel asserts `clicks.Cancelled == 1`; add assertion that a successful click does not call `Cancel`. Delete `LoadRepairsMissingOrDuplicateRecordKey` and `OptionalCaptureRegistersRoutesAndUnregisters`.
- [ ] **Step 2:** Build fails (`Clicks` parameter missing).
- [ ] **Step 3: Implement.**
  - `PointRecording` drops the hotkey. `StartRecording`: `if (compatibility?.Clicks is not { } clicks) { report("Click recording is unavailable."); return false; }`, `var error = clicks.Start(() => Dispatcher.UIThread.Post(() => _ = CompleteRecordingAsync()));` error → report, false.
  - `EndRecording(bool cancelSource = true)`: clears `recording`, calls `compatibility?.Clicks?.Cancel()` when `cancelSource`. `CompleteRecordingAsync` calls `EndRecording(false)` (the source finishes after swallowing the button-up).
  - Button label `Record point`; enabled rule drops `playback.HotkeysReady`.
  - Remove `HotkeyCommand.Capture` branch and mapping in `GlobalCommand`; Stop branch keeps `EndRecording()`.
  - Remove `ShortcutSettings.Capture`, store repair, Settings `Record point` row (grid back to 3 rows), `ShortcutInput` "Capture" case, `CaptureShortcut` Capture handling (back to 3 keys), `HotkeySet.Capture`, `HotkeyCommand.Capture`, `WindowsGlobalHotkeys` optional binding.
- [ ] **Step 4:** Build + all non-native tests (App, Windows) → PASS.
- [ ] **Step 5:** Commit `feat: record points with a blocked left click`.

---

### Task 3: Loop toggle and interval in Profiles rows

**Files:** Modify `MainWindow.Panes.cs` (`ProfilesPane`, `TableRow`); Test `tests/Macrofy.App.Tests/MacroRowUiTests.cs`.

- [ ] **Step 1: Failing test:**

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Xunit;
namespace Macrofy.App.Tests;
public sealed class MacroRowUiTests
{
    [AvaloniaFact]
    public void RowLoopToggleAndIntervalSecondsEditMacro()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var macro = state.Profile.Macros[0]; macro.Repeat = 1; macro.IntervalMs = 1000;
        var window = HarmlessUi.Create(state); window.Show();
        try
        {
            var id = macro.Id.ToString("N");
            var loop = PlaybackUiTests.Find<ToggleSwitch>(window, "Loop_" + id);
            var interval = PlaybackUiTests.Find<NumericUpDown>(window, "Interval_" + id);
            Assert.False(loop.IsChecked); Assert.Equal(1m, interval.Value);
            loop.IsChecked = true; Assert.Equal(0, macro.Repeat);
            loop.IsChecked = false; Assert.Equal(1, macro.Repeat);
            interval.Value = 2.5m; Assert.Equal(2500, macro.IntervalMs);
            macro.Repeat = 100; PlaybackUiTests.Click(window, "Tab_Settings"); PlaybackUiTests.Click(window, "Tab_Profiles");
            Assert.True(PlaybackUiTests.Find<ToggleSwitch>(window, "Loop_" + id).IsChecked);
        }
        finally { window.Close(); }
    }
}
```

- [ ] **Step 2:** Run → FAIL (`Loop_` not found).
- [ ] **Step 3: Implement.** Header `TableRow("Enabled", "Macro", "Target app", "Steps", "Loop", "Interval (s)", "Status", "Controls")`; `TableRow` columns `"64,*,*,55,60,84,85,172"`, `MinWidth = 818`. Per row:

```csharp
            var loop = new ToggleSwitch { Name = "Loop_" + macro.Id.ToString("N"), IsChecked = macro.Repeat != 1, OnContent = null, OffContent = null, MinWidth = 0, Width = 48, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(loop, "Loop until stopped: " + macro.Name);
            ToolTip.SetTip(loop, "On: repeat until stopped · Off: run once");
            loop.IsCheckedChanged += (_, _) => { var repeat = loop.IsChecked == true ? 0 : 1; if ((macro.Repeat != 1) == (repeat != 1)) return; macro.Repeat = repeat; Save(); RefreshPlayback(); };
            var interval = new NumericUpDown { Name = "Interval_" + macro.Id.ToString("N"), Value = macro.IntervalMs / 1000m, Minimum = 0, Maximum = 600, Increment = 0.5m, FormatString = "0.##", Width = 80, MinHeight = 30, ShowButtonSpinner = false, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(interval, "Interval between runs in seconds: " + macro.Name);
            interval.ValueChanged += (_, _) => { macro.IntervalMs = (int)Math.Round((interval.Value ?? 0) * 1000); Save(); };
```

  Insert `loop, interval` after the steps cell; in the row refresh: `loop.IsEnabled = interval.IsEnabled = !active;`. Exclude `NumericUpDown` from the double-tap edit gesture alongside Button/ToggleSwitch.
- [ ] **Step 4:** App tests → PASS (update `NativeUiTests` if a column-count assertion changes).
- [ ] **Step 5:** Commit `feat: loop toggle and interval on macro rows`.

---

### Task 4: Surface labels

**Files:** Modify `MainWindow.Compatibility.cs` (`WindowOption`, surface loading); tests `RecordPointUiTests.CompatibilityRecordPointFillsSelectedSurfacePoint`.

- [ ] **Step 1: Test:** in the Compatibility recording test, after selecting the window, assert surface item text `Main · 696×1237 (default)` and that it is selected without setting `SelectedIndex`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: Implement:** `private sealed record WindowOption(TargetWindow Window, string? Label = null) { public override string ToString() => Label ?? $"{Window.Title} · {Window.Token.Id.ToString("N")[..8]}"; }`; surfaces:

```csharp
                var options = surfaces.OrderByDescending(w => w.Token == selected.Window.Token).Select(w => new WindowOption(w, w.Token == selected.Window.Token
                    ? $"Main · {Size(w)} (default)" : $"Child · {Size(w)} · {w.Token.Id.ToString("N")[..8]}")).ToArray();
                if (version == generation) { surface.ItemsSource = options; surface.SelectedItem = options.FirstOrDefault(o => o.Window.Token == selected.Window.Token); }
```

  with `static string Size(TargetWindow w) => w.Geometry is { } g ? $"{g.Width}×{g.Height}" : "?×?";`.
- [ ] **Step 4:** App tests → PASS (adjust `CompatibilityUiTests` indexes if preselection changes them).
- [ ] **Step 5:** Commit `feat: label input surfaces and preselect the main one`.

---

### Task 5: Docs, verify, publish, push

- [ ] README: record with left click (blocked), Loop/Interval columns, surface labels; remove F7 record key text. Verification doc: dated note.
- [ ] `powershell -NoProfile -File scripts/verify-probe.ps1` → all pass; update README count.
- [ ] Commit `docs: click recording, row loop/interval, surface labels`; publish; push.
