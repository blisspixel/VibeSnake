namespace VibeSnake.Rules;

public enum LoreDepth : byte
{
    Surface = 0,
    Discoverable = 1,
    Archive = 2,
}

public enum LoreContentKind : byte
{
    StationIdentity = 0,
    RivalIdentity = 1,
    MutationGlossary = 2,
    RivalHistory = 3,
    StationHistory = 4,
    TrackNote = 5,
    ThemedCollection = 6,
    ReplayMilestone = 7,
    BroadcastFragment = 8,
    Transcript = 9,
    Timeline = 10,
    Mystery = 11,
    AlternateInterpretation = 12,
}

public enum LoreCanonTier : byte
{
    Foundation = 0,
    Contextual = 1,
    Disputed = 2,
}

public enum LoreUnlockKind : byte
{
    Always = 0,
    ProgressionReward = 1,
    SpectatorMilestone = 2,
    LocalReplayCount = 3,
}

public sealed record LoreEntry(
    int SchemaVersion,
    string Id,
    LoreDepth Depth,
    LoreContentKind Kind,
    LoreCanonTier CanonTier,
    string TitleCopyId,
    string BodyCopyId,
    string? SpeakerId,
    string? StationId,
    LoreUnlockKind UnlockKind,
    string? UnlockId,
    int UnlockThreshold,
    IReadOnlyList<string> EntityIds,
    IReadOnlyList<string> ContinuityEntryIds,
    bool RequiredForPlay,
    bool ActiveRunInterruptible,
    bool AwardsProgression);

public sealed record LoreUnlockContext(
    IReadOnlySet<string> ProgressionRewardIds,
    IReadOnlySet<string> SpectatorMilestoneIds,
    int LocalReplayCount)
{
    public static LoreUnlockContext Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        0);
}

public sealed record LoreCatalogValidation(
    bool Passed,
    int EntryCount,
    int SurfaceCount,
    int DiscoverableCount,
    int ArchiveCount,
    int DuplicateIdCount,
    int MissingCopyIdCount,
    int UnknownEntityCount,
    int BrokenContinuityCount,
    int InvalidUnlockCount,
    int UnsafeCriticalEntryCount,
    int SurfaceStationCount,
    int SurfaceRivalCount,
    int SurfaceMutationCount,
    int DiscoverableKindCount,
    int ArchiveKindCount);

/// <summary>
/// Closed authored lore metadata. Rules state stores no prose, unlock checks are
/// read-only, and every entry is optional presentation outside active play.
/// </summary>
public static class LoreCatalog
{
    public const int SchemaVersion = 1;

