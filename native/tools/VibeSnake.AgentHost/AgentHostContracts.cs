using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.AgentHost;

public sealed record StartAgentMatchV2(
    string Schema,
    string MatchHandle,
    string RetentionPolicy,
    AgentObservationV2 Observation,
    AgentViewerConnectionV1? Viewer)
{
    public const string Contract = "vibesnake-agent-match-start-v2";
}

public sealed record AgentMatchSummaryV2(
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
    AgentPassportV1 Passport,
    int FinalTick,
    RunStatus RunStatus,
    DeathCause DeathCause,
    int Score,
    string FinalStateHash,
    string ReplayPayloadHash,
    ReplayVerificationCode ReplayVerificationCode,
    AgentEpisodeMetricsV1 EpisodeMetrics,
    AgentStyleProgressV1? StyleContract,
    AgentLessonOutcomeV1? LessonOutcome,
    AgentRivalResultV1? Rival)
{
    public const string Contract = "vibesnake-agent-match-summary-v2";

    internal static AgentMatchSummaryV2 FromResult(AgentMatchResult result) =>
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
            result.StyleContract,
            result.LessonOutcome,
            result.Rival);
}

public sealed record AgentMatchResultStatusV2(
    string Schema,
    string MatchHandle,
    bool IsAvailable,
    AgentMatchSummaryV2? Result)
{
    public const string Contract = "vibesnake-agent-match-result-status-v2";
}

public sealed record AgentActionResponseV2(
    string Schema,
    bool Accepted,
    bool RulesAdvanced,
    AgentActionRejection Rejection,
    AgentLessonProgressDeltaV1? LessonDelta,
    AgentObservationV2 Observation,
    AgentMatchSummaryV2? MatchResult)
{
    public const string Contract = "vibesnake-agent-action-response-v2";

    internal static AgentActionResponseV2 FromResponse(AgentActionResponse response) =>
        new(
            Contract,
            response.Accepted,
            response.RulesAdvanced,
            response.Rejection,
            response.LessonDelta,
            response.Observation,
            response.MatchResult is null
                ? null
                : AgentMatchSummaryV2.FromResult(response.MatchResult));
}

public sealed record AgentBurstResponseV2(
    string Schema,
    bool Accepted,
    bool RulesAdvanced,
    AgentActionRejection Rejection,
    int StepsAdvanced,
    AgentBurstStopReason? StopReason,
    RunEventKind? StopEvent,
    AgentLessonProgressDeltaV1? LessonDelta,
    AgentObservationV2 Observation,
    AgentMatchSummaryV2? MatchResult)
{
    public const string Contract = "vibesnake-agent-burst-response-v2";

    internal static AgentBurstResponseV2 FromResponse(AgentBurstResponse response) =>
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
                : AgentMatchSummaryV2.FromResult(response.MatchResult));
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
