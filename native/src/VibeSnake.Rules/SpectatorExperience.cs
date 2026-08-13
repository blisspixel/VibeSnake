namespace VibeSnake.Rules;

public enum SpectatorSeedClass : byte
{
    ReviewedFixed = 0,
    Exploratory = 1,
    PreviousFailure = 2,
}

public enum SpectatorExplanationLevel : byte
{
    Hidden = 0,
    Concise = 1,
    Detailed = 2,
}

public enum SpectatorPredictionKind : byte
{
    None = 0,
    ScoreAtLeast100 = 1,
    ComboAtLeast5 = 2,
    SurviveAtLeast500Ticks = 3,
}

public enum SpectatorCommentaryTrigger : byte
{
    RunStart = 0,
    Food = 1,
    Power = 2,
    Pressure = 3,
    Terminal = 4,
}

[Flags]
public enum SpectatorRecoveryKind : byte
{
    None = 0,
    StalledTarget = 1,
    InvalidCustomChannel = 2,
    MissingCommentary = 4,
    AudioUnavailable = 8,
}

public sealed record SpectatorRivalDefinition(
    string PersonalityId,
    string BroadcastIdentity,
    string DeclaredBehavior,
    string StationAffinity,
    string ShedId,
    string RunStartCopyId,
    string FoodCopyId,
    string PowerCopyId,
    string PressureCopyId,
    string TerminalCopyId)
{
    public string CommentaryCopyId(SpectatorCommentaryTrigger trigger) => trigger switch
    {
        SpectatorCommentaryTrigger.RunStart => RunStartCopyId,
        SpectatorCommentaryTrigger.Food => FoodCopyId,
        SpectatorCommentaryTrigger.Power => PowerCopyId,
        SpectatorCommentaryTrigger.Pressure => PressureCopyId,
        SpectatorCommentaryTrigger.Terminal => TerminalCopyId,
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };
}

/// <summary>
/// Final world-bible identities bound to measured native personality policies.
/// Sheds are spectator presentation IDs and never alter hitboxes or rules.
/// </summary>
public static class SpectatorRivalCatalog
{
    public const string CommentaryFallbackCopyId = "spectator.commentary.fallback";

    public static IReadOnlyList<SpectatorRivalDefinition> All { get; } =
    [
        Rival("speed_demon", "Redline", "Converts open routes quickly and accepts narrow recovery windows", "The Pit", "rival-shed:redline-heat"),
        Rival("coward", "Shelter Coil", "Protects escape space, avoids crowded routes, and sacrifices score for survival", "The Flow Signal", "rival-shed:shelter-lattice"),
        Rival("greedy", "Crownchaser", "Protects combo value and accepts measured risk for record pace", "The Strike", "rival-shed:crown-gold"),
        Rival("power_hunter", "Mutagenist", "Replans aggressively around useful mutations and synergy state", "The Pit", "rival-shed:mutagen-prism"),
        Rival("drunk", "Noise Coil", "Uses bounded unpredictability while still respecting legal movement", "Chaos Theory", "rival-shed:noise-static"),
        Rival("optimal", "The Proof", "Chooses repeatable high-value routes with minimal expressive risk", "The Bureau", "rival-shed:proof-grid"),
        Rival("yolo", "Edge Prophet", "Seeks near misses, wraps, and high-pressure lines without hidden immunity", "Underground Scales", "rival-shed:edge-ember"),
        Rival("balanced", "Meanline", "Adapts between survival, food, and powers without a dominant preference", "The Global Coil", "rival-shed:meanline-spectrum"),
        Rival("wall_hugger", "Rimkeeper", "Treats boundaries and wraps as route infrastructure", "Ourotron", "rival-shed:rimkeeper-bronze"),
        Rival("zen_master", "Stillwater", "Preserves options, waits for clean openings, and avoids panic turns", "The Flow Signal", "rival-shed:stillwater-mist"),
    ];

