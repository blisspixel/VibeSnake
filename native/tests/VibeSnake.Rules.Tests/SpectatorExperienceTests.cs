namespace VibeSnake.Rules.Tests;

public sealed class SpectatorExperienceTests
{
    [Fact]
    public void Rival_catalog_binds_all_world_identities_to_measured_policies_and_distinct_sheds()
    {
        SpectatorRivalCatalog.Validate();

        Assert.Equal(10, SpectatorRivalCatalog.All.Count);
        Assert.Equal(
            AiPersonalityCatalog.BuiltIn.Select(item => item.Id),
            SpectatorRivalCatalog.All.Select(item => item.PersonalityId));
        Assert.Equal(
            AiPersonalityCatalog.BuiltIn.Select(item => item.Name),
            SpectatorRivalCatalog.All.Select(item => item.BroadcastIdentity));
        Assert.Equal(10, SpectatorRivalCatalog.All.Select(item => item.ShedId).Distinct().Count());
        Assert.Equal("The Pit", SpectatorRivalCatalog.Get("speed_demon").StationAffinity);
        Assert.Equal("Ourotron", SpectatorRivalCatalog.Get("wall_hugger").StationAffinity);
        Assert.Equal(
            50,
            SpectatorRivalCatalog.All
                .SelectMany(item => Enum.GetValues<SpectatorCommentaryTrigger>()
                    .Select(item.CommentaryCopyId))
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Throws<ArgumentException>(() => SpectatorRivalCatalog.Get("missing"));
    }

    [Fact]
    public void Selection_exposes_closed_seed_speed_mode_explanation_and_prediction_choices()
    {
        var selection = SpectatorSelection.CreateDefault();
        selection.Validate();

        Assert.Equal(42UL, SpectatorSeedCatalog.Get(SpectatorSeedClass.ReviewedFixed, 3));
        Assert.Equal(ulong.MaxValue, SpectatorSeedCatalog.Get(SpectatorSeedClass.PreviousFailure, 3));
        Assert.Equal([0.5, 1.0, 2.0, 4.0], SpectatorSelection.PlaybackSpeeds);
        Assert.Equal(RunModeCatalog.Vibe, selection.Mode);
        Assert.Equal(1.0, selection.PlaybackSpeed);
        Assert.False(SpectatorPredictionContract.WageringAllowed);
        Assert.Equal(0, SpectatorPredictionContract.CurrencyAward);
        Assert.Equal(0, SpectatorPredictionContract.HumanProgressionAwardCount);

        Assert.Throws<ArgumentException>(() =>
            (selection with { RivalPersonalityId = selection.PersonalityId }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpectatorSeedCatalog.Get(SpectatorSeedClass.Exploratory, 4));
        Assert.Throws<ArgumentException>(() =>
            (selection with { PlaybackSpeedIndex = 4 }).Validate());
    }

    [Fact]
    public void Match_controls_are_presentation_only_and_both_lanes_are_deterministic()
    {
        var left = new SpectatorMatchSession(SpectatorSelection.CreateDefault());
        var right = new SpectatorMatchSession(SpectatorSelection.CreateDefault());
        Assert.True(left.InitialRulesStateEqual);
        Assert.True(right.InitialRulesStateEqual);
        Assert.True(left.Paused);

        var featuredBefore = left.ViewedSnapshot.StateHash;
        left.SwitchViewedChannel();
        var rivalBefore = left.ViewedSnapshot.StateHash;
        Assert.Equal(featuredBefore, rivalBefore);
        left.SwitchViewedChannel();
        Assert.Equal(featuredBefore, left.ViewedSnapshot.StateHash);

        left.CyclePlaybackSpeed(1);
        left.CycleExplanationLevel(1);
        Assert.Equal(2.0, left.PlaybackSpeed);
        Assert.Equal(SpectatorExplanationLevel.Detailed, left.ExplanationLevel);
        Assert.False(left.Advance().RulesAdvanced);

        left.SetPaused(false);
        right.SetPaused(false);
        while (!left.IsComplete && !right.IsComplete)
        {
            Assert.True(left.Advance().RulesAdvanced);
            Assert.True(right.Advance().RulesAdvanced);
        }

        Assert.Equal(left.StepCount, right.StepCount);
        Assert.Equal(left.BuildResult(), right.BuildResult());
        Assert.True(left.BuildResult().EqualRules);
        Assert.False(left.BuildResult().AiProgressionAwarded);
        Assert.InRange(left.StepCount, 1, SpectatorMatchSession.MaximumBroadcastSteps);
    }

    [Fact]
    public void Missing_commentary_and_audio_fallbacks_do_not_change_either_rules_lane()
    {
        var baseline = new SpectatorMatchSession(SpectatorSelection.CreateDefault());
        var fallback = new SpectatorMatchSession(SpectatorSelection.CreateDefault());
        baseline.SetPaused(false);
        fallback.SetPaused(false);
        var unavailable = SpectatorRivalCatalog.All
            .SelectMany(item => Enum.GetValues<SpectatorCommentaryTrigger>()
                .Select(item.CommentaryCopyId))
            .ToHashSet(StringComparer.Ordinal);
        var observedAudioFallback = false;
        var observedCommentaryFallback = false;
        while (!baseline.IsComplete && !fallback.IsComplete)
        {
            baseline.Advance();
            var advance = fallback.Advance(
                audioAvailable: false,
                unavailableCommentaryCopyIds: unavailable);
            observedAudioFallback |= advance.Recovery.HasFlag(
                SpectatorRecoveryKind.AudioUnavailable);
            observedCommentaryFallback |= advance.Recovery.HasFlag(
                SpectatorRecoveryKind.MissingCommentary);
            Assert.False(advance.PresentationFallbackChangedRules);
        }

        Assert.True(observedAudioFallback);
        Assert.True(observedCommentaryFallback);
        Assert.Equal(baseline.BuildResult(), fallback.BuildResult());
        Assert.Equal(
            SpectatorRivalCatalog.CommentaryFallbackCopyId,
            fallback.LatestCommentaryCopyId);
    }

    [Fact]
    public void Invalid_custom_channel_recovers_to_the_official_balanced_profile()
    {
        var invalid = AiPersonalityCatalog.BuiltInProfiles[0] with
        {
            ContentKind = AiPersonalityContentKind.Custom,
            StatusLabel = AiPersonalityCatalog.CustomStatusLabel,
            OfficialLeagueQualified = false,
        };
        var resolved = SpectatorChannelResolver.Resolve(invalid);
        var missing = SpectatorChannelResolver.Resolve(null);
        var official = SpectatorChannelResolver.Resolve(
            AiPersonalityCatalog.BuiltInProfiles[0]);

        Assert.Equal("balanced", resolved.Profile.Personality.Id);
        Assert.Equal("balanced", missing.Profile.Personality.Id);
        Assert.Equal(
            SpectatorRecoveryKind.InvalidCustomChannel,
            resolved.Recovery);
        Assert.Equal(SpectatorRecoveryKind.None, official.Recovery);
        Assert.Equal("speed_demon", official.Profile.Personality.Id);
    }

    [Fact]
    public void Stall_tracker_triggers_bounded_visible_state_recovery()
    {
        var tracker = new SpectatorStallTracker();
        var stalled = new AiDecision(
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
        tracker.Observe(stalled);
        Assert.False(tracker.ShouldRecover);
        for (var index = 0; index < SpectatorMatchSession.StalledDecisionThreshold; index++)
        {
            tracker.Observe(stalled);
        }

        Assert.True(tracker.ShouldRecover);
        var run = SnakeRun.Create(
            42UL,
            RunModeCatalog.CreateConfig(RunModeCatalog.Vibe));
        var controller = new AiPersonalityController(
            AiPersonalityCatalog.GetBuiltIn("balanced"),
            99UL);
        var recovery = controller.SelectStallRecoveryDecision(run);
        Assert.True(recovery.RecoveredStalledTarget);
        Assert.Equal(AiDecisionReason.RecoverStalledTarget, recovery.Reason);
        Assert.InRange(recovery.OnwardChoiceCount, 0, 3);
        tracker.NoteRecovery();
        Assert.False(tracker.ShouldRecover);
    }

    [Fact]
    public void Overlay_is_concise_typed_and_hides_reason_at_the_hidden_level()
    {
        var session = new SpectatorMatchSession(SpectatorSelection.CreateDefault());
        session.SetPaused(false);
        session.Advance();
        var concise = session.BuildOverlay(localRecordScore: 50, vibeLevelId: "grounded");

        Assert.Equal("balanced", concise.PersonalityId);
        Assert.Equal("Meanline", concise.PersonalityName);
        Assert.Equal("The Global Coil", concise.StationAffinity);
        Assert.NotNull(concise.DecisionReason);
        Assert.StartsWith("spectator.reason.", concise.DecisionReasonCopyId);
        Assert.True(concise.OfficialLeagueQualified);
        Assert.True(concise.PredictionsInformationalOnly);
        Assert.Equal(session.ViewedSnapshot.Score - 50, concise.RecordDelta);

        session.CycleExplanationLevel(-1);
        var hidden = session.BuildOverlay(0, "grounded");
        Assert.Equal(SpectatorExplanationLevel.Hidden, hidden.ExplanationLevel);
        Assert.Null(hidden.DecisionReasonCopyId);
    }

    [Fact]
    public void Exact_seed_challenge_uses_identical_human_rules_and_excludes_ai_state()
    {
        var session = new SpectatorMatchSession(SpectatorSelection.CreateDefault());
        var challenge = session.CreateChallenge();
        var human = challenge.CreateHumanRun();
        var expected = SnakeRun.Create(
            SpectatorSelection.CreateDefault().GameplaySeed,
            SpectatorSelection.CreateDefault().CreateRunConfig());

        challenge.Validate();
        Assert.Equal(expected.ComputeStateHash(), human.ComputeStateHash());
        Assert.Equal(expected.ConfigHash, human.ConfigHash);
        Assert.True(challenge.HumanRulesEqual);
        Assert.False(challenge.ContainsAiDecisionTrace);
        Assert.False(challenge.ContainsAiRandomState);
        Assert.False(challenge.AwardsAiProgression);
        Assert.Equal(
            ScoreRunContextCatalog.SeededChallengeRunKind,
            challenge.RunKindId);
    }

    [Fact]
    public void Prediction_thresholds_and_reason_copy_ids_cover_the_closed_contracts()
    {
        var outcome = new SpectatorLaneOutcome(
            "balanced",
            Score: 100,
            FinalTick: 500,
            RunStatus.Dead,
            DeathCause.Starvation,
            MaximumCombo: 5,
            FoodEaten: 0,
            PowerCollections: 0,
            CollisionRecoveries: 0,
            FinalStateHash: "0123456789abcdef",
            EndedByBroadcastLimit: false);

        Assert.Null(SpectatorPredictionContract.Evaluate(SpectatorPredictionKind.None, outcome));
        Assert.True(SpectatorPredictionContract.Evaluate(
            SpectatorPredictionKind.ScoreAtLeast100,
            outcome));
        Assert.True(SpectatorPredictionContract.Evaluate(
            SpectatorPredictionKind.ComboAtLeast5,
            outcome));
        Assert.True(SpectatorPredictionContract.Evaluate(
            SpectatorPredictionKind.SurviveAtLeast500Ticks,
            outcome));
        Assert.False(SpectatorPredictionContract.Evaluate(
            SpectatorPredictionKind.ScoreAtLeast100,
            outcome with { Score = 99 }));
        Assert.False(SpectatorPredictionContract.Evaluate(
            SpectatorPredictionKind.ComboAtLeast5,
            outcome with { MaximumCombo = 4 }));
        Assert.False(SpectatorPredictionContract.Evaluate(
            SpectatorPredictionKind.SurviveAtLeast500Ticks,
            outcome with { FinalTick = 499 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpectatorPredictionContract.Evaluate((SpectatorPredictionKind)255, outcome));

        var expected = new Dictionary<AiDecisionReason, string>
        {
            [AiDecisionReason.AdvanceFood] = "spectator.reason.advance-food",
            [AiDecisionReason.AdvancePower] = "spectator.reason.advance-power",
            [AiDecisionReason.PreserveOptions] = "spectator.reason.preserve-options",
            [AiDecisionReason.ContinueCourse] = "spectator.reason.continue-course",
            [AiDecisionReason.EscapeHazard] = "spectator.reason.escape-hazard",
            [AiDecisionReason.BoundedChaos] = "spectator.reason.bounded-chaos",
            [AiDecisionReason.RecoverStalledTarget] = "spectator.reason.recover-stall",
        };
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, SpectatorMatchSession.DecisionReasonCopyId(pair.Key));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpectatorMatchSession.DecisionReasonCopyId((AiDecisionReason)255));
    }

    [Fact]
    public void Challenge_and_selection_validation_reject_every_identity_mutation()
    {
        var selection = SpectatorSelection.CreateDefault();
        SpectatorSelection[] invalidSelections =
        [
            selection with { PlaybackSpeedIndex = -1 },
            selection with { PlaybackSpeedIndex = SpectatorSelection.PlaybackSpeeds.Count },
            selection with { ExplanationLevel = (SpectatorExplanationLevel)255 },
            selection with { Prediction = (SpectatorPredictionKind)255 },
        ];
        foreach (var invalid in invalidSelections)
        {
            Assert.Throws<ArgumentException>(invalid.Validate);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpectatorSeedCatalog.Get((SpectatorSeedClass)255));

        var challenge = new SpectatorMatchSession(selection).CreateChallenge();
        SpectatorChallengeDescriptor[] invalidChallenges =
        [
            challenge with { SchemaVersion = 2 },
            challenge with { Kind = "wrong" },
            challenge with { RulesetId = "wrong" },
            challenge with { RulesVersion = -1 },
            challenge with { ConfigHashAlgorithm = "wrong" },
            challenge with { RunKindId = "wrong" },
            challenge with { SeedCategoryId = "wrong" },
            challenge with { HumanRulesEqual = false },
            challenge with { ContainsAiDecisionTrace = true },
            challenge with { ContainsAiRandomState = true },
            challenge with { AwardsAiProgression = true },
        ];
        foreach (var invalid in invalidChallenges)
        {
            Assert.Throws<InvalidDataException>(invalid.Validate);
        }

        Assert.Throws<InvalidOperationException>(() =>
            (challenge with { ConfigHash = new string('0', 64) }).CreateHumanRun());
        Assert.Throws<InvalidOperationException>(() =>
            (challenge with { ScoreCategoryId = "wrong" }).CreateHumanRun());
    }

    [Fact]
    public void Channel_snapshot_and_session_boundaries_fail_closed()
    {
        var official = AiPersonalityCatalog.BuiltInProfiles[0];
        var unqualified = official with { OfficialLeagueQualified = false };
        var customClaimingQualification = official with
        {
            ContentKind = AiPersonalityContentKind.Custom,
            OfficialLeagueQualified = true,
        };
        var invalidArgument = official with
        {
            Personality = official.Personality with { Id = string.Empty },
        };
        var invalidRange = official with
        {
            Personality = official.Personality with { Aggression = -1 },
        };
        foreach (var profile in new[]
                 {
                     unqualified,
                     customClaimingQualification,
                     invalidArgument,
                     invalidRange,
                 })
        {
            var resolution = SpectatorChannelResolver.Resolve(profile);
            Assert.Equal("balanced", resolution.Profile.Personality.Id);
            Assert.Equal(SpectatorRecoveryKind.InvalidCustomChannel, resolution.Recovery);
        }

        var run = SnakeRun.Create(42UL, SpectatorSelection.CreateDefault().CreateRunConfig());
        var snapshot = run.GetSnapshot() with
        {
            ShieldTicksRemaining = 1,
            PhaseShiftTicksRemaining = 1,
            LastStandHeld = true,
            LastStandRecoveryTicksRemaining = 1,
        };
        var resources = SpectatorSurvivalResources.FromSnapshot(snapshot);
        Assert.Equal(4, resources.ActiveResourceCount);
        Assert.True(resources.Shield);
        Assert.True(resources.PhaseShift);
        Assert.True(resources.LastStandHeld);
        Assert.True(resources.LastStandRecovery);

        var session = new SpectatorMatchSession(SpectatorSelection.CreateDefault());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BuildOverlay(-1, "grounded"));
        Assert.Throws<ArgumentException>(() => session.BuildOverlay(0, string.Empty));
        Assert.Throws<InvalidOperationException>(session.BuildResult);
        Assert.Throws<ArgumentException>(() => session.ScoreFor("missing"));
        var initial = session.BuildOverlay(0, "grounded");
        Assert.Null(initial.DecisionReason);
        Assert.Null(initial.DecisionReasonCopyId);

        session.SetPaused(false);
        while (!session.IsComplete)
        {
            session.Advance();
        }
        Assert.False(session.Advance().RulesAdvanced);
        Assert.False(session.StepOnce().RulesAdvanced);
    }
}
