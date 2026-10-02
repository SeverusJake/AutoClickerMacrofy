using Macrofy.Platform;
using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

internal readonly record struct ScreenNativeInput(uint Type, int X = 0, int Y = 0, uint Data = 0, uint Flags = 0, ushort VirtualKey = 0, ushort ScanCode = 0);
internal interface IScreenPlayerNative
{
    ScreenGeometry ReadGeometry();
    PointerPoint? ReadPointer();
    bool IsPointOnMonitor(PointerPoint point);
    bool IsKeyHeld(int key);
    int Send(ScreenNativeInput[] inputs);
}

/// <summary>Screen input with ownership limited to successfully inserted downs. Callers serialize complete gestures.</summary>
public sealed class WindowsScreenInputPlayer : IScreenInputPlayer
{
    private readonly IScreenPlayerNative native;
    private readonly object gate = new();
    private readonly List<ScreenNativeInput> heldReleases = [];
    // Inputs a playback session keeps down on purpose; release-all cleanup skips them.
    private readonly List<HeldInput> pinned = [];
    public WindowsScreenInputPlayer() : this(new Win32ScreenInputNative()) { }
    internal WindowsScreenInputPlayer(IScreenPlayerNative native) => this.native = native;
    public ScreenGeometry ReadGeometry() => native.ReadGeometry();
    public ScreenPointResult ReadPointer() => native.ReadPointer() is { } point ? new(point) : new(null, new("PointerUnavailable", "Screen pointer position could not be read."));

