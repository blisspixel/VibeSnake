using Godot;

namespace VibeSnake.Game;

/// <summary>
/// Production-owned bare-loop colors and geometry. Qualification reads these
/// same tokens so visibility evidence cannot drift from rendering.
/// </summary>
internal static class GameplayPresentation
{
    public static readonly Color HeadColor = new(0.72f, 1.0f, 0.82f);
    public static readonly Color BodyColor = new(0.22f, 0.88f, 0.47f);
    public static readonly Color FoodColor = new(1.0f, 0.20f, 0.35f);
    public static readonly Color DetachedObstacleFill = new(0.18f, 0.05f, 0.07f);

    public const float HeadInset = 1.0f;
    public const float BodyInset = 2.0f;
    public const float FoodInset = 4.0f;
    public const float DetachedObstacleInset = 2.0f;
    public const float DetachedObstacleOutlineWidth = 1.5f;
}