    public static IReadOnlyList<LoreEntry> All { get; } =
    [
        Surface("station-flow-signal", LoreContentKind.StationIdentity, stationId: "flow_signal", entities: ["station:flow_signal", "host:cadence-vale"]),
        Surface("station-chaos-theory", LoreContentKind.StationIdentity, stationId: "chaos_theory", entities: ["station:chaos_theory", "host:dr-sibilant"]),
        Surface("station-global-coil", LoreContentKind.StationIdentity, stationId: "global_coil", entities: ["station:global_coil", "host:sol-coil"]),
        Surface("station-ourotron", LoreContentKind.StationIdentity, stationId: "ourotron", entities: ["station:ourotron", "host:vektor-null"]),
        Surface("station-pit", LoreContentKind.StationIdentity, stationId: "the_pit", entities: ["station:the_pit", "host:dj-rattlebyte"]),
        Surface("station-bureau", LoreContentKind.StationIdentity, stationId: "the_bureau", entities: ["station:the_bureau", "host:anchor-seven"]),
        Surface("station-strike", LoreContentKind.StationIdentity, stationId: "the_strike", entities: ["station:the_strike", "host:rivet"]),
        Surface("station-underground", LoreContentKind.StationIdentity, stationId: "underground_scales", entities: ["station:underground_scales", "host:molt-one"]),
        Surface("rival-redline", LoreContentKind.RivalIdentity, stationId: "the_pit", entities: ["rival:speed_demon"]),
        Surface("rival-shelter-coil", LoreContentKind.RivalIdentity, stationId: "flow_signal", entities: ["rival:coward"]),
        Surface("rival-crownchaser", LoreContentKind.RivalIdentity, stationId: "the_strike", entities: ["rival:greedy"]),
        Surface("rival-mutagenist", LoreContentKind.RivalIdentity, stationId: "the_pit", entities: ["rival:power_hunter"]),
        Surface("rival-noise-coil", LoreContentKind.RivalIdentity, stationId: "chaos_theory", entities: ["rival:drunk"]),
        Surface("rival-proof", LoreContentKind.RivalIdentity, stationId: "the_bureau", entities: ["rival:optimal"]),
        Surface("rival-edge-prophet", LoreContentKind.RivalIdentity, stationId: "underground_scales", entities: ["rival:yolo"]),
        Surface("rival-meanline", LoreContentKind.RivalIdentity, stationId: "global_coil", entities: ["rival:balanced"]),
        Surface("rival-rimkeeper", LoreContentKind.RivalIdentity, stationId: "ourotron", entities: ["rival:wall_hugger"]),
        Surface("rival-stillwater", LoreContentKind.RivalIdentity, stationId: "flow_signal", entities: ["rival:zen_master"]),
        Surface("mutation-glossary", LoreContentKind.MutationGlossary, entities: ["mutation:shield", "mutation:phase-shift", "mutation:gluttony", "mutation:last-stand", "mutation:bait", "mutation:segment-detach", "mutation:slow-mo", "mutation:boost", "mutation:magnet"]),

        Discoverable("history-redline", LoreContentKind.RivalHistory, LoreUnlockKind.SpectatorMilestone, "match-win", entities: ["rival:speed_demon"], continuity: ["rival-redline"]),
        Discoverable("history-shelter-coil", LoreContentKind.RivalHistory, LoreUnlockKind.ProgressionReward, "dossier:shelter-coil", entities: ["rival:coward"], continuity: ["rival-shelter-coil"]),
        Discoverable("history-proof", LoreContentKind.RivalHistory, LoreUnlockKind.ProgressionReward, "dossier:the-proof", entities: ["rival:optimal"], continuity: ["rival-proof"]),
        Discoverable("history-meanline", LoreContentKind.RivalHistory, LoreUnlockKind.SpectatorMilestone, "first-broadcast", entities: ["rival:balanced"], continuity: ["rival-meanline"]),
        Discoverable("track-flow-breath", LoreContentKind.TrackNote, LoreUnlockKind.SpectatorMilestone, "survive-500", stationId: "flow_signal", entities: ["station:flow_signal"], continuity: ["station-flow-signal"]),
        Discoverable("track-chaos-offset", LoreContentKind.TrackNote, LoreUnlockKind.SpectatorMilestone, "collision-save", stationId: "chaos_theory", entities: ["station:chaos_theory"], continuity: ["station-chaos-theory"]),
        Discoverable("collection-mutation-prism", LoreContentKind.ThemedCollection, LoreUnlockKind.ProgressionReward, "shed:mutagenist", stationId: "the_pit", entities: ["rival:power_hunter", "mutation:shield", "mutation:phase-shift"], continuity: ["rival-mutagenist", "mutation-glossary"]),
        Discoverable("collection-first-signal", LoreContentKind.ThemedCollection, LoreUnlockKind.ProgressionReward, "shed:first-signal", stationId: "global_coil", entities: ["station:global_coil"], continuity: ["station-global-coil"]),
        Discoverable("replay-first-echo", LoreContentKind.ReplayMilestone, LoreUnlockKind.LocalReplayCount, threshold: 1, entities: ["concept:echo"]),
        Discoverable("replay-five-echoes", LoreContentKind.ReplayMilestone, LoreUnlockKind.LocalReplayCount, threshold: 5, entities: ["concept:echo"], continuity: ["replay-first-echo"]),
        Discoverable("fragment-bureau-comfort", LoreContentKind.BroadcastFragment, LoreUnlockKind.SpectatorMilestone, "power-route", speakerId: "anchor-seven", stationId: "the_bureau", entities: ["station:the_bureau"], continuity: ["station-bureau"]),
        Discoverable("fragment-underground-shed", LoreContentKind.BroadcastFragment, LoreUnlockKind.ProgressionReward, "shed:edge-prophet", speakerId: "molt-one", stationId: "underground_scales", entities: ["station:underground_scales", "rival:yolo"], continuity: ["station-underground", "rival-edge-prophet"]),
        Discoverable("history-ourotron-five", LoreContentKind.StationHistory, LoreUnlockKind.ProgressionReward, "replay-frame:ourotron", stationId: "ourotron", entities: ["station:ourotron"], continuity: ["station-ourotron"]),
        Discoverable("history-strike-carrier", LoreContentKind.StationHistory, LoreUnlockKind.ProgressionReward, "station-note:strike-1", stationId: "the_strike", entities: ["station:the_strike"], continuity: ["station-strike"]),

        Archive("transcript-molt-hearing", LoreContentKind.Transcript, LoreCanonTier.Contextual, LoreUnlockKind.ProgressionReward, "archive:rim-route", speakerId: "archive-collective", entities: ["event:great-molt"], continuity: ["mutation-glossary"]),
        Archive("transcript-pit-safety", LoreContentKind.Transcript, LoreCanonTier.Contextual, LoreUnlockKind.SpectatorMilestone, "collision-save", speakerId: "dj-rattlebyte", stationId: "the_pit", entities: ["station:the_pit"], continuity: ["station-pit", "collection-mutation-prism"]),
        Archive("timeline-first-carrier", LoreContentKind.Timeline, LoreCanonTier.Foundation, LoreUnlockKind.LocalReplayCount, threshold: 1, entities: ["event:first-carrier"]),
        Archive("timeline-great-molt", LoreContentKind.Timeline, LoreCanonTier.Foundation, LoreUnlockKind.ProgressionReward, "archive:rim-route", entities: ["event:great-molt"], continuity: ["timeline-first-carrier"]),
        Archive("timeline-coil-accord", LoreContentKind.Timeline, LoreCanonTier.Foundation, LoreUnlockKind.SpectatorMilestone, "first-broadcast", entities: ["event:coil-accord"], continuity: ["timeline-great-molt"]),
        Archive("mystery-ninth-frequency", LoreContentKind.Mystery, LoreCanonTier.Disputed, LoreUnlockKind.LocalReplayCount, threshold: 5, entities: ["mystery:ninth-frequency"], continuity: ["replay-five-echoes"]),
        Archive("interpretation-disciplined-molt", LoreContentKind.AlternateInterpretation, LoreCanonTier.Disputed, LoreUnlockKind.SpectatorMilestone, "survive-500", stationId: "flow_signal", entities: ["event:great-molt", "station:flow_signal"], continuity: ["station-flow-signal", "timeline-great-molt"]),
        Archive("interpretation-liberated-molt", LoreContentKind.AlternateInterpretation, LoreCanonTier.Disputed, LoreUnlockKind.SpectatorMilestone, "match-win", stationId: "chaos_theory", entities: ["event:great-molt", "station:chaos_theory"], continuity: ["station-chaos-theory", "timeline-great-molt"]),
    ];

