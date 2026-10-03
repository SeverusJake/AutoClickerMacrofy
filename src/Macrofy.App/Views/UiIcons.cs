using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace Macrofy.App.Views;

internal static class UiIcons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["Profiles"] = "M3 7h6l2 2h10v11H3z M3 7V4h7l2 3",
        ["Apps"] = "M3 3h18v13H3z M8 21h8 M12 16v5",
        ["Macros"] = "M3 5h8 M3 12h8 M3 19h8 M15 5l7 7-7 7z",
        ["Compatibility"] = "M12 3L3 7v5c0 5 9 9 9 9s9-4 9-9V7z M8 12l3 3 5-6",
        ["Settings"] = "M10 2l-.5 3-2 .9L5 4.5 2.5 9 5 11v2l-2.5 2L5 19.5l2.5-1.4 2 .9.5 3h4l.5-3 2-.9 2.5 1.4 2.5-4.5-2.5-2v-2l2.5-2L19 4.5 16.5 6l-2-.9L14 2z M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0",
        ["Log"] = "M4 3h16v18H4z M8 7h8 M8 12h8 M8 17h5",
        ["About"] = "M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0 M12 11v6 M12 7v.1",
        ["play"] = "M8 4l13 8-13 8z",
        ["pause"] = "M8 5v14 M16 5v14",
        ["stop"] = "M5 5h14v14H5z",
        ["edit"] = "M14 5l5 5 M4 20l5-1L21 7l-5-5L4 14z",
        ["plus"] = "M12 4v16 M4 12h16",
        ["trash"] = "M3 6h18 M9 6V3h6v3 M5 6l1 15h12l1-15 M10 10v7 M14 10v7",
        ["check"] = "M4 12l5 5L20 6",
        ["close"] = "M6 6l12 12 M6 18L18 6",
        ["sun"] = "M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M12 1v2 M12 21v2 M1 12h2 M21 12h2 M4 4l2 2 M18 18l2 2 M4 20l2-2 M18 6l2-2",
        ["moon"] = "M20 15A9 9 0 0 1 9 3a9 9 0 1 0 11 12z",
        ["record"] = "M19 12a7 7 0 1 1-14 0 7 7 0 0 1 14 0",
        ["test"] = "M9 3h6 M10 3v7L4 19q-1 2 2 2h12q3 0 2-2l-6-9V3 M7 15h10",
        ["up"] = "M12 20V4 M5 11l7-7 7 7",
        ["down"] = "M12 4v16 M5 13l7 7 7-7",
        ["copy"] = "M3 3h13v13H3z M8 8h13v13H8z",
        ["Click"] = "M6 3v15l4-4 3 7 3-1.3-3-6.7h6z",
        ["Key"] = "M2 6h20v12H2z M6 10h.01 M10 10h.01 M14 10h.01 M18 10h.01 M7 14h10",
        ["Text"] = "M5 5h14 M12 5v14 M9 19h6",
        ["Wait"] = "M20 13a8 8 0 1 1-16 0 8 8 0 0 1 16 0 M12 9v4l3 2 M9 2h6",
        ["Wheel"] = "M12 3a5 5 0 0 1 5 5v8a5 5 0 0 1-10 0V8a5 5 0 0 1 5-5 M12 7v4",
        ["Mouse down"] = "M12 3v12 M7 10l5 5 5-5 M6 20h12",
        ["Mouse up"] = "M12 21V9 M7 14l5-5 5 5 M6 4h12",
        ["Key down"] = "M3 15h18v6H3z M12 3v8 M9 8l3 3 3-3",
        ["Key up"] = "M3 15h18v6H3z M12 11V3 M9 6l3-3 3 3",
        ["Combo key"] = "M2 9h14v10H2z M6 13h.01 M10 13h.01 M7 16h5 M20 3v6 M17 6h6"
    };
    public static Control Create(string name, IBrush color) => new Viewbox
    {
        Width = 18, Height = 18,
        Child = new Avalonia.Controls.Shapes.Path { Width = 24, Height = 24, Data = Geometry.Parse(Paths[name]), Stroke = color,
            StrokeThickness = name == "pause" ? 3 : 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Fill = name == "record" ? color : null }
    };
}
