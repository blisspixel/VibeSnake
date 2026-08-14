using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
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

public enum AgentStyleCriterionComparator : byte
{
    AtLeast = 0,
}

public enum AgentStyleCriterionUnit : byte
{
    Count = 0,
    BasisPoints = 1,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentStyleCriterionDefinitionV2(
    string Id,
    string DisplayName,
    string Description,
    AgentStyleCriterionComparator Comparator,
    AgentStyleCriterionUnit Unit,
    int Target);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentStyleContractDefinitionV2(
    string Id,
    string DisplayName,
    string Description,
    string EvaluationPolicyId,
    IReadOnlyList<AgentStyleCriterionDefinitionV2> Criteria,
    IReadOnlyList<string> SupportedModeIds);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentStyleCriterionProgressV2(
    string CriterionId,
    string DisplayName,
    AgentStyleCriterionComparator Comparator,
    AgentStyleCriterionUnit Unit,
    int Current,
    int Target,
    long? Numerator,
    long? Denominator,
    bool Satisfied);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentStyleProgressV2(
    string Schema,
    string ContractId,
    string DisplayName,
    string EvaluationPolicyId,
    IReadOnlyList<AgentStyleCriterionProgressV2> Criteria,
    int CriteriaSatisfied,
    bool AllCriteriaSatisfied)
{
    public const string Contract = "vibesnake-agent-style-progress-v2";

    public bool Equals(AgentStyleProgressV2? other) =>
        other is not null
        && string.Equals(Schema, other.Schema, StringComparison.Ordinal)
        && string.Equals(ContractId, other.ContractId, StringComparison.Ordinal)
        && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
        && string.Equals(EvaluationPolicyId, other.EvaluationPolicyId, StringComparison.Ordinal)
        && Criteria.SequenceEqual(other.Criteria)
        && CriteriaSatisfied == other.CriteriaSatisfied
        && AllCriteriaSatisfied == other.AllCriteriaSatisfied;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Schema, StringComparer.Ordinal);
        hash.Add(ContractId, StringComparer.Ordinal);
        hash.Add(DisplayName, StringComparer.Ordinal);
        hash.Add(EvaluationPolicyId, StringComparer.Ordinal);
        foreach (var criterion in Criteria)
        {
            hash.Add(criterion);
        }

