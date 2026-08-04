namespace VibeSnake.Rules.Tests;

/// <summary>
/// Minimizes a failing multi-step command prefix while preserving the failure.
/// First failing length is found by binary search; empty interior steps are then
/// dropped when the oracle still fails.
/// </summary>
internal static class ParityDeltaReducer
{
    public static IReadOnlyList<TStep> MinimizePrefix<TStep>(
        IReadOnlyList<TStep> steps,
        Func<IReadOnlyList<TStep>, bool> stillFails)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(stillFails);
        if (steps.Count == 0)
        {
            throw new ArgumentException("At least one step is required.", nameof(steps));
        }

        if (!stillFails(steps))
        {
            throw new InvalidOperationException(
                "The full command prefix does not fail, so it cannot be minimized.");
        }

        var low = 1;
        var high = steps.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = steps.Take(mid).ToArray();
            if (stillFails(candidate))
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        var minimized = steps.Take(low).ToList();

        // Drop empty no-op steps from the interior when the oracle still fails.
        var index = 0;
        while (index < minimized.Count - 1)
        {
            var without = new List<TStep>(minimized.Count - 1);
            for (var stepIndex = 0; stepIndex < minimized.Count; stepIndex++)
            {
                if (stepIndex == index)
                {
                    continue;
                }

                without.Add(minimized[stepIndex]);
            }

            if (IsNoOpStep(minimized[index]) && stillFails(without))
            {
                minimized = without;
                continue;
            }

            index++;
        }

        if (!stillFails(minimized))
        {
            throw new InvalidOperationException(
                "Delta reduction produced a prefix that no longer reproduces the failure.");
        }

        return minimized;
    }

    public static IReadOnlyList<string> MinimizeCommandBatches(
        IReadOnlyList<string> commandBatches,
        Func<IReadOnlyList<string>, bool> stillFails)
    {
        ArgumentNullException.ThrowIfNull(commandBatches);
        ArgumentNullException.ThrowIfNull(stillFails);
        return MinimizePrefix(commandBatches, stillFails);
    }

    private static bool IsNoOpStep<TStep>(TStep step)
    {
        if (step is null)
        {
            return true;
        }

        if (step is string text)
        {
            return text.Length == 0;
        }

        return false;
    }
}
