using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

public enum AgentSeedVisibility : byte
{
    Open = 0,
    Blind = 1,
}

public enum AgentAction : byte
{
    Continue = 0,
    Up = 1,
    Right = 2,
    Down = 3,
    Left = 4,
}

public enum AgentPublicIntent : byte
{
    Undeclared = 0,
    SeekFood = 1,
    SeekPower = 2,
    PreserveSpace = 3,
    TakeRisk = 4,
    Recover = 5,
}

public enum AgentMatchLifecycle : byte
{
    AwaitingAction = 0,
    Completed = 1,
    Aborted = 2,
    FailedClosed = 3,
}

public enum AgentMatchEndReason : byte
{
    None = 0,
    RulesTerminal = 1,
    StepLimit = 2,
    AgentFinished = 3,
    ReplayFailure = 4,
}

public enum AgentActionRejection : byte
{
    None = 0,
    InvalidRequest = 1,
    InvalidAction = 2,
    StaleTick = 3,
    StaleStateHash = 4,
    IllegalDirection = 5,
    IdempotencyConflict = 6,
    MatchNotAwaitingAction = 7,
    ReplayFailure = 8,
    WrongActionProfile = 9,
    MutationCapacityExceeded = 10,
}

public enum AgentBurstStopReason : byte
{
    RequestedLimit = 0,
    DecisionEvent = 1,
    MatchStepLimit = 2,
    RulesTerminal = 3,
    ReplayFailure = 4,
    LessonRequirementsReached = 5,
}

public sealed record AgentMatchOptions
{
    public const int DefaultMaximumSteps = 2_000;
    public const int MaximumAllowedSteps = 2_000;
    public const int MaximumMatchIdLength = 128;

    public AgentMatchOptions(
        string matchId,
        string modeId,
        int modeVersion,
        ulong gameplaySeed,
        AgentSeedVisibility seedVisibility,
        int maximumSteps = DefaultMaximumSteps,
        string? styleContractId = null,
        string? rivalPersonalityId = null,
        AgentPassportV4? passport = null,
        string actionProfile = AgentPassportV4.FourDirectionActionProfile,
        string? lessonId = null)
    {
        ValidateToken(matchId, MaximumMatchIdLength, nameof(matchId));
        if (!RunModeCatalog.IsSupportedIdentity(modeId, modeVersion))
        {
            throw new ArgumentException(
                $"Unsupported run mode identity {modeId}@{modeVersion}.",
                nameof(modeId));
        }

        if (!Enum.IsDefined(seedVisibility))
        {
            throw new ArgumentOutOfRangeException(nameof(seedVisibility));
        }

        if (maximumSteps <= 0 || maximumSteps > MaximumAllowedSteps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSteps),
                $"An agent match must contain between 1 and {MaximumAllowedSteps} steps.");
        }

        if (!AgentPassportV4.IsSupportedActionProfile(actionProfile))
        {
            throw new ArgumentException(
                "The action profile is unsupported.",
                nameof(actionProfile));
        }

        if (passport is not null
            && !string.Equals(passport.ActionProfile, actionProfile, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Agent Passport action profile must match the selected match action profile.",
                nameof(passport));
        }

        if (styleContractId is not null)
        {
            var style = AgentStyleContractCatalog.Get(styleContractId);
            if (!style.SupportedModeIds.Contains(modeId, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Style contract {styleContractId} does not support mode {modeId}.",
                    nameof(styleContractId));
            }
        }

        if (rivalPersonalityId is not null)
        {
            _ = AiPersonalityCatalog.GetBuiltIn(rivalPersonalityId);
        }

        if (lessonId is not null)
        {
            var lesson = AgentSignalSchoolCatalog.Get(lessonId);
            if (seedVisibility != AgentSeedVisibility.Open
                || modeId != lesson.ModeId
                || gameplaySeed != lesson.PracticeSeed
                || maximumSteps != lesson.MaximumSteps
                || styleContractId is not null
                || rivalPersonalityId is not null)
            {
                throw new ArgumentException(
                    "Signal School lessons require their canonical open seed, mode, step cap, and no style or rival.",
                    nameof(lessonId));
            }
        }

        MatchId = matchId;
        ModeId = modeId;
        ModeVersion = modeVersion;
        GameplaySeed = gameplaySeed;
        SeedVisibility = seedVisibility;
        MaximumSteps = maximumSteps;
        StyleContractId = styleContractId;
        RivalPersonalityId = rivalPersonalityId;
        ActionProfile = actionProfile;
        Passport = passport ?? AgentPassportV4.CreateAnonymous(actionProfile);
        LessonId = lessonId;
    }

    public string MatchId { get; }

    public string ModeId { get; }

    public int ModeVersion { get; }

    public ulong GameplaySeed { get; }

    public AgentSeedVisibility SeedVisibility { get; }

    public int MaximumSteps { get; }

    public string? StyleContractId { get; }

    public string? RivalPersonalityId { get; }

    public string ActionProfile { get; }

    public AgentPassportV4 Passport { get; }

    public string? LessonId { get; }

    internal RunConfig CreateRunConfig() =>
        RunModeCatalog.CreateConfig(RunModeCatalog.Get(ModeId, ModeVersion));

    internal static void ValidateToken(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{parameterName} cannot exceed {maximumLength} characters.",
                parameterName);
        }

        if (value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                $"{parameterName} may contain only ASCII letters, digits, period, hyphen, and underscore.",
                parameterName);
        }
    }
}

