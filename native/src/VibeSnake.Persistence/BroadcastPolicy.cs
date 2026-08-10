using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public enum BroadcastStationApproval : byte
{
    PlannedUnapproved = 0,
    ApprovedForPack = 1,
}

public enum BroadcastBoundary : byte
{
    RunStart = 0,
    MajorMilestone = 1,
    Recovery = 2,
    PostRun = 3,
    OrdinaryCombo = 4,
    CriticalWarning = 5,
}

public enum BroadcastDecisionCode : byte
{
    SegmentGranted = 0,
    StationUnknown = 1,
    BoundaryNotAllowed = 2,
    CriticalCueProtected = 3,
    CooldownActive = 4,
    FatigueLimitReached = 5,
}

public sealed record BroadcastStationIdentity(
    string StationId,
    string StationName,
    string MusicalInclusionRule,
    string HostName,
    string HostPerspective,
    string VisualIdentity,
    IReadOnlyList<string> ShortIds,
    IReadOnlyList<string> CaptionCopyIds,
    IReadOnlyList<string> TransitionStingers,
    string CoilRelationship,
    bool SupportsAdaptiveLayers,
    BroadcastStationApproval Approval);

public sealed record BroadcastRequest(
    string StationId,
    BroadcastBoundary Boundary,
    int PresentationStep,
    bool CriticalCueActive,
    bool AudioAvailable);

public sealed record BroadcastDecision(
    BroadcastDecisionCode Code,
    string StationId,
    BroadcastBoundary Boundary,
    string? SegmentId,
    string? CaptionCopyId,
    bool AudioRequested,
    bool TrackContinues,
    bool OptionalBroadcastInterrupted,
    bool AdaptiveLayerRequested,
    float MusicDuckDecibels,
    int BroadcastPriority,
    int CriticalCuePriority,
    int SegmentsUsed,
    string StatusMessage);

/// <summary>
/// Authored identity metadata for the eight planned SBN stations. Approval is
/// explicit: no entry becomes shipped audio merely by existing in this catalog.
/// </summary>
public static class BroadcastStationCatalog
{
    private static readonly BroadcastStationIdentity[] Entries =
    [
        Station(
            "flow_signal",
            "The Flow Signal",
            "Sustained chill, focus, liquid rhythm, and spacious arrangements that never hide warnings.",
            "Cadence Vale",
            "Calm and exact; notices rhythm, restraint, and recovery without scolding failure.",
            "Seafoam carrier rings on a deep green field",
            ["FLOW SIGNAL. HOLD THE LINE.", "RHYTHMOS CARRIER LOCKED.", "BREATHE. ROUTE. CONTINUE."],
            "The Ministry of Focus treats sustained flow as civic practice."),
        Station(
            "chaos_theory",
            "Chaos Theory",
            "Jazz, bossa, fusion, odd accents, and controlled improvisation with a stable rhythmic floor.",
            "Dr. Sibilant",
            "Curious and playfully analytical; frames risky routes as composition, never unfair randomness.",
            "Amber probability arcs over midnight blue",
            ["CHAOS THEORY. VARIABLES LIVE.", "IMPROVISATION ORDER ON AIR.", "KEEP ONE EXIT UNWRITTEN."],
            "The Improvisational Order argues that controlled uncertainty prevents cultural stagnation."),
        Station(
            "global_coil",
            "The Global Coil",
            "Communal world rhythm and warm dance forms with culturally reviewed context and no novelty framing.",
            "Sol Coil",
            "Warm and connective; relates a local run to a wider circuit without flattening cultural difference.",
            "Interlocking solar bands in coral and gold",
            ["GLOBAL COIL. SIGNALS TOGETHER.", "THE PLANETARY RELAY IS OPEN.", "ONE CIRCUIT. MANY RHYTHMS."],
            "Planetary relay collectives treat shared rhythm as warmth across difference."),
        Station(
            "ourotron",
            "Ourotron",
            "Original synthwave, outrun, and retro-future forms with no artist, franchise, or nostalgia imitation.",
            "Vektor Null",
            "Romantic and precise; speaks of lost futures as archived places rather than borrowed properties.",
            "Magenta horizon grid with a closed cyan loop",
            ["OUROTRON. THE FUTURE REMEMBERS.", "ARCHIVE LOOP RESTORED.", "TOMORROW SHEDS BACKWARD."],
            "The Order of Retrowave preserves imagined futures as inherited memory."),
        Station(
            "the_pit",
            "The Pit",
            "Bass, drum and bass, and trap built for pressure, with critical cues always above drops and sub energy.",
            "DJ Rattlebyte",
            "Competitive and ecstatic; respects bold recovery more than empty aggression.",
            "Hazard orange fangs around a black pressure core",
            ["THE PIT. PRESSURE HAS A VOICE.", "RATTLEBYTE IN THE RED.", "RECOVER LOUDER THAN YOU FALL."],
            "The Venom Syndicate believes controlled pressure reveals a signal's true shape."),
        Station(
            "the_bureau",
            "The Bureau",
            "Dry civic jazz and talk framing with exact facts, restrained beds, and no false gameplay authority.",
            "Anchor Seven",
            "Deadpan institutional news whose precision exposes bureaucracy without misstating controls or safety.",
            "Ivory forms, red stamps, and rigid information columns",
            ["THE BUREAU. YOUR SIGNAL IS FILED.", "ANCHOR SEVEN. FACTS REMAIN PENDING.", "COMFORT NOTICE RECEIVED."],
            "The Bureau of Information Comfort makes confusion feel official, never mechanically true."),
        Station(
            "the_strike",
            "The Strike",
            "Original rock, metal, and alternative forms with decisive rhythm and no contempt-driven aggression.",
            "Rivet",
            "Direct and solidaristic; honors persistence, clear routes, and refusal to coast.",
            "Molten red chevrons breaking a steel ring",
            ["THE STRIKE. BREAK THE GIVEN RHYTHM.", "RIVET ON THE MOLTEN LINE.", "MAKE THE NEXT ROUTE YOURS."],
            "The Molten Core Collective sees a clean strike as the start of a shared rhythm."),
        Station(
            "underground_scales",
            "Underground Scales",
            "Hip-hop, beats, and electronic forms grounded in reviewed authorship, local texture, and no stereotype borrowing.",
            "Molt One",
            "Intimate and inventive; treats each shed and route as an authored signature.",
            "Violet handbills, thermal ink, and layered neighborhood tags",
            ["UNDERGROUND SCALES. LEAVE A MARK.", "MOLT ONE BELOW THE CARRIER.", "THE GAPS ARE PART OF THE BEAT."],
            "Independent neighborhood relays protect identity in the gaps between official signals."),
    ];

