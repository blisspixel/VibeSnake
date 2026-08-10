using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record SpectatorQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int RivalCount,
    int MeasuredPolicyClaimCount,
    int AuthoredCommentaryCount,
    int DistinctShedCount,
    int StationAffinityCount,
    int SeedClassCount,
    int SeedsPerClass,
    int PlaybackSpeedCount,
    int ExplanationLevelCount,
    int PredictionCount,
    bool WageringAllowed,
    int CurrencyAward,
    int HumanProgressionAwardCount,
    bool InitialRulesEqual,
    bool DeterministicMatchComplete,
    bool OverlayContractComplete,
    bool KeyboardRouteComplete,
    bool ControllerRouteComplete,
    bool ChannelSwitchRulesUnchanged,
    bool StallRecoveryComplete,
    bool InvalidChannelFallbackComplete,
    bool MissingCommentaryFallbackComplete,
    bool AudioFallbackComplete,
    bool PresentationFallbackRulesUnchanged,
    int ChallengeSchemaVersion,
    bool ChallengeEqualRules,
    bool ChallengeAiStateExcluded,
    int LeagueSchemaVersion,
    int StandingCount,
    int ChallengeRecordCount,
    bool RivalryRecordComplete,
    int MilestoneContractCount,
    bool LocalPersistenceRoundTrip,
    bool PlayerIdentityExcluded,
    string HumanReviewStatus,
    IReadOnlyList<string> PendingHumanChecks)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

internal static class SpectatorQualification
{
    public const int AuthoredCommentaryCount = 50;
    public const int MilestoneContractCount = 7;

