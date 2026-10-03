using Macrofy.Core.Actions;
using Xunit;
namespace Macrofy.Core.Tests;
public class ActionCompilerTests
{
    private static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "F10" };
    [Fact] public void CompiledChordOwnsImmutableSnapshot()
    {
        var source = new List<Macrofy.Platform.Models.KeyIdentity> { new("A") };
        var action = new CompiledAction.Key(source, 0);
        source[0] = new("B");
        Assert.Equal("A", action.Keys[0].LogicalKey);
    }
    [Theory] [InlineData("Click", "-50.5, 1000", 600000)] [InlineData("Wheel", "-32768", 0)] [InlineData("Wheel", "32767", 0)]
    public void AcceptsFixedPixelsWheelAndDelayEndpoints(string kind, string value, int delay) =>
        Assert.True(ActionCompiler.TryCompile(new(kind, value, delay), CoordinateMode.FixedPixels, Reserved, out _, out _));
    [Fact] public void ChordPreservesCanonicalDeclaredOrder()
    {
        Assert.True(ActionCompiler.TryCompile(new("Combo key", "ctrl + shift + a", 25), CoordinateMode.FixedPixels, Reserved, out var action, out var error), error);
        var key = Assert.IsType<CompiledAction.Key>(action);
        Assert.Equal(new[] { "Control", "Shift", "A" }, key.Keys.Select(k => k.LogicalKey));
        Assert.Equal(25, key.DelayMs);
    }
    [Theory]
    [InlineData("Key", "Unknown", "Unsupported")]
    [InlineData("Combo key", "Ctrl + Control + A", "Duplicate")]
    [InlineData("Combo key", "Ctrl +", "empty")]
    [InlineData("Combo key", "Ctrl + Shift", "nonmodifier")]
    [InlineData("Combo key", "A + Ctrl", "last")]
    [InlineData("Combo key", "A + B", "last")]
    [InlineData("Combo key", "Ctrl + F10", "reserved")]
    [InlineData("Click", "NaN, 0", "finite")]
    [InlineData("Wheel", "0", "nonzero")]
    [InlineData("Wheel", "32768", "16-bit")]
    [InlineData("Wait", "-1", "600000")]
    [InlineData("Wait", "600001", "600000")]
    public void RejectsInvalidValues(string kind, string value, string diagnostic)
    {
        Assert.False(ActionCompiler.TryCompile(new(kind, value, 0), CoordinateMode.FixedPixels, Reserved, out var action, out var error));
        Assert.Null(action); Assert.Contains(diagnostic, error, StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData(0)] [InlineData(600000)]
    public void AcceptsWaitBoundaries(int duration)
    {
        Assert.True(ActionCompiler.TryCompile(new("Wait", duration.ToString(), 0), CoordinateMode.FixedPixels, Reserved, out var action, out _));
        Assert.Equal(duration, Assert.IsType<CompiledAction.Wait>(action).DurationMs);
    }
    [Theory] [InlineData("0, 100", true)] [InlineData("100, 0", true)] [InlineData("101, 0", false)] [InlineData("-1, 0", false)]
    public void PercentageBoundsAppliedAtCompile(string value, bool valid)
    {
        Assert.Equal(valid, ActionCompiler.TryCompile(new("Click", value, 0), CoordinateMode.Percentage, Reserved, out _, out _));
    }
    [Fact] public void UnicodePreservedAndMalformedOrLongTextRejected()
    {
        Assert.True(ActionCompiler.TryCompile(new("Text", "零😀", 0), CoordinateMode.FixedPixels, Reserved, out var action, out _));
        Assert.Equal("零😀", Assert.IsType<CompiledAction.Text>(action).Content);
        foreach (var value in new[] { "\ud800", "\udc00", new string('x', 4097) })
        {
            Assert.False(ActionCompiler.TryCompile(new("Text", value, 0), CoordinateMode.FixedPixels, Reserved, out _, out var error)); Assert.NotEmpty(error);
        }
    }
    [Theory] [InlineData(-1)] [InlineData(600001)]
    public void RejectsWaitAfterOutsideBounds(int delay) => Assert.False(ActionCompiler.TryCompile(new("Wait", "0", delay), CoordinateMode.FixedPixels, Reserved, out _, out _));
    [Theory] [InlineData("return", "Enter")] [InlineData("esc", "Escape")] [InlineData("D7", "7")] [InlineData("RightCtrl + f24", "RightControl")]
    public void BackendAliasesCanonicalized(string value, string expected)
    {
        Assert.True(KeyParser.TryParse(value, out var keys, out var error), error); Assert.Equal(expected, keys[0].LogicalKey);
    }
}

