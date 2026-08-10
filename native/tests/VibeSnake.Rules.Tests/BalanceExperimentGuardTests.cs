using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class BalanceExperimentGuardTests
{
    [Fact]
    public void Registry_blocks_tuning_until_human_targets_and_one_family_experiments_exist()
    {
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "config", "balance_experiments_v1.json");
        var bytes = File.ReadAllBytes(path);
        using var parsed = JsonDocument.Parse(bytes);
        var root = parsed.RootElement;

        RequireExactFields(root,
        [
            "schemaVersion", "kind", "status", "eligibleBalanceFamilies",
            "experienceEffects", "rules", "requiredExperimentFields", "decisionValues",
            "humanTargetRanges", "experiments",
        ]);
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "vibesnake-balance-experiment-registry-v1",
            root.GetProperty("kind").GetString());
        Assert.Equal("targets-pending-human-review", root.GetProperty("status").GetString());
        Assert.Equal(
            [
                "starvation", "speed", "combo", "power-frequency", "power-weights",
                "near-miss", "dda-bounds",
            ],
            ReadStrings(root, "eligibleBalanceFamilies"));
        Assert.Equal(
            ["competence", "autonomy", "tension", "recovery"],
            ReadStrings(root, "experienceEffects"));

        var rules = root.GetProperty("rules");
        RequireExactFields(rules,
        [
            "targetRangesRequiredBeforeChange", "exactlyOneBalanceFamilyPerExperiment",
            "fixedSeedCorpusRequired", "relevantHumanScenarioRequired",
            "averageScoreAloneProhibited", "configAndRulesIdentityRequired",
            "keepOrRevertDecisionRequired",
        ]);
        Assert.All(rules.EnumerateObject(), rule => Assert.True(rule.Value.GetBoolean(), rule.Name));
        Assert.Equal(18, root.GetProperty("requiredExperimentFields").GetArrayLength());
        Assert.Equal(["keep", "revert", "blocked"], ReadStrings(root, "decisionValues"));
        Assert.Empty(root.GetProperty("humanTargetRanges").EnumerateArray());
        Assert.Empty(root.GetProperty("experiments").EnumerateArray());

        var evidence = new
        {
            schemaVersion = 1,
            kind = "balance-experiment-guard-v1",
            passed = true,
            registrySha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            balanceFamilyCount = 7,
            experienceEffectCount = 4,
            requiredExperimentFieldCount = 18,
            humanTargetRangeCount = 0,
            experimentCount = 0,
            tuningEligible = false,
            blockedReason = "Structured human evidence has not established target ranges.",
            notes = new[]
            {
                "No balance value changed.",
                "Average score alone cannot authorize an experiment.",
                "Each future experiment is limited to one balance family and requires automated plus human evidence.",
            },
        };
        var outputDirectory = Path.Combine(repositoryRoot, "TestResults", "native");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "balance_experiment_guard.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement parent, string property) =>
        parent.GetProperty(property)
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

    private static void RequireExactFields(JsonElement element, params string[] expected)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        var observed = element.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(observed.Length, observed.Distinct(StringComparer.Ordinal).Count());
        Assert.True(observed.ToHashSet(StringComparer.Ordinal).SetEquals(expected));
    }
}
