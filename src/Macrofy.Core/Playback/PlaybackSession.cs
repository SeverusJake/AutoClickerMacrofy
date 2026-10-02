using Macrofy.Core.Actions;
using Macrofy.Platform.Models;

namespace Macrofy.Core.Playback;

internal sealed class PlaybackSession
{
    private readonly object sync = new();
    private readonly PlaybackRequest request;
    private readonly IPlaybackExecutor executor;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate;
    private readonly Action publish;
    private readonly CancellationTokenSource stop;
    private readonly TaskCompletionSource<DeliveryResult> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource completion = NewSignal();
    private TaskCompletionSource changed = NewSignal();
    private readonly long began;
    private PlaybackState state = PlaybackState.Starting;
    private PlaybackBinding? binding;
    // Only preparation needs history: binding publication and loss observation share sync.
    private HashSet<TargetToken>? lostDuringPreparation;
    private bool targetLost;
    private bool pauseRequested, inGesture, needsResumeValidation;
    private long? pausedAt, ended;
    private TimeSpan pausedTime, remaining;
    private long waitBegan;
    private bool waiting;
    private int currentStep;
    private long completedSteps, completedLoops;
    private PlatformError? deliveryError, cleanupError;
    // Inputs this session pressed and still holds; released on pause and at the end.
    private readonly List<HeldEntry> held = [];
    private sealed record HeldEntry(HeldInput Input, PointerPoint Point, CoordinateMode Mode);

    public PlaybackSession(PlaybackRequest request, IPlaybackExecutor executor, TimeProvider clock,
        SemaphoreSlim gate, Action publish, CancellationToken cancellationToken)
    {
        this.request = request; this.executor = executor; this.clock = clock; this.gate = gate; this.publish = publish;
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        began = clock.GetTimestamp();
    }

