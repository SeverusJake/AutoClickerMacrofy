using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Macrofy.App.Models;
using Macrofy.Platform.Models;
namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    private sealed record PointRecording(Func<Task<ScreenPointResult>> Read, Action<PointerPoint> Captured, Action<string> Report);
    private PointRecording? recording;
    private string recordMessage = "";

    /// <summary>Record point button: the next left click outside Macrofy is blocked and its position read; no input is sent.</summary>
    private Button RecordPointButton(string name, Func<bool> available, Func<Task<ScreenPointResult>> read, Action<PointerPoint> captured, Action<string> report)
    {
        var button = TextButton("", () => { }); button.Name = name;
        PointRecording? mine = null;
        bool Active() => mine is not null && ReferenceEquals(recording, mine);
        bool CanStart() => recording is null && available() && Workspace.ActiveCount == 0 && !CompatibilityLocked;
        void Update()
        {
            button.Content = Active() ? "Cancel recording" : "Record point";
            button.IsEnabled = Active() || CanStart();
        }
        button.Click += (_, _) =>
        {
            if (Active()) { EndRecording(); report(""); }
            else if (CanStart())
            {
                mine = new(read, captured, report);
                if (StartRecording(mine)) report("Click the spot to record it. That click is not sent to the app. Click Cancel recording to stop.");
            }
            RefreshPlayback();
        };
        refreshPlayback.Add(Update); Update(); return button;
    }

    private bool StartRecording(PointRecording next)
    {
        if (compatibility?.Clicks is not { } clicks) { next.Report("Click recording is unavailable."); return false; }
        recording = next;
        var error = clicks.Start(() => Dispatcher.UIThread.Post(() => _ = CompleteRecordingAsync()));
        if (error is null) return true;
        recording = null; next.Report("Recording unavailable: " + error.Message); return false;
    }

    /// <summary>Ends the recording; the click source is cancelled unless it already finished after swallowing the click.</summary>
    private void EndRecording(bool cancelSource = true)
    {
        if (recording is null) return;
        recording = null;
        if (cancelSource) compatibility?.Clicks?.Cancel();
    }

    private async Task CompleteRecordingAsync()
    {
        if (recording is not { } current) return;
        EndRecording(cancelSource: false);
        try
        {
            var point = await current.Read();
            if (point.Point is { } p) current.Captured(p); else current.Report(point.Error?.Message ?? "Pointer unavailable.");
        }
        catch (Exception error) { current.Report("Record failed: " + error.Message); }
        RefreshPlayback();
    }

    private async Task<ScreenPointResult> ReadMacroPointAsync(SavedApp? app)
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

    private static string FormatPoint(PointerPoint point) => string.Create(CultureInfo.InvariantCulture, $"{point.X}, {point.Y}");
}
