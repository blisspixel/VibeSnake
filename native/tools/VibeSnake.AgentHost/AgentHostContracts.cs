using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.AgentHost;

public sealed record StartAgentMatchV4(
    string Schema,
    string MatchHandle,
    string RetentionPolicy,
    AgentObservationV4 Observation,
    AgentViewerConnectionV1? Viewer)
{
    public const string Contract = "vibesnake-agent-match-start-v4";
}

public sealed record AgentMatchSummaryV4(
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
    AgentPassportV3 Passport,
    int FinalTick,
    RunStatus RunStatus,
    DeathCause DeathCause,
    int Score,
    string FinalStateHash,
    string ReplayPayloadHash,
    ReplayVerificationCode ReplayVerificationCode,
    AgentEpisodeMetricsV1 EpisodeMetrics,
    AgentStyleOutcomeV2? StyleOutcome,
    AgentLessonOutcomeV1? LessonOutcome,
    AgentRivalResultV1? Rival)
{
    public const string Contract = "vibesnake-agent-match-summary-v4";

    internal static AgentMatchSummaryV4 FromResult(AgentMatchResultV4 result) =>
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

public sealed record AgentMatchResultStatusV4(
    string Schema,
    string MatchHandle,
    bool IsAvailable,
    AgentMatchSummaryV4? Result)
{
    public const string Contract = "vibesnake-agent-match-result-status-v4";
}

public sealed record AgentActionResponseV4(
    string Schema,
    bool Accepted,
    bool RulesAdvanced,
    AgentActionRejection Rejection,
    AgentLessonProgressDeltaV1? LessonDelta,
    AgentObservationV4 Observation,
    AgentMatchSummaryV4? MatchResult)
{
    public const string Contract = "vibesnake-agent-action-response-v4";

    internal static AgentActionResponseV4 FromResponse(AgentActionResponse response) =>
        new(
            Contract,
            response.Accepted,
            response.RulesAdvanced,
            response.Rejection,
            response.LessonDelta,
            response.Observation,
            response.MatchResult is null
                ? null
                : AgentMatchSummaryV4.FromResult(response.MatchResult));
}

public sealed record AgentBurstResponseV4(
    string Schema,
    bool Accepted,
    bool RulesAdvanced,
    AgentActionRejection Rejection,
    int StepsAdvanced,
    AgentBurstStopReason? StopReason,
    RunEventKind? StopEvent,
    AgentLessonProgressDeltaV1? LessonDelta,
    AgentObservationV4 Observation,
    AgentMatchSummaryV4? MatchResult)
{
    public const string Contract = "vibesnake-agent-burst-response-v4";

    internal static AgentBurstResponseV4 FromResponse(AgentBurstResponse response) =>
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
                : AgentMatchSummaryV4.FromResult(response.MatchResult));
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
