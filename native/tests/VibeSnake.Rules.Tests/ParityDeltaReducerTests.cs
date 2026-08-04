namespace VibeSnake.Rules.Tests;

public sealed class ParityDeltaReducerTests
{
    [Fact]
    public void Binary_search_keeps_shortest_failing_prefix()
    {
        // Fails only when at least five steps are present.
        var steps = Enumerable.Range(1, 12).Select(value => $"S{value}").ToArray();
        var minimized = ParityDeltaReducer.MinimizePrefix(
            steps,
            stillFails: prefix => prefix.Count >= 5);

        Assert.Equal(5, minimized.Count);
        Assert.Equal(["S1", "S2", "S3", "S4", "S5"], minimized);
        Assert.True(minimized.Count >= 5);
    }

    [Fact]
    public void Interior_empty_command_batches_are_dropped_when_safe()
    {
        var batches = new[] { "U", "", "R", "", "D" };
        // Failure requires seeing U, R, and D in order, ignoring empties.
        bool StillFails(IReadOnlyList<string> prefix)
        {
            var compact = prefix.Where(batch => batch.Length > 0).ToArray();
            return compact is ["U", "R", "D"] or { Length: > 3 };
        }

        var fullFails = StillFails(batches);
        Assert.True(fullFails);

        var minimized = ParityDeltaReducer.MinimizeCommandBatches(batches, StillFails);
        Assert.Equal(["U", "R", "D"], minimized);
        Assert.True(StillFails(minimized));
    }

    [Fact]
    public void Minimized_reproducer_is_reexecuted_from_clean_state()
    {
        // Synthetic engine: diverge when compact history begins U,R,U and later
        // includes D. The full sequence carries empty batches and a trailing tail.
        var full = new[] { "U", "", "R", "U", "", "D", "R", "U" };

        bool Diverges(IReadOnlyList<string> commands)
        {
            var compact = commands.Where(batch => batch.Length > 0).ToArray();
            if (compact.Length < 4)
            {
                return false;
            }

            return compact[0] == "U"
                && compact[1] == "R"
                && compact[2] == "U"
                && compact.Contains("D", StringComparer.Ordinal);
        }

        Assert.True(Diverges(full));
        var minimized = ParityDeltaReducer.MinimizeCommandBatches(full, Diverges);

        // Clean re-execution: re-run the oracle only on the minimized prefix.
        Assert.Equal(["U", "R", "U", "D"], minimized);
        Assert.True(Diverges(minimized));
        Assert.False(Diverges(minimized.Take(3).ToArray()));
    }

    [Fact]
    public void Full_prefix_that_does_not_fail_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ParityDeltaReducer.MinimizePrefix(
                ["A", "B"],
                stillFails: _ => false));
    }

    [Fact]
    public void Movement_style_step_prefix_reduces_to_first_failure()
    {
        var steps = Enumerable.Range(0, 32)
            .Select(index => new MovementStep(index, Commands: index == 17 ? "X" : "U"))
            .ToArray();

        bool StillFails(IReadOnlyList<MovementStep> prefix) =>
            prefix.Any(step => step.Commands.Contains('X', StringComparison.Ordinal));

        var minimized = ParityDeltaReducer.MinimizePrefix(steps, StillFails);
        Assert.Equal(18, minimized.Count);
        Assert.Equal("X", minimized[^1].Commands);
        Assert.True(StillFails(minimized));
        Assert.False(StillFails(minimized.Take(17).ToArray()));
    }

    private sealed record MovementStep(int Index, string Commands);
}
