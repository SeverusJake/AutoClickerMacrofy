using Macrofy.Core.Actions;
using Macrofy.Platform.Models;
using Xunit;

namespace Macrofy.Core.Tests;

public sealed class HoldActionCompilerTests
{
    private static readonly IReadOnlySet<string> Reserved = new HashSet<string> { "F9", "F8", "F10" };

    private static CompiledAction Compile(ActionDefinition definition)
    {
        Assert.True(ActionCompiler.TryCompile(definition, CoordinateMode.FixedPixels, Reserved, out var action, out var error), error);
        return action!;
    }

    [Fact]
    public void ClickCarriesButtonAndHold()
    {
        var click = Assert.IsType<CompiledAction.Click>(Compile(new("Click", "10, 20", 5, "Right", 250)));
        Assert.Equal(new CompiledAction.Click(new(10, 20), CoordinateMode.FixedPixels, 5, MouseButton.Right, 250), click);
        Assert.Equal(MouseButton.Left, Assert.IsType<CompiledAction.Click>(Compile(new("Click", "1, 2", 0))).Button);
    }

    [Fact]
    public void KeyCarriesHold()
    {
        var key = Assert.IsType<CompiledAction.Key>(Compile(new("Key", "Shift + W", 0, HoldMs: 1500)));
        Assert.Equal(1500, key.HoldMs);
        Assert.Equal(new[] { "Shift", "W" }, key.Keys.Select(k => k.LogicalKey));
    }

    [Fact]
    public void PressAndReleaseStepsCompile()
    {
        var down = Assert.IsType<CompiledAction.MouseDown>(Compile(new("Mouse down", "30, 40", 0, "Middle")));
        Assert.Equal((new PointerPoint(30, 40), MouseButton.Middle), (down.Point, down.Button));
        var up = Assert.IsType<CompiledAction.MouseUp>(Compile(new("Mouse up", "50, 60", 7)));
        Assert.Equal((new PointerPoint(50, 60), MouseButton.Left, 7), (up.Point, up.Button, up.DelayMs));
        Assert.Equal(new[] { "W" }, Assert.IsType<CompiledAction.KeyDown>(Compile(new("Key down", "W", 0))).Keys.Select(k => k.LogicalKey));
        Assert.Equal(new[] { "Control", "A" }, Assert.IsType<CompiledAction.KeyUp>(Compile(new("Key up", "Ctrl + A", 0))).Keys.Select(k => k.LogicalKey));
    }

    [Theory]
    [InlineData("Click", "1, 2", "Back", 0, "Button")]
    [InlineData("Mouse down", "1, 2", "", 0, "Button")]
    [InlineData("Click", "1, 2", "Left", 600001, "Hold")]
    [InlineData("Key", "W", "Left", -1, "Hold")]
    [InlineData("Key down", "F10", "Left", 0, "reserved")]
    [InlineData("Mouse up", "x", "Left", 0, "X, Y")]
    public void RejectsInvalidButtonHoldAndValues(string kind, string value, string button, int hold, string diagnostic)
    {
        Assert.False(ActionCompiler.TryCompile(new(kind, value, 0, button, hold), CoordinateMode.FixedPixels, Reserved, out _, out var error));
        Assert.Contains(diagnostic, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OtherKindsIgnoreButtonAndHold() =>
        Assert.IsType<CompiledAction.Text>(Compile(new("Text", "hi", 0, "Back", 999999)));
}
