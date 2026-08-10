using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class InputPromptGlyphsTests
{
    [Theory]
    [InlineData(null, InputPromptFamily.GenericController)]
    [InlineData("Xbox Wireless Controller", InputPromptFamily.Xbox)]
    [InlineData("Sony DualSense Wireless Controller", InputPromptFamily.PlayStation)]
    [InlineData("Nintendo Switch Pro Controller", InputPromptFamily.Nintendo)]
    [InlineData("8BitDo Arcade Stick", InputPromptFamily.GenericController)]
    public void Detects_controller_family_from_sanitized_device_name(
        string? deviceName,
        InputPromptFamily expected)
    {
        Assert.Equal(expected, InputPromptGlyphs.DetectControllerFamily(deviceName));
    }

    [Theory]
    [InlineData("button:south", InputPromptFamily.Xbox, "[A]")]
    [InlineData("button:south", InputPromptFamily.PlayStation, "[Cross]")]
    [InlineData("button:south", InputPromptFamily.Nintendo, "[B]")]
    [InlineData("button:east", InputPromptFamily.Nintendo, "[A]")]
    [InlineData("button:start", InputPromptFamily.PlayStation, "[Options]")]
    [InlineData("axis:left_y:-1", InputPromptFamily.Xbox, "[Left Stick Up]")]
    [InlineData("axis:right_trigger:+1", InputPromptFamily.GenericController, "[Right Trigger]")]
    [InlineData("key:enter", InputPromptFamily.Keyboard, "[Enter]")]
    public void Formats_stable_text_glyphs(
        string token,
        InputPromptFamily family,
        string expected)
    {
        Assert.Equal(expected, InputPromptGlyphs.FormatToken(token, family));
    }

    [Fact]
    public void Invalid_tokens_fail_closed_to_unbound_prompt()
    {
        Assert.Equal(
            "[Unbound]",
            InputPromptGlyphs.FormatToken("not-a-token", InputPromptFamily.Keyboard));
        Assert.Equal(
            new InputPromptGlyphDescriptor(false, "Unbound", InputPromptGlyphShape.Unbound),
            InputPromptGlyphs.DescribeToken("not-a-token", InputPromptFamily.Keyboard));
    }

    [Theory]
    [InlineData("key:enter", InputPromptGlyphShape.Keycap)]
    [InlineData("button:south", InputPromptGlyphShape.FaceButton)]
    [InlineData("button:left_shoulder", InputPromptGlyphShape.Shoulder)]
    [InlineData("button:dpad_up", InputPromptGlyphShape.DirectionalPad)]
    [InlineData("axis:left_x:-1", InputPromptGlyphShape.Stick)]
    [InlineData("axis:right_trigger:+1", InputPromptGlyphShape.Trigger)]
    [InlineData("button:start", InputPromptGlyphShape.SystemButton)]
    public void Describes_vector_badge_shape(
        string token,
        InputPromptGlyphShape expectedShape)
    {
        var descriptor = InputPromptGlyphs.DescribeToken(token, InputPromptFamily.Xbox);

        Assert.True(descriptor.IsBound);
        Assert.Equal(expectedShape, descriptor.Shape);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Label));
    }

    [Fact]
    public void Every_supported_controller_button_has_a_readable_label_and_shape()
    {
        string[] identifiers =
        [
            "dpad_up", "dpad_down", "dpad_left", "dpad_right", "south", "east", "west",
            "north", "a", "b", "x", "y", "start", "select", "back", "guide", "left_stick",
            "right_stick", "left_shoulder", "right_shoulder", "misc1", "paddle1", "paddle2",
            "paddle3", "paddle4", "touchpad",
        ];

        foreach (var family in Enum.GetValues<InputPromptFamily>())
        {
            foreach (var identifier in identifiers)
            {
                var descriptor = InputPromptGlyphs.DescribeToken("button:" + identifier, family);
                Assert.True(descriptor.IsBound, $"{family}:{identifier}");
                Assert.NotEqual(InputPromptGlyphShape.Unbound, descriptor.Shape);
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Label));
            }
        }
    }

    [Theory]
    [InlineData("button:a", InputPromptFamily.PlayStation, "Cross", InputPromptGlyphShape.FaceButton)]
    [InlineData("button:b", InputPromptFamily.Nintendo, "A", InputPromptGlyphShape.FaceButton)]
    [InlineData("button:x", InputPromptFamily.Xbox, "X", InputPromptGlyphShape.FaceButton)]
    [InlineData("button:y", InputPromptFamily.GenericController, "North", InputPromptGlyphShape.FaceButton)]
    [InlineData("button:back", InputPromptFamily.PlayStation, "Create", InputPromptGlyphShape.SystemButton)]
    [InlineData("button:paddle4", InputPromptFamily.Xbox, "Paddle 4", InputPromptGlyphShape.Shoulder)]
    [InlineData("button:unknown", InputPromptFamily.Xbox, "unknown", InputPromptGlyphShape.SystemButton)]
    public void Preserves_alias_fallback_and_shape_semantics(
        string token,
        InputPromptFamily family,
        string expectedLabel,
        InputPromptGlyphShape expectedShape)
    {
        var descriptor = InputPromptGlyphs.DescribeToken(token, family);

        Assert.Equal(expectedLabel, descriptor.Label);
        Assert.Equal(expectedShape, descriptor.Shape);
    }

    [Theory]
    [InlineData("key:up", "Up")]
    [InlineData("key:apostrophe", "'")]
    [InlineData("key:z", "Z")]
    [InlineData("axis:right_x:-1", "Right Stick Left")]
    [InlineData("axis:right_y:+1", "Right Stick Down")]
    [InlineData("axis:unknown:-1", "unknown -")]
    [InlineData("axis:unknown:+1", "unknown +")]
    public void Formats_named_and_fallback_tokens(string token, string expectedLabel)
    {
        Assert.Equal(expectedLabel, InputPromptGlyphs.DescribeToken(token, InputPromptFamily.Keyboard).Label);
    }
}
