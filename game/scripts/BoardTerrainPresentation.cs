using Godot;

namespace VibeSnake.Game;

internal enum BoardEnvironment
{
    Garden,
    Cliffs,
    Rainforest,
    Geothermal,
    Temple,
}

internal enum BoardTerrainElementKind
{
    Foliage,
    Bloom,
    Stone,
}

internal readonly record struct BoardTerrainElement(
    Vector2 Position,
    BoardTerrainElementKind Kind,
    float Size,
    int Variant);

internal sealed record BoardTerrainDefinition(
    BoardEnvironment Environment,
    Color Veil,
    Color Grid,
    Color Foliage,
    Color Accent,
    Color Stone,
    IReadOnlyList<BoardTerrainElement> Elements);

/// <summary>
/// Stable score-banded terrain inspired by the Python background renderer.
/// Generation is deterministic and contains no frame-time animation, so the
/// texture adds depth without shimmer or flashing.
/// </summary>
internal static class BoardTerrainCatalog
{
    private const int ElementCount = 72;
    private static readonly Dictionary<BoardEnvironment, BoardTerrainDefinition>
        Definitions = new Dictionary<BoardEnvironment, BoardTerrainDefinition>
        {
            [BoardEnvironment.Garden] = Definition(
                BoardEnvironment.Garden,
                0xA11CE001U,
                new Color(0.08f, 0.25f, 0.10f, 0.14f),
                new Color(0.22f, 0.55f, 0.24f, 0.10f),
                new Color(0.30f, 0.68f, 0.30f, 0.26f),
                new Color(0.95f, 0.68f, 0.30f, 0.32f),
                new Color(0.42f, 0.46f, 0.42f, 0.22f)),
            [BoardEnvironment.Cliffs] = Definition(
                BoardEnvironment.Cliffs,
                0xC11FF500U,
                new Color(0.28f, 0.18f, 0.10f, 0.12f),
                new Color(0.50f, 0.34f, 0.20f, 0.10f),
                new Color(0.48f, 0.38f, 0.20f, 0.24f),
                new Color(0.84f, 0.60f, 0.26f, 0.28f),
                new Color(0.55f, 0.42f, 0.30f, 0.28f)),
            [BoardEnvironment.Rainforest] = Definition(
                BoardEnvironment.Rainforest,
                0xBA1F0E57U,
                new Color(0.03f, 0.20f, 0.09f, 0.16f),
                new Color(0.12f, 0.46f, 0.20f, 0.10f),
                new Color(0.20f, 0.62f, 0.26f, 0.28f),
                new Color(0.92f, 0.30f, 0.46f, 0.30f),
                new Color(0.26f, 0.38f, 0.30f, 0.22f)),
            [BoardEnvironment.Geothermal] = Definition(
                BoardEnvironment.Geothermal,
                0x6E07E2A1U,
                new Color(0.20f, 0.08f, 0.24f, 0.14f),
                new Color(0.44f, 0.20f, 0.52f, 0.10f),
                new Color(0.50f, 0.24f, 0.54f, 0.22f),
                new Color(1.0f, 0.38f, 0.30f, 0.32f),
                new Color(0.42f, 0.30f, 0.48f, 0.26f)),
            [BoardEnvironment.Temple] = Definition(
                BoardEnvironment.Temple,
                0x7E1A1E00U,
                new Color(0.24f, 0.18f, 0.08f, 0.14f),
                new Color(0.58f, 0.46f, 0.20f, 0.10f),
                new Color(0.46f, 0.42f, 0.20f, 0.22f),
                new Color(0.92f, 0.78f, 0.36f, 0.32f),
                new Color(0.50f, 0.44f, 0.32f, 0.28f)),
        };

    public static BoardTerrainDefinition Resolve(int score)
    {
        var environment = score switch
        {
            >= 1_000 => BoardEnvironment.Temple,
            >= 600 => BoardEnvironment.Geothermal,
            >= 300 => BoardEnvironment.Rainforest,
            >= 100 => BoardEnvironment.Cliffs,
            _ => BoardEnvironment.Garden,
        };
        return Definitions[environment];
    }

    public static void AssertContract()
    {
        BoardEnvironment[] expected =
        [
            BoardEnvironment.Garden,
            BoardEnvironment.Cliffs,
            BoardEnvironment.Rainforest,
            BoardEnvironment.Geothermal,
            BoardEnvironment.Temple,
        ];
        int[] scores = [0, 100, 300, 600, 1_000];
        for (var index = 0; index < scores.Length; index++)
        {
            var definition = Resolve(scores[index]);
            if (definition.Environment != expected[index]
                || definition.Elements.Count != ElementCount
                || definition.Elements.Any(element =>
                    element.Position.X < 8.0f
                    || element.Position.X > 1_272.0f
                    || element.Position.Y < 68.0f
                    || element.Position.Y > 712.0f))
            {
                throw new InvalidOperationException("Board terrain contract failed.");
            }
        }
    }

    private static BoardTerrainDefinition Definition(
        BoardEnvironment environment,
        uint seed,
        Color veil,
        Color grid,
        Color foliage,
        Color accent,
        Color stone)
    {
        var elements = new BoardTerrainElement[ElementCount];
        var state = seed;
        for (var index = 0; index < elements.Length; index++)
        {
            state = Next(state);
            var x = 8.0f + (state % 1_265U);
            state = Next(state);
            var y = 68.0f + (state % 645U);
            state = Next(state);
            var kind = (BoardTerrainElementKind)(state % 3U);
            var size = 2.0f + ((state >> 8) % 5U);
            var variant = (int)((state >> 16) % 3U);
            elements[index] = new BoardTerrainElement(
                new Vector2(x, y),
                kind,
                size,
                variant);
        }

        return new BoardTerrainDefinition(
            environment,
            veil,
            grid,
            foliage,
            accent,
            stone,
            elements);
    }

    private static uint Next(uint value)
    {
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        return value;
    }
}