    public static SpectatorQualificationEvidence Run(
        string userDataRoot,
        bool keyboardRouteComplete,
        bool controllerRouteComplete)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "Spectator qualification requires an absolute user-data root.",
                nameof(userDataRoot));
        }

        SpectatorRivalCatalog.Validate();
        var rivals = SpectatorRivalCatalog.All;
        var measuredPolicyClaimCount = AiPersonalityCatalog.BehaviorClaims
            .Select(item => item.PersonalityId)
            .Intersect(rivals.Select(item => item.PersonalityId), StringComparer.Ordinal)
            .Count();
        var authoredCommentaryCount = rivals
            .SelectMany(rival => Enum.GetValues<SpectatorCommentaryTrigger>()
                .Select(rival.CommentaryCopyId))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var distinctShedCount = rivals
            .Select(item => item.ShedId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        var selection = SpectatorSelection.CreateDefault() with
        {
            Prediction = SpectatorPredictionKind.ScoreAtLeast100,
        };
        selection.Validate();
        var left = new SpectatorMatchSession(selection);
        var right = new SpectatorMatchSession(selection);
        var initialRulesEqual = left.InitialRulesStateEqual && right.InitialRulesStateEqual;
        var viewedStateBeforeSwitches = left.ViewedSnapshot.StateHash;
        for (var index = 0; index < 8; index++)
        {
            left.SwitchViewedChannel();
        }
        var channelSwitchRulesUnchanged = left.ViewedSnapshot.StateHash
            == viewedStateBeforeSwitches;
        left.SetPaused(false);
        right.SetPaused(false);
        var overlayContractComplete = false;
        while (!left.IsComplete && !right.IsComplete)
        {
            var leftAdvance = left.Advance();
            var rightAdvance = right.Advance();
            if (!leftAdvance.RulesAdvanced || !rightAdvance.RulesAdvanced)
            {
                throw new InvalidOperationException(
                    "A running spectator session failed to advance both equal-rules lanes.");
            }

            var overlay = left.BuildOverlay(localRecordScore: 50, vibeLevelId: "grounded");
            overlayContractComplete |= overlay.PersonalityId == selection.PersonalityId
                && overlay.RivalPersonalityId == selection.RivalPersonalityId
                && overlay.DecisionReason is not null
                && overlay.DecisionReasonCopyId is not null
                && overlay.SurvivalResources.HungerMaximumTicks > 0
                && overlay.RecordDelta == left.ViewedSnapshot.Score - 50
                && overlay.OfficialLeagueQualified
                && overlay.PredictionsInformationalOnly;
        }

        if (!left.IsComplete || !right.IsComplete)
        {
            throw new InvalidOperationException(
                "Deterministic spectator sessions completed at different steps.");
        }

        var leftResult = left.BuildResult();
        var rightResult = right.BuildResult();
        var deterministicMatchComplete = leftResult == rightResult
            && left.StepCount == right.StepCount
            && leftResult.EqualRules
            && !leftResult.AiProgressionAwarded;

        var fallbackBaseline = new SpectatorMatchSession(selection);
        var fallbackSession = new SpectatorMatchSession(selection);
        fallbackBaseline.SetPaused(false);
        fallbackSession.SetPaused(false);
        var unavailableCommentary = rivals
            .SelectMany(rival => Enum.GetValues<SpectatorCommentaryTrigger>()
                .Select(rival.CommentaryCopyId))
            .ToHashSet(StringComparer.Ordinal);
        var missingCommentaryFallbackComplete = false;
        var audioFallbackComplete = false;
        var presentationFallbackRulesUnchanged = true;
        while (!fallbackBaseline.IsComplete && !fallbackSession.IsComplete)
        {
            _ = fallbackBaseline.Advance();
            var advance = fallbackSession.Advance(
                audioAvailable: false,
                unavailableCommentaryCopyIds: unavailableCommentary);
            missingCommentaryFallbackComplete |= advance.Recovery.HasFlag(
                SpectatorRecoveryKind.MissingCommentary);
            audioFallbackComplete |= advance.Recovery.HasFlag(
                SpectatorRecoveryKind.AudioUnavailable);
            presentationFallbackRulesUnchanged &= !advance.PresentationFallbackChangedRules;
        }
        presentationFallbackRulesUnchanged &= fallbackBaseline.BuildResult()
            == fallbackSession.BuildResult();
        missingCommentaryFallbackComplete &= fallbackSession.LatestCommentaryCopyId
            == SpectatorRivalCatalog.CommentaryFallbackCopyId;

        var invalidCustom = AiPersonalityCatalog.BuiltInProfiles[0] with
        {
            ContentKind = AiPersonalityContentKind.Custom,
            StatusLabel = AiPersonalityCatalog.CustomStatusLabel,
            OfficialLeagueQualified = false,
        };
        var channelResolution = SpectatorChannelResolver.Resolve(invalidCustom);
        var invalidChannelFallbackComplete = channelResolution.Profile.Personality.Id == "balanced"
            && channelResolution.Recovery == SpectatorRecoveryKind.InvalidCustomChannel;

        var stallTracker = new SpectatorStallTracker();
        var stalledDecision = new AiDecision(
            Direction.Up,
            AiTargetKind.Food,
            new GridPoint(4, 4),
            4,
            4,
            0,
            2,
            3,
            3,
            UsedChaos: false);
        stallTracker.Observe(stalledDecision);
        for (var index = 0; index < SpectatorMatchSession.StalledDecisionThreshold; index++)
        {
            stallTracker.Observe(stalledDecision);
        }
        var recoveryController = new AiPersonalityController(
            AiPersonalityCatalog.GetBuiltIn("balanced"),
            99UL);
        var recoveryDecision = recoveryController.SelectStallRecoveryDecision(
            SnakeRun.Create(selection.GameplaySeed, selection.CreateRunConfig()));
        var stallRecoveryComplete = stallTracker.ShouldRecover
            && recoveryDecision.RecoveredStalledTarget
            && recoveryDecision.Reason == AiDecisionReason.RecoverStalledTarget;

        var challenge = left.CreateChallenge();
        challenge.Validate();
        var humanRun = challenge.CreateHumanRun();
        var expectedHumanRun = SnakeRun.Create(
            selection.GameplaySeed,
            selection.CreateRunConfig());
        var challengeEqualRules = challenge.HumanRulesEqual
            && humanRun.ComputeStateHash() == expectedHumanRun.ComputeStateHash()
            && humanRun.ConfigHash == leftResult.ConfigHash;
        var challengeAiStateExcluded = !challenge.ContainsAiDecisionTrace
            && !challenge.ContainsAiRandomState
            && !challenge.AwardsAiProgression;
        for (var index = 0;
            index < SpectatorMatchSession.MaximumBroadcastSteps
                && humanRun.Status == RunStatus.Running;
            index++)
        {
            humanRun.Step();
        }
        if (humanRun.Status == RunStatus.Running)
        {
            throw new InvalidOperationException(
                "The human seed-challenge qualification did not reach a terminal state.");
        }

        var league = SpectatorLeagueDocument.CreateDefaults()
            .WithMatch(leftResult)
            .WithHumanChallenge(
                selection.PersonalityId,
                leftResult.Featured.Score,
                challenge,
                humanRun,
                ScoreRunContextCatalog.SeededChallenge);
        var rivalryRecordComplete = league.Rivalries.Count == 1
            && league.Rivalries[0].Matches == 1
            && league.StandingFor(selection.PersonalityId).Matches == 1;
        var challengeRecordComplete = league.Challenges.Single(item =>
                item.PersonalityId == selection.PersonalityId)
            .Attempts == 1;
        var serializedLeague = league.SerializeCanonical();
        var playerIdentityExcluded = !serializedLeague.Contains(
                "player",
                StringComparison.OrdinalIgnoreCase)
            && !serializedLeague.Contains(
                "progression",
                StringComparison.OrdinalIgnoreCase);
        var localPersistenceRoundTrip = PersistAndReload(
            userDataRoot,
            league,
            serializedLeague);

        string[] pendingHumanChecks =
        [
            "Exercise keyboard and Xbox, PlayStation, Nintendo, and generic controllers on Windows, macOS, and Linux",
            "Review spectator overlay readability, focus, and clean-capture behavior across supported display classes",
            "Editorially review every authored rival line in context and verify the unavailable-audio presentation",
            "Observe extended rivalry sessions and exact-seed challenges for clarity, pacing, and entertainment value",
        ];
        var passed = rivals.Count == 10
            && measuredPolicyClaimCount == 10
            && authoredCommentaryCount == AuthoredCommentaryCount
            && distinctShedCount == 10
            && rivals.Count(item => !string.IsNullOrWhiteSpace(item.StationAffinity)) == 10
            && Enum.GetValues<SpectatorSeedClass>().Length == 3
            && SpectatorSeedCatalog.SeedsPerClass == 4
            && SpectatorSelection.PlaybackSpeeds.Count == 4
            && Enum.GetValues<SpectatorExplanationLevel>().Length == 3
            && Enum.GetValues<SpectatorPredictionKind>().Length == 4
            && !SpectatorPredictionContract.WageringAllowed
            && SpectatorPredictionContract.CurrencyAward == 0
            && SpectatorPredictionContract.HumanProgressionAwardCount == 0
            && initialRulesEqual
            && deterministicMatchComplete
            && overlayContractComplete
            && keyboardRouteComplete
            && controllerRouteComplete
            && channelSwitchRulesUnchanged
            && stallRecoveryComplete
            && invalidChannelFallbackComplete
            && missingCommentaryFallbackComplete
            && audioFallbackComplete
            && presentationFallbackRulesUnchanged
            && challengeEqualRules
            && challengeAiStateExcluded
            && league.SchemaVersion == SpectatorLeagueDocument.CurrentSchemaVersion
            && league.Standings.Count == 10
            && league.Challenges.Count == 10
            && rivalryRecordComplete
            && challengeRecordComplete
            && SpectatorLeagueDocument.MaximumMilestonesPerPersonality
                == MilestoneContractCount
            && localPersistenceRoundTrip
            && playerIdentityExcluded;
        if (!passed)
        {
            throw new InvalidOperationException("Spectator experience qualification failed.");
        }

        return new SpectatorQualificationEvidence(
            SchemaVersion: 1,
            Kind: "spectator-experience-qualification-v1",
            Passed: true,
            RivalCount: rivals.Count,
            MeasuredPolicyClaimCount: measuredPolicyClaimCount,
            AuthoredCommentaryCount: authoredCommentaryCount,
            DistinctShedCount: distinctShedCount,
            StationAffinityCount: rivals.Count(item =>
                !string.IsNullOrWhiteSpace(item.StationAffinity)),
            SeedClassCount: Enum.GetValues<SpectatorSeedClass>().Length,
            SeedsPerClass: SpectatorSeedCatalog.SeedsPerClass,
            PlaybackSpeedCount: SpectatorSelection.PlaybackSpeeds.Count,
            ExplanationLevelCount: Enum.GetValues<SpectatorExplanationLevel>().Length,
            PredictionCount: Enum.GetValues<SpectatorPredictionKind>().Length,
            WageringAllowed: SpectatorPredictionContract.WageringAllowed,
            CurrencyAward: SpectatorPredictionContract.CurrencyAward,
            HumanProgressionAwardCount: SpectatorPredictionContract.HumanProgressionAwardCount,
            InitialRulesEqual: initialRulesEqual,
            DeterministicMatchComplete: deterministicMatchComplete,
            OverlayContractComplete: overlayContractComplete,
            KeyboardRouteComplete: keyboardRouteComplete,
            ControllerRouteComplete: controllerRouteComplete,
            ChannelSwitchRulesUnchanged: channelSwitchRulesUnchanged,
            StallRecoveryComplete: stallRecoveryComplete,
            InvalidChannelFallbackComplete: invalidChannelFallbackComplete,
            MissingCommentaryFallbackComplete: missingCommentaryFallbackComplete,
            AudioFallbackComplete: audioFallbackComplete,
            PresentationFallbackRulesUnchanged: presentationFallbackRulesUnchanged,
            ChallengeSchemaVersion: challenge.SchemaVersion,
            ChallengeEqualRules: challengeEqualRules,
            ChallengeAiStateExcluded: challengeAiStateExcluded,
            LeagueSchemaVersion: league.SchemaVersion,
            StandingCount: league.Standings.Count,
            ChallengeRecordCount: league.Challenges.Count,
            RivalryRecordComplete: rivalryRecordComplete && challengeRecordComplete,
            MilestoneContractCount: MilestoneContractCount,
            LocalPersistenceRoundTrip: localPersistenceRoundTrip,
            PlayerIdentityExcluded: playerIdentityExcluded,
            HumanReviewStatus: "pending-platform-and-content-review",
            PendingHumanChecks: pendingHumanChecks);
    }

    private static bool PersistAndReload(
        string userDataRoot,
        SpectatorLeagueDocument league,
        string expectedPayload)
    {
        var qualificationRoot = Path.Combine(
            userDataRoot,
            ".spectator-qualification-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SpectatorLeagueStore(qualificationRoot);
            store.Save(league);
            var loaded = store.Load();
            return loaded.IsSuccess
                && loaded.Document!.SerializeCanonical() == expectedPayload
                && !Directory.EnumerateFiles(qualificationRoot, "*.tmp-*").Any();
        }
        finally
        {
            if (Directory.Exists(qualificationRoot))
            {
                Directory.Delete(qualificationRoot, recursive: true);
            }
        }
    }
}
