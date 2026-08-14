using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public sealed record RadioTrackMetadata(
    string PackId,
    string PackVersion,
    string StationId,
    string StationName,
    string TrackId,
    string DisplayTitle,
    string Path,
    string MediaType,
    long Bytes,
    string Sha256);

public sealed record RadioStationMetadata(
    string PackId,
    string PackVersion,
    string StationId,
    string StationName,
    IReadOnlyList<RadioTrackMetadata> Tracks);

/// <summary>
/// Runtime radio catalog projected only from already validated radio manifests.
/// Track order is manifest order; station order is stable by station id.
/// </summary>
public sealed record RadioCatalog(IReadOnlyList<RadioStationMetadata> Stations)
{
    public const int MaximumStations = OptionalPackStore.MaximumInstalledPacks;
    public const int MaximumDisplayTitleCharacters = 96;

    public static RadioCatalog Empty { get; } = new(Array.Empty<RadioStationMetadata>());

    public static RadioCatalog FromValidatedManifests(
        IEnumerable<ContentPackManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        var source = manifests.ToArray();
        if (source.Length > MaximumStations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(manifests),
                $"Radio catalog exceeds {MaximumStations} stations.");
        }

        var stations = new List<RadioStationMetadata>(source.Length);
        var packIds = new HashSet<string>(StringComparer.Ordinal);
        var stationIds = new HashSet<string>(StringComparer.Ordinal);
        var globalTrackIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var manifest in source)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            if (manifest.Kind != ContentPackKind.Radio || manifest.Radio is null)
            {
                throw new ArgumentException(
                    "Radio catalogs accept only validated radio manifests.",
                    nameof(manifests));
            }

            var radio = manifest.Radio;
            if (manifest.Id != "vibesnake.radio." + radio.StationId.Replace('_', '-')
                || !packIds.Add(manifest.Id)
                || !stationIds.Add(radio.StationId))
            {
                throw new ArgumentException(
                    "Radio catalog contains an invalid or duplicate station identity.",
                    nameof(manifests));
            }

            var filesById = manifest.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
            var tracks = new List<RadioTrackMetadata>(radio.TrackIds.Count);
            foreach (var trackId in radio.TrackIds)
            {
                if (!globalTrackIds.Add(trackId)
                    || !filesById.TryGetValue(trackId, out var file)
                    || file.Role != "radio-track"
                    || file.MediaType != "audio/mpeg"
                    || file.RuntimeUse != "optional")
                {
                    throw new ArgumentException(
                        "Radio catalog contains invalid or duplicate track metadata.",
                        nameof(manifests));
                }

                tracks.Add(new RadioTrackMetadata(
                    manifest.Id,
                    manifest.Version,
                    radio.StationId,
                    radio.StationName,
                    trackId,
                    DisplayTitle(file.Path),
                    file.Path,
                    file.MediaType,
                    file.Bytes,
                    file.Sha256));
            }

            if (tracks.Count == 0)
            {
                throw new ArgumentException(
                    "Radio stations require at least one validated track.",
                    nameof(manifests));
            }

            stations.Add(new RadioStationMetadata(
                manifest.Id,
                manifest.Version,
                radio.StationId,
                radio.StationName,
                tracks.AsReadOnly()));
        }

        return new RadioCatalog(
            stations
                .OrderBy(station => station.StationId, StringComparer.Ordinal)
                .ToArray());
    }

    private static string DisplayTitle(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path)
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();
        if (string.IsNullOrWhiteSpace(stem))
        {
            return "UNTITLED TRACK";
        }

        return stem.Length <= MaximumDisplayTitleCharacters
            ? stem.ToUpperInvariant()
            : stem[..MaximumDisplayTitleCharacters].ToUpperInvariant();
    }
}

public enum RadioPlaybackMode : byte
{
    NoStations = 0,
    Stopped = 1,
    Playing = 2,
    Paused = 3,
    StationUnavailable = 4,
}

public enum RadioPackState : byte
{
    Missing = 0,
    Ready = 1,
    Degraded = 2,
}

public sealed record RadioPlaybackSnapshot(
    RadioPlaybackMode Mode,
    RadioPackState PackState,
    bool Muted,
    string? PackId,
    string? PackVersion,
    string? StationId,
    string? StationName,
    string? TrackId,
    string? TrackTitle,
    int StationCount,
    int PlayableTrackCount,
    string StatusMessage,
    string StationLine,
    string TrackLine,
    string PackLine,
    string MuteLine,
    string HelpLine)
{
    public bool IsAudible => Mode == RadioPlaybackMode.Playing && !Muted;

    public string CompactLine => StationId is null
        ? $"RADIO: NO PACK  |  {(Muted ? "MUTED" : "QUIET")}"
        : $"RADIO: {StationName}  |  {TrackTitle ?? "NO TRACK"}  |  {(Muted ? "MUTED" : Mode.ToString().ToUpperInvariant())}";
}

