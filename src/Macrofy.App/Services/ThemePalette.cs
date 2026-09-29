using Avalonia.Media;

namespace Macrofy.App.Services;

public sealed record ThemeChoice(string Id, string Name, string Accent, string Secondary, string Tertiary, string Success, string Warning, string Danger, string Info);

public sealed class ThemePalette
{
    public static readonly ThemeChoice[] Choices = [
        new("neon-district", "Neon District", "#ffe600", "#00e5ff", "#ff70d6", "#b7ff5a", "#ffb454", "#ff7186", "#8eabff"),
        new("synthwave", "Synthwave", "#ff66c4", "#45e7ef", "#bd99ff", "#89f5ad", "#ffd36d", "#ff8799", "#76baff"),
        new("matrix", "Matrix", "#70ff89", "#d0ff5a", "#bba0ff", "#48e2a0", "#ffd05a", "#ff8b83", "#64c9ff"),
        new("tron", "Tron", "#5ce5ff", "#ffb454", "#cb9aff", "#9dff8a", "#ffcb66", "#ff7ea2", "#8da8ff"),
        new("redline", "Redline", "#ff667d", "#ffb05a", "#ff82df", "#acff86", "#ffc467", "#ff8075", "#7baeff"),
        new("windows-blue", "Windows Blue", "#72bcff", "#58cce5", "#bc9aea", "#85d39b", "#e9b769", "#ef8a99", "#9bafff"),
        new("graphite", "Graphite", "#cbd5e1", "#adc6d5", "#ccb9e4", "#a2d5b2", "#dec59a", "#e2a2ad", "#aebeea"),
        new("emerald", "Emerald", "#00d4aa", "#54d4e0", "#c19dea", "#92eba9", "#eac36e", "#ff839e", "#91b7ff"),
        new("violet", "Violet", "#bb86fc", "#70cce8", "#eaa2e1", "#94dcb0", "#e6c777", "#f693a8", "#94b7f7"),
        new("warm-amber", "Warm Amber", "#ffb95f", "#8acbcf", "#caa1e3", "#a9d69b", "#e2c67e", "#f29fa8", "#97b6e8")
    ];
    public ThemeChoice Choice { get; }
    public bool Dark { get; }
    private readonly Dictionary<string, Color> colors;
    public ThemePalette(string id, string mode)
    {
        Choice = Choices.FirstOrDefault(c => c.Id == id) ?? Choices[0]; Dark = mode == "dark";
        colors = new() { ["page"] = Color.Parse(Dark ? "#101216" : "#fafafa"), ["surface"] = Color.Parse(Dark ? "#18191d" : "#ffffff"),
            ["ink"] = Color.Parse(Dark ? "#f2f2f5" : "#202330"), ["muted"] = Color.Parse(Dark ? "#bfc4cc" : "#555968"),
            ["line"] = Color.Parse(Dark ? "#535964" : "#b7bbc4") };
        var bases = Choice.Id switch
        {
            "synthwave" => ("#110f1b", "#211a31", "#59275f"), "matrix" => ("#030805", "#0d1811", "#14391e"),
            "tron" => ("#060b11", "#10202a", "#0d4054"), "redline" => ("#100b0d", "#23171b", "#88253b"),
            "windows-blue" => ("#101316", "#1d2229", "#0078d4"), "graphite" => ("#18181b", "#27272a", "#3f4650"),
            "emerald" => ("#0f0f0f", "#1a1a1a", "#006b55"), "violet" => ("#121212", "#1e1e1e", "#65428b"),
            "warm-amber" => ("#101316", "#1d2229", "#a7540b"), _ => ("#0a0c10", "#18191d", "#ffe600")
        };
        var originalAccent = Color.Parse(Choice.Accent);
        colors["page"] = Dark ? Color.Parse(bases.Item1) : Mix(Colors.White, originalAccent, .035);
        colors["surface"] = Dark ? Color.Parse(bases.Item2) : Mix(Colors.White, originalAccent, .008);
        foreach (var (role, value) in new[] { ("accent", Choice.Accent), ("secondary", Choice.Secondary), ("tertiary", Choice.Tertiary), ("success", Choice.Success), ("warning", Choice.Warning), ("danger", Choice.Danger), ("info", Choice.Info) })
        {
            var color = Color.Parse(value);
            if (!Dark)
                for (var i = 0; i <= 25; i++)
                {
                    var candidate = Mix(color, Colors.Black, i * .04);
                    if (Contrast(candidate, Mix(colors["surface"], candidate, .23)) >= 4.5) { color = candidate; break; }
                }
            colors[role] = color;
        }
        colors["subtle"] = Mix(colors["surface"], colors["accent"], .04);
        colors["title"] = Color.Parse(bases.Item3);
        colors["title-ink"] = Contrast(colors["title"], Colors.Black) > Contrast(colors["title"], Colors.White) ? Colors.Black : Colors.White;
    }
    public IBrush Brush(string role) => new SolidColorBrush(colors[role]);
    public IBrush Tint(string role, double amount = .12) => new SolidColorBrush(Mix(colors["surface"], colors[role], amount));
    private static Color Mix(Color background, Color foreground, double amount) => Color.FromRgb(
        (byte)Math.Round(background.R * (1 - amount) + foreground.R * amount),
        (byte)Math.Round(background.G * (1 - amount) + foreground.G * amount),
        (byte)Math.Round(background.B * (1 - amount) + foreground.B * amount));
    private static double Luminance(Color color)
    {
        static double Linear(byte v) { var n = v / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    private static double Contrast(Color a, Color b) { var x = Luminance(a); var y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
}
