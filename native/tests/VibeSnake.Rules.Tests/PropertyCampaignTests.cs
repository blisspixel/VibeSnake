using System.Text.Json;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// First-class property campaign that explores random legal command streams and
/// emits a versioned JSON report for the automated QA laboratory (V040-10).
/// </summary>
public sealed class PropertyCampaignTests
{
    private static readonly string[] Invariants =
    [
        "score_non_decreasing",
        "body_in_bounds",
        "head_exists_once",
        "food_contract",
        "session_counters_non_negative",
        "session_counters_survive_restore",
        "state_hash_stable_after_round_trip",
        "config_hash_stable_within_run",
        "death_cause_defined",
    ];

    [Fact]
    public void Property_campaign_passes_and_writes_versioned_report()
    {
        const int seedCount = 8;
        const int operationsPerSeed = 256;
        ulong[] seeds =
        [
            0UL,
            1UL,
            7UL,
            42UL,
            99UL,
            255UL,
            65535UL,
            ulong.MaxValue,
        ];
        Assert.Equal(seedCount, seeds.Length);

        var stepsExecuted = 0;
        var restoresExecuted = 0;
        var restartsExecuted = 0;
        PropertyCampaignFailure? firstFailure = null;

        foreach (var seed in seeds)
        {
            if (firstFailure is not null)
            {
                break;
            }

            var commandRandom = new Pcg32(seed, sequence: 17_001UL);
            var config = new RunConfig(
                Width: 16,
                Height: 12,
                StarvationTicks: 300,
                MaximumDirectionQueue: 3);
            var run = SnakeRun.Create(NextUInt64(commandRandom), config);
            var previousScore = run.Score;
            var fixedConfigHash = run.ConfigHash;

            for (var operation = 0; operation < operationsPerSeed; operation++)
            {
                var commandCount = commandRandom.NextInt(5);
                for (var commandIndex = 0; commandIndex < commandCount; commandIndex++)
                {
                    run.QueueDirection((Direction)commandRandom.NextInt(4));
                }

                run.Step();
                stepsExecuted++;

                firstFailure = CheckInvariants(
                    run,
                    config,
                    seed,
                    operation,
                    previousScore,
                    fixedConfigHash);
                if (firstFailure is not null)
                {
                    break;
                }

                previousScore = run.Score;

                if (operation % 13 == 0)
                {
                    var beforeFood = run.SessionFoodEaten;
                    var beforeWraps = run.SessionWraps;
                    var beforeNearMiss = run.SessionNearMisses;
                    var beforePowers = run.SessionPowerupsCollected;
                    var beforeMaxCombo = run.SessionMaxCombo;
                    var beforeHash = run.ComputeStateHash();
                    run = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());
                    restoresExecuted++;
                    if (run.ComputeStateHash() != beforeHash)
                    {
                        firstFailure = Fail(
                            seed,
                            operation,
                            run,
                            "state_hash_stable_after_round_trip",
                            $"hash {beforeHash} became {run.ComputeStateHash()}");
                        break;
                    }

                    if (
                        run.SessionFoodEaten != beforeFood
                        || run.SessionWraps != beforeWraps
                        || run.SessionNearMisses != beforeNearMiss
                        || run.SessionPowerupsCollected != beforePowers
                        || run.SessionMaxCombo != beforeMaxCombo)
                    {
                        firstFailure = Fail(
                            seed,
                            operation,
                            run,
                            "session_counters_survive_restore",
                            "session counters diverged after canonical restore");
                        break;
                    }
                }

                if (run.Status != RunStatus.Running)
                {
                    run = run.Restart(NextUInt64(commandRandom));
                    restartsExecuted++;
                    previousScore = run.Score;
                    fixedConfigHash = run.ConfigHash;
                }
            }
        }

        var result = new PropertyCampaignResult(
            CampaignId: "core-property-invariants-v1",
            TestFilter: "Property_campaign_passes_and_writes_versioned_report",
            SeedCount: seedCount,
            OperationsPerSeed: operationsPerSeed,
            StepsExecuted: stepsExecuted,
            RestoresExecuted: restoresExecuted,
            RestartsExecuted: restartsExecuted,
            InvariantsChecked: Invariants,
            Passed: firstFailure is null,
            FirstFailure: firstFailure);

