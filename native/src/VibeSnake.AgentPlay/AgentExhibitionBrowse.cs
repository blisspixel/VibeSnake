using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

/// <summary>
/// Why one archived exhibition cannot currently be watched. Absence is a
/// factual state rather than an error: an archive entry names a replay file
/// rather than embedding it, so the file can be gone while the exhibition
/// itself is still a true record of what happened.
/// </summary>
public enum AgentExhibitionWatchBlock : byte
{
    /// <summary>Nothing blocks it. The named lane replay is present.</summary>
    None = 0,

    /// <summary>The named agent-lane replay file is no longer in the replay store.</summary>
    AgentReplayMissing = 1,

    /// <summary>The exhibition has a rival lane whose replay file is no longer present.</summary>
    RivalReplayMissing = 2,
}

/// <summary>
/// One archived exhibition as a browser row. Every field is a public fact the
/// receipt already published or a presence check made when the row was built.
///
/// The row deliberately carries no display time. Exhibition identity excludes
/// it, and a browser that sorted by it would present the same exhibition
/// differently on every visit.
/// </summary>
public sealed record AgentExhibitionBrowseEntryV1(
    string Schema,
    int Position,
    string ReceiptHash,
    string RouteIdentityHash,
    string ModeId,
    string GameplaySeed,
    int Score,
    int FinalTick,
    AgentMatchEndReason EndReason,
    RunStatus RunStatus,
    string? LessonId,
    string? StyleContractId,
    string? RivalPersonalityId,
    int? RivalScore,
    string AgentReplayFileName,
    string? RivalReplayFileName,
    AgentExhibitionWatchBlock WatchBlock,
    bool RematchAvailable)
{
    public const string Contract = "vibesnake-agent-exhibition-browse-entry-v1";

    /// <summary>Whether this row can be watched right now.</summary>
    public bool WatchAvailable => WatchBlock == AgentExhibitionWatchBlock.None;

    /// <summary>
    /// Whether this row is a rivalry, which a browser shows as two lanes rather
    /// than one.
    /// </summary>
    public bool IsRivalry => RivalReplayFileName is not null;
}

