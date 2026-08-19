using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.AgentHost;

public sealed record StartAgentMatchV5(
    string Schema,
    string MatchHandle,
    string RetentionPolicy,
    AgentObservationV5 Observation,
    AgentViewerConnectionV1? Viewer)
{
    public const string Contract = "vibesnake-agent-match-start-v5";
}

public sealed record AgentMatchSummaryV5(
    string Schema,
    string MatchHandle,
    AgentMatchLifecycle Lifecycle,
    AgentMatchEndReason EndReason,
    string RulesetId,
    int RulesVersion,
    string ModeId,
    int ModeVersion,
    string ConfigHashAlgorithm,
    string ConfigHash,
    AgentSeedVisibility SeedVisibility,
    string GameplaySeed,
    AgentPassportV4 Passport,
    int FinalTick,
    RunStatus RunStatus,
    DeathCause DeathCause,
    int Score,
    string FinalStateHash,
    string ReplayPayloadHash,
    ReplayVerificationCode ReplayVerificationCode,
    AgentEpisodeMetricsV1 EpisodeMetrics,
    AgentStyleOutcomeV3? StyleOutcome,
    AgentLessonOutcomeV3? LessonOutcome,
    AgentRivalResultV1? Rival)
{
    public const string Contract = "vibesnake-agent-match-summary-v5";

    internal static AgentMatchSummaryV5 FromResult(AgentMatchResultV5 result) =>
        new(
            Contract,
            result.MatchId,
            result.Lifecycle,
            result.EndReason,
            result.RulesetId,
            result.RulesVersion,
            result.ModeId,
            result.ModeVersion,
            result.ConfigHashAlgorithm,
            result.ConfigHash,
            result.SeedVisibility,
            result.GameplaySeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.Passport,
            result.FinalTick,
            result.RunStatus,
            result.DeathCause,
            result.Score,
            result.FinalStateHash,
            result.ReplayPayloadHash,
            result.ReplayVerificationCode,
            result.EpisodeMetrics,
            result.StyleOutcome,
            result.LessonOutcome,
            result.Rival);
}

public sealed record AgentMatchResultStatusV5(
    string Schema,
    string MatchHandle,
    bool IsAvailable,
    AgentMatchSummaryV5? Result)
{
    public const string Contract = "vibesnake-agent-match-result-status-v5";
}

public sealed record AgentActionResponseV5(
    string Schema,
    bool Accepted,
    bool RulesAdvanced,
    AgentActionRejection Rejection,
    AgentLessonProgressDeltaV2? LessonDelta,
    AgentObservationV5 Observation,
    AgentMatchSummaryV5? MatchResult)
{
    public const string Contract = "vibesnake-agent-action-response-v5";

    internal static AgentActionResponseV5 FromResponse(AgentActionResponse response) =>
        new(
            Contract,
            response.Accepted,
            response.RulesAdvanced,
            response.Rejection,
            response.LessonDelta,
            response.Observation,
            response.MatchResult is null
                ? null
                : AgentMatchSummaryV5.FromResult(response.MatchResult));
}

public sealed record AgentBurstResponseV5(
    string Schema,
    bool Accepted,
    bool RulesAdvanced,
    AgentActionRejection Rejection,
    int StepsAdvanced,
    AgentBurstStopReason? StopReason,
    RunEventKind? StopEvent,
    AgentLessonProgressDeltaV2? LessonDelta,
    AgentObservationV5 Observation,
    AgentMatchSummaryV5? MatchResult)
{
    public const string Contract = "vibesnake-agent-burst-response-v5";

    internal static AgentBurstResponseV5 FromResponse(AgentBurstResponse response) =>
        new(
            Contract,
            response.Accepted,
            response.RulesAdvanced,
            response.Rejection,
            response.StepsAdvanced,
            response.StopReason,
            response.StopEvent,
            response.LessonDelta,
            response.Observation,
            response.MatchResult is null
                ? null
                : AgentMatchSummaryV5.FromResult(response.MatchResult));
}

