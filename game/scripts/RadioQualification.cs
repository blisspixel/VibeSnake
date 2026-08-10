using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record RadioBehaviorEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int ValidatedManifestStationCount,
    int ScenarioStationCount,
    int ScenarioTrackCount,
    bool CatalogDrivenByValidatedManifests,
    bool StationTrackMetadataComplete,
    bool PackMuteHelpStateComplete,
    bool ShuffleNoImmediateRepeat,
    bool SingleTrackEndBehaviorExplicit,
    bool StationSwitchComplete,
    bool PerStationResumeComplete,
    bool PauseResumeComplete,
    bool EndOfTrackAdvanceComplete,
    bool MissingTrackRecoveryComplete,
    bool MissingPackGraceful,
    bool RadioRandomSeparateFromGameplay,
    bool KeyboardCycleComplete,
    bool ControllerCycleComplete,
    bool DecoderAdapterPresent,
    bool PackagedInventoryAvailable,
    bool RulesStateUnchanged,
    string MissingPackHelp,
    IReadOnlyList<string> StationIds)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

internal static class RadioQualification
{
    public static RadioBehaviorEvidence Run(
        bool decoderAdapterPresent,
        bool packagedInventoryAvailable)
    {
        var rulesProbe = SnakeRun.Create(20260808UL);
        var rulesHashBefore = rulesProbe.ComputeStateHash();
        var validatedCatalog = ContentPackQualification.CreateValidatedRadioCatalog();
        var validatedStation = validatedCatalog.Stations.Single();
        var catalogDrivenByValidatedManifests =
            validatedStation.PackId == "vibesnake.radio.flow-signal"
            && validatedStation.StationId == "flow_signal"
            && validatedStation.StationName == "The Flow Signal"
            && validatedStation.Tracks.Count == 1
            && validatedStation.Tracks[0].TrackId
                == "asset:audio/radio/flow/track-01.mp3";

        var scenarioCatalog = CreateScenarioCatalog();
        var stationTrackMetadataComplete = scenarioCatalog.Stations.All(station =>
            !string.IsNullOrWhiteSpace(station.PackId)
            && !string.IsNullOrWhiteSpace(station.PackVersion)
            && !string.IsNullOrWhiteSpace(station.StationId)
            && !string.IsNullOrWhiteSpace(station.StationName)
            && station.Tracks.All(track =>
                track.PackId == station.PackId
                && track.StationId == station.StationId
                && track.MediaType == "audio/mpeg"
                && track.Bytes > 0
                && track.Sha256.Length == 64));

        var policy = new RadioPlaybackPolicy(
            scenarioCatalog,
            new RandomStreamBank(41UL).Radio);
        var first = policy.PlayOrResume();
        var noImmediateRepeat = true;
        var endOfTrackAdvance = false;
        for (var index = 0; index < 64; index++)
        {
            var previous = policy.Snapshot.TrackId;
            var next = policy.OnTrackEnded();
            noImmediateRepeat &= previous != next.TrackId;
            endOfTrackAdvance |= previous != next.TrackId;
        }

        var alphaBeforeSwitch = policy.Snapshot;
        var beta = policy.TuneNextStation();
        var alphaResumed = policy.TuneNextStation();
        var stationSwitchComplete = beta.StationId == "beta"
            && alphaResumed.StationId == "alpha";
        var perStationResumeComplete = alphaResumed.TrackId == alphaBeforeSwitch.TrackId;
        var paused = policy.Pause();
        var resumed = policy.PlayOrResume();
        var pauseResumeComplete = paused.Mode == RadioPlaybackMode.Paused
            && resumed.Mode == RadioPlaybackMode.Playing
            && paused.TrackId == resumed.TrackId;
        var muted = policy.SetMuted(true);
        var packMuteHelpStateComplete = muted.PackState == RadioPackState.Ready
            && muted.MuteLine == "MUTE: MUSIC MUTED"
            && !string.IsNullOrWhiteSpace(muted.StationLine)
            && !string.IsNullOrWhiteSpace(muted.TrackLine)
            && !string.IsNullOrWhiteSpace(muted.PackLine)
            && muted.HelpLine.Contains("never changes score", StringComparison.Ordinal);

        var failedTrack = policy.Snapshot.TrackId!;
        var recovered = policy.NoteTrackUnavailable(failedTrack);
        var missingTrackRecoveryComplete = recovered.Mode == RadioPlaybackMode.Playing
            && recovered.TrackId != failedTrack
            && recovered.PackState == RadioPackState.Degraded
            && recovered.StatusMessage.Contains("recovered", StringComparison.Ordinal);
        var missingPack = policy.ReplaceCatalog(RadioCatalog.Empty);
        var missingPackGraceful = missingPack.Mode == RadioPlaybackMode.NoStations
            && missingPack.PackState == RadioPackState.Missing
            && missingPack.HelpLine.Contains("core play remains available", StringComparison.Ordinal)
            && missingPack.StatusMessage.Contains("gameplay continues", StringComparison.Ordinal);

        var singlePolicy = new RadioPlaybackPolicy(
            new RadioCatalog([CreateStation("solo", 1)]),
            new RandomStreamBank(91UL).Radio);
        var soloFirst = singlePolicy.PlayOrResume();
        var soloEnded = singlePolicy.OnTrackEnded();
        var singleTrackEndBehaviorExplicit = soloFirst.TrackId == soloEnded.TrackId
            && soloEnded.StatusMessage.Contains("Single-track station restarted", StringComparison.Ordinal);

        var exercisedBank = new RandomStreamBank(20260808UL);
        var controlBank = new RandomStreamBank(20260808UL);
        var isolatedPolicy = new RadioPlaybackPolicy(scenarioCatalog, exercisedBank.Radio);
        isolatedPolicy.PlayOrResume();
        for (var index = 0; index < 32; index++)
        {
            isolatedPolicy.OnTrackEnded();
        }
        var radioRandomSeparateFromGameplay =
            exercisedBank.Gameplay.State == controlBank.Gameplay.State
            && exercisedBank.Radio.State != controlBank.Radio.State;
        var keyboardCycleComplete = GameActions.ActionHasKeyboardToken(
            GameActions.CycleRadio,
            "key:j");
        var controllerCycleComplete = GameActions.ActionHasControllerToken(
            GameActions.CycleRadio,
            "button:right_stick");
        var rulesStateUnchanged = rulesProbe.ComputeStateHash() == rulesHashBefore;
        var passed = catalogDrivenByValidatedManifests
            && stationTrackMetadataComplete
            && packMuteHelpStateComplete
            && noImmediateRepeat
            && singleTrackEndBehaviorExplicit
            && stationSwitchComplete
            && perStationResumeComplete
            && pauseResumeComplete
            && endOfTrackAdvance
            && missingTrackRecoveryComplete
            && missingPackGraceful
            && radioRandomSeparateFromGameplay
            && keyboardCycleComplete
            && controllerCycleComplete
            && decoderAdapterPresent
            && packagedInventoryAvailable
            && rulesStateUnchanged;
        if (!passed)
        {
            throw new InvalidOperationException("Radio behavior qualification failed.");
        }

        return new RadioBehaviorEvidence(
            SchemaVersion: 1,
            Kind: "radio-behavior-qualification-v1",
            Passed: true,
            ValidatedManifestStationCount: validatedCatalog.Stations.Count,
            ScenarioStationCount: scenarioCatalog.Stations.Count,
            ScenarioTrackCount: scenarioCatalog.Stations.Sum(station => station.Tracks.Count),
            CatalogDrivenByValidatedManifests: catalogDrivenByValidatedManifests,
            StationTrackMetadataComplete: stationTrackMetadataComplete,
            PackMuteHelpStateComplete: packMuteHelpStateComplete,
            ShuffleNoImmediateRepeat: noImmediateRepeat,
            SingleTrackEndBehaviorExplicit: singleTrackEndBehaviorExplicit,
            StationSwitchComplete: stationSwitchComplete,
            PerStationResumeComplete: perStationResumeComplete,
            PauseResumeComplete: pauseResumeComplete,
            EndOfTrackAdvanceComplete: endOfTrackAdvance,
            MissingTrackRecoveryComplete: missingTrackRecoveryComplete,
            MissingPackGraceful: missingPackGraceful,
            RadioRandomSeparateFromGameplay: radioRandomSeparateFromGameplay,
            KeyboardCycleComplete: keyboardCycleComplete,
            ControllerCycleComplete: controllerCycleComplete,
            DecoderAdapterPresent: decoderAdapterPresent,
            PackagedInventoryAvailable: packagedInventoryAvailable,
            RulesStateUnchanged: rulesStateUnchanged,
            MissingPackHelp: missingPack.HelpLine,
            StationIds: scenarioCatalog.Stations.Select(station => station.StationId).ToArray());
    }

    private static RadioCatalog CreateScenarioCatalog() =>
        new([CreateStation("alpha", 3), CreateStation("beta", 3)]);

    private static RadioStationMetadata CreateStation(string stationId, int trackCount)
    {
        var packId = "vibesnake.radio." + stationId;
        var tracks = Enumerable.Range(1, trackCount)
            .Select(index => new RadioTrackMetadata(
                packId,
                "1.0.0",
                stationId,
                stationId.ToUpperInvariant() + " STATION",
                $"asset:audio/radio/{stationId}/track-{index:00}.mp3",
                $"TRACK {index:00}",
                $"audio/radio/{stationId}/track-{index:00}.mp3",
                "audio/mpeg",
                1_000 + index,
                new string((char)('a' + index), 64)))
            .ToArray();
        return new RadioStationMetadata(
            packId,
            "1.0.0",
            stationId,
            stationId.ToUpperInvariant() + " STATION",
            tracks);
    }
}
