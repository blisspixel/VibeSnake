using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record BroadcastQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int PlannedStationCount,
    int ApprovedStationCount,
    int AllowedBoundaryCount,
    int MaximumSegmentsPerRun,
    bool EveryStationIdentityComplete,
    bool ApprovalStateExplicit,
    bool RadioShuffleBagComplete,
    bool TrackCooldownComplete,
    bool ResumeStateRetained,
    bool HostBoundariesRestricted,
    bool OrdinaryComboKeepsTrackContinuous,
    bool EventAwareDuckingComplete,
    bool CriticalCueIntelligibilityProtected,
    bool MissingFilesRetainCaptions,
    bool LongSessionFatigueBounded,
    bool HostNoRepeatBagComplete,
    bool AdaptiveLayersRequireSupport,
    bool RadioRandomSeparateFromGameplay,
    bool RulesStateUnchanged,
    string AuthoredContentReviewStatus,
    IReadOnlyList<BroadcastStationIdentity> Stations,
    IReadOnlyList<string> AllowedBoundaries,
    IReadOnlyList<string> PendingHumanChecks)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

internal static class BroadcastQualification
{
    public static BroadcastQualificationEvidence Run()
    {
        const ulong seed = 20260808UL;
        var rulesProbe = SnakeRun.Create(seed);
        var rulesHashBefore = rulesProbe.ComputeStateHash();
        var stations = BroadcastStationCatalog.All;
        var everyStationIdentityComplete = stations.Count == 8
            && stations.Select(station => station.StationId).Distinct(StringComparer.Ordinal).Count() == 8
            && stations.Select(station => station.StationName).Distinct(StringComparer.Ordinal).Count() == 8
            && stations.Select(station => station.HostName).Distinct(StringComparer.Ordinal).Count() == 8
            && stations.Select(station => station.VisualIdentity).Distinct(StringComparer.Ordinal).Count() == 8
            && stations.All(station => !string.IsNullOrWhiteSpace(station.MusicalInclusionRule)
                && !string.IsNullOrWhiteSpace(station.HostPerspective)
                && !string.IsNullOrWhiteSpace(station.CoilRelationship)
                && station.ShortIds.Count == 3
                && station.ShortIds.Distinct(StringComparer.Ordinal).Count() == 3
                && station.CaptionCopyIds.Count == station.ShortIds.Count
                && station.CaptionCopyIds.Distinct(StringComparer.Ordinal).Count() == 3
                && station.TransitionStingers.Count == 4);
        var approvedStationCount = stations.Count(station =>
            station.Approval == BroadcastStationApproval.ApprovedForPack);
        var approvalStateExplicit = approvedStationCount == 0
            && stations.All(station => station.Approval
                == BroadcastStationApproval.PlannedUnapproved);

        var radioBank = new RandomStreamBank(seed);
        var radioControl = new RandomStreamBank(seed);
        var radio = new RadioPlaybackPolicy(CreateRadioCatalog(), radioBank.Radio);
        var current = radio.PlayOrResume();
        var firstTrack = current.TrackId;
        var firstCycle = new HashSet<string>(StringComparer.Ordinal) { current.TrackId! };
        for (var index = 1; index < 4; index++)
        {
            current = radio.OnTrackEnded();
            firstCycle.Add(current.TrackId!);
        }

        var radioShuffleBagComplete = firstCycle.Count == 4;
        var cycleEnd = current.TrackId;
        current = radio.OnTrackEnded();
        var trackCooldownComplete = current.TrackId != cycleEnd;
        var stationTrack = current.TrackId;
        var paused = radio.Pause();
        var resumed = radio.PlayOrResume();
        var resumeStateRetained = paused.TrackId == stationTrack
            && resumed.TrackId == stationTrack;

        var broadcast = new BroadcastPolicy(radioBank.Copy);
        BroadcastBoundary[] allowedBoundaries =
        [
            BroadcastBoundary.RunStart,
            BroadcastBoundary.MajorMilestone,
            BroadcastBoundary.Recovery,
            BroadcastBoundary.PostRun,
        ];
        var decisions = allowedBoundaries.Select((boundary, index) =>
            broadcast.Evaluate(new BroadcastRequest(
                "flow_signal",
                boundary,
                index * BroadcastPolicy.SegmentCooldownSteps,
                CriticalCueActive: false,
                AudioAvailable: false))).ToArray();
        var ordinaryTrackBefore = radio.Snapshot.TrackId;
        var ordinary = broadcast.Evaluate(new BroadcastRequest(
            "flow_signal",
            BroadcastBoundary.OrdinaryCombo,
            301,
            CriticalCueActive: false,
            AudioAvailable: false));
        var ordinaryTrackAfter = radio.Snapshot.TrackId;
        var hostBoundariesRestricted = decisions.All(decision =>
                decision.Code == BroadcastDecisionCode.SegmentGranted)
            && ordinary.Code == BroadcastDecisionCode.BoundaryNotAllowed;
        var ordinaryComboKeepsTrackContinuous = ordinary.TrackContinues
            && ordinaryTrackBefore == ordinaryTrackAfter
            && ordinary.SegmentId is null;
        var eventAwareDuckingComplete = decisions.Select(decision => decision.MusicDuckDecibels)
            .SequenceEqual(new[] { -3.0f, -3.0f, -6.0f, 0.0f });
        var critical = broadcast.Evaluate(new BroadcastRequest(
            "flow_signal",
            BroadcastBoundary.CriticalWarning,
            302,
            CriticalCueActive: true,
            AudioAvailable: false));
        var criticalCueIntelligibilityProtected =
            critical.Code == BroadcastDecisionCode.CriticalCueProtected
            && critical.OptionalBroadcastInterrupted
            && critical.CriticalCuePriority > critical.BroadcastPriority
            && critical.MusicDuckDecibels == -9.0f
            && critical.TrackContinues;
        var missingFilesRetainCaptions = decisions.All(decision =>
            !decision.AudioRequested
            && !string.IsNullOrWhiteSpace(decision.CaptionCopyId));

        broadcast.ResetRun();
        var longSessionSegments = new List<string>();
        for (var index = 0; index < BroadcastPolicy.MaximumSegmentsPerRun; index++)
        {
            for (var ordinaryIndex = 0; ordinaryIndex < 25; ordinaryIndex++)
            {
                broadcast.Evaluate(new BroadcastRequest(
                    "flow_signal",
                    BroadcastBoundary.OrdinaryCombo,
                    (index * BroadcastPolicy.SegmentCooldownSteps) + ordinaryIndex,
                    CriticalCueActive: false,
                    AudioAvailable: false));
            }

            var decision = broadcast.Evaluate(new BroadcastRequest(
                "flow_signal",
                BroadcastBoundary.MajorMilestone,
                (index + 1) * BroadcastPolicy.SegmentCooldownSteps,
                CriticalCueActive: false,
                AudioAvailable: false));
            longSessionSegments.Add(decision.SegmentId!);
        }

        var fatigue = broadcast.Evaluate(new BroadcastRequest(
            "flow_signal",
            BroadcastBoundary.PostRun,
            (BroadcastPolicy.MaximumSegmentsPerRun + 1)
                * BroadcastPolicy.SegmentCooldownSteps,
            CriticalCueActive: false,
            AudioAvailable: false));
        var longSessionFatigueBounded = longSessionSegments.Count
                == BroadcastPolicy.MaximumSegmentsPerRun
            && fatigue.Code == BroadcastDecisionCode.FatigueLimitReached
            && fatigue.SegmentsUsed == BroadcastPolicy.MaximumSegmentsPerRun;
        var hostNoRepeatBagComplete = longSessionSegments
            .Zip(longSessionSegments.Skip(1), (left, right) => left != right)
            .All(result => result)
            && longSessionSegments.Take(3).Distinct(StringComparer.Ordinal).Count() == 3;
        var adaptiveLayersRequireSupport = stations.All(station => !station.SupportsAdaptiveLayers)
            && decisions.All(decision => !decision.AdaptiveLayerRequested);
        var radioRandomSeparateFromGameplay = radioBank.Gameplay.State
            == radioControl.Gameplay.State;
        var rulesStateUnchanged = rulesProbe.ComputeStateHash() == rulesHashBefore;
        string[] pendingHumanChecks =
        [
            "Approve station inclusion, host voice, visual identity, and Coil relationship per shipped pack",
            "Approve authored station IDs, transition stingers, captions, rights, loudness, and metadata",
            "Run long-session repetition, interruption, fatigue, and critical-cue listening review",
            "Confirm each adaptive music layer has compatible authored stems before enabling it",
        ];
        var passed = everyStationIdentityComplete
            && approvalStateExplicit
            && radioShuffleBagComplete
            && trackCooldownComplete
            && resumeStateRetained
            && hostBoundariesRestricted
            && ordinaryComboKeepsTrackContinuous
            && eventAwareDuckingComplete
            && criticalCueIntelligibilityProtected
            && missingFilesRetainCaptions
            && longSessionFatigueBounded
            && hostNoRepeatBagComplete
            && adaptiveLayersRequireSupport
            && radioRandomSeparateFromGameplay
            && rulesStateUnchanged;
        if (!passed)
        {
            throw new InvalidOperationException("Broadcast qualification failed.");
        }

        return new BroadcastQualificationEvidence(
            SchemaVersion: 1,
            Kind: "broadcast-qualification-v1",
            Passed: true,
            PlannedStationCount: stations.Count,
            ApprovedStationCount: approvedStationCount,
            AllowedBoundaryCount: allowedBoundaries.Length,
            MaximumSegmentsPerRun: BroadcastPolicy.MaximumSegmentsPerRun,
            EveryStationIdentityComplete: everyStationIdentityComplete,
            ApprovalStateExplicit: approvalStateExplicit,
            RadioShuffleBagComplete: radioShuffleBagComplete,
            TrackCooldownComplete: trackCooldownComplete,
            ResumeStateRetained: resumeStateRetained,
            HostBoundariesRestricted: hostBoundariesRestricted,
            OrdinaryComboKeepsTrackContinuous: ordinaryComboKeepsTrackContinuous,
            EventAwareDuckingComplete: eventAwareDuckingComplete,
            CriticalCueIntelligibilityProtected: criticalCueIntelligibilityProtected,
            MissingFilesRetainCaptions: missingFilesRetainCaptions,
            LongSessionFatigueBounded: longSessionFatigueBounded,
            HostNoRepeatBagComplete: hostNoRepeatBagComplete,
            AdaptiveLayersRequireSupport: adaptiveLayersRequireSupport,
            RadioRandomSeparateFromGameplay: radioRandomSeparateFromGameplay,
            RulesStateUnchanged: rulesStateUnchanged,
            AuthoredContentReviewStatus: "pending-no-broadcast-audio-approved",
            Stations: stations,
            AllowedBoundaries: allowedBoundaries.Select(boundary => boundary.ToString()).ToArray(),
            PendingHumanChecks: pendingHumanChecks);
    }

    private static RadioCatalog CreateRadioCatalog()
    {
        var tracks = Enumerable.Range(1, 4).Select(index => new RadioTrackMetadata(
            PackId: "vibesnake.radio.flow_signal",
            PackVersion: "1.0.0",
            StationId: "flow_signal",
            StationName: "The Flow Signal",
            TrackId: $"flow-{index}",
            DisplayTitle: $"FLOW TRACK {index}",
            Path: $"audio/radio/flow_signal/flow-{index}.mp3",
            MediaType: "audio/mpeg",
            Bytes: 1_000 + index,
            Sha256: new string((char)('a' + index), 64))).ToArray();
        return new RadioCatalog(
        [
            new RadioStationMetadata(
                "vibesnake.radio.flow_signal",
                "1.0.0",
                "flow_signal",
                "The Flow Signal",
                tracks),
        ]);
    }
}
