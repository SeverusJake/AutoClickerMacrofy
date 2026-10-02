#if WINDOWS
using Macrofy.Core.Actions;
using Macrofy.Core.Playback;
using Macrofy.Platform;
using Macrofy.Platform.Models;
using Macrofy.Platform.Windows;

namespace Macrofy.App.Services;

/// <summary>
/// Sends one balanced gesture. The caller owns the coordinator dispatch gate or an exclusive test lease
/// until this method returns, including its independent cleanup. This class does not grant compatibility.
/// </summary>
public sealed class WindowsGestureSender(ITargetContext targets, IPermissionService permission,
    IInputPlayer window, IScreenInputPlayer screen, Func<TargetToken, ScreenPointResult> readClientPointer,
    Func<bool> stopReady)
{
    public WindowsGestureSender(WindowsWindowCatalog catalog, IInputPlayer window, IScreenInputPlayer screen, Func<bool> stopReady)
        : this(catalog, new WindowsPermissionService(catalog), window, screen,
            token => { var point = catalog.ReadPlaybackPointerPosition(token); return new(point.Point, point.Error); }, stopReady) { }

    public async ValueTask<DeliveryResult> ValidateAsync(PlaybackBinding binding, bool fixedCoordinates, CancellationToken cancellationToken) =>
        (await CheckAsync(binding, fixedCoordinates, cancellationToken)).Result;

    internal async ValueTask<(DeliveryResult Result, TargetContext? Context)> CheckAsync(
        PlaybackBinding binding, bool fixedCoordinates, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return (Fail("Cancelled", "Input was cancelled."), null);
        if (!stopReady()) return (Fail("StopUnavailable", "Register the global Stop shortcut before sending input."), null);
        if (binding.Token is not { } token)
            return binding.SavedAppId is null && binding.State is null && binding.Fingerprint is null
                ? (new(true), null) : (Fail("InvalidTarget", "A window target cannot fall back to Screen."), null);
        if (binding.State is not { } state || !Enum.IsDefined(state) || token.Id == Guid.Empty || string.IsNullOrWhiteSpace(binding.Fingerprint))
            return (Fail("InvalidTarget", "Choose a valid live window and target state."), null);
        var current = await targets.GetAsync(token, cancellationToken);
        if (current.Context is not { } context) return (new(false, current.Error ?? new("TargetLost", "Original target disappeared.")), null);
        if (context.Window.Token != token || context.Window.Title != binding.Name || context.SurfaceFingerprint != binding.Fingerprint)
            return (Fail("TargetChanged", "Original target identity or input surface changed. Select it again."), null);
        if (context.Window.Geometry is not { Width: > 0, Height: > 0, DpiScale: > 0 } geometry || !double.IsFinite(geometry.DpiScale))
            return (Fail("InvalidGeometry", "Target client geometry is unavailable."), null);
        if (fixedCoordinates && geometry != binding.InitialGeometry)
            return (Fail("GeometryChanged", "Target geometry changed. Check fixed coordinates and compatibility again."), null);
        if (context.IsForeground || (state == TargetState.Minimized ? !context.Window.IsMinimized : context.Window.IsMinimized))
            return (Fail("TargetStateChanged", "Place the original target in the requested background or minimized state."), null);
        var access = await permission.CheckAsync(token, cancellationToken);
        return access.Allowed ? (new(true), context) : (new(false, access.Error ?? new("PermissionDenied", "Target permission could not be verified.")), null);
    }

    public async ValueTask<GestureResult> SendGestureAsync(PlaybackBinding binding, CompiledAction action, CancellationToken cancellationToken)
    {
        DeliveryResult delivery = new(true);
        PlatformError? cleanupError = null;
        var dispatched = false;
        try
        {
            var fixedCoordinates = action is CompiledAction.Click { Mode: CoordinateMode.FixedPixels } or CompiledAction.MouseDown { Mode: CoordinateMode.FixedPixels } or CompiledAction.MouseUp { Mode: CoordinateMode.FixedPixels };
            var check = await CheckAsync(binding, fixedCoordinates, cancellationToken);
            if (!check.Result.Queued) return new(check.Result, check.Result.CleanupError);
            if (action is CompiledAction.Wait) return new(new(true));
            var geometry = check.Context?.Window.Geometry;
            var commands = BuildCommands(binding, action, geometry);
            // A release stops protecting its input first, so failure cleanup still lets it go.
            foreach (var input in HeldBy(action, release: true)) Unpin(binding, input);
            async ValueTask<DeliveryResult> ValidatePost(CancellationToken ct)
            {
                var fresh = await CheckAsync(binding, fixedCoordinates, ct);
                if (!fresh.Result.Queued) return fresh.Result;
                // A percentage is recalculated between gestures; never reuse a converted point after a resize mid-gesture.
                if (binding.Token is not null && action is CompiledAction.Click or CompiledAction.Wheel or CompiledAction.MouseDown or CompiledAction.MouseUp && fresh.Context!.Window.Geometry != geometry)
                    return Fail("GeometryChanged", "Target geometry changed while preparing pointer input.");
                return new(true);
            }
            foreach (var command in commands)
            {
                delivery = await ValidatePost(cancellationToken);
                if (!delivery.Queued) break;
                dispatched = true;
                delivery = binding.Token is { } token
                    ? window is WindowsInputPlayer native
                        ? await native.SendValidatedAsync(token, command, ValidatePost, cancellationToken)
                        : await window.SendAsync(token, command, cancellationToken)
                    : await screen.SendAsync(command, cancellationToken);
                cleanupError ??= delivery.CleanupError;
                if (!delivery.Queued || delivery.Error is not null || delivery.CleanupError is not null) break;
            }
            if (delivery.Queued && delivery.Error is null && delivery.CleanupError is null)
            {
                var point = commands.OfType<PointerCommand>().LastOrDefault()?.Point ?? default;
                foreach (var input in HeldBy(action, release: false)) Pin(binding, input, point);
            }
        }
        catch (OperationCanceledException) { delivery = Fail("Cancelled", "Future input cancelled; already-delivered input cannot be withdrawn."); }
        catch (ArgumentException ex) { delivery = Fail("InvalidInput", ex.Message); }
        catch (Exception ex) { delivery = Fail("DeliveryFailed", ex.Message); }
        finally
        {
            if (dispatched)
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                try
                {
                    var cleanup = binding.Token is { } token
                        ? await window.ReleaseHeldAsync(token, budget.Token)
                        : await screen.ReleaseHeldAsync(budget.Token);
                    cleanupError ??= cleanup.CleanupError ?? cleanup.Error ?? (!cleanup.Queued ? new("CleanupFailed", "Owned input could not be released.") : null);
                }
                catch (OperationCanceledException) { cleanupError ??= new("CleanupTimeout", "Input release exceeded its independent 500ms budget."); }
                catch (Exception ex) { cleanupError ??= new("CleanupFailed", ex.Message); }
            }
        }
        return new(delivery, cleanupError);
    }

    private readonly object pinSync = new();
    private readonly List<(TargetToken? Token, HeldInput Input, PointerPoint Point)> pins = [];

    /// <summary>Best-effort release of session-held input at its stored point, without target-state checks.</summary>
    public async ValueTask<GestureResult> ReleaseHeldAsync(PlaybackBinding binding, IReadOnlyList<HeldInput> inputs, CancellationToken cancellationToken)
    {
        PlatformError? error = null;
        foreach (var input in inputs.Reverse())
        {
            var point = Unpin(binding, input);
            InputCommand command = input.Button is { } button ? new PointerCommand(PointerKind.Up, point, button) : new KeyCommand(KeyKind.Up, input.Key!);
            try
            {
                var result = binding.Token is { } token ? await window.SendAsync(token, command, cancellationToken) : await screen.SendAsync(command, cancellationToken);
                if (!result.Queued || result.Error is not null) error ??= result.Error ?? new("CleanupFailed", "Held input could not be released.");
            }
            catch (Exception ex) { error ??= new("CleanupFailed", ex.Message); }
        }
        return new(new(true), error);
    }

    private static IEnumerable<HeldInput> HeldBy(CompiledAction action, bool release) => (action, release) switch
    {
        (CompiledAction.MouseDown down, false) => [new(Button: down.Button)],
        (CompiledAction.MouseUp up, true) => [new(Button: up.Button)],
        (CompiledAction.KeyDown down, false) => down.Keys.Select(k => new HeldInput(Key: k)),
        (CompiledAction.KeyUp up, true) => up.Keys.Select(k => new HeldInput(Key: k)),
        _ => []
    };

    private void Pin(PlaybackBinding binding, HeldInput input, PointerPoint point)
    {
        lock (pinSync) pins.Add((binding.Token, input, point));
        if (binding.Token is { } token) window.Pin(token, input); else screen.Pin(input);
    }

    private PointerPoint Unpin(PlaybackBinding binding, HeldInput input)
    {
        PointerPoint point = default;
        lock (pinSync)
        {
            var index = pins.FindIndex(p => p.Token == binding.Token && (input.Button is { } b ? p.Input.Button == b
                : p.Input.Key is { } k && string.Equals(k.LogicalKey, input.Key?.LogicalKey, StringComparison.OrdinalIgnoreCase)));
            if (index < 0) return point;
            (point, input) = (pins[index].Point, pins[index].Input); pins.RemoveAt(index);
        }
        if (binding.Token is { } token) window.Unpin(token, input); else screen.Unpin(input);
        return point;
    }

    private PointerPoint ResolvePoint(PlaybackBinding binding, PointerPoint point, CoordinateMode mode, ClientGeometry? clientGeometry)
    {
        var screenGeometry = binding.Token is null ? screen.ReadGeometry() : default;
        var width = clientGeometry?.Width ?? screenGeometry.Width;
        var height = clientGeometry?.Height ?? screenGeometry.Height;
        var left = binding.Token is null ? screenGeometry.Left : 0;
        var top = binding.Token is null ? screenGeometry.Top : 0;
        if (!Enum.IsDefined(mode) || !double.IsFinite(point.X) || !double.IsFinite(point.Y) || width <= 0 || height <= 0)
            throw new ArgumentException("Click coordinates or target geometry are invalid.");
        if (mode == CoordinateMode.Percentage)
        {
            if (point.X < 0 || point.Y < 0 || point.X > 100 || point.Y > 100) throw new ArgumentException("Percentage coordinates must be between 0 and 100.");
            point = new(left + Math.Floor(point.X * (width - 1) / 100), top + Math.Floor(point.Y * (height - 1) / 100));
        }
        if (point.X < left || point.Y < top || point.X >= (long)left + width || point.Y >= (long)top + height)
            throw new ArgumentException("Click point is outside target geometry.");
        return point;
    }

    private IReadOnlyList<InputCommand> BuildCommands(PlaybackBinding binding, CompiledAction action, ClientGeometry? clientGeometry)
    {
        switch (action)
        {
            case CompiledAction.Click click:
                var point = ResolvePoint(binding, click.Point, click.Mode, clientGeometry);
                return [new PointerCommand(PointerKind.Down, point, click.Button), new PointerCommand(PointerKind.Up, point, click.Button)];
            case CompiledAction.MouseDown down:
                return [new PointerCommand(PointerKind.Down, ResolvePoint(binding, down.Point, down.Mode, clientGeometry), down.Button)];
            case CompiledAction.MouseUp up:
                return [new PointerCommand(PointerKind.Up, ResolvePoint(binding, up.Point, up.Mode, clientGeometry), up.Button)];
            case CompiledAction.KeyDown down when down.Keys.Count > 0:
                return down.Keys.Select(k => (InputCommand)new KeyCommand(KeyKind.Down, k)).ToArray();
            case CompiledAction.KeyUp up when up.Keys.Count > 0:
                return up.Keys.Reverse().Select(k => (InputCommand)new KeyCommand(KeyKind.Up, k)).ToArray();
            case CompiledAction.Key key when key.Keys.Count > 0:
                return key.Keys.Select(k => (InputCommand)new KeyCommand(KeyKind.Down, k))
                    .Concat(key.Keys.Reverse().Select(k => (InputCommand)new KeyCommand(KeyKind.Up, k))).ToArray();
            case CompiledAction.Text text:
                return [new TextCommand(text.Content)];
            case CompiledAction.Wheel wheel:
                var pointer = binding.Token is { } token ? readClientPointer(token) : screen.ReadPointer();
                if (pointer.Point is not { } current) throw new ArgumentException(pointer.Error?.Message ?? "Current pointer is unavailable.");
                return [new PointerCommand(PointerKind.VerticalWheel, current, WheelDelta: wheel.Delta)];
            default:
                throw new ArgumentException("Unsupported input action.");
        }
    }

    internal static DeliveryResult Fail(string code, string message) => new(false, new(code, message));
}
#endif
