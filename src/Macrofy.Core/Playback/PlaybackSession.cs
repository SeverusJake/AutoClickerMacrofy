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
                    else
                    {
                        var result = await DispatchAsync(action).ConfigureAwait(false);
                        if (!result.Delivery.Queued || result.Delivery.Error is not null || result.CleanupError is not null || result.Delivery.CleanupError is not null)
                        {
                            Fail(result.Delivery, result.CleanupError); return;
                        }
                    }
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
        catch (PlaybackFailure failure) { Fail(failure.Result); }
        catch (Exception) { Fail(new(false, new("ExecutorException", "Playback executor failed unexpectedly."))); }
        finally
        {
            lock (sync)
            {
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
            bool validate;
            lock (sync)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (pauseRequested) { EnterPaused(); pauseWait = changed.Task; }
                validate = needsResumeValidation;
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
    private sealed class PlaybackFailure(DeliveryResult result) : Exception { public DeliveryResult Result { get; } = result; }
}
