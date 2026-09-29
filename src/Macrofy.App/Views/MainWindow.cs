using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Macrofy.App.Models;
using Macrofy.App.Services;

namespace Macrofy.App.Views;

public sealed partial class MainWindow : Window
{
    private static readonly string[] Tabs = ["Profiles", "Apps", "Macros", "Compatibility", "Settings", "Log", "About"];
    private readonly WorkspaceStore? store;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly List<(string Time, string Event)> log = [];
    private readonly List<Action> refreshPlayback = [];
    private readonly Dictionary<(Guid Macro, int Step), ActionDraft> drafts = [];
    private ThemePalette palette;
    private string selectedTab = "Profiles";
    private int selectedStep;
    private TextBlock footerStatus = new();
    private TextBlock messageText = new();
    private string message = "";
    private bool dirty;
    private Button pauseAll = new(), stopAll = new();
    private ComboBox? commonProfile;
    private Grid? responsiveEditor;
    private Control? responsiveSequence, responsiveInspector;
    public WorkspaceState Workspace { get; }

    public MainWindow() : this(new WorkspaceStore(System.IO.Path.Combine(AppContext.BaseDirectory, "MacrofyData"))) { }
    private MainWindow(WorkspaceStore store) : this(new WorkspaceState(store.Load()), store) { message = store.LoadError ?? ""; Render(); }
    public MainWindow(WorkspaceState workspace, WorkspaceStore? store = null)
    {
        Workspace = workspace; this.store = store;
        palette = new(workspace.Document.Theme, workspace.Document.Mode);
        Title = "Macrofy — UI preview"; Width = 1100; Height = 760; MinWidth = 780; MinHeight = 580;
        FontFamily = new FontFamily("Tahoma"); FontSize = 14;
        timer.Tick += (_, _) => { Workspace.Tick(); RefreshPlayback(); };
        Opened += (_, _) => timer.Start();
        Closed += (_, _) => { timer.Stop(); Workspace.StopAll(); };
        KeyDown += (_, e) => { if (e.Key == Key.F10) { Workspace.StopAll(); AddLog("Stopped all preview macros"); RefreshPlayback(); e.Handled = true; } };
        SizeChanged += (_, _) => ReflowEditor();
        Render();
    }

