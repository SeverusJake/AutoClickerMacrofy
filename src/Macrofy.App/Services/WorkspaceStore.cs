using System.Text.Json;
using System.Security.Cryptography;
using Macrofy.App.Models;

namespace Macrofy.App.Services;

public sealed class WorkspaceStore(string folder)
{
    private readonly string path = Path.Combine(folder, "ui-workspace.json");
    private bool blocked;
    private string? loadedHash;
    public string? LoadError { get; private set; }
    public string Folder => folder;

    public WorkspaceDocument Load()
    {
        if (!File.Exists(path)) return WorkspaceDocument.CreateDefault();
        try
        {
            var bytes = File.ReadAllBytes(path);
            var result = JsonSerializer.Deserialize<WorkspaceDocument>(bytes) ?? throw new InvalidDataException("Empty workspace.");
            if (result.Version != 1) throw new InvalidDataException("Unsupported workspace version.");
            if (result.CompatibilityEvidence is null || result.CompatibilityEvidence.Any(e => e is null || e.SavedAppId == Guid.Empty ||
                e.App is null || string.IsNullOrWhiteSpace(e.App.Name) || string.IsNullOrWhiteSpace(e.App.ExecutablePath) ||
                e.Title is null || string.IsNullOrWhiteSpace(e.SurfaceFingerprint) || !Enum.IsDefined(e.State) || !Enum.IsDefined(e.Capability) ||
                e.Geometry is not { Width: > 0, Height: > 0, DpiScale: > 0 } || !double.IsFinite(e.Geometry.DpiScale)))
                throw new InvalidDataException("Invalid compatibility evidence.");
            if (result.Mode is not ("light" or "dark") || ThemePalette.Choices.All(t => t.Id != result.Theme)) throw new InvalidDataException("Invalid appearance settings.");
            if (result.Shortcuts is null || new[] { result.Shortcuts.Run, result.Shortcuts.Pause, result.Shortcuts.Stop }
                .Any(key => key is null || !System.Text.RegularExpressions.Regex.IsMatch(key, "^F(?:[1-9]|1[0-2])$")) ||
                new[] { result.Shortcuts.Run, result.Shortcuts.Pause, result.Shortcuts.Stop }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
                throw new InvalidDataException("Shortcut keys must be unique function keys from F1 to F12.");
            if (result.Profiles is null || result.Profiles.Count == 0 ||
                result.Profiles.Any(p => p is null || string.IsNullOrWhiteSpace(p.Name) || p.Apps is null || p.Macros is null || p.Macros.Count == 0 ||
                    p.Apps.Any(a => a is null || string.IsNullOrWhiteSpace(a.Name) || a.Executable is null || a.TitleRule is null) ||
                    p.Macros.Any(m => m is null || string.IsNullOrWhiteSpace(m.Name) || m.Repeat < 0 || m.IntervalMs is < 0 or > 600000 ||
                        m.Coordinates is not ("Fixed pixels" or "Percentage") || m.WindowState is not ("Minimized" or "Background") ||
                        m.Steps is null || m.Steps.Any(s => s is null || string.IsNullOrWhiteSpace(s.Kind) || s.Value is null || s.DelayMs is < 0 or > 600000))))
                throw new InvalidDataException("Invalid workspace data.");
            var ids = result.Profiles.SelectMany(p => p.Macros.Select(m => m.Id).Concat(p.Apps.Select(a => a.Id)).Append(p.Id)).ToArray();
            if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length) throw new InvalidDataException("Invalid workspace identities.");
            loadedHash = Convert.ToHexString(SHA256.HashData(bytes));
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
        {
            blocked = true;
            LoadError = "Workspace could not load. Original file preserved: " + error.Message;
            return WorkspaceDocument.CreateDefault();
        }
    }

    public void Save(WorkspaceDocument document)
    {
        if (blocked) throw new InvalidOperationException("Original workspace preserved. Saving is blocked until its load error is resolved.");
        Directory.CreateDirectory(folder);
        using var ownership = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var currentHash = File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
        if (currentHash != loadedHash) throw new InvalidOperationException("Workspace changed in another app instance. Reopen to load its changes; your unsaved edits remain in this window.");
        var temporary = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllBytes(temporary, bytes);
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
        loadedHash = Convert.ToHexString(SHA256.HashData(bytes));
    }
}
