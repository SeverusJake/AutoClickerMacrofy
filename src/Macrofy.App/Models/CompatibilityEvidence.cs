using Macrofy.Core.Playback;
using Macrofy.Platform.Models;
namespace Macrofy.App.Models;
public sealed record CompatibilityEvidence(Guid SavedAppId, AppIdentity App, string Title, string SurfaceFingerprint,
    TargetState State, InputCapability Capability, ClientGeometry Geometry, DateTimeOffset TestedAt, bool ObservedSuccess);
public sealed class CompatibilityAttempt : IDisposable
{
    internal CompatibilityAttempt(Guid appId, TargetContext context, TargetState state, InputCapability capability, GestureResult result, IDisposable? lease, object owner)
    { SavedAppId = appId; Context = context; State = state; Capability = capability; Result = result; this.lease = lease; Owner = owner; }
    private IDisposable? lease;
    private readonly object sync = new();
    private bool confirming;
    private bool discarded;
    private bool persistenceStarted;
    internal object Owner { get; }
    internal TargetContext Context { get; }
    internal bool BeginConfirmation()
    {
        lock (sync) { if (lease is null || discarded || confirming) return false; confirming = true; return true; }
    }
    internal void EndConfirmation(bool completed)
    {
        lock (sync) { confirming = false; if (!completed) persistenceStarted = false; if (completed || discarded) Release(); }
    }
    internal bool TryBeginPersistence()
    {
        lock (sync)
        {
            if (!confirming || discarded || persistenceStarted) return false;
            persistenceStarted = true;
            return true;
        }
    }
    public Guid SavedAppId { get; }
    public TargetToken Token => Context.Window.Token;
    public string Fingerprint => Context.SurfaceFingerprint;
    public ClientGeometry? Geometry => Context.Window.Geometry;
    public TargetState State { get; }
    public InputCapability Capability { get; }
    public GestureResult Result { get; }
    public void Dispose() { lock (sync) { discarded = true; if (!confirming) Release(); } }
    private void Release() => Interlocked.Exchange(ref lease, null)?.Dispose();
}
