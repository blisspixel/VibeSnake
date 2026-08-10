using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal readonly record struct MouseMenuTarget(
    string Id,
    int MenuIndex,
    Rect2 LogicalBounds);

internal static class MouseInputPolicy
{
    private const float MenuTargetWidth = 580.0f;
    private const float CosmeticTargetLeft = 46.0f;
    private const float CosmeticTargetTop = 208.0f;
    private const float CosmeticTargetWidth = 350.0f;
    private const float CosmeticTargetHeight = 96.0f;
    private const float CosmeticTargetStride = 104.0f;

    public static IReadOnlyList<MouseMenuTarget> MenuTargets { get; } =
        MenuTargetsForWidth(VirtualViewport.LogicalWidth);

    public static IReadOnlyList<MouseMenuTarget> MenuTargetsForWidth(float logicalWidth)
    {
        if (logicalWidth < MenuTargetWidth + 40.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        }

        var left = (logicalWidth - MenuTargetWidth) * 0.5f;
        return
        [
            Target("start", 0, left),
            Target("customize", 1, left),
            Target("achievements", 2, left),
            Target("scores", 3, left),
            Target("spectator", 4, left),
            Target("replays", 5, left),
            Target("settings", 6, left),
            Target("help", 7, left),
            Target("quit", 8, left),
        ];
    }

    public static int? ResolveMenuIndex(Vector2 logicalPoint)
        => ResolveMenuIndex(logicalPoint, VirtualViewport.LogicalWidth);

    public static int? ResolveMenuIndex(Vector2 logicalPoint, float logicalWidth)
    {
        var target = MenuTargetsForWidth(logicalWidth)
            .FirstOrDefault(item => item.LogicalBounds.HasPoint(logicalPoint));
        return string.IsNullOrEmpty(target.Id) ? null : target.MenuIndex;
    }

    public static int? ResolveCosmeticPageIndex(Vector2 logicalPoint)
    {
        for (var index = 0; index < 3; index++)
        {
            var bounds = new Rect2(
                CosmeticTargetLeft,
                CosmeticTargetTop + (index * CosmeticTargetStride),
                CosmeticTargetWidth,
                CosmeticTargetHeight);
            if (bounds.HasPoint(logicalPoint))
            {
                return index;
            }
        }

        return null;
    }

    public static string? ResolveGameplayDirectionAction(
        Vector2 logicalPoint,
        GridPoint head,
        float cellSize,
        float boardTop)
    {
        if (cellSize <= 0.0f || boardTop < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        var headCenter = new Vector2(
            (head.X * cellSize) + (cellSize * 0.5f),
            boardTop + (head.Y * cellSize) + (cellSize * 0.5f));
        var delta = logicalPoint - headCenter;
        if (Math.Abs(delta.X) < 0.001f && Math.Abs(delta.Y) < 0.001f)
        {
            return null;
        }

        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            return delta.X < 0.0f ? GameActions.MoveLeft : GameActions.MoveRight;
        }

        return delta.Y < 0.0f ? GameActions.MoveUp : GameActions.MoveDown;
    }

    private static MouseMenuTarget Target(
        string id,
        int menuIndex,
        float left)
    {
        var top = 238.0f + (menuIndex * 40.0f);
        return new(
            id,
            menuIndex,
            new Rect2(left, top, MenuTargetWidth, 35.0f));
    }
}

internal sealed record MouseInputQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string DeviceClass,
    int MenuTargetCount,
    bool MenuHitTestingComplete,
    bool LeftClickConfirmComplete,
    bool RightClickBackComplete,
    bool VerticalWheelNavigationComplete,
    bool HorizontalWheelNavigationComplete,
    bool GameplayDirectionComplete,
    bool WindowScalingApplied,
    bool LetterboxInputRejected,
    bool KeyboardBindingsUnchanged,
    bool ControllerBindingsUnchanged,
    IReadOnlyList<string> MenuTargets,
    IReadOnlyList<string> PendingHumanChecks)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}
