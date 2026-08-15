using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentStyleContractValidationTests
{
    [Fact]
    public void Progress_validation_rejects_each_catalog_and_summary_contradiction()
    {
        var valid = ValidProgress(AgentStyleContractCatalog.StillwaterId);

        Assert.True(AgentStyleContractCatalog.IsValidProgress(valid));
        Assert.False(AgentStyleContractCatalog.IsValidProgress(null));
        Assert.False(AgentStyleContractCatalog.IsValidProgress(valid with { Schema = "wrong" }));
        Assert.False(AgentStyleContractCatalog.IsValidProgress(valid with { ContractId = "unknown" }));
        Assert.False(AgentStyleContractCatalog.IsValidProgress(valid with { DisplayName = "Wrong" }));
        Assert.False(AgentStyleContractCatalog.IsValidProgress(
            valid with { EvaluationPolicyId = "wrong" }));
        Assert.False(AgentStyleContractCatalog.IsValidProgress(valid with { Criteria = [] }));
        Assert.False(AgentStyleContractCatalog.IsValidProgress(
            valid with { ThresholdsReached = 1 }));
        Assert.False(AgentStyleContractCatalog.IsValidProgress(
            valid with { AllThresholdsReached = false }));
    }

    [Fact]
    public void Progress_validation_rejects_each_criterion_identity_and_state_contradiction()
    {
        var valid = ValidProgress(AgentStyleContractCatalog.StillwaterId);
        var first = valid.Criteria[0];

        AssertInvalidFirstCriterion(valid, null!);
        AssertInvalidFirstCriterion(valid, first with { CriterionId = "wrong" });
        AssertInvalidFirstCriterion(valid, first with { DisplayName = "Wrong" });
        AssertInvalidFirstCriterion(
            valid,
            first with { Comparator = (AgentStyleCriterionComparator)byte.MaxValue });
        AssertInvalidFirstCriterion(valid, first with { Unit = AgentStyleCriterionUnit.BasisPoints });
        AssertInvalidFirstCriterion(valid, first with { Target = first.Target + 1 });
        AssertInvalidFirstCriterion(valid, first with { Current = -1, ThresholdReached = false });
        AssertInvalidFirstCriterion(valid, first with { ThresholdReached = false });
        AssertInvalidFirstCriterion(valid, first with { Numerator = 1 });
    }

    [Fact]
    public void Basis_point_validation_rejects_malformed_and_overflowing_evidence()
    {
        var valid = ValidProgress(AgentStyleContractCatalog.StillwaterId);
        var rate = valid.Criteria[1];

        AssertInvalidSecondCriterion(valid, rate with { Unit = (AgentStyleCriterionUnit)byte.MaxValue });
        AssertInvalidSecondCriterion(valid, rate with { Numerator = null });
        AssertInvalidSecondCriterion(valid, rate with { Denominator = null });
        AssertInvalidSecondCriterion(valid, rate with { Numerator = -1 });
        AssertInvalidSecondCriterion(valid, rate with { Denominator = -1 });
        AssertInvalidSecondCriterion(valid, rate with { Numerator = 101, Denominator = 100 });
        AssertInvalidSecondCriterion(valid, rate with { Current = rate.Current - 1 });
        AssertInvalidSecondCriterion(
            valid,
            rate with
            {
                Current = int.MaxValue,
                Numerator = long.MaxValue,
                Denominator = long.MaxValue,
            });

        var zeroRate = rate with
        {
            Current = 0,
            Numerator = 0,
            Denominator = 0,
            ThresholdReached = false,
        };
        var zeroProgress = valid with
        {
            Criteria = Array.AsReadOnly([valid.Criteria[0], zeroRate]),
            ThresholdsReached = 1,
            AllThresholdsReached = false,
        };
        Assert.True(AgentStyleContractCatalog.IsValidProgress(zeroProgress));
    }

    [Fact]
    public void Outcome_validation_requires_lowercase_replay_hash_and_valid_evidence()
    {
        var progress = ValidProgress(AgentStyleContractCatalog.StillwaterId);
        var valid = Outcome(progress, new string('a', 64));

        Assert.True(AgentStyleContractCatalog.IsValidOutcome(valid));
        Assert.False(AgentStyleContractCatalog.IsValidOutcome(null));
        Assert.False(AgentStyleContractCatalog.IsValidOutcome(valid with { Schema = "wrong" }));
        Assert.False(AgentStyleContractCatalog.IsValidOutcome(valid with { ReplayPayloadHash = null! }));
        Assert.False(AgentStyleContractCatalog.IsValidOutcome(valid with { ReplayPayloadHash = "abc" }));
        Assert.False(AgentStyleContractCatalog.IsValidOutcome(
            valid with { ReplayPayloadHash = new string('A', 64) }));
        Assert.False(AgentStyleContractCatalog.IsValidOutcome(
            valid with { ReplayPayloadHash = new string('g', 64) }));
        Assert.False(AgentStyleContractCatalog.IsValidOutcome(valid with { ContractId = "unknown" }));
    }

    [Fact]
    public void Progress_and_outcome_value_semantics_include_every_published_field()
    {
        var progress = ValidProgress(AgentStyleContractCatalog.StillwaterId);
        Assert.True(progress.Equals(progress with { }));
        Assert.False(progress.Equals(null));
        Assert.False(progress.Equals(progress with { Schema = "wrong" }));
        Assert.False(progress.Equals(progress with { ContractId = "wrong" }));
        Assert.False(progress.Equals(progress with { DisplayName = "Wrong" }));
        Assert.False(progress.Equals(progress with { EvaluationPolicyId = "wrong" }));
        Assert.False(progress.Equals(progress with { Criteria = [] }));
        Assert.False(progress.Equals(progress with { ThresholdsReached = 1 }));
        Assert.False(progress.Equals(progress with { AllThresholdsReached = false }));
        Assert.Equal(progress.GetHashCode(), (progress with { }).GetHashCode());

        var outcome = Outcome(progress, new string('a', 64));
        Assert.True(outcome.Equals(outcome with { }));
        Assert.False(outcome.Equals(null));
        Assert.False(outcome.Equals(outcome with { Schema = "wrong" }));
        Assert.False(outcome.Equals(outcome with { ContractId = "wrong" }));
        Assert.False(outcome.Equals(outcome with { DisplayName = "Wrong" }));
        Assert.False(outcome.Equals(outcome with { EvaluationPolicyId = "wrong" }));
        Assert.False(outcome.Equals(outcome with { Criteria = [] }));
        Assert.False(outcome.Equals(outcome with { ThresholdsReached = 1 }));
        Assert.False(outcome.Equals(outcome with { AllThresholdsReached = false }));
        Assert.False(outcome.Equals(outcome with { ReplayPayloadHash = new string('b', 64) }));
        Assert.Equal(outcome.GetHashCode(), (outcome with { }).GetHashCode());
    }

    [Fact]
    public void Replay_equivalence_compares_every_progress_field()
    {
        var progress = ValidProgress(AgentStyleContractCatalog.StillwaterId);

        Assert.True(AgentStyleEvidenceReplayEvaluator.Equivalent(progress, progress with { }));
        Assert.False(AgentStyleEvidenceReplayEvaluator.Equivalent(
            progress,
            progress with { Schema = "wrong" }));
        Assert.False(AgentStyleEvidenceReplayEvaluator.Equivalent(
            progress,
            progress with { ContractId = "wrong" }));
        Assert.False(AgentStyleEvidenceReplayEvaluator.Equivalent(
            progress,
            progress with { DisplayName = "Wrong" }));
        Assert.False(AgentStyleEvidenceReplayEvaluator.Equivalent(
            progress,
            progress with { EvaluationPolicyId = "wrong" }));
        Assert.False(AgentStyleEvidenceReplayEvaluator.Equivalent(
            progress,
            progress with { ThresholdsReached = 1 }));
        Assert.False(AgentStyleEvidenceReplayEvaluator.Equivalent(
            progress,
            progress with { AllThresholdsReached = false }));
        Assert.False(AgentStyleEvidenceReplayEvaluator.Equivalent(
            progress,
            progress with { Criteria = [] }));
        Assert.Throws<ArgumentNullException>(() =>
            AgentStyleEvidenceReplayEvaluator.Equivalent(null!, progress));
        Assert.Throws<ArgumentNullException>(() =>
            AgentStyleEvidenceReplayEvaluator.Equivalent(progress, null!));
    }

    [Fact]
    public void Structural_evidence_honors_pending_direction_food_growth_and_gluttony()
    {
        var config = new RunConfig(Width: 5, Height: 5, PowerSpawnIntervalTicks: 0);
        var initial = SnakeRun.Create(1UL, config).GetSnapshot();
        var snapshot = initial with
        {
            Direction = Direction.Right,
            PendingDirections = [Direction.Right],
            Body = [new GridPoint(2, 1), new GridPoint(2, 2)],
            Food = new GridPoint(2, 1),
            DetachedObstacles = [new GridPoint(3, 2), new GridPoint(2, 3)],
            DetachedObstacleTicksRemaining = 2,
        };

        Assert.Equal(0, AgentStyleEvidenceMath.StructuralOpenExitCount(config, snapshot));
        Assert.Equal(
            1,
            AgentStyleEvidenceMath.StructuralOpenExitCount(
                config,
                snapshot with { GluttonyTicksRemaining = 2 }));
        Assert.Equal(
            0,
            AgentStyleEvidenceMath.StructuralOpenExitCount(
                config,
                snapshot with
                {
                    GluttonyTicksRemaining = 2,
                    Body =
                    [
                        new GridPoint(2, 1),
                        new GridPoint(2, 1),
                        new GridPoint(2, 2),
                    ],
                }));
        Assert.Equal(
            0,
            AgentStyleEvidenceMath.StructuralOpenExitCount(
                config,
                snapshot with
                {
                    GluttonyTicksRemaining = 2,
                    Body =
                    [
                        new GridPoint(2, 1),
                        new GridPoint(2, 1),
                        new GridPoint(2, 2),
                    ],
                    DetachedObstacles =
                    [
                        new GridPoint(2, 1),
                        new GridPoint(3, 2),
                        new GridPoint(2, 3),
                    ],
                }));
        Assert.Equal(
            0,
            AgentStyleEvidenceMath.StructuralOpenExitCount(config, snapshot with { Body = [] }));
        Assert.Throws<ArgumentNullException>(() =>
            AgentStyleEvidenceMath.StructuralOpenExitCount(null!, snapshot));
        Assert.Throws<ArgumentNullException>(() =>
            AgentStyleEvidenceMath.StructuralOpenExitCount(config, null!));
    }

    [Fact]
    public void Wrapped_distance_rejects_each_out_of_board_axis()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentStyleEvidenceMath.WrappedManhattanDistance(
                new GridPoint(0, 0),
                new GridPoint(5, 0),
                5,
                5));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentStyleEvidenceMath.WrappedManhattanDistance(
                new GridPoint(0, 0),
                new GridPoint(0, 5),
                5,
                5));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentStyleEvidenceMath.WrappedManhattanDistance(
                new GridPoint(0, 0),
                new GridPoint(0, 0),
                5,
                0));
    }

    private static void AssertInvalidFirstCriterion(
        AgentStyleProgressV3 valid,
        AgentStyleCriterionProgressV3 criterion)
    {
        Assert.False(AgentStyleContractCatalog.IsValidProgress(
            valid with
            {
                Criteria = Array.AsReadOnly([criterion, valid.Criteria[1]]),
            }));
    }

    private static void AssertInvalidSecondCriterion(
        AgentStyleProgressV3 valid,
        AgentStyleCriterionProgressV3 criterion)
    {
        Assert.False(AgentStyleContractCatalog.IsValidProgress(
            valid with
            {
                Criteria = Array.AsReadOnly([valid.Criteria[0], criterion]),
            }));
    }

    private static AgentStyleProgressV3 ValidProgress(string contractId)
    {
        var definition = AgentStyleContractCatalog.Get(contractId);
        var criteria = definition.Criteria.Select(ValidCriterion).ToArray();
        return new AgentStyleProgressV3(
            AgentStyleProgressV3.Contract,
            definition.Id,
            definition.DisplayName,
            definition.EvaluationPolicyId,
            Array.AsReadOnly(criteria),
            criteria.Length,
            AllThresholdsReached: true);
    }

    private static AgentStyleCriterionProgressV3 ValidCriterion(
        AgentStyleCriterionDefinitionV2 definition)
    {
        var (numerator, denominator) = definition.Target switch
        {
            9_900 => (99L, 100L),
            10_000 => (1L, 1L),
            6_500 => (13L, 20L),
            _ => ((long?)null, (long?)null),
        };
        return new AgentStyleCriterionProgressV3(
            definition.Id,
            definition.DisplayName,
            definition.Comparator,
            definition.Unit,
            definition.Target,
            definition.Target,
            numerator,
            denominator,
            ThresholdReached: true);
    }

    private static AgentStyleOutcomeV3 Outcome(
        AgentStyleProgressV3 progress,
        string replayPayloadHash) =>
        new(
            AgentStyleOutcomeV3.Contract,
            progress.ContractId,
            progress.DisplayName,
            progress.EvaluationPolicyId,
            progress.Criteria,
            progress.ThresholdsReached,
            progress.AllThresholdsReached,
            replayPayloadHash);
}
