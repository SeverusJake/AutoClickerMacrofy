using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

internal sealed record NativeWindow(nint TopHandle, nint SurfaceHandle, int ProcessId, long ProcessStartTicks,
    string ExecutablePath, string Title, string SurfaceClass, ClientGeometry? Geometry,
    bool IsMinimized, bool IsForeground, string FileIdentity);

internal interface IWindowNative : IDisposable
{
    int CurrentProcessId { get; }
    IReadOnlyList<NativeWindow> EnumerateWindows();
    NativeWindow? ReadWindow(nint top, nint surface);
    event Action<nint>? Destroyed;
}

internal sealed class WindowIdentityRegistry : IDisposable
{
    private readonly IWindowNative native;
    private readonly Dictionary<TargetToken, NativeWindow> targets = [];
    private readonly HashSet<TargetToken> invalid = [];
    private readonly object gate = new();
    public event Action<TargetToken>? TargetLost;

    public WindowIdentityRegistry(IWindowNative native)
    {
        this.native = native;
        native.Destroyed += OnDestroyed;
    }

    public TargetToken Register(NativeWindow window)
    {
        lock (gate)
        {
            foreach (var pair in targets)
                if (!invalid.Contains(pair.Key) && SameLifetime(pair.Value, window)) return pair.Key;
            var token = new TargetToken(Guid.NewGuid());
            targets.Add(token, window);
            return token;
        }
    }

    public bool TryResolve(TargetToken token, out NativeWindow window)
    {
        window = null!;
        bool lost = false;
        lock (gate)
        {
            if (invalid.Contains(token) || !targets.TryGetValue(token, out var original)) return false;
            var current = native.ReadWindow(original.TopHandle, original.SurfaceHandle);
            if (current is null || !SameLifetime(original, current)) lost = invalid.Add(token);
            else { window = current; targets[token] = current; return true; }
        }
        if (lost) TargetLost?.Invoke(token);
        return false;
    }

    private static bool SameLifetime(NativeWindow a, NativeWindow b) =>
        a.TopHandle == b.TopHandle && a.SurfaceHandle == b.SurfaceHandle &&
        a.ProcessId == b.ProcessId && a.ProcessStartTicks == b.ProcessStartTicks;

    private void OnDestroyed(nint handle)
    {
        TargetToken[] lost;
        lock (gate)
        {
            lost = targets.Where(p => (p.Value.TopHandle == handle || p.Value.SurfaceHandle == handle)
                && !invalid.Contains(p.Key)).Select(p => p.Key).ToArray();
            foreach (var token in lost) invalid.Add(token);
        }
        foreach (var token in lost) TargetLost?.Invoke(token);
    }

    public void Dispose() => native.Destroyed -= OnDestroyed;
}
