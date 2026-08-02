namespace VibeSnake.Rules;

public enum ReplayRecordingState : byte
{
    Recording = 0,
    Failed = 1,
    Finalized = 2,
}

public enum ReplayRecordingFailureCode : byte
{
    None = 0,
    CommandLimitExceeded = 1,
    StepLimitExceeded = 2,
    Diverged = 3,
    PendingCommands = 4,
    TerminalState = 5,
    SizeLimitExceeded = 6,
}

public sealed record ReplayRecordingResult(
    bool IsSuccessful,
    RunReplay? Replay,
    ReplayRecordingFailureCode FailureCode,
    int? FirstDivergentStep,
    string Message);

public sealed class RunReplayRecorder
{
    private readonly string _initialCanonicalState;
    private readonly SnakeRun _mirror;
    private readonly int _checkpointInterval;
    private readonly List<Direction> _pendingCommands = [];
    private readonly List<ReplayStep> _steps = [];
    private readonly List<ReplayCheckpoint> _checkpoints;
    private ReplayRecordingResult? _finalResult;

    public RunReplayRecorder(
        SnakeRun liveRun,
        int checkpointInterval = RunReplay.DefaultCheckpointInterval)
    {
        ArgumentNullException.ThrowIfNull(liveRun);
        if (liveRun.Status != RunStatus.Running)
        {
            throw new ArgumentException(
                "Replay recording must begin from a running state.",
                nameof(liveRun));
        }

        if (checkpointInterval <= 0 || checkpointInterval > RunReplay.MaximumSteps)
        {
            throw new ArgumentOutOfRangeException(nameof(checkpointInterval));
        }

        _initialCanonicalState = liveRun.SerializeCanonicalState();
        _mirror = SnakeRun.RestoreCanonicalState(_initialCanonicalState);
        _checkpointInterval = checkpointInterval;
        _checkpoints = [new ReplayCheckpoint(0, _mirror.ComputeStateHash())];
    }

    public ReplayRecordingState State { get; private set; } =
        ReplayRecordingState.Recording;

    public ReplayRecordingFailureCode FailureCode { get; private set; } =
        ReplayRecordingFailureCode.None;

    public int? FirstDivergentStep { get; private set; }

    public string? FailureMessage { get; private set; }

    public int RecordedStepCount => _steps.Count;

    public int PendingCommandCount => _pendingCommands.Count;

    public bool TryRecordCommand(Direction command)
    {
        if (!Enum.IsDefined(command))
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        if (State != ReplayRecordingState.Recording)
        {
            return false;
        }

        if (_mirror.Status != RunStatus.Running)
        {
            return Fail(
                ReplayRecordingFailureCode.TerminalState,
                "Replay recording cannot accept commands after the run ended.",
                _steps.Count + 1);
        }

        if (_pendingCommands.Count >= ReplayStep.MaximumCommands)
        {
            return Fail(
                ReplayRecordingFailureCode.CommandLimitExceeded,
                $"A single rules step exceeded the {ReplayStep.MaximumCommands}-command replay limit.",
                _steps.Count + 1);
        }

        _pendingCommands.Add(command);
        return true;
    }

    public bool TryCompleteStep(RunStepResult actualResult, SnakeRun liveRun)
    {
        ArgumentNullException.ThrowIfNull(liveRun);
        if (State != ReplayRecordingState.Recording)
        {
            return false;
        }

        var nextStep = _steps.Count + 1;
        if (_steps.Count >= RunReplay.MaximumSteps)
        {
            return Fail(
                ReplayRecordingFailureCode.StepLimitExceeded,
                $"The run exceeded the {RunReplay.MaximumSteps}-step replay limit.",
                nextStep);
        }

        if (_mirror.Status != RunStatus.Running)
        {
            return Fail(
                ReplayRecordingFailureCode.TerminalState,
                "Replay recording received a step after the run ended.",
                nextStep);
        }

        var replayStep = new ReplayStep(nextStep, _pendingCommands);
        foreach (var command in replayStep.Commands)
        {
            _mirror.QueueDirection(command);
        }

        var mirrorResult = _mirror.Step();
        if (mirrorResult != actualResult)
        {
            return Fail(
                ReplayRecordingFailureCode.Diverged,
                $"The live step result diverged from deterministic replay at step {nextStep}.",
                nextStep);
        }

        if (!string.Equals(
            mirrorResult.StateHash,
            liveRun.ComputeStateHash(),
            StringComparison.Ordinal))
        {
            return Fail(
                ReplayRecordingFailureCode.Diverged,
                $"The live state diverged from deterministic replay at step {nextStep}.",
                nextStep);
        }

        _steps.Add(replayStep);
        _pendingCommands.Clear();
        if (
            nextStep % _checkpointInterval == 0
            || mirrorResult.Status != RunStatus.Running)
        {
            _checkpoints.Add(new ReplayCheckpoint(nextStep, mirrorResult.StateHash));
        }

        return true;
    }

    public ReplayRecordingResult Finish(SnakeRun liveRun)
    {
        ArgumentNullException.ThrowIfNull(liveRun);
        if (_finalResult is not null)
        {
            return _finalResult;
        }

        if (State == ReplayRecordingState.Failed)
        {
            return FinalizeFailure();
        }

        if (_pendingCommands.Count != 0)
        {
            Fail(
                ReplayRecordingFailureCode.PendingCommands,
                "Replay recording cannot finish while commands are waiting for a rules step.",
                _steps.Count + 1);
            return FinalizeFailure();
        }

        if (!string.Equals(
            _mirror.SerializeCanonicalState(),
            liveRun.SerializeCanonicalState(),
            StringComparison.Ordinal))
        {
            Fail(
                ReplayRecordingFailureCode.Diverged,
                "The final live state diverged from deterministic replay.",
                _steps.Count);
            return FinalizeFailure();
        }

        if (_checkpoints[^1].StepIndex != _steps.Count)
        {
            _checkpoints.Add(
                new ReplayCheckpoint(_steps.Count, _mirror.ComputeStateHash()));
        }

        var snapshot = _mirror.GetSnapshot();
        var outcome = new ReplayOutcome(
            _steps.Count,
            snapshot.Tick,
            snapshot.Status,
            snapshot.DeathCause,
            snapshot.Score,
            snapshot.StateHash);
        RunReplay replay;
        try
        {
            replay = RunReplay.CreateRecorded(
                _initialCanonicalState,
                _steps,
                _checkpointInterval,
                _checkpoints,
                outcome);
        }
        catch (ArgumentException exception)
        {
            Fail(
                ReplayRecordingFailureCode.SizeLimitExceeded,
                "The finalized replay exceeded its serialization contract: "
                    + exception.Message,
                _steps.Count);
            return FinalizeFailure();
        }

        State = ReplayRecordingState.Finalized;
        _finalResult = new ReplayRecordingResult(
            true,
            replay,
            ReplayRecordingFailureCode.None,
            null,
            "The run replay was recorded and mirror-verified.");
        return _finalResult;
    }

    private bool Fail(
        ReplayRecordingFailureCode code,
        string message,
        int? firstDivergentStep)
    {
        State = ReplayRecordingState.Failed;
        FailureCode = code;
        FailureMessage = message;
        FirstDivergentStep = firstDivergentStep;
        _pendingCommands.Clear();
        return false;
    }

    private ReplayRecordingResult FinalizeFailure()
    {
        _finalResult = new ReplayRecordingResult(
            false,
            null,
            FailureCode,
            FirstDivergentStep,
            FailureMessage ?? "Replay recording failed without a diagnostic.");
        return _finalResult;
    }
}
