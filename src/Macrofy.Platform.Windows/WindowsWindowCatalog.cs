using System.Text.RegularExpressions;
using Macrofy.Platform;
using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

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

    private TargetWindow ToWindow(TargetToken token, NativeWindow window) => new(token,
        new(Path.GetFileNameWithoutExtension(window.ExecutablePath), window.ExecutablePath), window.Title, window.IsMinimized, geometry.Get(token, window));

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
