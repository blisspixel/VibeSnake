using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.AgentPlay;

/// <summary>
/// Why one passport write ended the way it did.
/// </summary>
public enum AgentPassportWriteCode : byte
{
    /// <summary>A new agent record was created from this exhibition.</summary>
    Created = 0,

    /// <summary>An existing agent record absorbed this exhibition.</summary>
    Updated = 1,

    /// <summary>This exhibition was already recorded. Nothing was written.</summary>
    AlreadyRecorded = 2,

    /// <summary>The match has no canonical receipt, so it has no public record to keep.</summary>
    NoVerifiedReceipt = 3,

    /// <summary>The store could not be read or written. No state changed.</summary>
    StoreUnavailable = 4,

    /// <summary>A seventeenth agent would exceed the store. Nothing was written.</summary>
    CapacityReached = 5,

    /// <summary>This agent already holds as many receipts as one record may keep.</summary>
    ReceiptLedgerFull = 6,

    /// <summary>No archived exhibition carries that receipt hash.</summary>
    NotArchived = 7,
}

/// <summary>
/// Why one passport removal ended the way it did.
/// </summary>
public enum AgentPassportForgetCode : byte
{
    /// <summary>The named records were removed and the store was rewritten.</summary>
    Forgotten = 0,

    /// <summary>No record matched, so nothing was written.</summary>
    NotRecorded = 1,

    /// <summary>The store could not be read or written. No state changed.</summary>
    StoreUnavailable = 2,
}

/// <summary>
/// The persistent public identity store: one record per agent, each assembled
/// only from verified exhibition receipts.
///
/// It is deliberately separate from the exhibition archive. The archive keeps
/// exhibitions and can evict the oldest; a passport keeps what an agent has
/// done and must not quietly lose history because the archive shelf filled up.
/// Separating them also keeps one bound from deciding the other. This store
/// still has its own agent-count and byte ceilings. A seventeenth agent is
/// refused rather than silently dropped.
/// </summary>
public sealed record AgentPassportDocumentV1(
    string Schema,
    int SchemaVersion,
    int Capacity,
    IReadOnlyList<string> RecordedReceiptHashes,
    IReadOnlyList<AgentPassportRecordV1> Records)
{
    public const string Contract = "vibesnake-agent-passport-document-v1";
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// How many distinct agents one local store keeps. A local preview store is
    /// not a league table, and an unbounded one would be a slow leak.
    /// </summary>
    public const int MaximumRecords = 16;

    /// <summary>
    /// How many receipt hashes one agent record may keep. Bounded because the
    /// ledger exists to stop double counting, not to be a second archive. A
    /// thirty-third distinct receipt is refused rather than dropping an older
    /// hash that still belongs to this record.
    /// </summary>
    public const int MaximumRecordedReceiptsPerAgent = 32;

    /// <summary>A hard serialized-byte ceiling checked before any write reaches disk.</summary>
    public const int MaximumBytes = 1_048_576;

    public static AgentPassportDocumentV1 Empty { get; } = new(
        Contract,
        CurrentSchemaVersion,
        MaximumRecords,
        Array.Empty<string>(),
        Array.Empty<AgentPassportRecordV1>());

    public bool IsWellFormed()
    {
        if (!string.Equals(Schema, Contract, StringComparison.Ordinal)
            || SchemaVersion != CurrentSchemaVersion
            || Capacity != MaximumRecords
            || Records.Count > MaximumRecords
            || Records.Any(record =>
                record.ReceiptHashes.Count > MaximumRecordedReceiptsPerAgent)
            || !Records.All(record => record.IsSelfConsistent())
            || Records
                .Select(record => record.AgentId)
                .Distinct(StringComparer.Ordinal)
                .Count() != Records.Count
            || RecordedReceiptHashes
                .Distinct(StringComparer.Ordinal)
                .Count() != RecordedReceiptHashes.Count)
        {
            return false;
        }

        var rebuilt = RebuildLedger(Records);
        return rebuilt.Count == RecordedReceiptHashes.Count
            && rebuilt.SequenceEqual(RecordedReceiptHashes, StringComparer.Ordinal);
    }

    internal static IReadOnlyList<string> RebuildLedger(
        IReadOnlyList<AgentPassportRecordV1> records) =>
        records.SelectMany(record => record.ReceiptHashes).ToArray();
}