    public static SpectatorRivalDefinition Get(string personalityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personalityId);
        return All.SingleOrDefault(item =>
                string.Equals(item.PersonalityId, personalityId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The spectator rival is unknown.", nameof(personalityId));
    }

    public static void Validate()
    {
        if (All.Count != AiPersonalityCatalog.BuiltIn.Count
            || All.Select(item => item.PersonalityId).Distinct(StringComparer.Ordinal).Count() != All.Count
            || All.Select(item => item.ShedId).Distinct(StringComparer.Ordinal).Count() != All.Count)
        {
            throw new InvalidOperationException("Spectator rivals require ten unique personality and shed IDs.");
        }

        foreach (var rival in All)
        {
            var personality = AiPersonalityCatalog.GetBuiltIn(rival.PersonalityId);
            var claim = AiPersonalityCatalog.BehaviorClaims.Single(item =>
                item.PersonalityId == rival.PersonalityId);
            if (personality.Name != rival.BroadcastIdentity
                || string.IsNullOrWhiteSpace(rival.StationAffinity)
                || string.IsNullOrWhiteSpace(rival.DeclaredBehavior)
                || string.IsNullOrWhiteSpace(claim.PlayerFacingMeaning)
                || Enum.GetValues<SpectatorCommentaryTrigger>()
                    .Select(rival.CommentaryCopyId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != Enum.GetValues<SpectatorCommentaryTrigger>().Length)
            {
                throw new InvalidOperationException(
                    "Every spectator rival requires measured truth, a station, a shed, and authored commentary.");
            }
        }
    }

    private static SpectatorRivalDefinition Rival(
        string personalityId,
        string identity,
        string behavior,
        string station,
        string shed)
    {
        var prefix = $"spectator.commentary.{personalityId.Replace('_', '-')}";
        return new SpectatorRivalDefinition(
            personalityId,
            identity,
            behavior,
            station,
            shed,
            $"{prefix}.run-start",
            $"{prefix}.food",
            $"{prefix}.power",
            $"{prefix}.pressure",
            $"{prefix}.terminal");
    }
}

public static class SpectatorSeedCatalog
{
    public const int SeedsPerClass = 4;

    private static readonly Dictionary<SpectatorSeedClass, IReadOnlyList<ulong>> Seeds =
        new Dictionary<SpectatorSeedClass, IReadOnlyList<ulong>>
        {
            [SpectatorSeedClass.ReviewedFixed] = [0UL, 1UL, 7UL, 42UL],
            [SpectatorSeedClass.Exploratory] = [20_260_808UL, 32_452_843UL, 49_979_687UL, 67_867_967UL],
            [SpectatorSeedClass.PreviousFailure] = [99UL, 255UL, 65_535UL, ulong.MaxValue],
        };

    public static IReadOnlyList<ulong> Get(SpectatorSeedClass seedClass)
    {
        if (!Seeds.TryGetValue(seedClass, out var seeds))
        {
            throw new ArgumentOutOfRangeException(nameof(seedClass));
        }

        return seeds;
    }

    public static ulong Get(SpectatorSeedClass seedClass, int slot)
    {
        var seeds = Get(seedClass);
        if (slot is < 0 or >= SeedsPerClass)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return seeds[slot];
    }
}

public static class SpectatorPredictionContract
{
    public const bool WageringAllowed = false;
    public const int CurrencyAward = 0;
    public const int HumanProgressionAwardCount = 0;

    public static bool? Evaluate(
        SpectatorPredictionKind prediction,
        SpectatorLaneOutcome outcome) => prediction switch
        {
            SpectatorPredictionKind.None => null,
            SpectatorPredictionKind.ScoreAtLeast100 => outcome.Score >= 100,
            SpectatorPredictionKind.ComboAtLeast5 => outcome.MaximumCombo >= 5,
            SpectatorPredictionKind.SurviveAtLeast500Ticks => outcome.FinalTick >= 500,
            _ => throw new ArgumentOutOfRangeException(nameof(prediction)),
        };
}

public sealed record SpectatorSelection(
    string PersonalityId,
    string RivalPersonalityId,
    string ModeId,
    int ModeVersion,
    SpectatorSeedClass SeedClass,
    int SeedSlot,
    int PlaybackSpeedIndex,
    SpectatorExplanationLevel ExplanationLevel,
    SpectatorPredictionKind Prediction)
{
    public static IReadOnlyList<double> PlaybackSpeeds { get; } = [0.5, 1.0, 2.0, 4.0];

    public static SpectatorSelection CreateDefault() => new(
        PersonalityId: "balanced",
        RivalPersonalityId: "speed_demon",
        ModeId: RunModeCatalog.VibeId,
        ModeVersion: RunModeCatalog.CurrentModeVersion,
        SeedClass: SpectatorSeedClass.ReviewedFixed,
        SeedSlot: 0,
        PlaybackSpeedIndex: 1,
        ExplanationLevel: SpectatorExplanationLevel.Concise,
        Prediction: SpectatorPredictionKind.None);

    public ulong GameplaySeed => SpectatorSeedCatalog.Get(SeedClass, SeedSlot);

    public double PlaybackSpeed => PlaybackSpeeds[PlaybackSpeedIndex];

    public RunModeDefinition Mode => RunModeCatalog.Get(ModeId, ModeVersion);

    public RunConfig CreateRunConfig() => RunModeCatalog.CreateConfig(Mode);

    public void Validate()
    {
        _ = SpectatorRivalCatalog.Get(PersonalityId);
        _ = SpectatorRivalCatalog.Get(RivalPersonalityId);
        _ = Mode;
        _ = GameplaySeed;
        if (PersonalityId == RivalPersonalityId)
        {
            throw new ArgumentException("A featured spectator rivalry requires two distinct personalities.");
        }

        if (PlaybackSpeedIndex < 0 || PlaybackSpeedIndex >= PlaybackSpeeds.Count
            || !Enum.IsDefined(ExplanationLevel)
            || !Enum.IsDefined(Prediction))
        {
            throw new ArgumentException("The spectator selection contains an unsupported option.");
        }
    }
}

public sealed record SpectatorChallengeDescriptor(
    int SchemaVersion,
    string Kind,
    ulong GameplaySeed,
    string RulesetId,
    int RulesVersion,
    string ModeId,
    int ModeVersion,
    string ScoreCategoryId,
    string ConfigHashAlgorithm,
    string ConfigHash,
    string RunKindId,
    string SeedCategoryId,
    bool HumanRulesEqual,
    bool ContainsAiDecisionTrace,
    bool ContainsAiRandomState,
    bool AwardsAiProgression)
{
    public const int CurrentSchemaVersion = 1;
    public const string KindId = "vibesnake-spectator-seed-challenge-v1";

    public SnakeRun CreateHumanRun()
    {
        Validate();
        var mode = RunModeCatalog.Get(ModeId, ModeVersion);
        var config = RunModeCatalog.CreateConfig(mode);
        if (config.ComputeConfigHash() != ConfigHash
            || RunModeCatalog.GetScoreCategoryId(config) != ScoreCategoryId)
        {
            throw new InvalidOperationException("The spectator challenge config identity diverged.");
        }

        return SnakeRun.Create(GameplaySeed, config);
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion
            || Kind != KindId
            || RulesetId != SnakeRun.RulesetId
            || RulesVersion != SnakeRun.RulesVersion
            || ConfigHashAlgorithm != RunConfig.ConfigHashAlgorithmId
            || RunKindId != ScoreRunContextCatalog.SeededChallengeRunKind
            || SeedCategoryId != ScoreRunContextCatalog.FixedChallengeSeedCategory
            || !HumanRulesEqual
            || ContainsAiDecisionTrace
            || ContainsAiRandomState
            || AwardsAiProgression)
        {
            throw new InvalidDataException("The spectator challenge identity is invalid.");
        }
    }
}

public sealed record SpectatorSurvivalResources(
    int HungerTicksRemaining,
    int HungerMaximumTicks,
    bool Shield,
    bool PhaseShift,
    bool LastStandHeld,
    bool LastStandRecovery,
    int ActiveResourceCount)
{
    public static SpectatorSurvivalResources FromSnapshot(RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var count = (snapshot.HasShield ? 1 : 0)
            + (snapshot.HasPhaseShift ? 1 : 0)
            + (snapshot.LastStandHeld ? 1 : 0)
            + (snapshot.HasLastStandRecovery ? 1 : 0);
        return new SpectatorSurvivalResources(
            snapshot.HungerTicksRemaining,
            snapshot.HungerMaximumTicks,
            snapshot.HasShield,
            snapshot.HasPhaseShift,
            snapshot.LastStandHeld,
            snapshot.HasLastStandRecovery,
            count);
    }
}

public sealed record SpectatorExperienceOverlay(
    string PersonalityId,
    string PersonalityName,
    string RivalPersonalityId,
    string RivalName,
    string StationAffinity,
    string ShedId,
    AiTargetKind TargetKind,
    GridPoint? Target,
    AiRiskBand RiskBand,
    SpectatorSurvivalResources SurvivalResources,
    string VibeLevelId,
    int RecordDelta,
    AiDecisionReason? DecisionReason,
    string? DecisionReasonCopyId,
    SpectatorExplanationLevel ExplanationLevel,
    string CommentaryCopyId,
    SpectatorRecoveryKind Recovery,
    bool OfficialLeagueQualified,
    bool PredictionsInformationalOnly);

public sealed record SpectatorLaneOutcome(
    string PersonalityId,
    int Score,
    int FinalTick,
    RunStatus Status,
    DeathCause DeathCause,
    int MaximumCombo,
    int FoodEaten,
    int PowerCollections,
    int CollisionRecoveries,
    string FinalStateHash,
    bool EndedByBroadcastLimit);

public sealed record SpectatorMatchResult(
    ulong GameplaySeed,
    string ModeId,
    int ModeVersion,
    string ConfigHash,
    SpectatorLaneOutcome Featured,
    SpectatorLaneOutcome Rival,
    SpectatorPredictionKind Prediction,
    bool? PredictionCorrect,
    bool EqualRules,
    bool AiProgressionAwarded);

public sealed record SpectatorAdvance(
    RunStepResult? FeaturedStep,
    RunStepResult? RivalStep,
    SpectatorRecoveryKind Recovery,
    string CommentaryCopyId,
    bool RulesAdvanced,
    bool PresentationFallbackChangedRules);

public sealed record SpectatorChannelResolution(
    AiPersonalityProfile Profile,
    SpectatorRecoveryKind Recovery);

public static class SpectatorChannelResolver
{
    public static SpectatorChannelResolution Resolve(AiPersonalityProfile? requested)
    {
        try
        {
            if (requested is not null)
            {
                requested.Personality.Validate();
                if (requested.ContentKind == AiPersonalityContentKind.BuiltIn
                    && requested.OfficialLeagueQualified
                    && AiPersonalityCatalog.BuiltIn.Any(item =>
                        item.Id == requested.Personality.Id))
                {
                    return new SpectatorChannelResolution(requested, SpectatorRecoveryKind.None);
                }
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or ArgumentOutOfRangeException)
        {
            // Invalid local data falls through to the canonical safe channel.
        }

        var fallback = AiPersonalityCatalog.BuiltInProfiles.Single(item =>
            item.Personality.Id == "balanced");
        return new SpectatorChannelResolution(
            fallback,
            SpectatorRecoveryKind.InvalidCustomChannel);
    }
}

public sealed class SpectatorStallTracker
{
    private GridPoint? _lastTarget;
    private AiTargetKind _lastTargetKind;
    private bool _hasObservation;

