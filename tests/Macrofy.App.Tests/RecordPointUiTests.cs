using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Macrofy.App.Models;
using Macrofy.App.Services;
using Macrofy.App.Views;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class RecordPointUiTests
{
    [AvaloniaFact]
    public async Task ClickRecordsWindowAndScreenPointsAndReportsMissingWindow()
    {
        var (state, window, catalog, clicks) = Open();
        try
        {
            var macro = state.Profile.Macros.Single(m => m.Name == "Collect rewards");
            PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "StepRecord_0");
            Assert.Equal(1, clicks.Started);
            Assert.Equal("Click the spot to record it. That click is not sent to the app. Click Cancel recording to stop.", PlaybackUiTests.Find<TextBlock>(window, "RecordStatus").Text);
            clicks.Click();
            await PlaybackControllerTests.Until(() => macro.Steps[0].Value == "123, 45");
            Assert.Equal(0, clicks.Cancelled); Assert.Equal(catalog.Window.Token, catalog.ReadToken);
            Assert.Equal("123", PlaybackUiTests.Find<TextBox>(window, "StepX_0").Text); Assert.Equal("Step 1 recorded at 123, 45.", PlaybackUiTests.Find<TextBlock>(window, "RecordStatus").Text);
            var cookie = macro.AppId; macro.AppId = null;
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "StepRecord_0"); clicks.Click();
            await PlaybackControllerTests.Until(() => macro.Steps[0].Value == "-10, 20");
            macro.AppId = cookie; catalog.Resolution = new ResolutionResult.Missing();
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "StepRecord_0"); clicks.Click();
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBlock>(window, "RecordStatus").Text == "No open window matches CookieRun. Open it first.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CancelStopsListeningAndPercentageDisablesRecording()
    {
        var (state, window, _, clicks) = Open();
        try
        {
            var macro = state.Profile.Macros.Single(m => m.Name == "Collect rewards");
            PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            PlaybackUiTests.Click(window, "StepRecord_0");
            Assert.Equal("Cancel recording", PlaybackUiTests.Find<Button>(window, "StepRecord_0").Content);
            PlaybackUiTests.Click(window, "StepRecord_0");
            Assert.Equal(1, clicks.Cancelled); Assert.Equal("Record point", PlaybackUiTests.Find<Button>(window, "StepRecord_0").Content);
            clicks.Click(); Assert.Equal("600, 420", macro.Steps[0].Value);
            macro.Coordinates = "Percentage";
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            Assert.False(PlaybackUiTests.Find<Button>(window, "StepRecord_0").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CompatibilityRecordPointFillsSelectedSurfacePoint()
    {
        var (state, window, catalog, clicks) = Open();
        try
        {
            state.Document.ShowAdvancedTools = true;
            PlaybackUiTests.Click(window, "Tab_Profiles"); PlaybackUiTests.Click(window, "Tab_Settings");
            PlaybackUiTests.Find<CheckBox>(window, "ShowAdvancedTools").IsChecked = true;
            PlaybackUiTests.Click(window, "Tab_Compatibility");
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilityApp").SelectedItem = state.Profile.Apps.Single(a => a.Name == "CookieRun");
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<ComboBox>(window, "CompatibilityWindow").ItemCount == 1);
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilityWindow").SelectedIndex = 0;
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<ComboBox>(window, "CompatibilitySurface").ItemCount == 1);
            Assert.Equal("Main · 696×1237 (default)", PlaybackUiTests.Find<ComboBox>(window, "CompatibilitySurface").SelectedItem?.ToString());
            PlaybackUiTests.Click(window, "CompatibilityRecordPoint");
            clicks.Click();
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBox>(window, "CompatibilityValue").Text == "123, 45");
            Assert.Equal(catalog.Window.Token, catalog.ReadToken);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RecordButtonSitsRightBeforeWaitAfter()
    {
        var (state, window, _, _) = Open();
        try
        {
            var macro = state.Profile.Macros.Single(m => m.Name == "Collect rewards");
            PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            var record = PlaybackUiTests.Find<Button>(window, "StepRecord_0");
            var delay = PlaybackUiTests.Find<TextBox>(window, "StepDelay_0");
            var recordRight = record.TranslatePoint(new Avalonia.Point(record.Bounds.Width, 0), window)!.Value.X;
            var delayLeft = delay.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X;
            Assert.InRange(delayLeft - recordRight, 0, 24);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AllRecordButtonsHaveTheSameWidth()
    {
        var (state, window, _, _) = Open();
        try
        {
            var macro = state.Profile.Macros.Single(m => m.Name == "Collect rewards");
            macro.Steps = [new("Click", "1, 2", 0), new("Key", "Space", 0), new("Combo key", "Ctrl + C", 0)];
            PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            var widths = new[] { "StepRecord_0", "StepRecordKey_1", "StepRecordCombo_2" }.Select(name => PlaybackUiTests.Find<Button>(window, name).Bounds.Width).ToArray();
            Assert.All(widths, width => Assert.Equal(widths[0], width));
            PlaybackUiTests.Click(window, "StepRecord_0");
            Assert.Equal(widths[0], PlaybackUiTests.Find<Button>(window, "StepRecord_0").Bounds.Width);
        }
        finally { window.Close(); }
    }

    private static (WorkspaceState State, MainWindow Window, CaptureCatalog Catalog, FakeClicks Clicks) Open()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var catalog = new CaptureCatalog();
        var keys = new HarmlessHotkeys(); var clicks = new FakeClicks();
        var service = new CompatibilityService(state.Document, catalog, () => null,
            (_, _, _) => throw new InvalidOperationException("Recording must not send input"));
        var ui = new CompatibilityUiServices(service, catalog, (_, _) => Task.FromResult<IReadOnlyList<TargetWindow>>([catalog.Window]),
            token => { catalog.ReadToken = token; return new(new(123, 45)); }, () => new(new(-10, 20)), clicks);
        var window = new MainWindow(state, new UiPlayback(state.Document), keys, compatibility: ui);
        window.Show();
        return (state, window, catalog, clicks);
    }

    private sealed class FakeClicks : IPointClickSource
    {
        private Action? pending;
        public int Started, Cancelled;
        public PlatformError? Start(Action clicked) { Started++; pending = clicked; return null; }
        public void Cancel() { Cancelled++; pending = null; }
        public void Click() { var clicked = pending; pending = null; clicked?.Invoke(); }
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
