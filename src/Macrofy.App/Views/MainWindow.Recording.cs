using System.Globalization;
using Avalonia.Controls;
using Macrofy.App.Models;
using Macrofy.Platform.Models;
namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    private sealed record PointRecording(Func<Task<ScreenPointResult>> Read, Action<PointerPoint> Captured, Action<string> Report);
    private PointRecording? recording;
    private string recordMessage = "";

    /// <summary>Record point button: the record key is registered only while waiting; pressing it reads the pointer and never sends input.</summary>
    private Button RecordPointButton(string name, Func<bool> available, Func<Task<ScreenPointResult>> read, Action<PointerPoint> captured, Action<string> report)
    {
        var button = TextButton("", () => { }); button.Name = name;
        PointRecording? mine = null;
        bool Active() => mine is not null && ReferenceEquals(recording, mine);
        bool CanStart() => recording is null && available() && Workspace.ActiveCount == 0 && !CompatibilityLocked && playback.HotkeysReady;
        void Update()
        {
            button.Content = Active() ? "Cancel recording" : $"Record point ({Workspace.Document.Shortcuts.Capture})";
            button.IsEnabled = Active() || CanStart();
        }
        button.Click += (_, _) =>
        {
            if (Active()) { EndRecording(); report(""); }
            else if (CanStart())
            {
                mine = new(read, captured, report);
                if (StartRecording(mine)) report($"Hover over the spot and press {Workspace.Document.Shortcuts.Capture}. Press the button again to cancel.");
            }
            RefreshPlayback();
        };
        refreshPlayback.Add(Update); Update(); return button;
    }

    private bool StartRecording(PointRecording next)
    {
        var shortcuts = Workspace.Document.Shortcuts;
        var set = new HotkeySet(new(shortcuts.Run), new(shortcuts.Pause), new(shortcuts.Stop), new(shortcuts.Capture));
        var result = hotkeys.Configure(set);
        if (!result.Registered)
        {
            next.Report("Record key unavailable: " + (result.Error?.Message ?? "registration failed."));
            if (hotkeys.Configure(set with { Capture = null }).Registered) registeredKeys = set with { Capture = null };
            return false;
        }
        registeredKeys = set; recording = next; return true;
    }

    private void EndRecording()
    {
        if (recording is null) return;
        recording = null;
        var shortcuts = Workspace.Document.Shortcuts;
        var set = new HotkeySet(new(shortcuts.Run), new(shortcuts.Pause), new(shortcuts.Stop));
        if (hotkeys.Configure(set).Registered) registeredKeys = set;
    }

    private async Task CompleteRecordingAsync()
    {
        if (recording is not { } current) return;
        EndRecording();
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