    public int ConsecutiveStalledDecisions { get; private set; }

    public bool ShouldRecover =>
        ConsecutiveStalledDecisions >= SpectatorMatchSession.StalledDecisionThreshold;

    public void Observe(AiDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ConsecutiveStalledDecisions = _hasObservation
            && decision.Target == _lastTarget
            && decision.TargetKind == _lastTargetKind
            && !decision.ReducedTargetDistance
                ? ConsecutiveStalledDecisions + 1
                : 0;
        _lastTarget = decision.Target;
        _lastTargetKind = decision.TargetKind;
        _hasObservation = true;
    }

    public void NoteRecovery() => ConsecutiveStalledDecisions = 0;
}

/// <summary>
/// Two equal-rules AI lanes share one gameplay seed. View switching and all
/// commentary/audio fallbacks are presentation-only and cannot mutate either
/// lane. The exact seed challenge deliberately excludes controller state.
/// </summary>
public sealed class SpectatorMatchSession
{
    public const int StalledDecisionThreshold = 6;
    public const int CommentaryCooldownSteps = 8;
    public const int MaximumBroadcastSteps = 2_000;

    private readonly SpectatorLane _featured;
    private readonly SpectatorLane _rival;
    private string _viewedPersonalityId;
    private int _playbackSpeedIndex;
    private SpectatorExplanationLevel _explanationLevel;
    private string _latestCommentaryCopyId;
    private int _commentaryCooldown;
    private SpectatorRecoveryKind _lastRecovery;

