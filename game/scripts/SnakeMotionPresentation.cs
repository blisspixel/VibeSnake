using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

/// <summary>
/// Interpolates presentation-only snake positions between deterministic rules
/// steps. Rules, collision, input, replay hashes, and scoring remain grid based.
/// </summary>
internal sealed class SnakeMotionPresentation
{
    private GridPoint[] _previousBody = [];
    private GridPoint[] _currentBody = [];
    private ulong _startedAtMilliseconds;
    private int _durationMilliseconds = RunConfig.RulesTickMilliseconds;
    private bool _finalRedrawPending;

    public void Reset(IReadOnlyList<GridPoint> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _previousBody = body.ToArray();
        _currentBody = body.ToArray();
        _startedAtMilliseconds = 0UL;
        _durationMilliseconds = RunConfig.RulesTickMilliseconds;
        _finalRedrawPending = false;
    }

    public void Begin(
        IReadOnlyList<GridPoint> previousBody,
        IReadOnlyList<GridPoint> currentBody,
        ulong nowMilliseconds,
        int durationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(previousBody);
        ArgumentNullException.ThrowIfNull(currentBody);
        if (previousBody.Count == 0 || currentBody.Count == 0)
        {
            throw new ArgumentException("Snake presentation bodies must not be empty.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationMilliseconds);

        _previousBody = previousBody.ToArray();
        _currentBody = currentBody.ToArray();
        _startedAtMilliseconds = nowMilliseconds;
        _durationMilliseconds = durationMilliseconds;
        _finalRedrawPending = true;
    }

    public bool IsAnimating(ulong nowMilliseconds) =>
        _currentBody.Length > 0
        && nowMilliseconds >= _startedAtMilliseconds
        && nowMilliseconds - _startedAtMilliseconds < (ulong)_durationMilliseconds;

    public bool ConsumeRedrawRequest(ulong nowMilliseconds)
    {
        if (IsAnimating(nowMilliseconds))
        {
            return true;
        }

        // A paused replay or completed match has no later rules step to paint
        // the settled pose. Request its final frame exactly once.
        var pending = _finalRedrawPending;
        _finalRedrawPending = false;
        return pending;
    }

    public IReadOnlyList<Vector2> Resolve(
        IReadOnlyList<GridPoint> currentBody,
        ulong nowMilliseconds,
        int gridWidth,
        int gridHeight,
        bool reducedMotion = false)
    {
        ArgumentNullException.ThrowIfNull(currentBody);
        if (gridWidth <= 0 || gridHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gridWidth));
        }

        if (reducedMotion || !MatchesCurrentBody(currentBody) || _previousBody.Length == 0)
        {
            return currentBody.Select(ToVector).ToArray();
        }

        var progress = ResolveProgress(nowMilliseconds);
        var positions = new Vector2[currentBody.Count];
        var removedFromTail = Math.Max(0, _previousBody.Length - currentBody.Count);
        for (var index = 0; index < currentBody.Count; index++)
        {
            var previousIndex = currentBody.Count > _previousBody.Length
                ? Math.Min(index, _previousBody.Length - 1)
                : Math.Min(index + removedFromTail, _previousBody.Length - 1);
            var start = ToVector(_previousBody[previousIndex]);
            var end = ToVector(currentBody[index]);
            positions[index] = CrossesWrapBoundary(start, end, gridWidth, gridHeight)
                ? end
                : start.Lerp(end, progress);
        }

        return positions;
    }

    private bool MatchesCurrentBody(IReadOnlyList<GridPoint> body) =>
        body.Count == _currentBody.Length
        && body.Count > 0
        && body[^1] == _currentBody[^1]
        && body[0] == _currentBody[0];

    private float ResolveProgress(ulong nowMilliseconds)
    {
        if (nowMilliseconds <= _startedAtMilliseconds)
        {
            return 0.0f;
        }

        var elapsed = nowMilliseconds - _startedAtMilliseconds;
        return Math.Clamp(elapsed / (float)_durationMilliseconds, 0.0f, 1.0f);
    }

    private static bool CrossesWrapBoundary(
        Vector2 start,
        Vector2 end,
        int gridWidth,
        int gridHeight) =>
        Math.Abs(end.X - start.X) > gridWidth * 0.5f
        || Math.Abs(end.Y - start.Y) > gridHeight * 0.5f;

    private static Vector2 ToVector(GridPoint point) => new(point.X, point.Y);
}

internal static class SnakeMotionPresentationQualification
{
    public static void AssertContract()
    {
        var presentation = new SnakeMotionPresentation();
        GridPoint[] before = [new(1, 2), new(2, 2), new(3, 2)];
        GridPoint[] after = [new(2, 2), new(3, 2), new(4, 2)];
        presentation.Begin(before, after, 1_000UL, 50);
        var midpoint = presentation.Resolve(after, 1_025UL, 64, 33);
        if (midpoint.Count != 3
            || midpoint[0].DistanceTo(new Vector2(1.5f, 2.0f)) > 0.0001f
            || midpoint[2].DistanceTo(new Vector2(3.5f, 2.0f)) > 0.0001f
            || !presentation.IsAnimating(1_025UL)
            || presentation.IsAnimating(1_050UL))
        {
            throw new InvalidOperationException("Snake movement interpolation contract failed.");
        }

        var reduced = presentation.Resolve(after, 1_025UL, 64, 33, reducedMotion: true);
        if (reduced[0] != new Vector2(2.0f, 2.0f)
            || reduced[2] != new Vector2(4.0f, 2.0f)
            || !presentation.ConsumeRedrawRequest(1_025UL)
            || !presentation.ConsumeRedrawRequest(1_051UL)
            || presentation.ConsumeRedrawRequest(1_052UL))
        {
            throw new InvalidOperationException(
                "Reduced motion or the final settled snake-frame redraw contract failed.");
        }

        presentation.Begin(before, after, 1_100UL, 50);
        if (!presentation.ConsumeRedrawRequest(1_200UL)
            || presentation.Resolve(after, 1_200UL, 64, 33)[2] != new Vector2(4.0f, 2.0f)
            || presentation.ConsumeRedrawRequest(1_201UL))
        {
            throw new InvalidOperationException("A skipped animation did not paint its settled pose exactly once.");
        }
        presentation.Begin(before, after, 1_300UL, 50);
        presentation.Reset(after);
        if (presentation.ConsumeRedrawRequest(1_300UL))
        {
            throw new InvalidOperationException("Reset snake poses retained a stale final redraw request.");
        }

        GridPoint[] wrappedBefore = [new(63, 4)];
        GridPoint[] wrappedAfter = [new(0, 4)];
        presentation.Begin(wrappedBefore, wrappedAfter, 2_000UL, 50);
        var wrapped = presentation.Resolve(wrappedAfter, 2_025UL, 64, 33);
        if (wrapped[0].DistanceTo(new Vector2(0.0f, 4.0f)) > 0.0001f)
        {
            throw new InvalidOperationException("Snake wrap presentation crossed the full board.");
        }
    }
}
