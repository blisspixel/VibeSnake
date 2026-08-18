using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

/// <summary>
/// Why one archive attempt ended the way it did. Every code is a factual
/// outcome rather than a grade, and only <see cref="ArchiveUnavailable"/> and
/// <see cref="ConflictingReceipt"/> describe a refusal the caller should treat
/// as a problem.
/// </summary>
public enum AgentExhibitionArchiveCode : byte
{
    /// <summary>The exhibition was written and is now readable.</summary>
    Archived = 0,

    /// <summary>An identical exhibition was already archived. Nothing was written.</summary>
    AlreadyArchived = 1,

    /// <summary>The match has no canonical receipt, so it has no exhibition identity.</summary>
    NoVerifiedReceipt = 2,

    /// <summary>Both verified lane replays must be saved before an exhibition can name them.</summary>
    ReplayNotSaved = 3,

    /// <summary>The archive could not be read or written. No state changed.</summary>
    ArchiveUnavailable = 4,

    /// <summary>
    /// A different exhibition already occupies this receipt hash. The archive
    /// never overwrites different data under an existing identity.
    /// </summary>
    ConflictingReceipt = 5,
}

/// <summary>
/// Why one removal request ended the way it did. Removal is deliberately its own
/// vocabulary: forgetting an exhibition that was never kept is not an error, and
/// a caller clearing a store should not have to distinguish that from a failure.
/// </summary>
public enum AgentExhibitionForgetCode : byte
{
    /// <summary>The named exhibitions were removed and the archive was rewritten.</summary>
    Forgotten = 0,

    /// <summary>No entry matched, so nothing was written.</summary>
    NotArchived = 1,

    /// <summary>The archive could not be read or written. No state changed.</summary>
    ArchiveUnavailable = 2,
}

