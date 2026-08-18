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
    AgentExhibitionArchiveIndexV2 Archive) : IAgentExhibitionArchiveResponse
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
    AgentExhibitionArchiveIndexV2 Archive) : IAgentExhibitionArchiveResponse
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
    AgentExhibitionArchiveIndexV2 Archive) : IAgentExhibitionArchiveResponse
{
    public const string Contract = "vibesnake-agent-exhibition-listing-v1";
}

/// <summary>
/// Marker for every response that carries the archive index, so one gate can
/// assert that no archive surface answers without publishing its bounds.
/// </summary>
public interface IAgentExhibitionArchiveResponse
{
    AgentExhibitionArchiveIndexV2 Archive { get; }
}

/// <summary>
/// The archive as it stands, with both of its bounds and the exact bytes it
/// occupies. Effective capacity is the lesser of the entry and byte ceilings, so
/// an entry count alone cannot tell a caller how much room is actually left.
/// </summary>
public sealed record AgentExhibitionArchiveIndexV2(
    string Schema,
    int SchemaVersion,
    int EntryCount,
    int Capacity,
    int BytesUsed,
    int MaximumBytes,
    int RemainingEntries,
    int RemainingBytes,
    bool RecoveredFromCorruption,
    bool MigratedFromLegacySchema,
    IReadOnlyList<AgentArchivedExhibitionIndexEntryV2> Entries)
{
    public const string Contract = "vibesnake-agent-exhibition-archive-index-v2";

    internal static AgentExhibitionArchiveIndexV2 Create(
        AgentExhibitionArchiveV2 archive,
        int bytesUsed,
        bool recoveredFromCorruption,
        bool migratedFromLegacySchema,
        Func<string, bool> replayFileExists,
        IReadOnlyList<AgentArchivedExhibitionV2>? listed = null) =>
        new(
            Contract,
            archive.SchemaVersion,
            archive.Entries.Count,
            archive.Capacity,
            bytesUsed,
            AgentExhibitionArchiveV2.MaximumBytes,
            Math.Max(0, archive.Capacity - archive.Entries.Count),
            Math.Max(0, AgentExhibitionArchiveV2.MaximumBytes - bytesUsed),
            recoveredFromCorruption,
            migratedFromLegacySchema,
            (listed ?? archive.Entries)
                .Select(entry => AgentArchivedExhibitionIndexEntryV2.FromEntry(
                    entry,
                    replayFileExists))
                .ToArray());
}

/// <summary>
/// One listed exhibition. Every identity field is a copy of a receipt value, so
/// listing the archive reveals nothing the receipt did not already publish. The
/// two presence flags are the exception: they are observed now rather than
/// copied, because a named replay file can be deleted after it was archived and
/// a caller choosing what to open needs to know that.
/// </summary>
public sealed record AgentArchivedExhibitionIndexEntryV2(
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
    bool AgentReplayPresent,
    string? RivalReplayFileName,
    bool? RivalReplayPresent,
    string? RivalPersonalityId,
    int? RivalScore)
{
    public const string Contract = "vibesnake-agent-archived-exhibition-index-entry-v2";

    internal static AgentArchivedExhibitionIndexEntryV2 FromEntry(
        AgentArchivedExhibitionV2 entry,
        Func<string, bool> replayFileExists) =>
        new(
            Contract,
            entry.ReceiptHash,
            entry.RouteIdentityHash,
            entry.DivisionId,
            entry.ModeId,
            entry.GameplaySeed,
            entry.Score,
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
