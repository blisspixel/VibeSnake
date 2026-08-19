using System.Collections.ObjectModel;
using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

/// <summary>
/// Why a recorded exhibition story could not be built. Refusal is a factual
/// state: the story is derived from a receipt and verified replays, so a
/// missing or disagreeing tape is not a story.
/// </summary>
public enum AgentExhibitionStoryRefuse : byte
{
    None = 0,
    InvalidReceipt = 1,
    AgentReplayHashMismatch = 2,
    RivalReplayMissing = 3,
    RivalReplayHashMismatch = 4,
    NotArchived = 5,
    AgentReplayMissing = 6,
}

public enum AgentHighlightLane : byte
{
    Agent = 0,
    Rival = 1,
}

public enum AgentHighlightKind : byte
{
    TerminalWon = 0,
    TerminalDied = 1,
    LeadChange = 2,
    Recovery = 3,
    StyleAllThresholds = 4,
    LessonAllRequirements = 5,
    StyleThresholdFirst = 6,
    LessonRequirementFirst = 7,
    NearMiss = 8,
    ComboMilestone = 9,
    PowerActivated = 10,
    PowerCollected = 11,
    HungerWarning = 12,
    PressureTrapped = 13,
    PressurePinned = 14,
    IntentChanged = 15,
}

public enum AgentScoreRelation : byte
{
    Ahead = 0,
    Level = 1,
    Behind = 2,
}

public enum AgentMontageRate : byte
{
    Selected = 0,
    Linger = 1,
    Skip = 2,
}

public sealed record AgentHighlightV1(
    string Schema,
    AgentHighlightLane Lane,
    int Tick,
    AgentHighlightKind Kind,
    int Ordinal,
    int? Detail)
{
    public const string Contract = "vibesnake-agent-highlight-v1";
}

public sealed record AgentMontageWindowV1(
    string Schema,
    AgentHighlightLane Lane,
    int StartTick,
    int EndTickInclusive,
    AgentMontageRate Rate)
{
    public const string Contract = "vibesnake-agent-montage-window-v1";
}

public sealed record AgentExhibitionStoryV1(
    string Schema,
    string ReceiptHash,
    string RouteIdentityHash,
    string AgentReplayPayloadHash,
    string? RivalReplayPayloadHash,
    string CatalogId,
    string SelectorId,
    string PaceId,
    IReadOnlyList<AgentHighlightV1> Highlights,
    IReadOnlyList<int> TurningPointIndexes,
    IReadOnlyList<AgentMontageWindowV1> Montage)
{
    public const string Contract = "vibesnake-agent-exhibition-story-v1";
    public const string CatalogIdValue = "vibesnake-agent-highlight-catalog-v1";
    public const string SelectorIdValue = "vibesnake-agent-turning-point-select-v1";
    public const string PaceIdValue = "vibesnake-agent-broadcast-pace-v1";
    public const int MaximumHighlights = 64;
    public const int MaximumTurningPoints = 8;
    public const int MinimumTurningPointGap = 8;
}

/// <summary>
/// Builds a recorded-first story from one verified exhibition. It invents no
/// second identity: the story binds the receipt hashes and the verified
/// replay payload hashes already published.
/// </summary>
public static class AgentExhibitionStory
{
    public static AgentExhibitionStoryV1? TryCreate(
        AgentExhibitionReceiptV2 receipt,
        RunReplay agentReplay,
        RunReplay? rivalReplay,
        out AgentExhibitionStoryRefuse refuse)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(agentReplay);
        if (!AgentExhibitionReceipt.HasCanonicalHash(receipt))
        {
            refuse = AgentExhibitionStoryRefuse.InvalidReceipt;
            return null;
        }

        if (!string.Equals(
                agentReplay.PayloadHash,
                receipt.AgentReplayPayloadHash,
                StringComparison.Ordinal))
        {
            refuse = AgentExhibitionStoryRefuse.AgentReplayHashMismatch;
            return null;
        }

        if (receipt.RivalReplayPayloadHash is not null)
        {
            if (rivalReplay is null)
            {
                refuse = AgentExhibitionStoryRefuse.RivalReplayMissing;
                return null;
            }

            if (!string.Equals(
                    rivalReplay.PayloadHash,
                    receipt.RivalReplayPayloadHash,
                    StringComparison.Ordinal))
            {
                refuse = AgentExhibitionStoryRefuse.RivalReplayHashMismatch;
                return null;
            }
        }