/// <summary>
/// One archived exhibition. It stores the canonical receipt verbatim beside the
/// replay file names the application-owned replay store actually wrote, so both
/// lanes of a rivalry can be found again without re-deriving anything.
///
/// Every promoted field is a copy of a receipt value, published so an index can
/// be listed and chosen from without opening a single receipt. A playtester
/// reading the v1 index could tell two exhibitions apart only when their seeds
/// differed, and had to open a receipt to learn how a run ended or which
/// practice it was. v2 promotes the mode, the terminal facts, and the lesson and
/// style identities for exactly that reason.
/// </summary>
public sealed record AgentArchivedExhibitionV2(
    string Schema,
    string ReceiptHash,
    string RouteIdentityHash,
    string DivisionId,
    string ModeId,
    string GameplaySeed,
    int Score,
    AgentMatchEndReason EndReason,
    RunStatus RunStatus,
    string? LessonId,
    string? StyleContractId,
    string AgentReplayFileName,
    string? RivalReplayFileName,
    string? RivalPersonalityId,
    int? RivalScore,
    AgentExhibitionReceiptV2 Receipt)
{
    public const string Contract = "vibesnake-agent-archived-exhibition-v2";

    /// <summary>
    /// Builds an entry from a receipt and the two lane file names. The receipt
    /// is stored without its presentation display time, because display time is
    /// never part of exhibition identity and an archive that kept it would make
    /// the same exhibition look different on every visit.
    /// </summary>
    public static AgentArchivedExhibitionV2 Create(
        AgentExhibitionReceiptV2 receipt,
        string agentReplayFileName,
        string? rivalReplayFileName)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentReplayFileName);
        if (rivalReplayFileName is not null && string.IsNullOrWhiteSpace(rivalReplayFileName))
        {
            throw new ArgumentException(
                "A rival replay file name must be absent or non-empty.",
                nameof(rivalReplayFileName));
        }

        // A rivalry archives two lanes or it is not a rivalry.
        if ((receipt.RivalReplayPayloadHash is null) != (rivalReplayFileName is null))
        {
            throw new ArgumentException(
                "A receipted rival lane must be archived with its saved replay file name.",
                nameof(rivalReplayFileName));
        }

        return new AgentArchivedExhibitionV2(
            Contract,
            receipt.ReceiptHash,
            receipt.RouteIdentityHash,
            receipt.Division.DivisionId,
            receipt.Division.ModeId,
            receipt.GameplaySeed,
            receipt.Score,
            receipt.EndReason,
            receipt.RunStatus,
            receipt.LessonOutcome?.LessonId,
            receipt.StyleOutcome?.ContractId,
            agentReplayFileName,
            rivalReplayFileName,
            receipt.RivalPersonalityId,
            receipt.RivalScore,
            receipt.WithDisplayTime(null));
    }

    /// <summary>
    /// Whether two entries name the same exhibition kept the same way. This is
    /// deliberately not record equality: the receipt carries a list of accepted
    /// presentation events, and a positional record compares that list by
    /// reference, so two identical exhibitions loaded from different sources
    /// would compare unequal and a repeat archive would look like a conflict.
    /// The receipt hash already covers every canonical receipt fact, so the only
    /// fields left to compare are the lane file names the archive itself adds.
    /// </summary>
    public bool DescribesSameExhibitionAs(AgentArchivedExhibitionV2 other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(ReceiptHash, other.ReceiptHash, StringComparison.Ordinal)
            && string.Equals(
                RouteIdentityHash,
                other.RouteIdentityHash,
                StringComparison.Ordinal)
            && string.Equals(
                AgentReplayFileName,
                other.AgentReplayFileName,
                StringComparison.Ordinal)
            && string.Equals(
                RivalReplayFileName,
                other.RivalReplayFileName,
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Confirms that an entry still describes itself: the receipt recomputes to
    /// its own canonical hashes, and every promoted field still equals the
    /// receipt value it was copied from. A stored entry that fails this is
    /// treated as corruption rather than as data.
    /// </summary>
    public bool IsSelfConsistent() =>
        string.Equals(Schema, Contract, StringComparison.Ordinal)
        && AgentExhibitionReceipt.HasCanonicalHash(Receipt)
        && Receipt.DisplayTimeUtc is null
        && string.Equals(ReceiptHash, Receipt.ReceiptHash, StringComparison.Ordinal)
        && string.Equals(
            RouteIdentityHash,
            Receipt.RouteIdentityHash,
            StringComparison.Ordinal)
        && string.Equals(
            DivisionId,
            Receipt.Division.DivisionId,
            StringComparison.Ordinal)
        && string.Equals(ModeId, Receipt.Division.ModeId, StringComparison.Ordinal)
        && string.Equals(GameplaySeed, Receipt.GameplaySeed, StringComparison.Ordinal)
        && Score == Receipt.Score
        && EndReason == Receipt.EndReason
        && RunStatus == Receipt.RunStatus
        && string.Equals(LessonId, Receipt.LessonOutcome?.LessonId, StringComparison.Ordinal)
        && string.Equals(
            StyleContractId,
            Receipt.StyleOutcome?.ContractId,
            StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(AgentReplayFileName)
        && (RivalReplayFileName is null) == (Receipt.RivalReplayPayloadHash is null)
        && string.Equals(
            RivalPersonalityId,
            Receipt.RivalPersonalityId,
            StringComparison.Ordinal)
        && RivalScore == Receipt.RivalScore;
}

/// <summary>
/// The bounded local exhibition archive. It is explicit, application-owned, and
/// deliberately outside the supported Persistence assembly: an agent exhibition
/// is a preview surface and must never share a store, a schema, or a recovery
/// path with a human player's saves.
/// </summary>
public sealed record AgentExhibitionArchiveV2(
    string Schema,
    int SchemaVersion,
    int Capacity,
    IReadOnlyList<AgentArchivedExhibitionV2> Entries)
{
    public const string Contract = "vibesnake-agent-exhibition-archive-v2";
    public const int CurrentSchemaVersion = 2;

    /// <summary>The one older schema this store migrates forward rather than rejects.</summary>
    public const int LegacySchemaVersion = 1;
    public const string LegacyContract = "vibesnake-agent-exhibition-archive-v1";
    public const string LegacyEntryContract = "vibesnake-agent-archived-exhibition-v1";

    /// <summary>
    /// The archive is an exhibition shelf, not a history database. Oldest
    /// entries are evicted first and the caller is told exactly which ones.
    /// </summary>
    public const int MaximumEntries = 32;

    /// <summary>
    /// A hard serialized-byte ceiling checked before any write reaches disk, so
    /// an unusually large receipt cannot grow the file without bound even while
    /// the entry count is legal. A receipt carries one accepted presentation
    /// event per accepted rules step, so a long exhibition is far larger than a
    /// short one and the byte ceiling can evict before the entry count does.
    /// Effective capacity is therefore the lesser of the two bounds, which is
    /// why every result publishes the exact bytes the archive occupies.
    /// </summary>
    public const int MaximumBytes = 4_194_304;

    public static AgentExhibitionArchiveV2 Empty { get; } = new(
        Contract,
        CurrentSchemaVersion,
        MaximumEntries,
        Array.Empty<AgentArchivedExhibitionV2>());

    /// <summary>
    /// Whether a loaded document is structurally usable. Anything else is
    /// corruption: it is backed up rather than repaired, because a partially
    /// trusted exhibition archive is worse than an empty one.
    /// </summary>
    public bool IsWellFormed() =>
        string.Equals(Schema, Contract, StringComparison.Ordinal)
        && SchemaVersion == CurrentSchemaVersion
        && Capacity == MaximumEntries
        && Entries.Count <= MaximumEntries
        && Entries.All(entry => entry.IsSelfConsistent())
        && Entries
            .Select(entry => entry.ReceiptHash)
            .Distinct(StringComparer.Ordinal)
            .Count() == Entries.Count;
}

/// <summary>
/// One exhibition an operation dropped. Reporting only a count told a caller
/// that something was lost without telling them what, so both eviction and
/// removal now name the identities they took out.
/// </summary>
public sealed record AgentExhibitionArchiveDropV1(
    string Schema,
    string ReceiptHash,
    string RouteIdentityHash)
{
    public const string Contract = "vibesnake-agent-exhibition-archive-drop-v1";

    internal static AgentExhibitionArchiveDropV1 FromEntry(AgentArchivedExhibitionV2 entry) =>
        new(Contract, entry.ReceiptHash, entry.RouteIdentityHash);
}

/// <summary>
/// The factual outcome of one archive attempt, including the archive as it
/// stands afterwards. The archive is always returned, including on a refusal,
/// so a caller never has to guess what a failed write left behind.
/// </summary>
public sealed record AgentExhibitionArchiveWriteV1(
    AgentExhibitionArchiveCode Code,
    string Message,
    bool Archived,
    IReadOnlyList<AgentExhibitionArchiveDropV1> Evicted,
    bool RecoveredFromCorruption,
    bool MigratedFromLegacySchema,
    int BytesUsed,
    int BytesProjected,
    int StoredSchemaVersion,
    AgentExhibitionArchiveV2 Archive);

/// <summary>
/// The factual outcome of one removal request.
/// </summary>
public sealed record AgentExhibitionForgetResultV1(
    AgentExhibitionForgetCode Code,
    string Message,
    IReadOnlyList<AgentExhibitionArchiveDropV1> Forgotten,
    bool RecoveredFromCorruption,
    bool MigratedFromLegacySchema,
    int BytesUsed,
    int BytesProjected,
    int StoredSchemaVersion,
    AgentExhibitionArchiveV2 Archive);

/// <summary>
/// Reads and writes the bounded exhibition archive under one caller-supplied
/// user-data root. The store never discovers a path of its own: path policy
/// belongs to the host that owns the platform, and a store that could pick its
/// own directory would be untestable and unauditable.
/// </summary>
public sealed class AgentExhibitionArchiveStore
{
    public const string DirectoryName = "agent_arena";
    public const string FileName = "exhibition_archive.json";
    public const string TemporaryFileName = "exhibition_archive.json.tmp";
    public const string CorruptFileExtension = ".corrupt.json";

    /// <summary>
    /// How many unreadable documents may be kept beside the archive before the
    /// store refuses to write at all. Discarding evidence to make room would
    /// defeat the point of quarantining it.
    /// </summary>
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

    public AgentExhibitionArchiveStore(string userDataRoot)
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

    public string ArchivePath => Path.Combine(_directory, FileName);

    /// <summary>
    /// Returns the archive as it stands. A missing file is an empty archive, the
    /// one supported older schema is migrated forward, and an unreadable or
    /// inconsistent file is quarantined and reported as empty rather than
    /// silently repaired.
    /// </summary>
    public AgentExhibitionArchiveV2 Read()
    {
        lock (_sync)
        {
            return LoadLocked().Archive;
        }
    }

    /// <summary>
    /// The archive as it stands, with the exact bytes it occupies and whether
    /// reading it recovered from corruption or migrated an older schema.
    /// </summary>
    public AgentExhibitionArchiveReadV1 Inspect()
    {
        lock (_sync)
        {
            var loaded = LoadLocked();
            return new AgentExhibitionArchiveReadV1(
                loaded.Archive,
                loaded.Recovered,
                loaded.Blocked,
                loaded.Migrated,
                loaded.BytesUsed,
                loaded.BytesProjected,
                loaded.StoredSchemaVersion);
        }
    }

    /// <summary>
    /// Whether the archive currently holds a document that could neither be
    /// read nor moved aside. Writing over it would destroy bytes a person may
    /// still need, so every write refuses while this is true. Answering performs
    /// the same load and quarantine attempt a write would, so a recoverable
    /// document is quarantined here too rather than only inspected.
    /// </summary>
    public bool IsBlocked()
    {
        lock (_sync)
        {
            return LoadLocked().Blocked;
        }
    }

    /// <summary>
    /// Archives one verified exhibition beside its saved lane replays. The write
    /// is atomic, bounded, and refuses to overwrite a different exhibition that
    /// already occupies the same receipt hash.
    /// </summary>
    public AgentExhibitionArchiveWriteV1 Archive(
        AgentExhibitionReceiptV2 receipt,
        string agentReplayFileName,
        string? rivalReplayFileName)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var entry = AgentArchivedExhibitionV2.Create(
            receipt,
            agentReplayFileName,
            rivalReplayFileName);
        lock (_sync)
        {
            var loaded = LoadLocked();
            var archive = loaded.Archive;
            if (loaded.Blocked)
            {
                // The stored document is unreadable and could not be moved
                // aside. Writing now would overwrite bytes a person may still
                // need to inspect, so refuse instead.
                return Refused(
                    AgentExhibitionArchiveCode.ArchiveUnavailable,
                    "The stored archive could not be read or quarantined, so nothing was written. Move or remove it before archiving again.",
                    loaded);
            }

            var existing = archive.Entries.FirstOrDefault(candidate => string.Equals(
                candidate.ReceiptHash,
                entry.ReceiptHash,
                StringComparison.Ordinal));
            if (existing is not null)
            {
                return existing.DescribesSameExhibitionAs(entry)
                    ? Refused(
                        AgentExhibitionArchiveCode.AlreadyArchived,
                        "This exhibition is already archived under the same receipt hash.",
                        loaded)
                    : Refused(
                        AgentExhibitionArchiveCode.ConflictingReceipt,
                        "A different exhibition already occupies this receipt hash. The archive never overwrites it.",
                        loaded);
            }

            var kept = archive.Entries.ToList();
            kept.Add(entry);
            var evicted = new List<AgentExhibitionArchiveDropV1>();
            while (kept.Count > AgentExhibitionArchiveV2.MaximumEntries)
            {
                evicted.Add(AgentExhibitionArchiveDropV1.FromEntry(kept[0]));
                kept.RemoveAt(0);
            }

            var candidateArchive = archive with { Entries = kept.AsReadOnly() };
            var payload = Serialize(candidateArchive);
            // Measure the exact bytes that would land on disk, not an estimate.
            while (payload.Length > AgentExhibitionArchiveV2.MaximumBytes && kept.Count > 1)
            {
                evicted.Add(AgentExhibitionArchiveDropV1.FromEntry(kept[0]));
                kept.RemoveAt(0);
                candidateArchive = archive with { Entries = kept.AsReadOnly() };
                payload = Serialize(candidateArchive);
            }

            if (payload.Length > AgentExhibitionArchiveV2.MaximumBytes)
            {
                return Refused(
                    AgentExhibitionArchiveCode.ArchiveUnavailable,
                    $"One exhibition exceeds the {AgentExhibitionArchiveV2.MaximumBytes}-byte archive ceiling and was not written.",
                    loaded);
            }

            try
            {
                WriteAtomicLocked(payload);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return Refused(
                    AgentExhibitionArchiveCode.ArchiveUnavailable,
                    "The exhibition archive could not be written: " + exception.Message,
                    loaded);
            }

            return new AgentExhibitionArchiveWriteV1(
                AgentExhibitionArchiveCode.Archived,
                evicted.Count == 0
                    ? "The exhibition was archived."
                    : $"The exhibition was archived and {evicted.Count} older exhibition(s) were evicted at capacity.",
                Archived: true,
                evicted.AsReadOnly(),
                loaded.Recovered,
                loaded.Migrated,
                payload.Length,
                payload.Length,
                AgentExhibitionArchiveV2.CurrentSchemaVersion,
                candidateArchive);
        }
    }

    /// <summary>
    /// Removes one archived exhibition by receipt hash, or every exhibition when
    /// no hash is given. Nothing but eviction used to remove anything, which left
    /// a caller with no way to drop a run they did not want to keep, and no way
    /// to clear a store whose named replay files they had already deleted.
    /// </summary>
    public AgentExhibitionForgetResultV1 Forget(string? receiptHash)
    {
        if (receiptHash is not null && string.IsNullOrWhiteSpace(receiptHash))
        {
            throw new ArgumentException(
                "A receipt hash must be absent or non-empty.",
                nameof(receiptHash));
        }

        lock (_sync)
        {
            var loaded = LoadLocked();
            var archive = loaded.Archive;
            if (loaded.Blocked)
            {
                return new AgentExhibitionForgetResultV1(
                    AgentExhibitionForgetCode.ArchiveUnavailable,
                    "The stored archive could not be read or quarantined, so nothing was removed. Move or remove it before trying again.",
                    Array.Empty<AgentExhibitionArchiveDropV1>(),
                    RecoveredFromCorruption: false,
                    MigratedFromLegacySchema: false,
                    loaded.BytesUsed,
                    loaded.BytesProjected,
                    loaded.StoredSchemaVersion,
                    archive);
            }

            var removed = archive.Entries
                .Where(entry => receiptHash is null
                    || string.Equals(entry.ReceiptHash, receiptHash, StringComparison.Ordinal))
                .Select(AgentExhibitionArchiveDropV1.FromEntry)
                .ToArray();
            if (removed.Length == 0)
            {
                return new AgentExhibitionForgetResultV1(
                    AgentExhibitionForgetCode.NotArchived,
                    receiptHash is null
                        ? "The archive is already empty."
                        : "No archived exhibition carries that receipt hash.",
                    removed,
                    loaded.Recovered,
                    loaded.Migrated,
                    loaded.BytesUsed,
                    loaded.BytesProjected,
                    loaded.StoredSchemaVersion,
                    archive);
            }

            var kept = archive.Entries
                .Where(entry => receiptHash is not null
                    && !string.Equals(entry.ReceiptHash, receiptHash, StringComparison.Ordinal))
                .ToArray();
            var candidateArchive = archive with { Entries = kept };
            var payload = Serialize(candidateArchive);
            try
            {
                WriteAtomicLocked(payload);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return new AgentExhibitionForgetResultV1(
                    AgentExhibitionForgetCode.ArchiveUnavailable,
                    "The exhibition archive could not be written: " + exception.Message,
                    Array.Empty<AgentExhibitionArchiveDropV1>(),
                    loaded.Recovered,
                    loaded.Migrated,
                    loaded.BytesUsed,
                    loaded.BytesProjected,
                    loaded.StoredSchemaVersion,
                    archive);
            }

            return new AgentExhibitionForgetResultV1(
                AgentExhibitionForgetCode.Forgotten,
                receiptHash is null
                    ? $"The archive was cleared and {removed.Length} exhibition(s) were removed."
                    : "The exhibition was removed from the archive.",
                removed,
                loaded.Recovered,
                loaded.Migrated,
                payload.Length,
                payload.Length,
                AgentExhibitionArchiveV2.CurrentSchemaVersion,
                candidateArchive);
        }
    }

    private static AgentExhibitionArchiveWriteV1 Refused(
        AgentExhibitionArchiveCode code,
        string message,
        LoadedArchive loaded) =>
        new(
            code,
            message,
            Archived: false,
            Array.Empty<AgentExhibitionArchiveDropV1>(),
            loaded.Recovered,
            loaded.Migrated,
            loaded.BytesUsed,
            loaded.BytesProjected,
            loaded.StoredSchemaVersion,
            loaded.Archive);

    private LoadedArchive LoadLocked()
    {
        var path = ArchivePath;
        if (!File.Exists(path))
        {
            return new LoadedArchive(
                AgentExhibitionArchiveV2.Empty,
                Recovered: false,
                Blocked: false,
                Migrated: false,
                BytesUsed: 0,
                Serialize(AgentExhibitionArchiveV2.Empty).Length,
                StoredSchemaVersion: 0);
        }

        AgentExhibitionArchiveV2? loaded = null;
        var migrated = false;
        var storedSchemaVersion = 0;
        var storedBytes = 0;
        try
        {
            // Check the size before reading it, so a file that grew outside this
            // store can never be pulled into memory in full just to be rejected.
            var length = new FileInfo(path).Length;
            if (length <= AgentExhibitionArchiveV2.MaximumBytes)
            {
                var bytes = File.ReadAllBytes(path);
                storedBytes = bytes.Length;
                storedSchemaVersion = ReadSchemaVersion(bytes);
                (loaded, migrated) = Deserialize(bytes);
            }
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            loaded = null;
        }

        if (loaded is not null && loaded.IsWellFormed())
        {
            return new LoadedArchive(
                loaded,
                Recovered: false,
                Blocked: false,
                migrated,
                storedBytes,
                Serialize(loaded).Length,
                storedSchemaVersion);
        }

        var quarantined = TryQuarantineLocked(path);
        return new LoadedArchive(
            AgentExhibitionArchiveV2.Empty,
            quarantined,
            !quarantined,
            Migrated: false,
            BytesUsed: 0,
            Serialize(AgentExhibitionArchiveV2.Empty).Length,
            StoredSchemaVersion: 0);
    }

    /// <summary>
    /// Reads the current schema, or migrates the one supported older schema
    /// forward. Migration is lossless by construction: every field v2 promotes
    /// is derived from the receipt that v1 already stored verbatim, and the
    /// rebuilt entry has to verify against that receipt exactly as a freshly
    /// archived one would. A legacy document that fails any of those checks is
    /// treated as corruption rather than migrated on hope.
    /// </summary>
    private static (AgentExhibitionArchiveV2? Archive, bool Migrated) Deserialize(byte[] bytes)
    {
        if (ReadSchemaVersion(bytes) != AgentExhibitionArchiveV2.LegacySchemaVersion)
        {
            return (
                JsonSerializer.Deserialize<AgentExhibitionArchiveV2>(bytes, SerializerOptions),
                false);
        }

        var legacy = JsonSerializer.Deserialize<LegacyArchive>(bytes, SerializerOptions);
        if (legacy is null
            || !string.Equals(
                legacy.Schema,
                AgentExhibitionArchiveV2.LegacyContract,
                StringComparison.Ordinal)
            || legacy.Capacity != AgentExhibitionArchiveV2.MaximumEntries
            || legacy.Entries.Count > AgentExhibitionArchiveV2.MaximumEntries)
        {
            return (null, false);
        }

        var upgraded = new List<AgentArchivedExhibitionV2>(legacy.Entries.Count);
        foreach (var entry in legacy.Entries)
        {
            if (!string.Equals(
                    entry.Schema,
                    AgentExhibitionArchiveV2.LegacyEntryContract,
                    StringComparison.Ordinal)
                || entry.Receipt.DisplayTimeUtc is not null
                || !AgentExhibitionReceipt.HasCanonicalHash(entry.Receipt)
                || !string.Equals(
                    entry.ReceiptHash,
                    entry.Receipt.ReceiptHash,
                    StringComparison.Ordinal))
            {
                return (null, false);
            }

            upgraded.Add(AgentArchivedExhibitionV2.Create(
                entry.Receipt,
                entry.AgentReplayFileName,
                entry.RivalReplayFileName));
        }

        return (
            AgentExhibitionArchiveV2.Empty with { Entries = upgraded.AsReadOnly() },
            true);
    }

    private static int ReadSchemaVersion(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("schema_version", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var version)
                ? version
                : 0;
    }

    private static bool TryQuarantineLocked(string path)
    {
        try
        {
            // Keep the unreadable bytes rather than deleting them. A person can
            // still inspect what went wrong, and no exhibition is destroyed by a
            // schema or transfer accident. If every slot is taken, refuse rather
            // than start discarding evidence.
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
        File.Move(temporary, ArchivePath, overwrite: true);
    }

    private static byte[] Serialize(AgentExhibitionArchiveV2 archive) =>
        JsonSerializer.SerializeToUtf8Bytes(archive, SerializerOptions);

    /// <summary>
    /// One load. Two sizes are carried rather than one because a read never
    /// writes: after a migration the document in memory is the current schema
    /// while the bytes on disk are still the old one, and a playtester checking
    /// bytes_used against the file found them disagreeing. BytesUsed is what the
    /// file holds now; BytesProjected is what the next write would produce and
    /// is therefore the size the byte ceiling actually binds.
    /// </summary>
    private sealed record LoadedArchive(
        AgentExhibitionArchiveV2 Archive,
        bool Recovered,
        bool Blocked,
        bool Migrated,
        int BytesUsed,
        int BytesProjected,
        int StoredSchemaVersion);

    /// <summary>
    /// The schema-1 shape, retained only so a store written by an earlier host
    /// can be migrated instead of quarantined.
    /// </summary>
    private sealed record LegacyArchive(
        string Schema,
        int SchemaVersion,
        int Capacity,
        IReadOnlyList<LegacyEntry> Entries);

    private sealed record LegacyEntry(
        string Schema,
        string ReceiptHash,
        string RouteIdentityHash,
        string DivisionId,
        string GameplaySeed,
        int Score,
        string AgentReplayFileName,
        string? RivalReplayFileName,
        string? RivalPersonalityId,
        int? RivalScore,
        AgentExhibitionReceiptV2 Receipt);
}

/// <summary>
/// One read of the archive, with the exact bytes it occupies. A caller cannot
/// see the effective room left from an entry count alone, because the byte
/// ceiling can bind first.
/// </summary>
public sealed record AgentExhibitionArchiveReadV1(
    AgentExhibitionArchiveV2 Archive,
    bool RecoveredFromCorruption,
    bool Blocked,
    bool MigratedFromLegacySchema,
    int BytesUsed,
    int BytesProjected,
    int StoredSchemaVersion);
