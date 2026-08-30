using System.Text.Json;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// Native parity against the reviewed Python-origin terminal achievement_candidate
/// corpus with the product flag enabled. Default core_rules fixtures keep the flag
/// off (PD-009).
/// </summary>
public sealed class SharedAchievementCandidateTraceParityTests
{
    [Fact]
    public void Csharp_matches_reviewed_python_origin_achievement_candidate_traces()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "achievement_candidates_rules_v1.json");
        var fixture = JsonSerializer.Deserialize<AchievementFixture>(
            File.ReadAllText(fixturePath),
            TestJsonSerializerOptions.SnakeCase);

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.SchemaVersion);
        Assert.Equal("achievement-candidates-targeted-v1", fixture.Contract);
        Assert.Equal(SnakeRun.RulesetId, fixture.Ruleset.Id);
        Assert.Equal(SnakeRun.RulesVersion, fixture.Ruleset.Version);
        Assert.Equal(
            "positions-injected-or-random-output-normalized-v2",
            fixture.RandomnessPolicy);
        Assert.Equal("python-core-reference-v3", fixture.SourceEngine);
        Assert.Equal(
            [
                "terminal_achievement_candidates",
                "already_unlocked_suppression",
                "ordered_events",
            ],
            fixture.ComparisonScope);
        Assert.Equal(
            ["default_flag_off_corpus", "profile_lifetime_achievements"],
            fixture.ExcludedScope);
        Assert.True(fixture.Config.EnableAchievementCandidates);
        Assert.Equal(64, fixture.Config.Width);
        Assert.Equal(33, fixture.Config.Height);
        Assert.Equal(600, fixture.Config.StarvationTicks);
        Assert.Equal(3, fixture.Config.MaximumDirectionQueue);
        Assert.Equal(SnakeRun.MaximumScore, fixture.Config.MaximumScore);
        Assert.Equal(60, fixture.Config.ComboWindowTicks);
        Assert.Equal(30, fixture.Config.SpeedBonusTicks);
        Assert.Equal(10, fixture.Config.FoodScore);
        Assert.Equal(4, fixture.CaseCount);
        Assert.Equal(fixture.CaseCount, fixture.Cases.Count);
        Assert.Equal(
            [
                "starvation-score-candidates",
                "starvation-suppresses-already-unlocked",
                "starvation-zero-score-no-candidates",
                "self-collision-score-candidates",
            ],
            fixture.Cases.Select(item => item.Id));
        Assert.All(fixture.Cases, traceCase => Assert.Empty(traceCase.Commands));

        foreach (var traceCase in fixture.Cases)
        {
            ExecuteCase(fixture.Config, traceCase);
        }
    }

    private static void ExecuteCase(AchievementConfig fixtureConfig, AchievementCase traceCase)
    {
        var config = new RunConfig(
            Width: fixtureConfig.Width,
            Height: fixtureConfig.Height,
            StarvationTicks: fixtureConfig.StarvationTicks,
            MaximumDirectionQueue: fixtureConfig.MaximumDirectionQueue,
            FoodScore: fixtureConfig.FoodScore,
            ComboWindowTicks: fixtureConfig.ComboWindowTicks,
            SpeedBonusTicks: fixtureConfig.SpeedBonusTicks,
            EnableNearMiss: true,
            EnableComboExpiredEvent: true,
            EnableAchievementCandidates: true);
        var initial = traceCase.Initial;
        var run = SnakeRun.CreateForTesting(
            config,
            initial.Body.Select(ToGridPoint),
            ParseDirection(initial.Direction),
            initial.Food is null ? null : ToGridPoint(initial.Food),
            fixtureConfig.StarvationTicks - initial.StarvationTicksElapsed,
            score: initial.Score,
            comboCount: initial.Combo,
            ticksSinceLastFood: initial.TicksSinceLastFood);

        if (initial.AlreadyUnlocked is { Count: > 0 })
        {
            run.ApplyProfileUnlocks(new HashSet<string>(initial.AlreadyUnlocked, StringComparer.Ordinal));
        }

        var result = run.Step();
        var snapshot = run.GetSnapshot();
        var expected = traceCase.Expected;
        var expectedState = new
        {
            expected.Tick,
            Direction = expected.Direction.ToUpperInvariant(),
            Head = expected.Head,
            expected.Body,
            expected.Score,
            expected.Alive,
            expected.Won,
            expected.DeathCause,
        };
        var actualState = new
        {
            snapshot.Tick,
            Direction = snapshot.Direction.ToString().ToUpperInvariant(),
            Head = new[] { snapshot.Head.X, snapshot.Head.Y },
            Body = snapshot.Body.Select(point => new[] { point.X, point.Y }).ToList(),
            snapshot.Score,
            Alive = snapshot.Status == RunStatus.Running,
            Won = snapshot.Status == RunStatus.Won,
            DeathCause = NormalizeDeathCause(snapshot.DeathCause),
        };
        var actualEvents = result.OrderedEvents.Select(NormalizeEvent).ToList();
        if (
            !ParityDivergence.AreEquivalent(expectedState, actualState)
            || !ParityDivergence.AreEquivalent(expected.Events, actualEvents))
        {
            ParityDivergence.ThrowWithBundle(
                new ParityDivergenceRequest(
                    Contract: "achievement-candidates-targeted-v1",
                    Fixture: "achievement_candidates_rules_v1.json",
                    TestFilter:
                        "VibeSnake.Rules.Tests.SharedAchievementCandidateTraceParityTests."
                        + "Csharp_matches_reviewed_python_origin_achievement_candidate_traces",
                    CaseId: traceCase.Id,
                    Seed: null,
                    FirstDivergentStep: expected.Tick,
                    InitialState: traceCase.Initial,
                    CommandPrefix: Array.Empty<string>(),
                    ExpectedState: expectedState,
                    ExpectedEvents: expected.Events,
                    ActualState: actualState,
                    ActualEvents: actualEvents,
                    ActualCanonicalState: JsonSerializer.Deserialize<JsonElement>(
                        run.SerializeCanonicalState()),
                    ActualStateHash: result.StateHash));
        }
    }

    private static AchievementEvent NormalizeEvent(RunEventDetail detail) => new(
        RulesEventCatalog.ToWireName(detail.Kind),
        detail.Position is { } position ? [position.X, position.Y] : null,
        detail.NewDirection?.ToString().ToUpperInvariant(),
        detail.Value,
        detail.Cause is { } cause ? NormalizeDeathCause(cause) : null);

    private static string? NormalizeDeathCause(DeathCause cause) => cause switch
    {
        DeathCause.None => null,
        DeathCause.SelfCollision => "self_collision",
        DeathCause.Starvation => "starvation",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unknown death cause."),
    };

    private static Direction ParseDirection(string value) =>
        Enum.Parse<Direction>(value, ignoreCase: true);

    private static GridPoint ToGridPoint(IReadOnlyList<int> coordinates)
    {
        Assert.Equal(2, coordinates.Count);
        return new GridPoint(coordinates[0], coordinates[1]);
    }

    private sealed record AchievementFixture(
        int SchemaVersion,
        string Contract,
        AchievementRuleset Ruleset,
        string RandomnessPolicy,
        string SourceEngine,
        int CaseCount,
        AchievementConfig Config,
        List<string> ComparisonScope,
        List<string> ExcludedScope,
        List<AchievementCase> Cases);

    private sealed record AchievementRuleset(string Id, int Version);

    private sealed record AchievementConfig(
        int Width,
        int Height,
        int StarvationTicks,
        int MaximumDirectionQueue,
        int MaximumScore,
        int ComboWindowTicks,
        int SpeedBonusTicks,
        int FoodScore,
        bool EnableAchievementCandidates);

    private sealed record AchievementCase(
        string Id,
        AchievementInitial Initial,
        List<string> Commands,
        AchievementExpected Expected);

    private sealed record AchievementInitial(
        List<List<int>> Body,
        string Direction,
        List<int>? Food,
        int Score,
        int Combo,
        int TicksSinceLastFood,
        int StarvationTicksElapsed,
        List<string>? AlreadyUnlocked);

    private sealed record AchievementExpected(
        int Tick,
        string Direction,
        List<int> Head,
        List<List<int>> Body,
        int Score,
        bool Alive,
        bool Won,
        string? DeathCause,
        List<AchievementEvent> Events);

    private sealed record AchievementEvent(
        string Kind,
        List<int>? Position = null,
        string? Direction = null,
        int? Value = null,
        string? DeathCause = null);
}
