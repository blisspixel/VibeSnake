using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class BroadcastPolicyTests
{
    [Fact]
    public void Planned_station_catalog_has_complete_unique_identity_without_claiming_approval()
    {
        string[] expectedIds =
        [
            "flow_signal",
            "chaos_theory",
            "global_coil",
            "ourotron",
            "the_pit",
            "the_bureau",
            "the_strike",
            "underground_scales",
        ];
        var stations = BroadcastStationCatalog.All;
        var identities = StationIdentityCatalog.All;

        Assert.Equal(expectedIds, identities.Select(identity => identity.Id));
        Assert.Equal(expectedIds, stations.Select(station => station.StationId));
        Assert.Equal(
            identities.Select(identity => identity.DisplayName),
            stations.Select(station => station.StationName));
        Assert.Equal(8, stations.Select(station => station.StationName).Distinct().Count());
        Assert.Equal(8, stations.Select(station => station.HostName).Distinct().Count());
        Assert.Equal(8, stations.Select(station => station.VisualIdentity).Distinct().Count());
        Assert.All(stations, station =>
        {
            Assert.NotEmpty(station.MusicalInclusionRule);
            Assert.NotEmpty(station.HostPerspective);
            Assert.NotEmpty(station.CoilRelationship);
            Assert.Equal(3, station.ShortIds.Count);
            Assert.Equal(3, station.ShortIds.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(3, station.CaptionCopyIds.Count);
            Assert.Equal(
                3,
                station.CaptionCopyIds.Distinct(StringComparer.Ordinal).Count());
            Assert.All(
                station.CaptionCopyIds,
                id => Assert.StartsWith("broadcast.station.", id, StringComparison.Ordinal));
            Assert.Equal(4, station.TransitionStingers.Count);
            Assert.Equal(BroadcastStationApproval.PlannedUnapproved, station.Approval);
            Assert.False(station.SupportsAdaptiveLayers);
        });
        Assert.Null(BroadcastStationCatalog.Find("not-a-station"));
        Assert.Throws<ArgumentException>(() => BroadcastStationCatalog.Find(" "));
    }

    [Fact]
    public void Host_segments_are_limited_to_four_declared_boundaries()
    {
        var policy = Policy();
        var runStart = policy.Evaluate(Request(BroadcastBoundary.RunStart, 0));
        var ordinary = policy.Evaluate(Request(BroadcastBoundary.OrdinaryCombo, 1));
        var milestone = policy.Evaluate(Request(BroadcastBoundary.MajorMilestone, 100));
        var recovery = policy.Evaluate(Request(BroadcastBoundary.Recovery, 200));
        var postRun = policy.Evaluate(Request(BroadcastBoundary.PostRun, 300));

        Assert.Equal(BroadcastDecisionCode.SegmentGranted, runStart.Code);
        Assert.Equal(BroadcastDecisionCode.BoundaryNotAllowed, ordinary.Code);
        Assert.Equal(BroadcastDecisionCode.SegmentGranted, milestone.Code);
        Assert.Equal(BroadcastDecisionCode.SegmentGranted, recovery.Code);
        Assert.Equal(BroadcastDecisionCode.SegmentGranted, postRun.Code);
        Assert.True(ordinary.TrackContinues);
        Assert.Null(ordinary.SegmentId);
        Assert.Equal(4, policy.SegmentsUsed);
    }

    [Fact]
    public void Short_ids_use_a_no_repeat_bag_and_long_sessions_stop_at_the_fatigue_limit()
    {
        var policy = Policy();
        var selected = new List<string>();
        for (var index = 0; index < BroadcastPolicy.MaximumSegmentsPerRun; index++)
        {
            var decision = policy.Evaluate(Request(
                BroadcastBoundary.MajorMilestone,
                index * BroadcastPolicy.SegmentCooldownSteps));
            Assert.Equal(BroadcastDecisionCode.SegmentGranted, decision.Code);
            selected.Add(decision.SegmentId!);
            if (selected.Count > 1)
            {
                Assert.NotEqual(selected[^2], selected[^1]);
            }
        }

        Assert.Equal(3, selected.Take(3).Distinct(StringComparer.Ordinal).Count());
        var fatigued = policy.Evaluate(Request(
            BroadcastBoundary.PostRun,
            BroadcastPolicy.MaximumSegmentsPerRun * BroadcastPolicy.SegmentCooldownSteps));
        Assert.Equal(BroadcastDecisionCode.FatigueLimitReached, fatigued.Code);
        Assert.Equal(BroadcastPolicy.MaximumSegmentsPerRun, fatigued.SegmentsUsed);
        Assert.Contains("fatigue", fatigued.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Cooldown_and_critical_cues_prevent_optional_host_interruption()
    {
        var policy = Policy();
        policy.Evaluate(Request(BroadcastBoundary.RunStart, 0));
        var cooldown = policy.Evaluate(Request(BroadcastBoundary.Recovery, 99));
        var critical = policy.Evaluate(Request(
            BroadcastBoundary.MajorMilestone,
            100,
            critical: true));
        var warning = policy.Evaluate(Request(BroadcastBoundary.CriticalWarning, 101));

        Assert.Equal(BroadcastDecisionCode.CooldownActive, cooldown.Code);
        Assert.Equal(BroadcastDecisionCode.CriticalCueProtected, critical.Code);
        Assert.Equal(BroadcastDecisionCode.CriticalCueProtected, warning.Code);
        Assert.True(critical.OptionalBroadcastInterrupted);
        Assert.True(critical.TrackContinues);
        Assert.Equal(-9.0f, critical.MusicDuckDecibels);
        Assert.True(critical.CriticalCuePriority > critical.BroadcastPriority);
    }

    [Fact]
    public void Missing_or_unapproved_audio_retains_caption_and_never_requests_an_unsupported_layer()
    {
        var policy = Policy();
        var silent = policy.Evaluate(Request(BroadcastBoundary.RunStart, 0));

        Assert.Equal(BroadcastDecisionCode.SegmentGranted, silent.Code);
        Assert.False(silent.AudioRequested);
        Assert.False(silent.AdaptiveLayerRequested);
        Assert.StartsWith(
            "broadcast.station.flow-signal.id.",
            silent.CaptionCopyId,
            StringComparison.Ordinal);
        Assert.Contains("caption", silent.StatusMessage, StringComparison.OrdinalIgnoreCase);

        policy.ResetRun();
        var stillUnapproved = policy.Evaluate(Request(
            BroadcastBoundary.MajorMilestone,
            0,
            audio: true));
        Assert.False(stillUnapproved.AudioRequested);
        Assert.False(string.IsNullOrWhiteSpace(stillUnapproved.CaptionCopyId));
    }

    [Fact]
    public void Broadcast_rng_and_invalid_requests_cannot_affect_gameplay()
    {
        const ulong seed = 20260808UL;
        var exercised = new RandomStreamBank(seed);
        var control = new RandomStreamBank(seed);
        var policy = new BroadcastPolicy(exercised.Copy);
        var initialState = policy.RandomState;
        for (var index = 0; index < 3; index++)
        {
            policy.Evaluate(Request(
                BroadcastBoundary.MajorMilestone,
                index * BroadcastPolicy.SegmentCooldownSteps));
        }

        Assert.NotEqual(initialState, policy.RandomState);
        Assert.Equal(control.Gameplay.State, exercised.Gameplay.State);
        Assert.Throws<ArgumentNullException>(() => new BroadcastPolicy(null!));
        Assert.Throws<ArgumentNullException>(() => policy.Evaluate(null!));
        Assert.Throws<ArgumentException>(() => policy.Evaluate(
            new BroadcastRequest(" ", BroadcastBoundary.RunStart, 400, false, false)));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Evaluate(
            Request(BroadcastBoundary.PostRun, 10)));

        var missing = new BroadcastPolicy(new RandomStreamBank(seed).Copy).Evaluate(
            new BroadcastRequest("not-installed", BroadcastBoundary.RunStart, 0, false, false));
        Assert.Equal(BroadcastDecisionCode.StationUnknown, missing.Code);
        Assert.True(missing.TrackContinues);
    }

    private static BroadcastPolicy Policy() => new(new RandomStreamBank(42UL).Copy);

    private static BroadcastRequest Request(
        BroadcastBoundary boundary,
        int step,
        bool critical = false,
        bool audio = false) => new(
            "flow_signal",
            boundary,
            step,
            critical,
            audio);
}
