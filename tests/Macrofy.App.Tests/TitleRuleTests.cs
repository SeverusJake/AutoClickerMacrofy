using Macrofy.App.Services;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.App.Tests;

public sealed class TitleRuleTests
{
    private const string Crosvm = @"C:\Program Files\Google\Play Games\current\emulator\crosvm.exe";

    [Theory]
    [InlineData("CookieRun*", "CookieRun: Crumble - Idle RPG - SeverusJake", true)]
    [InlineData("cookierun*", "CookieRun: Crumble", true)]
    [InlineData("CookieRun", "CookieRun: Crumble", false)]
    [InlineData("*Crumble*", "CookieRun: Crumble - Idle RPG", true)]
    [InlineData("Crumble*", "CookieRun: Crumble", false)]
    [InlineData("Game ?", "Game 7", true)]
    [InlineData("Game ?", "Game 77", false)]
    [InlineData("A.B", "AxB", false)]
    [InlineData("*", "", true)]
    public void MatchesWholeTitleWithWildcards(string rule, string title, bool expected) =>
        Assert.Equal(expected, TitleRule.Matches(rule, title));

    [Fact]
    public void MatchesWindowRequiresSameExecutableIgnoringCase()
    {
        var window = Window("CookieRun: Crumble", Crosvm);
        Assert.True(TitleRule.MatchesWindow(Crosvm.ToUpperInvariant(), "CookieRun*", window));
        Assert.False(TitleRule.MatchesWindow(@"C:\Other\crosvm.exe", "CookieRun*", window));
        Assert.False(TitleRule.MatchesWindow(Crosvm, "CookieRun*", Window("CookieRun: Crumble", null)));
    }

    [Theory]
    [InlineData("CookieRun: Crumble - Idle RPG - SeverusJake", "CookieRun: Crumble - Idle RPG")]
    [InlineData("Untitled - Notepad", "Untitled")]
    [InlineData("Discord", "Discord")]
    [InlineData(" - Leading", " - Leading")]
    public void SuggestedNameDropsLastSegment(string title, string expected) =>
        Assert.Equal(expected, TitleRule.SuggestedName(title));

    [Fact]
    public void SuggestionsAreOrderedWithLabels()
    {
        var suggestions = TitleRule.Suggestions("CookieRun: Crumble - Idle RPG - SeverusJake", Crosvm);
        Assert.Equal(new[]
        {
            new TitleRuleSuggestion("Without last part", "CookieRun: Crumble - Idle RPG*"),
            new TitleRuleSuggestion("Exact title", "CookieRun: Crumble - Idle RPG - SeverusJake"),
            new TitleRuleSuggestion("Program name", "*crosvm*")
        }, suggestions);
    }

    [Fact]
    public void SuggestionsHideDuplicatesAndMissingProgram()
    {
        Assert.Equal(new[] { "*crosvm**", "*crosvm*" }, TitleRule.Suggestions("*CROSVM*", Crosvm).Select(s => s.Rule.ToLowerInvariant()));
        Assert.Equal(new[] { "Discord*", "Discord" }, TitleRule.Suggestions("Discord", "").Select(s => s.Rule));
    }

    private static TargetWindow Window(string title, string? executable) =>
        new(new TargetToken(Guid.NewGuid()), new AppIdentity("app", executable), title, false, new ClientGeometry(800, 600, 1));
}
