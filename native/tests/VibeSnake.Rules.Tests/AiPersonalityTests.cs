namespace VibeSnake.Rules.Tests;

public sealed class AiPersonalityTests
{
    [Fact]
    public void Built_in_catalog_matches_the_frozen_ten_personality_contract()
    {
        Assert.Equal(10, AiPersonalityCatalog.BuiltIn.Count);
        Assert.Equal(
            [
                "speed_demon",
                "coward",
                "greedy",
                "power_hunter",
                "drunk",
                "optimal",
                "yolo",
                "balanced",
                "wall_hugger",
                "zen_master",
            ],
            AiPersonalityCatalog.BuiltIn.Select(personality => personality.Id));
        Assert.All(AiPersonalityCatalog.BuiltIn, personality => personality.Validate());
        Assert.Equal(100, AiPersonalityCatalog.GetBuiltIn("greedy").Greed);
        Assert.Equal(
            new AiDisplayColor(255, 0, 255),
            AiPersonalityCatalog.GetBuiltIn("power_hunter").Color);
    }

    [Fact]
    public void Catalog_and_trait_contracts_reject_unknown_or_invalid_values()
    {
        Assert.Throws<ArgumentException>(() => AiPersonalityCatalog.GetBuiltIn("unknown"));
        Assert.Throws<ArgumentException>(() => AiPersonalityCatalog.GetBuiltIn(" "));

        var personality = AiPersonalityCatalog.GetBuiltIn("balanced");
        Assert.Equal(50, personality.GetTrait(AiPersonalityTrait.Greed));
        Assert.Equal(75, personality.WithTrait(AiPersonalityTrait.Greed, 75).Greed);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            personality.WithTrait(AiPersonalityTrait.Chaos, 101));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            personality.GetTrait((AiPersonalityTrait)byte.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            personality.WithTrait((AiPersonalityTrait)byte.MaxValue, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (personality with { Aggression = -1 }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (personality with { Id = string.Empty }).Validate());
    }

    [Fact]
    public void Controller_is_deterministic_and_reports_decision_diagnostics()
    {
        var personality = AiPersonalityCatalog.GetBuiltIn("balanced");
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe) with
        {
            Width = 10,
            Height = 8,
            PowerSpawnIntervalTicks = 5,
            PowerVisibleTicks = 20,
        };
        var left = SnakeRun.Create(42UL, config);
        var right = SnakeRun.Create(42UL, config);
        var leftController = new AiPersonalityController(personality, 99UL);
        var rightController = new AiPersonalityController(personality, 99UL);

        for (var step = 0; step < 40 && left.Status == RunStatus.Running; step++)
        {
            var leftDecision = leftController.SelectDecision(left);
            var rightDecision = rightController.SelectDecision(right);
            Assert.Equal(leftDecision, rightDecision);
            Assert.InRange(leftDecision.LegalChoiceCount, 1, 3);
            Assert.InRange(leftDecision.SafeChoiceCount, 0, leftDecision.LegalChoiceCount);
            Assert.InRange(leftDecision.HazardNeighborCount, 0, 4);
            Assert.InRange(leftDecision.OnwardChoiceCount, 0, 3);

            left.QueueDirection(leftDecision.Direction);
            right.QueueDirection(rightDecision.Direction);
            Assert.Equal(left.Step().StateHash, right.Step().StateHash);
        }

        Assert.Equal(left.ComputeStateHash(), right.ComputeStateHash());
    }

    [Fact]
    public void Controller_rejects_invalid_personality_and_terminal_state()
    {
        var invalid = AiPersonalityCatalog.GetBuiltIn("balanced") with { Chaos = 101 };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AiPersonalityController(invalid, 1UL));

        var run = SnakeRun.CreateForTesting(
            new RunConfig(Width: 3, Height: 2),
            [new GridPoint(0, 0), new GridPoint(1, 0)],
            Direction.Right,
            new GridPoint(2, 1),
            hungerTicksRemaining: 1);
        run.Step();
        Assert.NotEqual(RunStatus.Running, run.Status);
        var controller = new AiPersonalityController(
            AiPersonalityCatalog.GetBuiltIn("balanced"),
            1UL);
        Assert.Throws<InvalidOperationException>(() => controller.SelectDecision(run));
    }
}
