using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public enum LocalPlaytestLoadCode : byte
{
    Success = 0,
    InvalidJson = 1,
    InvalidDocument = 2,
    IoError = 3,
}

public sealed record LocalPlaytestLoadResult(
    LocalPlaytestLoadCode Code,
    string Message,
    LocalPlaytestSummaryDocument? Document = null)
{
    public bool IsSuccess => Code == LocalPlaytestLoadCode.Success && Document is not null;
}

public sealed record LocalPlaytestAppendResult(
    LocalPlaytestSummaryDocument Document,
    bool Added,
    int EvictedCount);

public sealed record LocalPlaytestExportResult(
    string FileName,
    int SummaryCount,
    string Sha256,
    int PrunedExportCount);

public sealed record LocalPlaytestDeleteResult(
    bool StoreExisted,
    int ExportFilesDeleted);

public sealed record LocalPowerDecisionSummary(
    string PowerId,
    int Offered,
    int DetoursObserved,
    int Collected,
    int Activated,
    int Expired,
    int Consumed,
    int Saved,
    int DeathAdjacent)
{
    public void Validate()
    {
        if (PowerDecisionCatalog.All.All(definition => definition.Id != PowerId)
            || Offered < 0
            || DetoursObserved < 0
            || Collected < 0
            || Activated < 0
            || Expired < 0
            || Consumed < 0
            || Saved < 0
            || DeathAdjacent < 0
            || DetoursObserved > Offered
            || Collected > Offered
            || Activated > Collected
            || Expired > Offered
            || Consumed > Activated
            || Saved > Consumed
            || DeathAdjacent > 1)
        {
            throw new InvalidDataException("Local power-decision counts are invalid.");
        }
    }
}