    public Task<DeliveryResult> Started => started.Task;
    public Task Completion => completion.Task;
    public bool PauseRequested { get { lock (sync) return pauseRequested; } }
    public PlaybackSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                var now = ended ?? clock.GetTimestamp();
                var elapsed = clock.GetElapsedTime(began, now) - pausedTime;
                if (pausedAt is { } paused) elapsed -= clock.GetElapsedTime(paused, now);
                return new(request.MacroId, request.MacroName, request.ProfileName, state, request.Actions,
                    currentStep, completedSteps, completedLoops, elapsed, RemainingAt(now), deliveryError, cleanupError);
            }
        }
    }

    // Always leave the caller's thread, even if preparation and every gesture complete synchronously.
    public void Launch() => _ = Task.Run(RunAsync);
    public void TogglePause() { SetPaused(!PauseRequested); }
    public void SetPaused(bool paused)
    {
        lock (sync)
        {
            if (ended is not null || stop.IsCancellationRequested || paused == pauseRequested) return;
            pauseRequested = paused;
            if (paused)
            {
                if (inGesture) state = PlaybackState.Pausing;
                else EnterPaused();
            }
            else
            {
                if (pausedAt is { } timestamp) pausedTime += clock.GetElapsedTime(timestamp, clock.GetTimestamp());
                pausedAt = null;
                needsResumeValidation = binding is not null;
                state = binding is null ? PlaybackState.Starting : PlaybackState.Running;
            }
            Signal();
        }
        publish();
    }

    public void Stop()
    {
        try { stop.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException) { /* A cancellation subscriber cannot prevent other sessions stopping. */ }
    }

    public void TargetLost(TargetToken token)
    {
        bool cancel;
        lock (sync)
        {
            if (ended is not null || request.Target.Rule is null) return;
            if (binding is null) (lostDuringPreparation ??= []).Add(token);
            cancel = (binding?.Token ?? request.Target.SelectedSurface) == token;
            if (cancel) MarkTargetLost();
        }
        if (cancel) Stop();
    }

    private void MarkTargetLost()
    {
        targetLost = true;
        deliveryError = new("TargetLost", "The original playback target disappeared. Select a live target before starting again.");
    }

    private async Task RunAsync()
    {
        try
        {
            stop.Token.ThrowIfCancellationRequested();
            var preparation = await executor.PrepareAsync(request, stop.Token).ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
            if (preparation.Error is not null || preparation.Binding is null)
            {
                Fail(new(false, preparation.Error ?? new("PreparationFailed", "Target preparation returned no binding.")));
                return;
            }
            lock (sync)
            {
                binding = preparation.Binding;
                if (binding.Token is { } token && lostDuringPreparation?.Contains(token) == true) MarkTargetLost();
                lostDuringPreparation = null;
                if (targetLost) throw new PlaybackFailure(new(false, deliveryError));
                stop.Token.ThrowIfCancellationRequested();
                started.TrySetResult(new(true));
            }
            await WaitAsync(request.StartDelayMs).ConfigureAwait(false);
            while (request.Repeat == 0 || completedLoops < request.Repeat)
            {
                for (var index = 0; index < request.Actions.Count; index++)
                {
                    await ReadyAsync().ConfigureAwait(false);
                    lock (sync) currentStep = index + 1;
                    publish();
                    var action = request.Actions[index];
                    if (action is CompiledAction.Wait wait) await WaitAsync(wait.DurationMs).ConfigureAwait(false);
                    else await ExecuteStepAsync(action).ConfigureAwait(false);
                    lock (sync) completedSteps++;
                    publish();
                    await WaitAsync(action.DelayMs).ConfigureAwait(false);
                }
                lock (sync) completedLoops++;
                publish();
                if (request.Repeat != 0 && completedLoops >= request.Repeat) break;
                await WaitAsync(request.IntervalMs).ConfigureAwait(false);
                // Bound synchronous zero-delay work and give concurrent sessions a scheduling turn.
                await Task.Yield();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (PlaybackFailure failure) { Fail(failure.Result, failure.Cleanup); }
        catch (Exception) { Fail(new(false, new("ExecutorException", "Playback executor failed unexpectedly."))); }
        finally
        {
            var release = await ReleaseAllAsync().ConfigureAwait(false);
            lock (sync)
            {
                if (!targetLost && Failed(release)) cleanupError ??= release.CleanupError ?? release.Delivery.CleanupError ?? release.Delivery.Error ?? new("CleanupFailed", "Held input could not be released.");
                ended = clock.GetTimestamp();
                remaining = TimeSpan.Zero; waiting = false;
                lostDuringPreparation = null;
                var stopped = targetLost || stop.IsCancellationRequested;
                state = cleanupError is not null || (deliveryError is not null && !stopped)
                    ? PlaybackState.Error : stopped ? PlaybackState.Stopped : PlaybackState.Completed;
                Signal();
            }
            started.TrySetResult(new(false, deliveryError ?? new("Cancelled", "Playback stopped during preparation."), cleanupError));
            stop.Dispose();
            completion.TrySetResult();
            publish();
        }
    }

    private void Fail(DeliveryResult result, PlatformError? cleanup = null)
    {
        lock (sync)
        {
            if (!targetLost) deliveryError = result.Error ?? (!result.Queued ? new("DeliveryFailed", "Input was not queued.") : null);
            cleanupError = cleanup ?? result.CleanupError;
        }
        started.TrySetResult(new(false, deliveryError, cleanupError));
    }

    private async Task ReadyAsync()
    {
        while (true)
        {
            Task? pauseWait = null;
            bool validate, releaseFirst = false;
            lock (sync)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (pauseRequested)
                {
                    releaseFirst = held.Count > 0;
                    if (!releaseFirst) { EnterPaused(); pauseWait = changed.Task; }
                }
                validate = needsResumeValidation;
            }
            if (releaseFirst)
            {
                // Pausing gives control back: release held input first; resuming does not press it again.
                var release = await ReleaseAllAsync().ConfigureAwait(false);
                if (Failed(release)) throw new PlaybackFailure(new(true), release.CleanupError ?? release.Delivery.Error ?? new("CleanupFailed", "Held input could not be released."));
                continue;
            }
            if (pauseWait is not null)
            {
                publish();
                await pauseWait.WaitAsync(stop.Token).ConfigureAwait(false);
                continue;
            }
            if (validate)
            {
                var result = await DispatchAsync(null).ConfigureAwait(false);
                if (!result.Delivery.Queued || result.Delivery.Error is not null || result.Delivery.CleanupError is not null)
                    throw new PlaybackFailure(result.Delivery);
                continue;
            }
            return;
        }
    }

    private async Task<GestureResult> DispatchAsync(CompiledAction? action)
    {
        while (true)
        {
            // Resume validation itself takes the shared gate, without recursing through ReadyAsync.
            if (action is not null) await ReadyAsync().ConfigureAwait(false);
            await gate.WaitAsync(stop.Token).ConfigureAwait(false);
            bool dispatch;
            lock (sync)
            {
                dispatch = !pauseRequested && !stop.IsCancellationRequested;
                if (dispatch) { inGesture = true; state = PlaybackState.Running; needsResumeValidation = false; }
            }
            if (!dispatch)
            {
                gate.Release();
                await WaitUntilResumedAsync().ConfigureAwait(false);
                continue;
            }
            publish();
            try
            {
                var validation = await executor.ValidateAsync(binding!, stop.Token).ConfigureAwait(false);
                if (!validation.Queued || validation.Error is not null || validation.CleanupError is not null || action is null)
                    return new(validation);
                // Validation can await native target checks. Honor a pause arriving there before sending input.
                lock (sync) { if (pauseRequested) continue; }
                stop.Token.ThrowIfCancellationRequested();
                return await executor.ExecuteAsync(binding!, action, stop.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (sync) { inGesture = false; if (pauseRequested) EnterPaused(); }
                gate.Release();
                publish();
            }
        }
    }

    private async Task ExecuteStepAsync(CompiledAction action)
    {
        switch (action)
        {
            case CompiledAction.Click { HoldMs: > 0 } click:
                await PressButtonAsync(click.Point, click.Mode, click.Button).ConfigureAwait(false);
                await WaitAsync(click.HoldMs).ConfigureAwait(false);
                await ReleaseButtonAsync(click.Button, click.Point, click.Mode).ConfigureAwait(false);
                break;
            case CompiledAction.Key { HoldMs: > 0 } key:
                await PressKeysAsync(key.Keys).ConfigureAwait(false);
                await WaitAsync(key.HoldMs).ConfigureAwait(false);
                await ReleaseKeysAsync(key.Keys).ConfigureAwait(false);
                break;
            case CompiledAction.MouseDown down: await PressButtonAsync(down.Point, down.Mode, down.Button).ConfigureAwait(false); break;
            case CompiledAction.MouseUp up: await ReleaseButtonAsync(up.Button, up.Point, up.Mode).ConfigureAwait(false); break;
            case CompiledAction.KeyDown down: await PressKeysAsync(down.Keys).ConfigureAwait(false); break;
            case CompiledAction.KeyUp up: await ReleaseKeysAsync(up.Keys).ConfigureAwait(false); break;
            default: Require(await DispatchAsync(action).ConfigureAwait(false)); break;
        }
    }

    private async Task PressButtonAsync(PointerPoint point, CoordinateMode mode, MouseButton button)
    {
        lock (sync) { if (held.Any(h => h.Input.Button == button)) return; }
        Require(await DispatchAsync(new CompiledAction.MouseDown(point, mode, button, 0)).ConfigureAwait(false));
        lock (sync) held.Add(new(new(Button: button), point, mode));
    }

    private async Task ReleaseButtonAsync(MouseButton button, PointerPoint point, CoordinateMode mode)
    {
        lock (sync) { if (held.RemoveAll(h => h.Input.Button == button) == 0) return; }
        Require(await DispatchAsync(new CompiledAction.MouseUp(point, mode, button, 0)).ConfigureAwait(false));
    }

    private async Task PressKeysAsync(IReadOnlyList<KeyIdentity> keys)
    {
        KeyIdentity[] press;
        lock (sync) press = keys.Where(k => !held.Any(h => SameKey(h.Input.Key, k))).ToArray();
        if (press.Length == 0) return;
        Require(await DispatchAsync(new CompiledAction.KeyDown(press, 0)).ConfigureAwait(false));
        lock (sync) foreach (var key in press) held.Add(new(new(Key: key), default, CoordinateMode.FixedPixels));
    }

    private async Task ReleaseKeysAsync(IReadOnlyList<KeyIdentity> keys)
    {
        KeyIdentity[] release;
        lock (sync)
        {
            release = keys.Where(k => held.Any(h => SameKey(h.Input.Key, k))).ToArray();
            held.RemoveAll(h => release.Any(k => SameKey(h.Input.Key, k)));
        }
        if (release.Length == 0) return;
        Require(await DispatchAsync(new CompiledAction.KeyUp(release, 0)).ConfigureAwait(false));
    }

    /// <summary>Releases everything this session holds under the shared gate, even after Stop.</summary>
    private async Task<GestureResult> ReleaseAllAsync()
    {
        HeldInput[] inputs; PlaybackBinding? current;
        lock (sync) { inputs = held.Select(h => h.Input).ToArray(); held.Clear(); current = binding; }
        if (inputs.Length == 0 || current is null) return new(new(true));
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
            return new(new(true), new("CleanupTimeout", "Held input could not be released: input stayed busy."));
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            return await executor.ReleaseHeldAsync(current, inputs, budget.Token).ConfigureAwait(false);
        }
        catch (Exception) { return new(new(true), new("CleanupFailed", "Held input could not be released.")); }
        finally { gate.Release(); }
    }

    private static bool SameKey(KeyIdentity? held, KeyIdentity key) => held is not null && string.Equals(held.LogicalKey, key.LogicalKey, StringComparison.OrdinalIgnoreCase);
    private static bool Failed(GestureResult result) => !result.Delivery.Queued || result.Delivery.Error is not null || result.CleanupError is not null || result.Delivery.CleanupError is not null;
    private static void Require(GestureResult result)
    {
        if (Failed(result)) throw new PlaybackFailure(result.Delivery, result.CleanupError);
    }

    private async Task WaitUntilResumedAsync()
    {
        while (true)
        {
            Task signal;
            lock (sync)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (!pauseRequested) return;
                EnterPaused(); signal = changed.Task;
            }
            publish();
            await signal.WaitAsync(stop.Token).ConfigureAwait(false);
        }
    }

    private async Task WaitAsync(int milliseconds)
    {
        lock (sync) remaining = TimeSpan.FromMilliseconds(milliseconds);
        while (true)
        {
            await ReadyAsync().ConfigureAwait(false);
            using var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            Task timer, signal;
            lock (sync)
            {
                if (pauseRequested) continue;
                if (remaining <= TimeSpan.Zero) { state = PlaybackState.Running; return; }
                state = PlaybackState.Waiting; waitBegan = clock.GetTimestamp(); waiting = true;
                signal = changed.Task;
                timer = Task.Delay(remaining, clock, timerCancellation.Token);
            }
            publish();
            try { await Task.WhenAny(timer, signal).ConfigureAwait(false); }
            finally
            {
                timerCancellation.Cancel();
                lock (sync) FreezeWait();
            }
            stop.Token.ThrowIfCancellationRequested();
        }
    }

    private TimeSpan RemainingAt(long timestamp) => waiting
        ? TimeSpan.FromTicks(Math.Max(0, (remaining - clock.GetElapsedTime(waitBegan, timestamp)).Ticks)) : remaining;
    private void FreezeWait() { remaining = RemainingAt(clock.GetTimestamp()); waiting = false; }
    private void EnterPaused()
    {
        FreezeWait();
        pausedAt ??= clock.GetTimestamp();
        state = PlaybackState.Paused;
    }
    private void Signal() { var previous = changed; changed = NewSignal(); previous.TrySetResult(); }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class PlaybackFailure(DeliveryResult result, PlatformError? cleanup = null) : Exception
    {
        public DeliveryResult Result { get; } = result;
        public PlatformError? Cleanup { get; } = cleanup;
    }
}
