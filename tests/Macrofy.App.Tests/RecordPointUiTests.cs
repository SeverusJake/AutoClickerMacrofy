using Avalonia.Controls;
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
    public void SettingsShowsAndChangesRecordKey()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var keys = new HarmlessHotkeys();
        var window = new MainWindow(state, new UiPlayback(state.Document), keys); window.Show();
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

    [AvaloniaFact]
    public async Task HotkeyRecordsWindowAndScreenPointsAndReportsMissingWindow()
    {
        var (state, window, catalog, keys) = Open();
        try
        {
            var macro = state.Profile.Macros.Single(m => m.Name == "Collect rewards");
            PlaybackUiTests.Click(window, "Edit_" + macro.Id.ToString("N"));
            Assert.Equal("Background clicks don't work in every game. Watch the first run.", PlaybackUiTests.Find<TextBlock>(window, "MacroModeNote").Text);
            PlaybackUiTests.Click(window, "RecordPoint");
            Assert.Equal("F7", keys.Last!.Capture!.Key);
            Assert.Equal("Hover over the spot and press F7. Press the button again to cancel.", PlaybackUiTests.Find<TextBlock>(window, "RecordStatus").Text);
            keys.Trigger(HotkeyCommand.Capture);
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBox>(window, "ActionValue").Text == "123, 45");
            Assert.Null(keys.Last!.Capture); Assert.Equal(catalog.Window.Token, catalog.ReadToken);
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

    [AvaloniaFact]
    public async Task CompatibilityRecordPointFillsSelectedSurfacePoint()
    {
        var (state, window, catalog, keys) = Open();
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
            PlaybackUiTests.Find<ComboBox>(window, "CompatibilitySurface").SelectedIndex = 0;
            PlaybackUiTests.Click(window, "CompatibilityRecordPoint");
            keys.Trigger(HotkeyCommand.Capture);
            await PlaybackControllerTests.Until(() => PlaybackUiTests.Find<TextBox>(window, "CompatibilityValue").Text == "123, 45");
            Assert.Equal(catalog.Window.Token, catalog.ReadToken);
        }
        finally { window.Close(); }
    }

    private static (WorkspaceState State, MainWindow Window, CaptureCatalog Catalog, HarmlessHotkeys Keys) Open()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var catalog = new CaptureCatalog();
        var keys = new HarmlessHotkeys();
        var service = new CompatibilityService(state.Document, catalog, () => null,
            (_, _, _) => throw new InvalidOperationException("Recording must not send input"));
        var ui = new CompatibilityUiServices(service, catalog, (_, _) => Task.FromResult<IReadOnlyList<TargetWindow>>([catalog.Window]),
            token => { catalog.ReadToken = token; return new(new(123, 45)); }, () => new(new(-10, 20)));
        var window = new MainWindow(state, new UiPlayback(state.Document), keys, compatibility: ui);
        window.Show();
        return (state, window, catalog, keys);
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
