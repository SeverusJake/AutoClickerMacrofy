using System.Collections.ObjectModel;
using Macrofy.Core.Actions;
using Macrofy.Platform.Models;

namespace Macrofy.Core.Playback;

public sealed class PlaybackCoordinator : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, PlaybackSession> sessions = [];
    private readonly SemaphoreSlim dispatchGate = new(1, 1);
    private readonly IPlaybackExecutor executor;
    private readonly TimeProvider timeProvider;
    private bool disposed;

    public PlaybackCoordinator(IPlaybackExecutor executor, TimeProvider timeProvider)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public IReadOnlyDictionary<Guid, PlaybackSnapshot> Snapshots
    {
        get { lock (sync) return new ReadOnlyDictionary<Guid, PlaybackSnapshot>(sessions.ToDictionary(p => p.Key, p => p.Value.Snapshot)); }
    }

    /// <summary>Raised on worker threads, outside state locks. Subscribers must marshal UI work.</summary>
    public event EventHandler? Changed;

    /// <summary>Returns after preparation and launch. Cancellation remains linked to the session lifetime.</summary>
    public Task<DeliveryResult> StartAsync(PlaybackRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PlaybackSession session;
        lock (sync)
        {
            if (disposed) return Task.FromResult(Failure("Disposed", "Playback coordinator has shut down."));
            if (sessions.TryGetValue(request.MacroId, out var previous) && !previous.Completion.IsCompleted)
                return Task.FromResult(Failure("Busy", "This macro is already active or finishing cleanup."));
            if (request.Actions is null || request.Actions.Count == 0 || request.Actions.Any(a => a is null) ||
                request.Repeat < 0 || request.IntervalMs is < 0 or > 600000 || request.StartDelayMs is < 0 or > 600000 ||
                request.Actions.Any(a => a.DelayMs is < 0 or > 600000 || a is CompiledAction.Wait { DurationMs: < 0 or > 600000 }) ||
                request.Target is null || (request.Target.Rule is null &&
                    (request.Target.SavedAppId is not null || request.Target.State is not null || request.Target.SelectedSurface is not null)))
                return Task.FromResult(Failure("InvalidRequest", "Playback actions, timing, or target are invalid."));

            // Compiled actions and all their nested values are immutable; freeze the caller-owned list.
            var frozen = request with { Actions = Array.AsReadOnly(request.Actions.ToArray()) };
            session = new PlaybackSession(frozen, executor, timeProvider, dispatchGate, Publish, cancellationToken);
            sessions[request.MacroId] = session;
        }
        session.Launch();
        Publish();
        return session.Started;
    }

    public void TogglePause(Guid macroId) => Find(macroId)?.TogglePause();
    public void TogglePauseAll()
    {
        var active = ActiveSessions();
        var pause = active.Any(s => !s.PauseRequested);
        foreach (var session in active) session.SetPaused(pause);
    }
    public void Stop(Guid macroId) => Find(macroId)?.Stop();
    public void StopAll() { foreach (var session in ActiveSessions()) session.Stop(); }

    /// <summary>Waits for sessions active at the time of this call, including preparation and cleanup.</summary>
    public Task WaitForIdleAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(ActiveSessions().Select(s => s.Completion)).WaitAsync(cancellationToken);

    /// <summary>Waits for the current lifetime of one macro, including cleanup; a later restart is not followed.</summary>
    public Task WaitForCompletionAsync(Guid macroId, CancellationToken cancellationToken = default)
    {
        Task completion;
        lock (sync) completion = sessions.TryGetValue(macroId, out var session) ? session.Completion : Task.CompletedTask;
        return completion.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        PlaybackSession[] active;
        lock (sync) { disposed = true; active = sessions.Values.ToArray(); }
        foreach (var session in active) session.Stop();
        await Task.WhenAll(active.Select(s => s.Completion)).ConfigureAwait(false);
        // Keeping the tiny managed semaphore permits concurrent/reentrant disposal safely.
    }

    private PlaybackSession? Find(Guid id) { lock (sync) return sessions.GetValueOrDefault(id); }
    private PlaybackSession[] ActiveSessions() { lock (sync) return sessions.Values.Where(s => !s.Completion.IsCompleted).ToArray(); }
    private static DeliveryResult Failure(string code, string message) => new(false, new(code, message));
    private void Publish()
    {
        var observers = Changed;
        if (observers is null) return;
        foreach (EventHandler observer in observers.GetInvocationList())
        {
            try { observer(this, EventArgs.Empty); }
            catch { /* Observers must not strand a session or prevent native cleanup. */ }
        }
    }
}
