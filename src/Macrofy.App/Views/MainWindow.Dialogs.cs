using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Macrofy.App.Models;

namespace Macrofy.App.Views;

public sealed partial class MainWindow
{
    private Window Dialog(string title, Control body)
    {
        return new Window { Title = title, Width = 540, SizeToContent = SizeToContent.Height, MinWidth = 420, MaxHeight = 700, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = palette.Brush("surface"), Foreground = palette.Brush("ink"),
            RequestedThemeVariant = RequestedThemeVariant, FontFamily = FontFamily, FontSize = 14, Content = new Border { Padding = new Thickness(24), Child = body } };
    }
    private void RenameProfile(Profile profile) => NameDialog("Profile name", profile.Name, value => { profile.Name = value; Save(); Render(); });
    private void RenameMacro(Macro macro)
    {
        if (Workspace.IsActive(macro) || CompatibilityLocked) return;
        NameDialog("Macro name", macro.Name, value => { if (Workspace.IsActive(macro) || CompatibilityLocked) return; macro.Name = value; Save(); Render(); });
    }
    private void NameDialog(string title, string current, Action<string> changed)
    {
        var name = new TextBox { Text = current, MinHeight = 34 }; var error = Text("", "danger", 12);
        var body = Stack(Field(title, name), error); var dialog = Dialog(title, body);
        body.Children.Add(Row(IconButton("check", "Save name", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "Enter a name."; return; }
            changed(name.Text.Trim()); dialog.Close();
        }, "success"), IconButton("close", "Cancel", dialog.Close)));
        dialog.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        _ = dialog.ShowDialog(this);
    }
    private void Confirm(string title, Action action)
    {
        var body = Stack(Text(title, size: 16)); var dialog = Dialog("Confirm", body);
        body.Children.Add(Row(TextButton("Delete", () => { action(); dialog.Close(); }), TextButton("Cancel", dialog.Close)));
        _ = dialog.ShowDialog(this);
    }
    private void AppDialog(SavedApp? app)
    {
        if (CompatibilityLocked) return;
        if (app is not null && Workspace.Profile.Macros.Any(m => m.AppId == app.Id && Workspace.IsActive(m))) return;
        var profile = Workspace.Profile;
        var name = new TextBox { Text = app?.Name ?? "", MinHeight = 34 };
        var executable = new TextBox { Text = app?.Executable ?? "", MinHeight = 34 };
        var title = new TextBox { Text = app?.TitleRule ?? "*", MinHeight = 34 };
        var error = Text("", "danger", 12);
        var body = Stack(Field("App name", name), Field("Executable path", executable), Field("Window title rule", title), error);
        var dialog = Dialog(app is null ? "Add app" : "Edit app", body);
        body.Children.Add(Row(IconButton("check", "Save app", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(executable.Text) || string.IsNullOrWhiteSpace(title.Text)) { error.Text = "Enter an app name, executable path and title rule."; return; }
            if (CompatibilityLocked || (app is not null && profile.Macros.Any(m => m.AppId == app.Id && Workspace.IsActive(m)))) return;
            var item = app ?? new SavedApp(); playback.SelectSurface(item.Id, null); item.Name = name.Text.Trim(); item.Executable = executable.Text.Trim(); item.TitleRule = title.Text.Trim();
            if (app is null) profile.Apps.Add(item);
            ResetCompatibilityContext();
            Save(); Render(); dialog.Close();
        }, "success"), IconButton("close", "Cancel", dialog.Close)));
        _ = dialog.ShowDialog(this);
    }
    private void DuplicateProfile()
    {
        if (CompatibilityLocked) return;
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(Workspace.Profile))!;
        copy.Id = Guid.NewGuid(); copy.Name += " copy"; var apps = new Dictionary<Guid, Guid>();
        foreach (var app in copy.Apps) { var old = app.Id; app.Id = Guid.NewGuid(); apps[old] = app.Id; }
        foreach (var macro in copy.Macros) { macro.Id = Guid.NewGuid(); if (macro.AppId is { } id && apps.TryGetValue(id, out var newId)) macro.AppId = newId; }
        copy.SelectedMacroId = copy.Macros[0].Id;
        Workspace.Document.Profiles.Add(copy); Workspace.SelectProfile(copy.Id); selectedStep = 0; Save(); Render();
    }
    private void DuplicateMacro(Macro macro)
    {
        var copy = JsonSerializer.Deserialize<Macro>(JsonSerializer.Serialize(macro))!;
        copy.Id = Guid.NewGuid(); copy.Name += " copy"; Workspace.Profile.Macros.Add(copy); Edit(copy);
    }
    private void DeleteProfile()
    {
        var profile = Workspace.Profile;
        if (CompatibilityLocked || Workspace.Document.Profiles.Count < 2 || profile.Macros.Any(Workspace.IsActive)) return;
        Confirm("Delete profile “" + profile.Name + "” and its macros?", () =>
        {
            if (CompatibilityLocked || profile.Macros.Any(Workspace.IsActive)) return;
            foreach (var app in profile.Apps) playback.SelectSurface(app.Id, null);
            foreach (var item in profile.Macros) ClearDrafts(item);
            Workspace.Document.Profiles.Remove(profile); Workspace.SelectProfile(Workspace.Document.Profiles[0].Id); selectedStep = 0; Save(); Render();
        });
    }
    private void DeleteMacro(Macro macro)
    {
        var profile = Workspace.Profile;
        if (CompatibilityLocked || profile.Macros.Count < 2 || Workspace.IsActive(macro)) return;
        Confirm("Delete macro “" + macro.Name + "”?", () =>
        {
            if (CompatibilityLocked || Workspace.IsActive(macro)) return;
            ClearDrafts(macro); profile.Macros.Remove(macro); Workspace.SelectMacro(profile.Macros[0].Id); selectedStep = 0; Save(); Render();
        });
    }
}
