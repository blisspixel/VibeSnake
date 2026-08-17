using System.Text.Json;
using System.Text.Json.Serialization;

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
/// One archived exhibition. It stores the canonical receipt verbatim beside the
/// replay file names the application-owned replay store actually wrote, so both
/// lanes of a rivalry can be found again without re-deriving anything. The
/// promoted fields are copies of receipt values, published so an index can be
/// listed without opening every receipt.
/// </summary>
public sealed record AgentArchivedExhibitionV1(
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
    AgentExhibitionReceiptV2 Receipt)
{
    public const string Contract = "vibesnake-agent-archived-exhibition-v1";

    /// <summary>
    /// Builds an entry from a receipt and the two lane file names. The receipt
    /// is stored without its presentation display time, because display time is
    /// never part of exhibition identity and an archive that kept it would make
    /// the same exhibition look different on every visit.
    /// </summary>
    public static AgentArchivedExhibitionV1 Create(
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

        return new AgentArchivedExhibitionV1(
            Contract,
            receipt.ReceiptHash,
            receipt.RouteIdentityHash,
            receipt.Division.DivisionId,
            receipt.GameplaySeed,
            receipt.Score,
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
    public bool DescribesSameExhibitionAs(AgentArchivedExhibitionV1 other)
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
        && string.Equals(GameplaySeed, Receipt.GameplaySeed, StringComparison.Ordinal)
        && Score == Receipt.Score
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
public sealed record AgentExhibitionArchiveV1(
    string Schema,
    int SchemaVersion,
    int Capacity,
    IReadOnlyList<AgentArchivedExhibitionV1> Entries)
{
    public const string Contract = "vibesnake-agent-exhibition-archive-v1";
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The archive is an exhibition shelf, not a history database. Oldest
    /// entries are evicted first and the caller is told how many were dropped.
    /// </summary>
    public const int MaximumEntries = 32;

    /// <summary>
    /// A hard serialized-byte ceiling checked before any write reaches disk, so
    /// an unusually large receipt cannot grow the file without bound even while
    /// the entry count is legal. A receipt carries one accepted presentation
    /// event per accepted rules step, so a long exhibition is far larger than a
    /// short one and the byte ceiling can evict before the entry count does.
    /// Effective capacity is therefore the lesser of the two bounds.
    /// </summary>
    public const int MaximumBytes = 4_194_304;

    public static AgentExhibitionArchiveV1 Empty { get; } = new(
        Contract,
        CurrentSchemaVersion,
        MaximumEntries,
        Array.Empty<AgentArchivedExhibitionV1>());

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
/// The factual outcome of one archive attempt, including the archive as it
/// stands afterwards. The archive is always returned, including on a refusal,
/// so a caller never has to guess what a failed write left behind.
/// </summary>
public sealed record AgentExhibitionArchiveWriteV1(
    AgentExhibitionArchiveCode Code,
    string Message,
    bool Archived,
    int EvictedCount,
    bool RecoveredFromCorruption,
    AgentExhibitionArchiveV1 Archive);

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
    /// Returns the archive as it stands. A missing file is an empty archive;
    /// an unreadable or inconsistent file is quarantined and reported as empty
    /// rather than silently repaired.
    /// </summary>
    public AgentExhibitionArchiveV1 Read()
    {
        lock (_sync)
        {
            return LoadLocked().Archive;
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
        var entry = AgentArchivedExhibitionV1.Create(
            receipt,
            agentReplayFileName,
            rivalReplayFileName);
        lock (_sync)
        {
            var (archive, recovered, blocked) = LoadLocked();
            if (blocked)
            {
                // The stored document is unreadable and could not be moved
                // aside. Writing now would overwrite bytes a person may still
                // need to inspect, so refuse instead.
                return new AgentExhibitionArchiveWriteV1(
                    AgentExhibitionArchiveCode.ArchiveUnavailable,
                    "The stored archive could not be read or quarantined, so nothing was written. Move or remove it before archiving again.",
                    Archived: false,
                    EvictedCount: 0,
                    RecoveredFromCorruption: false,
                    archive);
            }

            var existing = archive.Entries.FirstOrDefault(candidate => string.Equals(
                candidate.ReceiptHash,
                entry.ReceiptHash,
                StringComparison.Ordinal));
            if (existing is not null)
            {
                return existing.DescribesSameExhibitionAs(entry)
                    ? new AgentExhibitionArchiveWriteV1(
                        AgentExhibitionArchiveCode.AlreadyArchived,
                        "This exhibition is already archived under the same receipt hash.",
                        Archived: false,
                        EvictedCount: 0,
                        recovered,
                        archive)
                    : new AgentExhibitionArchiveWriteV1(
                        AgentExhibitionArchiveCode.ConflictingReceipt,
                        "A different exhibition already occupies this receipt hash. The archive never overwrites it.",
                        Archived: false,
                        EvictedCount: 0,
                        recovered,
                        archive);
            }

            var kept = archive.Entries.ToList();
            kept.Add(entry);
            var evicted = 0;
            while (kept.Count > AgentExhibitionArchiveV1.MaximumEntries)
            {
                kept.RemoveAt(0);
                evicted++;
            }

            var candidateArchive = archive with { Entries = kept.AsReadOnly() };
            var payload = Serialize(candidateArchive);
            // Measure the exact bytes that would land on disk, not an estimate.
            while (payload.Length > AgentExhibitionArchiveV1.MaximumBytes && kept.Count > 1)
            {
                kept.RemoveAt(0);
                evicted++;
                candidateArchive = archive with { Entries = kept.AsReadOnly() };
                payload = Serialize(candidateArchive);
            }

            if (payload.Length > AgentExhibitionArchiveV1.MaximumBytes)
            {
                return new AgentExhibitionArchiveWriteV1(
                    AgentExhibitionArchiveCode.ArchiveUnavailable,
                    $"One exhibition exceeds the {AgentExhibitionArchiveV1.MaximumBytes}-byte archive ceiling and was not written.",
                    Archived: false,
                    EvictedCount: 0,
                    recovered,
                    archive);
            }

            try
            {
                WriteAtomicLocked(payload);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return new AgentExhibitionArchiveWriteV1(
                    AgentExhibitionArchiveCode.ArchiveUnavailable,
                    "The exhibition archive could not be written: " + exception.Message,
                    Archived: false,
                    EvictedCount: 0,
                    recovered,
                    archive);
            }

            return new AgentExhibitionArchiveWriteV1(
                AgentExhibitionArchiveCode.Archived,
                evicted == 0
                    ? "The exhibition was archived."
                    : $"The exhibition was archived and {evicted} older exhibition(s) were evicted at capacity.",
                Archived: true,
                evicted,
                recovered,
                candidateArchive);
        }
    }

    private (AgentExhibitionArchiveV1 Archive, bool Recovered, bool Blocked) LoadLocked()
    {
        var path = ArchivePath;
        if (!File.Exists(path))
        {
            return (AgentExhibitionArchiveV1.Empty, false, false);
        }

        AgentExhibitionArchiveV1? loaded = null;
        try
        {
            // Check the size before reading it, so a file that grew outside this
            // store can never be pulled into memory in full just to be rejected.
            if (new FileInfo(path).Length <= AgentExhibitionArchiveV1.MaximumBytes)
            {
                loaded = JsonSerializer.Deserialize<AgentExhibitionArchiveV1>(
                    File.ReadAllBytes(path),
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
            return (loaded, false, false);
        }

        var quarantined = TryQuarantineLocked(path);
        return (AgentExhibitionArchiveV1.Empty, quarantined, !quarantined);
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

    private static byte[] Serialize(AgentExhibitionArchiveV1 archive) =>
        JsonSerializer.SerializeToUtf8Bytes(archive, SerializerOptions);
}
