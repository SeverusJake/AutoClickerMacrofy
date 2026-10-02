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