public sealed record LocalPlaytestSummary(
    string SummaryId,
    string CapturedAtUtc,
    string AppVersion,
    string RunKind,
    string RulesetId,
    int RulesVersion,
    string ModeId,
    int ModeVersion,
    string ScoreCategoryId,
    string ConfigHash,
    bool AdaptationEnabled,
    string AdaptivePolicyId,
    string AdaptiveFinalState,
    string Seed,
    string Outcome,
    string DeathCause,
    int SurvivalSteps,
    int Score,
    int FinalLength,
    int FoodEaten,
    int Wraps,
    int NearMisses,
    int PowerupsCollected,
    int ComboPeak,
    string FinalStateHash,
    IReadOnlyList<LocalPowerDecisionSummary>? PowerDecisions = null)
{
    public const string HumanRunKind = ScoreRunContextCatalog.NormalHumanRunKind;

    private static readonly Regex IdentifierPattern = new(
        "^[a-z0-9][a-z0-9._@-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex AppVersionPattern = new(
        "^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static LocalPlaytestSummary Capture(
        SnakeRun run,
        string appVersion,
        DateTimeOffset capturedAtUtc,
        IReadOnlyList<LocalPowerDecisionSummary>? powerDecisions = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        if (run.Status == RunStatus.Running || run.MasterSeed is not { } seed)
        {
            throw new ArgumentException(
                "A local playtest summary requires a terminal seeded run.",
                nameof(run));
        }

        var summary = new LocalPlaytestSummary(
            SummaryId: string.Empty,
            CapturedAtUtc: FormatUtc(capturedAtUtc),
            AppVersion: appVersion,
            RunKind: HumanRunKind,
            RulesetId: SnakeRun.RulesetId,
            RulesVersion: SnakeRun.RulesVersion,
            ModeId: run.Configuration.ModeId,
            ModeVersion: run.Configuration.ModeVersion,
            ScoreCategoryId: run.ScoreCategoryId,
            ConfigHash: run.ConfigHash,
            AdaptationEnabled: run.Configuration.EnableAdaptation,
            AdaptivePolicyId: run.Configuration.AdaptivePolicyId,
            AdaptiveFinalState: ToWire(run.AdaptiveDifficulty.State),
            Seed: seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Outcome: run.Status == RunStatus.Won ? "won" : "dead",
            DeathCause: ToWire(run.DeathCause),
            SurvivalSteps: run.Tick,
            Score: run.Score,
            FinalLength: run.Body.Count,
            FoodEaten: run.SessionFoodEaten,
            Wraps: run.SessionWraps,
            NearMisses: run.SessionNearMisses,
            PowerupsCollected: run.SessionPowerupsCollected,
            ComboPeak: run.SessionMaxCombo,
            FinalStateHash: run.ComputeStateHash(),
            PowerDecisions: powerDecisions ?? CreateEmptyPowerDecisions());
        summary = summary.WithComputedId();
        summary.Validate();
        return summary;
    }

    public void Validate()
    {
        RequireHash(SummaryId, 64, nameof(SummaryId));
        if (SummaryId != ComputeId())
        {
            throw new InvalidDataException("Local playtest summaryId does not match its facts.");
        }

        if (!DateTimeOffset.TryParseExact(
                CapturedAtUtc,
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out _))
        {
            throw new InvalidDataException("capturedAtUtc must use canonical UTC milliseconds.");
        }

        if (!AppVersionPattern.IsMatch(AppVersion)
            || RunKind != HumanRunKind
            || !IdentifierPattern.IsMatch(RulesetId)
            || RulesVersion <= 0
            || !RunModeCatalog.IsSupportedIdentity(ModeId, ModeVersion)
            || !IdentifierPattern.IsMatch(ScoreCategoryId)
            || !IdentifierPattern.IsMatch(AdaptivePolicyId))
        {
            throw new InvalidDataException("Local playtest summary identity is invalid.");
        }

        RequireHash(ConfigHash, 64, nameof(ConfigHash));
        RequireHash(FinalStateHash, 16, nameof(FinalStateHash));
        if (!ulong.TryParse(
                Seed,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out _)
            || Outcome is not ("dead" or "won")
            || DeathCause is not ("none" or "self-collision" or "starvation")
            || (Outcome == "won") != (DeathCause == "none")
            || AdaptiveFinalState is not ("disabled" or "support" or "standard" or "pressure")
            || SurvivalSteps <= 0
            || Score < 0
            || FinalLength <= 0
            || FoodEaten < 0
            || Wraps < 0
            || NearMisses < 0
            || PowerupsCollected < 0
            || ComboPeak < 0
            || PowerDecisions is null)
        {
            throw new InvalidDataException("Local playtest summary run facts are invalid.");
        }

        var expectedCategory = ModeId switch
        {
            RunModeCatalog.ClassicId => RunModeCatalog.ClassicScoreCategoryId,
            RunModeCatalog.VibeId when AdaptationEnabled =>
                RunModeCatalog.VibeAdaptiveScoreCategoryId,
            RunModeCatalog.VibeId => RunModeCatalog.VibeFixedScoreCategoryId,
            _ => throw new InvalidDataException("Local playtest summary mode is invalid."),
        };
        var expectedAdaptivePolicy = AdaptationEnabled
            ? AdaptiveDifficultyPolicy.CurrentPolicyId
            : AdaptiveDifficultyPolicy.DisabledPolicyId;
        if (ScoreCategoryId != expectedCategory
            || AdaptivePolicyId != expectedAdaptivePolicy
            || (ModeId == RunModeCatalog.ClassicId && AdaptationEnabled))
        {
            throw new InvalidDataException(
                "Local playtest summary mode, category, or adaptation facts conflict.");
        }

        var expectedPowerIds = PowerDecisionCatalog.All.Select(definition => definition.Id);
        if (!PowerDecisions.Select(item => item.PowerId).SequenceEqual(expectedPowerIds))
        {
            throw new InvalidDataException(
                "Local power-decision rows must contain all nine powers in catalog order.");
        }

        foreach (var powerDecision in PowerDecisions)
        {
            powerDecision.Validate();
        }
    }

    private string ComputeId()
    {
        var identityFacts = this with { SummaryId = string.Empty };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            identityFacts,
            LocalPlaytestSummaryDocument.SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    internal LocalPlaytestSummary WithComputedId()
    {
        var withoutId = this with { SummaryId = string.Empty };
        return withoutId with { SummaryId = withoutId.ComputeId() };
    }

    internal void ValidateLegacyId()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("summaryId", string.Empty);
            writer.WriteString("capturedAtUtc", CapturedAtUtc);
            writer.WriteString("appVersion", AppVersion);
            writer.WriteString("runKind", RunKind);
            writer.WriteString("rulesetId", RulesetId);
            writer.WriteNumber("rulesVersion", RulesVersion);
            writer.WriteString("modeId", ModeId);
            writer.WriteNumber("modeVersion", ModeVersion);
            writer.WriteString("scoreCategoryId", ScoreCategoryId);
            writer.WriteString("configHash", ConfigHash);
            writer.WriteBoolean("adaptationEnabled", AdaptationEnabled);
            writer.WriteString("adaptivePolicyId", AdaptivePolicyId);
            writer.WriteString("adaptiveFinalState", AdaptiveFinalState);
            writer.WriteString("seed", Seed);
            writer.WriteString("outcome", Outcome);
            writer.WriteString("deathCause", DeathCause);
            writer.WriteNumber("survivalSteps", SurvivalSteps);
            writer.WriteNumber("score", Score);
            writer.WriteNumber("finalLength", FinalLength);
            writer.WriteNumber("foodEaten", FoodEaten);
            writer.WriteNumber("wraps", Wraps);
            writer.WriteNumber("nearMisses", NearMisses);
            writer.WriteNumber("powerupsCollected", PowerupsCollected);
            writer.WriteNumber("comboPeak", ComboPeak);
            writer.WriteString("finalStateHash", FinalStateHash);
            writer.WriteEndObject();
        }

        var expected = Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan))
            .ToLowerInvariant();
        if (SummaryId != expected)
        {
            throw new InvalidDataException(
                "Legacy local playtest summaryId does not match its facts.");
        }
    }

    public static IReadOnlyList<LocalPowerDecisionSummary> CreateEmptyPowerDecisions() =>
        PowerDecisionCatalog.All.Select(definition => new LocalPowerDecisionSummary(
            definition.Id,
            Offered: 0,
            DetoursObserved: 0,
            Collected: 0,
            Activated: 0,
            Expired: 0,
            Consumed: 0,
            Saved: 0,
            DeathAdjacent: 0)).ToArray();

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);

    private static string ToWire(AdaptiveDifficultyState state) => state switch
    {
        AdaptiveDifficultyState.Disabled => "disabled",
        AdaptiveDifficultyState.Support => "support",
        AdaptiveDifficultyState.Standard => "standard",
        AdaptiveDifficultyState.Pressure => "pressure",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static string ToWire(VibeSnake.Rules.DeathCause cause) => cause switch
    {
        VibeSnake.Rules.DeathCause.None => "none",
        VibeSnake.Rules.DeathCause.SelfCollision => "self-collision",
        VibeSnake.Rules.DeathCause.Starvation => "starvation",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, null),
    };

    private static void RequireHash(string value, int length, string field)
    {
        if (value.Length != length || value.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            throw new InvalidDataException($"Local playtest {field} is not a lowercase hex hash.");
        }
    }
}