    public SpectatorMatchSession(SpectatorSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();
        SpectatorRivalCatalog.Validate();
        Selection = selection;
        var config = selection.CreateRunConfig();
        _featured = new SpectatorLane(
            selection.PersonalityId,
            selection.GameplaySeed,
            config,
            ControllerSeed(selection.GameplaySeed, selection.PersonalityId));
        _rival = new SpectatorLane(
            selection.RivalPersonalityId,
            selection.GameplaySeed,
            config,
            ControllerSeed(selection.GameplaySeed, selection.RivalPersonalityId));
        _viewedPersonalityId = selection.PersonalityId;
        _playbackSpeedIndex = selection.PlaybackSpeedIndex;
        _explanationLevel = selection.ExplanationLevel;
        _latestCommentaryCopyId = SpectatorRivalCatalog
            .Get(_viewedPersonalityId)
            .RunStartCopyId;
        InitialRulesStateEqual = _featured.Run.ComputeStateHash()
            == _rival.Run.ComputeStateHash();
    }

    public SpectatorSelection Selection { get; }

    public bool InitialRulesStateEqual { get; }

    public int StepCount { get; private set; }

    public bool Paused { get; private set; } = true;

    public int PlaybackSpeedIndex => _playbackSpeedIndex;

    public double PlaybackSpeed => SpectatorSelection.PlaybackSpeeds[_playbackSpeedIndex];