    private void Render()
    {
        refreshPlayback.Clear(); responsiveEditor = null; responsiveSequence = responsiveInspector = null;
        palette = new(Workspace.Document.Theme, Workspace.Document.Mode);
        RequestedThemeVariant = palette.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Background = palette.Brush("page"); Foreground = palette.Brush("ink");
        Styles.Clear();
        foreach (var type in new[] { typeof(TextBox), typeof(ComboBox), typeof(NumericUpDown) })
        {
            var style = new Style(x => x.OfType(type));
            style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, palette.Brush("surface")));
            style.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, palette.Brush("ink")));
            style.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, palette.Brush("line")));
            Styles.Add(style);
        }
        var root = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto") };
        var brand = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(12, 6) };
        brand.Children.Add(Text("Macrofy", "title-ink", 16));
        var topRight = Row(Text("UI preview", "title-ink", 12), Text("Profile: " + Workspace.Profile.Name, "title-ink", 12));
        Grid.SetColumn(topRight, 1); brand.Children.Add(topRight);
        Add(root, new Border { Background = palette.Brush("title"), Child = brand }, 0);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(6, 4, 6, 0) };
        var roles = new[] { "accent", "info", "tertiary", "secondary", "warning", "success", "danger" };
        for (var i = 0; i < Tabs.Length; i++)
        {
            var tab = Tabs[i];
            var button = IconButton(tab, tab, () => { selectedTab = tab; Render(); }, roles[i]);
            button.Name = "Tab_" + tab; button.Width = 44; button.Height = 36;
            button.CornerRadius = new CornerRadius(2, 2, 0, 0);
            button.Background = palette.Tint(roles[i], selectedTab == tab ? .23 : .10);
            button.BorderThickness = new Thickness(1, 2, 1, selectedTab == tab ? 0 : 1);
            AutomationProperties.SetHelpText(button, selectedTab == tab ? "Selected tab" : "Open " + tab);
            button.KeyDown += (_, e) =>
            {
                var index = Array.IndexOf(Tabs, tab);
                var next = e.Key == Key.Right ? (index + 1) % Tabs.Length : e.Key == Key.Left ? (index + Tabs.Length - 1) % Tabs.Length : e.Key == Key.Home ? 0 : e.Key == Key.End ? Tabs.Length - 1 : -1;
                if (next < 0 || e.KeyModifiers != KeyModifiers.None) return;
                selectedTab = Tabs[next]; Render(); UpdateLayout(); this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "Tab_" + selectedTab)?.Focus(); e.Handled = true;
            };
            tabs.Children.Add(button);
        }
        Add(root, new Border { Background = palette.Brush("subtle"), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = tabs }, 1);

        commonProfile = new ComboBox { Name = "ActiveProfile", ItemsSource = Workspace.Document.Profiles, SelectedItem = Workspace.Profile, Width = 240, MinHeight = 32 };
        AutomationProperties.SetName(commonProfile, "Active profile");
        commonProfile.SelectionChanged += (_, _) =>
        {
            if (commonProfile.SelectedItem is Profile profile && profile.Id != Workspace.Profile.Id) { Workspace.SelectProfile(profile.Id); selectedStep = 0; Save(); Render(); }
        };
        var appearance = Row(IconButton("sun", "Light mode", () => SetAppearance("light")), IconButton("moon", "Dark mode", () => SetAppearance("dark")));
        var context = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(16, 10) };
        context.Children.Add(Row(Text("Profile"), commonProfile)); Grid.SetColumn(appearance, 1); context.Children.Add(appearance);
        Add(root, new Border { Background = palette.Tint("secondary", .07), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = context }, 2);
        Control content = selectedTab switch
        {
            "Profiles" => ProfilesPane(), "Apps" => AppsPane(), "Macros" => MacrosPane(), "Compatibility" => CompatibilityPane(),
            "Settings" => SettingsPane(), "Log" => LogPane(), _ => AboutPane()
        };
        Add(root, content, 3);
        footerStatus = Text(Workspace.AggregateStatus, "muted", 12); footerStatus.Name = "GlobalStatus";
        messageText = Text(message, "danger", 12); messageText.TextWrapping = TextWrapping.Wrap;
        pauseAll = IconButton("pause", "Pause all preview macros", () => { Workspace.TogglePauseAll(); AddLog("Toggled pause for all preview macros"); RefreshPlayback(); }, "warning"); pauseAll.Name = "PauseAll";
        stopAll = IconButton("stop", "Stop all (F10)", () => { Workspace.StopAll(); AddLog("Stopped all preview macros"); RefreshPlayback(); }, "danger"); stopAll.Name = "StopAll";
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(16, 8) };
        footer.Children.Add(Stack(footerStatus, messageText)); var globalControls = Row(pauseAll, stopAll); Grid.SetColumn(globalControls, 1); footer.Children.Add(globalControls);
        Add(root, new Border { Background = palette.Tint("info", .06), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer }, 4);
        Content = root; RefreshPlayback(); ReflowEditor();
    }

    private void ReflowEditor()
    {
        if (responsiveEditor is null || responsiveSequence is null || responsiveInspector is null) return;
        var narrow = Bounds.Width < 1000;
        responsiveEditor.ColumnDefinitions = new(narrow ? "*" : "*,230"); responsiveEditor.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        Grid.SetColumn(responsiveInspector, narrow ? 0 : 1); Grid.SetRow(responsiveInspector, narrow ? 1 : 0);
    }
    private void Save()
    {
        if (store is null) return;
        try { store.Save(Workspace.Document); dirty = false; message = ""; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { dirty = true; message = "Changes not saved: " + error.Message; }
        messageText.Text = message; Title = "Macrofy — UI preview" + (dirty ? " · Unsaved" : "");
    }
    private void SetAppearance(string mode) { Workspace.Document.Mode = mode; Save(); Render(); }
    private void AddLog(string entry)
    {
        log.Insert(0, (DateTime.Now.ToString("HH:mm:ss"), entry));
        if (log.Count > 200) log.RemoveAt(log.Count - 1);
    }
    private void RefreshPlayback()
    {
        footerStatus.Text = Workspace.AggregateStatus;
        pauseAll.IsEnabled = stopAll.IsEnabled = Workspace.ActiveCount > 0;
        var paused = Workspace.ActiveCount > 0 && Workspace.Sessions.Values.All(s => s.State != "Running");
        SetIcon(pauseAll, paused ? "play" : "pause", paused ? "Resume all preview macros" : "Pause all preview macros", "warning");
        foreach (var refresh in refreshPlayback) refresh();
    }
    private void Start(Macro macro)
    {
        if (HasDraft(macro)) { messageText.Text = "Apply action edits before previewing."; return; }
        if (Workspace.StartPreview(macro)) { AddLog("Preview started: " + Workspace.Profile.Name + " / " + macro.Name); RefreshPlayback(); }
        else { message = "Cannot preview: add valid steps and a saved target, or choose Screen."; messageText.Text = message; }
    }
    private void Edit(Macro macro) { Workspace.SelectMacro(macro.Id); selectedStep = 0; selectedTab = "Macros"; Save(); Render(); }
    private bool HasDraft(Macro macro) => drafts.Any(pair => pair.Key.Macro == macro.Id && pair.Value.Changed);
    private void ClearDrafts(Macro macro) { foreach (var key in drafts.Keys.Where(key => key.Macro == macro.Id).ToArray()) drafts.Remove(key); }
    private TextBlock Text(string value, string role = "ink", double size = 14) => new()
    { Text = value, Foreground = palette.Brush(role), FontSize = size, VerticalAlignment = VerticalAlignment.Center };
    private static StackPanel Row(params Control[] controls) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center }.WithChildren(controls);
    private static StackPanel Stack(params Control[] controls) => new StackPanel { Spacing = 12 }.WithChildren(controls);
    private static void Add(Grid grid, Control control, int row, int column = 0) { Grid.SetRow(control, row); Grid.SetColumn(control, column); grid.Children.Add(control); }
    private Border Panel(Control content, string role = "surface", Thickness? padding = null) => new() { Background = palette.Brush(role), Padding = padding ?? new Thickness(20), Child = content };
    private Control Scroll(Control content) => new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private Button IconButton(string icon, string label, Action action, string role = "ink")
    {
        var button = new Button { Width = 34, Height = 34, Padding = new Thickness(6), CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), BorderBrush = palette.Brush(role), Background = role == "ink" ? palette.Brush("surface") : palette.Tint(role), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        SetIcon(button, icon, label, role); button.Click += (_, _) => action(); return button;
    }
    private void SetIcon(Button button, string icon, string label, string role = "ink")
    {
        if (button.Tag as string != icon) { button.Content = UiIcons.Create(icon, palette.Brush(role)); button.Tag = icon; }
        ToolTip.SetTip(button, label); AutomationProperties.SetName(button, label);
    }
    private Control Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label); return new StackPanel { Spacing = 6, Children = { Text(label, "muted", 12), input } };
    }
    private ComboBox Choice(IEnumerable<string> options, string selected, Action<string> changed)
    {
        var picker = new ComboBox { ItemsSource = options.ToArray(), SelectedItem = selected, MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is string value) changed(value); }; return picker;
    }
    private Button TextButton(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 6), Background = palette.Brush("surface"), Foreground = palette.Brush("ink"), BorderBrush = palette.Brush("line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) };
        button.Click += (_, _) => action(); return button;
    }
    private void NewProfile()
    {
        var macro = new Macro(); var profile = new Profile { Name = "New profile", Macros = [macro], SelectedMacroId = macro.Id };
        Workspace.Document.Profiles.Add(profile); Workspace.SelectProfile(profile.Id); selectedStep = 0; Save(); Render(); RenameProfile(profile);
    }
    private void NewMacro()
    {
        var macro = new Macro(); Workspace.Profile.Macros.Add(macro); Edit(macro); RenameMacro(macro);
    }
    private void OpenProbe()
    {
        var candidates = new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "CompatibilityProbe", "Macrofy.CompatibilityProbe.exe"), System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "compatibility-probe", "win-x64", "Macrofy.CompatibilityProbe.exe")) };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) { message = "Compatibility probe was not found beside the app."; messageText.Text = message; return; }
        try { Process.Start(new ProcessStartInfo(path, "--interactive") { UseShellExecute = true }); }
        catch (Exception error) { messageText.Text = "Could not open probe: " + error.Message; }
    }
}

internal sealed class ActionDraft(MacroStep original)
{
    public string Kind { get; set; } = original.Kind;
    public string Value { get; set; } = original.Value;
    public int Delay { get; set; } = original.DelayMs;
    public string Error { get; set; } = "";
    public bool Changed => Kind != original.Kind || Value != original.Value || Delay != original.DelayMs;
}

internal static class PanelChildren
{
    internal static T WithChildren<T>(this T panel, IEnumerable<Control> controls) where T : Avalonia.Controls.Panel
    { foreach (var control in controls) panel.Children.Add(control); return panel; }
}