public sealed record LocalPlaytestSummaryDocument(
    int SchemaVersion,
    string Kind,
    string CollectionBasis,
    int RetentionLimit,
    IReadOnlyList<LocalPlaytestSummary> Summaries)
{
    public const int CurrentSchemaVersion = 2;
    public const string DocumentKind = "vibesnake-local-playtest-summaries-v2";
    public const int LegacySchemaVersion = 1;
    public const string LegacyDocumentKind = "vibesnake-local-playtest-summaries-v1";
    public const string ExplicitOptInBasis = "explicit-local-opt-in";
    public const int MaximumSummaries = 200;
    public const int MaximumDocumentBytes = 512 * 1024;

    private static readonly HashSet<string> DocumentFields =
    [
        "schemaVersion",
        "kind",
        "collectionBasis",
        "retentionLimit",
        "summaries",
    ];

    private static readonly HashSet<string> LegacySummaryFields =
    [
        "summaryId", "capturedAtUtc", "appVersion", "runKind", "rulesetId",
        "rulesVersion", "modeId", "modeVersion", "scoreCategoryId", "configHash",
        "adaptationEnabled", "adaptivePolicyId", "adaptiveFinalState", "seed", "outcome",
        "deathCause", "survivalSteps", "score", "finalLength", "foodEaten", "wraps",
        "nearMisses", "powerupsCollected", "comboPeak", "finalStateHash",
    ];

    private static readonly HashSet<string> SummaryFields =
        [.. LegacySummaryFields, "powerDecisions"];

    internal static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static LocalPlaytestSummaryDocument CreateEmpty() => new(
        CurrentSchemaVersion,
        DocumentKind,
        ExplicitOptInBasis,
        MaximumSummaries,
        Array.Empty<LocalPlaytestSummary>());

    public LocalPlaytestAppendResult Append(LocalPlaytestSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Validate();
        summary.Validate();
        if (Summaries.Any(item => item.SummaryId == summary.SummaryId))
        {
            return new LocalPlaytestAppendResult(this, Added: false, EvictedCount: 0);
        }

        var combined = Summaries.Append(summary).ToArray();
        var evicted = Math.Max(0, combined.Length - MaximumSummaries);
        var retained = combined.Skip(evicted).ToArray();
        var next = this with { Summaries = retained };
        next.Validate();
        return new LocalPlaytestAppendResult(next, Added: true, EvictedCount: evicted);
    }

    public string SerializeCanonical()
    {
        Validate();
        var value = JsonSerializer.Serialize(this, SerializerOptions) + "\n";
        if (Encoding.UTF8.GetByteCount(value) > MaximumDocumentBytes)
        {
            throw new InvalidDataException("Local playtest summary document exceeds its byte limit.");
        }

        return value;
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion
            || Kind != DocumentKind
            || CollectionBasis != ExplicitOptInBasis
            || RetentionLimit != MaximumSummaries
            || Summaries.Count > MaximumSummaries
            || Summaries.Select(item => item.SummaryId).Distinct(StringComparer.Ordinal).Count()
                != Summaries.Count)
        {
            throw new InvalidDataException("Local playtest summary document contract is invalid.");
        }

        foreach (var summary in Summaries)
        {
            summary.Validate();
        }
    }

    public static LocalPlaytestLoadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LocalPlaytestLoadResult(
                LocalPlaytestLoadCode.InvalidJson,
                "Local playtest summary document is empty.");
        }

        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
        {
            return new LocalPlaytestLoadResult(
                LocalPlaytestLoadCode.InvalidDocument,
                "Local playtest summary document exceeds its byte limit.");
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            RequireExactFields(parsed.RootElement, DocumentFields, "document");
            var schemaVersion = parsed.RootElement.GetProperty("schemaVersion").GetInt32();
            var expectedSummaryFields = schemaVersion == LegacySchemaVersion
                ? LegacySummaryFields
                : SummaryFields;
            if (parsed.RootElement.TryGetProperty("summaries", out var summaries)
                && summaries.ValueKind == JsonValueKind.Array)
            {
                foreach (var summary in summaries.EnumerateArray())
                {
                    RequireExactFields(summary, expectedSummaryFields, "summary");
                }
            }

            var document = JsonSerializer.Deserialize<LocalPlaytestSummaryDocument>(
                json,
                SerializerOptions) ?? throw new InvalidDataException(
                    "Local playtest summary document is null.");
            if (schemaVersion == LegacySchemaVersion)
            {
                if (document.Kind != LegacyDocumentKind
                    || document.CollectionBasis != ExplicitOptInBasis
                    || document.RetentionLimit != MaximumSummaries)
                {
                    throw new InvalidDataException(
                        "Legacy local playtest summary document contract is invalid.");
                }

                document = new LocalPlaytestSummaryDocument(
                    CurrentSchemaVersion,
                    DocumentKind,
                    ExplicitOptInBasis,
                    MaximumSummaries,
                    document.Summaries.Select(summary =>
                    {
                        summary.ValidateLegacyId();
                        return (summary with
                        {
                            PowerDecisions = LocalPlaytestSummary.CreateEmptyPowerDecisions(),
                        }).WithComputedId();
                    }).ToArray());
            }

            document.Validate();

            return new LocalPlaytestLoadResult(
                LocalPlaytestLoadCode.Success,
                "Local playtest summaries loaded.",
                document);
        }
        catch (JsonException exception)
        {
            return new LocalPlaytestLoadResult(
                LocalPlaytestLoadCode.InvalidJson,
                "Local playtest summary JSON is invalid: " + exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return new LocalPlaytestLoadResult(
                LocalPlaytestLoadCode.InvalidDocument,
                exception.Message);
        }
    }

    private static void RequireExactFields(
        JsonElement element,
        HashSet<string> expected,
        string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Local playtest {label} must be an object.");
        }

        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !observed.Add(property.Name))
            {
                throw new InvalidDataException(
                    $"Local playtest {label} contains an unknown or duplicate field.");
            }
        }

        if (!observed.SetEquals(expected))
        {
            throw new InvalidDataException($"Local playtest {label} is missing a required field.");
        }
    }
}

