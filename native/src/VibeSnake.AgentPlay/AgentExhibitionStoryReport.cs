using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

/// <summary>
/// Where a recorded story is at one tick. Presentation seeks and labels from
/// this cursor so a screen never invents a beat, a lane, or a pace.
/// </summary>
public sealed record AgentExhibitionStoryCursorV1(
    string Schema,
    int Tick,
    int WindowIndex,
    AgentMontageRate Rate,
    AgentHighlightLane Lane,
    int? HighlightIndex,
    int? TurningPointIndex,
    AgentScoreRelation? ScoreRelation,
    int NextPlayableTick,
    int PreviousTurningPointTick,
    int NextTurningPointTick)
{
    public const string Contract = "vibesnake-agent-story-cursor-v1";
}

/// <summary>
/// The archive-bound story a person can watch. It loads the named lane files,
/// builds the recorded-first montage, and refuses a missing or disagreeing
/// tape before any screen starts playback.
/// </summary>
public sealed record AgentExhibitionStoryReportV1(
    string Schema,
    AgentExhibitionStoryRefuse Refuse,
    string? ReceiptHash,
    string? RouteIdentityHash,
    string? AgentReplayFileName,
    string? RivalReplayFileName,
    AgentExhibitionStoryV1? Story,
    AgentExhibitionStoryCursorV1? Cursor)
{
    public const string Contract = "vibesnake-agent-exhibition-story-report-v1";

    public bool IsAvailable =>
        Refuse == AgentExhibitionStoryRefuse.None && Story is not null && Cursor is not null;

    /// <summary>
    /// Linger holds a turning point at half the viewer's chosen speed. Skip
    /// windows are not played; the cursor jumps to the next beat instead.
    /// </summary>
    public const int LingerSpeedBasisPoints = 5_000;

    public const int SelectedSpeedBasisPoints = 10_000;

    public static int SpeedBasisPoints(AgentMontageRate rate) =>
        rate == AgentMontageRate.Linger
            ? LingerSpeedBasisPoints
            : SelectedSpeedBasisPoints;

    public static AgentExhibitionStoryReportV1 FromArchive(
        AgentArchivedExhibitionV2? entry,
        Func<string, RunReplay?> loadReplay)
    {
        ArgumentNullException.ThrowIfNull(loadReplay);
        if (entry is null)
        {
            return Unavailable(AgentExhibitionStoryRefuse.NotArchived);
        }

        var agentReplay = loadReplay(entry.AgentReplayFileName);
        if (agentReplay is null)
        {
            return Unavailable(
                AgentExhibitionStoryRefuse.AgentReplayMissing,
                entry);
        }

        RunReplay? rivalReplay = null;
        if (entry.RivalReplayFileName is { } rivalName)
        {
            rivalReplay = loadReplay(rivalName);
            if (rivalReplay is null)
            {
                return Unavailable(
                    AgentExhibitionStoryRefuse.RivalReplayMissing,
                    entry);
            }
        }

        var story = AgentExhibitionStory.TryCreate(
            entry.Receipt,
            agentReplay,
            rivalReplay,
            out var refuse);
        return story is null
            ? Unavailable(refuse, entry)
            : Available(entry, story);
    }

    public AgentExhibitionStoryReportV1 AtTick(int tick) =>
        Story is null
            ? this
            : this with { Cursor = At(Story, tick) };

    public AgentExhibitionStoryReportV1 SeekTurningPoint(int direction)
    {
        if (Story is null || Cursor is null || direction == 0)
        {
            return this;
        }

        var target = direction > 0
            ? Cursor.NextTurningPointTick
            : Cursor.PreviousTurningPointTick;
        return AtTick(target);
    }

    public static AgentExhibitionStoryCursorV1 At(AgentExhibitionStoryV1 story, int tick)
    {
        ArgumentNullException.ThrowIfNull(story);
        var last = story.Montage.Count == 0
            ? 0
            : story.Montage[^1].EndTickInclusive;
        var bounded = Math.Clamp(tick, 0, last);
        var windowIndex = 0;
        for (var index = 0; index < story.Montage.Count; index++)
        {
            var window = story.Montage[index];
            if (bounded >= window.StartTick && bounded <= window.EndTickInclusive)
            {
                windowIndex = index;
                break;
            }
        }

        var windowAtTick = story.Montage[windowIndex];
        var highlightIndex = FindHighlight(story, bounded);
        var turningPointIndex = FindTurningPoint(story, bounded);
        return new AgentExhibitionStoryCursorV1(
            AgentExhibitionStoryCursorV1.Contract,
            bounded,
            windowIndex,
            windowAtTick.Rate,
            windowAtTick.Lane,
            highlightIndex,
            turningPointIndex,
            ScoreRelationAt(story, bounded),
            NextPlayableTick(story, bounded, windowIndex),
            NearestTurningPointTick(story, bounded, forward: false),
            NearestTurningPointTick(story, bounded, forward: true));
    }

    private static AgentExhibitionStoryReportV1 Available(
        AgentArchivedExhibitionV2 entry,
        AgentExhibitionStoryV1 story) =>
        new(
            Contract,
            AgentExhibitionStoryRefuse.None,
            entry.ReceiptHash,
            entry.RouteIdentityHash,
            entry.AgentReplayFileName,
            entry.RivalReplayFileName,
            story,
            At(story, 0));

    private static AgentExhibitionStoryReportV1 Unavailable(
        AgentExhibitionStoryRefuse refuse,
        AgentArchivedExhibitionV2? entry = null) =>
        new(
            Contract,
            refuse,
            entry?.ReceiptHash,
            entry?.RouteIdentityHash,
            entry?.AgentReplayFileName,
            entry?.RivalReplayFileName,
            Story: null,
            Cursor: null);

    private static int? FindHighlight(AgentExhibitionStoryV1 story, int tick)
    {
        for (var index = story.Highlights.Count - 1; index >= 0; index--)
        {
            if (story.Highlights[index].Tick == tick)
            {
                return index;
            }
        }

        return null;
    }

    private static int? FindTurningPoint(AgentExhibitionStoryV1 story, int tick)
    {
        for (var index = 0; index < story.TurningPointIndexes.Count; index++)
        {
            if (story.Highlights[story.TurningPointIndexes[index]].Tick == tick)
            {
                return index;
            }
        }

        return null;
    }

    private static AgentScoreRelation? ScoreRelationAt(
        AgentExhibitionStoryV1 story,
        int tick)
    {
        AgentScoreRelation? relation = null;
        foreach (var highlight in story.Highlights)
        {
            if (highlight.Kind != AgentHighlightKind.LeadChange || highlight.Tick > tick)
            {
                continue;
            }

            if (highlight.Detail is { } detail)
            {
                relation = (AgentScoreRelation)detail;
            }
        }

        return relation;
    }

    private static int NextPlayableTick(
        AgentExhibitionStoryV1 story,
        int tick,
        int windowIndex)
    {
        if (story.Montage[windowIndex].Rate != AgentMontageRate.Skip)
        {
            return tick;
        }

        for (var index = windowIndex + 1; index < story.Montage.Count; index++)
        {
            if (story.Montage[index].Rate != AgentMontageRate.Skip)
            {
                return story.Montage[index].StartTick;
            }
        }

        return story.Montage[^1].EndTickInclusive;
    }

    private static int NearestTurningPointTick(
        AgentExhibitionStoryV1 story,
        int tick,
        bool forward)
    {
        if (story.TurningPointIndexes.Count == 0)
        {
            return tick;
        }

        if (forward)
        {
            foreach (var index in story.TurningPointIndexes)
            {
                var candidate = story.Highlights[index].Tick;
                if (candidate > tick)
                {
                    return candidate;
                }
            }

            return story.Highlights[story.TurningPointIndexes[^1]].Tick;
        }

        for (var index = story.TurningPointIndexes.Count - 1; index >= 0; index--)
        {
            var candidate = story.Highlights[story.TurningPointIndexes[index]].Tick;
            if (candidate < tick)
            {
                return candidate;
            }
        }

        return story.Highlights[story.TurningPointIndexes[0]].Tick;
    }
}
