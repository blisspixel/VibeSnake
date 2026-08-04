using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class InputBindingTokenTests
{
    [Theory]
    [InlineData("key:up", InputBindingKind.Key, "up", 0.0f)]
    [InlineData("key:Enter", InputBindingKind.Key, "enter", 0.0f)]
    [InlineData("button:south", InputBindingKind.Button, "south", 0.0f)]
    [InlineData("axis:left_x:+1", InputBindingKind.Axis, "left_x", 1.0f)]
    [InlineData("axis:left_y:-1", InputBindingKind.Axis, "left_y", -1.0f)]
    public void Parses_valid_tokens(
        string token,
        InputBindingKind kind,
        string identifier,
        float axisValue)
    {
        Assert.True(InputBindingToken.TryParse(token, out var binding));
        Assert.Equal(kind, binding.Kind);
        Assert.Equal(identifier, binding.Identifier);
        Assert.Equal(axisValue, binding.AxisValue, precision: 5);
    }

    [Theory]
    [InlineData("")]
    [InlineData("up")]
    [InlineData("key:")]
    [InlineData(":up")]
    [InlineData("key:up!")]
    [InlineData("axis:left_x")]
    [InlineData("axis:left_x:0")]
    [InlineData("axis:left_x:2")]
    [InlineData("mouse:left")]
    public void Rejects_invalid_tokens(string token)
    {
        Assert.False(InputBindingToken.TryParse(token, out _));
    }

    [Fact]
    public void Recognizes_keyboard_default_tokens()
    {
        Assert.True(InputBindingToken.IsKnownKeyboardDefaultToken("key:enter"));
        Assert.False(InputBindingToken.IsKnownKeyboardDefaultToken("button:south"));
    }
}
