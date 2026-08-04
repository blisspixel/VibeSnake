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
}

internal static class ShellTransitions
{
    public static bool CanTransition(ShellScreen from, ShellScreen to) => (from, to) switch
    {
        (ShellScreen.Menu, ShellScreen.Running) => true,
        (ShellScreen.Menu, ShellScreen.Menu) => true,
        (ShellScreen.Running, ShellScreen.Paused) => true,
        (ShellScreen.Running, ShellScreen.Ended) => true,
        (ShellScreen.Running, ShellScreen.Menu) => true,
        (ShellScreen.Paused, ShellScreen.Running) => true,
        (ShellScreen.Paused, ShellScreen.Menu) => true,
        (ShellScreen.Ended, ShellScreen.Running) => true,
        (ShellScreen.Ended, ShellScreen.Menu) => true,
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
