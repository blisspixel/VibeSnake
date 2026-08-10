using VibeSnake.Persistence;

namespace VibeSnake.Game;

internal static class CheckoutRadioCatalog
{
    private sealed record StationSource(
        string Id,
        string Name,
        IReadOnlyList<string> Prefixes);

    private static readonly StationSource[] Stations =
    [
        new("flow_signal", "The Flow Signal", ["flow_signal_", "ambient_", "chill_"]),
        new("chaos_theory", "Chaos Theory", ["chaos_theory_", "jazz_"]),
        new("global_coil", "The Global Coil", ["global_coil_", "world_", "soul_"]),
        new("ourotron", "Ourotron", ["ourotron_", "synthwave_"]),
        new("the_pit", "The Pit", ["the_pit_", "dance_"]),
        new("the_bureau", "The Bureau", ["the_bureau_"]),
        new("the_strike", "The Strike", ["the_strike_", "rock_"]),
        new("underground_scales", "Underground Scales", ["underground_scales_", "hiphop_"]),
    ];

    public static bool TryCreate(
        string radioDirectory,
        out RadioCatalog catalog,
        out IReadOnlyDictionary<string, string> sourcePaths)
    {
        catalog = RadioCatalog.Empty;
        sourcePaths = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(radioDirectory))
        {
            return false;
        }

        var root = Path.GetFullPath(radioDirectory);
        if (!Directory.Exists(root))
        {
            return false;
        }

        var files = Directory.EnumerateFiles(root, "*.mp3", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var resolvedPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var stations = new List<RadioStationMetadata>();
        foreach (var source in Stations)
        {
            var matching = files
                .Where(path => source.Prefixes.Any(prefix =>
                    Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (matching.Length == 0)
            {
                continue;
            }

            var packId = "vibesnake.checkout." + source.Id.Replace('_', '-');
            var tracks = new List<RadioTrackMetadata>(matching.Length);
            foreach (var path in matching)
            {
                var fileName = Path.GetFileName(path);
                var trackId = "checkout:audio/radio/" + fileName;
                resolvedPaths.Add(trackId, path);
                tracks.Add(new RadioTrackMetadata(
                    packId,
                    "checkout",
                    source.Id,
                    source.Name,
                    trackId,
                    Path.GetFileNameWithoutExtension(fileName)
                        .Replace('_', ' ')
                        .ToUpperInvariant(),
                    "audio/radio/" + fileName,
                    "audio/mpeg",
                    new FileInfo(path).Length,
                    new string('0', 64)));
            }

            stations.Add(new RadioStationMetadata(
                packId,
                "checkout",
                source.Id,
                source.Name,
                tracks.AsReadOnly()));
        }

        if (stations.Count == 0)
        {
            return false;
        }

        catalog = new RadioCatalog(stations.AsReadOnly());
        sourcePaths = resolvedPaths;
        return true;
    }
}
