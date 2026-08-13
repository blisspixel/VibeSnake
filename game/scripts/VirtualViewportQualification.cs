using Godot;
using System.Text.Json;

namespace VibeSnake.Game;

internal readonly record struct VirtualViewportCaseResult(
    string Id,
    float RequestedWidth,
    float RequestedHeight,
    float EffectiveWidth,
    float EffectiveHeight,
    float Scale,
    float OffsetX,
    float OffsetY);

internal sealed record VirtualViewportMatrixEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    IReadOnlyList<VirtualViewportCaseResult> Cases)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

/// <summary>
/// Deterministic display matrix for aspect preservation, minimum-size clamping,
/// letterbox safe areas, high-density scaling, and pointer round trips.
/// </summary>
internal static class VirtualViewportQualification
{
    private const float Tolerance = 0.01f;

    private static readonly ViewportCaseDefinition[] Definitions =
    [
        new("minimum-clamp", 320.0f, 180.0f, 640.0f, 360.0f, 0.5f, 0.0f, 0.0f),
        new("hd-16-9", 1920.0f, 1080.0f, 1920.0f, 1080.0f, 1.5f, 0.0f, 0.0f),
        new("classic-4-3", 1024.0f, 768.0f, 1024.0f, 768.0f, 0.8f, 0.0f, 96.0f),
        new("desktop-16-10", 1920.0f, 1200.0f, 1920.0f, 1200.0f, 1.5f, 0.0f, 60.0f),
        new("ultrawide-21-9", 3440.0f, 1440.0f, 3440.0f, 1440.0f, 2.0f, 440.0f, 0.0f),
        new("square-1-1", 1024.0f, 1024.0f, 1024.0f, 1024.0f, 0.8f, 0.0f, 224.0f),
        new("high-density-4k", 3840.0f, 2160.0f, 3840.0f, 2160.0f, 3.0f, 0.0f, 0.0f),
        new("high-density-5k", 5120.0f, 2880.0f, 5120.0f, 2880.0f, 4.0f, 0.0f, 0.0f),
    ];

    public static VirtualViewportMatrixEvidence Run()
    {
        var results = new List<VirtualViewportCaseResult>(Definitions.Length);
        foreach (var definition in Definitions)
        {
            var viewport = new VirtualViewport(
                definition.RequestedWidth,
                definition.RequestedHeight);
            AssertClose(definition.Id, "effective width", viewport.WindowWidth, definition.EffectiveWidth);
            AssertClose(definition.Id, "effective height", viewport.WindowHeight, definition.EffectiveHeight);
            AssertClose(definition.Id, "scale", viewport.Scale, definition.Scale);
            AssertClose(definition.Id, "offset X", viewport.OffsetX, definition.OffsetX);
            AssertClose(definition.Id, "offset Y", viewport.OffsetY, definition.OffsetY);
            AssertDestination(definition.Id, viewport);
            AssertPointerRoundTrips(definition.Id, viewport);
            AssertLetterboxExclusion(definition.Id, viewport);

            results.Add(new VirtualViewportCaseResult(
                definition.Id,
                definition.RequestedWidth,
                definition.RequestedHeight,
                viewport.WindowWidth,
                viewport.WindowHeight,
                viewport.Scale,
                viewport.OffsetX,
                viewport.OffsetY));
        }

        return new VirtualViewportMatrixEvidence(
            SchemaVersion: 1,
            Kind: "virtual-viewport-matrix-v1",
            Passed: true,
            Cases: results);
    }

    private static void AssertDestination(string id, VirtualViewport viewport)
    {
        var destination = viewport.DestinationRect;
        AssertClose(id, "destination X", destination.Position.X, viewport.OffsetX);
        AssertClose(id, "destination Y", destination.Position.Y, viewport.OffsetY);
        AssertClose(
            id,
            "destination width",
            destination.Size.X,
            VirtualViewport.LogicalWidth * viewport.Scale);
        AssertClose(
            id,
            "destination height",
            destination.Size.Y,
            VirtualViewport.LogicalHeight * viewport.Scale);
        if (destination.Position.X < -Tolerance
            || destination.Position.Y < -Tolerance
            || destination.End.X > viewport.WindowWidth + Tolerance
            || destination.End.Y > viewport.WindowHeight + Tolerance)
        {
            throw new InvalidOperationException(id + " destination escaped the effective window.");
        }
    }

    private static void AssertPointerRoundTrips(string id, VirtualViewport viewport)
    {
        Vector2[] logicalPoints =
        [
            Vector2.Zero,
            new Vector2(640.0f, 360.0f),
            new Vector2(1279.5f, 719.5f),
        ];
        foreach (var logicalPoint in logicalPoints)
        {
            var roundTrip = viewport.WindowToLogical(viewport.LogicalToWindow(logicalPoint));
            AssertClose(id, "pointer X", roundTrip.X, logicalPoint.X);
            AssertClose(id, "pointer Y", roundTrip.Y, logicalPoint.Y);
            if (!VirtualViewport.ContainsLogicalPoint(roundTrip))
            {
                throw new InvalidOperationException(id + " rejected an in-bounds pointer round trip.");
            }
        }
    }

    private static void AssertLetterboxExclusion(string id, VirtualViewport viewport)
    {
        Vector2? windowPoint = viewport.OffsetX > Tolerance
            ? new Vector2(viewport.OffsetX - 1.0f, viewport.WindowHeight * 0.5f)
            : viewport.OffsetY > Tolerance
                ? new Vector2(viewport.WindowWidth * 0.5f, viewport.OffsetY - 1.0f)
                : null;
        if (windowPoint is { } point
            && VirtualViewport.ContainsLogicalPoint(viewport.WindowToLogical(point)))
        {
            throw new InvalidOperationException(id + " accepted a pointer in the letterbox safe area.");
        }
    }

    private static void AssertClose(string id, string field, float actual, float expected)
    {
        if (Math.Abs(actual - expected) > Tolerance)
        {
            throw new InvalidOperationException(
                $"{id} {field} was {actual} but expected {expected}.");
        }
    }

    private readonly record struct ViewportCaseDefinition(
        string Id,
        float RequestedWidth,
        float RequestedHeight,
        float EffectiveWidth,
        float EffectiveHeight,
        float Scale,
        float OffsetX,
        float OffsetY);
}