public sealed record AgentExhibitionReceiptStatusV1(
    string Schema,
    string MatchHandle,
    bool IsAvailable,
    AgentExhibitionReceiptV2? Receipt)
{
    public const string Contract = "vibesnake-agent-exhibition-receipt-status-v1";
}

/// <summary>
/// The result of one explicit archive request, including the archive index as
/// it stands afterwards. The index is always present, including on a refusal,
/// so a caller never has to guess what a failed write left behind. Entries carry
/// their promoted identity fields rather than the full receipts, because a
/// browser lists exhibitions far more often than it opens one.
/// </summary>
public sealed record AgentExhibitionArchiveStatusV2(
    string Schema,
    string MatchHandle,
    bool Archived,
    AgentExhibitionArchiveCode Code,
    string Message,
    string? ReceiptHash,
    string? RouteIdentityHash,
    IReadOnlyList<AgentExhibitionArchiveDropV1> Evicted,
    AgentExhibitionArchiveIndexV3 Archive) : IAgentExhibitionArchiveResponse
{
    public const string Contract = "vibesnake-agent-exhibition-archive-status-v2";
}

/// <summary>
/// The result of one explicit removal request.
/// </summary>
public sealed record AgentExhibitionForgetStatusV1(
    string Schema,
    bool Forgotten,
    AgentExhibitionForgetCode Code,
    string Message,
    IReadOnlyList<AgentExhibitionArchiveDropV1> Removed,
    AgentExhibitionArchiveIndexV3 Archive) : IAgentExhibitionArchiveResponse
{
    public const string Contract = "vibesnake-agent-exhibition-forget-status-v1";
}

/// <summary>
/// One read-only listing of the archive, optionally narrowed to a single walked
/// line. A caller used to be able to see the index only by writing to it.
/// </summary>
public sealed record AgentExhibitionArchiveListingV1(
    string Schema,
    string? RouteIdentityHashFilter,
    int MatchedCount,
    AgentExhibitionArchiveIndexV3 Archive) : IAgentExhibitionArchiveResponse
{
    public const string Contract = "vibesnake-agent-exhibition-listing-v1";
}

/// <summary>
/// Marker for every response that carries the archive index, so one gate can
/// assert that no archive surface answers without publishing its bounds.
/// </summary>
public interface IAgentExhibitionArchiveResponse
{
    AgentExhibitionArchiveIndexV3 Archive { get; }
}

/// <summary>
/// The archive as it stands, with both of its bounds and the bytes it occupies.
/// Effective capacity is the lesser of the entry and byte ceilings, so an entry
/// count alone cannot tell a caller how much room is actually left.
///
/// Two sizes and two schema versions are published rather than one of each,
/// because a read never writes. After a legacy archive is migrated on read, the
/// document in memory is the current schema while the file still holds the old
/// one. A playtester checking bytes_used against the file found them disagreeing
/// and was right to. bytes_used is what the file holds now and is verifiable
/// against it; bytes_projected is what the next write would produce and is the
/// size the byte ceiling actually binds, which is why remaining_bytes follows it.
/// </summary>
public sealed record AgentExhibitionArchiveIndexV3(
    string Schema,
    int SchemaVersion,
    int StoredSchemaVersion,
    int EntryCount,
    int Capacity,
    int BytesUsed,
    int BytesProjected,
    int MaximumBytes,
    int RemainingEntries,
    int RemainingBytes,
    bool RecoveredFromCorruption,
    bool MigratedFromLegacySchema,
    IReadOnlyList<AgentArchivedExhibitionIndexEntryV3> Entries)
{
    public const string Contract = "vibesnake-agent-exhibition-archive-index-v3";

    internal static AgentExhibitionArchiveIndexV3 Create(
        AgentExhibitionArchiveV2 archive,
        int bytesUsed,
        int bytesProjected,
        int storedSchemaVersion,
        bool recoveredFromCorruption,
        bool migratedFromLegacySchema,
        Func<string, bool> replayFileExists,
        IReadOnlyList<AgentArchivedExhibitionV2>? listed = null)
    {
        // Position is an entry's place in the whole store, not in the listing,
        // so a filtered listing still says where each exhibition sits and which
        // one eviction reaches first.
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < archive.Entries.Count; index++)
        {
            positions[archive.Entries[index].ReceiptHash] = index;
        }

        return new AgentExhibitionArchiveIndexV3(
            Contract,
            archive.SchemaVersion,
            storedSchemaVersion,
            archive.Entries.Count,
            archive.Capacity,
            bytesUsed,
            bytesProjected,
            AgentExhibitionArchiveV2.MaximumBytes,
            Math.Max(0, archive.Capacity - archive.Entries.Count),
            Math.Max(0, AgentExhibitionArchiveV2.MaximumBytes - bytesProjected),
            recoveredFromCorruption,
            migratedFromLegacySchema,
            (listed ?? archive.Entries)
                .Select(entry => AgentArchivedExhibitionIndexEntryV3.FromEntry(
                    entry,
                    positions.TryGetValue(entry.ReceiptHash, out var position) ? position : -1,
                    replayFileExists))
                .ToArray());
    }
}

