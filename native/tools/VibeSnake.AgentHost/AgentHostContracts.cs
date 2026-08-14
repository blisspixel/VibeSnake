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
    AgentStyleOutcomeV2? StyleOutcome,
    AgentLessonOutcomeV2? LessonOutcome,
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