    public static IReadOnlyList<BroadcastStationIdentity> All => Entries;

    public static BroadcastStationIdentity? Find(string stationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        return Entries.FirstOrDefault(entry => entry.StationId == stationId);
    }

    private static BroadcastStationIdentity Station(
        string id,
        string name,
        string inclusion,
        string host,
        string perspective,
        string visual,
        IReadOnlyList<string> shortIds,
        string coilRelationship) => new(
            id,
            name,
            inclusion,
            host,
            perspective,
            visual,
            shortIds,
            Enumerable.Range(1, shortIds.Count)
                .Select(index =>
                    $"broadcast.station.{id.Replace('_', '-')}.id.{index}")
                .ToArray(),
            [
                id + ".flow",
                id + ".heat",
                id + ".overdrive",
                id + ".transcendent",
            ],
            coilRelationship,
            SupportsAdaptiveLayers: false,
            Approval: BroadcastStationApproval.PlannedUnapproved);
}

/// <summary>
/// Playback-free host/lore scheduling. It cannot seek, replace, or advance the
/// current track. Critical cues always preempt optional broadcast material.
/// </summary>
public sealed class BroadcastPolicy
{
    public const int SegmentCooldownSteps = 100;
    public const int MaximumSegmentsPerRun = 8;
    public const int BroadcastPriority = 30;
    public const int CriticalCuePriority = 100;

    private readonly Pcg32 _random;
    private readonly Dictionary<string, Queue<int>> _idBags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _lastIdIndexByStation = new(StringComparer.Ordinal);
    private int? _lastSegmentStep;
    private int _lastRequestStep = -1;
    private int _segmentsUsed;

    public BroadcastPolicy(Pcg32 broadcastRandom)
    {
        ArgumentNullException.ThrowIfNull(broadcastRandom);
        _random = broadcastRandom;
    }

    public ulong RandomState => _random.State;

    public int SegmentsUsed => _segmentsUsed;

