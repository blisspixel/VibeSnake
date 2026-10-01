using Godot;
using VibeSnake.Persistence;

namespace VibeSnake.Game;

internal readonly record struct WindowSizePresetDefinition(
    string Id,
    string Label,
    Vector2I Size);

internal static class DisplayOptions
{
    public static IReadOnlyList<WindowSizePresetDefinition> WindowSizes { get; } =
    [
        new(PreferencesDocument.ClassicWindowSize, "1024 x 768  (4:3 CLASSIC)", new Vector2I(1024, 768)),
        new(PreferencesDocument.HdWindowSize, "1280 x 720  (16:9 HD)", new Vector2I(1280, 720)),
        new(PreferencesDocument.DesktopWindowSize, "1440 x 900  (16:10)", new Vector2I(1440, 900)),
        new(PreferencesDocument.FullHdWindowSize, "1920 x 1080  (16:9 FULL HD)", new Vector2I(1920, 1080)),
    ];

    public static string WindowModeLabel(string mode) => mode switch
    {
        PreferencesDocument.WindowedMode => "WINDOWED",
        PreferencesDocument.BorderlessMode => "BORDERLESS FULLSCREEN",
        PreferencesDocument.ExclusiveFullscreenMode => "EXCLUSIVE FULLSCREEN",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static WindowSizePresetDefinition WindowSize(string id) =>
        WindowSizes.SingleOrDefault(item => item.Id == id) is { Id.Length: > 0 } result
            ? result
            : WindowSizes[0];

    public static Vector2I FitWindowToScreen(Vector2I requested, Vector2I screenSize)
    {
        if (requested.X <= 0 || requested.Y <= 0 || screenSize.X <= 0 || screenSize.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requested));
        }

        var maximumWidth = Math.Max(1, screenSize.X - (screenSize.X > 720 ? 80 : 0));
        var maximumHeight = Math.Max(1, screenSize.Y - (screenSize.Y > 440 ? 80 : 0));
        var scale = Math.Min(
            1.0f,
            Math.Min(maximumWidth / (float)requested.X, maximumHeight / (float)requested.Y));
        return new Vector2I(
            Math.Max(1, (int)MathF.Floor(requested.X * scale)),
            Math.Max(1, (int)MathF.Floor(requested.Y * scale)));
    }

    public static Rect2I ResolveUsableScreenBounds(Rect2I screenBounds, Rect2I reportedUsableBounds)
    {
        if (screenBounds.Size.X <= 0 || screenBounds.Size.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(screenBounds));
        }

        // Backends without work-area support can report an empty rectangle.
        // Bound valid reports to this monitor while preserving desktop offsets.
        var usable = reportedUsableBounds.Size.X > 0 && reportedUsableBounds.Size.Y > 0
            ? screenBounds.Intersection(reportedUsableBounds)
            : default;
        return usable.Size.X > 0 && usable.Size.Y > 0 ? usable : screenBounds;
    }

    public static Vector2I CenterWindow(Vector2I windowSize, Rect2I usableBounds)
    {
        if (windowSize.X <= 0 || windowSize.Y <= 0
            || usableBounds.Size.X <= 0 || usableBounds.Size.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize));
        }

        return usableBounds.Position + ((usableBounds.Size - windowSize) / 2);
    }

    public static void AssertWorkAreaContract()
    {
        Rect2I[] screens =
        [
            new(0, 0, 1920, 1080),
            new(-1920, -200, 1920, 1080),
            new(1920, 0, 1920, 1080),
        ];
        Rect2I[] workAreas =
        [
            new(0, 0, 1920, 1040),
            new(-1880, -160, 1880, 1000),
            new(1920, 48, 1920, 1032),
        ];
        for (var index = 0; index < screens.Length; index++)
        {
            var usable = ResolveUsableScreenBounds(screens[index], workAreas[index]);
            var fitted = FitWindowToScreen(new Vector2I(1920, 1080), usable.Size);
            var position = CenterWindow(fitted, usable);
            if (usable != workAreas[index]
                || !usable.Encloses(new Rect2I(position, fitted))
                || Math.Abs((position.X - usable.Position.X) * 2 - (usable.Size.X - fitted.X)) > 1
                || Math.Abs((position.Y - usable.Position.Y) * 2 - (usable.Size.Y - fitted.Y)) > 1)
            {
                throw new InvalidOperationException(
                    "Window fitting did not preserve the monitor's usable desktop area.");
            }
        }

        var primary = screens[0];
        if (ResolveUsableScreenBounds(primary, default) != primary
            || ResolveUsableScreenBounds(primary, new Rect2I(-5000, 0, 100, 100)) != primary
            || ResolveUsableScreenBounds(primary, new Rect2I(-40, 0, 2000, 1040))
                != workAreas[0])
        {
            throw new InvalidOperationException("Unsupported or oversized work-area reports were not bounded.");
        }
    }
}
