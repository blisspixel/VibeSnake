namespace VibeSnake.Rules;

public sealed record RunEndSummary(
    string Outcome,
    string Cause,
    string RecoveryHint,
    int Score,
    int PersonalBest,
    bool IsNewPersonalBest,
    int Length,
    int SurvivalSteps,
    int FoodEaten,
    int PeakCombo,
    IReadOnlyList<string> NewlyUnlockedIds)
{
    public static RunEndSummary Create(
        SnakeRun run,
        int personalBest,
        bool isNewPersonalBest,
        IEnumerable<string>? newlyUnlockedIds = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status is not (RunStatus.Dead or RunStatus.Won))
        {
            throw new ArgumentException("Run-end summary requires a terminal run.", nameof(run));
        }

        if (personalBest < run.Score || personalBest > SnakeRun.MaximumScore)
        {
            throw new ArgumentOutOfRangeException(nameof(personalBest));
        }

        var unlocks = (newlyUnlockedIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var (outcome, cause, recovery) = Describe(run.Status, run.DeathCause);
        return new RunEndSummary(
            outcome,
            cause,
            recovery,
            run.Score,
            personalBest,
            isNewPersonalBest,
            run.Body.Count,
            run.Tick,
            run.SessionFoodEaten,
            run.SessionMaxCombo,
            unlocks);
    }

    private static (string Outcome, string Cause, string Recovery) Describe(
        RunStatus status,
        DeathCause cause) => (status, cause) switch
        {
            (RunStatus.Won, DeathCause.None) => (
                "GRID COMPLETE",
                "EVERY FREE CELL WAS CLAIMED",
                "Start again to improve the same fair-score category."),
            (RunStatus.Dead, DeathCause.SelfCollision) => (
                "RUN ENDED",
                "SELF COLLISION",
                "Shield, Phase Shift, or Last Stand can prevent a body collision."),
            (RunStatus.Dead, DeathCause.Starvation) => (
                "RUN ENDED",
                "STARVATION",
                "Eat before hunger reaches zero; Last Stand can recover starvation."),
            _ => throw new InvalidOperationException("Terminal run has an unknown outcome."),
        };
}

/// <summary>
/// Rejects the exact input sequence associated with terminal resolution. A
/// later deliberate input sequence may restart.
/// </summary>
public sealed class RestartIntentGate
{
    private long _terminalInputSequence = -1;

    public void NoteTerminal(long inputSequence)
    {
        if (inputSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputSequence));
        }

        _terminalInputSequence = inputSequence;
    }

    public bool CanRestart(long inputSequence)
    {
        if (inputSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputSequence));
        }

        return _terminalInputSequence >= 0 && inputSequence > _terminalInputSequence;
    }

    public void Reset() => _terminalInputSequence = -1;
}
