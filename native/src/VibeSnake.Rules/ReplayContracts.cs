namespace VibeSnake.Rules;

public enum ReplayCompatibilityCode : byte
{
    Compatible = 0,
    InvalidPayload = 1,
    UnsupportedSchema = 2,
    UnsupportedKind = 3,
    UnsupportedRuleset = 4,
    UnsupportedRulesVersion = 5,
    UnsupportedRandomAlgorithm = 6,
    UnsupportedStateHashAlgorithm = 7,
    UnsupportedIntegrityAlgorithm = 8,
    IntegrityMismatch = 9,
    UnsupportedConfigHashAlgorithm = 10,
}

public sealed record ReplayCompatibility(
    ReplayCompatibilityCode Code,
    string Message)
{
    public bool IsCompatible => Code == ReplayCompatibilityCode.Compatible;
}

public sealed record ReplayReadResult(
    ReplayCompatibility Compatibility,
    RunReplay? Replay);

public enum ReplayVerificationCode : byte
{
    Verified = 0,
    InvalidInitialState = 1,
    WorkLimitExceeded = 2,
    InitialCheckpointDiverged = 3,
    ActionsAfterTerminal = 4,
    CheckpointDiverged = 5,
    OutcomeDiverged = 6,
    ConfigIdentityDiverged = 7,
}

public sealed record ReplayVerificationResult(
    ReplayVerificationCode Code,
    int? FirstDivergentStep,
    string Message,
    string? ExpectedStateHash = null,
    string? ActualStateHash = null)
{
    public bool IsValid => Code == ReplayVerificationCode.Verified;
}

public sealed record ReplayStep
{
    public const int MaximumCommands = 64;

    public ReplayStep(int stepIndex, IEnumerable<Direction> commands)
    {
        if (stepIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepIndex));
        }

        ArgumentNullException.ThrowIfNull(commands);
        var commandCopy = new List<Direction>();
        foreach (var command in commands)
        {
            if (commandCopy.Count >= MaximumCommands)
            {
                throw new ArgumentException(
                    $"A replay step cannot contain more than {MaximumCommands} commands.",
                    nameof(commands));
            }

            if (!Enum.IsDefined(command))
            {
                throw new ArgumentOutOfRangeException(nameof(commands));
            }

            commandCopy.Add(command);
        }

        StepIndex = stepIndex;
        Commands = Array.AsReadOnly(commandCopy.ToArray());
    }

    public int StepIndex { get; }

    public IReadOnlyList<Direction> Commands { get; }
}

public sealed record ReplayCheckpoint
{
    public ReplayCheckpoint(int stepIndex, string stateHash)
    {
        if (stepIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepIndex));
        }

        if (!RunReplay.IsStateHash(stateHash))
        {
            throw new ArgumentException(
                "A replay checkpoint must contain a lowercase 64-bit state hash.",
                nameof(stateHash));
        }

        StepIndex = stepIndex;
        StateHash = stateHash;
    }

    public int StepIndex { get; }

    public string StateHash { get; }
}

public sealed record ReplayOutcome
{
    public ReplayOutcome(
        int stepCount,
        int finalTick,
        RunStatus status,
        DeathCause deathCause,
        int score,
        string stateHash)
    {
        if (stepCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepCount));
        }

        if (finalTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(finalTick));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (!Enum.IsDefined(deathCause))
        {
            throw new ArgumentOutOfRangeException(nameof(deathCause));
        }

        if (score < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(score));
        }

        if (!RunReplay.IsStateHash(stateHash))
        {
            throw new ArgumentException(
                "A replay outcome must contain a lowercase 64-bit state hash.",
                nameof(stateHash));
        }

        StepCount = stepCount;
        FinalTick = finalTick;
        Status = status;
        DeathCause = deathCause;
        Score = score;
        StateHash = stateHash;
    }

    public int StepCount { get; }

    public int FinalTick { get; }

    public RunStatus Status { get; }

    public DeathCause DeathCause { get; }

    public int Score { get; }

    public string StateHash { get; }

    public bool IsTerminal => Status != RunStatus.Running;
}
