using Godot;

namespace VibeSnake.Game;

/// <summary>
/// Logical 1280x720 canvas mapping for windowed and letterboxed presentation.
/// Pointer coordinates are transformed back into canvas space without stretching
/// the gameplay grid.
/// </summary>
internal sealed class VirtualViewport
{
    public const float LogicalWidth = 1280.0f;
    public const float LogicalHeight = 720.0f;
    public const float MinimumWindowWidth = 640.0f;
    public const float MinimumWindowHeight = 360.0f;

    public VirtualViewport(float windowWidth, float windowHeight)
    {
        Resize(windowWidth, windowHeight);
    }

    public float WindowWidth { get; private set; }

    public float WindowHeight { get; private set; }

    public float Scale { get; private set; } = 1.0f;

    public float OffsetX { get; private set; }

    public float OffsetY { get; private set; }

    public Rect2 DestinationRect { get; private set; }

    public void Resize(float windowWidth, float windowHeight)
    {
        if (windowWidth <= 0.0f || windowHeight <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowWidth),
                "Window dimensions must be positive.");
        }

        WindowWidth = Math.Max(windowWidth, MinimumWindowWidth);
        WindowHeight = Math.Max(windowHeight, MinimumWindowHeight);

        var scaleX = WindowWidth / LogicalWidth;
        var scaleY = WindowHeight / LogicalHeight;
        Scale = Math.Min(scaleX, scaleY);
        var drawnWidth = LogicalWidth * Scale;
        var drawnHeight = LogicalHeight * Scale;
        OffsetX = (WindowWidth - drawnWidth) * 0.5f;
        OffsetY = (WindowHeight - drawnHeight) * 0.5f;
        DestinationRect = new Rect2(OffsetX, OffsetY, drawnWidth, drawnHeight);
    }

    public Vector2 WindowToLogical(Vector2 windowPoint)
    {
        if (Scale <= 0.0f)
        {
            return Vector2.Zero;
        }

        return new Vector2(
            (windowPoint.X - OffsetX) / Scale,
            (windowPoint.Y - OffsetY) / Scale);
    }

    public Vector2 LogicalToWindow(Vector2 logicalPoint) =>
        new(
            OffsetX + (logicalPoint.X * Scale),
            OffsetY + (logicalPoint.Y * Scale));

    public bool ContainsLogicalPoint(Vector2 logicalPoint) =>
        logicalPoint.X >= 0.0f
        && logicalPoint.Y >= 0.0f
        && logicalPoint.X < LogicalWidth
        && logicalPoint.Y < LogicalHeight;
}