    public SpectatorExplanationLevel ExplanationLevel => _explanationLevel;

    public string ViewedPersonalityId => _viewedPersonalityId;

    public string LatestCommentaryCopyId => _latestCommentaryCopyId;

    public RunSnapshot ViewedSnapshot => ViewedLane.Run.GetSnapshot();

    public RunModeDefinition Mode => Selection.Mode;

    public bool IsComplete => StepCount >= MaximumBroadcastSteps
        || (_featured.Run.Status != RunStatus.Running
            && _rival.Run.Status != RunStatus.Running);

    public void TogglePaused() => Paused = !Paused;

    public void SetPaused(bool paused) => Paused = paused;

    public void CyclePlaybackSpeed(int offset)
    {
        _playbackSpeedIndex = (_playbackSpeedIndex + offset
            + SpectatorSelection.PlaybackSpeeds.Count)
            % SpectatorSelection.PlaybackSpeeds.Count;
    }

    public void CycleExplanationLevel(int offset)
    {
        var count = Enum.GetValues<SpectatorExplanationLevel>().Length;
        _explanationLevel = (SpectatorExplanationLevel)(
            ((int)_explanationLevel + offset + count) % count);
    }

    public void SwitchViewedChannel()
    {
        _viewedPersonalityId = _viewedPersonalityId == _featured.PersonalityId
            ? _rival.PersonalityId
            : _featured.PersonalityId;
        _latestCommentaryCopyId = SpectatorRivalCatalog
            .Get(_viewedPersonalityId)
            .RunStartCopyId;
        _commentaryCooldown = 0;
    }

