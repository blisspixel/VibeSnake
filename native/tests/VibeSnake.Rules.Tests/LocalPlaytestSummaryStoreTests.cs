using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class LocalPlaytestSummaryStoreTests
{
    [Fact]
    public void Capture_round_trips_the_closed_balance_fact_schema()
    {
        var run = CreateTerminalRun(41UL);
        var captured = new DateTimeOffset(2026, 8, 8, 20, 10, 12, 345, TimeSpan.Zero);

        var summary = LocalPlaytestSummary.Capture(run, "0.2.1", captured);
        var document = LocalPlaytestSummaryDocument.CreateEmpty().Append(summary).Document;
        var payload = document.SerializeCanonical();
        var read = LocalPlaytestSummaryDocument.Read(payload);

        Assert.True(read.IsSuccess);
        var roundTripped = Assert.Single(read.Document!.Summaries);
        Assert.Equal(summary.SummaryId, roundTripped.SummaryId);
        Assert.Equal(summary.PowerDecisions, roundTripped.PowerDecisions);
        Assert.Equal(payload, read.Document.SerializeCanonical());
        Assert.Equal(64, summary.SummaryId.Length);
        Assert.Equal("2026-08-08T20:10:12.345Z", summary.CapturedAtUtc);
        Assert.Equal(LocalPlaytestSummary.HumanRunKind, summary.RunKind);
        Assert.Equal(run.MasterSeed!.Value.ToString(), summary.Seed);
        Assert.Equal(run.ScoreCategoryId, summary.ScoreCategoryId);
        Assert.Equal(run.ConfigHash, summary.ConfigHash);
        Assert.Equal(run.Tick, summary.SurvivalSteps);
        Assert.Equal(run.ComputeStateHash(), summary.FinalStateHash);
        Assert.Equal(9, summary.PowerDecisions!.Count);
        Assert.All(summary.PowerDecisions, item => item.Validate());
        Assert.DoesNotContain("name", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("device", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inputTiming", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upload", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capture_requires_a_terminal_seeded_run_and_valid_app_version()
    {
        var running = SnakeRun.Create(1UL, RunModeCatalog.CreateConfig(RunModeCatalog.Vibe));
        var unseeded = SnakeRun.CreateForTesting(
            RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
            {
                Width = 5,
                Height = 4,
                StarvationTicks = 1,
                StarvationWarningTicks = 0,
                PowerSpawnIntervalTicks = 0,
            },
            [new GridPoint(2, 2)],
            Direction.Right,
            new GridPoint(0, 0),
            hungerTicksRemaining: 1);
        unseeded.Step();

        Assert.Throws<ArgumentException>(() => LocalPlaytestSummary.Capture(
            running,
            "0.2.1",
            DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => LocalPlaytestSummary.Capture(
            unseeded,
            "0.2.1",
            DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => LocalPlaytestSummary.Capture(
            CreateTerminalRun(2UL),
            " ",
            DateTimeOffset.UnixEpoch));
        Assert.Throws<InvalidDataException>(() => LocalPlaytestSummary.Capture(
            CreateTerminalRun(3UL),
            "version with spaces",
            DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Reader_rejects_unknown_duplicate_missing_and_conflicting_fields()
    {
        var summary = LocalPlaytestSummary.Capture(
            CreateTerminalRun(4UL),
            "0.2.1",
            DateTimeOffset.UnixEpoch);
        var payload = LocalPlaytestSummaryDocument.CreateEmpty()
            .Append(summary)
            .Document
            .SerializeCanonical();

        Assert.Equal(
            LocalPlaytestLoadCode.InvalidJson,
            LocalPlaytestSummaryDocument.Read(string.Empty).Code);
        Assert.Equal(
            LocalPlaytestLoadCode.InvalidJson,
            LocalPlaytestSummaryDocument.Read("{").Code);
        Assert.Equal(
            LocalPlaytestLoadCode.InvalidDocument,
            LocalPlaytestSummaryDocument.Read(
                payload.Replace("\"kind\":", "\"unknown\":1,\"kind\":", StringComparison.Ordinal)).Code);
        Assert.Equal(
            LocalPlaytestLoadCode.InvalidDocument,
            LocalPlaytestSummaryDocument.Read(
                payload.Replace("\"summaryId\":", "\"nickname\":\"player\",\"summaryId\":", StringComparison.Ordinal)).Code);
        Assert.Equal(
            LocalPlaytestLoadCode.InvalidDocument,
            LocalPlaytestSummaryDocument.Read(
                payload.Replace("\"kind\":", "\"kind\":\"duplicate\",\"kind\":", StringComparison.Ordinal)).Code);
        Assert.Equal(
            LocalPlaytestLoadCode.InvalidDocument,
            LocalPlaytestSummaryDocument.Read(
                payload.Replace($"\"summaryId\":\"{summary.SummaryId}\",", string.Empty, StringComparison.Ordinal)).Code);
        Assert.Equal(
            LocalPlaytestLoadCode.InvalidDocument,
            LocalPlaytestSummaryDocument.Read(
                payload.Replace("\"scoreCategoryId\":\"vibe-standard-v1-dda-off\"", "\"scoreCategoryId\":\"classic-standard-v1\"", StringComparison.Ordinal)).Code);
    }

    [Fact]
    public void Summary_validation_covers_every_identity_run_fact_and_mode_boundary()
    {
        var valid = LocalPlaytestSummary.Capture(
            CreateTerminalRun(44UL),
            "0.2.1",
            DateTimeOffset.UnixEpoch);

        AssertInvalid(valid with { SummaryId = "short" }, "hash");
        AssertInvalid(valid with { SummaryId = new string('0', 64) }, "does not match");
        AssertInvalid(Rehash(valid with { CapturedAtUtc = "2026-08-08" }), "canonical UTC");
        AssertInvalid(Rehash(valid with { AppVersion = "bad version" }), "identity");
        AssertInvalid(Rehash(valid with { RunKind = "automated" }), "identity");
        AssertInvalid(Rehash(valid with { RulesetId = "INVALID" }), "identity");
        AssertInvalid(Rehash(valid with { RulesVersion = 0 }), "identity");
        AssertInvalid(Rehash(valid with { ModeId = "future" }), "identity");
        AssertInvalid(Rehash(valid with { ScoreCategoryId = "INVALID" }), "identity");
        AssertInvalid(Rehash(valid with { AdaptivePolicyId = "INVALID" }), "identity");
        AssertInvalid(Rehash(valid with { ConfigHash = "short" }), "configHash");
        AssertInvalid(Rehash(valid with { FinalStateHash = "ABCDEF0123456789" }), "finalStateHash");
        AssertInvalid(Rehash(valid with { Seed = "-1" }), "run facts");
        AssertInvalid(Rehash(valid with { Outcome = "quit" }), "run facts");
        AssertInvalid(Rehash(valid with { DeathCause = "quit" }), "run facts");
        AssertInvalid(Rehash(valid with { Outcome = "won" }), "run facts");
        AssertInvalid(Rehash(valid with { AdaptiveFinalState = "future" }), "run facts");
        AssertInvalid(Rehash(valid with { SurvivalSteps = 0 }), "run facts");
        AssertInvalid(Rehash(valid with { Score = -1 }), "run facts");
        AssertInvalid(Rehash(valid with { FinalLength = 0 }), "run facts");
        AssertInvalid(Rehash(valid with { FoodEaten = -1 }), "run facts");
        AssertInvalid(Rehash(valid with { Wraps = -1 }), "run facts");
        AssertInvalid(Rehash(valid with { NearMisses = -1 }), "run facts");
        AssertInvalid(Rehash(valid with { PowerupsCollected = -1 }), "run facts");
        AssertInvalid(Rehash(valid with { ComboPeak = -1 }), "run facts");
        AssertInvalid(Rehash(valid with { PowerDecisions = null }), "run facts");
        AssertInvalid(Rehash(valid with
        {
            PowerDecisions = valid.PowerDecisions!
                .Take(valid.PowerDecisions!.Count - 1)
                .ToArray(),
        }), "all nine");
        AssertInvalid(Rehash(valid with
        {
            PowerDecisions = valid.PowerDecisions!.Select((item, index) =>
                index == 0 ? item with { Offered = -1 } : item).ToArray(),
        }), "counts");
        AssertInvalid(Rehash(valid with
        {
            PowerDecisions = valid.PowerDecisions!.Select((item, index) =>
                index == 0
                    ? item with { Offered = 1, DetoursObserved = 2 }
                    : item).ToArray(),
        }), "counts");
        AssertInvalid(
            Rehash(valid with { ScoreCategoryId = RunModeCatalog.ClassicScoreCategoryId }),
            "conflict");
        AssertInvalid(
            Rehash(valid with { AdaptivePolicyId = AdaptiveDifficultyPolicy.CurrentPolicyId }),
            "conflict");

        var won = Rehash(valid with { Outcome = "won", DeathCause = "none" });
        var classic = Rehash(valid with
        {
            ModeId = RunModeCatalog.ClassicId,
            ModeVersion = RunModeCatalog.Classic.Version,
            ScoreCategoryId = RunModeCatalog.ClassicScoreCategoryId,
            AdaptivePolicyId = AdaptiveDifficultyPolicy.DisabledPolicyId,
            AdaptiveFinalState = "disabled",
        });
        var adaptive = Rehash(valid with
        {
            AdaptationEnabled = true,
            ScoreCategoryId = RunModeCatalog.VibeAdaptiveScoreCategoryId,
            AdaptivePolicyId = AdaptiveDifficultyPolicy.CurrentPolicyId,
            AdaptiveFinalState = "pressure",
        });

        won.Validate();
        classic.Validate();
        adaptive.Validate();
    }

    [Fact]
    public void Document_validation_rejects_each_envelope_boundary()
    {
        var validSummary = LocalPlaytestSummary.Capture(
            CreateTerminalRun(45UL),
            "0.2.1",
            DateTimeOffset.UnixEpoch);
        var valid = LocalPlaytestSummaryDocument.CreateEmpty().Append(validSummary).Document;

        Assert.Throws<InvalidDataException>(() => (valid with { SchemaVersion = 3 }).Validate());
        Assert.Throws<InvalidDataException>(() => (valid with { Kind = "future" }).Validate());
        Assert.Throws<InvalidDataException>(() => (valid with { CollectionBasis = "implicit" }).Validate());
        Assert.Throws<InvalidDataException>(() => (valid with { RetentionLimit = 201 }).Validate());
        Assert.Throws<InvalidDataException>(() => (valid with
        {
            Summaries = Enumerable.Repeat(
                validSummary,
                LocalPlaytestSummaryDocument.MaximumSummaries + 1).ToArray(),
        }).Validate());
        Assert.Throws<InvalidDataException>(() => (valid with
        {
            Summaries = [validSummary, validSummary],
        }).Validate());

        var payload = valid.SerializeCanonical();
        var summariesSegment = payload[
            payload.IndexOf("\"summaries\":[", StringComparison.Ordinal)..^2];
        Assert.Equal(
            LocalPlaytestLoadCode.InvalidJson,
            LocalPlaytestSummaryDocument.Read(
                payload.Replace(summariesSegment, "\"summaries\":1", StringComparison.Ordinal)).Code);
        Assert.Equal(
            LocalPlaytestLoadCode.InvalidDocument,
            LocalPlaytestSummaryDocument.Read(
                payload.Replace(
                    summariesSegment,
                    "\"summaries\":[1]",
                    StringComparison.Ordinal)).Code);
    }

    [Fact]
    public void Reader_migrates_schema_one_with_verified_legacy_identity()
    {
        var summary = LocalPlaytestSummary.Capture(
            CreateTerminalRun(46UL),
            "0.2.1",
            DateTimeOffset.UnixEpoch);
        var legacy = SerializeLegacyDocument(summary);
        using var legacyJson = JsonDocument.Parse(legacy);
        var legacyId = legacyJson.RootElement.GetProperty("summaries")[0]
            .GetProperty("summaryId")
            .GetString();

        var migrated = LocalPlaytestSummaryDocument.Read(legacy);

        Assert.True(migrated.IsSuccess, migrated.Message);
        Assert.Equal(LocalPlaytestSummaryDocument.CurrentSchemaVersion, migrated.Document!.SchemaVersion);
        Assert.Equal(LocalPlaytestSummaryDocument.DocumentKind, migrated.Document.Kind);
        var migratedSummary = Assert.Single(migrated.Document.Summaries);
        Assert.NotEqual(legacyId, migratedSummary.SummaryId);
        Assert.Equal(summary.SummaryId, migratedSummary.SummaryId);
        Assert.Equal(9, migratedSummary.PowerDecisions!.Count);
        Assert.All(migratedSummary.PowerDecisions, item =>
            Assert.Equal(0, item.Offered + item.DetoursObserved + item.Collected
                + item.Activated + item.Expired + item.Consumed + item.Saved
                + item.DeathAdjacent));

        var tampered = legacy.Replace("\"score\":0", "\"score\":1", StringComparison.Ordinal);
        Assert.Equal(
            LocalPlaytestLoadCode.InvalidDocument,
            LocalPlaytestSummaryDocument.Read(tampered).Code);
    }

    [Fact]
    public void Append_is_idempotent_and_retains_only_the_latest_two_hundred_summaries()
    {
        var terminal = CreateTerminalRun(5UL);
        var origin = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        var document = LocalPlaytestSummaryDocument.CreateEmpty();
        var first = LocalPlaytestSummary.Capture(terminal, "0.2.1", origin);
        var firstAppend = document.Append(first);
        var duplicate = firstAppend.Document.Append(first);

        Assert.True(firstAppend.Added);
        Assert.False(duplicate.Added);
        Assert.Equal(0, duplicate.EvictedCount);

        document = duplicate.Document;
        var evicted = 0;
        for (var index = 1; index <= LocalPlaytestSummaryDocument.MaximumSummaries; index++)
        {
            var result = document.Append(
                LocalPlaytestSummary.Capture(terminal, "0.2.1", origin.AddMilliseconds(index)));
            document = result.Document;
            evicted += result.EvictedCount;
        }

        Assert.Equal(LocalPlaytestSummaryDocument.MaximumSummaries, document.Summaries.Count);
        Assert.Equal(1, evicted);
        Assert.DoesNotContain(document.Summaries, item => item.SummaryId == first.SummaryId);
        Assert.True(
            System.Text.Encoding.UTF8.GetByteCount(document.SerializeCanonical())
                < LocalPlaytestSummaryDocument.MaximumDocumentBytes);
    }

    [Fact]
    public void Store_exports_canonical_local_facts_and_permanently_deletes_store_and_exports()
    {
        using var temp = new TemporaryDirectory();
        var store = new LocalPlaytestSummaryStore(temp.Path);
        var summary = LocalPlaytestSummary.Capture(
            CreateTerminalRun(6UL),
            "0.2.1",
            DateTimeOffset.UnixEpoch);

        var append = store.Append(summary);
        var duplicate = store.Append(summary);
        var exported = store.Export(
            new DateTimeOffset(2026, 8, 8, 21, 22, 23, 456, TimeSpan.Zero));
        var exportPath = Path.Combine(store.ExportDirectory, exported.FileName);
        var exportPayload = File.ReadAllText(exportPath);
        using var exportJson = JsonDocument.Parse(exportPayload);

        Assert.True(append.Added);
        Assert.False(duplicate.Added);
        Assert.Single(store.Load().Document!.Summaries);
        Assert.Matches(
            "^playtest-summaries_20260808T212223456Z_[0-9a-f]{12}\\.json$",
            exported.FileName);
        Assert.Equal(1, exported.SummaryCount);
        Assert.Matches("^[0-9a-f]{64}$", exported.Sha256);
        Assert.Equal(LocalPlaytestSummaryStore.ExportKind, exportJson.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, exportJson.RootElement.GetProperty("summaryCount").GetInt32());
        Assert.Equal(1, exportJson.RootElement.GetProperty("summaries").GetArrayLength());
        Assert.DoesNotContain(temp.Path, exportPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", exportPayload, StringComparison.OrdinalIgnoreCase);

        var deleted = store.DeleteAll();

        Assert.True(deleted.StoreExisted);
        Assert.Equal(1, deleted.ExportFilesDeleted);
        Assert.False(File.Exists(store.StorePath));
        Assert.False(File.Exists(exportPath));
        Assert.Empty(store.Load().Document!.Summaries);
        Assert.Equal(new LocalPlaytestDeleteResult(false, 0), store.DeleteAll());
    }

    [Fact]
    public void Store_rejects_relative_roots_and_does_not_overwrite_corrupt_data()
    {
        Assert.Throws<ArgumentException>(() => new LocalPlaytestSummaryStore("relative"));
        using var temp = new TemporaryDirectory();
        var store = new LocalPlaytestSummaryStore(temp.Path);
        Directory.CreateDirectory(store.StoreDirectory);
        File.WriteAllText(store.StorePath, "not-json");
        var before = File.ReadAllText(store.StorePath);

        var loaded = store.Load();

        Assert.Equal(LocalPlaytestLoadCode.InvalidJson, loaded.Code);
        Assert.Throws<InvalidDataException>(() => store.Append(
            LocalPlaytestSummary.Capture(
                CreateTerminalRun(7UL),
                "0.2.1",
                DateTimeOffset.UnixEpoch)));
        Assert.Throws<InvalidDataException>(() => store.Export(DateTimeOffset.UnixEpoch));
        Assert.Equal(before, File.ReadAllText(store.StorePath));
    }

    [Fact]
    public void Store_rejects_oversized_data_before_parsing_and_deletion_removes_owned_temporaries()
    {
        using var temp = new TemporaryDirectory();
        var store = new LocalPlaytestSummaryStore(temp.Path);
        Directory.CreateDirectory(store.ExportDirectory);
        File.WriteAllText(
            store.StorePath,
            new string('x', LocalPlaytestSummaryDocument.MaximumDocumentBytes + 1));
        var storeTemporaryPath = store.StorePath + ".tmp";
        var exportTemporaryPath = Path.Combine(
            store.ExportDirectory,
            "playtest-summaries_interrupted.json.tmp");
        File.WriteAllText(storeTemporaryPath, "partial");
        File.WriteAllText(exportTemporaryPath, "partial");

        var loaded = store.Load();

        Assert.Equal(LocalPlaytestLoadCode.InvalidDocument, loaded.Code);
        Assert.Contains("byte limit", loaded.Message, StringComparison.Ordinal);

        var deleted = store.DeleteAll();

        Assert.True(deleted.StoreExisted);
        Assert.Equal(0, deleted.ExportFilesDeleted);
        Assert.False(File.Exists(store.StorePath));
        Assert.False(File.Exists(storeTemporaryPath));
        Assert.False(File.Exists(exportTemporaryPath));
    }

    [Fact]
    public void Explicit_exports_retain_only_the_newest_twenty_files()
    {
        using var temp = new TemporaryDirectory();
        var store = new LocalPlaytestSummaryStore(temp.Path);
        store.Append(LocalPlaytestSummary.Capture(
            CreateTerminalRun(8UL),
            "0.2.1",
            DateTimeOffset.UnixEpoch));
        LocalPlaytestExportResult? last = null;

        for (var index = 0; index <= LocalPlaytestSummaryStore.MaximumExportFiles; index++)
        {
            last = store.Export(DateTimeOffset.UnixEpoch.AddMilliseconds(index));
        }

        Assert.NotNull(last);
        Assert.Equal(1, last.PrunedExportCount);
        Assert.Equal(
            LocalPlaytestSummaryStore.MaximumExportFiles,
            Directory.GetFiles(store.ExportDirectory, "playtest-summaries_*.json").Length);
        Assert.Equal(
            LocalPlaytestSummaryStore.MaximumExportFiles,
            store.DeleteAll().ExportFilesDeleted);
    }

    private static SnakeRun CreateTerminalRun(ulong seed)
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
        {
            Width = 5,
            Height = 4,
            StarvationTicks = 4,
            StarvationWarningTicks = 0,
            PowerSpawnIntervalTicks = 0,
        };
        var run = SnakeRun.Create(seed, config);
        for (var step = 0; step < 100 && run.Status == RunStatus.Running; step++)
        {
            run.Step();
        }

        Assert.NotEqual(RunStatus.Running, run.Status);
        return run;
    }

    private static void AssertInvalid(LocalPlaytestSummary summary, string message)
    {
        var exception = Assert.Throws<InvalidDataException>(summary.Validate);
        Assert.Contains(message, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static LocalPlaytestSummary Rehash(LocalPlaytestSummary summary)
    {
        var identityFacts = summary with { SummaryId = string.Empty };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            identityFacts,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            });
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            .ToLowerInvariant();
        return summary with { SummaryId = hash };
    }

    private static string SerializeLegacyDocument(LocalPlaytestSummary summary)
    {
        var current = LocalPlaytestSummaryDocument.CreateEmpty()
            .Append(summary)
            .Document
            .SerializeCanonical();
        using var parsed = JsonDocument.Parse(current);
        var sourceSummary = parsed.RootElement.GetProperty("summaries")[0];
        var identityBuffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(identityBuffer))
        {
            WriteLegacySummary(writer, sourceSummary, string.Empty);
        }

        var legacyId = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(identityBuffer.WrittenSpan))
            .ToLowerInvariant();
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", LocalPlaytestSummaryDocument.LegacySchemaVersion);
            writer.WriteString("kind", LocalPlaytestSummaryDocument.LegacyDocumentKind);
            writer.WriteString(
                "collectionBasis",
                LocalPlaytestSummaryDocument.ExplicitOptInBasis);
            writer.WriteNumber("retentionLimit", LocalPlaytestSummaryDocument.MaximumSummaries);
            writer.WriteStartArray("summaries");
            WriteLegacySummary(writer, sourceSummary, legacyId);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(output.ToArray()) + "\n";
    }

    private static void WriteLegacySummary(
        Utf8JsonWriter writer,
        JsonElement sourceSummary,
        string summaryId)
    {
        writer.WriteStartObject();
        foreach (var property in sourceSummary.EnumerateObject())
        {
            if (property.NameEquals("powerDecisions"))
            {
                continue;
            }

            if (property.NameEquals("summaryId"))
            {
                writer.WriteString("summaryId", summaryId);
            }
            else
            {
                property.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "vibesnake-playtest-summary-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