    public BroadcastDecision Evaluate(BroadcastRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StationId);
        if (request.PresentationStep < 0 || request.PresentationStep < _lastRequestStep)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Broadcast presentation steps must be nonnegative and monotonic.");
        }

        _lastRequestStep = request.PresentationStep;
        var station = BroadcastStationCatalog.Find(request.StationId);
        if (station is null)
        {
            return Suppressed(
                request,
                BroadcastDecisionCode.StationUnknown,
                "No broadcast identity exists for this station.");
        }

        if (request.Boundary == BroadcastBoundary.CriticalWarning
            || request.CriticalCueActive)
        {
            return Suppressed(
                request,
                BroadcastDecisionCode.CriticalCueProtected,
                "Critical gameplay cue owns the channel.",
                interrupted: true,
                duck: -9.0f);
        }

        if (request.Boundary == BroadcastBoundary.OrdinaryCombo)
        {
            return Suppressed(
                request,
                BroadcastDecisionCode.BoundaryNotAllowed,
                "Ordinary combo changes keep the current track and host channel continuous.");
        }

        if (_segmentsUsed >= MaximumSegmentsPerRun)
        {
            return Suppressed(
                request,
                BroadcastDecisionCode.FatigueLimitReached,
                "Per-run broadcast fatigue limit reached.");
        }

        if (_lastSegmentStep is { } last
            && request.PresentationStep - last < SegmentCooldownSteps)
        {
            return Suppressed(
                request,
                BroadcastDecisionCode.CooldownActive,
                "Broadcast segment cooldown is active.");
        }

        var selectedIndex = SelectShortIdIndex(station);
        var segmentId = $"{station.StationId}.id.{selectedIndex + 1}";
        var captionCopyId = station.CaptionCopyIds[selectedIndex];
        _segmentsUsed++;
        _lastSegmentStep = request.PresentationStep;
        var duck = request.Boundary switch
        {
            BroadcastBoundary.Recovery => -6.0f,
            BroadcastBoundary.PostRun => 0.0f,
            _ => -3.0f,
        };
        return new BroadcastDecision(
            BroadcastDecisionCode.SegmentGranted,
            station.StationId,
            request.Boundary,
            segmentId,
            captionCopyId,
            AudioRequested: request.AudioAvailable
                && station.Approval == BroadcastStationApproval.ApprovedForPack,
            TrackContinues: true,
            OptionalBroadcastInterrupted: false,
            AdaptiveLayerRequested: station.SupportsAdaptiveLayers
                && request.Boundary == BroadcastBoundary.MajorMilestone,
            MusicDuckDecibels: duck,
            BroadcastPriority: BroadcastPriority,
            CriticalCuePriority: CriticalCuePriority,
            SegmentsUsed: _segmentsUsed,
            StatusMessage: request.AudioAvailable
                && station.Approval == BroadcastStationApproval.ApprovedForPack
                    ? "Approved broadcast segment scheduled with caption."
                    : "Caption fallback scheduled; no approved host audio requested.");
    }

    public void ResetRun()
    {
        _lastSegmentStep = null;
        _lastRequestStep = -1;
        _segmentsUsed = 0;
        _idBags.Clear();
        _lastIdIndexByStation.Clear();
    }

    private int SelectShortIdIndex(BroadcastStationIdentity station)
    {
        if (!_idBags.TryGetValue(station.StationId, out var bag) || bag.Count == 0)
        {
            _lastIdIndexByStation.TryGetValue(station.StationId, out var lastIndex);
            bag = RefillBag(
                station.ShortIds.Count,
                _lastIdIndexByStation.ContainsKey(station.StationId) ? lastIndex : null);
            _idBags[station.StationId] = bag;
        }

        var selected = bag.Dequeue();
        _lastIdIndexByStation[station.StationId] = selected;
        return selected;
    }

    private Queue<int> RefillBag(int count, int? previousIndex)
    {
        var indices = Enumerable.Range(0, count).ToArray();
        for (var index = indices.Length - 1; index > 0; index--)
        {
            var swapIndex = _random.NextInt(index + 1);
            (indices[index], indices[swapIndex]) = (indices[swapIndex], indices[index]);
        }

        if (indices.Length > 1 && indices[0] == previousIndex)
        {
            var replacement = Array.FindIndex(indices, 1, item => item != previousIndex);
            (indices[0], indices[replacement]) = (indices[replacement], indices[0]);
        }

        return new Queue<int>(indices);
    }

    private BroadcastDecision Suppressed(
        BroadcastRequest request,
        BroadcastDecisionCode code,
        string status,
        bool interrupted = false,
        float duck = 0.0f) => new(
            code,
            request.StationId,
            request.Boundary,
            SegmentId: null,
            CaptionCopyId: null,
            AudioRequested: false,
            TrackContinues: true,
            OptionalBroadcastInterrupted: interrupted,
            AdaptiveLayerRequested: false,
            MusicDuckDecibels: duck,
            BroadcastPriority: BroadcastPriority,
            CriticalCuePriority: CriticalCuePriority,
            SegmentsUsed: _segmentsUsed,
            StatusMessage: status);
}
