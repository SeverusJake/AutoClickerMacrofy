using Macrofy.Core.Actions;
using Xunit;

namespace Macrofy.Core.Tests;

public sealed class KeyComboCompilerTests
{
    private static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "F9", "F8", "F10" };

    [Theory]
    [InlineData("Key", "Shift", "Shift")]
    [InlineData("Key", "q", "Q")]
    [InlineData("Key down", "Ctrl", "Control")]
    [InlineData("Key up", "Space", "Space")]
    public void SingleKeyStepsTakeOneKeyIncludingALoneModifier(string kind, string value, string expected)
    {
        Assert.True(ActionCompiler.TryCompile(new(kind, value, 0), CoordinateMode.FixedPixels, Reserved, out var action, out var error), error);
        var keys = action switch { CompiledAction.Key k => k.Keys, CompiledAction.KeyDown d => d.Keys, CompiledAction.KeyUp u => u.Keys, _ => throw new InvalidOperationException() };
        Assert.Equal(expected, Assert.Single(keys).LogicalKey);
    }

    [Theory]
    [InlineData("Key")]
    [InlineData("Key down")]
    [InlineData("Key up")]
    public void SingleKeyStepsRejectCombinations(string kind)
    {
        Assert.False(ActionCompiler.TryCompile(new(kind, "Ctrl + C", 0), CoordinateMode.FixedPixels, Reserved, out _, out var error));
        Assert.Contains("Use Combo key", error);
    }

    [Fact]
    public void ComboKeyNeedsModifiersPlusOneKeyAndKeepsHold()
    {
        Assert.True(ActionCompiler.TryCompile(new("Combo key", "Ctrl + Shift + S", 5, HoldMs: 300), CoordinateMode.FixedPixels, Reserved, out var action, out var error), error);
        var key = Assert.IsType<CompiledAction.Key>(action);
        Assert.Equal(new[] { "Control", "Shift", "S" }, key.Keys.Select(k => k.LogicalKey));
        Assert.Equal((5, 300), (key.DelayMs, key.HoldMs));
        Assert.False(ActionCompiler.TryCompile(new("Combo key", "C", 0), CoordinateMode.FixedPixels, Reserved, out _, out error));
        Assert.Contains("modifier", error);
    }
}
