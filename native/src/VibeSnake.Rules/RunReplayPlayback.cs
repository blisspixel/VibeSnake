namespace VibeSnake.Rules;

/// <summary>
/// One deterministic replay step prepared for presentation. Commands are the
/// recorded logical attempts, and the snapshot is the state after the step.
/// </summary>
public sealed record ReplayPlaybackFrame(
    int StepIndex,
    IReadOnlyList<Direction> Commands,
    RunStepResult Result,
    RunSnapshot Snapshot);

/// <summary>
/// Clock-free replay playback. A shell controls pace and pause by deciding when
/// to advance; this type owns deterministic restoration, stepping, and seeking.
/// </summary>
public sealed class RunReplayPlayback
{
    private readonly RunReplay _replay;
    private SnakeRun _run;
    private int _stepIndex;

    public RunReplayPlayback(RunReplay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var verification = replay.Verify();
        if (!verification.IsValid)
        {
            throw new ArgumentException(
                "Replay playback requires a compatible, deterministically verified replay.",
                nameof(replay));
        }

        _replay = replay;
        Verification = verification;
        _run = SnakeRun.RestoreCanonicalState(replay.InitialCanonicalState);
    }

    public ReplayVerificationResult Verification { get; }

    public int StepIndex => _stepIndex;

    public int StepCount => _replay.Steps.Count;

    public bool IsComplete => _stepIndex == StepCount;

    public double Progress => StepCount == 0
        ? 1.0
        : (double)_stepIndex / StepCount;

    public RunSnapshot CurrentSnapshot => _run.GetSnapshot();

    public RunModeDefinition Mode => _run.Mode;

    public RunConfig Configuration => _run.Configuration;

    public string ScoreCategoryId => _run.ScoreCategoryId;

    public bool TryAdvance(out ReplayPlaybackFrame? frame)
    {
        if (IsComplete)
        {
            frame = null;
            return false;
        }

        var replayStep = _replay.Steps[_stepIndex];
        foreach (var command in replayStep.Commands)
        {
            _run.QueueDirection(command);
        }

        var result = _run.Step();
        _stepIndex++;
        frame = new ReplayPlaybackFrame(
            _stepIndex,
            replayStep.Commands,
            result,
            _run.GetSnapshot());
        return true;
    }

    public void Seek(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex > StepCount)
        {
            throw new ArgumentOutOfRangeException(nameof(stepIndex));
        }

        if (stepIndex < _stepIndex)
        {
            Reset();
        }

        while (_stepIndex < stepIndex)
        {
            _ = TryAdvance(out _);
        }
    }

    public void Reset()
    {
        _run = SnakeRun.RestoreCanonicalState(_replay.InitialCanonicalState);
        _stepIndex = 0;
    }
}
