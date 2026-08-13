using Godot;
using VibeSnake.Persistence;

namespace VibeSnake.Game;

internal readonly record struct PromptBadgeMeasurement(float Width, float Height);

/// <summary>
/// Asset-free vector prompt badges. Every shape retains a readable text label,
/// so family artwork is never the only way an action is communicated.
/// </summary>
internal static class PromptBadgeRenderer
{
    private const float HorizontalPadding = 8.0f;
    private const float VerticalPadding = 4.0f;
    private const float OutlineWidth = 2.0f;

    public static PromptBadgeMeasurement Measure(
        Font font,
        InputPromptGlyphDescriptor glyph,
        int fontSize)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fontSize);

        var textSize = font.GetStringSize(
            glyph.Label,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize);
        var height = Math.Max(font.GetHeight(fontSize) + (VerticalPadding * 2.0f), fontSize + 8.0f);
        var width = Math.Max(
            textSize.X
                + (HorizontalPadding * 2.0f)
                + ContentLeadingInset(glyph.Shape),
            height);
        return new PromptBadgeMeasurement(width, height);
    }

    public static PromptBadgeMeasurement Draw(
        CanvasItem canvas,
        Font font,
        InputPromptGlyphDescriptor glyph,
        Vector2 baseline,
        int fontSize,
        ShellPalette palette)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var measurement = Measure(font, glyph, fontSize);
        var top = baseline.Y - font.GetAscent(fontSize) - VerticalPadding;
        var rect = new Rect2(baseline.X, top, measurement.Width, measurement.Height);
        var outline = glyph.IsBound ? palette.PromptOutline : palette.WarningText;

        DrawShape(canvas, rect, glyph.Shape, palette.PromptFill, outline);

        var textSize = font.GetStringSize(
            glyph.Label,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize);
        var leadingInset = ContentLeadingInset(glyph.Shape);
        var textBaseline = new Vector2(
            rect.Position.X
                + leadingInset
                + ((rect.Size.X - leadingInset - textSize.X) / 2.0f),
            rect.Position.Y
                + ((rect.Size.Y - font.GetHeight(fontSize)) / 2.0f)
                + font.GetAscent(fontSize));
        canvas.DrawString(
            font,
            textBaseline,
            glyph.Label,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize,
            palette.BodyText);
        return measurement;
    }

    private static void DrawShape(
        CanvasItem canvas,
        Rect2 rect,
        InputPromptGlyphShape shape,
        Color fill,
        Color outline)
    {
        switch (shape)
        {
            case InputPromptGlyphShape.FaceButton when rect.Size.X <= rect.Size.Y * 1.3f:
                var center = rect.GetCenter();
                var radius = rect.Size.Y / 2.0f;
                canvas.DrawCircle(center, radius, fill);
                canvas.DrawCircle(
                    center,
                    radius - (OutlineWidth / 2.0f),
                    outline,
                    filled: false,
                    width: OutlineWidth,
                    antialiased: true);
                break;
            case InputPromptGlyphShape.Stick:
                DrawOutlinedRect(canvas, rect, fill, outline);
                canvas.DrawCircle(
                    rect.Position + new Vector2(8.0f, rect.Size.Y / 2.0f),
                    Math.Max(2.0f, rect.Size.Y * 0.16f),
                    outline);
                break;
            case InputPromptGlyphShape.DirectionalPad:
                DrawOutlinedRect(canvas, rect, fill, outline);
                DrawDirectionalMark(canvas, rect, outline);
                break;
            case InputPromptGlyphShape.Trigger:
                DrawOutlinedRect(canvas, rect, fill, outline);
                canvas.DrawRect(
                    new Rect2(rect.Position + new Vector2(4.0f, 3.0f), new Vector2(rect.Size.X - 8.0f, 2.0f)),
                    outline);
                break;
            case InputPromptGlyphShape.Shoulder:
                DrawOutlinedRect(canvas, rect, fill, outline);
                canvas.DrawRect(
                    new Rect2(rect.Position + new Vector2(2.0f, 2.0f), new Vector2(rect.Size.X - 4.0f, 2.0f)),
                    outline);
                break;
            case InputPromptGlyphShape.SystemButton:
                DrawOutlinedRect(canvas, rect, fill, outline);
                canvas.DrawCircle(
                    rect.Position + new Vector2(6.0f, rect.Size.Y / 2.0f),
                    1.5f,
                    outline);
                break;
            case InputPromptGlyphShape.Unbound:
            case InputPromptGlyphShape.Keycap:
            case InputPromptGlyphShape.FaceButton:
            default:
                DrawOutlinedRect(canvas, rect, fill, outline);
                break;
        }
    }

    private static void DrawOutlinedRect(
        CanvasItem canvas,
        Rect2 rect,
        Color fill,
        Color outline)
    {
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, outline, filled: false, width: OutlineWidth, antialiased: true);
    }

    private static void DrawDirectionalMark(CanvasItem canvas, Rect2 rect, Color color)
    {
        var center = rect.Position + new Vector2(8.0f, rect.Size.Y / 2.0f);
        var extent = Math.Min(5.0f, rect.Size.Y * 0.18f);
        canvas.DrawRect(
            new Rect2(center - new Vector2(extent, 1.0f), new Vector2(extent * 2.0f, 2.0f)),
            color);
        canvas.DrawRect(
            new Rect2(center - new Vector2(1.0f, extent), new Vector2(2.0f, extent * 2.0f)),
            color);
    }

    private static float ContentLeadingInset(InputPromptGlyphShape shape) =>
        shape switch
        {
            InputPromptGlyphShape.DirectionalPad => 14.0f,
            InputPromptGlyphShape.Stick => 14.0f,
            InputPromptGlyphShape.SystemButton => 8.0f,
            _ => 0.0f,
        };
}