public sealed record LocalPlaytestSummaryExport(
    int SchemaVersion,
    string Kind,
    string ExportedAtUtc,
    string SourceDocumentSha256,
    int SummaryCount,
    IReadOnlyList<LocalPlaytestSummary> Summaries);

public sealed class LocalPlaytestSummaryStore
{
    public const string StoreDirectoryName = "playtest-summaries";
    public const string StoreFileName = "summaries.json";
    public const string ExportDirectoryName = "exports";
    public const string ExportKind = "vibesnake-local-playtest-summary-export-v1";
    public const int MaximumExportFiles = 20;

    public LocalPlaytestSummaryStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException("The user-data root must be absolute.", nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        StoreDirectory = Path.Combine(UserDataRoot, StoreDirectoryName);
        StorePath = Path.Combine(StoreDirectory, StoreFileName);
        ExportDirectory = Path.Combine(StoreDirectory, ExportDirectoryName);
    }

    public string UserDataRoot { get; }

    public string StoreDirectory { get; }

    public string StorePath { get; }

    public string ExportDirectory { get; }

    public LocalPlaytestLoadResult Load()
    {
        if (!File.Exists(StorePath))
        {
            return new LocalPlaytestLoadResult(
                LocalPlaytestLoadCode.Success,
                "No local playtest summaries exist.",
                LocalPlaytestSummaryDocument.CreateEmpty());
        }

        try
        {
            if (new FileInfo(StorePath).Length > LocalPlaytestSummaryDocument.MaximumDocumentBytes)
            {
                return new LocalPlaytestLoadResult(
                    LocalPlaytestLoadCode.InvalidDocument,
                    "Local playtest summary document exceeds its byte limit.");
            }

            return LocalPlaytestSummaryDocument.Read(File.ReadAllText(StorePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new LocalPlaytestLoadResult(
                LocalPlaytestLoadCode.IoError,
                "Local playtest summaries could not be read: " + exception.Message);
        }
    }

    public LocalPlaytestAppendResult Append(LocalPlaytestSummary summary)
    {
        var loaded = Load();
        if (!loaded.IsSuccess || loaded.Document is null)
        {
            throw new InvalidDataException(loaded.Message);
        }

        var result = loaded.Document.Append(summary);
        Save(result.Document);
        return result;
    }

    public LocalPlaytestExportResult Export(DateTimeOffset exportedAtUtc)
    {
        var loaded = Load();
        if (!loaded.IsSuccess || loaded.Document is null)
        {
            throw new InvalidDataException(loaded.Message);
        }

        var sourcePayload = loaded.Document.SerializeCanonical();
        var sourceHash = Sha256(Encoding.UTF8.GetBytes(sourcePayload));
        var timestamp = exportedAtUtc.ToUniversalTime().ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);
        var export = new LocalPlaytestSummaryExport(
            LocalPlaytestSummaryDocument.CurrentSchemaVersion,
            ExportKind,
            exportedAtUtc.ToUniversalTime().ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                System.Globalization.CultureInfo.InvariantCulture),
            sourceHash,
            loaded.Document.Summaries.Count,
            loaded.Document.Summaries);
        var payload = JsonSerializer.Serialize(
            export,
            LocalPlaytestSummaryDocument.SerializerOptions) + "\n";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var payloadHash = Sha256(payloadBytes);
        var fileName = $"playtest-summaries_{timestamp}_{payloadHash[..12]}.json";
        Directory.CreateDirectory(ExportDirectory);
        var path = Path.Combine(ExportDirectory, fileName);
        var temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, payloadBytes);
        File.Move(temporaryPath, path, overwrite: true);
        var exports = Directory.EnumerateFiles(
                ExportDirectory,
                "playtest-summaries_*.json",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var pruned = Math.Max(0, exports.Length - MaximumExportFiles);
        foreach (var obsolete in exports.Take(pruned))
        {
            File.Delete(obsolete);
        }

        return new LocalPlaytestExportResult(
            fileName,
            loaded.Document.Summaries.Count,
            payloadHash,
            pruned);
    }

    public LocalPlaytestDeleteResult DeleteAll()
    {
        var storeExisted = File.Exists(StorePath);
        if (storeExisted)
        {
            File.Delete(StorePath);
        }

        var temporaryStorePath = StorePath + ".tmp";
        if (File.Exists(temporaryStorePath))
        {
            File.Delete(temporaryStorePath);
        }

        var exportFilesDeleted = 0;
        if (Directory.Exists(ExportDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(
                ExportDirectory,
                "playtest-summaries_*.json",
                SearchOption.TopDirectoryOnly))
            {
                File.Delete(path);
                exportFilesDeleted++;
            }


            foreach (var path in Directory.EnumerateFiles(
                ExportDirectory,
                "playtest-summaries_*.json.tmp",
                SearchOption.TopDirectoryOnly))
            {
                File.Delete(path);
            }
        }

        return new LocalPlaytestDeleteResult(storeExisted, exportFilesDeleted);
    }

    private void Save(LocalPlaytestSummaryDocument document)
    {
        Directory.CreateDirectory(StoreDirectory);
        var temporaryPath = StorePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            document.SerializeCanonical(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, StorePath, overwrite: true);
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