/// <summary>
/// One agent a forget request removed. Reporting only a count told a caller
/// that a public record vanished without saying whose.
/// </summary>
public sealed record AgentPassportDropV1(
    string Schema,
    string AgentId,
    int Exhibitions,
    string FirstReceiptHash,
    string LatestReceiptHash)
{
    public const string Contract = "vibesnake-agent-passport-drop-v1";

    internal static AgentPassportDropV1 FromRecord(AgentPassportRecordV1 record) =>
        new(
            Contract,
            record.AgentId,
            record.Exhibitions,
            record.FirstReceiptHash,
            record.LatestReceiptHash);
}

/// <summary>The factual outcome of one passport write.</summary>
public sealed record AgentPassportWriteResultV1(
    AgentPassportWriteCode Code,
    string Message,
    bool Recorded,
    string? AgentId,
    IReadOnlyList<AgentPassportDropV1> Evicted,
    bool RecoveredFromCorruption,
    int BytesUsed,
    int BytesProjected,
    int StoredSchemaVersion,
    AgentPassportDocumentV1 Document);

/// <summary>The factual outcome of one passport removal.</summary>
public sealed record AgentPassportForgetResultV1(
    AgentPassportForgetCode Code,
    string Message,
    IReadOnlyList<AgentPassportDropV1> Forgotten,
    bool RecoveredFromCorruption,
    int BytesUsed,
    int BytesProjected,
    int StoredSchemaVersion,
    AgentPassportDocumentV1 Document);

/// <summary>
/// The store as it stands, with the exact bytes it occupies. A read never
/// writes, so bytes_used is the file and bytes_projected is the next write.
/// </summary>
public sealed record AgentPassportReadV1(
    AgentPassportDocumentV1 Document,
    bool RecoveredFromCorruption,
    bool Blocked,
    int BytesUsed,
    int BytesProjected,
    int StoredSchemaVersion);

