using System.Text;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class ScoreHistoryDocumentTests
{
    private static readonly string[] ImportedPlayerLabels = ["Top", "Lower"];
    private static readonly int[] ImportedScores = [100, 25];
    private static readonly int[] RankedScores = [110, 100, 90, 80, 70, 60, 50, 40, 30, 20];

    [Fact]
    public void Player_label_truncation_never_splits_a_surrogate_pair()
    {
        var normalized = ScoreHistoryDocument.CreateDefaults()
            .Add(
                Identity(1),
                At(1),
                new string('x', ScoreHistoryDocument.MaximumPlayerLabelCharacters - 1)
                    + "\U0001F40D"
                    + "suffix")
            .Document.Entries[0].PlayerLabel;

        Assert.Equal(
            new string('x', ScoreHistoryDocument.MaximumPlayerLabelCharacters - 1),
            normalized);
        Assert.DoesNotContain(normalized, char.IsSurrogate);
    }

    [Fact]
    public void Native_scores_are_ranked_bounded_and_separated_by_exact_category()
    {
        var document = ScoreHistoryDocument.CreateDefaults();
        foreach (var score in new[] { 10, 90, 30, 40, 50, 60, 70, 80, 20, 100 })
        {
            var update = document.Add(Identity(score), At(score));
            Assert.True(update.Retained);
            document = update.Document;
        }

        var rejected = document.Add(Identity(5), At(200));
        Assert.False(rejected.Retained);
        Assert.Null(rejected.Rank);
        Assert.Same(document, rejected.Document);

        var first = document.Add(Identity(110), At(201));
        Assert.True(first.Retained);
        Assert.Equal(1, first.Rank);
        Assert.Equal(10, first.Document.Entries.Count);
        Assert.Equal(
            RankedScores,
            first.Document.ScoresForCategory(first.Document.Entries[0].CategoryKey)
                .Select(entry => entry.Score));

        var challenge = Identity(45) with
        {
            RunKindId = ScoreRunContextCatalog.SeededChallengeRunKind,
            SeedCategoryId = ScoreRunContextCatalog.FixedChallengeSeedCategory,
            DisplayCategoryId = ScoreRunContextCatalog.SeededChallenge.DisplayCategoryId,
        };
        var separated = first.Document.Add(challenge, At(202));
        Assert.Equal(11, separated.Document.Entries.Count);
        Assert.Equal(2, separated.Document.Entries.Select(entry => entry.CategoryKey).Distinct().Count());
        Assert.Equal(1, separated.Rank);
    }

    [Fact]
    public void Native_add_rejects_nonterminal_noncompetitive_invalid_and_null_inputs()
    {
        var defaults = ScoreHistoryDocument.CreateDefaults();
        Assert.Throws<ArgumentNullException>(() => defaults.Add(null!, At(1)));
        Assert.Throws<ArgumentException>(
            () => defaults.Add(Identity(1) with { Status = RunStatus.Running }, At(1)));
        Assert.Throws<ArgumentException>(
            () => defaults.Add(
                Identity(1) with
                {
                    RunKindId = ScoreRunContextCatalog.TutorialRunKind,
                    SeedCategoryId = ScoreRunContextCatalog.TutorialSeedCategory,
                    DisplayCategoryId = ScoreRunContextCatalog.Tutorial.DisplayCategoryId,
                    CompetitiveEligible = false,
                },
                At(1)));
        Assert.Throws<InvalidDataException>(
            () => defaults.Add(Identity(1) with { ConfigHash = "bad" }, At(1)));
        Assert.Throws<ArgumentNullException>(
            () => defaults.Add(Identity(1), At(1), null!));
        Assert.Throws<ArgumentException>(() => defaults.ScoresForCategory(" "));

        var fullCategories = defaults;
        for (var index = 0; index < ScoreHistoryDocument.MaximumCategoryCount; index++)
        {
            fullCategories = fullCategories.Add(
                Identity(index) with { RulesetId = "rules-" + index },
                At(index)).Document;
        }

        Assert.Throws<InvalidOperationException>(() => fullCategories.Add(
            Identity(1) with { RulesetId = "one-category-too-many" },
            At(100)));
    }

    [Fact]
    public void Personal_best_merge_is_idempotent_and_preserves_current_and_legacy_visibility()
    {
        var current = PersonalBestDocument.CreateDefaults().Apply(Identity(250)).Document;
        var legacyJson = $$"""
            {"schemaVersion":1,"entries":[{"rulesetId":"vibesnake-core","rulesVersion":4,"configHash":"{{new string('b', 64)}}","configHashAlgorithm":"sha256-canonical-runconfig-v1","bestScore":125}]}
            """;
        var legacy = PersonalBestDocument.Read(legacyJson).Document!;
        var combined = new PersonalBestDocument(
            PersonalBestDocument.CurrentSchemaVersion,
            current.Entries.Concat(legacy.Entries).ToArray());

        var first = ScoreHistoryDocument.CreateDefaults().MergePersonalBests(combined);
        Assert.Equal(2, first.AddedEntryCount);
        Assert.Equal(2, first.Document.Entries.Count);
        Assert.All(
            first.Document.Entries,
            entry => Assert.Equal(ScoreHistoryDocument.PersonalBestMigrationSourceId, entry.SourceId));
        Assert.Contains(first.Document.Entries, entry => entry.RecordedAtUtc == "unknown");

        var second = first.Document.MergePersonalBests(combined);
        Assert.Equal(0, second.AddedEntryCount);
        Assert.Equal(first.Document.SerializeCanonical(), second.Document.SerializeCanonical());
        Assert.Throws<ArgumentNullException>(
            () => first.Document.MergePersonalBests(null!));

        var afterImport = first.Document.ImportPythonTopTen(
            [new PythonScoreEntry(0, "Alpha", 50, "2026-01-01T00:00:00Z")],
            new string('a', 64));
        Assert.Equal(3, afterImport.Entries.Count);
        Assert.Equal(2, afterImport.Entries.Count(entry =>
            entry.SourceId == ScoreHistoryDocument.PersonalBestMigrationSourceId));
        Assert.Contains(
            afterImport.Entries,
            entry => entry.SourceId == ScoreHistoryDocument.PythonTopTenSourceId && entry.Score == 50);
        Assert.Contains(
            afterImport.Entries,
            entry => entry.SourceId == ScoreHistoryDocument.PersonalBestMigrationSourceId
                && entry.Score == 250);
        Assert.Contains(
            afterImport.Entries,
            entry => entry.SourceId == ScoreHistoryDocument.PersonalBestMigrationSourceId
                && entry.Score == 125);
    }

    [Fact]
    public void Canonical_round_trip_is_strict_sorted_and_bounded()
    {
        var document = ScoreHistoryDocument.CreateDefaults()
            .Add(Identity(20, 'b'), At(2), " PLAYER\nONE ").Document
            .Add(Identity(30, 'a'), At(1)).Document;
        var canonical = document.SerializeCanonical();
        var read = ScoreHistoryDocument.Read(canonical);

        Assert.True(read.IsSuccess);
        Assert.Equal(canonical, read.Document!.SerializeCanonical());
        Assert.Contains("PLAYER ONE", canonical, StringComparison.Ordinal);
        Assert.Equal(18, EntryFieldCount(read.Document.Entries[0]));
        Assert.True(
            string.CompareOrdinal(
                read.Document.Entries[0].CategoryKey,
                read.Document.Entries[1].CategoryKey) < 0);

        var tooLarge = new string('x', (int)ScoreHistoryDocument.MaximumDocumentBytes + 1);
        Assert.Equal(ScoreHistoryLoadCode.TooLarge, ScoreHistoryDocument.Read(tooLarge).Code);
        Assert.Equal(ScoreHistoryLoadCode.Empty, ScoreHistoryDocument.Read(" ").Code);
        Assert.Equal(ScoreHistoryLoadCode.InvalidJson, ScoreHistoryDocument.Read("{").Code);
    }

    [Fact]
    public void Parser_and_serializer_reject_shape_marker_sequence_identity_and_metadata_drift()
    {
        var document = ScoreHistoryDocument.CreateDefaults()
            .Add(Identity(20), At(1)).Document;
        var canonical = document.SerializeCanonical();
        string[] invalid =
        [
            "[]",
            "{}",
            canonical.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal),
            canonical.Replace("\"entries\":[", "\"unknown\":0,\"entries\":[", StringComparison.Ordinal),
            canonical.Replace("\"sequence\":1", "\"sequence\":2", StringComparison.Ordinal),
            canonical.Replace("\"nextSequence\":2", "\"nextSequence\":0", StringComparison.Ordinal),
            canonical.Replace("\"playerLabel\":\"LOCAL PLAYER\"", "\"playerLabel\":\"\"", StringComparison.Ordinal),
            canonical.Replace("\"recordedAtUtc\":", "\"recordedAtUtc\":\"dup\",\"recordedAtUtc\":", StringComparison.Ordinal),
            canonical.Replace("\"sourceId\":\"native-terminal\"", "\"sourceId\":\"unknown\"", StringComparison.Ordinal),
            canonical.Replace("\"runKindId\":\"normal-human\"", "\"runKindId\":\"legacy-0.2\"", StringComparison.Ordinal),
            canonical.Replace("\"score\":20", "\"score\":-1", StringComparison.Ordinal),
            canonical.Replace("\"pythonTopTenImported\":false", "\"pythonTopTenImported\":true", StringComparison.Ordinal),
            canonical.Replace("\"pythonTopTenSourceSha256\":\"\"", "\"pythonTopTenSourceSha256\":\"bad\"", StringComparison.Ordinal),
            canonical.Replace("\"pythonTopTenImportedCount\":0", "\"pythonTopTenImportedCount\":1", StringComparison.Ordinal),
        ];

        foreach (var payload in invalid)
        {
            var result = ScoreHistoryDocument.Read(payload);
            Assert.False(result.IsSuccess, payload);
            Assert.Null(result.Document);
        }

        var invalidDocument = ScoreHistoryDocument.CreateDefaults() with { NextSequence = 0 };
        Assert.Throws<InvalidDataException>(() => invalidDocument.SerializeCanonical());
        var validEntry = document.Entries[0];
        Assert.Throws<InvalidDataException>(() => new ScoreHistoryDocument(
            1, 2, false, string.Empty, 0, [validEntry, validEntry]).SerializeCanonical());
        Assert.Throws<InvalidDataException>(() => new ScoreHistoryDocument(
            1,
            12,
            false,
            string.Empty,
            0,
            Enumerable.Range(1, 11).Select(sequence => validEntry with { Sequence = sequence }).ToArray())
            .SerializeCanonical());
        Assert.Throws<InvalidDataException>(() => new ScoreHistoryDocument(
            1,
            66,
            false,
            string.Empty,
            0,
            Enumerable.Range(1, 65).Select(sequence => validEntry with
            {
                Sequence = sequence,
                RulesetId = "rules-" + sequence,
            }).ToArray()).SerializeCanonical());
        Assert.Throws<InvalidDataException>(() => new ScoreHistoryDocument(
            1,
            2,
            true,
            new string('a', 64),
            1,
            [validEntry]).SerializeCanonical());
        Assert.Throws<InvalidDataException>(() => new ScoreHistoryDocument(
            1,
            2,
            false,
            string.Empty,
            0,
            [validEntry with { SourceId = ScoreHistoryDocument.PythonTopTenSourceId }])
            .SerializeCanonical());
    }

    [Fact]
    public void Explicit_python_import_is_one_time_hash_recorded_sorted_and_source_preserving()
    {
        var root = CreateRoot();
        try
        {
            var store = new ScoreHistoryStore(root);
            var source = PythonPayload(
                "{\"name\":\" Lower \",\"score\":25,\"timestamp\":\"2026-01-02T00:00:00\"},"
                + "{\"name\":\"Top\",\"score\":100,\"timestamp\":\"2026-01-01T00:00:00\"}");
            Directory.CreateDirectory(Path.GetDirectoryName(store.PythonImportInboxPath)!);
            File.WriteAllText(store.PythonImportInboxPath, source);
            var sourceBefore = File.ReadAllBytes(store.PythonImportInboxPath);

            var imported = store.ImportPythonTopTen();

            Assert.Equal(PythonScoreImportCode.Success, imported.Code);
            Assert.Equal(2, imported.ImportedEntryCount);
            Assert.NotNull(imported.SourceSha256);
            Assert.Equal(sourceBefore, File.ReadAllBytes(store.PythonImportInboxPath));
            Assert.Equal(ImportedScores, imported.Document!.Entries.Select(entry => entry.Score));
            Assert.Equal(ImportedPlayerLabels, imported.Document.Entries.Select(entry => entry.PlayerLabel));
            Assert.All(imported.Document.Entries, entry =>
            {
                Assert.Equal(ScoreRunContextCatalog.LegacyDisplayCategory, entry.DisplayCategoryId);
                Assert.Equal(ScoreHistoryDocument.PythonTopTenSourceId, entry.SourceId);
            });
            var importedEntry = imported.Document.Entries[0];
            Assert.Throws<InvalidDataException>(() => new ScoreHistoryDocument(
                1,
                imported.Document.NextSequence,
                true,
                imported.Document.PythonTopTenSourceSha256,
                2,
                imported.Document.Entries.Select(entry => entry == importedEntry
                    ? entry with { SourceId = ScoreHistoryDocument.NativeTerminalSourceId }
                    : entry).ToArray()).SerializeCanonical());
            var legacyReport = ScoreBrowseReport.Create(
                imported.Document,
                PersonalBestDocument.CreateDefaults());
            var legacyCategory = Assert.Single(legacyReport.Categories);
            Assert.Equal(ScoreRunContextCatalog.LegacyDisplayCategory, legacyCategory.DisplayName);
            Assert.Equal("UNKNOWN HISTORICAL RULES / NONCOMPETITIVE", legacyCategory.IdentityLine);
            Assert.False(legacyCategory.Competitive);
            Assert.Null(legacyCategory.PersonalBest);

            var again = store.ImportPythonTopTen();
            Assert.Equal(PythonScoreImportCode.AlreadyImported, again.Code);
            Assert.True(again.IsSuccess);
            Assert.Equal(imported.SourceSha256, again.SourceSha256);
            Assert.Equal(2, again.ImportedEntryCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Python_import_rejects_missing_oversized_malformed_future_and_wrong_fields()
    {
        var root = CreateRoot();
        try
        {
            var store = new ScoreHistoryStore(root);
            Assert.Equal(PythonScoreImportCode.SourceNotFound, store.ImportPythonTopTen().Code);
            Assert.False(store.ImportPythonTopTen().IsSuccess);
            Assert.True(Directory.Exists(Path.GetDirectoryName(store.PythonImportInboxPath)));

            File.WriteAllBytes(
                store.PythonImportInboxPath,
                new byte[ScoreHistoryStore.MaximumPythonSourceBytes + 1]);
            Assert.Equal(PythonScoreImportCode.SourceTooLarge, store.ImportPythonTopTen().Code);

            string[] invalid =
            [
                "{",
                "[]",
                "{}",
                PythonPayload(string.Empty).Replace("\"schema_version\":1", "\"schema_version\":2", StringComparison.Ordinal),
                PythonPayload(string.Empty).Replace("\"schema_version\":1", "\"schema_version\":true", StringComparison.Ordinal),
                PythonPayload(string.Empty).Replace("\"legacy_highscore_json\":true", "\"legacy_highscore_json\":1", StringComparison.Ordinal),
                PythonPayload(string.Empty).Replace("\"scores\":[]", "\"scores\":{},", StringComparison.Ordinal),
                PythonPayload("{\"name\":\"A\",\"score\":true,\"timestamp\":\"t\"}"),
                PythonPayload("{\"name\":\"A\",\"score\":1,\"timestamp\":null}"),
                PythonPayload("{\"name\":\"A\",\"score\":1,\"timestamp\":\"\"}"),
                PythonPayload("{\"name\":\"A\",\"score\":1,\"timestamp\":\"t\",\"extra\":0}"),
                PythonPayload(string.Join(",", Enumerable.Repeat(
                    "{\"name\":\"A\",\"score\":1,\"timestamp\":\"t\"}", 11))),
            ];

            foreach (var payload in invalid)
            {
                File.WriteAllText(store.PythonImportInboxPath, payload);
                Assert.Equal(PythonScoreImportCode.InvalidSource, store.ImportPythonTopTen().Code);
                Assert.False(File.Exists(store.ScoreHistoryPath));
            }

            File.WriteAllText(store.PythonImportInboxPath, PythonPayload(string.Empty));
            using var locked = new FileStream(
                store.PythonImportInboxPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.Equal(PythonScoreImportCode.IoError, store.ImportPythonTopTen().Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Store_round_trips_atomically_validates_root_and_blocks_bad_destination()
    {
        Assert.Throws<ArgumentException>(() => new ScoreHistoryStore("relative"));
        Assert.Throws<ArgumentException>(() => new ScoreHistoryStore(" "));
        var root = CreateRoot();
        try
        {
            var store = new ScoreHistoryStore(root);
            Assert.True(store.Load().IsSuccess);
            var document = ScoreHistoryDocument.CreateDefaults().Add(Identity(99), At(1)).Document;
            store.Save(document);
            Assert.Equal(document.SerializeCanonical(), store.Load().Document!.SerializeCanonical());
            Assert.False(File.Exists(store.ScoreHistoryPath + ".tmp"));

            File.WriteAllText(store.ScoreHistoryPath, "future");
            Directory.CreateDirectory(Path.GetDirectoryName(store.PythonImportInboxPath)!);
            File.WriteAllText(store.PythonImportInboxPath, PythonPayload(string.Empty));
            Assert.Equal(
                PythonScoreImportCode.DestinationBlocked,
                store.ImportPythonTopTen().Code);

            File.WriteAllBytes(
                store.ScoreHistoryPath,
                new byte[ScoreHistoryDocument.MaximumDocumentBytes + 1]);
            Assert.Equal(ScoreHistoryLoadCode.TooLarge, store.Load().Code);

            File.Delete(store.ScoreHistoryPath);
            store.Save(document);
            using (var locked = new FileStream(
                       store.ScoreHistoryPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                Assert.Equal(ScoreHistoryLoadCode.IoError, store.Load().Code);
            }

            Assert.Throws<ArgumentNullException>(() => store.Save(null!));
            File.Delete(store.ScoreHistoryPath);
            Directory.CreateDirectory(store.ScoreHistoryPath);
            var saveFailure = Record.Exception(() => store.Save(document));
            Assert.True(
                saveFailure is IOException or UnauthorizedAccessException,
                saveFailure?.ToString());
            Assert.False(File.Exists(store.ScoreHistoryPath + ".tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Browse_report_orders_current_challenge_and_legacy_with_identity_and_bests()
    {
        var normal = Identity(100);
        var challenge = Identity(80, 'b') with
        {
            RunKindId = ScoreRunContextCatalog.SeededChallengeRunKind,
            SeedCategoryId = ScoreRunContextCatalog.FixedChallengeSeedCategory,
            DisplayCategoryId = ScoreRunContextCatalog.SeededChallenge.DisplayCategoryId,
        };
        var history = ScoreHistoryDocument.CreateDefaults()
            .Add(challenge, At(2)).Document
            .Add(normal, At(1)).Document;
        var bests = PersonalBestDocument.CreateDefaults()
            .Apply(normal).Document
            .Apply(challenge).Document;
        var report = ScoreBrowseReport.Create(history, bests);

        Assert.True(report.HasCategories);
        Assert.Equal(2, report.Categories.Count);
        Assert.EndsWith("/ NORMAL-HUMAN", report.Categories[0].DisplayName, StringComparison.Ordinal);
        Assert.EndsWith("/ SEEDED-CHALLENGE", report.Categories[1].DisplayName, StringComparison.Ordinal);
        Assert.Equal(100, report.Categories[0].PersonalBest);
        Assert.True(report.Categories[0].Competitive);
        Assert.Contains("vibesnake-core@4", report.Categories[0].IdentityLine, StringComparison.Ordinal);

        var empty = ScoreBrowseReport.Create(
            ScoreHistoryDocument.CreateDefaults(),
            PersonalBestDocument.CreateDefaults());
        Assert.False(empty.HasCategories);
        Assert.Empty(empty.Categories);
        Assert.Throws<ArgumentNullException>(
            () => ScoreBrowseReport.Create(null!, PersonalBestDocument.CreateDefaults()));
        Assert.Throws<ArgumentNullException>(
            () => ScoreBrowseReport.Create(ScoreHistoryDocument.CreateDefaults(), null!));
    }

    [Fact]
    public void Browse_report_handles_personal_best_only_and_adapted_history_only_categories()
    {
        var fixedIdentity = Identity(75);
        var fixedBest = PersonalBestDocument.CreateDefaults().Apply(fixedIdentity).Document;
        var bestOnly = ScoreBrowseReport.Create(ScoreHistoryDocument.CreateDefaults(), fixedBest);
        var bestCategory = Assert.Single(bestOnly.Categories);
        Assert.Equal(75, bestCategory.PersonalBest);
        Assert.Empty(bestCategory.Scores);
        Assert.Contains("DDA OFF", bestCategory.IdentityLine, StringComparison.Ordinal);

        var adapted = Identity(90, 'c') with
        {
            ScoreCategoryId = RunModeCatalog.VibeAdaptiveScoreCategoryId,
            AdaptationEnabled = true,
            AdaptivePolicyId = AdaptiveDifficultyPolicy.CurrentPolicyId,
        };
        var adaptedHistory = ScoreHistoryDocument.CreateDefaults().Add(adapted, At(3)).Document;
        var historyOnly = ScoreBrowseReport.Create(
            adaptedHistory,
            PersonalBestDocument.CreateDefaults());
        var historyCategory = Assert.Single(historyOnly.Categories);
        Assert.Null(historyCategory.PersonalBest);
        Assert.Single(historyCategory.Scores);
        Assert.Contains("DDA ON", historyCategory.IdentityLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Constants_and_label_normalization_are_stable()
    {
        Assert.Equal(1, ScoreHistoryDocument.CurrentSchemaVersion);
        Assert.Equal("score_history.json", ScoreHistoryDocument.FileName);
        Assert.Equal(10, ScoreHistoryDocument.MaximumScoresPerCategory);
        Assert.Equal(640, ScoreHistoryDocument.MaximumEntryCount);
        var anonymous = ScoreHistoryDocument.CreateDefaults()
            .Add(Identity(1), At(1), "\n\t").Document.Entries[0].PlayerLabel;
        Assert.Equal(
            new string('x', ScoreHistoryDocument.MaximumPlayerLabelCharacters),
            ScoreHistoryDocument.CreateDefaults()
                .Add(Identity(1), At(1), new string('x', 100))
                .Document.Entries[0].PlayerLabel);
        Assert.Equal("Anonymous", anonymous);
    }

    private static RunScoreIdentity Identity(int score, char hashCharacter = 'a') => new(
        SnakeRun.RulesetId,
        SnakeRun.RulesVersion,
        new string(hashCharacter, 64),
        RunConfig.ConfigHashAlgorithmId,
        score,
        RunStatus.Dead,
        DeathCause.SelfCollision);

    private static DateTimeOffset At(int seconds) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

    private static int EntryFieldCount(ScoreHistoryEntry entry)
    {
        var document = new ScoreHistoryDocument(
            ScoreHistoryDocument.CurrentSchemaVersion,
            entry.Sequence + 1,
            false,
            string.Empty,
            0,
            [entry]);
        using var parsed = System.Text.Json.JsonDocument.Parse(document.SerializeCanonical());
        return parsed.RootElement.GetProperty("entries")[0].EnumerateObject().Count();
    }

    private static string PythonPayload(string entries) =>
        "{\"schema_version\":1,\"migrations\":{\"legacy_highscore_json\":true},\"scores\":["
        + entries
        + "]}";

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-score-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