/// <summary>
/// The browse view over the local exhibition archive: what a person can look
/// at, watch, and take as a same-seed challenge.
///
/// This is the machine half of the AA-06 browser. It holds every decision that
/// can be stated as a rule so presentation never invents one, and so the
/// isolation promise can be proven without a running game: taking an agent's
/// line as a challenge is a human run in its own score category and touches no
/// ordinary score, achievement, progression, or cosmetic state.
/// </summary>
public sealed record AgentExhibitionBrowseReportV1(
    string Schema,
    int EntryCount,
    int WatchableCount,
    int RematchableCount,
    int RivalryCount,
    int MissingReplayCount,
    int SelectedIndex,
    IReadOnlyList<AgentExhibitionBrowseEntryV1> Entries)
{
    public const string Contract = "vibesnake-agent-exhibition-browse-report-v1";

    /// <summary>
    /// The score context every same-seed challenge taken from this browser runs
    /// under. It is a real human run with its own display category, so it never
    /// mixes with an ordinary fresh-seed score and never inherits an agent's.
    /// </summary>
    public static ScoreRunContext ChallengeRunContext => ScoreRunContextCatalog.SeededChallenge;

    public bool IsEmpty => EntryCount == 0;

    public AgentExhibitionBrowseEntryV1? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Entries.Count ? Entries[SelectedIndex] : null;

    /// <summary>
    /// Builds the browse view from an archive listing and a replay-presence
    /// probe. Order is the archive's order, oldest first, because that is the
    /// order eviction removes them in and a browser that reordered would hide
    /// which exhibition is about to be lost.
    /// </summary>
    public static AgentExhibitionBrowseReportV1 Create(
        AgentExhibitionArchiveV2 archive,
        Func<string, bool> replayFileExists,
        int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(replayFileExists);

        var entries = new List<AgentExhibitionBrowseEntryV1>(archive.Entries.Count);
        for (var position = 0; position < archive.Entries.Count; position++)
        {
            var entry = archive.Entries[position];
            var agentPresent = replayFileExists(entry.AgentReplayFileName);
            var rivalPresent = entry.RivalReplayFileName is null
                || replayFileExists(entry.RivalReplayFileName);
            var block = !agentPresent
                ? AgentExhibitionWatchBlock.AgentReplayMissing
                : rivalPresent
                    ? AgentExhibitionWatchBlock.None
                    : AgentExhibitionWatchBlock.RivalReplayMissing;

            entries.Add(new AgentExhibitionBrowseEntryV1(
                AgentExhibitionBrowseEntryV1.Contract,
                position,
                entry.ReceiptHash,
                entry.RouteIdentityHash,
                entry.ModeId,
                entry.GameplaySeed,
                entry.Score,
                entry.Receipt.FinalTick,
                entry.EndReason,
                entry.RunStatus,
                entry.LessonId,
                entry.StyleContractId,
                entry.RivalPersonalityId,
                entry.RivalScore,
                entry.AgentReplayFileName,
                entry.RivalReplayFileName,
                block,
                // A rematch replays the line, not the recording, so it needs
                // only the seed and mode the receipt already published. A
                // missing replay file stops watching, never rematching.
                RematchAvailable: RunModeCatalog.IsSupportedIdentity(
                    entry.ModeId,
                    RunModeCatalog.CurrentModeVersion)
                    && ulong.TryParse(
                        entry.GameplaySeed,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out _)));
        }

        var bounded = entries.Count == 0
            ? -1
            : Math.Clamp(selectedIndex, 0, entries.Count - 1);
        return new AgentExhibitionBrowseReportV1(
            Contract,
            entries.Count,
            entries.Count(entry => entry.WatchAvailable),
            entries.Count(entry => entry.RematchAvailable),
            entries.Count(entry => entry.IsRivalry),
            entries.Count(entry => !entry.WatchAvailable),
            bounded,
            entries.AsReadOnly());
    }

    /// <summary>
    /// Moves the selection without wrapping past either end, so a person holding
    /// a direction never loops silently back to where they started.
    /// </summary>
    public AgentExhibitionBrowseReportV1 WithSelection(int index) =>
        Entries.Count == 0
            ? this with { SelectedIndex = -1 }
            : this with { SelectedIndex = Math.Clamp(index, 0, Entries.Count - 1) };

    /// <summary>
    /// The exact same-seed challenge for the selected exhibition, or null when
    /// nothing is selected or its identity is not one this build can start.
    /// </summary>
    public AgentExhibitionChallengeV1? SelectedChallenge() =>
        Selected is { RematchAvailable: true } entry
            ? AgentExhibitionChallengeV1.FromEntry(entry)
            : null;
}

/// <summary>
/// One exact same-seed challenge handed from an archived exhibition to a human
/// run. It carries the identity a run needs and nothing else: no passport and no
/// progression reference. The agent's score rides along as context a person can
/// see, never as a rule, so a challenge run ends on the same rules as any other.
/// </summary>
public sealed record AgentExhibitionChallengeV1(
    string Schema,
    string ReceiptHash,
    string RouteIdentityHash,
    string ModeId,
    ulong GameplaySeed,
    int AgentScore,
    string RunKindId,
    string SeedCategoryId,
    string DisplayCategoryId)
{
    public const string Contract = "vibesnake-agent-exhibition-challenge-v1";

    internal static AgentExhibitionChallengeV1 FromEntry(AgentExhibitionBrowseEntryV1 entry)
    {
        var context = AgentExhibitionBrowseReportV1.ChallengeRunContext;
        return new AgentExhibitionChallengeV1(
            Contract,
            entry.ReceiptHash,
            entry.RouteIdentityHash,
            entry.ModeId,
            ulong.Parse(
                entry.GameplaySeed,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture),
            entry.Score,
            context.RunKindId,
            context.SeedCategoryId,
            context.DisplayCategoryId);
    }

    /// <summary>
    /// A challenge is a human run in the seeded-challenge category. It is
    /// deliberately not the ordinary fresh-seed category, because the seed was
    /// chosen rather than drawn, and deliberately not an agent category,
    /// because a person is playing it.
    /// </summary>
    public bool IsIsolatedFromOrdinaryScores =>
        !string.Equals(
            RunKindId,
            ScoreRunContextCatalog.NormalHumanRunKind,
            StringComparison.Ordinal)
        && !string.Equals(
            RunKindId,
            ScoreRunContextCatalog.AiRunKind,
            StringComparison.Ordinal)
        && string.Equals(
            SeedCategoryId,
            ScoreRunContextCatalog.FixedChallengeSeedCategory,
            StringComparison.Ordinal);
}
