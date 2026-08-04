namespace VibeSnake.Rules;

/// <summary>
/// Presentation-side fixed-step scheduler for rules ticks.
/// Slow-Mo and Boost change only the wall-clock interval between steps;
/// each <see cref="SnakeRun.Step"/> remains one grid cell and one rules tick.
/// </summary>
public static class RulesCadenceClock
{
    public const int MaximumStepsPerDrain = 16;

    /// <summary>
    /// Effective wall-clock milliseconds between rules steps for the given cadence scale.
    /// </summary>
    public static int StepIntervalMilliseconds(int numerator, int denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        return checked(RunConfig.RulesTickMilliseconds * numerator / denominator);
    }

    /// <summary>
    /// Accumulates real time and returns how many rules steps to advance.
    /// The interval callback is re-evaluated after each counted step so tempo
    /// powers that expire mid-burst change the next interval immediately.
    /// </summary>
    public static int DrainSteps(
        ref double accumulatedMilliseconds,
        double deltaSeconds,
        Func<int> intervalMilliseconds,
        int maximumSteps = MaximumStepsPerDrain)
    {
        ArgumentNullException.ThrowIfNull(intervalMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSteps);

        if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        if (
            double.IsNaN(accumulatedMilliseconds)
            || double.IsInfinity(accumulatedMilliseconds)
            || accumulatedMilliseconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(accumulatedMilliseconds));
        }

        accumulatedMilliseconds += deltaSeconds * 1000.0;
        var steps = 0;
        while (steps < maximumSteps)
        {
            var interval = intervalMilliseconds();
            if (interval <= 0)
            {
                throw new InvalidOperationException(
                    "Rules step interval must be a positive number of milliseconds.");
            }

            if (accumulatedMilliseconds < interval)
            {
                break;
            }

            accumulatedMilliseconds -= interval;
            steps++;
        }

        return steps;
    }
}
