using System.Globalization;
using Avalonia.Controls;
using Macrofy.App.Models;
using Macrofy.Platform.Models;
namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    private bool captureBusy;
    private string captureMessage = "";
    private TextBlock? captureStatus;
    /// <summary>Seconds to hover before a point is read. Capturing only reads the pointer; it never sends input.</summary>
    public int CaptureCountdownSeconds { get; set; } = 5;

    private Control CapturePointControl(Macro macro, ActionDraft draft)
    {
        var button = TextButton("Capture point in 5 s", () => _ = CapturePointAsync(macro, draft)); button.Name = "CapturePoint";
        captureStatus = Wrap(captureMessage, "muted", 12); captureStatus.Name = "CaptureStatus";
        var panel = new StackPanel { Spacing = 4, Children = { button, captureStatus } };
        void Update()
        {
            panel.IsVisible = draft.Kind == "Click" && compatibility is not null;
            var pixels = macro.Coordinates != "Percentage";
            button.IsEnabled = pixels && !captureBusy && !Workspace.IsActive(macro) && !CompatibilityLocked;
            ToolTip.SetTip(button, pixels ? "Hover over the spot to click. Macrofy reads the pointer; no input is sent." : "Capture gives pixels. Switch Coordinates to Fixed pixels.");
        }
        refreshPlayback.Add(Update); Update(); return panel;
    }

    private async Task CapturePointAsync(Macro macro, ActionDraft draft)
    {
        if (captureBusy || compatibility is null) return;
        var app = macro.AppId is { } id ? Workspace.Profile.Apps.SingleOrDefault(a => a.Id == id) : null;
        if (macro.AppId is not null && app is null) { SetCaptureMessage("Saved target app is missing."); return; }
        captureBusy = true; RefreshPlayback();
        try
        {
            for (var seconds = CaptureCountdownSeconds; seconds > 0; seconds--)
            {
                SetCaptureMessage($"Hover over the spot to click{(app is null ? "" : " in " + app.Name)}… {seconds}s. No input is sent.");
                await Task.Delay(1000);
            }
            var point = await ReadCapturePointAsync(app);
            if (point.Point is not { } p) { SetCaptureMessage(point.Error?.Message ?? "Pointer unavailable."); return; }
            draft.Value = string.Create(CultureInfo.InvariantCulture, $"{p.X}, {p.Y}");
            SetCaptureMessage($"Captured {draft.Value}. Apply change to save it.");
        }
        catch (Exception error) { SetCaptureMessage("Capture failed: " + error.Message); }
        finally { captureBusy = false; Render(); }
    }

    private async Task<ScreenPointResult> ReadCapturePointAsync(SavedApp? app)
    {
        if (app is null) return compatibility!.ReadScreenPointer?.Invoke() ?? new(null, new("Unavailable", "Screen pointer reading is unavailable."));
        var resolution = await compatibility!.Catalog.ResolveAsync(new TargetRule(new AppIdentity(app.Name, app.Executable), app.TitleRule));
        return resolution switch
        {
            ResolutionResult.Matched match => compatibility.ReadPointer(match.Window.Token),
            ResolutionResult.Ambiguous => new(null, new("TargetAmbiguous", $"Several windows match {app.Name}. Narrow its title rule on the Apps tab.")),
            _ => new(null, new("TargetMissing", $"No open window matches {app.Name}. Open it first."))
        };
    }

    private void SetCaptureMessage(string text) { captureMessage = text; if (captureStatus is not null) captureStatus.Text = text; }
}
