using Macrofy.Platform.Models;

namespace Macrofy.Platform.Windows;

public interface IScreenClicker
{
    PointerPositionResult ReadPointerPosition();
    ValueTask<DeliveryResult> ClickAsync(PointerPoint point, CancellationToken cancellationToken = default);
}

/// <summary>Explicit screen input. Window playback never falls back to this player.</summary>
public sealed class WindowsScreenClicker : IScreenClicker
{
    private readonly IScreenInputNative native;
    public WindowsScreenClicker() : this(new Win32ScreenInputNative()) { }
    internal WindowsScreenClicker(IScreenInputNative native) => this.native = native;

    public PointerPositionResult ReadPointerPosition() => native.ReadPointer() is { } point
        ? new(point) : new(null, new("PointerUnavailable", "Screen pointer position could not be read."));

    public ValueTask<DeliveryResult> ClickAsync(PointerPoint point, CancellationToken cancellationToken = default)
    {
        DeliveryResult result;
        if (cancellationToken.IsCancellationRequested) result = Failure("Cancelled", "Screen click cancelled before injection.");
        else
        {
            var bounds = native.ReadBounds();
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || bounds.Width <= 1 || bounds.Height <= 1 ||
                point.X < bounds.Left || point.Y < bounds.Top || point.X >= (long)bounds.Left + bounds.Width ||
                point.Y >= (long)bounds.Top + bounds.Height || !native.IsPointOnMonitor(point))
                result = Failure("InvalidInput", "Choose a point on a connected monitor in screen pixels.");
            else if (native.IsLeftButtonHeld()) result = Failure("MouseHeld", "Release the physical left mouse button before testing.");
            else if (cancellationToken.IsCancellationRequested) result = Failure("Cancelled", "Screen click cancelled before injection.");
            else
            {
                var px = (int)Math.Round((Math.Floor(point.X) - bounds.Left) * 65535 / (bounds.Width - 1));
                var py = (int)Math.Round((Math.Floor(point.Y) - bounds.Top) * 65535 / (bounds.Height - 1));
                // One ordered batch: absolute move, left down, left up. There is no send-ahead loop.
                var inserted = native.Send([new(px, py, 0xC001), new(0, 0, 2), new(0, 0, 4)]);
                if (inserted == 3) result = new(true);
                else
                {
                    // A partial batch can leave down inserted without up. Release once, without moving again.
                    var released = inserted != 2 || native.Send([new(0, 0, 4)]) == 1;
                    result = Failure(released ? "DeliveryFailed" : "CleanupFailed",
                        $"Windows inserted {inserted} of 3 screen events. Input may be blocked by application permissions.{(released ? "" : " Left-button release also failed.")}");
                }
            }
        }
        return ValueTask.FromResult(result);
    }
    private static DeliveryResult Failure(string code, string message) => new(false, new(code, message));
}
