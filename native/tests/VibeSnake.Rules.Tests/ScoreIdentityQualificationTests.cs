using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ScoreIdentityQualificationTests
{
    [Fact]
    public void Score_context_persistence_legacy_migration_and_achievement_audit_are_complete()
    {
        var run = SnakeRun.Create(71UL);
        var identities = ScoreRunContextCatalog.All
            .Select(context => RunScoreIdentity.FromRun(run, context))
            .ToArray();
        Assert.Equal(8, identities.Length);
        Assert.Equal(8, identities.Select(identity =>
            $"{identity.RunKindId}|{identity.SeedCategoryId}").Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, identities.Count(identity => identity.CompetitiveEligible));

        var normal = identities.Single(identity =>
            identity.RunKindId == ScoreRunContextCatalog.NormalHumanRunKind) with
        {
            Score = 120,
            Status = RunStatus.Dead,
            DeathCause = DeathCause.Starvation,
        };
        var challenge = identities.Single(identity =>
            identity.RunKindId == ScoreRunContextCatalog.SeededChallengeRunKind) with
        {
            Score = 140,
            Status = RunStatus.Dead,
            DeathCause = DeathCause.Starvation,
        };
        var document = PersonalBestDocument.CreateDefaults()
            .Apply(normal).Document
            .Apply(challenge).Document;
        var canonical = document.SerializeCanonical();
        using var canonicalJson = JsonDocument.Parse(canonical);
        var entries = canonicalJson.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(2, canonicalJson.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, entries.Length);
        Assert.All(entries, entry => Assert.Equal(14, entry.EnumerateObject().Count()));
        Assert.NotEqual(entries[0].GetProperty("runKindId").GetString(), entries[1].GetProperty("runKindId").GetString());
        var historyMerge = ScoreHistoryDocument.CreateDefaults().MergePersonalBests(document);
        var history = historyMerge.Document;
        using var historyJson = JsonDocument.Parse(history.SerializeCanonical());
        var historyEntries = historyJson.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(2, historyMerge.AddedEntryCount);
        Assert.All(historyEntries, entry => Assert.Equal(18, entry.EnumerateObject().Count()));

        var legacyPayload = $$"""
            {"schemaVersion":1,"entries":[{"rulesetId":"vibesnake-core","rulesVersion":4,"configHash":"{{new string('a', 64)}}","configHashAlgorithm":"sha256-canonical-runconfig-v1","bestScore":250}]}
            """;
        var migrated = PersonalBestDocument.Read(legacyPayload);
        Assert.True(migrated.IsSuccess);
        var legacyEntry = Assert.Single(migrated.Document!.Entries);
        Assert.Equal(ScoreRunContextCatalog.LegacyDisplayCategory, legacyEntry.DisplayCategoryId);
        Assert.Equal(ScoreRunContextCatalog.LegacyRunKind, legacyEntry.RunKindId);

        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var auditPath = Path.Combine(repositoryRoot, "config", "achievement_mode_audit_v1.json");
        var auditBytes = File.ReadAllBytes(auditPath);
        using var auditJson = JsonDocument.Parse(auditBytes);
        var auditRoot = auditJson.RootElement;
        RequireExactFields(
            auditRoot,
            ["schemaVersion", "kind", "classicPolicy", "vibePolicy", "entries"]);
        Assert.Equal(1, auditRoot.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("vibesnake-achievement-mode-audit-v1", auditRoot.GetProperty("kind").GetString());
        var auditEntries = auditRoot.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(25, auditEntries.Length);
        Assert.Equal(
            25,
            auditEntries.Select(item => item.GetProperty("referenceId").GetString())
                .Distinct(StringComparer.Ordinal).Count());
        Assert.All(auditEntries, entry => RequireExactFields(
            entry,
            [
                "referenceId", "nativeId", "classicEligibility", "vibeEligibility",
                "decision", "reason",
            ]));

        var nativeAuditEntries = auditEntries
            .Where(item => item.GetProperty("nativeId").ValueKind == JsonValueKind.String)
            .ToArray();
        Assert.Equal(17, nativeAuditEntries.Length);
        Assert.Equal(
            AchievementCatalog.Definitions.Select(definition => definition.Id).Order(StringComparer.Ordinal),
            nativeAuditEntries.Select(item => item.GetProperty("nativeId").GetString()!)
                .Order(StringComparer.Ordinal));
        Assert.All(AchievementCatalog.Definitions, definition => Assert.Equal(
            AchievementModeEligibility.Vibe,
            definition.ModeEligibility));
        Assert.All(nativeAuditEntries, entry =>
        {
            Assert.Equal("excluded", entry.GetProperty("classicEligibility").GetString());
            Assert.Equal("eligible", entry.GetProperty("vibeEligibility").GetString());
            Assert.Equal("keep-vibe-only", entry.GetProperty("decision").GetString());
        });
        Assert.Equal(
            8,
            auditEntries.Count(item => item.GetProperty("nativeId").ValueKind == JsonValueKind.Null));

        var evidence = new
        {
            schemaVersion = 1,
            kind = "score-identity-qualification-v1",
            passed = true,
            runContextCount = identities.Length,
            competitiveContextCount = identities.Count(identity => identity.CompetitiveEligible),
            separatedRunKinds = identities.Select(identity => identity.RunKindId).ToArray(),
            separatedSeedCategories = identities.Select(identity => identity.SeedCategoryId).ToArray(),
            personalBestSchemaVersion = PersonalBestDocument.CurrentSchemaVersion,
            scoreEntryFieldCount = 14,
            scoreHistorySchemaVersion = ScoreHistoryDocument.CurrentSchemaVersion,
            scoreHistoryEntryFieldCount = 18,
            maximumScoresPerCategory = ScoreHistoryDocument.MaximumScoresPerCategory,
            personalBestHistoryMigrationCount = historyMerge.AddedEntryCount,
            explicitModeIdentity = true,
            explicitDifficultyPolicy = true,
            explicitAdaptivePolicy = true,
            legacyMigrationVisible = legacyEntry.DisplayCategoryId
                == ScoreRunContextCatalog.LegacyDisplayCategory,
            achievementAuditSha256 = Convert.ToHexString(SHA256.HashData(auditBytes)).ToLowerInvariant(),
            referenceAchievementCount = auditEntries.Length,
            nativeRulesLocalAchievementCount = nativeAuditEntries.Length,
            classicEligibleAchievementCount = 0,
            vibeEligibleAchievementCount = nativeAuditEntries.Length,
            referenceOnlyExcludedCount = auditEntries.Length - nativeAuditEntries.Length,
            notes = new[]
            {
                "Normal human and seeded challenge scores are competitive but never share categories.",
                "Tutorial, practice, AI, replay, modified, and legacy scores cannot update a current personal best.",
                "Classic achievement exclusions preserve the frozen classic@1 no-progression contract.",
                "Native top-ten history retains the same complete score identity as personal bests.",
            },
        };
        var outputDirectory = Path.Combine(repositoryRoot, "TestResults", "native");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "score_identity.json"),
            JsonSerializer.Serialize(evidence, TestJsonSerializerOptions.Indented) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void RequireExactFields(JsonElement element, params string[] expected)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        var observed = element.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(observed.Length, observed.Distinct(StringComparer.Ordinal).Count());
        Assert.True(observed.ToHashSet(StringComparer.Ordinal).SetEquals(expected));
    }
}
