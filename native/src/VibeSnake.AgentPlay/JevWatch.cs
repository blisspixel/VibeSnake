using System.Globalization;
using System.Text;
using System.Text.Json;
using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

public sealed record JevWatchStep(
    int Tick,
    string Action,
    string ModelChoice,
    string Intent,
    double Probability,
    string Model,
    double? CostUsd);

public sealed record JevWatchReport(
    ulong Seed,
    RunStatus Status,
    int Score,
    int Steps,
    double? CostUsd,
    IReadOnlyList<JevWatchStep> Lines);

public static class JevSnakeAdvisor
{
    public const double DefaultMinimumProbability = 0.34;

    public static string Describe(SnakeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("tick", run.Tick);
            writer.WriteString("status", run.Status.ToString().ToLowerInvariant());
            writer.WriteString("heading", run.Direction.ToString().ToLowerInvariant());
            writer.WriteNumber("width", run.Configuration.Width);
            writer.WriteNumber("height", run.Configuration.Height);
            writer.WriteNumber("length", run.Body.Count);
            writer.WriteNumber("score", run.Score);
            writer.WriteNumber("hungerTicksRemaining", run.HungerTicksRemaining);
            writer.WritePropertyName("head");
            WritePoint(writer, run.Head);
            writer.WritePropertyName("food");
            if (run.Food is { } food)
            {
                WritePoint(writer, food);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WritePropertyName("ahead");
            WriteCell(writer, run, run.Direction);
            writer.WritePropertyName("left");
            WriteCell(writer, run, TurnLeft(run.Direction));
            writer.WritePropertyName("right");
            WriteCell(writer, run, TurnRight(run.Direction));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static IReadOnlyList<string> LegalActions(SnakeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status != RunStatus.Running)
        {
            return [];
        }

        var actions = new List<string>(5) { "continue" };
        foreach (var direction in new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left })
        {
            if (direction != run.Direction && direction != run.Direction.Opposite())
            {
                actions.Add(direction.ToString().ToLowerInvariant());
            }
        }

        return actions;
    }

    public static async Task<JevWatchStep> ChooseAsync(
        JevClient client,
        SnakeRun run,
        double minimumProbability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(run);
        if (!double.IsFinite(minimumProbability) || minimumProbability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumProbability),
                "Minimum probability must be from 0 through 1.");
        }

        var legal = LegalActions(run);
        var answer = await client.AskAsync(Describe(run), legal, cancellationToken).ConfigureAwait(false);
        var action = legal.Contains(answer.Action, StringComparer.Ordinal) && answer.Probability >= minimumProbability
            ? answer.Action
            : "continue";
        if (action != "continue" && Enum.TryParse<Direction>(action, ignoreCase: true, out var direction))
        {
            _ = run.QueueDirection(direction);
        }

        return new JevWatchStep(
            run.Tick,
            action,
            answer.Action,
            answer.Intent,
            answer.Probability,
            answer.Model,
            answer.CostUsd);
    }

    public static string FormatStep(JevWatchStep step) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"tick={step.Tick} action={step.Action} choice={step.ModelChoice} intent={step.Intent} probability={step.Probability:0.000}");

    private static void WritePoint(Utf8JsonWriter writer, GridPoint point)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", point.X);
        writer.WriteNumber("y", point.Y);
        writer.WriteEndObject();
    }

    private static void WriteCell(Utf8JsonWriter writer, SnakeRun run, Direction direction)
    {
        var point = run.Head.Add(direction.Offset()).Wrap(run.Configuration.Width, run.Configuration.Height);
        if (run.Food is { } food && food.Equals(point))
        {
            writer.WriteStringValue("food");
            return;
        }

        writer.WriteStringValue(run.Body.Contains(point) ? "body" : "empty");
    }

    private static Direction TurnLeft(Direction direction) => direction switch
    {
        Direction.Up => Direction.Left,
        Direction.Left => Direction.Down,
        Direction.Down => Direction.Right,
        Direction.Right => Direction.Up,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction."),
    };

    private static Direction TurnRight(Direction direction) => direction switch
    {
        Direction.Up => Direction.Right,
        Direction.Right => Direction.Down,
        Direction.Down => Direction.Left,
        Direction.Left => Direction.Up,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction."),
    };
}

public static class JevWatch
{
    public const int MaximumSteps = 2_000;

    public static Task<JevWatchReport> PlayAsync(
        JevClient client,
        ulong seed,
        int maximumSteps,
        double minimumProbability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        return PlayAsync(
            client,
            SnakeRun.Create(seed),
            seed,
            maximumSteps,
            minimumProbability,
            cancellationToken);
    }

    internal static async Task<JevWatchReport> PlayAsync(
        JevClient client,
        SnakeRun run,
        ulong seed,
        int maximumSteps,
        double minimumProbability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(run);
        if (maximumSteps is < 1 or > MaximumSteps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSteps),
                "A Jev watch runs from 1 through 2000 steps.");
        }

        var lines = new List<JevWatchStep>(maximumSteps);
        double? cost = null;
        while (run.Status == RunStatus.Running && lines.Count < maximumSteps)
        {
            var step = await JevSnakeAdvisor.ChooseAsync(
                client,
                run,
                minimumProbability,
                cancellationToken).ConfigureAwait(false);
            _ = run.Step();
            lines.Add(step);
            if (step.CostUsd is { } stepCost)
            {
                cost = (cost ?? 0) + stepCost;
            }
        }

        return new JevWatchReport(seed, run.Status, run.Score, lines.Count, cost, lines);
    }
}
