using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

public enum AgentExperienceMetric : byte
{
    SurvivalSteps = 0,
    FoodEaten = 1,
    PeakCombo = 2,
    Wraps = 3,
    NearMisses = 4,
    PowersCollected = 5,
    PowersActivated = 6,
    Recoveries = 7,
    DirectionChanges = 8,
}

public sealed record AgentEpisodeMetricsV1(
    string Schema,
    int SurvivalSteps,
    int FoodEaten,
    int PeakCombo,
    int Wraps,
    int NearMisses,
    int PowersCollected,
    int PowersActivated,
    int Recoveries,
    int StarvationWarnings,
    int DirectionChanges)
{
    public const string Contract = "vibesnake-agent-episode-metrics-v1";

    public int ValueFor(AgentExperienceMetric metric) => metric switch
    {
        AgentExperienceMetric.SurvivalSteps => SurvivalSteps,
        AgentExperienceMetric.FoodEaten => FoodEaten,
        AgentExperienceMetric.PeakCombo => PeakCombo,
        AgentExperienceMetric.Wraps => Wraps,
        AgentExperienceMetric.NearMisses => NearMisses,
        AgentExperienceMetric.PowersCollected => PowersCollected,
        AgentExperienceMetric.PowersActivated => PowersActivated,
        AgentExperienceMetric.Recoveries => Recoveries,
        AgentExperienceMetric.DirectionChanges => DirectionChanges,
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };
}

public sealed record AgentStyleContractDefinition(
    string Id,
    string DisplayName,
    string Description,
    AgentExperienceMetric Metric,
    int Target,
    IReadOnlyList<string> SupportedModeIds);

public sealed record AgentStyleProgressV1(
    string ContractId,
    string DisplayName,
    AgentExperienceMetric Metric,
    int Current,
    int Target,
    bool Completed);

public static class AgentStyleContractCatalog
{
    public const string StillwaterId = "stillwater";
    public const string CrownchaserId = "crownchaser";
    public const string EdgeProphetId = "edge-prophet";
    public const string MutagenistId = "mutagenist";
    public const string RedlineId = "redline";

    private static readonly IReadOnlyList<string> BothModes =
        Array.AsReadOnly(new[] { RunModeCatalog.ClassicId, RunModeCatalog.VibeId });
    private static readonly IReadOnlyList<string> VibeOnly =
        Array.AsReadOnly(new[] { RunModeCatalog.VibeId });

    public static IReadOnlyList<AgentStyleContractDefinition> All { get; } =
        Array.AsReadOnly(new[]
        {
            new AgentStyleContractDefinition(
                StillwaterId,
                "Stillwater",
                "Survive calmly while preserving future routes.",
                AgentExperienceMetric.SurvivalSteps,
                200,
                BothModes),
            new AgentStyleContractDefinition(
                CrownchaserId,
                "Crownchaser",
                "Build and preserve a meaningful combo chain.",
                AgentExperienceMetric.PeakCombo,
                4,
                VibeOnly),
            new AgentStyleContractDefinition(
                EdgeProphetId,
                "Edge Prophet",
                "Turn controlled danger into visible near misses.",
                AgentExperienceMetric.NearMisses,
                3,
                VibeOnly),
            new AgentStyleContractDefinition(
                MutagenistId,
                "Mutagenist",
                "Route through the power system and activate its tools.",
                AgentExperienceMetric.PowersActivated,
                2,
                VibeOnly),
            new AgentStyleContractDefinition(
                RedlineId,
                "Redline",
                "Convert direct food routes into sustained forward pressure.",
                AgentExperienceMetric.FoodEaten,
                6,
                BothModes),
        });

    public static AgentStyleContractDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return All.SingleOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown agent style contract {id}.", nameof(id));
    }

    public static AgentStyleProgressV1 Evaluate(
        string id,
        string modeId,
        AgentEpisodeMetricsV1 metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var definition = Get(id);
        if (!definition.SupportedModeIds.Contains(modeId, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Style contract {id} does not support mode {modeId}.",
                nameof(modeId));
        }

        var current = metrics.ValueFor(definition.Metric);
        return new AgentStyleProgressV1(
            definition.Id,
            definition.DisplayName,
            definition.Metric,
            current,
            definition.Target,
            current >= definition.Target);
    }
}

public sealed record AgentSignalLessonDefinition(
    string Id,
    string Title,
    string Instruction,
    string ModeId,
    ulong PracticeSeed,
    int MaximumSteps,
    AgentExperienceMetric Metric,
    int Target,
    string EvaluationPolicyId);

public sealed record AgentLessonProgressV1(
    string Schema,
    string LessonId,
    string Title,
    string Instruction,
    string EvaluationPolicyId,
    AgentExperienceMetric Metric,
    int Current,
    int Target,
    int Remaining,
    bool TargetReached)
{
    public const string Contract = "vibesnake-agent-lesson-progress-v1";
}

public sealed record AgentLessonProgressDeltaV1(
    string Schema,
    string LessonId,
    AgentExperienceMetric Metric,
    int Previous,
    int Current,
    int Delta,
    int Target,
    bool TargetReachedThisMutation)
{
    public const string Contract = "vibesnake-agent-lesson-progress-delta-v1";
}

public sealed record AgentLessonOutcomeV1(
    string Schema,
    string LessonId,
    string EvaluationPolicyId,
    AgentExperienceMetric Metric,
    int FinalValue,
    int Target,
    int Shortfall,
    bool TargetReached,
    string ReplayPayloadHash)
{
    public const string Contract = "vibesnake-agent-lesson-outcome-v1";
}