        var highlights = Scan(receipt, agentReplay, rivalReplay);
        var turningPoints = SelectTurningPoints(highlights);
        var montage = PlanMontage(highlights, turningPoints, agentReplay.Steps.Count);
        refuse = AgentExhibitionStoryRefuse.None;
        return new AgentExhibitionStoryV1(
            AgentExhibitionStoryV1.Contract,
            receipt.ReceiptHash,
            receipt.RouteIdentityHash,
            receipt.AgentReplayPayloadHash,
            receipt.RivalReplayPayloadHash,
            AgentExhibitionStoryV1.CatalogIdValue,
            AgentExhibitionStoryV1.SelectorIdValue,
            AgentExhibitionStoryV1.PaceIdValue,
            highlights,
            turningPoints,
            montage);
    }

    private static IReadOnlyList<AgentHighlightV1> Scan(
        AgentExhibitionReceiptV2 receipt,
        RunReplay agentReplay,
        RunReplay? rivalReplay)
    {
        var raw = new List<AgentHighlightV1>();
        ScanLane(raw, AgentHighlightLane.Agent, agentReplay);
        if (rivalReplay is not null)
        {
            ScanLane(raw, AgentHighlightLane.Rival, rivalReplay);
            AddLeadChanges(raw, agentReplay, rivalReplay);
        }

        AddIntentChanges(raw, receipt);
        AddLessonRequirementFirsts(raw, receipt, agentReplay);
        AddStyleThresholdFirsts(raw, receipt, agentReplay);
        if (receipt.LessonOutcome is { AllRequirementsSatisfied: true })
        {
            raw.Add(Highlight(
                AgentHighlightLane.Agent,
                receipt.FinalTick,
                AgentHighlightKind.LessonAllRequirements,
                0,
                null));
        }

        if (receipt.StyleOutcome is { AllThresholdsReached: true })
        {
            raw.Add(Highlight(
                AgentHighlightLane.Agent,
                receipt.FinalTick,
                AgentHighlightKind.StyleAllThresholds,
                0,
                null));
        }

        return Cap(raw);
    }

    private static void ScanLane(
        List<AgentHighlightV1> raw,
        AgentHighlightLane lane,
        RunReplay replay)
    {
        var playback = new RunReplayPlayback(replay);
        var seenCombo = new HashSet<int>();
        var seenPressure = new HashSet<AgentExitPressureV1>();
        var ordinals = new Dictionary<AgentHighlightKind, int>();
        while (playback.TryAdvance(out var frame) && frame is not null)
        {
            var tick = frame.Snapshot.Tick;
            foreach (var detail in frame.Result.OrderedEvents)
            {
                var kind = detail.Kind switch
                {
                    RunEventKind.Won => AgentHighlightKind.TerminalWon,
                    RunEventKind.Died => AgentHighlightKind.TerminalDied,
                    RunEventKind.CollisionPrevented => AgentHighlightKind.Recovery,
                    RunEventKind.NearMiss => AgentHighlightKind.NearMiss,
                    RunEventKind.PowerActivated => AgentHighlightKind.PowerActivated,
                    RunEventKind.PowerCollected => AgentHighlightKind.PowerCollected,
                    RunEventKind.StarvationWarning => AgentHighlightKind.HungerWarning,
                    _ => (AgentHighlightKind?)null,
                };
                if (kind is { } mapped)
                {
                    Add(raw, ordinals, lane, tick, mapped, detail.Value);
                }
            }

            var combo = frame.Snapshot.ComboCount;
            if (combo is 2 or 3 or 4 && seenCombo.Add(combo))
            {
                Add(raw, ordinals, lane, tick, AgentHighlightKind.ComboMilestone, combo);
            }

            var exits = AgentStyleEvidenceMath.StructuralOpenExitCount(
                playback.Configuration,
                frame.Snapshot);
            var pressure = AgentSurvivalStateV1.Pressure(
                frame.Snapshot.Status == RunStatus.Running,
                exits);
            if ((pressure == AgentExitPressureV1.Pinned
                    || pressure == AgentExitPressureV1.Trapped)
                && seenPressure.Add(pressure))
            {
                Add(
                    raw,
                    ordinals,
                    lane,
                    tick,
                    pressure == AgentExitPressureV1.Trapped
                        ? AgentHighlightKind.PressureTrapped
                        : AgentHighlightKind.PressurePinned,
                    exits);
            }

        }
    }

    private static void AddLeadChanges(
        List<AgentHighlightV1> raw,
        RunReplay agentReplay,
        RunReplay rivalReplay)
    {
        var agent = new RunReplayPlayback(agentReplay);
        var rival = new RunReplayPlayback(rivalReplay);
        var lastRivalScore = 0;
        AgentScoreRelation? previous = null;
        var ordinal = 0;
        while (agent.TryAdvance(out var agentFrame) && agentFrame is not null)
        {
            while (!rival.IsComplete && rival.StepIndex < agentFrame.Snapshot.Tick)
            {
                if (rival.TryAdvance(out var rivalFrame) && rivalFrame is not null)
                {
                    lastRivalScore = rivalFrame.Snapshot.Score;
                }
            }

            var relation = Relation(agentFrame.Snapshot.Score, lastRivalScore);
            if (previous is { } && previous != relation)
            {
                raw.Add(Highlight(
                    relation == AgentScoreRelation.Behind
                        ? AgentHighlightLane.Rival
                        : AgentHighlightLane.Agent,
                    agentFrame.Snapshot.Tick,
                    AgentHighlightKind.LeadChange,
                    ordinal,
                    (int)relation));
                ordinal++;
            }

            previous = relation;
        }
    }

    private static void AddIntentChanges(
        List<AgentHighlightV1> raw,
        AgentExhibitionReceiptV2 receipt)
    {
        AgentPublicIntent? previous = null;
        var ordinal = 0;
        foreach (var step in receipt.AcceptedPresentationEvents)
        {
            if (previous is { } prior
                && prior != step.DeclaredIntent
                && !(prior == AgentPublicIntent.Undeclared
                    && step.DeclaredIntent == AgentPublicIntent.Undeclared))
            {
                raw.Add(Highlight(
                    AgentHighlightLane.Agent,
                    step.Tick,
                    AgentHighlightKind.IntentChanged,
                    ordinal,
                    (int)step.DeclaredIntent));
                ordinal++;
            }

            previous = step.DeclaredIntent;
        }
    }

    /// <summary>
    /// First-crossing ticks are reconstructed from the named agent tape with
    /// the same evaluators the terminal outcomes use. Attempt-witness lesson
    /// requirements have no tape tick, so they are not first-crossing beats.
    /// Detail is the zero-based requirement index.
    /// </summary>
    private static void AddLessonRequirementFirsts(
        List<AgentHighlightV1> raw,
        AgentExhibitionReceiptV2 receipt,
        RunReplay agentReplay)
    {
        if (receipt.LessonOutcome is null)
        {
            return;
        }

        var playback = new RunReplayPlayback(agentReplay);
        var tracker = new AgentLessonEvidenceTracker(
            receipt.LessonOutcome.LessonId,
            playback.Configuration);
        var reached = new HashSet<int>();
        var ordinal = 0;
        while (!playback.IsComplete)
        {
            var before = playback.CurrentSnapshot;
            if (!playback.TryAdvance(out var frame) || frame is null)
            {
                break;
            }

            tracker.RecordStep(before, frame.Result, frame.Snapshot);
            var progress = tracker.Snapshot(
                AgentLessonEvidenceState.Live,
                receipt.Passport.ActionProfile);
            for (var index = 0; index < progress.Requirements.Count; index++)
            {
                if (reached.Contains(index) || !progress.Requirements[index].Satisfied)
                {
                    continue;
                }

                reached.Add(index);
                raw.Add(Highlight(
                    AgentHighlightLane.Agent,
                    frame.Snapshot.Tick,
                    AgentHighlightKind.LessonRequirementFirst,
                    ordinal,
                    index));
                ordinal++;
            }
        }
    }

    /// <summary>
    /// First-crossing ticks are reconstructed from the named agent tape with
    /// the same style evaluator the terminal outcome uses. Detail is the
    /// zero-based criterion index.
    /// </summary>
    private static void AddStyleThresholdFirsts(
        List<AgentHighlightV1> raw,
        AgentExhibitionReceiptV2 receipt,
        RunReplay agentReplay)
    {
        if (receipt.StyleOutcome is null)
        {
            return;
        }

        var playback = new RunReplayPlayback(agentReplay);
        var tracker = new AgentStyleEvidenceTracker(
            receipt.StyleOutcome.ContractId,
            receipt.Division.ModeId,
            playback.Configuration,
            playback.CurrentSnapshot);
        var reached = new HashSet<int>();
        var ordinal = 0;
        while (!playback.IsComplete)
        {
            var before = playback.CurrentSnapshot;
            if (!playback.TryAdvance(out var frame) || frame is null)
            {
                break;
            }

            tracker.Record(before, frame.Result, frame.Snapshot);
            var progress = tracker.Snapshot();
            for (var index = 0; index < progress.Criteria.Count; index++)
            {
                if (reached.Contains(index) || !progress.Criteria[index].ThresholdReached)
                {
                    continue;
                }

                reached.Add(index);
                raw.Add(Highlight(
                    AgentHighlightLane.Agent,
                    frame.Snapshot.Tick,
                    AgentHighlightKind.StyleThresholdFirst,
                    ordinal,
                    index));
                ordinal++;
            }
        }
    }

    private static int[] SelectTurningPoints(
        IReadOnlyList<AgentHighlightV1> highlights)
    {
        var ranked = highlights
            .Select((highlight, index) => (highlight, index))
            .OrderBy(item => Priority(item.highlight.Kind))
            .ThenBy(item => item.highlight.Tick)
            .ThenBy(item => item.highlight.Lane)
            .ThenBy(item => item.highlight.Kind)
            .ThenBy(item => item.highlight.Ordinal)
            .ToArray();
        var selected = new List<int>();
        var kindCounts = new Dictionary<AgentHighlightKind, int>();
        foreach (var (highlight, index) in ranked)
        {
            if (selected.Count >= AgentExhibitionStoryV1.MaximumTurningPoints)
            {
                break;
            }

            var sameKind = kindCounts.GetValueOrDefault(highlight.Kind);
            var kindCap = highlight.Kind switch
            {
                AgentHighlightKind.TerminalWon or AgentHighlightKind.TerminalDied => int.MaxValue,
                AgentHighlightKind.LeadChange => 3,
                _ => 2,
            };
            if (sameKind >= kindCap)
            {
                continue;
            }

            if (highlight.Kind is not AgentHighlightKind.TerminalWon
                and not AgentHighlightKind.TerminalDied
                && selected.Any(chosen =>
                    Math.Abs(highlights[chosen].Tick - highlight.Tick)
                    < AgentExhibitionStoryV1.MinimumTurningPointGap))
            {
                continue;
            }

            selected.Add(index);
            kindCounts[highlight.Kind] = sameKind + 1;
        }

        return selected
            .OrderBy(index => highlights[index].Tick)
            .ThenBy(index => highlights[index].Lane)
            .ToArray();
    }

    private static ReadOnlyCollection<AgentMontageWindowV1> PlanMontage(
        IReadOnlyList<AgentHighlightV1> highlights,
        int[] turningPoints,
        int stepCount)
    {
        // Snapshot ticks are 0 at the initial state and equal the step count
        // after the last recorded step. Coverage includes both ends so a
        // highlight at FinalTick is inside the montage rather than after it.
        var lastTick = Math.Max(
            Math.Max(0, stepCount),
            highlights.Count == 0 ? 0 : highlights.Max(highlight => highlight.Tick));
        var rate = new AgentMontageRate[lastTick + 1];
        var lane = new AgentHighlightLane[lastTick + 1];
        Array.Fill(rate, AgentMontageRate.Skip);
        Array.Fill(lane, AgentHighlightLane.Agent);

        if (turningPoints.Length == 0)
        {
            Paint(rate, lane, 0, lastTick, AgentMontageRate.Selected, AgentHighlightLane.Agent);
        }
        else
        {
            Paint(
                rate,
                lane,
                0,
                Math.Min(8, highlights[turningPoints[0]].Tick),
                AgentMontageRate.Selected,
                AgentHighlightLane.Agent);
            foreach (var index in turningPoints)
            {
                var point = highlights[index];
                var lingerEnd = Math.Min(lastTick, point.Tick + 3);
                Paint(
                    rate,
                    lane,
                    Math.Max(0, point.Tick - 6),
                    Math.Max(0, point.Tick - 1),
                    AgentMontageRate.Selected,
                    point.Lane);
                Paint(
                    rate,
                    lane,
                    point.Tick,
                    lingerEnd,
                    AgentMontageRate.Linger,
                    point.Lane);
                Paint(
                    rate,
                    lane,
                    lingerEnd + 1,
                    Math.Min(lastTick, lingerEnd + 4),
                    AgentMontageRate.Selected,
                    point.Lane);
            }
        }

        return MergeWindows(rate, lane);
    }

    private static void Paint(
        AgentMontageRate[] rate,
        AgentHighlightLane[] lane,
        int startTick,
        int endTickInclusive,
        AgentMontageRate paintedRate,
        AgentHighlightLane paintedLane)
    {
        var last = rate.Length - 1;
        var start = Math.Clamp(startTick, 0, last);
        var end = Math.Clamp(endTickInclusive, 0, last);
        if (end < start)
        {
            return;
        }

        for (var tick = start; tick <= end; tick++)
        {
            // Linger is the turning-point hold. It wins a later Selected paint
            // so two nearby beats cannot erase each other's pause.
            if (paintedRate == AgentMontageRate.Linger
                || rate[tick] != AgentMontageRate.Linger)
            {
                rate[tick] = paintedRate;
                lane[tick] = paintedLane;
            }
        }
    }

    private static ReadOnlyCollection<AgentMontageWindowV1> MergeWindows(
        AgentMontageRate[] rate,
        AgentHighlightLane[] lane)
    {
        var windows = new List<AgentMontageWindowV1>();
        var start = 0;
        for (var tick = 1; tick <= rate.Length; tick++)
        {
            if (tick < rate.Length
                && rate[tick] == rate[start]
                && lane[tick] == lane[start])
            {
                continue;
            }

            windows.Add(new AgentMontageWindowV1(
                AgentMontageWindowV1.Contract,
                lane[start],
                start,
                tick - 1,
                rate[start]));
            start = tick;
        }

        return windows.AsReadOnly();
    }

    private static IReadOnlyList<AgentHighlightV1> Cap(List<AgentHighlightV1> raw)
    {
        if (raw.Count <= AgentExhibitionStoryV1.MaximumHighlights)
        {
            return raw.AsReadOnly();
        }

        var kept = new HashSet<AgentHighlightKind>
        {
            AgentHighlightKind.TerminalWon,
            AgentHighlightKind.TerminalDied,
            AgentHighlightKind.LeadChange,
            AgentHighlightKind.StyleAllThresholds,
            AgentHighlightKind.LessonAllRequirements,
            AgentHighlightKind.StyleThresholdFirst,
            AgentHighlightKind.LessonRequirementFirst,
        };
        return raw
            .OrderBy(highlight => kept.Contains(highlight.Kind) ? 0 : 1)
            .ThenBy(highlight => Priority(highlight.Kind))
            .ThenBy(highlight => highlight.Tick)
            .Take(AgentExhibitionStoryV1.MaximumHighlights)
            .OrderBy(highlight => highlight.Tick)
            .ThenBy(highlight => highlight.Lane)
            .ThenBy(highlight => highlight.Kind)
            .ToArray();
    }

    private static void Add(
        List<AgentHighlightV1> raw,
        Dictionary<AgentHighlightKind, int> ordinals,
        AgentHighlightLane lane,
        int tick,
        AgentHighlightKind kind,
        int? detail)
    {
        var ordinal = ordinals.GetValueOrDefault(kind);
        raw.Add(Highlight(lane, tick, kind, ordinal, detail));
        ordinals[kind] = ordinal + 1;
    }

    private static AgentHighlightV1 Highlight(
        AgentHighlightLane lane,
        int tick,
        AgentHighlightKind kind,
        int ordinal,
        int? detail) =>
        new(AgentHighlightV1.Contract, lane, tick, kind, ordinal, detail);

    private static AgentScoreRelation Relation(int agentScore, int rivalScore) =>
        agentScore > rivalScore
            ? AgentScoreRelation.Ahead
            : agentScore == rivalScore
                ? AgentScoreRelation.Level
                : AgentScoreRelation.Behind;

    private static int Priority(AgentHighlightKind kind) => kind switch
    {
        AgentHighlightKind.TerminalWon or AgentHighlightKind.TerminalDied => 0,
        AgentHighlightKind.LeadChange => 1,
        AgentHighlightKind.Recovery => 2,
        AgentHighlightKind.StyleAllThresholds
            or AgentHighlightKind.LessonAllRequirements => 3,
        AgentHighlightKind.StyleThresholdFirst
            or AgentHighlightKind.LessonRequirementFirst => 4,
        AgentHighlightKind.NearMiss => 5,
        AgentHighlightKind.ComboMilestone => 6,
        AgentHighlightKind.PowerActivated or AgentHighlightKind.PowerCollected => 7,
        AgentHighlightKind.HungerWarning => 8,
        AgentHighlightKind.PressureTrapped or AgentHighlightKind.PressurePinned => 9,
        _ => 10,
    };
}