    public SpectatorAdvance Advance(
        bool audioAvailable = true,
        IReadOnlySet<string>? unavailableCommentaryCopyIds = null)
    {
        if (Paused || IsComplete)
        {
            return new SpectatorAdvance(
                null,
                null,
                SpectatorRecoveryKind.None,
                _latestCommentaryCopyId,
                RulesAdvanced: false,
                PresentationFallbackChangedRules: false);
        }

        return AdvanceCore(audioAvailable, unavailableCommentaryCopyIds);
    }

    public SpectatorAdvance StepOnce(
        bool audioAvailable = true,
        IReadOnlySet<string>? unavailableCommentaryCopyIds = null) =>
        IsComplete
            ? new SpectatorAdvance(
                null,
                null,
                SpectatorRecoveryKind.None,
                _latestCommentaryCopyId,
                RulesAdvanced: false,
                PresentationFallbackChangedRules: false)
            : AdvanceCore(audioAvailable, unavailableCommentaryCopyIds);

    public SpectatorExperienceOverlay BuildOverlay(int localRecordScore, string vibeLevelId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(localRecordScore);

        ArgumentException.ThrowIfNullOrWhiteSpace(vibeLevelId);
        var lane = ViewedLane;
        var rivalLane = ReferenceEquals(lane, _featured) ? _rival : _featured;
        var rival = SpectatorRivalCatalog.Get(lane.PersonalityId);
        var decision = lane.CurrentDecision;
        return new SpectatorExperienceOverlay(
            lane.PersonalityId,
            rival.BroadcastIdentity,
            rivalLane.PersonalityId,
            SpectatorRivalCatalog.Get(rivalLane.PersonalityId).BroadcastIdentity,
            rival.StationAffinity,
            rival.ShedId,
            decision?.TargetKind ?? AiTargetKind.None,
            decision?.Target,
            decision?.RiskBand ?? AiRiskBand.Open,
            SpectatorSurvivalResources.FromSnapshot(lane.Run.GetSnapshot()),
            vibeLevelId,
            lane.Run.Score - localRecordScore,
            decision?.Reason,
            decision is null || _explanationLevel == SpectatorExplanationLevel.Hidden
                ? null
                : DecisionReasonCopyId(decision.Reason),
            _explanationLevel,
            _latestCommentaryCopyId,
            _lastRecovery,
            OfficialLeagueQualified: true,
            PredictionsInformationalOnly: !SpectatorPredictionContract.WageringAllowed
                && SpectatorPredictionContract.CurrencyAward == 0
                && SpectatorPredictionContract.HumanProgressionAwardCount == 0);
    }

    public SpectatorChallengeDescriptor CreateChallenge()
    {
        var config = Selection.CreateRunConfig();
        return new SpectatorChallengeDescriptor(
            SpectatorChallengeDescriptor.CurrentSchemaVersion,
            SpectatorChallengeDescriptor.KindId,
            Selection.GameplaySeed,
            SnakeRun.RulesetId,
            SnakeRun.RulesVersion,
            Selection.ModeId,
            Selection.ModeVersion,
            RunModeCatalog.GetScoreCategoryId(config),
            RunConfig.ConfigHashAlgorithmId,
            config.ComputeConfigHash(),
            ScoreRunContextCatalog.SeededChallengeRunKind,
            ScoreRunContextCatalog.FixedChallengeSeedCategory,
            HumanRulesEqual: true,
            ContainsAiDecisionTrace: false,
            ContainsAiRandomState: false,
            AwardsAiProgression: false);
    }

    public SpectatorMatchResult BuildResult()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException("A spectator match result requires both terminal lanes.");
        }

