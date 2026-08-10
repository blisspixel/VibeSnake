namespace VibeSnake.Game;

/// <summary>
/// Explicit allowed presentation-screen transitions for the Godot shell.
/// Rules state remains owned by <see cref="VibeSnake.Rules.SnakeRun"/>.
/// </summary>
internal enum ShellScreen : byte
{
    Menu = 0,
    Running = 1,
    Paused = 2,
    Ended = 3,
    Achievements = 4,
    Bindings = 5,
    ContentPacks = 6,
    Replays = 7,
    Settings = 8,
    Onboarding = 9,
    Scores = 10,
    Tour = 11,
    Cosmetics = 12,
    Spectator = 13,
    Lore = 14,
    Comparisons = 15,
}

internal static class ShellTransitions
{
    public static bool CanTransition(ShellScreen from, ShellScreen to) => (from, to) switch
    {
        (ShellScreen.Menu, ShellScreen.Running) => true,
        (ShellScreen.Menu, ShellScreen.Menu) => true,
        (ShellScreen.Menu, ShellScreen.Achievements) => true,
        (ShellScreen.Menu, ShellScreen.Bindings) => true,
        (ShellScreen.Menu, ShellScreen.ContentPacks) => true,
        (ShellScreen.Menu, ShellScreen.Replays) => true,
        (ShellScreen.Menu, ShellScreen.Settings) => true,
        (ShellScreen.Menu, ShellScreen.Onboarding) => true,
        (ShellScreen.Menu, ShellScreen.Scores) => true,
        (ShellScreen.Menu, ShellScreen.Tour) => true,
        (ShellScreen.Menu, ShellScreen.Cosmetics) => true,
        (ShellScreen.Menu, ShellScreen.Spectator) => true,
        (ShellScreen.Running, ShellScreen.Paused) => true,
        (ShellScreen.Running, ShellScreen.Ended) => true,
        (ShellScreen.Running, ShellScreen.Menu) => true,
        (ShellScreen.Paused, ShellScreen.Running) => true,
        (ShellScreen.Paused, ShellScreen.Menu) => true,
        (ShellScreen.Ended, ShellScreen.Running) => true,
        (ShellScreen.Ended, ShellScreen.Menu) => true,
        (ShellScreen.Ended, ShellScreen.Achievements) => true,
        (ShellScreen.Ended, ShellScreen.Bindings) => true,
        (ShellScreen.Ended, ShellScreen.ContentPacks) => true,
        (ShellScreen.Ended, ShellScreen.Replays) => true,
        (ShellScreen.Ended, ShellScreen.Settings) => true,
        (ShellScreen.Ended, ShellScreen.Onboarding) => true,
        (ShellScreen.Ended, ShellScreen.Scores) => true,
        (ShellScreen.Ended, ShellScreen.Tour) => true,
        (ShellScreen.Ended, ShellScreen.Spectator) => true,
        (ShellScreen.Ended, ShellScreen.Comparisons) => true,
        (ShellScreen.Achievements, ShellScreen.Menu) => true,
        (ShellScreen.Achievements, ShellScreen.Achievements) => true,
        (ShellScreen.Achievements, ShellScreen.Tour) => true,
        (ShellScreen.Achievements, ShellScreen.Cosmetics) => true,
        (ShellScreen.Bindings, ShellScreen.Menu) => true,
        (ShellScreen.Bindings, ShellScreen.Bindings) => true,
        (ShellScreen.ContentPacks, ShellScreen.Menu) => true,
        (ShellScreen.ContentPacks, ShellScreen.ContentPacks) => true,
        (ShellScreen.Replays, ShellScreen.Menu) => true,
        (ShellScreen.Replays, ShellScreen.Replays) => true,
        (ShellScreen.Replays, ShellScreen.Comparisons) => true,
        (ShellScreen.Settings, ShellScreen.Menu) => true,
        (ShellScreen.Settings, ShellScreen.Settings) => true,
        (ShellScreen.Settings, ShellScreen.Bindings) => true,
        (ShellScreen.Onboarding, ShellScreen.Onboarding) => true,
        (ShellScreen.Onboarding, ShellScreen.Menu) => true,
        (ShellScreen.Onboarding, ShellScreen.Running) => true,
        (ShellScreen.Onboarding, ShellScreen.Settings) => true,
        (ShellScreen.Scores, ShellScreen.Menu) => true,
        (ShellScreen.Scores, ShellScreen.Scores) => true,
        (ShellScreen.Tour, ShellScreen.Menu) => true,
        (ShellScreen.Tour, ShellScreen.Achievements) => true,
        (ShellScreen.Tour, ShellScreen.Running) => true,
        (ShellScreen.Tour, ShellScreen.Tour) => true,
        (ShellScreen.Cosmetics, ShellScreen.Menu) => true,
        (ShellScreen.Cosmetics, ShellScreen.Achievements) => true,
        (ShellScreen.Cosmetics, ShellScreen.Cosmetics) => true,
        (ShellScreen.Spectator, ShellScreen.Menu) => true,
        (ShellScreen.Spectator, ShellScreen.Spectator) => true,
        (ShellScreen.Spectator, ShellScreen.Running) => true,
        (ShellScreen.Spectator, ShellScreen.Lore) => true,
        (ShellScreen.Lore, ShellScreen.Menu) => true,
        (ShellScreen.Lore, ShellScreen.Spectator) => true,
        (ShellScreen.Lore, ShellScreen.Lore) => true,
        (ShellScreen.Comparisons, ShellScreen.Menu) => true,
        (ShellScreen.Comparisons, ShellScreen.Replays) => true,
        (ShellScreen.Comparisons, ShellScreen.Comparisons) => true,
        (ShellScreen.Comparisons, ShellScreen.Running) => true,
        _ => false,
    };

    public static void EnsureTransition(ShellScreen from, ShellScreen to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Illegal shell transition from {from} to {to}.");
        }
    }
}