public sealed record AgentActionRequest
{
    public const int MaximumIdempotencyKeyLength = 128;

    public AgentActionRequest(
        string idempotencyKey,
        int expectedTick,
        string expectedStateHash,
        AgentAction action,
        AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared)
    {
        AgentMatchOptions.ValidateToken(
            idempotencyKey,
            MaximumIdempotencyKeyLength,
            nameof(idempotencyKey));
        ArgumentOutOfRangeException.ThrowIfNegative(expectedTick);

        ArgumentException.ThrowIfNullOrWhiteSpace(expectedStateHash);
        if (expectedStateHash.Length > 64)
        {
            throw new ArgumentException(
                "expectedStateHash cannot exceed 64 characters.",
                nameof(expectedStateHash));
        }

        if (!Enum.IsDefined(declaredIntent))
        {
            throw new ArgumentOutOfRangeException(nameof(declaredIntent));
        }

        IdempotencyKey = idempotencyKey;
        ExpectedTick = expectedTick;
        ExpectedStateHash = expectedStateHash;
        Action = action;
        DeclaredIntent = declaredIntent;
    }

    public string IdempotencyKey { get; }

    public int ExpectedTick { get; }

    public string ExpectedStateHash { get; }

    public AgentAction Action { get; }

    public AgentPublicIntent DeclaredIntent { get; }
}

public sealed record AgentBurstRequest
{
    public const int MaximumBurstSteps = 16;

    public AgentBurstRequest(
        string idempotencyKey,
        int expectedTick,
        string expectedStateHash,
        AgentAction initialAction,
        int maximumSteps,
        AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared)
    {
        AgentMatchOptions.ValidateToken(
            idempotencyKey,
            AgentActionRequest.MaximumIdempotencyKeyLength,
            nameof(idempotencyKey));
        ArgumentOutOfRangeException.ThrowIfNegative(expectedTick);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedStateHash);
        if (expectedStateHash.Length > 64)
        {
            throw new ArgumentException(
                "expectedStateHash cannot exceed 64 characters.",
                nameof(expectedStateHash));
        }

        if (maximumSteps is < 1 or > MaximumBurstSteps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSteps),
                $"A symbolic burst must contain between 1 and {MaximumBurstSteps} steps.");
        }

        if (!Enum.IsDefined(declaredIntent))
        {
            throw new ArgumentOutOfRangeException(nameof(declaredIntent));
        }

        IdempotencyKey = idempotencyKey;
        ExpectedTick = expectedTick;
        ExpectedStateHash = expectedStateHash;
        InitialAction = initialAction;
        MaximumSteps = maximumSteps;
        DeclaredIntent = declaredIntent;
    }

    public string IdempotencyKey { get; }

    public int ExpectedTick { get; }

    public string ExpectedStateHash { get; }

    public AgentAction InitialAction { get; }

    public int MaximumSteps { get; }

    public AgentPublicIntent DeclaredIntent { get; }
}

public readonly record struct AgentPointV1(int X, int Y);

public sealed record AgentPowerPickupV1(
    PowerKind Kind,
    AgentPointV1 Position,
    int VisibilityTicksRemaining);

public sealed record AgentPublicEventV1(
    RunEventKind Kind,
    AgentPointV1? Position,
    Direction? NewDirection,
    int? Value,
    DeathCause? Cause,
    PowerKind? Power);