        var endedByLimit = StepCount >= MaximumBroadcastSteps;
        var featured = _featured.BuildOutcome(endedByLimit);
        var rival = _rival.BuildOutcome(endedByLimit);
        return new SpectatorMatchResult(
            Selection.GameplaySeed,
            Selection.ModeId,
            Selection.ModeVersion,
            Selection.CreateRunConfig().ComputeConfigHash(),
            featured,
            rival,
            Selection.Prediction,
            SpectatorPredictionContract.Evaluate(Selection.Prediction, featured),
            EqualRules: _featured.Run.ConfigHash == _rival.Run.ConfigHash
                && _featured.Run.Mode == _rival.Run.Mode,
            AiProgressionAwarded: false);
    }

    public int ScoreFor(string personalityId) => Lane(personalityId).Run.Score;

    private SpectatorAdvance AdvanceCore(
        bool audioAvailable,
        IReadOnlySet<string>? unavailableCommentaryCopyIds)
    {
        var featuredStep = _featured.Advance();
        var rivalStep = _rival.Advance();
        if (featuredStep is not null || rivalStep is not null)
        {
            StepCount++;
        }
        var viewedStep = _viewedPersonalityId == _featured.PersonalityId
            ? featuredStep
            : rivalStep;
        var lane = ViewedLane;
        var recovery = lane.LastAdvanceRecoveredStall
            ? SpectatorRecoveryKind.StalledTarget
            : SpectatorRecoveryKind.None;
        var candidate = SelectCommentary(lane, viewedStep);
        var terminalCommentary = viewedStep is { Status: not RunStatus.Running };
        if (candidate is not null && (_commentaryCooldown <= 0 || terminalCommentary))
        {
            if (unavailableCommentaryCopyIds?.Contains(candidate) == true)
            {
                _latestCommentaryCopyId = SpectatorRivalCatalog.CommentaryFallbackCopyId;
                recovery |= SpectatorRecoveryKind.MissingCommentary;
            }
            else if (!string.Equals(candidate, _latestCommentaryCopyId, StringComparison.Ordinal))
            {
                _latestCommentaryCopyId = candidate;
            }

            _commentaryCooldown = CommentaryCooldownSteps;
        }
        else if (_commentaryCooldown > 0)
        {
            _commentaryCooldown--;
        }

        if (!audioAvailable)
        {
            recovery |= SpectatorRecoveryKind.AudioUnavailable;
        }

        _lastRecovery = recovery;
        return new SpectatorAdvance(
            featuredStep,
            rivalStep,
            recovery,
            _latestCommentaryCopyId,
            RulesAdvanced: featuredStep is not null || rivalStep is not null,
            PresentationFallbackChangedRules: false);
    }

    private static string? SelectCommentary(SpectatorLane lane, RunStepResult? step)
    {
        if (step is null)
        {
            return null;
        }

        var result = step.Value;
        var rival = SpectatorRivalCatalog.Get(lane.PersonalityId);
        if (result.Status != RunStatus.Running)
        {
            return rival.TerminalCopyId;
        }

        if (result.OrderedEvents.Any(item => item.Kind is
                RunEventKind.PowerCollected
                or RunEventKind.PowerActivated
                or RunEventKind.CollisionPrevented))
        {
            return rival.PowerCopyId;
        }

        if (result.OrderedEvents.Any(item => item.Kind == RunEventKind.AteFood))
        {
            return rival.FoodCopyId;
        }

        if (lane.LastAdvanceRecoveredStall
            || lane.CurrentDecision?.RiskBand is AiRiskBand.Exposed or AiRiskBand.DeadEnd)
        {
            return rival.PressureCopyId;
        }

        return null;
    }

    private SpectatorLane ViewedLane => Lane(_viewedPersonalityId);

    private SpectatorLane Lane(string personalityId) =>
        _featured.PersonalityId == personalityId
            ? _featured
            : _rival.PersonalityId == personalityId
                ? _rival
                : throw new ArgumentException("The personality is not part of this match.", nameof(personalityId));

