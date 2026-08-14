using System.Security.Cryptography;
using System.Text;
using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentLessonEvidenceTests
{
    [Fact]
    public void Empty_and_partial_outcomes_name_the_first_unmet_requirement_and_fresh_retry()
    {
        foreach (var definition in AgentSignalSchoolCatalog.All)
        {
            var result = CreateSession(definition).Finish();
            var outcome = Assert.IsType<AgentLessonOutcomeV2>(result.LessonOutcome);

            Assert.False(outcome.AllRequirementsSatisfied);
            Assert.Equal(0, outcome.RequirementsSatisfied);
            Assert.Equal(definition.Requirements[0].Id, outcome.FirstUnmetRequirementId);
            Assert.Equal(
                definition.Requirements[0].EvidenceSource == AgentLessonEvidenceSource.AttemptWitness
                    ? AgentLessonReviewCode.InsufficientAttemptEvidence
                    : AgentLessonReviewCode.ReplayRequirementUnmet,
                outcome.ReviewCode);
            Assert.Equal(AgentMatchEndReason.AgentFinished, outcome.EndReason);
            Assert.True(AgentSignalSchoolCatalog.IsValidOutcome(outcome));
            AssertRetry(outcome.RetryDescriptor, definition.Id, AgentPassportV4.FourDirectionActionProfile);
        }

        var firstTurn = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var partial = CreateSession(firstTurn);
        var initial = partial.Observe();
        var rejected = partial.SubmitAction(Request("partial-reversal", initial, AgentAction.Left));
        var partialOutcome = Assert.IsType<AgentLessonOutcomeV2>(partial.Finish().LessonOutcome);

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
        AgentLessonProgressV2?[] invalidProgress =
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

        var outcome = Assert.IsType<AgentLessonOutcomeV2>(session.Finish().LessonOutcome);
        Assert.True(AgentSignalSchoolCatalog.IsValidOutcome(outcome));
        AgentLessonOutcomeV2?[] invalidOutcomes =
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
            outcome with { RetryDescriptor = outcome.RetryDescriptor with { Tool = "get_match" } },
            outcome with { RetryDescriptor = outcome.RetryDescriptor with { FreshSessionRequired = false } },
        ];
        Assert.All(invalidOutcomes, value => Assert.False(AgentSignalSchoolCatalog.IsValidOutcome(value)));
        var wrapOutcome = Assert.IsType<AgentLessonOutcomeV2>(CreateSession(
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
                "81f91792eea8f6ceac32f310e9ea14f8d97805ebf92600898390b08aab25725c/19ea14c871b657140e3bd8958d59f940e17dd48015382ab12f3065eaac4c873f/9622f1da2758883893e1b078e56d82f375cd51db8718684bdde228f5cc57eca2"
            },
            {
                AgentSignalSchoolCatalog.FirstTurnId,
                2UL,
                "39e0977508495f1563521a374fc568ad90c18c2e9e2bbe97f26338e4e7e32635/2b0c22ba885bb50f5a1ca3d730c50afbde61f1ebdbd5c3c88029a049dfdc01b0/8cdec6a6a88d2b81f39bc0f66e13fe282236ee9891b34ce3eccfa9c93e6cec46"
            },
            {
                AgentSignalSchoolCatalog.WrapLineId,
                1UL,
                "3d1a648a1a5b6e3cec1694a0bd05d9ac82426eb3a9b46b4a1bc7c2dce945ce91/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/f425552966d7d610109da5bfe3f43a23f31fadfd1bacc4e5ecd562c9fb88920c"
            },
            {
                AgentSignalSchoolCatalog.WrapLineId,
                2UL,
                "e95dfeebfbf19c40de15395e6afaad1c1b683d1705b6218172665b55e9421163/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/1ce7b48744bd7ba1f7767d2c5791404ab5fe407d0ee356661d32eae99342c3ec"
            },
            {
                AgentSignalSchoolCatalog.HungerRouteId,
                1UL,
                "4dc0cc595affb1f4bb05c52daa2875bfd6a35cb779dc3eb9d51f3dc0e17ce3fe/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/03f8c3fee2da2a26febc2a528f5cdca2a264f2664f0c44e874b209ff9b86e715"
            },
            {
                AgentSignalSchoolCatalog.HungerRouteId,
                2UL,
                "c3dc636819c387f4595138e9945a92bc498f2b94771707209d729da0cf73ab1a/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/44fcffcf02c089ee1c0730d0ec95d2c02071cc4fe4e0d8d0414437fa1b922a39"
            },
            {
                AgentSignalSchoolCatalog.ExitRouteId,
                1UL,
                "055edcb949ff279ea0af08c6b03cd677150bac3f2d4dd12478a8f7c71d9fdc7e/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/a079c59c22a8a87cb437ccd0b02fba3d9755dfff38288691a614434d56aff564"
            },
            {
                AgentSignalSchoolCatalog.ExitRouteId,
                2UL,
                "408b7e8b16c0c544b44aab8df7142161e650c6384800b1679769cd51327af368/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/1cfa0d6cb18767f851f8a5d6206d8e2d21d21b1435e7ceebf6c4415d4eddc8dc"
            },
            {
                AgentSignalSchoolCatalog.PowerRouteId,
                3UL,
                "98c18b0ac8184eaa8276b13964dbcc054af9a1ef6b88349c5c67e6d2888bedef/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/d89fc3ee4ad055936e66c8738c7227b6a5fe6c3bce20ce681c990dea53827c29"
            },
            {
                AgentSignalSchoolCatalog.PowerRouteId,
                2UL,
                "165d21703978af5bcafa050c9de4fee1419ec1e02800107a6859862d4d49a281/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/13127b2a99b94952ecb3b2fe931a605595804b92a93ccbb198a02d2fce0e18ab"
            },
            {
                AgentSignalSchoolCatalog.RecoverRouteId,
                5UL,
                "785e32811d871175b7f89f89d2dcb5541c73169d8ad4420f0d120cbb7b250d39/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/cc503871fbf34519303fe49c659bbf46c1f838b80e282ff397fba30569047c95"
            },
            {
                AgentSignalSchoolCatalog.RecoverRouteId,
                6UL,
                "de0ae19c29aa567650d1f6fcc5ac8177af38c98a58795f8e1182b432079a3c35/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/c415f2ce8403d5a4c1017289f7a5de258a18e332596ceaf7327b562cb5a39420"
            },
            {
                AgentSignalSchoolCatalog.ComboRouteId,
                1UL,
                "263adaf48903347909c6eaec639534078d86336da4e6bfe8079568424900d1c7/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/86c9a1c5e4404ef57000ee566c33fda407ac333e588aecb0a8c7ad4be0f58de4"
            },
            {
                AgentSignalSchoolCatalog.ComboRouteId,
                2UL,
                "9a10b23e10d79881d9452c2896638e3ce3e8dd5f3ae760d752174f6c03325828/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/763bace5f417ff7854fbbe22efe4aea9583b541bd48a63349fe8e72cea3ce89b"
            },
            {
                AgentSignalSchoolCatalog.DeathReadId,
                1UL,
                "31d98a3f442d75836b8b94f078f8f686fb75fc7a298ef5110c917b6b202b232a/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/929caad36a23c3b3280fa996adbcb69762be1c73a56070de4a273ed00b3f21f6"
            },
            {
                AgentSignalSchoolCatalog.DeathReadId,
                2UL,
                "d67c04adb8b9a06bac3194c02e312094be14457a49c0a850320322e0affb5b26/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/dd46084b5eb01dd9e1cb9c080007f220fd0c880ccb30c8abb24d04c1ee6680a5"
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

    private static void AssertNoAttemptEvidence(AgentLessonProgressV2? progress)
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