public static class AgentSignalSchoolCatalog
{
    public const string PrimaryMetricEvaluationPolicy = "primary-public-metric-v1";

    public static IReadOnlyList<AgentSignalLessonDefinition> All { get; } =
        Array.AsReadOnly(new[]
        {
            Lesson(
                "first-turn",
                "First Signal",
                "Reach one accepted direction change.",
                RunModeCatalog.ClassicId,
                7UL,
                16,
                AgentExperienceMetric.DirectionChanges,
                1),
            Lesson(
                "wrap-line",
                "Open Circuit",
                "Produce one typed board-wrap event.",
                RunModeCatalog.ClassicId,
                65_535UL,
                160,
                AgentExperienceMetric.Wraps,
                1),
            Lesson(
                "hunger-route",
                "Feed the Signal",
                "Collect one food under Vibe rules.",
                RunModeCatalog.VibeId,
                4_294_967_291UL,
                180,
                AgentExperienceMetric.FoodEaten,
                1),
            Lesson(
                "power-route",
                "Tune the Current",
                "Activate one visible power.",
                RunModeCatalog.VibeId,
                32_452_843UL,
                320,
                AgentExperienceMetric.PowersActivated,
                1),
            Lesson(
                "combo-route",
                "Hold the Chorus",
                "Reach a peak combo of three.",
                RunModeCatalog.VibeId,
                49_979_687UL,
                480,
                AgentExperienceMetric.PeakCombo,
                3),
            Lesson(
                "recover-route",
                "Return from Static",
                "Produce one typed collision-prevented recovery.",
                RunModeCatalog.VibeId,
                0UL,
                600,
                AgentExperienceMetric.Recoveries,
                1),
        });

    public static AgentSignalLessonDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return All.SingleOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown Signal School lesson {id}.", nameof(id));
    }

    public static bool IsCompleted(string id, AgentEpisodeMetricsV1 metrics)
    {
        return Evaluate(id, metrics).TargetReached;
    }

    public static AgentLessonProgressV1 Evaluate(
        string id,
        AgentEpisodeMetricsV1 metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var lesson = Get(id);
        var current = metrics.ValueFor(lesson.Metric);
        return new AgentLessonProgressV1(
            AgentLessonProgressV1.Contract,
            lesson.Id,
            lesson.Title,
            lesson.Instruction,
            lesson.EvaluationPolicyId,
            lesson.Metric,
            current,
            lesson.Target,
            Math.Max(0, lesson.Target - current),
            current >= lesson.Target);
    }

    public static AgentLessonProgressDeltaV1 Delta(
        AgentLessonProgressV1 previous,
        AgentLessonProgressV1 current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous.LessonId != current.LessonId
            || previous.Metric != current.Metric
            || previous.Target != current.Target)
        {
            throw new ArgumentException("Lesson progress values must describe the same target.");
        }

        return new AgentLessonProgressDeltaV1(
            AgentLessonProgressDeltaV1.Contract,
            current.LessonId,
            current.Metric,
            previous.Current,
            current.Current,
            current.Current - previous.Current,
            current.Target,
            !previous.TargetReached && current.TargetReached);
    }

    private static AgentSignalLessonDefinition Lesson(
        string id,
        string title,
        string instruction,
        string modeId,
        ulong practiceSeed,
        int maximumSteps,
        AgentExperienceMetric metric,
        int target) =>
        new(
            id,
            title,
            instruction,
            modeId,
            practiceSeed,
            maximumSteps,
            metric,
            target,
            PrimaryMetricEvaluationPolicy);
}

internal sealed class AgentEpisodeMetricsTracker
{
    private int _foodEaten;
    private int _peakCombo;
    private int _wraps;
    private int _nearMisses;
    private int _powersCollected;
    private int _powersActivated;
    private int _recoveries;
    private int _starvationWarnings;
    private int _directionChanges;

    public void Record(RunStepResult result, RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _peakCombo = Math.Max(_peakCombo, snapshot.ComboCount);
        foreach (var item in result.OrderedEvents)
        {
            switch (item.Kind)
            {
                case RunEventKind.AteFood:
                    _foodEaten++;
                    break;
                case RunEventKind.Wrapped:
                    _wraps++;
                    break;
                case RunEventKind.NearMiss:
                    _nearMisses++;
                    break;
                case RunEventKind.PowerCollected:
                    _powersCollected++;
                    break;
                case RunEventKind.PowerActivated:
                    _powersActivated++;
                    break;
                case RunEventKind.CollisionPrevented:
                    _recoveries++;
                    break;
                case RunEventKind.StarvationWarning:
                    _starvationWarnings++;
                    break;
                case RunEventKind.DirectionChanged:
                    _directionChanges++;
                    break;
            }
        }
    }

    public AgentEpisodeMetricsV1 Snapshot(int survivalSteps) =>
        new(
            AgentEpisodeMetricsV1.Contract,
            survivalSteps,
            _foodEaten,
            _peakCombo,
            _wraps,
            _nearMisses,
            _powersCollected,
            _powersActivated,
            _recoveries,
            _starvationWarnings,
            _directionChanges);
}

internal static class AgentEpisodeMetricsReplayEvaluator
{
    public static AgentEpisodeMetricsV1 Evaluate(RunReplay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var playback = new RunReplayPlayback(replay);
        var metrics = new AgentEpisodeMetricsTracker();
        while (playback.TryAdvance(out var frame))
        {
            metrics.Record(frame!.Result, frame.Snapshot);
        }

        return metrics.Snapshot(playback.StepIndex);
    }
}
