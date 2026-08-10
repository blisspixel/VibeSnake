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
        string[] defaults =
        [
            "key:up",
            "key:down",
            "key:left",
            "key:right",
            "key:enter",
            "key:escape",
            "key:p",
            "key:f8",
            "key:space",
            "key:w",
            "key:a",
            "key:s",
            "key:d",
            "key:r",
            "key:q",
        ];

        Assert.All(defaults, token => Assert.True(InputBindingToken.IsKnownKeyboardDefaultToken(token)));
        Assert.False(InputBindingToken.IsKnownKeyboardDefaultToken("button:south"));
        Assert.False(InputBindingToken.IsKnownKeyboardDefaultToken("key:Enter"));
    }

    [Fact]
    public void Recognizes_supported_cross_platform_binding_vocabularies()
    {
        Assert.True(InputBindingToken.TryParse("key:f12", out var keyboard));
        Assert.True(InputBindingToken.IsSupportedKeyboardBinding(keyboard));

        Assert.True(InputBindingToken.TryParse("button:right_shoulder", out var button));
        Assert.True(InputBindingToken.IsSupportedControllerBinding(button));

        Assert.True(InputBindingToken.TryParse("axis:right_trigger:+1", out var axis));
        Assert.True(InputBindingToken.IsSupportedControllerBinding(axis));

        Assert.True(InputBindingToken.TryParse("button:unknown", out var unknown));
        Assert.False(InputBindingToken.IsSupportedControllerBinding(unknown));

        Assert.True(InputBindingToken.TryParse("key:f01", out var malformedFunctionKey));
        Assert.False(InputBindingToken.IsSupportedKeyboardBinding(malformedFunctionKey));

        Assert.True(InputBindingToken.TryParse("button:a", out var aliasedButton));
        Assert.Equal("button:south", InputBindingToken.GetConflictKey(aliasedButton));
    }

    [Fact]
    public void Supports_the_complete_documented_keyboard_vocabulary()
    {
        string[] namedIdentifiers =
        [
            "up", "down", "left", "right", "enter", "return", "escape", "esc", "space",
            "tab", "backspace", "delete", "home", "end", "insert", "minus", "equal",
            "comma", "period", "slash", "semicolon", "apostrophe",
        ];

        foreach (var identifier in namedIdentifiers.Concat(Enumerable.Range(1, 12).Select(number => $"f{number}")))
        {
            Assert.True(InputBindingToken.TryParse("key:" + identifier, out var binding));
            Assert.True(InputBindingToken.IsSupportedKeyboardBinding(binding), identifier);
        }

        foreach (var identifier in "abcdefghijklmnopqrstuvwxyz0123456789")
        {
            Assert.True(InputBindingToken.TryParse("key:" + identifier, out var binding));
            Assert.True(InputBindingToken.IsSupportedKeyboardBinding(binding), identifier.ToString());
        }

        foreach (var identifier in new[] { "f0", "f01", "f13", "page_up", "unknown" })
        {
            Assert.True(InputBindingToken.TryParse("key:" + identifier, out var binding));
            Assert.False(InputBindingToken.IsSupportedKeyboardBinding(binding), identifier);
        }
    }

    [Fact]
    public void Supports_the_complete_documented_controller_vocabulary_and_aliases()
    {
        string[] buttons =
        [
            "dpad_up", "dpad_down", "dpad_left", "dpad_right", "south", "east", "west",
            "north", "a", "b", "x", "y", "start", "select", "back", "guide", "left_stick",
            "right_stick", "left_shoulder", "right_shoulder", "misc1", "paddle1", "paddle2",
            "paddle3", "paddle4", "touchpad",
        ];
        string[] axes =
        [
            "left_x", "left_y", "right_x", "right_y", "left_trigger", "right_trigger",
        ];

        foreach (var identifier in buttons)
        {
            Assert.True(InputBindingToken.TryParse("button:" + identifier, out var binding));
            Assert.True(InputBindingToken.IsSupportedControllerBinding(binding), identifier);
        }

        foreach (var identifier in axes)
        {
            Assert.True(InputBindingToken.TryParse("axis:" + identifier + ":+1", out var binding));
            Assert.True(InputBindingToken.IsSupportedControllerBinding(binding), identifier);
        }

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["button:a"] = "button:south",
            ["button:b"] = "button:east",
            ["button:x"] = "button:west",
            ["button:y"] = "button:north",
            ["button:back"] = "button:select",
            ["key:return"] = "key:enter",
            ["key:esc"] = "key:escape",
        };
        foreach (var (token, expectedConflictKey) in aliases)
        {
            Assert.True(InputBindingToken.TryParse(token, out var binding));
            Assert.Equal(expectedConflictKey, InputBindingToken.GetConflictKey(binding));
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("key:abcdefghijklmnopqrstuvwxyz1234567")]
    [InlineData("key:has space")]
    [InlineData("key:slash/name")]
    [InlineData("axis:left_x:+1:extra")]
    [InlineData("axis:left_x:NaN")]
    [InlineData("axis:left_x:Infinity")]
    [InlineData("axis:left_x:-2")]
    public void Rejects_unsafe_or_non_finite_binding_tokens(string token)
    {
        Assert.False(InputBindingToken.TryParse(token, out _));
    }
}
