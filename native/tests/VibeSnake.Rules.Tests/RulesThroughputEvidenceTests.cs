using System.Diagnostics;
using System.Text.Json;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// Records pure rules throughput evidence for the 0.3 technology decision gate.
/// These numbers are host-dependent and do not claim presentation frame times,
/// declared-hardware p95, or product-feel acceptance.
/// </summary>
[Collection(RulesThroughputIntegrationGroup.Name)]
public sealed class RulesThroughputEvidenceTests
{
    private const int StepBudget = 50_000;
    private const double MinimumStepsPerSecond = 750.0;

    private static readonly Direction[] TurnPattern =
    [
        Direction.Up,
        Direction.Right,
        Direction.Down,
        Direction.Left,
    ];

    [Fact]
    public void Records_rules_throughput_evidence_for_decision_gate()
    {
        var createWatch = Stopwatch.StartNew();
        var run = SnakeRun.Create(42UL);
        createWatch.Stop();

        var stepWatch = Stopwatch.StartNew();
        var steps = 0;
        var restarts = 0;
        var patternIndex = 0;
        while (steps < StepBudget)
        {
            if (run.Status != RunStatus.Running)
            {
                run = run.Restart(checked(42UL + (ulong)restarts + 1UL));
                restarts++;
            }

            run.QueueDirection(TurnPattern[patternIndex % TurnPattern.Length]);
            patternIndex++;
            run.Step();
            steps++;
        }

        stepWatch.Stop();

        var elapsedSeconds = Math.Max(stepWatch.Elapsed.TotalSeconds, 1e-9);
        var stepsPerSecond = steps / elapsedSeconds;
        var evidence = new
        {
            schema_version = 1,
            kind = "rules-throughput-evidence-v1",
            ruleset_id = SnakeRun.RulesetId,
            rules_version = SnakeRun.RulesVersion,
            config_hash = run.ConfigHash,
            config_hash_algorithm = run.ConfigHashAlgorithm,
            step_budget = StepBudget,
            steps_executed = steps,
            restarts,
            create_milliseconds = createWatch.Elapsed.TotalMilliseconds,
            step_milliseconds = stepWatch.Elapsed.TotalMilliseconds,
            steps_per_second = stepsPerSecond,
            minimum_steps_per_second_floor = MinimumStepsPerSecond,
            notes = new[]
            {
                "Host-dependent pure rules measurement only.",
                "Does not measure Godot presentation frame times.",
                "Does not claim declared-hardware p50/p95/p99 acceptance.",
            },
        };

        var outputDirectory = ResolveEvidenceDirectory();
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "rules_throughput.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                evidence,
                TestJsonSerializerOptions.Indented) + "\n");

        Assert.True(
            stepsPerSecond >= MinimumStepsPerSecond,
            $"Rules throughput {stepsPerSecond:F1} steps/s is below the "
                + $"conservative CI floor of {MinimumStepsPerSecond:F0} steps/s. "
                + $"Evidence: {path}");
        Assert.True(File.Exists(path));
        Assert.True(createWatch.Elapsed.TotalMilliseconds >= 0.0);
        Assert.Equal(StepBudget, steps);
        Assert.Matches("^[0-9a-f]{64}$", run.ConfigHash);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, run.ConfigHashAlgorithm);
        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            Assert.Equal(
                run.ConfigHash,
                document.RootElement.GetProperty("config_hash").GetString());
        }
    }

    private static string ResolveEvidenceDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var roadmap = Path.Combine(directory.FullName, "ROADMAP.md");
            var solution = Path.Combine(directory.FullName, "native", "VibeSnake.slnx");
            if (File.Exists(roadmap) && File.Exists(solution))
            {
                return Path.Combine(directory.FullName, "TestResults", "native");
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestResults", "native"));
    }
}