    private static ulong ControllerSeed(ulong gameplaySeed, string personalityId)
    {
        var personalityIndex = AiPersonalityCatalog.BuiltIn
            .Select((item, index) => new { item.Id, Index = index + 1 })
            .Single(item => item.Id == personalityId)
            .Index;
        return unchecked(gameplaySeed ^ (0x9E3779B97F4A7C15UL * (ulong)personalityIndex));
    }

    internal static string DecisionReasonCopyId(AiDecisionReason reason) => reason switch
    {
        AiDecisionReason.AdvanceFood => "spectator.reason.advance-food",
        AiDecisionReason.AdvancePower => "spectator.reason.advance-power",
        AiDecisionReason.PreserveOptions => "spectator.reason.preserve-options",
        AiDecisionReason.ContinueCourse => "spectator.reason.continue-course",
        AiDecisionReason.EscapeHazard => "spectator.reason.escape-hazard",
        AiDecisionReason.BoundedChaos => "spectator.reason.bounded-chaos",
        AiDecisionReason.RecoverStalledTarget => "spectator.reason.recover-stall",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private sealed class SpectatorLane
    {
        private readonly AiPersonalityController _controller;
        private readonly SpectatorStallTracker _stallTracker = new();

        public SpectatorLane(
            string personalityId,
            ulong gameplaySeed,
            RunConfig config,
            ulong controllerSeed)
        {
            PersonalityId = personalityId;
            Run = SnakeRun.Create(gameplaySeed, config);
            _controller = new AiPersonalityController(
                AiPersonalityCatalog.GetBuiltIn(personalityId),
                controllerSeed);
        }

        public string PersonalityId { get; }

        public SnakeRun Run { get; }

        public AiDecision? CurrentDecision { get; private set; }

        public bool LastAdvanceRecoveredStall { get; private set; }

        public int MaximumCombo { get; private set; }

        public int FoodEaten { get; private set; }

        public int PowerCollections { get; private set; }

        public int CollisionRecoveries { get; private set; }

        public RunStepResult? Advance()
        {
            LastAdvanceRecoveredStall = false;
            if (Run.Status != RunStatus.Running)
            {
                return null;
            }

            var recover = _stallTracker.ShouldRecover;
            var decision = recover
                ? _controller.SelectStallRecoveryDecision(Run)
                : _controller.SelectDecision(Run);
            CurrentDecision = decision;
            LastAdvanceRecoveredStall = decision.RecoveredStalledTarget;
            _stallTracker.Observe(decision);
            if (decision.RecoveredStalledTarget)
            {
                _stallTracker.NoteRecovery();
            }
            Run.QueueDirection(decision.Direction);
            var result = Run.Step();
            MaximumCombo = Math.Max(MaximumCombo, Run.ComboCount);
            FoodEaten += result.OrderedEvents.Count(item => item.Kind == RunEventKind.AteFood);
            PowerCollections += result.OrderedEvents.Count(item =>
                item.Kind == RunEventKind.PowerCollected);
            CollisionRecoveries += result.OrderedEvents.Count(item =>
                item.Kind == RunEventKind.CollisionPrevented);
            return result;
        }

        public SpectatorLaneOutcome BuildOutcome(bool endedByBroadcastLimit)
        {
            if (Run.Status == RunStatus.Running && !endedByBroadcastLimit)
            {
                throw new InvalidOperationException("A spectator lane outcome requires a terminal run.");
            }

            return new SpectatorLaneOutcome(
                PersonalityId,
                Run.Score,
                Run.Tick,
                Run.Status,
                Run.DeathCause,
                MaximumCombo,
                FoodEaten,
                PowerCollections,
                CollisionRecoveries,
                Run.ComputeStateHash(),
                EndedByBroadcastLimit: Run.Status == RunStatus.Running
                    && endedByBroadcastLimit);
        }
    }
}
