using System.Globalization;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class PersonalBestDocumentTests
{
    [Fact]
    public void Apply_separates_categories_and_only_improves_scores()
    {
        var document = PersonalBestDocument.CreateDefaults();
        var first = document.Apply(Identity(score: 100));
        Assert.True(first.IsNewRecord);
        Assert.Null(first.PreviousBestScore);
        Assert.Equal(100, first.BestScore);

        var lower = first.Document.Apply(Identity(score: 80));
        Assert.False(lower.IsNewRecord);
        Assert.Equal(100, lower.PreviousBestScore);
        Assert.Equal(100, lower.BestScore);

        var higher = lower.Document.Apply(Identity(score: 150));
        Assert.True(higher.IsNewRecord);
        Assert.Equal(100, higher.PreviousBestScore);
        Assert.Equal(150, higher.BestScore);
        Assert.Equal(150, higher.Document.Find(Identity(score: 0))!.BestScore);

        var other = higher.Document.Apply(Identity(score: 20, hashCharacter: 'b'));
        Assert.Equal(2, other.Document.Entries.Count);
        Assert.Equal(20, other.BestScore);
    }

    [Fact]
    public void First_zero_score_is_stored_without_false_record_claim()
    {
        var update = PersonalBestDocument.CreateDefaults().Apply(Identity(score: 0));
        Assert.False(update.IsNewRecord);
        Assert.Equal(0, update.BestScore);
        Assert.Single(update.Document.Entries);
    }

    [Fact]
    public void Competitive_run_kinds_modes_and_adaptation_use_separate_explicit_categories()
    {
        var normal = Identity(score: 25);
        var challenge = normal with
        {
            RunKindId = ScoreRunContextCatalog.SeededChallengeRunKind,
            SeedCategoryId = ScoreRunContextCatalog.FixedChallengeSeedCategory,
            DisplayCategoryId = ScoreRunContextCatalog.SeededChallenge.DisplayCategoryId,
        };
        var classic = normal with
        {
            ModeId = RunModeCatalog.ClassicId,
            ScoreCategoryId = RunModeCatalog.ClassicScoreCategoryId,
            DifficultyPolicyId = RunModeCatalog.Classic.DifficultyPolicyId,
            ConfigHash = new string('b', 64),
        };
        var adapted = normal with
        {
            ScoreCategoryId = RunModeCatalog.VibeAdaptiveScoreCategoryId,
            AdaptationEnabled = true,
            AdaptivePolicyId = AdaptiveDifficultyPolicy.CurrentPolicyId,
            ConfigHash = new string('c', 64),
        };

        var document = PersonalBestDocument.CreateDefaults()
            .Apply(normal).Document
            .Apply(challenge).Document
            .Apply(classic).Document
            .Apply(adapted).Document;

        Assert.Equal(4, document.Entries.Count);
        Assert.Equal(4, document.Entries.Select(entry => entry.CategoryKey).Distinct().Count());
        Assert.All(document.Entries, entry => Assert.Equal(14, EntryFieldCount(entry)));
    }

    [Fact]
    public void Canonical_round_trip_is_sorted_and_strict()
    {
        var document = PersonalBestDocument.CreateDefaults()
            .Apply(Identity(score: 20, hashCharacter: 'b')).Document
            .Apply(Identity(score: 30, hashCharacter: 'a')).Document;
        var canonical = document.SerializeCanonical();
        var read = PersonalBestDocument.Read(canonical);

        Assert.True(read.IsSuccess);
        Assert.Equal(canonical, read.Document!.SerializeCanonical());
        Assert.True(
            string.CompareOrdinal(
                read.Document.Entries[0].CategoryKey,
                read.Document.Entries[1].CategoryKey) < 0);
    }

    [Fact]
    public void Rejects_nonterminal_invalid_and_exhausted_updates()
    {
        Assert.Throws<ArgumentException>(
            () => PersonalBestDocument.CreateDefaults().Apply(
                Identity(score: 10) with { Status = RunStatus.Running }));
        Assert.Throws<InvalidDataException>(
            () => PersonalBestDocument.CreateDefaults().Apply(
                Identity(score: 10) with { ConfigHash = "bad" }));
        Assert.Throws<ArgumentException>(
            () => PersonalBestDocument.CreateDefaults().Apply(
                Identity(score: 10) with
                {
                    RunKindId = ScoreRunContextCatalog.TutorialRunKind,
                    SeedCategoryId = ScoreRunContextCatalog.TutorialSeedCategory,
                    CompetitiveEligible = false,
                    DisplayCategoryId = ScoreRunContextCatalog.Tutorial.DisplayCategoryId,
                }));
        Assert.Throws<ArgumentNullException>(
            () => PersonalBestDocument.CreateDefaults().Apply(null!));
        Assert.Throws<ArgumentNullException>(
            () => PersonalBestDocument.CreateDefaults().Find(null!));

        var entries = Enumerable.Range(0, PersonalBestDocument.MaximumEntryCount)
            .Select(index => Entry(
                score: index,
                ruleset: "rules-" + index,
                hashCharacter: index.ToString("x64", CultureInfo.InvariantCulture)[0]))
            .ToArray();
        var full = new PersonalBestDocument(
            PersonalBestDocument.CurrentSchemaVersion,
            entries);
        Assert.Throws<InvalidOperationException>(
            () => full.Apply(Identity(score: 1, ruleset: "another")));
    }

    [Fact]
    public void Parser_rejects_wrong_shape_fields_duplicates_and_bounds()
    {
        var canonical = PersonalBestDocument.CreateDefaults()
            .Apply(Identity(score: 20)).Document.SerializeCanonical();
        string[] invalid =
        [
            "",
            "{",
            "[]",
            "{}",
            canonical.Replace("\"schemaVersion\":2", "\"schemaVersion\":3", StringComparison.Ordinal),
            canonical.Replace("\"entries\":[", "\"unknown\":1,\"entries\":[", StringComparison.Ordinal),
            canonical.Replace("\"rulesetId\":", "\"rulesetId\":\"dup\",\"rulesetId\":", StringComparison.Ordinal),
            canonical.Replace("\"rulesVersion\":4", "\"rulesVersion\":0", StringComparison.Ordinal),
            canonical.Replace(new string('a', 64), "short", StringComparison.Ordinal),
            canonical.Replace("\"bestScore\":20", "\"bestScore\":-1", StringComparison.Ordinal),
            canonical.Replace("\"bestScore\":20", "\"bestScore\":\"20\"", StringComparison.Ordinal),
            canonical.Replace("\"rulesetId\":\"vibesnake-core\"", "\"rulesetId\":null", StringComparison.Ordinal),
            canonical.Replace("\"runKindId\":\"normal-human\"", "\"runKindId\":\"tutorial\"", StringComparison.Ordinal),
            canonical.Replace("\"modeId\":\"vibe\"", "\"modeId\":\"future\"", StringComparison.Ordinal),
            canonical.Replace("\"adaptationEnabled\":false", "\"adaptationEnabled\":true", StringComparison.Ordinal),
        ];

        foreach (var payload in invalid)
        {
            var result = PersonalBestDocument.Read(payload);
            Assert.False(result.IsSuccess, payload);
            Assert.Null(result.Document);
        }
    }

    [Fact]
    public void Schema_one_entries_migrate_into_visible_legacy_categories()
    {
        var legacy = $$"""
            {"schemaVersion":1,"entries":[{"rulesetId":"vibesnake-core","rulesVersion":4,"configHash":"{{new string('a', 64)}}","configHashAlgorithm":"sha256-canonical-runconfig-v1","bestScore":250}]}
            """;

        var result = PersonalBestDocument.Read(legacy);

        Assert.True(result.IsSuccess);
        Assert.Contains("Legacy 0.2", result.Message, StringComparison.Ordinal);
        var entry = Assert.Single(result.Document!.Entries);
        Assert.Equal(PersonalBestDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Equal(PersonalBestDocument.LegacyModeId, entry.ModeId);
        Assert.Equal(ScoreRunContextCatalog.LegacyRunKind, entry.RunKindId);
        Assert.Equal(ScoreRunContextCatalog.LegacySeedCategory, entry.SeedCategoryId);
        Assert.Equal(ScoreRunContextCatalog.LegacyDisplayCategory, entry.DisplayCategoryId);
        Assert.False(entry.AdaptationEnabled);
        Assert.Equal(250, entry.BestScore);
        Assert.Contains("\"schemaVersion\":2", result.Document.SerializeCanonical(), StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_rejects_noncanonical_documents()
    {
        var valid = Entry(score: 1, ruleset: "core");
        Assert.Throws<InvalidDataException>(
            () => new PersonalBestDocument(3, [valid]).SerializeCanonical());
        Assert.Throws<InvalidDataException>(
            () => new PersonalBestDocument(
                PersonalBestDocument.CurrentSchemaVersion,
                [valid, valid]).SerializeCanonical());
        Assert.Throws<InvalidDataException>(
            () => new PersonalBestDocument(
                PersonalBestDocument.CurrentSchemaVersion,
                Enumerable.Repeat(valid, PersonalBestDocument.MaximumEntryCount + 1).ToArray())
                .SerializeCanonical());
    }

    [Fact]
    public void Store_round_trips_atomically_and_missing_file_defaults()
    {
        var root = CreateRoot();
        try
        {
            var store = new PersonalBestStore(root);
            Assert.True(store.Load().IsSuccess);
            Assert.Empty(store.Load().Document!.Entries);

            var document = PersonalBestDocument.CreateDefaults()
                .Apply(Identity(score: 99)).Document;
            store.Save(document);
            Assert.Equal(document.SerializeCanonical(), store.Load().Document!.SerializeCanonical());
            Assert.False(File.Exists(store.PersonalBestPath + ".tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Store_validates_root_and_reports_locked_read()
    {
        Assert.Throws<ArgumentException>(() => new PersonalBestStore("relative"));
        Assert.Throws<ArgumentException>(() => new PersonalBestStore(" "));
        var root = CreateRoot();
        try
        {
            var store = new PersonalBestStore(root);
            File.WriteAllText(store.PersonalBestPath, "locked");
            using var locked = new FileStream(
                store.PersonalBestPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.Equal(PersonalBestLoadCode.IoError, store.Load().Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Constants_are_stable()
    {
        Assert.Equal(2, PersonalBestDocument.CurrentSchemaVersion);
        Assert.Equal("personal_bests.json", PersonalBestDocument.FileName);
        Assert.Equal(64, PersonalBestDocument.MaximumEntryCount);
        Assert.Equal(128, PersonalBestDocument.MaximumIdentityCharacters);
    }

    private static RunScoreIdentity Identity(
        int score,
        char hashCharacter = 'a',
        string ruleset = "vibesnake-core") => new(
            ruleset,
            4,
            new string(hashCharacter, 64),
            "sha256-canonical-runconfig-v1",
            score,
            RunStatus.Dead,
            DeathCause.SelfCollision);

    private static PersonalBestEntry Entry(
        int score,
        string ruleset,
        char hashCharacter = 'a') => new(
            ruleset,
            4,
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            ScoreRunContextCatalog.NormalHumanRunKind,
            ScoreRunContextCatalog.FreshLocalSeedCategory,
            RunModeCatalog.VibeFixedScoreCategoryId,
            RunModeCatalog.Vibe.DifficultyPolicyId,
            AdaptationEnabled: false,
            AdaptiveDifficultyPolicy.DisabledPolicyId,
            ScoreRunContextCatalog.NormalHuman.DisplayCategoryId,
            new string(hashCharacter, 64),
            "hash-v1",
            score);

    private static int EntryFieldCount(PersonalBestEntry entry)
    {
        var document = new PersonalBestDocument(
            PersonalBestDocument.CurrentSchemaVersion,
            [entry]);
        using var parsed = System.Text.Json.JsonDocument.Parse(document.SerializeCanonical());
        return parsed.RootElement.GetProperty("entries")[0].EnumerateObject().Count();
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-personal-best-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
