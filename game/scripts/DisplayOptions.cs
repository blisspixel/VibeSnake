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
}