/// <summary>
/// Playback-free radio state machine. The caller owns decoding and reports a
/// missing/failed track back through <see cref="NoteTrackUnavailable"/>. Its
/// injected PCG stream must be the named radio stream, never gameplay state.
/// </summary>
public sealed class RadioPlaybackPolicy
{
    public const int MaximumUnavailableTracks = ContentPackManifest.MaximumFiles;

    private readonly Pcg32 _random;
    private readonly Dictionary<string, string> _resumeTrackByStation =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<string>> _shuffleBags =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _unavailableTrackIds = new(StringComparer.Ordinal);
    private RadioCatalog _catalog;
    private string? _stationId;
    private string? _trackId;
    private RadioPlaybackMode _mode;
    private bool _muted;
    private string _statusMessage;

    public RadioPlaybackPolicy(RadioCatalog catalog, Pcg32 radioRandom)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(radioRandom);
        ValidateCatalog(catalog);
        _catalog = catalog;
        _random = radioRandom;
        if (catalog.Stations.Count == 0)
        {
            _mode = RadioPlaybackMode.NoStations;
            _statusMessage = "No approved radio pack is installed.";
        }
        else
        {
            _stationId = catalog.Stations[0].StationId;
            _mode = RadioPlaybackMode.Stopped;
            _statusMessage = "Radio ready.";
        }
    }

    public RadioPlaybackSnapshot Snapshot => CreateSnapshot();

    public ulong RandomState => _random.State;

    public RadioPlaybackSnapshot SetMuted(bool muted)
    {
        _muted = muted;
        _statusMessage = muted ? "Radio audio muted." : "Radio audio available.";
        return Snapshot;
    }

    public RadioPlaybackSnapshot PlayOrResume()
    {
        if (_catalog.Stations.Count == 0)
        {
            _mode = RadioPlaybackMode.NoStations;
            _statusMessage = "No approved radio pack is installed.";
            return Snapshot;
        }

        var station = CurrentStation() ?? _catalog.Stations[0];
        _stationId = station.StationId;
        if (_trackId is null || !IsPlayable(station, _trackId))
        {
            _trackId = SelectTrack(station, allowResume: true, excludeTrackId: null);
        }

        if (_trackId is null)
        {
            _mode = RadioPlaybackMode.StationUnavailable;
            _statusMessage = "Station has no playable tracks.";
        }
        else
        {
            _mode = RadioPlaybackMode.Playing;
            _resumeTrackByStation[station.StationId] = _trackId;
            _statusMessage = "Radio playing.";
        }

        return Snapshot;
    }

    public RadioPlaybackSnapshot Pause()
    {
        if (_mode == RadioPlaybackMode.Playing)
        {
            _mode = RadioPlaybackMode.Paused;
            _statusMessage = "Radio paused; the decoder must preserve the exact track position.";
        }

        return Snapshot;
    }

    public RadioPlaybackSnapshot RetryIsolatedTracks()
    {
        _unavailableTrackIds.Clear();
        return PlayOrResume();
    }

    public RadioPlaybackSnapshot TuneNextStation()
    {
        if (_catalog.Stations.Count == 0)
        {
            return PlayOrResume();
        }

        var currentIndex = _catalog.Stations
            .Select((station, index) => (station, index))
            .FirstOrDefault(item => item.station.StationId == _stationId)
            .index;
        var nextIndex = (currentIndex + 1) % _catalog.Stations.Count;
        return TuneStation(_catalog.Stations[nextIndex].StationId);
    }

    public RadioPlaybackSnapshot TuneStation(string stationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        var station = _catalog.Stations.SingleOrDefault(item => item.StationId == stationId);
        if (station is null)
        {
            _statusMessage = "Requested station is not installed.";
            return Snapshot;
        }

        RememberCurrentTrack();
        _stationId = station.StationId;
        _trackId = SelectTrack(station, allowResume: true, excludeTrackId: null);
        if (_trackId is null)
        {
            _mode = RadioPlaybackMode.StationUnavailable;
            _statusMessage = "Station has no playable tracks.";
        }
        else
        {
            _mode = RadioPlaybackMode.Playing;
            _resumeTrackByStation[station.StationId] = _trackId;
            _statusMessage = "Station switched; the last station track resumes from its start.";
        }

        return Snapshot;
    }

    public RadioPlaybackSnapshot OnTrackEnded()
    {
        var station = CurrentStation();
        if (station is null)
        {
            return PlayOrResume();
        }

        var previous = _trackId;
        _trackId = SelectTrack(station, allowResume: false, excludeTrackId: previous);
        if (_trackId is null)
        {
            _mode = RadioPlaybackMode.StationUnavailable;
            _statusMessage = "Station ended with no playable replacement track.";
        }
        else
        {
            _mode = RadioPlaybackMode.Playing;
            _resumeTrackByStation[station.StationId] = _trackId;
            _statusMessage = station.Tracks.Count == 1
                ? "Single-track station restarted after end of track."
                : "End of track advanced through shuffle without an immediate repeat.";
        }

        return Snapshot;
    }

    public RadioPlaybackSnapshot NoteTrackUnavailable(string trackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        if (!_catalog.Stations.SelectMany(station => station.Tracks)
            .Any(track => track.TrackId == trackId))
        {
            _statusMessage = "Unavailable-track report did not match the radio catalog.";
            return Snapshot;
        }

        if (_unavailableTrackIds.Count >= MaximumUnavailableTracks
            && !_unavailableTrackIds.Contains(trackId))
        {
            throw new InvalidOperationException("Unavailable radio-track capacity was exceeded.");
        }

        _unavailableTrackIds.Add(trackId);
        RemoveFromShuffleBags(trackId);
        foreach (var remembered in _resumeTrackByStation
            .Where(pair => pair.Value == trackId)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _resumeTrackByStation.Remove(remembered);
        }

        if (_trackId != trackId)
        {
            _statusMessage = "Unavailable radio track was isolated.";
            return Snapshot;
        }

        var station = CurrentStation();
        _trackId = station is null
            ? null
            : SelectTrack(station, allowResume: false, excludeTrackId: trackId);
        if (_trackId is null)
        {
            _mode = station is null
                ? RadioPlaybackMode.NoStations
                : RadioPlaybackMode.StationUnavailable;
            _statusMessage = "Track failed and this station has no playable fallback.";
        }
        else
        {
            _mode = RadioPlaybackMode.Playing;
            _resumeTrackByStation[station!.StationId] = _trackId;
            _statusMessage = "Missing track skipped; radio recovered on the same station.";
        }

        return Snapshot;
    }

    public RadioPlaybackSnapshot ReplaceCatalog(RadioCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ValidateCatalog(catalog);
        RememberCurrentTrack();
        _catalog = catalog;
        _shuffleBags.Clear();
        // A catalog refresh is the repair/reinstall path. Clear isolation so a
        // replaced file with the same track id can play again this session.
        _unavailableTrackIds.Clear();

        if (catalog.Stations.Count == 0)
        {
            _stationId = null;
            _trackId = null;
            _mode = RadioPlaybackMode.NoStations;
            _statusMessage = "Radio pack became unavailable; gameplay continues without radio.";
            return Snapshot;
        }

        var station = catalog.Stations.SingleOrDefault(item => item.StationId == _stationId)
            ?? catalog.Stations[0];
        _stationId = station.StationId;
        _trackId = IsPlayable(station, _trackId)
            ? _trackId
            : SelectTrack(station, allowResume: true, excludeTrackId: null);
        _mode = _trackId is null
            ? RadioPlaybackMode.StationUnavailable
            : RadioPlaybackMode.Playing;
        _statusMessage = _trackId is null
            ? "Updated station has no playable tracks."
            : "Radio catalog refreshed from validated packs.";
        return Snapshot;
    }

    private RadioPlaybackSnapshot CreateSnapshot()
    {
        var station = CurrentStation();
        var track = station?.Tracks.SingleOrDefault(item => item.TrackId == _trackId);
        var playableTrackCount = station?.Tracks.Count(IsPlayable) ?? 0;
        var packState = station is null
            ? RadioPackState.Missing
            : playableTrackCount == station.Tracks.Count
                ? RadioPackState.Ready
                : RadioPackState.Degraded;
        var stationLine = station is null
            ? "STATION: NONE"
            : $"STATION: {station.StationName} [{station.StationId}]";
        var trackLine = track is null
            ? "TRACK: NONE"
            : $"TRACK: {track.DisplayTitle} [{track.TrackId}]";
        var packLine = station is null
            ? "PACK: MISSING OR UNAPPROVED"
            : $"PACK: {station.PackId} v{station.PackVersion} {packState.ToString().ToUpperInvariant()}";
        var helpLine = station is null
            ? "HELP: Install an approved radio pack from Content Packs; core play remains available."
            : _mode == RadioPlaybackMode.StationUnavailable
                ? "HELP: Repair or reinstall this station pack, or switch stations."
                : "HELP: Cycle stations with the radio action; radio never changes score or replays.";
        return new RadioPlaybackSnapshot(
            _mode,
            packState,
            _muted,
            station?.PackId,
            station?.PackVersion,
            station?.StationId,
            station?.StationName,
            track?.TrackId,
            track?.DisplayTitle,
            _catalog.Stations.Count,
            playableTrackCount,
            _statusMessage,
            stationLine,
            trackLine,
            packLine,
            _muted ? "MUTE: MUSIC MUTED" : "MUTE: MUSIC AUDIBLE",
            helpLine);
    }

    private RadioStationMetadata? CurrentStation() =>
        _catalog.Stations.SingleOrDefault(station => station.StationId == _stationId);

    private string? SelectTrack(
        RadioStationMetadata station,
        bool allowResume,
        string? excludeTrackId)
    {
        if (allowResume
            && _resumeTrackByStation.TryGetValue(station.StationId, out var resumed)
            && IsPlayable(station, resumed))
        {
            return resumed;
        }

        var playable = station.Tracks.Where(IsPlayable).ToArray();
        if (playable.Length == 0)
        {
            return null;
        }

        if (playable.Length == 1)
        {
            return playable[0].TrackId;
        }

        var playableIds = playable.Select(track => track.TrackId).ToHashSet(StringComparer.Ordinal);
        if (!_shuffleBags.TryGetValue(station.StationId, out var bag))
        {
            bag = new Queue<string>();
            _shuffleBags[station.StationId] = bag;
        }

        FilterBag(bag, playableIds);
        if (!bag.Any(trackId => trackId != excludeTrackId))
        {
            RefillBag(bag, playable.Select(track => track.TrackId).ToArray(), excludeTrackId);
        }

        while (bag.Count > 0)
        {
            var selected = bag.Dequeue();
            if (selected != excludeTrackId)
            {
                return selected;
            }

            bag.Enqueue(selected);
            if (!bag.Any(trackId => trackId != excludeTrackId))
            {
                break;
            }
        }

        return playable.First(track => track.TrackId != excludeTrackId).TrackId;
    }

    private void RefillBag(
        Queue<string> bag,
        IReadOnlyList<string> playableTrackIds,
        string? excludeTrackId)
    {
        var shuffled = playableTrackIds.ToArray();
        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            var swapIndex = _random.NextInt(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        if (shuffled.Length > 1
            && shuffled[0] == excludeTrackId)
        {
            var replacement = Array.FindIndex(shuffled, 1, item => item != excludeTrackId);
            (shuffled[0], shuffled[replacement]) = (shuffled[replacement], shuffled[0]);
        }

        bag.Clear();
        foreach (var trackId in shuffled)
        {
            bag.Enqueue(trackId);
        }
    }

    private static void FilterBag(Queue<string> bag, IReadOnlySet<string> playableTrackIds)
    {
        var retained = bag.Where(playableTrackIds.Contains).ToArray();
        bag.Clear();
        foreach (var trackId in retained)
        {
            bag.Enqueue(trackId);
        }
    }

    private void RemoveFromShuffleBags(string trackId)
    {
        foreach (var bag in _shuffleBags.Values)
        {
            var retained = bag.Where(item => item != trackId).ToArray();
            bag.Clear();
            foreach (var item in retained)
            {
                bag.Enqueue(item);
            }
        }
    }

    private bool IsPlayable(RadioStationMetadata station, string? trackId) =>
        trackId is not null
        && station.Tracks.Any(track => track.TrackId == trackId)
        && !_unavailableTrackIds.Contains(trackId);

    private bool IsPlayable(RadioTrackMetadata track) =>
        !_unavailableTrackIds.Contains(track.TrackId);

    private void RememberCurrentTrack()
    {
        if (_stationId is not null && _trackId is not null)
        {
            _resumeTrackByStation[_stationId] = _trackId;
        }
    }

    private static void ValidateCatalog(RadioCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog.Stations);
        if (catalog.Stations.Count > RadioCatalog.MaximumStations
            || catalog.Stations.Any(station => station is null
                || station.Tracks is null
                || station.Tracks.Count == 0))
        {
            throw new ArgumentException("Radio catalog is invalid.", nameof(catalog));
        }

        var stationIds = catalog.Stations.Select(station => station.StationId).ToArray();
        var trackIds = catalog.Stations.SelectMany(station => station.Tracks)
            .Select(track => track.TrackId)
            .ToArray();
        if (stationIds.Distinct(StringComparer.Ordinal).Count() != stationIds.Length
            || trackIds.Distinct(StringComparer.Ordinal).Count() != trackIds.Length)
        {
            throw new ArgumentException("Radio catalog identities must be unique.", nameof(catalog));
        }
    }
}