/// <summary>
/// One listed exhibition. Every identity field is a copy of a receipt value, so
/// listing the archive reveals nothing the receipt did not already publish. Two
/// fields are computed at read time rather than copied: the presence flags,
/// because a named replay file can be deleted after it was archived, and the
/// position, because eviction order is a property of the store rather than of
/// any one exhibition and a filtered listing would otherwise lose it.
/// </summary>
public sealed record AgentArchivedExhibitionIndexEntryV3(
    string Schema,
    int Position,
    string ReceiptHash,
    string RouteIdentityHash,
    string DivisionId,
    string ModeId,
    string GameplaySeed,
    int Score,
    int FinalTick,
    AgentMatchEndReason EndReason,
    RunStatus RunStatus,
    string? LessonId,
    string? StyleContractId,
    string AgentReplayFileName,
    bool AgentReplayPresent,
    string? RivalReplayFileName,
    bool? RivalReplayPresent,
    string? RivalPersonalityId,
    int? RivalScore)
{
    public const string Contract = "vibesnake-agent-archived-exhibition-index-entry-v3";

    internal static AgentArchivedExhibitionIndexEntryV3 FromEntry(
        AgentArchivedExhibitionV2 entry,
        int position,
        Func<string, bool> replayFileExists) =>
        new(
            Contract,
            position,
            entry.ReceiptHash,
            entry.RouteIdentityHash,
            entry.DivisionId,
            entry.ModeId,
            entry.GameplaySeed,
            entry.Score,
            entry.Receipt.FinalTick,
            entry.EndReason,
            entry.RunStatus,
            entry.LessonId,
            entry.StyleContractId,
            entry.AgentReplayFileName,
            replayFileExists(entry.AgentReplayFileName),
            entry.RivalReplayFileName,
            entry.RivalReplayFileName is null
                ? null
                : replayFileExists(entry.RivalReplayFileName),
            entry.RivalPersonalityId,
            entry.RivalScore);
}

public sealed record AgentReplaySaveV1(
    string Schema,
    string MatchHandle,
    bool IsSuccess,
    ReplaySaveCode Code,
    string Message,
    string? FileName,
    ReplayVerificationCode? ReplayVerificationCode,
    ReplaySaveCode? RivalCode,
    string? RivalMessage,
    string? RivalFileName,
    ReplayVerificationCode? RivalReplayVerificationCode)
{
    public const string Contract = "vibesnake-agent-replay-save-v1";
}

/// <summary>
/// The result of one explicit passport write, including the store index as it
/// stands afterwards. The index is always present, including on a refusal.
/// </summary>
public sealed record AgentPassportWriteStatusV1(
    string Schema,
    string? MatchHandle,
    bool Recorded,
    AgentPassportWriteCode Code,
    string Message,
    string? AgentId,
    IReadOnlyList<AgentPassportDropV1> Evicted,
    AgentPassportIndexV1 Passports) : IAgentPassportResponse
{
    public const string Contract = "vibesnake-agent-passport-write-status-v1";
}

