using System.Text.RegularExpressions;
using Macrofy.Platform;
using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

public sealed record InputSurfaceOption(TargetWindow Window, string ClassName, bool IsTopLevel);
public sealed record PointerPositionResult(PointerPoint? Point, PlatformError? Error = null);

public sealed class WindowsWindowCatalog : IWindowCatalog, ITargetContext, IDisposable
{
    private readonly IWindowNative native;
    private readonly WindowGeometryProvider geometry = new();
    internal WindowIdentityRegistry Registry { get; }
    public event Action<TargetToken>? TargetLost;

    public WindowsWindowCatalog() : this(new Win32WindowNative()) { }
    internal WindowsWindowCatalog(IWindowNative native)
    {
        this.native = native;
        Registry = new(native);
        Registry.TargetLost += OnLost;
    }
    private void OnLost(TargetToken token) { geometry.Invalidate(token); TargetLost?.Invoke(token); }

    public Task<IReadOnlyList<TargetWindow>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TargetWindow> result = native.EnumerateWindows()
            .Where(w => w.ProcessId != native.CurrentProcessId && !string.IsNullOrWhiteSpace(w.Title))
            .Select(w => ToWindow(Registry.Register(w), w)).ToArray();
        return Task.FromResult(result);
    }

    public async Task<ResolutionResult> ResolveAsync(TargetRule rule, TargetToken? selected = null, CancellationToken cancellationToken = default)
    {
        var candidates = (await ListAsync(cancellationToken)).Where(w => Matches(rule, w)).ToArray();
        if (selected is { } token)
        {
            var chosen = candidates.FirstOrDefault(w => w.Token == token);
            return chosen is null ? new ResolutionResult.Missing() : new ResolutionResult.Matched(chosen);
        }
        return candidates.Length switch { 0 => new ResolutionResult.Missing(), 1 => new ResolutionResult.Matched(candidates[0]), _ => new ResolutionResult.Ambiguous(candidates) };
    }

    public ValueTask<TargetContextResult> GetAsync(TargetToken target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Registry.TryResolve(target, out var window))
            return ValueTask.FromResult(new TargetContextResult(null, new("TargetLost", "Original target or input surface disappeared. Select a target for a new session.")));
        var fingerprint = $"{window.ExecutablePath}|{window.FileIdentity}|{window.Title}|{window.SurfaceClass}";
        return ValueTask.FromResult(new TargetContextResult(new(ToWindow(target, window), window.IsForeground, fingerprint)));
    }

    public Task<IReadOnlyList<InputSurfaceOption>> ListInputSurfacesAsync(TargetToken target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Registry.TryResolve(target, out var original)) return Task.FromResult<IReadOnlyList<InputSurfaceOption>>([]);
        IReadOnlyList<InputSurfaceOption> options = native.ReadSurfaces(original).Select(w =>
            new InputSurfaceOption(ToWindow(Registry.Register(w), w), w.SurfaceClass, w.TopHandle == w.SurfaceHandle)).ToArray();
        return Task.FromResult(options);
    }

    private TargetWindow ToWindow(TargetToken token, NativeWindow window) => new(token,
        new(Path.GetFileNameWithoutExtension(window.ExecutablePath), window.ExecutablePath), window.Title, window.IsMinimized, geometry.Get(token, window));

    /// <summary>Reads the current physical pointer in the original surface's client coordinates; never substitutes a captured point.</summary>
    public PointerPositionResult ReadPlaybackPointerPosition(TargetToken target) => ReadPointerPosition(target, requireRestored: false);

    public PointerPositionResult ReadPointerPosition(TargetToken target) => ReadPointerPosition(target, requireRestored: true);

    private PointerPositionResult ReadPointerPosition(TargetToken target, bool requireRestored)
    {
        if (!Registry.TryResolve(target, out var window)) return new(null, new("TargetLost", "Original target disappeared."));
        if (requireRestored && window.IsMinimized) return new(null, new("Minimized", "Restore game to choose a visible point, then minimize it for the test."));
        var g = geometry.Get(target, window);
        if (g is null || !native.TryReadPointer(window.SurfaceHandle, out var point) || point.X < 0 || point.Y < 0 || point.X >= g.Width || point.Y >= g.Height)
            return new(null, new("OutsideClient", requireRestored ? "Hover inside the selected game input surface before capture finishes." : "Current pointer must map inside the selected input surface's valid client geometry."));
        return new(point);
    }

    private static bool Matches(TargetRule rule, TargetWindow window)
    {
        var app = rule.App.ExecutablePath is { Length: > 0 } path
            ? string.Equals(path, window.App.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            : string.Equals(rule.App.Name, window.App.Name, StringComparison.OrdinalIgnoreCase);
        var pattern = "\\A" + Regex.Escape(rule.TitlePattern).Replace("\\*", ".*").Replace("\\?", ".") + "\\z";
        return app && Regex.IsMatch(window.Title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
    }

    public void Dispose() { Registry.TargetLost -= OnLost; Registry.Dispose(); native.Dispose(); }
}