public sealed record AgentPreviousActionV1(
    AgentAction Action,
    bool Accepted,
    AgentActionRejection Rejection,
    bool RulesAdvanced,
    AgentPublicIntent DeclaredIntent = AgentPublicIntent.Undeclared);

public sealed record AgentRivalObservationV1(
    string PersonalityId,
    string DisplayName,
    int Tick,
    RunStatus Status,
    DeathCause DeathCause,
    int Score);

public sealed record AgentRivalResultV1(
    string PersonalityId,
    string DisplayName,
    int FinalTick,
    RunStatus RunStatus,
    DeathCause DeathCause,
    int Score,
    string FinalStateHash,
    string ReplayPayloadHash,
    ReplayVerificationCode ReplayVerificationCode,
    AgentEpisodeMetricsV1 EpisodeMetrics);

public sealed record AgentObservationV5(
    string Schema,
    string MatchId,
    string RulesetId,
    int RulesVersion,
    string ModeId,
    int ModeVersion,
    string ConfigHashAlgorithm,
    string ConfigHash,
    AgentSeedVisibility SeedVisibility,
    ulong? GameplaySeed,
    AgentPassportV4 Passport,
    int Tick,
    int MaximumSteps,
    int StepsRemaining,
    string StateHash,
    int BoardWidth,
    int BoardHeight,
    bool WrapsAtEdges,
    RunStatus Status,
    DeathCause DeathCause,
    Direction Direction,
    AgentPointV1 Head,
    IReadOnlyList<AgentPointV1> Body,
    IReadOnlyList<Direction> PendingDirections,
    AgentPointV1? Food,
    int Score,
    int ComboCount,
    double ComboMultiplier,
    int TicksSinceLastFood,
    int HungerTicksRemaining,
    int HungerMaximumTicks,
    int HungerWarningTicks,
    AgentPowerPickupV1? PowerPickup,
    int PowerSpawnTicksElapsed,
    int ShieldTicksRemaining,
    int PhaseShiftTicksRemaining,
    bool LastStandHeld,
    int LastStandRecoveryTicksRemaining,
    int SlowMoTicksRemaining,
    int BoostTicksRemaining,
    int MagnetTicksRemaining,
    int GluttonyTicksRemaining,
    AgentPointV1? BaitPosition,
    IReadOnlyList<AgentPointV1> DetachedObstacles,
    int DetachedObstacleTicksRemaining,
    AdaptiveDifficultyState AdaptiveDifficultyState,
    string AdaptivePolicyId,
    bool AdaptationEnabled,
    IReadOnlyList<AgentPublicEventV1> PreviousEvents,
    AgentPreviousActionV1? PreviousAction,
    AgentMatchLifecycle Lifecycle,
    bool IsActionAwaited,
    AgentEpisodeMetricsV1 EpisodeMetrics,
    AgentStyleProgressV2? StyleContract,
    AgentLessonProgressV2? LessonProgress,
    AgentRivalObservationV1? Rival)
{
    public const string Contract = "vibesnake-agent-observation-v5";
}

public sealed record AgentActionResponse(
    bool Accepted,
    bool RulesAdvanced,
    AgentActionRejection Rejection,
    AgentLessonProgressDeltaV2? LessonDelta,
    AgentObservationV5 Observation,
    AgentMatchResultV5? MatchResult);

public sealed record AgentBurstResponse(
    bool Accepted,
    bool RulesAdvanced,
    AgentActionRejection Rejection,
    int StepsAdvanced,
    AgentBurstStopReason? StopReason,
    RunEventKind? StopEvent,
    AgentLessonProgressDeltaV2? LessonDelta,
    AgentObservationV5 Observation,
    AgentMatchResultV5? MatchResult);

public sealed record AgentMatchResultV5(
    string Schema,
    string MatchId,
    AgentMatchLifecycle Lifecycle,
    AgentMatchEndReason EndReason,
    string RulesetId,
    int RulesVersion,
    string ModeId,
    int ModeVersion,
    string ConfigHashAlgorithm,
    string ConfigHash,
    AgentSeedVisibility SeedVisibility,
    ulong GameplaySeed,
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
    AgentRivalResultV1? Rival,
    RunReplay VerifiedReplay,
    RunReplay? VerifiedRivalReplay)
{
    public const string Contract = "vibesnake-agent-match-result-v5";
}