        var path = PropertyCampaignReport.Write(result);
        Assert.True(File.Exists(path), $"Missing property campaign report: {path}");

        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var root = document.RootElement;
            Assert.Equal(PropertyCampaignReport.SchemaVersion, root.GetProperty("schema_version").GetInt32());
            Assert.Equal(PropertyCampaignReport.Kind, root.GetProperty("kind").GetString());
            Assert.Equal(seedCount, root.GetProperty("seed_count").GetInt32());
            Assert.Equal(operationsPerSeed, root.GetProperty("operations_per_seed").GetInt32());
            Assert.Equal(
                SnakeRun.CanonicalStateSchemaVersion,
                root.GetProperty("engine").GetProperty("canonical_state_schema_version").GetInt32());
            Assert.Equal(
                SnakeRun.StateHashAlgorithmId,
                root.GetProperty("engine").GetProperty("state_hash_algorithm").GetString());
            Assert.True(root.GetProperty("steps_executed").GetInt32() > 0);
            Assert.Contains(
                "score_non_decreasing",
                root.GetProperty("invariants_checked").EnumerateArray().Select(e => e.GetString()));
        }

        Assert.Null(firstFailure);
        Assert.True(result.Passed);
        Assert.True(stepsExecuted >= seedCount);
        Assert.True(restoresExecuted > 0);
    }

    private static PropertyCampaignFailure? CheckInvariants(
        SnakeRun run,
        RunConfig config,
        ulong seed,
        int operation,
        int previousScore,
        string fixedConfigHash)
    {
        if (run.Score < previousScore)
        {
            return Fail(seed, operation, run, "score_non_decreasing", $"score {run.Score} < {previousScore}");
        }

        if (run.ConfigHash != fixedConfigHash)
        {
            return Fail(
                seed,
                operation,
                run,
                "config_hash_stable_within_run",
                $"config hash changed from {fixedConfigHash} to {run.ConfigHash}");
        }

        var body = run.Body;
        if (body.Count < 1 || body[^1] != run.Head)
        {
            return Fail(seed, operation, run, "head_exists_once", "body empty or head identity broken");
        }

        for (var index = 0; index < body.Count; index++)
        {
            var point = body[index];
            if (!IsInBounds(point, config))
            {
                return Fail(
                    seed,
                    operation,
                    run,
                    "body_in_bounds",
                    $"segment ({point.X},{point.Y}) out of bounds");
            }
        }

        if (run.Status == RunStatus.Running)
        {
            if (run.Food is not { } food || !IsInBounds(food, config))
            {
                return Fail(seed, operation, run, "food_contract", "running state missing in-bounds food");
            }

            if (body.Contains(food))
            {
                return Fail(seed, operation, run, "food_contract", "food overlaps body");
            }
        }

        if (
            run.SessionFoodEaten < 0
            || run.SessionWraps < 0
            || run.SessionNearMisses < 0
            || run.SessionPowerupsCollected < 0
            || run.SessionMaxCombo < 0)
        {
            return Fail(seed, operation, run, "session_counters_non_negative", "negative session counter");
        }

        if (!Enum.IsDefined(run.DeathCause))
        {
            return Fail(
                seed,
                operation,
                run,
                "death_cause_defined",
                $"undefined death cause {(byte)run.DeathCause}");
        }

        if (run.Status == RunStatus.Running && run.DeathCause != DeathCause.None)
        {
            return Fail(
                seed,
                operation,
                run,
                "death_cause_defined",
                "running status with non-none death cause");
        }

        if (run.Status == RunStatus.Dead && run.DeathCause == DeathCause.None)
        {
            return Fail(
                seed,
                operation,
                run,
                "death_cause_defined",
                "dead status without death cause");
        }

        return null;
    }

    private static bool IsInBounds(GridPoint point, RunConfig config) =>
        point.X >= 0
        && point.Y >= 0
        && point.X < config.Width
        && point.Y < config.Height;

    private static PropertyCampaignFailure Fail(
        ulong seed,
        int operation,
        SnakeRun run,
        string invariant,
        string detail) =>
        new(
            Seed: seed,
            Operation: operation,
            Tick: run.Tick,
            Invariant: invariant,
            Detail: detail,
            StateHash: run.ComputeStateHash());

    [Fact]
    public void Property_campaign_report_records_first_failure_payload()
    {
        var failure = new PropertyCampaignFailure(
            Seed: 9UL,
            Operation: 3,
            Tick: 12,
            Invariant: "score_non_decreasing",
            Detail: "score dropped",
            StateHash: "0123456789abcdef");
        var result = new PropertyCampaignResult(
            CampaignId: "report-shape-check",
            TestFilter: "Property_campaign_report_records_first_failure_payload",
            SeedCount: 1,
            OperationsPerSeed: 1,
            StepsExecuted: 1,
            RestoresExecuted: 0,
            RestartsExecuted: 0,
            InvariantsChecked: Invariants,
            Passed: false,
            FirstFailure: failure);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-property-campaign-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = PropertyCampaignReport.Write(result, directory);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.False(root.GetProperty("passed").GetBoolean());
            var first = root.GetProperty("first_failure");
            Assert.Equal(9UL, first.GetProperty("seed").GetUInt64());
            Assert.Equal("score_non_decreasing", first.GetProperty("invariant").GetString());
            Assert.Equal("score dropped", first.GetProperty("detail").GetString());
            Assert.Contains(
                "Property_campaign_report_records_first_failure_payload",
                root.GetProperty("reproduction_command").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ulong NextUInt64(Pcg32 random) =>
        ((ulong)random.NextUInt() << 32) | random.NextUInt();
}
