using System.Security.Cryptography;
using System.Text;
using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentLessonEvidenceTests
{
    [Fact]
    public void Completed_lesson_recommends_finish_and_omits_retry_guidance()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var session = CreateSession(definition);
        var initial = session.Observe();

        Assert.Null(initial.LessonProgress!.RecommendedNextTool);
        var rejected = session.SubmitAction(Request("complete-reversal", initial, AgentAction.Left));
        var completed = session.SubmitAction(Request(
            "complete-turn",
            rejected.Observation,
            AgentAction.Up));

        Assert.True(completed.Observation.LessonProgress!.AllRequirementsSatisfied);
        Assert.Equal("finish_match", completed.Observation.LessonProgress.RecommendedNextTool);
        Assert.Null(completed.Observation.LessonProgress.RetryDescriptor);

        var result = session.Finish();
        var outcome = Assert.IsType<AgentLessonOutcomeV3>(result.LessonOutcome);
        var terminalProgress = Assert.IsType<AgentLessonProgressV3>(session.Observe().LessonProgress);
        Assert.Equal(AgentLessonReviewCode.TargetReached, outcome.ReviewCode);
        Assert.Null(outcome.RetryDescriptor);
        Assert.Null(terminalProgress.RecommendedNextTool);
        Assert.Null(terminalProgress.RetryDescriptor);
    }

    [Fact]
    public void Empty_and_partial_outcomes_name_the_first_unmet_requirement_and_fresh_retry()
    {
        foreach (var definition in AgentSignalSchoolCatalog.All)
        {
            var result = CreateSession(definition).Finish();
            var outcome = Assert.IsType<AgentLessonOutcomeV3>(result.LessonOutcome);

            Assert.False(outcome.AllRequirementsSatisfied);
            Assert.Equal(AgentMatchLifecycle.Aborted, result.Lifecycle);
            Assert.Equal(0, outcome.RequirementsSatisfied);
            Assert.Equal(definition.Requirements[0].Id, outcome.FirstUnmetRequirementId);
            Assert.Equal(
                definition.Requirements[0].EvidenceSource == AgentLessonEvidenceSource.AttemptWitness
                    ? AgentLessonReviewCode.InsufficientAttemptEvidence
                    : AgentLessonReviewCode.ReplayRequirementUnmet,
                outcome.ReviewCode);
            Assert.Equal(AgentMatchEndReason.AgentFinished, outcome.EndReason);
            Assert.True(AgentSignalSchoolCatalog.IsValidOutcome(outcome));
            AssertRetry(
                outcome.RetryDescriptor!,
                definition.Id,
                AgentPassportV4.FourDirectionActionProfile);
        }

        var firstTurn = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var partial = CreateSession(firstTurn);
        var initial = partial.Observe();
        var rejected = partial.SubmitAction(Request("partial-reversal", initial, AgentAction.Left));
        var partialOutcome = Assert.IsType<AgentLessonOutcomeV3>(partial.Finish().LessonOutcome);

        Assert.Equal(AgentActionRejection.IllegalDirection, rejected.Rejection);
        Assert.Equal(1, partialOutcome.RequirementsSatisfied);
        Assert.Equal("legal_turn_after_rejection", partialOutcome.FirstUnmetRequirementId);
        Assert.Equal(AgentLessonReviewCode.ReplayRequirementUnmet, partialOutcome.ReviewCode);
        Assert.Equal(1, partialOutcome.AttemptEvidenceCount);
        Assert.DoesNotContain(
            typeof(AgentLessonRetryDescriptorV1).GetProperties(),
            property => property.Name.Contains("Handle", StringComparison.Ordinal)
                || property.Name.Contains("State", StringComparison.Ordinal)
                || property.Name.Contains("Key", StringComparison.Ordinal)
                || property.Name.Contains("History", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exact_and_eight_way_concurrent_retries_record_one_attempt_witness()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var session = CreateSession(definition);
        var initial = session.Observe();
        var request = Request("concurrent-reversal", initial, AgentAction.Left);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => session.SubmitAction(request))));
        var exactRetry = session.SubmitAction(request);

        Assert.All(responses, response => Assert.Same(responses[0], response));
        Assert.Same(responses[0], exactRetry);
        Assert.All(responses, response =>
        {
            Assert.False(response.Accepted);
            Assert.False(response.RulesAdvanced);
            Assert.Equal(AgentActionRejection.IllegalDirection, response.Rejection);
        });
        Assert.Equal(0, session.Observe().Tick);
        Assert.Equal(1, session.Observe().LessonProgress!.AttemptEvidenceCount);
        Assert.Equal(
            ["opposite_reversal_rejected"],
            responses[0].LessonDelta!.NewlySatisfiedRequirementIds);
    }

    [Fact]
    public void Stale_conflicting_and_wrong_profile_requests_do_not_satisfy_attempt_evidence()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);

        var stale = CreateSession(definition);
        var initial = stale.Observe();
        var staleResponse = stale.SubmitAction(new AgentActionRequest(
            "stale-reversal",
            initial.Tick + 1,
            initial.StateHash,
            AgentAction.Left));
        Assert.Equal(AgentActionRejection.StaleTick, staleResponse.Rejection);
        AssertNoAttemptEvidence(staleResponse.Observation.LessonProgress);

        var conflict = CreateSession(definition);
        initial = conflict.Observe();
        var accepted = conflict.SubmitAction(Request("shared-key", initial, AgentAction.Continue));
        var conflicting = conflict.SubmitAction(Request("shared-key", initial, AgentAction.Left));
        Assert.True(accepted.Accepted);
        Assert.Equal(AgentActionRejection.IdempotencyConflict, conflicting.Rejection);
        AssertNoAttemptEvidence(conflicting.Observation.LessonProgress);

        var burstProfile = CreateSession(
            definition,
            AgentPassportV4.FourDirectionBurstActionProfile);
        initial = burstProfile.Observe();
        var wrongStepProfile = burstProfile.SubmitAction(
            Request("wrong-step-profile", initial, AgentAction.Left));
        Assert.Equal(AgentActionRejection.WrongActionProfile, wrongStepProfile.Rejection);
        AssertNoAttemptEvidence(wrongStepProfile.Observation.LessonProgress);

        var stepProfile = CreateSession(definition);
        initial = stepProfile.Observe();
        var wrongBurstProfile = stepProfile.SubmitBurst(new AgentBurstRequest(
            "wrong-burst-profile",
            initial.Tick,
            initial.StateHash,
            AgentAction.Left,
            1));
        Assert.Equal(AgentActionRejection.WrongActionProfile, wrongBurstProfile.Rejection);
        AssertNoAttemptEvidence(wrongBurstProfile.Observation.LessonProgress);
    }

    [Fact]
    public void Attempt_witnesses_are_bounded_and_hash_every_accepted_witness()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Get(
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion));
        var tracker = new AgentLessonEvidenceTracker(definition.Id, config);
        var snapshot = SnakeRun.Create(definition.PracticeSeed, config).GetSnapshot();

        for (var index = 0; index < AgentSignalSchoolCatalog.MaximumAttemptWitnesses; index++)
        {
            Assert.True(tracker.TryRecordOppositeReversal(
                AgentLessonAttemptOperation.Step,
                $"witness-{index}",
                snapshot,
                AgentAction.Left));
        }

        Assert.False(tracker.TryRecordOppositeReversal(
            AgentLessonAttemptOperation.Step,
            "witness-over-cap",
            snapshot,
            AgentAction.Left));
        var progress = tracker.Snapshot(
            AgentLessonEvidenceState.Live,
            AgentPassportV4.FourDirectionActionProfile);
        Assert.Equal(32, progress.AttemptEvidenceCount);
        Assert.Matches("^[0-9a-f]{64}$", progress.AttemptEvidenceHash);
        Assert.True(AgentSignalSchoolCatalog.IsValidProgress(progress));
    }

    [Fact]
    public void Replay_evaluator_rejects_witness_shape_profile_identity_and_state_divergence()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Get(
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion));
        var run = SnakeRun.Create(definition.PracticeSeed, config);
        var initial = run.GetSnapshot();
        var replay = RunReplay.Capture(run, [[Direction.Up]]);
        var witness = new AgentLessonAttemptWitnessV1(
            1,
            AgentLessonAttemptOperation.Step,
            Hash("valid-witness"),
            initial.Tick,
            initial.StateHash,
            AgentAction.Left);
        var progress = AgentLessonEvidenceReplayEvaluator.Evaluate(
            definition.Id,
            AgentPassportV4.FourDirectionActionProfile,
            replay,
            [witness]);

        Assert.True(progress.AllRequirementsSatisfied);
        Assert.Equal(AgentLessonEvidenceState.Verified, progress.EvidenceState);
        AgentLessonAttemptWitnessV1[] invalid =
        [
            witness with { Ordinal = 0 },
            witness with { Ordinal = 2 },
            witness with { Operation = AgentLessonAttemptOperation.Burst },
            witness with { IdempotencyKeyHash = witness.IdempotencyKeyHash.ToUpperInvariant() },
            witness with { IdempotencyKeyHash = "short" },
            witness with { Tick = -1 },
            witness with { Tick = replay.Steps.Count + 1 },
            witness with { StateHash = AlternateHash(initial.StateHash) },
            witness with { Action = AgentAction.Up },
            witness with { Action = AgentAction.Continue },
            witness with { Action = (AgentAction)255 },
        ];
        Assert.All(invalid, item => Assert.Throws<InvalidOperationException>(() =>
            AgentLessonEvidenceReplayEvaluator.Evaluate(
                definition.Id,
                AgentPassportV4.FourDirectionActionProfile,
                replay,
                [item])));
        Assert.Throws<InvalidOperationException>(() =>
            AgentLessonEvidenceReplayEvaluator.Evaluate(
                definition.Id,
                AgentPassportV4.FourDirectionActionProfile,
                replay,
                [witness, witness with { Ordinal = 2 }]));
        var playback = new RunReplayPlayback(replay);
        Assert.True(playback.TryAdvance(out _));
        var later = witness with
        {
            IdempotencyKeyHash = Hash("later-witness"),
            Tick = 1,
            StateHash = playback.CurrentSnapshot.StateHash,
            Action = AgentAction.Down,
        };
        Assert.Throws<InvalidOperationException>(() =>
            AgentLessonEvidenceReplayEvaluator.Evaluate(
                definition.Id,
                AgentPassportV4.FourDirectionActionProfile,
                replay,
                [later, witness with { Ordinal = 2 }]));
        Assert.Throws<ArgumentException>(() => AgentLessonEvidenceReplayEvaluator.Evaluate(
            definition.Id,
            "unknown-profile",
            replay,
            [witness]));
    }

    [Fact]
    public void Replay_requirements_enforce_wrap_hunger_exit_power_recovery_combo_and_death_facts()
    {
        AssertWrapAndHungerFacts();
        AssertExitFacts();
        AssertPowerFacts();
        AssertRecoveryFacts();
        AssertComboFacts();
        AssertDeathFacts();
    }

    [Fact]
    public void Progress_delta_and_validators_reject_cross_field_drift()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var session = CreateSession(definition);
        var before = session.Observe().LessonProgress!;
        var initial = session.Observe();
        var rejection = session.SubmitAction(Request("validator-reversal", initial, AgentAction.Left));
        var after = rejection.Observation.LessonProgress!;
        var delta = AgentSignalSchoolCatalog.Delta(before, after);

        Assert.Equal(AgentLessonProgressDeltaV2.Contract, delta.Schema);
        Assert.Equal(["opposite_reversal_rejected"], delta.NewlySatisfiedRequirementIds);
        Assert.Equal(0, delta.PreviousRequirementsSatisfied);
        Assert.Equal(1, delta.CurrentRequirementsSatisfied);
        Assert.False(delta.AllRequirementsReachedThisMutation);
        Assert.Throws<ArgumentNullException>(() => AgentSignalSchoolCatalog.Delta(null!, after));
        Assert.Throws<ArgumentNullException>(() => AgentSignalSchoolCatalog.Delta(before, null!));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Delta(
            before,
            after with { LessonId = AgentSignalSchoolCatalog.WrapLineId }));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Delta(
            before,
            after with { Requirements = after.Requirements.Reverse().ToArray() }));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Delta(after, before));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Delta(
            after,
            after with { AttemptEvidenceHash = AlternateHash(after.AttemptEvidenceHash) }));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Delta(
            after,
            after with { AttemptEvidenceCount = after.AttemptEvidenceCount + 1 }));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Delta(
            after,
            after with { Requirements = before.Requirements }));

        Assert.True(AgentSignalSchoolCatalog.IsValidProgress(before));
        AgentLessonProgressV3?[] invalidProgress =
        [
            null,
            before with { Schema = "wrong" },
            before with { LessonId = "missing" },
            before with { Title = "Wrong" },
            before with { Instruction = "Wrong" },
            before with { EvaluationPolicyId = "wrong" },
            before with { EvidenceState = (AgentLessonEvidenceState)255 },
            before with { AttemptEvidenceCount = -1 },
            before with { AttemptEvidenceCount = AgentSignalSchoolCatalog.MaximumAttemptWitnesses + 1 },
            before with { AttemptEvidenceHash = "short" },
            before with { AttemptEvidenceHash = before.AttemptEvidenceHash.ToUpperInvariant() },
            before with { AttemptEvidenceHash = AlternateHash(before.AttemptEvidenceHash) },
            after with { AttemptEvidenceHash = AgentSignalSchoolCatalog.EmptyAttemptEvidenceHash },
            after with
            {
                AttemptEvidenceCount = 0,
                AttemptEvidenceHash = AgentSignalSchoolCatalog.EmptyAttemptEvidenceHash,
            },
            after with
            {
                Requirements = before.Requirements,
                RequirementsSatisfied = 0,
                FirstUnmetRequirementId = before.FirstUnmetRequirementId,
            },
            before with { Requirements = before.Requirements.Take(1).ToArray() },
            before with { Requirements = before.Requirements.Reverse().ToArray() },
            before with { RequirementsSatisfied = 1 },
            before with { AllRequirementsSatisfied = true },
            before with { FirstUnmetRequirementId = "legal_turn_after_rejection" },
            before with { RecommendedNextTool = "finish_match" },
            before with { EvidenceState = AgentLessonEvidenceState.Verified },
            before with { RetryDescriptor = AgentSignalSchoolCatalog.CreateRetryDescriptor(
                definition.Id,
                AgentPassportV4.FourDirectionActionProfile) },
        ];
        Assert.All(invalidProgress, value => Assert.False(AgentSignalSchoolCatalog.IsValidProgress(value)));
        var wrapProgress = CreateSession(AgentSignalSchoolCatalog.Get(
            AgentSignalSchoolCatalog.WrapLineId)).Observe().LessonProgress!;
        Assert.False(AgentSignalSchoolCatalog.IsValidProgress(wrapProgress with
        {
            AttemptEvidenceCount = 1,
            AttemptEvidenceHash = AlternateHash(wrapProgress.AttemptEvidenceHash),
        }));

        var outcome = Assert.IsType<AgentLessonOutcomeV3>(session.Finish().LessonOutcome);
        Assert.True(AgentSignalSchoolCatalog.IsValidOutcome(outcome));
        AgentLessonOutcomeV3?[] invalidOutcomes =
        [
            null,
            outcome with { Schema = "wrong" },
            outcome with { LessonId = "missing" },
            outcome with { EvaluationPolicyId = "wrong" },
            outcome with { ReviewCode = (AgentLessonReviewCode)255 },
            outcome with { ReviewCode = AgentLessonReviewCode.TargetReached },
            outcome with { EndReason = AgentMatchEndReason.None },
            outcome with { EndReason = AgentMatchEndReason.ReplayFailure },
            outcome with { EndReason = (AgentMatchEndReason)255 },
            outcome with { AttemptEvidenceCount = -1 },
            outcome with { ReplayPayloadHash = "short" },
            outcome with { AttemptEvidenceHash = "short" },
            outcome with
            {
                AttemptEvidenceHash = AgentSignalSchoolCatalog.EmptyAttemptEvidenceHash,
                EvidenceHash = AgentLessonEvidenceReplayEvaluator.ComputeEvidenceHash(
                    outcome.ReplayPayloadHash,
                    AgentSignalSchoolCatalog.EmptyAttemptEvidenceHash),
            },
            outcome with
            {
                AttemptEvidenceCount = 0,
                AttemptEvidenceHash = AgentSignalSchoolCatalog.EmptyAttemptEvidenceHash,
                EvidenceHash = AgentLessonEvidenceReplayEvaluator.ComputeEvidenceHash(
                    outcome.ReplayPayloadHash,
                    AgentSignalSchoolCatalog.EmptyAttemptEvidenceHash),
            },
            outcome with
            {
                Requirements = before.Requirements,
                RequirementsSatisfied = 0,
                FirstUnmetRequirementId = before.FirstUnmetRequirementId,
                ReviewCode = AgentLessonReviewCode.InsufficientAttemptEvidence,
            },
            outcome with { EvidenceHash = AlternateHash(outcome.EvidenceHash) },
            outcome with { RequirementsSatisfied = 2 },
            outcome with { AllRequirementsSatisfied = true },
            outcome with { FirstUnmetRequirementId = "opposite_reversal_rejected" },
            outcome with { RetryDescriptor = outcome.RetryDescriptor! with { Tool = "get_match" } },
            outcome with { RetryDescriptor = outcome.RetryDescriptor! with { FreshSessionRequired = false } },
        ];
        Assert.All(invalidOutcomes, value => Assert.False(AgentSignalSchoolCatalog.IsValidOutcome(value)));
        var wrapOutcome = Assert.IsType<AgentLessonOutcomeV3>(CreateSession(
            AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.WrapLineId)).Finish().LessonOutcome);
        var nonEmptyAttemptHash = AlternateHash(wrapOutcome.AttemptEvidenceHash);
        Assert.False(AgentSignalSchoolCatalog.IsValidOutcome(wrapOutcome with
        {
            AttemptEvidenceHash = nonEmptyAttemptHash,
            EvidenceHash = AgentLessonEvidenceReplayEvaluator.ComputeEvidenceHash(
                wrapOutcome.ReplayPayloadHash,
                nonEmptyAttemptHash),
        }));
        Assert.False(AgentSignalSchoolCatalog.IsValidOutcome(wrapOutcome with
        {
            AttemptEvidenceCount = 1,
            AttemptEvidenceHash = nonEmptyAttemptHash,
            EvidenceHash = AgentLessonEvidenceReplayEvaluator.ComputeEvidenceHash(
                wrapOutcome.ReplayPayloadHash,
                nonEmptyAttemptHash),
        }));
    }

    [Theory]
    [MemberData(nameof(NonPracticeSeeds))]
    public void Non_practice_seed_rows_reach_the_same_replay_evaluated_requirements(
        string lessonId,
        ulong seed,
        string expectedEvidence)
    {
        var definition = AgentSignalSchoolCatalog.Get(lessonId);
        Assert.NotEqual(definition.PracticeSeed, seed);
        var options = new AgentMatchOptions(
            $"non-practice-{lessonId}-{seed}",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            seed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps);
        var session = new AgentMatchSession(options);
        var witnesses = new List<AgentLessonAttemptWitnessV1>();
        if (lessonId == AgentSignalSchoolCatalog.FirstTurnId)
        {
            var initial = session.Observe();
            var key = $"non-practice-{lessonId}-{seed}-reversal";
            var rejected = session.SubmitAction(Request(
                key,
                initial,
                AgentLessonRouteDriver.OppositeAction(initial)));
            Assert.Equal(AgentActionRejection.IllegalDirection, rejected.Rejection);
            witnesses.Add(new AgentLessonAttemptWitnessV1(
                1,
                AgentLessonAttemptOperation.Step,
                Hash(key),
                initial.Tick,
                initial.StateHash,
                AgentLessonRouteDriver.OppositeAction(initial)));
        }

        AgentMatchResultV5? result = null;
        for (var step = 0; step < definition.MaximumSteps && result is null; step++)
        {
            var observation = session.Observe();
            var response = session.SubmitAction(Request(
                $"non-practice-{lessonId}-{seed}-{step}",
                observation,
                AgentLessonRouteDriver.ChooseAction(lessonId, observation)));
            Assert.True(response.Accepted, $"{lessonId}@{seed}: {response.Rejection}");
            result = response.MatchResult;
            if (lessonId == AgentSignalSchoolCatalog.FirstTurnId)
            {
                break;
            }
        }

        result ??= session.Finish();
        var progress = AgentLessonEvidenceReplayEvaluator.Evaluate(
            lessonId,
            AgentPassportV4.FourDirectionActionProfile,
            result.VerifiedReplay,
            witnesses);
        Assert.True(
            progress.AllRequirementsSatisfied,
            $"{lessonId}@{seed}: first unmet {progress.FirstUnmetRequirementId}; replay={result.ReplayPayloadHash}");
        var evidence = $"{result.ReplayPayloadHash}/{progress.AttemptEvidenceHash}/" +
            AgentLessonEvidenceReplayEvaluator.ComputeEvidenceHash(
                result.ReplayPayloadHash,
                progress.AttemptEvidenceHash);
        Assert.Equal(expectedEvidence, evidence);
    }

    public static TheoryData<string, ulong, string> NonPracticeSeeds =>
        new()
        {
            {
                AgentSignalSchoolCatalog.FirstTurnId,
                1UL,
                "76a8eaeb4d354cf58d0762b8b6d427b1ca6b37df6b80de51cded59f66587969b/19ea14c871b657140e3bd8958d59f940e17dd48015382ab12f3065eaac4c873f/1df2b22f3d693afa41fdd69163e627e37d544e111f1e6e21b240e5110d326d37"
            },
            {
                AgentSignalSchoolCatalog.FirstTurnId,
                2UL,
                "84002c6bb063fa8e93ee2b11ff2b43c0b5bce66971e6ca7f885fad2c71540b78/2b0c22ba885bb50f5a1ca3d730c50afbde61f1ebdbd5c3c88029a049dfdc01b0/ef7c5c47229f022ea25fff8a47cad5d48d9fcb9fb10fd870e8fc2eef8a94757e"
            },
            {
                AgentSignalSchoolCatalog.WrapLineId,
                1UL,
                "fb7e4c6c89ab97684d0aabaa0fcf9cc34c25403dc746cec20b52addf92baab3c/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/8eae3b37db6f506242b394c7a28da7291d6c369b30436a2c706f850700da6efa"
            },
            {
                AgentSignalSchoolCatalog.WrapLineId,
                2UL,
                "e4ac8ac8eef066a228714465cc38f98b2275d5269282a200b3838498aa517ece/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/e1d63d7108fd8559ff74816e9c441feb76135a3c5119fd8419684490fa2d46bd"
            },
            {
                AgentSignalSchoolCatalog.HungerRouteId,
                1UL,
                "0429c1e5310c6bc6a0f4d03bdc162e58af758fe385899467087e9e0889e3a06e/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/1d13c2549e451b31ed0480d7beeba1b70a7c67bbdee62b8c52aa037b7455373c"
            },
            {
                AgentSignalSchoolCatalog.HungerRouteId,
                2UL,
                "fbd70adf1f0adc8cc75ffe03ab0687e07c2431dce1d859aa7228b827540e94d5/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/6981ae4cd75de484d1bf46a5f607a63eba337a1f203a036b1791e2d758c54500"
            },
            {
                AgentSignalSchoolCatalog.ExitRouteId,
                1UL,
                "66c618524277a5681f566610451f664be35349c06f4c7bf799163316ab77e662/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/d809065b0ad9b05e78d047f102d68a2c76d2f29536cae9c4f17e500c12617da4"
            },
            {
                AgentSignalSchoolCatalog.ExitRouteId,
                2UL,
                "db6194ffb2d54e487bf0a005008eef471a6717debb36a7611a868b916f95cef5/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/56a7b94a36dda652d61456cd78e9a286777701b3fc94daacc9988843d1b8a4ea"
            },
            {
                AgentSignalSchoolCatalog.PowerRouteId,
                3UL,
                "8926695b6ca8dcbaf2b2b876e8e640f0d03a9eaa7921214d252bc9cd12daf955/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/2ab4e7b32689809072f22e5dc479f637be702ee83d24c70337cd8c100c20cc83"
            },
            {
                AgentSignalSchoolCatalog.PowerRouteId,
                2UL,
                "662b882b39c7b1d00e82dbdda8f773ba62a46e6a8d1a3aecde8fc8559bcba8fa/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/965c4b4278263fd0cbf9ed889fd6627a2c6658a975af3c37c591276bcc16af05"
            },
            {
                AgentSignalSchoolCatalog.RecoverRouteId,
                5UL,
                "70a06047d06dbf0c99ea3177ab7a4e50928e5136f56cece267212f2978ffcdc7/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/2f0d0e12c3f7358710dea3f4b9f782fb1c7b0aa67b1219a5b972f0a19e5017ee"
            },
            {
                AgentSignalSchoolCatalog.RecoverRouteId,
                6UL,
                "e188577f7918dff58f8de937a75f501a2a10d3307f84c76cb56f824b04116691/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/bb8c34ad03a1300d37ef8fbd4a5f354a2ca043b7d4b0598106e7564815e3ad0d"
            },
            {
                AgentSignalSchoolCatalog.ComboRouteId,
                1UL,
                "1aacb4499e4a8cba2a294c86871a2466ec3c53b78e69dc44a886502219c19910/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/6672b27692db6cf8331ca2e23b7a719564cf127ede2c93ae1ea174e3f92af0ba"
            },
            {
                AgentSignalSchoolCatalog.ComboRouteId,
                2UL,
                "52ae32633fdf13b4c2275493dc584859fb42905ad74814e8c15e3f1cf5f23a16/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/a0ed5d35bd1170f85646339d0c5f3b2349104c5512dfd16d405d426323097896"
            },
            {
                AgentSignalSchoolCatalog.DeathReadId,
                1UL,
                "432656ef88cb5f5f9931796d407c13891229b2085c03f87a91e971abe40e2c7c/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/701b254738112afce3a868cf29ecaec3b347cf2b66bccdd55ea76ac9a5b1b544"
            },
            {
                AgentSignalSchoolCatalog.DeathReadId,
                2UL,
                "b142aaa5d16b0e3ee9d7090ce2b40001fcab0a0f0906aeb8c82f80c1c94a284f/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/dae4850c4c49d0bd6700c2c803258bf34a5fcaa7180625c098aa663c52fedb53"
            },
        };

    private static void AssertWrapAndHungerFacts()
    {
        var wrap = Tracker(AgentSignalSchoolCatalog.WrapLineId, out var before);
        var deadAfterWrap = before with
        {
            Tick = 1,
            Status = RunStatus.Dead,
            DeathCause = DeathCause.SelfCollision,
        };
        wrap.RecordStep(
            before,
            Result(deadAfterWrap, new RunEventDetail(RunEventKind.Wrapped)),
            deadAfterWrap);
        AssertProgress(wrap, 1, "running_after_wrap");

        wrap = Tracker(AgentSignalSchoolCatalog.WrapLineId, out before);
        var runningAfterWrap = before with { Tick = 1 };
        wrap.RecordStep(
            before,
            Result(runningAfterWrap, new RunEventDetail(RunEventKind.Wrapped)),
            runningAfterWrap);
        AssertProgress(wrap, 2, null);

        var hunger = Tracker(AgentSignalSchoolCatalog.HungerRouteId, out before);
        var starvation = before with
        {
            Tick = 1,
            Status = RunStatus.Dead,
            DeathCause = DeathCause.Starvation,
        };
        hunger.RecordStep(
            before,
            Result(starvation, new RunEventDetail(RunEventKind.AteFood)),
            starvation);
        AssertProgress(hunger, 1, "food_before_starvation");

        hunger = Tracker(AgentSignalSchoolCatalog.HungerRouteId, out before);
        var fed = before with { Tick = 1 };
        hunger.RecordStep(before, Result(fed, new RunEventDetail(RunEventKind.AteFood)), fed);
        AssertProgress(hunger, 2, null);
    }

    private static void AssertExitFacts()
    {
        var tracker = Tracker(AgentSignalSchoolCatalog.ExitRouteId, out var before);
        before = before with
        {
            Body = [new GridPoint(1, 1)],
            Direction = Direction.Right,
            DetachedObstacles = Array.Empty<GridPoint>(),
        };
        var noGrowth = before with { Tick = 1 };
        tracker.RecordStep(
            before,
            Result(noGrowth, new RunEventDetail(RunEventKind.AteFood)),
            noGrowth);
        AssertProgress(tracker, 0, "food_growth");

        tracker = Tracker(AgentSignalSchoolCatalog.ExitRouteId, out before);
        before = before with
        {
            Body = [new GridPoint(1, 1)],
            Direction = Direction.Right,
        };
        var oneExit = before with
        {
            Tick = 1,
            Body = [new GridPoint(1, 1), new GridPoint(2, 1)],
            DetachedObstacles = [new GridPoint(3, 1), new GridPoint(2, 0)],
            DetachedObstacleTicksRemaining = 1,
        };
        tracker.RecordStep(
            before,
            Result(oneExit, new RunEventDetail(RunEventKind.AteFood)),
            oneExit);
        AssertProgress(tracker, 1, "two_structural_exits_after_growth");

        tracker = Tracker(AgentSignalSchoolCatalog.ExitRouteId, out before);
        before = before with
        {
            Body = [new GridPoint(1, 1)],
            Direction = Direction.Right,
        };
        var twoExits = before with
        {
            Tick = 1,
            Body = [new GridPoint(1, 1), new GridPoint(2, 1)],
            DetachedObstacles = [new GridPoint(3, 1)],
            DetachedObstacleTicksRemaining = 1,
        };
        tracker.RecordStep(
            before,
            Result(twoExits, new RunEventDetail(RunEventKind.AteFood)),
            twoExits);
        AssertProgress(tracker, 2, null);
    }

    private static void AssertPowerFacts()
    {
        var tracker = Tracker(AgentSignalSchoolCatalog.PowerRouteId, out var before);
        var after = before with { Tick = 1 };
        tracker.RecordStep(before, Result(
            after,
            new RunEventDetail(RunEventKind.PowerActivated, Power: PowerKind.Shield),
            new RunEventDetail(RunEventKind.PowerCollected, Power: PowerKind.Shield)), after);
        AssertProgress(tracker, 1, "same_power_activated");

        tracker = Tracker(AgentSignalSchoolCatalog.PowerRouteId, out before);
        after = before with { Tick = 1 };
        tracker.RecordStep(before, Result(
            after,
            new RunEventDetail(RunEventKind.PowerCollected, Power: PowerKind.Shield),
            new RunEventDetail(RunEventKind.PowerActivated, Power: PowerKind.Boost)), after);
        AssertProgress(tracker, 1, "same_power_activated");

        tracker = Tracker(AgentSignalSchoolCatalog.PowerRouteId, out before);
        after = before with { Tick = 1 };
        tracker.RecordStep(before, Result(
            after,
            new RunEventDetail(RunEventKind.PowerCollected, Power: PowerKind.Shield),
            new RunEventDetail(RunEventKind.PowerActivated, Power: PowerKind.Shield)), after);
        AssertProgress(tracker, 2, null);
    }

    private static void AssertRecoveryFacts()
    {
        var tracker = Tracker(AgentSignalSchoolCatalog.RecoverRouteId, out var before);
        var dead = before with
        {
            Tick = 1,
            Status = RunStatus.Dead,
            DeathCause = DeathCause.SelfCollision,
        };
        tracker.RecordStep(before, Result(
            dead,
            new RunEventDetail(RunEventKind.CollisionPrevented,
                Cause: DeathCause.SelfCollision,
                Power: PowerKind.Shield)), dead);
        AssertProgress(tracker, 1, "running_after_recovery");

        tracker = Tracker(AgentSignalSchoolCatalog.RecoverRouteId, out before);
        var running = before with { Tick = 1 };
        tracker.RecordStep(before, Result(
            running,
            new RunEventDetail(RunEventKind.CollisionPrevented,
                Cause: DeathCause.SelfCollision,
                Power: PowerKind.Shield)), running);
        AssertProgress(tracker, 2, null);

        tracker = Tracker(AgentSignalSchoolCatalog.RecoverRouteId, out before);
        tracker.RecordStep(before, Result(
            running,
            new RunEventDetail(RunEventKind.CollisionPrevented, Cause: DeathCause.None)), running);
        AssertProgress(tracker, 0, "collision_prevented");
    }

    private static void AssertComboFacts()
    {
        var tracker = Tracker(AgentSignalSchoolCatalog.ComboRouteId, out var before);
        for (var combo = 1; combo <= 2; combo++)
        {
            var after = before with { Tick = before.Tick + 1, ComboCount = combo };
            tracker.RecordStep(
                before,
                Result(after, new RunEventDetail(RunEventKind.AteFood)),
                after);
            before = after;
        }
        AssertProgress(tracker, 0, "three_food");

        var completed = before with { Tick = before.Tick + 1, ComboCount = 3 };
        tracker.RecordStep(
            before,
            Result(completed, new RunEventDetail(RunEventKind.AteFood)),
            completed);
        AssertProgress(tracker, 2, null);
    }

    private static void AssertDeathFacts()
    {
        var tracker = Tracker(AgentSignalSchoolCatalog.DeathReadId, out var before);
        var dead = before with
        {
            Tick = 1,
            Status = RunStatus.Dead,
            DeathCause = DeathCause.Starvation,
        };
        tracker.RecordStep(before, Result(
            dead,
            new RunEventDetail(RunEventKind.Died, Cause: DeathCause.SelfCollision)), dead);
        AssertProgress(tracker, 1, "matching_death_event");

        tracker = Tracker(AgentSignalSchoolCatalog.DeathReadId, out before);
        tracker.RecordStep(before, Result(
            dead,
            new RunEventDetail(RunEventKind.Died, Cause: DeathCause.Starvation)), dead);
        AssertProgress(tracker, 2, null);

        tracker = Tracker(AgentSignalSchoolCatalog.DeathReadId, out before);
        var running = before with { Tick = 1 };
        tracker.RecordStep(before, Result(
            running,
            new RunEventDetail(RunEventKind.Died, Cause: DeathCause.Starvation)), running);
        AssertProgress(tracker, 0, "terminal_death");
    }

    private static AgentLessonEvidenceTracker Tracker(
        string lessonId,
        out RunSnapshot snapshot)
    {
        var definition = AgentSignalSchoolCatalog.Get(lessonId);
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Get(
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion));
        snapshot = SnakeRun.Create(definition.PracticeSeed, config).GetSnapshot();
        return new AgentLessonEvidenceTracker(lessonId, config);
    }

    private static RunStepResult Result(
        RunSnapshot after,
        params RunEventDetail[] events) =>
        new(after.Tick, RunEvent.None, events, after.Status, after.DeathCause, after.StateHash);

    private static AgentMatchSession CreateSession(
        AgentSignalLessonDefinitionV2 definition,
        string actionProfile = AgentPassportV4.FourDirectionActionProfile) =>
        new(new AgentMatchOptions(
            $"evidence-{definition.Id}-{actionProfile}",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            actionProfile: actionProfile,
            lessonId: definition.Id));

    private static AgentActionRequest Request(
        string key,
        AgentObservationV5 observation,
        AgentAction action) =>
        new(key, observation.Tick, observation.StateHash, action);

    private static void AssertNoAttemptEvidence(AgentLessonProgressV3? progress)
    {
        Assert.NotNull(progress);
        Assert.Equal(0, progress.AttemptEvidenceCount);
        Assert.Equal("opposite_reversal_rejected", progress.FirstUnmetRequirementId);
    }

    private static void AssertProgress(
        AgentLessonEvidenceTracker tracker,
        int requirementsSatisfied,
        string? firstUnmet)
    {
        var progress = tracker.Snapshot(
            AgentLessonEvidenceState.Live,
            AgentPassportV4.FourDirectionActionProfile);
        Assert.Equal(requirementsSatisfied, progress.RequirementsSatisfied);
        Assert.Equal(firstUnmet, progress.FirstUnmetRequirementId);
        Assert.Equal(requirementsSatisfied == 2, progress.AllRequirementsSatisfied);
        Assert.True(AgentSignalSchoolCatalog.IsValidProgress(progress));
    }

    private static void AssertRetry(
        AgentLessonRetryDescriptorV1 retry,
        string lessonId,
        string actionProfile)
    {
        Assert.Equal(AgentLessonRetryDescriptorV1.Contract, retry.Schema);
        Assert.Equal("start_lesson", retry.Tool);
        Assert.Equal(lessonId, retry.LessonId);
        Assert.Equal(actionProfile, retry.ActionProfile);
        Assert.True(retry.FreshSessionRequired);
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string AlternateHash(string value) =>
        value[0] == '0' ? $"1{value[1..]}" : $"0{value[1..]}";
}