        hash.Add(CriteriaSatisfied);
        hash.Add(AllCriteriaSatisfied);
        return hash.ToHashCode();
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentStyleOutcomeV2(
    string Schema,
    string ContractId,
    string DisplayName,
    string EvaluationPolicyId,
    IReadOnlyList<AgentStyleCriterionProgressV2> Criteria,
    int CriteriaSatisfied,
    bool AllCriteriaSatisfied,
    string ReplayPayloadHash)
{
    public const string Contract = "vibesnake-agent-style-outcome-v2";

    public bool Equals(AgentStyleOutcomeV2? other) =>
        other is not null
        && string.Equals(Schema, other.Schema, StringComparison.Ordinal)
        && string.Equals(ContractId, other.ContractId, StringComparison.Ordinal)
        && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
        && string.Equals(EvaluationPolicyId, other.EvaluationPolicyId, StringComparison.Ordinal)
        && Criteria.SequenceEqual(other.Criteria)
        && CriteriaSatisfied == other.CriteriaSatisfied
        && AllCriteriaSatisfied == other.AllCriteriaSatisfied
        && string.Equals(ReplayPayloadHash, other.ReplayPayloadHash, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Schema, StringComparer.Ordinal);
        hash.Add(ContractId, StringComparer.Ordinal);
        hash.Add(DisplayName, StringComparer.Ordinal);
        hash.Add(EvaluationPolicyId, StringComparer.Ordinal);
        foreach (var criterion in Criteria)
        {
            hash.Add(criterion);
        }

        hash.Add(CriteriaSatisfied);
        hash.Add(AllCriteriaSatisfied);
        hash.Add(ReplayPayloadHash, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

public static class AgentStyleContractCatalog
{
    public const string EvaluationPolicyId = "replay-composite-core4-v1";
    public const string StillwaterId = "stillwater";
    public const string CrownchaserId = "crownchaser";
    public const string EdgeProphetId = "edge-prophet";
    public const string MutagenistId = "mutagenist";
    public const string RedlineId = "redline";

    private static readonly IReadOnlyList<string> BothModes =
        Array.AsReadOnly(new[] { RunModeCatalog.ClassicId, RunModeCatalog.VibeId });
    private static readonly IReadOnlyList<string> VibeOnly =
        Array.AsReadOnly(new[] { RunModeCatalog.VibeId });

    public static IReadOnlyList<AgentStyleContractDefinitionV2> All { get; } =
        Array.AsReadOnly(new[]
        {
            Style(
                StillwaterId,
                "Stillwater",
                "Sustain a run while preserving multiple structural exits.",
                BothModes),
            Style(
                CrownchaserId,
                "Crownchaser",
                "Reach a four-food combo without abandoning an earlier food chain.",
                VibeOnly),
            Style(
                EdgeProphetId,
                "Edge Prophet",
                "Produce rewarded body-proximity near misses, including one on a wrap.",
                VibeOnly),
            Style(
                MutagenistId,
                "Mutagenist",
                "Activate distinct power kinds and hold overlapping active resources.",
                VibeOnly),
            Style(
                RedlineId,
                "Redline",
                "Collect food while making safe structural progress toward visible targets.",
                BothModes),
        });

    public static AgentStyleContractDefinitionV2 Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return All.SingleOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown agent style contract {id}.", nameof(id));
    }

    internal static void ValidateMode(string id, string modeId)
    {
        var definition = Get(id);
        if (!definition.SupportedModeIds.Contains(modeId, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Style contract {id} does not support mode {modeId}.",
                nameof(modeId));
        }
    }

    public static bool IsValidProgress(AgentStyleProgressV2? progress)
    {
        if (progress is null
            || !string.Equals(
                progress.Schema,
                AgentStyleProgressV2.Contract,
                StringComparison.Ordinal))
        {
            return false;
        }

        return IsValidEvidence(
            progress.ContractId,
            progress.DisplayName,
            progress.EvaluationPolicyId,
            progress.Criteria,
            progress.CriteriaSatisfied,
            progress.AllCriteriaSatisfied);
    }

    public static bool IsValidOutcome(AgentStyleOutcomeV2? outcome)
    {
        if (outcome is null
            || !string.Equals(
                outcome.Schema,
                AgentStyleOutcomeV2.Contract,
                StringComparison.Ordinal)
            || !IsLowerHex(outcome.ReplayPayloadHash, 64))
        {
            return false;
        }

        return IsValidEvidence(
            outcome.ContractId,
            outcome.DisplayName,
            outcome.EvaluationPolicyId,
            outcome.Criteria,
            outcome.CriteriaSatisfied,
            outcome.AllCriteriaSatisfied);
    }

    private static AgentStyleContractDefinitionV2 Style(
        string id,
        string displayName,
        string description,
        IReadOnlyList<string> supportedModeIds) =>
        new(
            id,
            displayName,
            description,
            EvaluationPolicyId,
            Criteria(id),
            supportedModeIds);

    private static ReadOnlyCollection<AgentStyleCriterionDefinitionV2> Criteria(string id) => id switch
    {
        StillwaterId => Pair(
            Criterion(
                "survival_steps",
                "Survival Steps",
                "Total rules-advanced steps.",
                AgentStyleCriterionUnit.Count,
                200),
            Criterion(
                "structural_open_exit_rate_bp",
                "Structural Open Exit Rate",
                "Basis points of rules-advanced steps whose post-step state remains running with at least two structural non-reversing exits; the denominator is all rules-advanced steps.",
                AgentStyleCriterionUnit.BasisPoints,
                9_900)),
        CrownchaserId => Pair(
            Criterion(
                "peak_combo",
                "Peak Combo",
                "Highest combo count in a rules-advanced post-step state.",
                AgentStyleCriterionUnit.Count,
                4),
            Criterion(
                "clean_pre_peak_continuity_bp",
                "Clean Pre-Peak Continuity",
                "At the first combo count of four, the uninterrupted combo length divided by all food eaten through that step, frozen thereafter in basis points.",
                AgentStyleCriterionUnit.BasisPoints,
                10_000)),
        EdgeProphetId => Pair(
            Criterion(
                "rewarded_body_proximity_near_misses",
                "Rewarded Body-Proximity Near Misses",
                "Core v4 NearMiss events at the post-step head with positive value and at least three occupied non-wrapping adjacent body cells.",
                AgentStyleCriterionUnit.Count,
                3),
            Criterion(
                "wrapped_rewarded_body_proximity_near_misses",
                "Wrapped Rewarded Body-Proximity Near Misses",
                "Rewarded core v4 body-proximity near misses accompanied by a Wrapped event in the same rules-advanced step.",
                AgentStyleCriterionUnit.Count,
                1)),
        MutagenistId => Pair(
            Criterion(
                "distinct_power_kinds_activated",
                "Distinct Power Kinds Activated",
                "Number of distinct known power kinds carried by PowerActivated events.",
                AgentStyleCriterionUnit.Count,
                2),
            Criterion(
                "maximum_concurrent_active_power_kinds",
                "Maximum Concurrent Active Power Kinds",
                "Maximum distinct active power kinds represented by any rules-advanced post-step public power state.",
                AgentStyleCriterionUnit.Count,
                2)),
        RedlineId => Pair(
            Criterion(
                "food_eaten",
                "Food Eaten",
                "Total AteFood events in rules-advanced steps.",
                AgentStyleCriterionUnit.Count,
                6),
            Criterion(
                "safe_food_progress_rate_bp",
                "Safe Food Progress Rate",
                "Basis points of rules-advanced steps beginning with food that eat or reduce wrapped distance to that exact pre-step target, end non-dead, and preserve an exit unless won; the denominator is all rules-advanced steps beginning with food.",
                AgentStyleCriterionUnit.BasisPoints,
                6_500)),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private static AgentStyleCriterionDefinitionV2 Criterion(
        string id,
        string displayName,
        string description,
        AgentStyleCriterionUnit unit,
        int target) =>
        new(
            id,
            displayName,
            description,
            AgentStyleCriterionComparator.AtLeast,
            unit,
            target);

    private static ReadOnlyCollection<AgentStyleCriterionDefinitionV2> Pair(
        AgentStyleCriterionDefinitionV2 first,
        AgentStyleCriterionDefinitionV2 second) =>
        Array.AsReadOnly(new[] { first, second });

    private static bool IsValidEvidence(
        string contractId,
        string displayName,
        string evaluationPolicyId,
        IReadOnlyList<AgentStyleCriterionProgressV2>? criteria,
        int criteriaSatisfied,
        bool allCriteriaSatisfied)
    {
        AgentStyleContractDefinitionV2 definition;
        try
        {
            definition = Get(contractId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!string.Equals(displayName, definition.DisplayName, StringComparison.Ordinal)
            || !string.Equals(
                evaluationPolicyId,
                definition.EvaluationPolicyId,
                StringComparison.Ordinal)
            || criteria is not { Count: 2 }
            || definition.Criteria.Count != criteria.Count)
        {
            return false;
        }

        var satisfied = 0;
        for (var index = 0; index < definition.Criteria.Count; index++)
        {
            var expected = definition.Criteria[index];
            var actual = criteria[index];
            if (actual is null
                || !string.Equals(actual.CriterionId, expected.Id, StringComparison.Ordinal)
                || !string.Equals(actual.DisplayName, expected.DisplayName, StringComparison.Ordinal)
                || actual.Comparator != expected.Comparator
                || actual.Unit != expected.Unit
                || actual.Target != expected.Target
                || actual.Current < 0
                || actual.Satisfied != (actual.Current >= actual.Target)
                || !HasValidEvidenceNumbers(actual))
            {
                return false;
            }

            satisfied += actual.Satisfied ? 1 : 0;
        }

        return criteriaSatisfied == satisfied
            && allCriteriaSatisfied == (satisfied == definition.Criteria.Count);
    }

    private static bool HasValidEvidenceNumbers(AgentStyleCriterionProgressV2 criterion)
    {
        if (criterion.Unit == AgentStyleCriterionUnit.Count)
        {
            return criterion.Numerator is null && criterion.Denominator is null;
        }

        if (criterion.Unit != AgentStyleCriterionUnit.BasisPoints
            || criterion.Numerator is not { } numerator
            || criterion.Denominator is not { } denominator
            || numerator < 0
            || denominator < 0
            || numerator > denominator)
        {
            return false;
        }

        try
        {
            var expected = denominator == 0
                ? 0
                : checked((int)(checked(numerator * 10_000L) / denominator));
            return criterion.Current == expected;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsLowerHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
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
