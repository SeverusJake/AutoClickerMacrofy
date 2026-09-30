namespace Macrofy.App.Services;

/// <summary>Keep each playback lease until its session has completed native cleanup, including paused/starting sessions.</summary>
public sealed class InputActivityGuard
{
    private readonly object sync = new();
    private readonly HashSet<Guid> playback = [];
    private bool testing;

    public bool TryEnterPlayback(Guid macroId, out IDisposable? lease)
    {
        lock (sync)
        {
            lease = null;
            if (testing || !playback.Add(macroId)) return false;
            lease = new Lease(() => { lock (sync) playback.Remove(macroId); });
            return true;
        }
    }

    public bool TryEnterTest(out IDisposable? lease)
    {
        lock (sync)
        {
            lease = null;
            if (testing || playback.Count != 0) return false;
            testing = true;
            lease = new Lease(() => { lock (sync) testing = false; });
            return true;
        }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
