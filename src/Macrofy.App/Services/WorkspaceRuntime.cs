using Avalonia.Threading;
using Macrofy.App.Models;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
#if WINDOWS
using Macrofy.Platform.Windows;
#endif
namespace Macrofy.App.Services;

/// <summary>Read-only target discovery and explicit observed-test service used by the compatibility pane.</summary>
public sealed record CompatibilityUiServices(CompatibilityService Service, IWindowCatalog Catalog,
    Func<TargetToken, CancellationToken, Task<IReadOnlyList<TargetWindow>>> ListSurfaces,
    Func<TargetToken, ScreenPointResult> ReadPointer);

/// <summary>One native sender and activity guard shared by all playback and explicit tests.</summary>
public sealed class WorkspaceRuntime : IAsyncDisposable
{
    public IWorkspacePlaybackController Playback { get; }
    public IGlobalHotkeys Hotkeys { get; }
    public CompatibilityUiServices? Compatibility { get; }
    private readonly IDisposable[] resources;
    private WorkspaceRuntime(IWorkspacePlaybackController playback, IGlobalHotkeys hotkeys, CompatibilityUiServices? compatibility, params IDisposable[] resources)
    { Playback = playback; Hotkeys = hotkeys; Compatibility = compatibility; this.resources = resources; }
    public static WorkspaceRuntime Create(WorkspaceDocument document, WorkspaceStore store)
    {
#if WINDOWS
        var hotkeys = new WindowsGlobalHotkeys();
        var catalog = new WindowsWindowCatalog();
        var window = new WindowsInputPlayer(catalog);
        var screen = new WindowsScreenInputPlayer();
        var activity = new InputActivityGuard();
        var sender = new WindowsGestureSender(catalog, window, screen, () => hotkeys.IsOperational);
        var compatibility = new CompatibilityService(document, catalog,
            () => activity.TryEnterTest(out var lease) ? lease : null, sender.SendGestureAsync,
            (snapshot, publish, ct) => PersistAsync(store, snapshot, publish, ct), ReadAsync);
        var playback = new WorkspacePlaybackController(document,
            new PlaybackCoordinator(new WindowsPlaybackExecutor(catalog, sender, compatibility), TimeProvider.System, catalog), activity, hotkeys, hotkeys);
        return new(playback, hotkeys, new(compatibility, catalog,
            async (token, ct) => (await catalog.ListInputSurfacesAsync(token, ct)).Select(s => s.Window).ToArray(),
            token => { var result = catalog.ReadPointerPosition(token); return new(result.Point, result.Error); }), window, catalog, hotkeys);
#else
        var unavailable = new UnavailableHotkeys();
        return new(new WorkspacePlaybackController(document, new(new UnavailableExecutor(), TimeProvider.System), new(), unavailable, unavailable), unavailable, null);
#endif
    }
    public static async Task<bool> ReadAsync(Func<bool> read, CancellationToken ct) =>
        await Dispatcher.UIThread.InvokeAsync(() => { ct.ThrowIfCancellationRequested(); return read(); }, DispatcherPriority.Normal, ct);

    public static async Task PersistAsync(WorkspaceStore store, Func<WorkspaceDocument> snapshot, Action publish, CancellationToken ct)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            var candidate = snapshot();
            store.Save(candidate);
            publish();
        });
    }
    public async ValueTask DisposeAsync()
    {
        await Playback.DisposeAsync();
        foreach (var resource in resources) resource.Dispose();
    }
#if !WINDOWS
    private sealed class UnavailableHotkeys : IGlobalHotkeys, IHotkeyHealth
    {
        public bool IsOperational => false;
        public PlatformError? OperationalError => new("UnsupportedPlatform", "Native playback requires Windows.");
        public event Action? HealthChanged { add { } remove { } }
        public event Action<HotkeyCommand>? Triggered { add { } remove { } }
        public HotkeyRegistrationResult Configure(HotkeySet set) => new(false, OperationalError);
    }
    private sealed class UnavailableExecutor : IPlaybackExecutor
    {
        public ValueTask<PlaybackPreparation> PrepareAsync(PlaybackRequest request, CancellationToken ct) => ValueTask.FromResult(new PlaybackPreparation(null, new("UnsupportedPlatform", "Native playback requires Windows.")));
        public ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, CancellationToken ct) => ValueTask.FromResult(new DeliveryResult(false));
        public ValueTask<GestureResult> ExecuteAsync(PlaybackBinding binding, Macrofy.Core.Actions.CompiledAction action, CancellationToken ct) => ValueTask.FromResult(new GestureResult(new(false)));
    }
#endif
}
