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

    [AvaloniaFact]
    public async Task MainWindowAndDialogsUseAppIcon()
    {
        var state = new WorkspaceState(WorkspaceDocument.CreateDefault());
        var (window, dialog) = await OpenAddApp(state, new ListedCatalog([]));
        try
        {
            Assert.NotNull(window.Icon);
            Assert.Same(window.Icon, dialog.Icon);
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
