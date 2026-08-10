using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class HumanPlaytestProtocolTests
{
    private static readonly string[] ExpectedCohorts =
    [
        "first-time-keyboard",
        "first-time-controller",
        "returning-arcade",
        "accessibility-focused",
    ];

    private static readonly string[] ExpectedStages =
    [
        "formative",
        "targeted-follow-up",
        "fresh-validation",
    ];

    private static readonly string[] ExpectedScenarios =
    [
        "first-launch",
        "tutorial",
        "mode-selection",
        "seeded-run",
        "death-attribution",
        "deliberate-restart",
        "settings-discovery",
        "fixed-seed-recovery",
        "voluntary-replay",
        "boost-phase-shift",
        "slow-mo-magnet",
        "bait-boost",
        "gluttony-magnet",
        "segment-detach-protection",
        "last-stand-long-combo",
    ];

    private static readonly string[] ExpectedProfiles =
    [
        "default",
        "muted",
        "reduced-motion",
        "flash-free",
        "high-contrast",
        "controller-only",
    ];

    private static readonly string[] ExpectedObservationFields =
    [
        "comprehension",
        "observedErrors",
        "chosenRoutes",
        "deathAttribution",
        "restartSuccess",
        "settingsDiscovery",
        "qualitativeFeedback",
        "recoveryAnticipated",
        "recoveryAttributable",
        "recoveryControllable",
        "recoveryWorthRetrying",
        "voluntaryReplay",
        "replayMotivation",
        "powerTypeIdentified",
        "powerVisibilityReadable",
        "powerDetourIntent",
        "powerSynergyExplained",
        "powerSaveAttributed",
        "powerDeathAdjacencyAttributed",
    ];

    [Fact]
    public void Human_playtest_protocol_is_closed_privacy_bounded_and_experience_unverified()
    {
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var protocolPath = Path.Combine(
            repositoryRoot,
            "config",
            "qa_human_playtest_protocol.json");
        var bytes = File.ReadAllBytes(protocolPath);
        using var parsed = JsonDocument.Parse(bytes);
        var root = parsed.RootElement;

        RequireExactFields(root,
        [
            "schemaVersion", "kind", "status", "privacy", "cohorts", "stages",
            "scenarios", "recoveryProfiles", "requiredBuildFields",
            "requiredObservationFields", "severityDefinitions", "repeatedPatternRule",
            "stopRules", "decisionValues", "requiredArtifactPaths", "humanTargetRanges",
        ]);
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("vibesnake-human-playtest-protocol-v1", root.GetProperty("kind").GetString());
        Assert.Equal(
            "automated-qualified-experience-unverified",
            root.GetProperty("status").GetString());

        var privacy = root.GetProperty("privacy");
        RequireExactFields(privacy,
        [
            "participantIdFormat", "consentRecordedOutsideRepository",
            "localSummarySharingOptional", "allowedParticipantFields",
            "forbiddenFieldFamilies",
        ]);
        Assert.Equal("session-[0-9]{3}", privacy.GetProperty("participantIdFormat").GetString());
        Assert.True(privacy.GetProperty("consentRecordedOutsideRepository").GetBoolean());
        Assert.True(privacy.GetProperty("localSummarySharingOptional").GetBoolean());
        Assert.Equal(6, privacy.GetProperty("allowedParticipantFields").GetArrayLength());
        Assert.Equal(10, privacy.GetProperty("forbiddenFieldFamilies").GetArrayLength());

        var cohorts = root.GetProperty("cohorts").EnumerateArray().ToArray();
        Assert.Equal(ExpectedCohorts, cohorts.Select(item => item.GetProperty("id").GetString()));
        Assert.All(cohorts, cohort =>
        {
            RequireExactFields(cohort, ["id", "eligibility"]);
            Assert.False(string.IsNullOrWhiteSpace(cohort.GetProperty("eligibility").GetString()));
        });

        var stages = root.GetProperty("stages").EnumerateArray().ToArray();
        Assert.Equal(ExpectedStages, stages.Select(item => item.GetProperty("id").GetString()));
        Assert.All(stages, stage => RequireExactFields(
            stage,
            ["id", "freshParticipantsRequired", "purpose"]));
        Assert.False(stages[0].GetProperty("freshParticipantsRequired").GetBoolean());
        Assert.False(stages[1].GetProperty("freshParticipantsRequired").GetBoolean());
        Assert.True(stages[2].GetProperty("freshParticipantsRequired").GetBoolean());

        var scenarios = root.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.Equal(ExpectedScenarios, scenarios.Select(item => item.GetProperty("id").GetString()));
        Assert.All(scenarios, scenario => RequireExactFields(
            scenario,
            ["id", "fixedSeed", "requiredObservations"]));
        var requiredObservationFields = ReadStrings(root, "requiredObservationFields");
        Assert.Equal(ExpectedObservationFields, requiredObservationFields);
        Assert.Equal(
            ExpectedObservationFields.Order(StringComparer.Ordinal),
            scenarios
                .SelectMany(item => ReadStrings(item, "requiredObservations"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        var reviewedSeeds = ReadReviewedSeeds(repositoryRoot);
        var scenarioSeeds = scenarios
            .Select(item => item.GetProperty("fixedSeed"))
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => ulong.Parse(
                item.GetString()!,
                System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.All(scenarioSeeds, seed => Assert.Contains(seed, reviewedSeeds));
        Assert.Equal(ExpectedProfiles, ReadStrings(root, "recoveryProfiles"));
        Assert.Equal(13, root.GetProperty("requiredBuildFields").GetArrayLength());

        var severities = root.GetProperty("severityDefinitions").EnumerateArray().ToArray();
        Assert.Equal(4, severities.Length);
        Assert.Equal(
            ["severity-1", "severity-2", "severity-3", "severity-4"],
            severities.Select(item => item.GetProperty("id").GetString()));
        Assert.All(severities, severity => RequireExactFields(severity, ["id", "meaning"]));
        Assert.Contains(
            "two or more sessions",
            root.GetProperty("repeatedPatternRule").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(5, root.GetProperty("stopRules").GetArrayLength());
        Assert.Equal(
            ["keep", "revise", "remove", "blocked"],
            ReadStrings(root, "decisionValues"));

        var requiredArtifacts = ReadStrings(root, "requiredArtifactPaths");
        Assert.Equal(11, requiredArtifacts.Count);
        Assert.Equal(requiredArtifacts.Count, requiredArtifacts.Distinct(StringComparer.Ordinal).Count());
        Assert.All(requiredArtifacts, path =>
        {
            Assert.StartsWith("TestResults/native/", path, StringComparison.Ordinal);
            Assert.False(Path.IsPathFullyQualified(path));
        });
        Assert.Empty(root.GetProperty("humanTargetRanges").EnumerateArray());

        var evidence = new
        {
            schemaVersion = 1,
            kind = "human-playtest-handoff-v1",
            passed = true,
            status = "automated-qualified-experience-unverified",
            protocolSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            cohortCount = cohorts.Length,
            stageCount = stages.Length,
            scenarioCount = scenarios.Length,
            recoveryProfileCount = ExpectedProfiles.Length,
            requiredBuildFieldCount = root.GetProperty("requiredBuildFields").GetArrayLength(),
            requiredObservationFieldCount = requiredObservationFields.Count,
            severityCount = severities.Length,
            requiredArtifactPaths = requiredArtifacts,
            privacyForbiddenFieldFamilyCount = privacy
                .GetProperty("forbiddenFieldFamilies")
                .GetArrayLength(),
            humanSessionCount = 0,
            experienceVerified = false,
            humanTargetRangesEstablished = false,
            notes = new[]
            {
                "This evidence qualifies the protocol and automated handoff only.",
                "No participant observation, experience claim, or human target is inferred.",
                "V070-06 remains open until formative, targeted, and fresh validation evidence is reviewed.",
            },
        };
        var outputDirectory = Path.Combine(repositoryRoot, "TestResults", "native");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "human_playtest_handoff.json");
        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.True(File.Exists(outputPath));
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement parent, string property) =>
        parent.GetProperty(property)
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

    private static IReadOnlySet<ulong> ReadReviewedSeeds(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "config", "qa_seed_corpora.json");
        using var parsed = JsonDocument.Parse(File.ReadAllBytes(path));
        return parsed.RootElement
            .GetProperty("corpora")
            .EnumerateArray()
            .Where(item => item.GetProperty("reviewed").GetBoolean())
            .SelectMany(item => item.GetProperty("seeds").EnumerateArray())
            .Select(item => item.GetUInt64())
            .ToHashSet();
    }

    private static void RequireExactFields(JsonElement element, params string[] expected)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        var observed = element.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(observed.Length, observed.Distinct(StringComparer.Ordinal).Count());
        Assert.True(observed.ToHashSet(StringComparer.Ordinal).SetEquals(expected));
    }
}