    public static bool IsUnlocked(LoreEntry entry, LoreUnlockContext context)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(context);
        return entry.UnlockKind switch
        {
            LoreUnlockKind.Always => true,
            LoreUnlockKind.ProgressionReward => context.ProgressionRewardIds.Contains(entry.UnlockId!),
            LoreUnlockKind.SpectatorMilestone => context.SpectatorMilestoneIds.Contains(entry.UnlockId!),
            LoreUnlockKind.LocalReplayCount => context.LocalReplayCount >= entry.UnlockThreshold,
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        };
    }

    public static LoreCatalogValidation Validate() => ValidateEntries(All);

    internal static LoreCatalogValidation ValidateEntries(
        IReadOnlyList<LoreEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var knownIds = entries.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var knownStations = KnownStationIds.ToHashSet(StringComparer.Ordinal);
        var knownEntities = KnownEntityIds.ToHashSet(StringComparer.Ordinal);
        var knownRewards = ProgressionGoalCatalog.Goals.Select(item => item.Reward.Id)
            .Concat(BroadcastTourCatalog.Events.Select(item => item.Reward.Id))
            .ToHashSet(StringComparer.Ordinal);
        var duplicateIds = entries.Count - knownIds.Count;
        var missingCopyIds = entries.Count(item =>
            string.IsNullOrWhiteSpace(item.TitleCopyId)
            || string.IsNullOrWhiteSpace(item.BodyCopyId)
            || item.TitleCopyId != $"lore.entry.{item.Id}.title"
            || item.BodyCopyId != $"lore.entry.{item.Id}.body");
        var unknownEntities = entries.Sum(item => item.EntityIds.Count(entity =>
            !knownEntities.Contains(entity)));
        var brokenContinuity = entries.Sum(item => item.ContinuityEntryIds.Count(id =>
            !knownIds.Contains(id) || id == item.Id));
        var invalidUnlocks = entries.Count(item => item.UnlockKind switch
        {
            LoreUnlockKind.Always => item.UnlockId is not null || item.UnlockThreshold != 0,
            LoreUnlockKind.ProgressionReward => item.UnlockId is null
                || !knownRewards.Contains(item.UnlockId)
                || item.UnlockThreshold != 0,
            LoreUnlockKind.SpectatorMilestone => item.UnlockId is null
                || !KnownSpectatorMilestones.Contains(item.UnlockId, StringComparer.Ordinal)
                || item.UnlockThreshold != 0,
            LoreUnlockKind.LocalReplayCount => item.UnlockId is not null
                || item.UnlockThreshold is < 1 or > 100,
            _ => true,
        });
        var unsafeCriticalEntries = entries.Count(item => item.SchemaVersion != SchemaVersion
            || !Enum.IsDefined(item.Depth)
            || !Enum.IsDefined(item.Kind)
            || !Enum.IsDefined(item.CanonTier)
            || string.IsNullOrWhiteSpace(item.Id)
            || item.RequiredForPlay
            || item.ActiveRunInterruptible
            || item.AwardsProgression
            || (item.StationId is not null && !knownStations.Contains(item.StationId))
            || (item.SpeakerId is not null
                && !KnownSpeakerIds.Contains(item.SpeakerId, StringComparer.Ordinal)));
        var surfaceStations = entries.Where(item => item.Depth == LoreDepth.Surface)
            .SelectMany(item => item.EntityIds)
            .Where(id => id.StartsWith("station:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var surfaceRivals = entries.Where(item => item.Depth == LoreDepth.Surface)
            .SelectMany(item => item.EntityIds)
            .Where(id => id.StartsWith("rival:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var surfaceMutations = entries.Where(item => item.Depth == LoreDepth.Surface)
            .SelectMany(item => item.EntityIds)
            .Where(id => id.StartsWith("mutation:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var discoverableKinds = entries.Where(item => item.Depth == LoreDepth.Discoverable)
            .Select(item => item.Kind)
            .Distinct()
            .Count();
        var archiveKinds = entries.Where(item => item.Depth == LoreDepth.Archive)
            .Select(item => item.Kind)
            .Distinct()
            .Count();
        var surfaceCount = entries.Count(item => item.Depth == LoreDepth.Surface);
        var discoverableCount = entries.Count(item => item.Depth == LoreDepth.Discoverable);
        var archiveCount = entries.Count(item => item.Depth == LoreDepth.Archive);
        var passed = entries.Count == 41
            && surfaceCount == 19
            && discoverableCount == 14
            && archiveCount == 8
            && duplicateIds == 0
            && missingCopyIds == 0
            && unknownEntities == 0
            && brokenContinuity == 0
            && invalidUnlocks == 0
            && unsafeCriticalEntries == 0
            && surfaceStations == 8
            && surfaceRivals == 10
            && surfaceMutations == 9
            && discoverableKinds == 6
            && archiveKinds == 4;
        return new LoreCatalogValidation(
            passed,
            entries.Count,
            surfaceCount,
            discoverableCount,
            archiveCount,
            duplicateIds,
            missingCopyIds,
            unknownEntities,
            brokenContinuity,
            invalidUnlocks,
            unsafeCriticalEntries,
            surfaceStations,
            surfaceRivals,
            surfaceMutations,
            discoverableKinds,
            archiveKinds);
    }

    private static LoreEntry Surface(
        string id,
        LoreContentKind kind,
        string? stationId = null,
        IReadOnlyList<string>? entities = null) => Entry(
            id,
            LoreDepth.Surface,
            kind,
            LoreCanonTier.Foundation,
            LoreUnlockKind.Always,
            stationId: stationId,
            entities: entities);

    private static LoreEntry Discoverable(
        string id,
        LoreContentKind kind,
        LoreUnlockKind unlockKind,
        string? unlockId = null,
        int threshold = 0,
        string? speakerId = null,
        string? stationId = null,
        IReadOnlyList<string>? entities = null,
        IReadOnlyList<string>? continuity = null) => Entry(
            id,
            LoreDepth.Discoverable,
            kind,
            LoreCanonTier.Contextual,
            unlockKind,
            unlockId,
            threshold,
            speakerId,
            stationId,
            entities,
            continuity);

    private static LoreEntry Archive(
        string id,
        LoreContentKind kind,
        LoreCanonTier canonTier,
        LoreUnlockKind unlockKind,
        string? unlockId = null,
        int threshold = 0,
        string? speakerId = null,
        string? stationId = null,
        IReadOnlyList<string>? entities = null,
        IReadOnlyList<string>? continuity = null) => Entry(
            id,
            LoreDepth.Archive,
            kind,
            canonTier,
            unlockKind,
            unlockId,
            threshold,
            speakerId,
            stationId,
            entities,
            continuity);

    private static LoreEntry Entry(
        string id,
        LoreDepth depth,
        LoreContentKind kind,
        LoreCanonTier canonTier,
        LoreUnlockKind unlockKind,
        string? unlockId = null,
        int threshold = 0,
        string? speakerId = null,
        string? stationId = null,
        IReadOnlyList<string>? entities = null,
        IReadOnlyList<string>? continuity = null) => new(
            SchemaVersion,
            id,
            depth,
            kind,
            canonTier,
            $"lore.entry.{id}.title",
            $"lore.entry.{id}.body",
            speakerId,
            stationId,
            unlockKind,
            unlockId,
            threshold,
            entities ?? Array.Empty<string>(),
            continuity ?? Array.Empty<string>(),
            RequiredForPlay: false,
            ActiveRunInterruptible: false,
            AwardsProgression: false);

    private static readonly IReadOnlyList<string> KnownStationIds =
    [
        "flow_signal",
        "chaos_theory",
        "global_coil",
        "ourotron",
        "the_pit",
        "the_bureau",
        "the_strike",
        "underground_scales",
    ];

    private static readonly IReadOnlyList<string> KnownSpeakerIds =
    [
        "cadence-vale",
        "dr-sibilant",
        "sol-coil",
        "vektor-null",
        "dj-rattlebyte",
        "anchor-seven",
        "rivet",
        "molt-one",
        "archive-collective",
    ];

    private static readonly IReadOnlyList<string> KnownSpectatorMilestones =
    [
        "first-broadcast",
        "match-win",
        "score-100",
        "survive-500",
        "combo-5",
        "power-route",
        "collision-save",
    ];

    private static readonly IReadOnlyList<string> KnownEntityIds =
    [
        .. KnownStationIds.Select(id => "station:" + id),
        .. AiPersonalityCatalog.BuiltIn.Select(item => "rival:" + item.Id),
        "host:cadence-vale",
        "host:dr-sibilant",
        "host:sol-coil",
        "host:vektor-null",
        "host:dj-rattlebyte",
        "host:anchor-seven",
        "host:rivet",
        "host:molt-one",
        "mutation:shield",
        "mutation:phase-shift",
        "mutation:gluttony",
        "mutation:last-stand",
        "mutation:bait",
        "mutation:segment-detach",
        "mutation:slow-mo",
        "mutation:boost",
        "mutation:magnet",
        "concept:echo",
        "event:first-carrier",
        "event:great-molt",
        "event:coil-accord",
        "mystery:ninth-frequency",
    ];
}
