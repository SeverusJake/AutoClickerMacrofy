using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

internal sealed class WindowGeometryProvider
{
    private readonly Dictionary<TargetToken, ClientGeometry> cache = [];
    public ClientGeometry? Get(TargetToken token, NativeWindow window)
    {
        lock (cache)
        {
            var g = window.Geometry;
            if (g is { Width: > 0, Height: > 0 } && double.IsFinite(g.DpiScale) && g.DpiScale > 0)
                return cache[token] = g;
            return window.IsMinimized && cache.TryGetValue(token, out var old) ? old : null;
        }
    }
    public void Invalidate(TargetToken token) { lock (cache) cache.Remove(token); }
}
