namespace VibeSnake.Game;

/// <summary>
/// Keeps the pointer out of fullscreen play after a short idle period while
/// guaranteeing that focus loss, windowed mode, and fresh pointer activity
/// make it visible again.
/// </summary>
internal static class IdleCursorPolicy
{
    public const ulong HideDelayMilliseconds = 1_500UL;

    public static bool ShouldHide(
        bool fullscreen,
        bool applicationFocused,
        ulong nowMilliseconds,
        ulong lastActivityMilliseconds) =>
        fullscreen
        && applicationFocused
        && nowMilliseconds >= lastActivityMilliseconds
        && nowMilliseconds - lastActivityMilliseconds >= HideDelayMilliseconds;

    public static void AssertQualification()
    {
        if (ShouldHide(false, true, 2_000UL, 0UL)
            || ShouldHide(true, false, 2_000UL, 0UL)
            || ShouldHide(true, true, HideDelayMilliseconds - 1UL, 0UL)
            || !ShouldHide(true, true, HideDelayMilliseconds, 0UL)
            || !ShouldHide(true, true, 12_000UL, 10_000UL)
            || ShouldHide(true, true, 9_000UL, 10_000UL))
        {
            throw new InvalidOperationException("Fullscreen idle cursor policy is incomplete.");
        }
    }
}
