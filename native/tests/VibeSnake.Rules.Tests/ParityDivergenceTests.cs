using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class ParityDivergenceTests
{
    private static readonly string[] CommandPrefix = ["UP", "LEFT"];
    private static readonly int[] DifferentHead = [2, 3];
    private static readonly int[] InitialHead = [1, 1];

    [Fact]
    public void Equivalent_normalized_documents_ignore_runtime_object_types()
    {
        var expected = new { Tick = 1, Head = new List<int> { 2, 3 } };
        var actual = new { Tick = 1, Head = new[] { 2, 3 } };

        Assert.True(ParityDivergence.AreEquivalent(expected, actual));
        Assert.False(ParityDivergence.AreEquivalent(expected, new { Tick = 2, Head = DifferentHead }));
    }

    [Fact]
    public void First_divergence_bundle_is_machine_readable_and_reproducible()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"vibesnake-divergence-test-{Guid.NewGuid():N}");
        try
        {
            var run = SnakeRun.Create(42UL);
            var request = new ParityDivergenceRequest(
                Contract: "test-contract-v1",
                Fixture: "fixture.json",
                TestFilter: "ParityDivergenceTests",
                CaseId: "case/unsafe",
                Seed: 42,
                FirstDivergentStep: 3,
                InitialState: new { Head = InitialHead },
                CommandPrefix: CommandPrefix,
                ExpectedState: new { Tick = 3 },
                ExpectedEvents: Array.Empty<object>(),
                ActualState: new { Tick = 4 },
                ActualEvents: Array.Empty<object>(),
                ActualCanonicalState: JsonSerializer.Deserialize<JsonElement>(
                    run.SerializeCanonicalState()),
                ActualStateHash: run.ComputeStateHash());

            var path = ParityDivergence.WriteBundle(request, outputDirectory);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            Assert.Equal(ParityDivergence.SchemaVersion, root.GetProperty("schema_version").GetInt32());
            Assert.Equal("case/unsafe", root.GetProperty("case_id").GetString());
            Assert.Equal(3, root.GetProperty("first_divergent_step").GetInt32());
            Assert.Equal(
                SnakeRun.RulesetId,
                root.GetProperty("engine").GetProperty("ruleset_id").GetString());
            Assert.Equal(SnakeRun.RulesVersion, root.GetProperty("engine").GetProperty("rules_version").GetInt32());
            Assert.Equal(
                run.ComputeStateHash(),
                root.GetProperty("actual").GetProperty("state_hash").GetString());
            Assert.Contains("dotnet test", root.GetProperty("reproduction_command").GetString());
            Assert.EndsWith("test-contract-v1-case_unsafe-step-000003.json", path);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Bundle_rejects_incomplete_identity()
    {
        var request = new ParityDivergenceRequest(
            Contract: " ",
            Fixture: "fixture.json",
            TestFilter: "test",
            CaseId: "case",
            Seed: null,
            FirstDivergentStep: 0,
            InitialState: new { },
            CommandPrefix: Array.Empty<object>(),
            ExpectedState: new { },
            ExpectedEvents: Array.Empty<object>(),
            ActualState: new { },
            ActualEvents: Array.Empty<object>(),
            ActualCanonicalState: new { },
            ActualStateHash: "hash");

        Assert.Throws<ArgumentException>(() => ParityDivergence.WriteBundle(request));
    }
}
