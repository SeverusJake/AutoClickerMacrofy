using System.Text.RegularExpressions;
using Macrofy.Platform.Models;
namespace Macrofy.App.Services;

public sealed record TitleRuleSuggestion(string Label, string Rule);

/// <summary>Saved-app title rules: whole-title, case-insensitive; <c>*</c> matches any run and <c>?</c> one character.</summary>
public static class TitleRule
{
    private const string Separator = " - ";

    public static bool Matches(string rule, string title)
    {
        var pattern = "\\A" + Regex.Escape(rule).Replace("\\*", ".*").Replace("\\?", ".") + "\\z";
        try { return Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static bool MatchesWindow(string executable, string rule, TargetWindow window) =>
        string.Equals(executable, window.App.ExecutablePath, StringComparison.OrdinalIgnoreCase) && Matches(rule, window.Title);

    /// <summary>Drops a trailing " - account/document" part; titles without one are kept whole.</summary>
    public static string SuggestedName(string title)
    {
        var cut = title.LastIndexOf(Separator, StringComparison.Ordinal);
        return cut > 0 ? title[..cut] : title;
    }

    public static IReadOnlyList<TitleRuleSuggestion> Suggestions(string title, string executablePath)
    {
        var suggestions = new List<TitleRuleSuggestion> { new("Without last part", SuggestedName(title) + "*"), new("Exact title", title) };
        var program = Path.GetFileNameWithoutExtension(executablePath);
        if (program.Length > 0) suggestions.Add(new("Program name", "*" + program + "*"));
        return suggestions.DistinctBy(s => s.Rule, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
