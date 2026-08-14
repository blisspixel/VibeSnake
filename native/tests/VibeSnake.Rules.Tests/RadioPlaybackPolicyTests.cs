using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class RadioPlaybackPolicyTests
{
    [Fact]
    public void Catalog_projects_stable_station_and_track_metadata_from_manifests()
    {
        var catalog = RadioCatalog.FromValidatedManifests(
        [
            Manifest("zeta", "night_drive-01.mp3"),
            Manifest("ambient", "soft_signal.mp3", "second-track.mp3"),
        ]);

        Assert.Equal(["ambient", "zeta"], catalog.Stations.Select(station => station.StationId));
        var ambient = catalog.Stations[0];
        Assert.Equal("vibesnake.radio.ambient", ambient.PackId);
        Assert.Equal("1.2.3", ambient.PackVersion);
        Assert.Equal("Ambient Station", ambient.StationName);
        Assert.Equal(["SOFT SIGNAL", "SECOND TRACK"], ambient.Tracks.Select(track => track.DisplayTitle));
        Assert.All(ambient.Tracks, track =>
        {
            Assert.Equal("audio/mpeg", track.MediaType);
            Assert.Equal("ambient", track.StationId);
            Assert.StartsWith("asset:audio/radio/ambient/", track.TrackId, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Catalog_rejects_non_radio_duplicate_and_invalid_track_metadata()
    {
        var radio = Manifest("flow", "one.mp3");
        var core = radio with { Id = ContentPackManifest.CorePackId, Kind = ContentPackKind.Core, Radio = null };
        Assert.Throws<ArgumentException>(() => RadioCatalog.FromValidatedManifests([core]));
        Assert.Throws<ArgumentException>(() => RadioCatalog.FromValidatedManifests([radio, radio]));

        var duplicateStation = Manifest("flow", "two.mp3") with
        {
            Id = "vibesnake.radio.flow-copy",
        };
        Assert.Throws<ArgumentException>(() =>
            RadioCatalog.FromValidatedManifests([radio, duplicateStation]));

        var badFile = radio.Files[0] with { MediaType = "audio/wav" };
        Assert.Throws<ArgumentException>(() =>
            RadioCatalog.FromValidatedManifests([radio with { Files = [badFile] }]));
        Assert.Throws<ArgumentNullException>(() =>
            RadioCatalog.FromValidatedManifests(null!));
    }

    [Fact]
    public void Empty_catalog_is_explicit_and_core_game_help_remains_available()
    {
        var policy = new RadioPlaybackPolicy(
            RadioCatalog.Empty,
            new RandomStreamBank(1UL).Radio);

        var initial = policy.Snapshot;
        Assert.Equal(RadioPlaybackMode.NoStations, initial.Mode);
        Assert.Equal(RadioPackState.Missing, initial.PackState);
        Assert.Equal("STATION: NONE", initial.StationLine);
        Assert.Equal("TRACK: NONE", initial.TrackLine);
        Assert.Equal("PACK: MISSING OR UNAPPROVED", initial.PackLine);
        Assert.Contains("core play remains available", initial.HelpLine, StringComparison.Ordinal);
        Assert.Contains("NO PACK", initial.CompactLine, StringComparison.Ordinal);
        Assert.False(initial.IsAudible);

        var afterPlay = policy.PlayOrResume();
        Assert.Equal(RadioPlaybackMode.NoStations, afterPlay.Mode);
        Assert.Contains("No approved radio pack", afterPlay.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Shuffle_never_immediately_repeats_when_station_has_alternatives()
    {
        var policy = Policy(Manifest("flow", "one.mp3", "two.mp3", "three.mp3"));
        var current = policy.PlayOrResume();
        Assert.Equal(RadioPlaybackMode.Playing, current.Mode);
        Assert.True(current.IsAudible);

        for (var index = 0; index < 64; index++)
        {
            var previousTrack = current.TrackId;
            current = policy.OnTrackEnded();
            Assert.NotEqual(previousTrack, current.TrackId);
            Assert.Contains("without an immediate repeat", current.StatusMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Shuffle_bag_plays_every_available_track_before_refill()
    {
        var policy = Policy(Manifest("flow", "one.mp3", "two.mp3", "three.mp3", "four.mp3"));
        var current = policy.PlayOrResume();
        var firstCycle = new HashSet<string>(StringComparer.Ordinal) { current.TrackId! };
        for (var index = 1; index < 4; index++)
        {
            current = policy.OnTrackEnded();
            Assert.True(firstCycle.Add(current.TrackId!));
        }

        Assert.Equal(4, firstCycle.Count);
        var next = policy.OnTrackEnded();
        Assert.Contains(next.TrackId!, firstCycle);
        Assert.NotEqual(current.TrackId, next.TrackId);
    }

    [Fact]
    public void Single_track_station_restarts_deliberately_at_end()
    {
        var policy = Policy(Manifest("solo", "only.mp3"));
        var first = policy.PlayOrResume();
        var ended = policy.OnTrackEnded();

        Assert.Equal(first.TrackId, ended.TrackId);
        Assert.Equal(RadioPlaybackMode.Playing, ended.Mode);
        Assert.Contains("Single-track station restarted", ended.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Station_switch_resumes_each_stations_last_track()
    {
        var policy = Policy(
            Manifest("alpha", "a-one.mp3", "a-two.mp3"),
            Manifest("beta", "b-one.mp3", "b-two.mp3"));
        var alpha = policy.PlayOrResume();
        var beta = policy.TuneNextStation();
        var alphaAgain = policy.TuneNextStation();

        Assert.Equal("alpha", alpha.StationId);
        Assert.Equal("beta", beta.StationId);
        Assert.Equal("alpha", alphaAgain.StationId);
        Assert.Equal(alpha.TrackId, alphaAgain.TrackId);
        Assert.Contains("resum", alphaAgain.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(beta.TrackId, policy.TuneStation("beta").TrackId);

        var unchanged = policy.TuneStation("not-installed");
        Assert.Equal("beta", unchanged.StationId);
        Assert.Contains("not installed", unchanged.StatusMessage, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => policy.TuneStation(" "));
    }

    [Fact]
    public void Pause_resume_and_mute_are_orthogonal()
    {
        var policy = Policy(Manifest("flow", "one.mp3", "two.mp3"));
        var playing = policy.PlayOrResume();
        var paused = policy.Pause();
        var muted = policy.SetMuted(true);
        var resumed = policy.PlayOrResume();

        Assert.Equal(RadioPlaybackMode.Playing, playing.Mode);
        Assert.Equal(RadioPlaybackMode.Paused, paused.Mode);
        Assert.Equal(playing.TrackId, paused.TrackId);
        Assert.True(muted.Muted);
        Assert.Equal("MUTE: MUSIC MUTED", muted.MuteLine);
        Assert.Equal(RadioPlaybackMode.Playing, resumed.Mode);
        Assert.Equal(playing.TrackId, resumed.TrackId);
        Assert.False(resumed.IsAudible);
        Assert.True(policy.SetMuted(false).IsAudible);
    }

    [Fact]
    public void Missing_track_recovers_then_exposes_repair_help_when_station_is_exhausted()
    {
        var policy = Policy(Manifest("flow", "one.mp3", "two.mp3"));
        var playing = policy.PlayOrResume();
        var firstTrack = playing.TrackId!;
        var recovered = policy.NoteTrackUnavailable(firstTrack);

        Assert.Equal(RadioPlaybackMode.Playing, recovered.Mode);
        Assert.NotEqual(firstTrack, recovered.TrackId);
        Assert.Equal(RadioPackState.Degraded, recovered.PackState);
        Assert.Contains("recovered", recovered.StatusMessage, StringComparison.Ordinal);

        var exhausted = policy.NoteTrackUnavailable(recovered.TrackId!);
        Assert.Equal(RadioPlaybackMode.StationUnavailable, exhausted.Mode);
        Assert.Null(exhausted.TrackId);
        Assert.Contains("Repair or reinstall", exhausted.HelpLine, StringComparison.Ordinal);
        Assert.False(exhausted.IsAudible);

        var unknown = policy.NoteTrackUnavailable("asset:unknown");
        Assert.Contains("did not match", unknown.StatusMessage, StringComparison.Ordinal);

        var repaired = policy.ReplaceCatalog(RadioCatalog.FromValidatedManifests(
            [Manifest("flow", "one.mp3", "two.mp3")]));
        Assert.Equal(RadioPlaybackMode.Playing, repaired.Mode);
        Assert.NotNull(repaired.TrackId);
        Assert.Equal(2, repaired.PlayableTrackCount);
        Assert.NotEqual(RadioPlaybackMode.StationUnavailable, repaired.Mode);
    }

    [Fact]
    public void Explicit_retry_clears_isolated_tracks_and_resumes_playback()
    {
        var policy = Policy(Manifest("flow", "one.mp3"));
        policy.PlayOrResume();
        var exhausted = policy.NoteTrackUnavailable(policy.Snapshot.TrackId!);
        Assert.Equal(RadioPlaybackMode.StationUnavailable, exhausted.Mode);

        var retried = policy.RetryIsolatedTracks();
        Assert.Equal(RadioPlaybackMode.Playing, retried.Mode);
        Assert.NotNull(retried.TrackId);
        Assert.Equal(1, retried.PlayableTrackCount);
    }

    [Fact]
    public void Non_current_missing_track_is_isolated_without_interrupting_playback()
    {
        var manifest = Manifest("flow", "one.mp3", "two.mp3", "three.mp3");
        var policy = Policy(manifest);
        var playing = policy.PlayOrResume();
        var other = manifest.Radio!.TrackIds.First(trackId => trackId != playing.TrackId);
        var after = policy.NoteTrackUnavailable(other);

        Assert.Equal(playing.TrackId, after.TrackId);
        Assert.Equal(RadioPlaybackMode.Playing, after.Mode);
        Assert.Equal(2, after.PlayableTrackCount);
        Assert.Contains("isolated", after.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_removal_stops_radio_without_affecting_core_flow_and_refresh_recovers()
    {
        var manifest = Manifest("flow", "one.mp3", "two.mp3");
        var policy = Policy(manifest);
        policy.PlayOrResume();

        var removed = policy.ReplaceCatalog(RadioCatalog.Empty);
        Assert.Equal(RadioPlaybackMode.NoStations, removed.Mode);
        Assert.Null(removed.StationId);
        Assert.Contains("gameplay continues", removed.StatusMessage, StringComparison.Ordinal);

        var restored = policy.ReplaceCatalog(RadioCatalog.FromValidatedManifests([manifest]));
        Assert.Equal(RadioPlaybackMode.Playing, restored.Mode);
        Assert.Equal("flow", restored.StationId);
        Assert.NotNull(restored.TrackId);
        Assert.Contains("refreshed", restored.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Radio_random_draws_do_not_advance_gameplay_random_state()
    {
        const ulong seed = 20260808UL;
        var exercised = new RandomStreamBank(seed);
        var control = new RandomStreamBank(seed);
        var initialRadioState = exercised.Radio.State;
        var policy = new RadioPlaybackPolicy(
            RadioCatalog.FromValidatedManifests(
                [Manifest("flow", "one.mp3", "two.mp3", "three.mp3")]),
            exercised.Radio);
        policy.PlayOrResume();
        for (var index = 0; index < 100; index++)
        {
            policy.OnTrackEnded();
        }

        Assert.NotEqual(initialRadioState, policy.RandomState);
        Assert.Equal(control.Gameplay.State, exercised.Gameplay.State);
        for (var index = 0; index < 32; index++)
        {
            Assert.Equal(control.Gameplay.NextUInt(), exercised.Gameplay.NextUInt());
        }
    }

    [Fact]
    public void Policy_rejects_duplicate_catalog_identity_and_null_dependencies()
    {
        var station = RadioCatalog.FromValidatedManifests([Manifest("flow", "one.mp3")])
            .Stations[0];
        var duplicateCatalog = new RadioCatalog([station, station]);
        Assert.Throws<ArgumentException>(() =>
            new RadioPlaybackPolicy(duplicateCatalog, new Pcg32(1UL)));
        Assert.Throws<ArgumentNullException>(() =>
            new RadioPlaybackPolicy(null!, new Pcg32(1UL)));
        Assert.Throws<ArgumentNullException>(() =>
            new RadioPlaybackPolicy(RadioCatalog.Empty, null!));
        Assert.Throws<ArgumentNullException>(() =>
            Policy(Manifest("flow", "one.mp3")).ReplaceCatalog(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new RadioPlaybackPolicy(new RadioCatalog(null!), new Pcg32(1UL)));
    }

    private static RadioPlaybackPolicy Policy(params ContentPackManifest[] manifests) =>
        new(
            RadioCatalog.FromValidatedManifests(manifests),
            new RandomStreamBank(20260808UL).Radio);

    private static ContentPackManifest Manifest(string stationId, params string[] fileNames)
    {
        var packId = $"vibesnake.radio.{stationId}";
        var files = fileNames.Select((fileName, index) =>
        {
            var path = $"audio/radio/{stationId}/{fileName}";
            return new ContentPackFile(
                $"asset:{path}",
                path,
                "audio/mpeg",
                1_000 + index,
                new string((char)('a' + (index % 6)), 64),
                "radio-track",
                "optional",
                "radio-rights");
        }).ToArray();
        return new ContentPackManifest(
            ContentPackManifest.CurrentSchemaVersion,
            packId,
            "1.2.3",
            ContentPackKind.Radio,
            stationId + " Pack",
            "Radio test pack.",
            new ContentPackCompatibility(
                new ContentPackVersionRange("0.3.0", "1.0.0"),
                new ContentPackRulesetRange(SnakeRun.RulesetId, 4, 5)),
            new ContentPackInventoryBinding(1, "assets", new string('f', 64)),
            [new ContentPackDependency(ContentPackManifest.CorePackId, "0.3.0", "1.0.0")],
            files,
            [new ContentPackCredit("radio-rights", "test", "Apache-2.0", "test", "fixture")],
            new ContentPackRadio(
                stationId,
                char.ToUpperInvariant(stationId[0]) + stationId[1..] + " Station",
                files.Select(file => file.Id).ToArray()));
    }
}
