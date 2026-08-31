using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class SharedRuleTraceParityTests
{
    [Fact]
    public void Csharp_matches_targeted_python_core_rule_traces()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "core_rules_v4.json");
        var fixture = JsonSerializer.Deserialize<RuleFixture>(
            File.ReadAllText(fixturePath),
            TestJsonSerializerOptions.SnakeCase);

        Assert.NotNull(fixture);
        Assert.Equal(4, fixture.SchemaVersion);
        Assert.Equal("core-rules-targeted-v4", fixture.Contract);
        Assert.Equal("vibesnake-core", fixture.Ruleset.Id);
        Assert.Equal(4, fixture.Ruleset.Version);
        Assert.Equal(SnakeRun.RulesetId, fixture.Ruleset.Id);
        Assert.Equal(SnakeRun.RulesVersion, fixture.Ruleset.Version);
        Assert.Equal(
            "positions-injected-or-random-output-normalized-v2",
            fixture.RandomnessPolicy);
        Assert.Equal("python-core-reference-v3", fixture.SourceEngine);
        Assert.Equal(
            [
                "food_entry",
                "growth",
                "base_score",
                "score_saturation",
                "speed_bonus",
                "speed_bonus_boundaries",
                "combo_interpolation",
                "combo_expiry",
                "combo_clock_monotonicity",
                "length_bonus",
                "length_bonus_boundaries",
                "command_acceptance",
                "queue_capacity",
                "queue_consumption",
                "self_collision",
                "departing_tail",
                "edge_wrapping",
                "starvation_progress",
                "exact_starvation_deadline",
                "collision_precedence",
                "full_grid_completion",
                "food_stability_without_collection",
                "random_respawn_legality",
                "random_stream_use",
                "ordered_events",
            ],
            fixture.ComparisonScope);
        Assert.Equal(
            ["food_respawn_coordinate", "risk_bonus", "power_effects"],
            fixture.ExcludedScope);
        Assert.Equal(64, fixture.Config.Width);
        Assert.Equal(33, fixture.Config.Height);
        Assert.Equal(600, fixture.Config.StarvationTicks);
        Assert.Equal(3, fixture.Config.MaximumDirectionQueue);
        Assert.Equal(2_000_000_000, fixture.Config.MaximumScore);
        Assert.Equal(60, fixture.Config.ComboWindowTicks);
        Assert.Equal(30, fixture.Config.SpeedBonusTicks);
        Assert.Equal(10, fixture.Config.FoodScore);
        Assert.Equal(SnakeRun.MaximumScore, fixture.Config.MaximumScore);
        Assert.Equal(35, fixture.CaseCount);
        Assert.Equal(fixture.CaseCount, fixture.Cases.Count);
        Assert.Equal(
            [
                "food-entry",
                "food-buffered-turn",
                "queue-rejections-and-consumption",
                "queue-capacity",
                "combo-before-three",
                "combo-threshold-three",
                "combo-after-three",
                "combo-threshold-five",
                "combo-after-five",
                "combo-before-ten",
                "combo-threshold-ten",
                "combo-after-ten",
                "combo-before-twenty",
                "combo-threshold-twenty",
                "combo-after-twenty-cap",
                "speed-bonus-last-eligible-tick",
                "speed-bonus-exact-boundary",
                "speed-bonus-after-boundary",
                "combo-window-exact-no-food",
                "combo-window-expired-no-food",
                "combo-window-exact-food",
                "expired-combo-late-food-no-speed-bonus",
                "length-exact-ten",
                "length-first-bonus",
                "length-above-boundary",
                "score-saturation-near-cap",
                "score-at-cap",
                "self-collision",
                "departing-tail-is-safe",
                "horizontal-wrap",
                "starvation-predeadline",
                "starvation-deadline-food-rescue",
                "starvation-deadline-death",
                "starvation-collision-precedence",
                "full-grid-victory",
            ],
            fixture.Cases.Select(traceCase => traceCase.Id));
        Assert.Equal(
            fixture.Cases.Count,
            fixture.Cases.Select(traceCase => traceCase.Id).Distinct(StringComparer.Ordinal).Count());

        AssertFrozenSemantics(
            fixture.Config,
            fixture.Cases.ToDictionary(traceCase => traceCase.Id, StringComparer.Ordinal));

        foreach (var traceCase in fixture.Cases)
        {
            ExecuteCase(fixture.Config, traceCase);
        }
    }

    private static void ExecuteCase(RuleConfig fixtureConfig, RuleCase traceCase)
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
            EnableComboExpiredEvent: true);
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

        var actualCommandAcceptance = new List<bool>(traceCase.Commands.Count);
        foreach (var command in traceCase.Commands)
        {
            actualCommandAcceptance.Add(run.QueueDirection(ParseDirection(command)));
        }

        var randomStateBefore = ReadRandomState(run);
        var result = run.Step();
        var snapshot = run.GetSnapshot();
        var expected = traceCase.Expected;
        var expectedState = new
        {
            expected.Tick,
            CommandAcceptance = traceCase.CommandAcceptance,
            expected.Direction,
            Head = expected.Head,
            expected.Body,
            expected.PendingDirections,
            expected.Score,
            expected.Combo,
            expected.TicksSinceLastFood,
            expected.StarvationTicksElapsed,
            expected.Wrapped,
            expected.AteFood,
            expected.Alive,
            expected.Won,
            expected.DeathCause,
            expected.FoodUnchanged,
            expected.RandomRespawn,
            expected.RandomUse,
        };
        var actualState = new
        {
            snapshot.Tick,
            CommandAcceptance = actualCommandAcceptance,
            Direction = snapshot.Direction.ToString().ToUpperInvariant(),
            Head = new[] { snapshot.Head.X, snapshot.Head.Y },
            Body = snapshot.Body.Select(point => new[] { point.X, point.Y }).ToList(),
            PendingDirections = snapshot.PendingDirections
                .Select(value => value.ToString().ToUpperInvariant())
                .ToList(),
            snapshot.Score,
            Combo = snapshot.ComboCount,
            snapshot.TicksSinceLastFood,
            StarvationTicksElapsed = fixtureConfig.StarvationTicks - snapshot.HungerTicksRemaining,
            Wrapped = result.Events.HasFlag(RunEvent.Wrapped),
            AteFood = result.Events.HasFlag(RunEvent.AteFood),
            Alive = snapshot.Status == RunStatus.Running,
            Won = snapshot.Status == RunStatus.Won,
            DeathCause = NormalizeDeathCause(snapshot.DeathCause),
            FoodUnchanged = snapshot.Food == (initial.Food is null ? null : ToGridPoint(initial.Food)),
            RandomRespawn = NormalizeRandomRespawn(config, result, snapshot),
            RandomUse = randomStateBefore == ReadRandomState(run) ? "unchanged" : "advanced",
        };
        var actualEvents = result.OrderedEvents.Select(NormalizeEvent).ToList();
        if (
            !ParityDivergence.AreEquivalent(expectedState, actualState)
            || !ParityDivergence.AreEquivalent(expected.Events, actualEvents))
        {
            ParityDivergence.ThrowWithBundle(
                new ParityDivergenceRequest(
                    Contract: "core-rules-targeted-v4",
                    Fixture: "core_rules_v4.json",
                    TestFilter:
                        "VibeSnake.Rules.Tests.SharedRuleTraceParityTests."
                        + "Csharp_matches_targeted_python_core_rule_traces",
                    CaseId: traceCase.Id,
                    Seed: null,
                    FirstDivergentStep: expected.Tick,
                    InitialState: traceCase.Initial,
                    CommandPrefix: traceCase.Commands,
                    ExpectedState: expectedState,
                    ExpectedEvents: expected.Events,
                    ActualState: actualState,
                    ActualEvents: actualEvents,
                    ActualCanonicalState: JsonSerializer.Deserialize<JsonElement>(
                        run.SerializeCanonicalState()),
                    ActualStateHash: result.StateHash));
        }
    }

    private static void AssertFrozenSemantics(
        RuleConfig config,
        IReadOnlyDictionary<string, RuleCase> cases)
    {
        Assert.All(cases.Values, traceCase =>
        {
            Assert.Equal(1, traceCase.Expected.Tick);
            Assert.Equal<int>(traceCase.Expected.Body[^1], traceCase.Expected.Head);
            Assert.Equal(traceCase.Initial.Direction.ToUpperInvariant(), traceCase.Initial.Direction);
            Assert.Equal(traceCase.Expected.Direction.ToUpperInvariant(), traceCase.Expected.Direction);
            Assert.All(
                traceCase.Commands,
                command => Assert.Equal(command.ToUpperInvariant(), command));
            Assert.All(
                traceCase.Expected.PendingDirections,
                direction => Assert.Equal(direction.ToUpperInvariant(), direction));

            if (traceCase.Expected.AteFood)
            {
                Assert.NotNull(traceCase.Initial.Food);
                Assert.Equal<int>(traceCase.Initial.Food, traceCase.Expected.Head);
                Assert.Equal(0, traceCase.Expected.TicksSinceLastFood);
                Assert.Equal(0, traceCase.Expected.StarvationTicksElapsed);
                Assert.False(traceCase.Expected.FoodUnchanged);
                Assert.Equal(traceCase.Initial.Body.Count + 1, traceCase.Expected.Body.Count);
                Assert.Equal(
                    PointStrings(traceCase.Initial.Body.Append(traceCase.Expected.Head)),
                    PointStrings(traceCase.Expected.Body));
                Assert.Equal(
                    traceCase.Expected.Won ? "full_grid_no_cell" : "legal_free_cell",
                    traceCase.Expected.RandomRespawn);
                Assert.Equal(
                    traceCase.Expected.Won ? "unchanged" : "advanced",
                    traceCase.Expected.RandomUse);
                Assert.Equal(
                    traceCase.Expected.Score - traceCase.Initial.Score,
                    traceCase.Expected.Events
                        .Where(detail => detail.Kind == "score_changed")
                        .Sum(detail => detail.Value));
                Assert.Contains(
                    traceCase.Expected.Events,
                    detail => detail.Kind == "hunger_reset" && detail.Value == config.StarvationTicks);
            }
            else
            {
                Assert.True(traceCase.Expected.FoodUnchanged);
                Assert.Equal("not_used", traceCase.Expected.RandomRespawn);
                Assert.Equal("unchanged", traceCase.Expected.RandomUse);
                Assert.Equal(
                    traceCase.Initial.TicksSinceLastFood + 1,
                    traceCase.Expected.TicksSinceLastFood);
                Assert.Equal(
                    traceCase.Initial.StarvationTicksElapsed + 1,
                    traceCase.Expected.StarvationTicksElapsed);
                Assert.Equal(traceCase.Initial.Body.Count, traceCase.Expected.Body.Count);
                Assert.Equal(traceCase.Initial.Score, traceCase.Expected.Score);
                if (traceCase.Id is not ("self-collision" or "starvation-collision-precedence"))
                {
                    Assert.Equal(
                        PointStrings(traceCase.Initial.Body.Skip(1).Append(traceCase.Expected.Head)),
                        PointStrings(traceCase.Expected.Body));
                }
            }

            Assert.All(traceCase.Expected.Events, detail =>
            {
                if (detail.Kind is "moved" or "ate_food" or "wrapped" or "won")
                {
                    Assert.Equal<int>(traceCase.Expected.Head, detail.Position!);
                }

                if (detail.Kind == "direction_changed")
                {
                    Assert.Equal(traceCase.Expected.Direction, detail.Direction);
                }

                if (detail.Kind == "combo_expired")
                {
                    Assert.Equal(0, detail.Value);
                }

                if (detail.Kind == "died")
                {
                    Assert.Equal(traceCase.Expected.DeathCause, detail.DeathCause);
                }
            });
        });

        var commandCases = new HashSet<string>(
            ["food-buffered-turn", "queue-rejections-and-consumption", "queue-capacity"],
            StringComparer.Ordinal);
        Assert.All(
            cases.Values.Where(traceCase => !commandCases.Contains(traceCase.Id)),
            traceCase =>
            {
                Assert.Empty(traceCase.Commands);
                Assert.Empty(traceCase.CommandAcceptance);
            });
        Assert.All(
            cases.Values.Where(traceCase => traceCase.Id is not (
                "queue-rejections-and-consumption" or "queue-capacity")),
            traceCase => Assert.Empty(traceCase.Expected.PendingDirections));

        Assert.Equal(
            [false, false, true, false, true, false],
            cases["queue-rejections-and-consumption"].CommandAcceptance);
        Assert.Equal(
            ["RIGHT", "LEFT", "UP", "DOWN", "LEFT", "LEFT"],
            cases["queue-rejections-and-consumption"].Commands);
        Assert.Equal(
            ["LEFT"],
            cases["queue-rejections-and-consumption"].Expected.PendingDirections);
        Assert.Equal(
            [true, true, true, false, false],
            cases["queue-capacity"].CommandAcceptance);
        Assert.Equal(
            ["UP", "LEFT", "DOWN", "RIGHT", "UP"],
            cases["queue-capacity"].Commands);
        Assert.Equal(["LEFT", "DOWN"], cases["queue-capacity"].Expected.PendingDirections);
        Assert.Equal([true], cases["food-buffered-turn"].CommandAcceptance);
        Assert.Equal(["UP"], cases["food-buffered-turn"].Commands);
        Assert.Equal("UP", cases["food-buffered-turn"].Expected.Direction);
        Assert.Equal("UP", cases["food-buffered-turn"].Expected.Events[0].Direction);
        Assert.Equal("UP", cases["queue-rejections-and-consumption"].Expected.Direction);
        Assert.Equal("UP", cases["queue-capacity"].Expected.Direction);
        Assert.All(
            cases.Values.Where(traceCase => traceCase.Id is not (
                "food-buffered-turn" or "queue-rejections-and-consumption" or "queue-capacity")),
            traceCase => Assert.Equal(traceCase.Initial.Direction, traceCase.Expected.Direction));

        (string Id, int InitialCombo, int InitialTicks, int ExpectedCombo, int Award)[] scoreCases =
        [
            ("food-entry", 0, 0, 1, 18),
            ("food-buffered-turn", 0, 0, 1, 18),
            ("combo-before-three", 1, 29, 2, 16),
            ("combo-threshold-three", 2, 29, 3, 20),
            ("combo-after-three", 3, 29, 4, 25),
            ("combo-threshold-five", 4, 29, 5, 30),
            ("combo-after-five", 5, 29, 6, 34),
            ("combo-before-ten", 8, 29, 9, 46),
            ("combo-threshold-ten", 9, 29, 10, 50),
            ("combo-after-ten", 10, 29, 11, 55),
            ("combo-before-twenty", 18, 29, 19, 95),
            ("combo-threshold-twenty", 19, 29, 20, 100),
            ("combo-after-twenty-cap", 20, 29, 21, 100),
            ("speed-bonus-last-eligible-tick", 0, 28, 1, 18),
            ("speed-bonus-exact-boundary", 0, 29, 1, 13),
            ("speed-bonus-after-boundary", 0, 30, 1, 13),
            ("combo-window-exact-food", 4, 59, 5, 30),
            ("expired-combo-late-food-no-speed-bonus", 4, 60, 1, 13),
            ("length-exact-ten", 0, 29, 1, 13),
            ("length-first-bonus", 0, 29, 1, 14),
            ("length-above-boundary", 0, 29, 1, 15),
        ];
        foreach (var (id, initialCombo, initialTicks, expectedCombo, award) in scoreCases)
        {
            var traceCase = cases[id];
            Assert.Equal(initialCombo, traceCase.Initial.Combo);
            Assert.Equal(initialTicks, traceCase.Initial.TicksSinceLastFood);
            Assert.Equal(expectedCombo, traceCase.Expected.Combo);
            Assert.Equal(traceCase.Initial.Score + award, traceCase.Expected.Score);
            Assert.Equal(
                award,
                traceCase.Expected.Events.Single(detail => detail.Kind == "score_changed").Value);
        }

        Assert.Equal(10, cases["length-exact-ten"].Expected.Body.Count);
        Assert.Equal(11, cases["length-first-bonus"].Expected.Body.Count);
        Assert.Equal(12, cases["length-above-boundary"].Expected.Body.Count);
        AssertScoreSaturation(cases["score-saturation-near-cap"], 1_999_999_999, 1);
        AssertScoreSaturation(cases["score-at-cap"], 2_000_000_000, 0);

        var exactCombo = cases["combo-window-exact-no-food"].Expected;
        Assert.Equal(4, exactCombo.Combo);
        Assert.Equal(60, exactCombo.TicksSinceLastFood);
        var expiredCombo = cases["combo-window-expired-no-food"].Expected;
        Assert.Equal(0, expiredCombo.Combo);
        Assert.Equal(61, expiredCombo.TicksSinceLastFood);
        Assert.Equal(0, expiredCombo.Events[0].Value);

        var selfCollision = cases["self-collision"];
        Assert.Equal(PointStrings(selfCollision.Initial.Body), PointStrings(selfCollision.Expected.Body));
        Assert.Equal<int>([2, 1], selfCollision.Expected.Head);
        Assert.Equal<int>([2, 2], selfCollision.Expected.Events.Single().Position!);
        Assert.Equal("self_collision", selfCollision.Expected.DeathCause);
        var departingTail = cases["departing-tail-is-safe"].Expected;
        Assert.Equal(["1,2", "2,2", "2,1", "1,1"], PointStrings(departingTail.Body));
        Assert.True(departingTail.Alive);
        Assert.Null(departingTail.DeathCause);

        var wrap = cases["horizontal-wrap"].Expected;
        Assert.Equal<int>([0, 10], wrap.Head);
        Assert.True(wrap.Wrapped);
        Assert.Equal(["moved", "wrapped"], EventKinds(wrap));
        Assert.Equal<int>([5, 5], cases["horizontal-wrap"].Initial.Food!);

        Assert.Equal(599, cases["starvation-predeadline"].Expected.StarvationTicksElapsed);
        Assert.True(cases["starvation-predeadline"].Expected.Alive);
        var rescued = cases["starvation-deadline-food-rescue"].Expected;
        Assert.Equal(19, rescued.Score);
        Assert.Equal(1, rescued.Combo);
        Assert.Equal(1, rescued.Events[^1].Value);
        var starved = cases["starvation-deadline-death"].Expected;
        Assert.Equal(600, starved.StarvationTicksElapsed);
        Assert.Equal("starvation", starved.DeathCause);
        Assert.Equal<int>([6, 5], starved.Head);
        Assert.Equal<int>([6, 5], starved.Events[^1].Position!);
        var precedence = cases["starvation-collision-precedence"];
        Assert.Equal(600, precedence.Expected.StarvationTicksElapsed);
        Assert.Equal("self_collision", precedence.Expected.DeathCause);
        Assert.Equal(PointStrings(precedence.Initial.Body), PointStrings(precedence.Expected.Body));
        Assert.Equal<int>([2, 2], precedence.Expected.Events.Single().Position!);

        var terminalCases = new HashSet<string>(
            [
                "self-collision",
                "starvation-deadline-death",
                "starvation-collision-precedence",
                "full-grid-victory",
            ],
            StringComparer.Ordinal);
        Assert.All(
            cases.Values,
            traceCase => Assert.Equal(!terminalCases.Contains(traceCase.Id), traceCase.Expected.Alive));
        Assert.All(
            cases.Values,
            traceCase => Assert.Equal(traceCase.Id == "full-grid-victory", traceCase.Expected.Won));
        Assert.All(
            cases.Values,
            traceCase => Assert.Equal(traceCase.Id == "horizontal-wrap", traceCase.Expected.Wrapped));
        Assert.All(
            cases.Values.Where(traceCase => !terminalCases.Contains(traceCase.Id)),
            traceCase => Assert.Null(traceCase.Expected.DeathCause));

        AssertEventOrders(cases);
        AssertFullGrid(cases["full-grid-victory"], config);
    }

    private static void AssertScoreSaturation(
        RuleCase traceCase,
        int initialScore,
        int awardedPoints)
    {
        Assert.Equal(initialScore, traceCase.Initial.Score);
        Assert.Equal(2_000_000_000, traceCase.Expected.Score);
        Assert.Equal(1, traceCase.Expected.Combo);
        Assert.Equal(
            awardedPoints,
            traceCase.Expected.Events.Single(detail => detail.Kind == "score_changed").Value);
    }

    private static void AssertEventOrders(IReadOnlyDictionary<string, RuleCase> cases)
    {
        var normalFoodCases = new HashSet<string>(
            [
                "food-entry",
                "combo-before-three",
                "combo-threshold-three",
                "combo-after-three",
                "combo-threshold-five",
                "combo-after-five",
                "combo-before-ten",
                "combo-threshold-ten",
                "combo-after-ten",
                "combo-before-twenty",
                "combo-threshold-twenty",
                "combo-after-twenty-cap",
                "speed-bonus-last-eligible-tick",
                "speed-bonus-exact-boundary",
                "speed-bonus-after-boundary",
                "combo-window-exact-food",
                "length-exact-ten",
                "length-first-bonus",
                "length-above-boundary",
                "score-saturation-near-cap",
                "score-at-cap",
            ],
            StringComparer.Ordinal);
        var movedOnlyCases = new HashSet<string>(
            [
                "combo-window-exact-no-food",
                "departing-tail-is-safe",
                "starvation-predeadline",
            ],
            StringComparer.Ordinal);

        foreach (var pair in cases)
        {
            string[] expectedKinds = pair.Key switch
            {
                "food-buffered-turn" =>
                    ["direction_changed", "moved", "ate_food", "score_changed", "hunger_reset"],
                "queue-rejections-and-consumption" or "queue-capacity" =>
                    ["direction_changed", "moved"],
                "combo-window-expired-no-food" => ["combo_expired", "moved"],
                "expired-combo-late-food-no-speed-bonus" =>
                    ["combo_expired", "moved", "ate_food", "score_changed", "hunger_reset"],
                "self-collision" or "starvation-collision-precedence" => ["died"],
                "horizontal-wrap" => ["moved", "wrapped"],
                "starvation-deadline-food-rescue" =>
                    ["moved", "ate_food", "score_changed", "hunger_reset", "score_changed", "near_miss"],
                "starvation-deadline-death" => ["moved", "died"],
                "full-grid-victory" =>
                    ["moved", "ate_food", "score_changed", "hunger_reset", "won"],
                _ when normalFoodCases.Contains(pair.Key) =>
                    ["moved", "ate_food", "score_changed", "hunger_reset"],
                _ when movedOnlyCases.Contains(pair.Key) => ["moved"],
                _ => throw new InvalidDataException($"Missing frozen event order for {pair.Key}."),
            };
            Assert.Equal(expectedKinds, EventKinds(pair.Value.Expected));
        }
    }

    private static void AssertFullGrid(RuleCase traceCase, RuleConfig config)
    {
        Assert.Equal(2_111, traceCase.Initial.Body.Count);
        Assert.Equal(2_112, traceCase.Expected.Body.Count);
        Assert.Equal(config.Width * config.Height, traceCase.Expected.Body.Count);
        Assert.Equal<int>([62, 32], traceCase.Initial.Body[^1]);
        Assert.Equal<int>([63, 32], traceCase.Initial.Food!);
        Assert.Equal<int>([63, 32], traceCase.Expected.Head);
        Assert.Equal(
            PointStrings(traceCase.Initial.Body),
            PointStrings(traceCase.Expected.Body.Take(traceCase.Initial.Body.Count)));
        Assert.Equal(2_111, PointStrings(traceCase.Initial.Body).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2_112, PointStrings(traceCase.Expected.Body).Distinct(StringComparer.Ordinal).Count());
        for (var index = 0; index < traceCase.Expected.Body.Count; index++)
        {
            var row = index / config.Width;
            var column = index % config.Width;
            var expectedX = row % 2 == 0 ? column : config.Width - 1 - column;
            Assert.Equal<int>([expectedX, row], traceCase.Expected.Body[index]);
        }

        Assert.False(traceCase.Expected.Alive);
        Assert.True(traceCase.Expected.Won);
        Assert.Null(traceCase.Expected.DeathCause);
        Assert.Equal(8_063, traceCase.Expected.Score);
        Assert.Equal(1, traceCase.Expected.Combo);
        Assert.Equal("full_grid_no_cell", traceCase.Expected.RandomRespawn);
        Assert.Equal("unchanged", traceCase.Expected.RandomUse);
    }

    private static string[] EventKinds(RuleExpected expected) =>
        expected.Events.Select(detail => detail.Kind).ToArray();

    private static string[] PointStrings(IEnumerable<List<int>> points) =>
        points.Select(point => string.Join(',', point)).ToArray();

    private static RuleEvent NormalizeEvent(RunEventDetail detail) => new(
        NormalizeEventKind(detail.Kind),
        detail.Position is { } position ? [position.X, position.Y] : null,
        detail.NewDirection?.ToString().ToUpperInvariant(),
        detail.Value,
        detail.Cause is { } cause ? NormalizeDeathCause(cause) : null);

    private static string NormalizeEventKind(RunEventKind kind) =>
        RulesEventCatalog.ToWireName(kind);

    private static string? NormalizeDeathCause(DeathCause cause) => cause switch
    {
        DeathCause.None => null,
        DeathCause.SelfCollision => "self_collision",
        DeathCause.Starvation => "starvation",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unknown death cause."),
    };

    private static string NormalizeRandomRespawn(
        RunConfig config,
        RunStepResult result,
        RunSnapshot snapshot)
    {
        if (!result.Events.HasFlag(RunEvent.AteFood))
        {
            return "not_used";
        }

        if (snapshot.Status == RunStatus.Won)
        {
            return snapshot.Food is null && snapshot.Body.Count == config.Width * config.Height
                ? "full_grid_no_cell"
                : "invalid";
        }

        if (snapshot.Food is not { } food)
        {
            return "invalid";
        }

        var inBounds = food.X >= 0 && food.X < config.Width && food.Y >= 0 && food.Y < config.Height;
        return inBounds && !snapshot.Body.Contains(food) ? "legal_free_cell" : "invalid";
    }

    private static string ReadRandomState(SnakeRun run)
    {
        using var document = JsonDocument.Parse(run.SerializeCanonicalState());
        return document.RootElement
            .GetProperty("random")
            .GetProperty("state")
            .GetString()!;
    }

    private static Direction ParseDirection(string value) => Enum.Parse<Direction>(value, ignoreCase: true);

    private static GridPoint ToGridPoint(IReadOnlyList<int> coordinates)
    {
        Assert.Equal(2, coordinates.Count);
        return new GridPoint(coordinates[0], coordinates[1]);
    }

    private sealed record RuleFixture(
        int SchemaVersion,
        string Contract,
        RuleRuleset Ruleset,
        string RandomnessPolicy,
        string SourceEngine,
        int CaseCount,
        RuleConfig Config,
        List<string> ComparisonScope,
        List<string> ExcludedScope,
        List<RuleCase> Cases);

    private sealed record RuleRuleset(string Id, int Version);

    private sealed record RuleConfig(
        int Width,
        int Height,
        int StarvationTicks,
        int MaximumDirectionQueue,
        int MaximumScore,
        int ComboWindowTicks,
        int SpeedBonusTicks,
        int FoodScore);

    private sealed record RuleCase(
        string Id,
        RuleInitial Initial,
        List<string> Commands,
        List<bool> CommandAcceptance,
        RuleExpected Expected);

    private sealed record RuleInitial(
        List<List<int>> Body,
        string Direction,
        List<int>? Food,
        int Score,
        int Combo,
        int TicksSinceLastFood,
        int StarvationTicksElapsed);

    private sealed record RuleExpected(
        int Tick,
        string Direction,
        List<int> Head,
        List<List<int>> Body,
        List<string> PendingDirections,
        int Score,
        int Combo,
        int TicksSinceLastFood,
        int StarvationTicksElapsed,
        bool Wrapped,
        bool AteFood,
        bool Alive,
        bool Won,
        string? DeathCause,
        bool FoodUnchanged,
        string RandomRespawn,
        string RandomUse,
        List<RuleEvent> Events);

    private sealed record RuleEvent(
        string Kind,
        List<int>? Position,
        string? Direction,
        int? Value,
        string? DeathCause);
}