/// <summary>
/// Reads and writes the bounded passport store under one caller-supplied
/// user-data root, on the same terms as the exhibition archive: it discovers no
/// path, writes atomically, quarantines rather than repairs, and refuses to
/// write over evidence it could not move aside.
/// </summary>
public sealed class AgentPassportStore
{
    public const string DirectoryName = "agent_arena";
    public const string FileName = "agent_passports.json";
    public const string TemporaryFileName = "agent_passports.json.tmp";
    public const string CorruptFileExtension = ".corrupt.json";
    public const int MaximumQuarantineSlots = 16;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
        },
    };

    private readonly object _sync = new();
    private readonly string _directory;

    public AgentPassportStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be a fully qualified path.",
                nameof(userDataRoot));
        }

        _directory = Path.Combine(Path.GetFullPath(userDataRoot), DirectoryName);
    }

    public string DocumentPath => Path.Combine(_directory, FileName);

    public AgentPassportDocumentV1 Read()
    {
        lock (_sync)
        {
            return LoadLocked().Document;
        }
    }

    public AgentPassportReadV1 Inspect()
    {
        lock (_sync)
        {
            var loaded = LoadLocked();
            return new AgentPassportReadV1(
                loaded.Document,
                loaded.Recovered,
                loaded.Blocked,
                loaded.BytesUsed,
                loaded.BytesProjected,
                loaded.StoredSchemaVersion);
        }
    }

    /// <summary>
    /// Records one verified exhibition against its agent's public identity.
    /// Recording is idempotent by receipt hash, so replaying the same
    /// exhibition never inflates a count. A receipt that cannot recompute its
    /// own hashes is refused rather than thrown, because a public record built
    /// from it would be a claim wearing a hash.
    /// </summary>
    public AgentPassportWriteResultV1 Record(AgentExhibitionReceiptV2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        lock (_sync)
        {
            var loaded = LoadLocked();
            var document = loaded.Document;
            if (loaded.Blocked)
            {
                return Refused(
                    AgentPassportWriteCode.StoreUnavailable,
                    "The stored passports could not be read or quarantined, so nothing was written.",
                    loaded,
                    receipt.Passport.AgentId);
            }

            if (!AgentExhibitionReceipt.HasCanonicalHash(receipt))
            {
                return Refused(
                    AgentPassportWriteCode.NoVerifiedReceipt,
                    "A receipt that cannot recompute its own canonical hashes is not a public record.",
                    loaded,
                    receipt.Passport.AgentId);
            }

            if (document.RecordedReceiptHashes.Contains(receipt.ReceiptHash, StringComparer.Ordinal))
            {
                return Refused(
                    AgentPassportWriteCode.AlreadyRecorded,
                    "This exhibition is already part of the agent's public record.",
                    loaded,
                    receipt.Passport.AgentId);
            }

            var records = document.Records.ToList();
            var index = records.FindIndex(record => string.Equals(
                record.AgentId,
                receipt.Passport.AgentId,
                StringComparison.Ordinal));
            var created = index < 0;
            if (created && records.Count >= AgentPassportDocumentV1.MaximumRecords)
            {
                return Refused(
                    AgentPassportWriteCode.CapacityReached,
                    $"The passport store already holds {AgentPassportDocumentV1.MaximumRecords} agents and will not drop one to make room.",
                    loaded,
                    receipt.Passport.AgentId);
            }

            if (!created
                && records[index].ReceiptHashes.Count
                    >= AgentPassportDocumentV1.MaximumRecordedReceiptsPerAgent)
            {
                return Refused(
                    AgentPassportWriteCode.ReceiptLedgerFull,
                    $"This agent already has {AgentPassportDocumentV1.MaximumRecordedReceiptsPerAgent} recorded exhibitions.",
                    loaded,
                    receipt.Passport.AgentId);
            }

            if (created)
            {
                records.Add(AgentPassportRecordV1.FromReceipt(receipt));
            }
            else
            {
                records[index] = records[index].WithReceipt(receipt);
            }

            var ledger = AgentPassportDocumentV1.RebuildLedger(records);

            var candidate = document with
            {
                Records = records.AsReadOnly(),
                RecordedReceiptHashes = ledger,
            };
            var payload = Serialize(candidate);
            if (payload.Length > AgentPassportDocumentV1.MaximumBytes)
            {
                return Refused(
                    AgentPassportWriteCode.StoreUnavailable,
                    $"The passport store would exceed its {AgentPassportDocumentV1.MaximumBytes}-byte ceiling and was not written.",
                    loaded,
                    receipt.Passport.AgentId);
            }

            try
            {
                WriteAtomicLocked(payload);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return Refused(
                    AgentPassportWriteCode.StoreUnavailable,
                    "The passport store could not be written: " + exception.Message,
                    loaded,
                    receipt.Passport.AgentId);
            }

            return new AgentPassportWriteResultV1(
                created ? AgentPassportWriteCode.Created : AgentPassportWriteCode.Updated,
                created
                    ? "A public record was created for this agent."
                    : "This exhibition was added to the agent's public record.",
                Recorded: true,
                receipt.Passport.AgentId,
                Array.Empty<AgentPassportDropV1>(),
                loaded.Recovered,
                payload.Length,
                payload.Length,
                AgentPassportDocumentV1.CurrentSchemaVersion,
                candidate);
        }
    }

    /// <summary>
    /// Removes one agent's public record, or clears the store. Forgetting an
    /// agent also forgets the receipt hashes that built it, so the same
    /// exhibition can rebuild the record rather than being refused against a
    /// record that no longer exists.
    /// </summary>
    public AgentPassportForgetResultV1 Forget(string? agentId)
    {
        if (agentId is not null && string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException(
                "An agent id must be absent or non-empty.",
                nameof(agentId));
        }

        lock (_sync)
        {
            var loaded = LoadLocked();
            var document = loaded.Document;
            if (loaded.Blocked)
            {
                return new AgentPassportForgetResultV1(
                    AgentPassportForgetCode.StoreUnavailable,
                    "The stored passports could not be read or quarantined, so nothing was removed.",
                    Array.Empty<AgentPassportDropV1>(),
                    RecoveredFromCorruption: false,
                    loaded.BytesUsed,
                    loaded.BytesProjected,
                    loaded.StoredSchemaVersion,
                    document);
            }

            var removed = document.Records
                .Where(record => agentId is null
                    || string.Equals(record.AgentId, agentId, StringComparison.Ordinal))
                .Select(AgentPassportDropV1.FromRecord)
                .ToArray();
            if (removed.Length == 0)
            {
                return new AgentPassportForgetResultV1(
                    AgentPassportForgetCode.NotRecorded,
                    agentId is null
                        ? "No agent has a public record yet."
                        : "That agent has no public record.",
                    removed,
                    loaded.Recovered,
                    loaded.BytesUsed,
                    loaded.BytesProjected,
                    loaded.StoredSchemaVersion,
                    document);
            }

            var kept = document.Records
                .Where(record => agentId is not null
                    && !string.Equals(record.AgentId, agentId, StringComparison.Ordinal))
                .ToArray();
            var candidate = document with
            {
                Records = kept,
                RecordedReceiptHashes = AgentPassportDocumentV1.RebuildLedger(kept),
            };
            var payload = Serialize(candidate);
            try
            {
                WriteAtomicLocked(payload);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return new AgentPassportForgetResultV1(
                    AgentPassportForgetCode.StoreUnavailable,
                    "The passport store could not be written: " + exception.Message,
                    Array.Empty<AgentPassportDropV1>(),
                    loaded.Recovered,
                    loaded.BytesUsed,
                    loaded.BytesProjected,
                    loaded.StoredSchemaVersion,
                    document);
            }

            return new AgentPassportForgetResultV1(
                AgentPassportForgetCode.Forgotten,
                agentId is null
                    ? $"Every public record was removed ({removed.Length})."
                    : "That agent's public record was removed.",
                removed,
                loaded.Recovered,
                payload.Length,
                payload.Length,
                AgentPassportDocumentV1.CurrentSchemaVersion,
                candidate);
        }
    }

    private static AgentPassportWriteResultV1 Refused(
        AgentPassportWriteCode code,
        string message,
        LoadedDocument loaded,
        string? agentId) =>
        new(
            code,
            message,
            Recorded: false,
            agentId,
            Array.Empty<AgentPassportDropV1>(),
            loaded.Recovered,
            loaded.BytesUsed,
            loaded.BytesProjected,
            loaded.StoredSchemaVersion,
            loaded.Document);

    private LoadedDocument LoadLocked()
    {
        var path = DocumentPath;
        if (!File.Exists(path))
        {
            return new LoadedDocument(
                AgentPassportDocumentV1.Empty,
                Recovered: false,
                Blocked: false,
                BytesUsed: 0,
                Serialize(AgentPassportDocumentV1.Empty).Length,
                StoredSchemaVersion: 0);
        }

        AgentPassportDocumentV1? loaded = null;
        var storedBytes = 0;
        var storedSchemaVersion = 0;
        try
        {
            if (new FileInfo(path).Length <= AgentPassportDocumentV1.MaximumBytes)
            {
                var bytes = File.ReadAllBytes(path);
                storedBytes = bytes.Length;
                storedSchemaVersion = ReadSchemaVersion(bytes);
                loaded = JsonSerializer.Deserialize<AgentPassportDocumentV1>(
                    bytes,
                    SerializerOptions);
            }
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            loaded = null;
        }

        if (loaded is not null && loaded.IsWellFormed())
        {
            return new LoadedDocument(
                loaded,
                Recovered: false,
                Blocked: false,
                storedBytes,
                Serialize(loaded).Length,
                storedSchemaVersion);
        }

        var quarantined = TryQuarantineLocked(path);
        return new LoadedDocument(
            AgentPassportDocumentV1.Empty,
            quarantined,
            !quarantined,
            BytesUsed: 0,
            Serialize(AgentPassportDocumentV1.Empty).Length,
            StoredSchemaVersion: 0);
    }

    private static int ReadSchemaVersion(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.TryGetProperty("schema_version", out var version)
                && version.TryGetInt32(out var value)
                    ? value
                    : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static bool TryQuarantineLocked(string path)
    {
        try
        {
            for (var attempt = 0; attempt < MaximumQuarantineSlots; attempt++)
            {
                var candidate = attempt == 0
                    ? path + CorruptFileExtension
                    : $"{path}.{attempt}{CorruptFileExtension}";
                if (File.Exists(candidate))
                {
                    continue;
                }

                File.Move(path, candidate);
                return true;
            }

            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WriteAtomicLocked(byte[] payload)
    {
        Directory.CreateDirectory(_directory);
        var temporary = Path.Combine(_directory, TemporaryFileName);
        File.WriteAllBytes(temporary, payload);
        File.Move(temporary, DocumentPath, overwrite: true);
    }

    private static byte[] Serialize(AgentPassportDocumentV1 document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);

    private sealed record LoadedDocument(
        AgentPassportDocumentV1 Document,
        bool Recovered,
        bool Blocked,
        int BytesUsed,
        int BytesProjected,
        int StoredSchemaVersion);
}