/// <summary>The result of one explicit passport removal.</summary>
public sealed record AgentPassportForgetStatusV1(
    string Schema,
    bool Forgotten,
    AgentPassportForgetCode Code,
    string Message,
    IReadOnlyList<AgentPassportDropV1> ForgottenAgents,
    AgentPassportIndexV1 Passports) : IAgentPassportResponse
{
    public const string Contract = "vibesnake-agent-passport-forget-status-v1";
}

/// <summary>
/// One read-only listing of public agent records, optionally narrowed to a
/// single agent. A caller used to be able to see the store only by writing to it.
/// </summary>
public sealed record AgentPassportListingV1(
    string Schema,
    string? AgentIdFilter,
    int MatchedCount,
    AgentPassportIndexV1 Passports) : IAgentPassportResponse
{
    public const string Contract = "vibesnake-agent-passport-listing-v1";
}

public interface IAgentPassportResponse
{
    AgentPassportIndexV1 Passports { get; }
}

/// <summary>
/// The passport store as it stands. Two sizes are published because a read
/// never writes: bytes_used is the file, bytes_projected is the next write.
/// </summary>
public sealed record AgentPassportIndexV1(
    string Schema,
    int SchemaVersion,
    int StoredSchemaVersion,
    int RecordCount,
    int Capacity,
    int BytesUsed,
    int BytesProjected,
    int MaximumBytes,
    int RemainingRecords,
    int RemainingBytes,
    bool RecoveredFromCorruption,
    IReadOnlyList<AgentPassportIndexEntryV1> Entries)
{
    public const string Contract = "vibesnake-agent-passport-index-v1";

    internal static AgentPassportIndexV1 Create(
        AgentPassportDocumentV1 document,
        int bytesUsed,
        int bytesProjected,
        int storedSchemaVersion,
        bool recoveredFromCorruption,
        IReadOnlyList<AgentPassportRecordV1>? listed = null)
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < document.Records.Count; index++)
        {
            positions[document.Records[index].AgentId] = index;
        }

        return new AgentPassportIndexV1(
            Contract,
            document.SchemaVersion,
            storedSchemaVersion,
            document.Records.Count,
            document.Capacity,
            bytesUsed,
            bytesProjected,
            AgentPassportDocumentV1.MaximumBytes,
            Math.Max(0, document.Capacity - document.Records.Count),
            Math.Max(0, AgentPassportDocumentV1.MaximumBytes - bytesProjected),
            recoveredFromCorruption,
            (listed ?? document.Records)
                .Select(record => AgentPassportIndexEntryV1.FromRecord(
                    record,
                    positions.TryGetValue(record.AgentId, out var position) ? position : -1))
                .ToArray());
    }
}

/// <summary>
/// One listed public record. Every field is a fact the receipts earned. The
/// caller-declared display name is absent on purpose: a name is a claim.
/// Position is computed at read time so a filtered listing still says where
/// the record sits and which one eviction reaches next.
/// </summary>
public sealed record AgentPassportIndexEntryV1(
    string Schema,
    int Position,
    string AgentId,
    IReadOnlyList<string> PolicyVersions,
    IReadOnlyList<string> DivisionIds,
    int Exhibitions,
    int BestScore,
    IReadOnlyList<AgentPassportStyleRecordV1> Styles,
    IReadOnlyList<AgentPassportLessonRecordV1> Lessons,
    IReadOnlyList<AgentPassportRivalRecordV1> Rivals,
    IReadOnlyList<AgentPassportMilestoneV1> Milestones,
    string FirstReceiptHash,
    string LatestReceiptHash)
{
    public const string Contract = "vibesnake-agent-passport-index-entry-v1";

    internal static AgentPassportIndexEntryV1 FromRecord(
        AgentPassportRecordV1 record,
        int position) =>
        new(
            Contract,
            position,
            record.AgentId,
            record.PolicyVersions,
            record.DivisionIds,
            record.Exhibitions,
            record.BestScore,
            record.Styles,
            record.Lessons,
            record.Rivals,
            record.Milestones,
            record.FirstReceiptHash,
            record.LatestReceiptHash);
}
