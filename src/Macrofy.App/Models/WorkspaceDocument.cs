namespace Macrofy.App.Models;

// Persistent workspace never includes live target tokens.
public sealed class WorkspaceDocument
{
    public int Version { get; set; } = 1;
    public string Theme { get; set; } = "neon-district";
    public string Mode { get; set; } = "light";
    public ShortcutSettings Shortcuts { get; set; } = new();
    public Guid ActiveProfileId { get; set; }
    public List<Profile> Profiles { get; set; } = [];
    public bool ShowAdvancedTools { get; set; }

    public static WorkspaceDocument CreateDefault()
    {
        var cookie = new SavedApp { Name = "CookieRun", Executable = @"C:\Program Files\Google\Play Games\current\emulator\crosvm.exe", TitleRule = "*CookieRun: Crumble - Idle RPG*" };
        var example = new SavedApp { Name = "Example game", Executable = @"C:\Games\Example\game.exe", TitleRule = "*Example game*" };
        var daily = new Profile { Name = "Daily games", Apps = [cookie, example], Macros = [
            new Macro { Name = "Auto click", AppId = example.Id, Steps = [new("Click", "480, 640", 100)] },
            new Macro { Name = "Collect rewards", AppId = cookie.Id, Steps = [new("Click", "600, 420", 500)] }
        ] };
        var notepad = new SavedApp { Name = "Notepad", Executable = @"C:\Windows\System32\notepad.exe", TitleRule = "*Notepad*" };
        var desktop = new Profile { Name = "Desktop tasks", Apps = [notepad], Macros = [
            new Macro { Name = "Open search", Steps = [new("Key", "Ctrl + K", 100)] },
            new Macro { Name = "Type note", AppId = notepad.Id, Steps = [new("Text", "Hello", 100), new("Key", "Enter", 100)] }
        ] };
        daily.SelectedMacroId = daily.Macros[0].Id; desktop.SelectedMacroId = desktop.Macros[0].Id;
        return new() { ActiveProfileId = daily.Id, Profiles = [daily, desktop] };
    }
}

public sealed class ShortcutSettings
{
    public string Run { get; set; } = "F9";
    public string Pause { get; set; } = "F8";
    public string Stop { get; set; } = "F10";
    /// <summary>Records the pointer position; registered only while a point is being recorded.</summary>
    public string Capture { get; set; } = "F7";
}

public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New profile";
    public Guid SelectedMacroId { get; set; }
    public List<SavedApp> Apps { get; set; } = [];
    public List<Macro> Macros { get; set; } = [];
    public override string ToString() => Name;
}

public sealed class SavedApp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New app";
    public string Executable { get; set; } = "";
    public string TitleRule { get; set; } = "*";
    public override string ToString() => Name;
}

public sealed class Macro
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New macro";
    public bool Enabled { get; set; } = true;
    public Guid? AppId { get; set; }
    public string WindowState { get; set; } = "Minimized";
    public string Coordinates { get; set; } = "Fixed pixels";
    public int Repeat { get; set; }
    public int IntervalMs { get; set; } = 1000;
    public List<MacroStep> Steps { get; set; } = [];
    public override string ToString() => Name;
}

public sealed record MacroStep(string Kind, string Value, int DelayMs);