    public ValueTask<DeliveryResult> SendAsync(InputCommand command, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (cancellationToken.IsCancellationRequested) return ValueTask.FromResult(FailAndClean("Cancelled", "Screen input cancelled before injection."));
            ScreenNativeInput[] inputs;
            try { inputs = Encode(command); }
            catch (ArgumentException e) { return ValueTask.FromResult(FailAndClean("InvalidInput", e.Message)); }
            catch (HeldInputException e) { return ValueTask.FromResult(FailAndClean(e.Code, e.Message)); }
            if (cancellationToken.IsCancellationRequested) return ValueTask.FromResult(FailAndClean("Cancelled", "Screen input cancelled before injection."));
            if (inputs.Length == 0) return ValueTask.FromResult(new DeliveryResult(true));
            var inserted = Math.Clamp(native.Send(inputs), 0, inputs.Length);
            foreach (var input in inputs.Take(inserted)) ApplyInserted(input);
            if (inserted != inputs.Length) return ValueTask.FromResult(FailAndClean("DeliveryFailed", $"Windows inserted {inserted} of {inputs.Length} screen events. Input may be blocked by application permissions."));
            if (cancellationToken.IsCancellationRequested) return ValueTask.FromResult(FailAndClean("Cancelled", "Screen input cancelled after injection; inserted input cannot be withdrawn."));
            return ValueTask.FromResult(new DeliveryResult(true));
        }
    }

    public ValueTask<DeliveryResult> ReleaseHeldAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return ValueTask.FromResult(ReleaseLocked(cancellationToken));
    }

    private DeliveryResult FailAndClean(string code, string message)
    {
        // A cancelled delivery must not cancel its independent cleanup budget.
        var cleanup = ReleaseLocked(CancellationToken.None);
        return new(false, new(code, message), cleanup.Error);
    }

    private DeliveryResult ReleaseLocked(CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMilliseconds(500));
        var releases = heldReleases.AsEnumerable().Reverse().Where(release => !IsPinned(release)).ToArray();
        if (releases.Length == 0) return new(true);
        if (budget.IsCancellationRequested) return new(false, new("CleanupTimeout", "Screen release cancelled or exceeded its 500ms budget."));
        var inserted = Math.Clamp(native.Send(releases), 0, releases.Length);
        foreach (var release in releases.Take(inserted)) ApplyInserted(release);
        return inserted == releases.Length ? new(true) : new(false, new("CleanupFailed", $"Windows inserted {inserted} of {releases.Length} owned-input releases."));
    }

    public void Pin(HeldInput input) { lock (gate) pinned.Add(input); }
    public void Unpin(HeldInput input) { lock (gate) pinned.Remove(input); }

    private bool IsPinned(ScreenNativeInput release) => pinned.Any(pin =>
        pin.Button is { } button
            ? release.Type == 0 && release.Flags == button switch { MouseButton.Left => 4u, MouseButton.Right => 0x10u, MouseButton.Middle => 0x40u, _ => 0x100u }
            : pin.Key is { } key && release.Type == 1 && (release.Flags & 4) == 0 && release.VirtualKey == MessageEncoder.VirtualKey(key.LogicalKey));

    private ScreenNativeInput[] Encode(InputCommand command)
    {
        switch (command)
        {
            case PointerCommand pointer:
                if (!Enum.IsDefined(pointer.Kind)) throw new ArgumentException("Unknown pointer action.");
                if (pointer.Kind is PointerKind.VerticalWheel or PointerKind.HorizontalWheel)
                {
                    if (pointer.WheelDelta == 0 || pointer.WheelDelta is < short.MinValue or > short.MaxValue) throw new ArgumentException("Wheel delta must be a nonzero signed 16-bit value.");
                    var point = native.ReadPointer() ?? throw new HeldInputException("PointerUnavailable", "Screen wheel requires the current physical pointer.");
                    ValidatePoint(point, native.ReadGeometry());
                    return [new(0, Data: unchecked((uint)pointer.WheelDelta), Flags: pointer.Kind == PointerKind.VerticalWheel ? 0x800u : 0x1000u)];
                }
                var geometry = native.ReadGeometry();
                ValidatePoint(pointer.Point, geometry);
                var x = geometry.Width == 1 ? 0 : (int)Math.Round((Math.Floor(pointer.Point.X) - geometry.Left) * 65535 / (geometry.Width - 1));
                var y = geometry.Height == 1 ? 0 : (int)Math.Round((Math.Floor(pointer.Point.Y) - geometry.Top) * 65535 / (geometry.Height - 1));
                var move = new ScreenNativeInput(0, x, y, Flags: 0xC001);
                if (pointer.Kind == PointerKind.Move) return [move];
                if (pointer.Button is not { } button || !Enum.IsDefined(button)) throw new ArgumentException("Pointer down/up needs a valid button.");
                var vk = button switch { MouseButton.Left => 1, MouseButton.Right => 2, MouseButton.Middle => 4, MouseButton.X1 => 5, _ => 6 };
                uint downFlag = button switch { MouseButton.Left => 2u, MouseButton.Right => 8u, MouseButton.Middle => 0x20u, _ => 0x80u };
                uint upFlag = button switch { MouseButton.Left => 4u, MouseButton.Right => 0x10u, MouseButton.Middle => 0x40u, _ => 0x100u };
                uint data = button switch { MouseButton.X1 => 1u, MouseButton.X2 => 2u, _ => 0u };
                var release = new ScreenNativeInput(0, Data: data, Flags: upFlag);
                if (pointer.Kind == PointerKind.Down)
                {
                    if (heldReleases.Contains(release) || native.IsKeyHeld(vk)) throw new HeldInputException("MouseHeld", "Release the required mouse button before screen input.");
                    return [move, new(0, Data: data, Flags: downFlag)];
                }
                if (!heldReleases.Contains(release)) throw new ArgumentException("Cannot release a mouse button this player did not acquire.");
                return [move, release];

            case KeyCommand key:
                if (!Enum.IsDefined(key.Kind) || key.Key is null) throw new ArgumentException("Unknown key action.");
                int keyVk = MessageEncoder.VirtualKey(key.Key.LogicalKey);
                if (key.Kind == KeyKind.Up)
                {
                    var owned = heldReleases.FindLastIndex(i => i.Type == 1 && i.VirtualKey == keyVk && (i.Flags & 4) == 0);
                    if (owned < 0) throw new ArgumentException("Cannot release a key this player did not acquire.");
                    return [heldReleases[owned]];
                }
                if (heldReleases.Any(i => i.Type == 1 && i.VirtualKey == keyVk && (i.Flags & 4) == 0) || IsRequiredKeyHeld(keyVk))
                    throw new HeldInputException("KeyHeld", "Release the required physical key before screen input.");
                var scan = key.Key.NativeScanCode;
                if (scan is < 0 or > 0xE1FF) throw new ArgumentException("Invalid native scan code.");
                bool extended = key.Key.IsExtended || keyVk is 0xA3 or 0xA5 or 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C || (scan.GetValueOrDefault() & 0xFF00) is 0xE000 or 0xE100;
                return [new(1, Flags: (extended ? 1u : 0u) | (scan.HasValue ? 8u : 0u), VirtualKey: (ushort)keyVk, ScanCode: (ushort)(scan.GetValueOrDefault() & 0xFF))];

            case TextCommand text:
                if (text.Text is null || text.Text.Length > 4096) throw new ArgumentException("Text is required and limited to 4096 UTF-16 units.");
                for (var i = 0; i < text.Text.Length; i++)
                {
                    if (char.IsHighSurrogate(text.Text[i]))
                    {
                        if (++i >= text.Text.Length || !char.IsLowSurrogate(text.Text[i])) throw new ArgumentException("Text contains an unpaired surrogate.");
                    }
                    else if (char.IsLowSurrogate(text.Text[i])) throw new ArgumentException("Text contains an unpaired surrogate.");
                }
                return text.Text.SelectMany(c => new[] { new ScreenNativeInput(1, Flags: 4, ScanCode: c), new ScreenNativeInput(1, Flags: 6, ScanCode: c) }).ToArray();
            default: throw new ArgumentException("Unsupported screen input action.");
        }
    }

    private bool IsRequiredKeyHeld(int vk)
    {
        if (native.IsKeyHeld(vk)) return true;
        return vk switch
        {
            0x10 or 0xA0 or 0xA1 => native.IsKeyHeld(0x10) || native.IsKeyHeld(0xA0) || native.IsKeyHeld(0xA1),
            0x11 or 0xA2 or 0xA3 => native.IsKeyHeld(0x11) || native.IsKeyHeld(0xA2) || native.IsKeyHeld(0xA3),
            0x12 or 0xA4 or 0xA5 => native.IsKeyHeld(0x12) || native.IsKeyHeld(0xA4) || native.IsKeyHeld(0xA5),
            _ => false
        };
    }

    private void ValidatePoint(PointerPoint point, ScreenGeometry geometry)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || geometry.Width <= 0 || geometry.Height <= 0 ||
            point.X < geometry.Left || point.Y < geometry.Top || point.X >= (long)geometry.Left + geometry.Width ||
            point.Y >= (long)geometry.Top + geometry.Height || !native.IsPointOnMonitor(point))
            throw new ArgumentException("Choose a point on a connected monitor in screen pixels.");
    }

    private void ApplyInserted(ScreenNativeInput input)
    {
        if (input.Type == 1)
        {
            if ((input.Flags & 2) != 0) heldReleases.Remove(input);
            else heldReleases.Add(input with { Flags = input.Flags | 2 });
        }
        else
        {
            uint up = input.Flags switch { 2 => 4u, 8 => 0x10u, 0x20 => 0x40u, 0x80 => 0x100u, _ => 0u };
            if (up != 0) heldReleases.Add(input with { Flags = up });
            else if (input.Flags is 4 or 0x10 or 0x40 or 0x100) heldReleases.Remove(input);
        }
    }

    private sealed class HeldInputException(string code, string message) : Exception(message)
    { public string Code { get; } = code; }
}
