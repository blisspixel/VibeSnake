namespace VibeSnake.Rules;

public enum PowerTacticalFamily : byte
{
    Protection = 1,
    Tempo = 2,
    Harvest = 3,
    Geometry = 4,
}

public enum PowerStatePresentation : byte
{
    Timed = 1,
    Held = 2,
    ImmediateWithTimedHazard = 3,
}

public enum PowerLifecycleStage : byte
{
    Offered = 1,
    DetourObserved = 2,
    Collected = 3,
    Activated = 4,
    Expired = 5,
    Consumed = 6,
    Saved = 7,
    DeathAdjacent = 8,
}

public sealed record PowerDecisionDefinition(
    PowerKind Kind,
    string Id,
    PowerTacticalFamily Family,
    PowerStatePresentation StatePresentation,
    string IntendedQuestion,
    string OfferTelegraph);

public sealed record PowerSynergyScenario(
    string Id,
    IReadOnlyList<PowerKind> Powers,
    string Setup,
    string ExpectedInteraction);

public sealed record MutationForkExperiment(
    string Id,
    bool EnabledByDefault,
    int ChoiceCount,
    bool WithdrawUnchosenOffer,
    string EvidenceStatus);

public sealed record MutationForkResolution(
    PowerKind Collected,
    PowerKind Withdrawn);

public sealed record MutationForkOffer(
    PowerKind First,
    PowerKind Second)
{
    public MutationForkResolution Resolve(PowerKind collected)
    {
        if (collected == First)
        {
            return new MutationForkResolution(First, Second);
        }

        if (collected == Second)
        {
            return new MutationForkResolution(Second, First);
        }

        throw new ArgumentOutOfRangeException(
            nameof(collected),
            collected,
            "The collected power must be one of the fork offers.");
    }
}

/// <summary>
/// Stable decision contract for the complete nine-power portfolio. Offer
/// filtering is rules-owned because it changes scored random outcomes.
/// </summary>
public static class PowerDecisionCatalog
{
    public const string PolicyId = "power-decisions-v1";

    private static readonly IReadOnlyList<PowerDecisionDefinition> Entries =
    [
        Entry(PowerKind.Shield, "shield", PowerTacticalFamily.Protection, PowerStatePresentation.Timed, "How aggressively can I route while one collision block is ready?", "ONE COLLISION BLOCK"),
        Entry(PowerKind.PhaseShift, "phase-shift", PowerTacticalFamily.Protection, PowerStatePresentation.Timed, "Can I route through a dangerous body knot before the timer ends?", "BODY PASS WINDOW"),
        Entry(PowerKind.LastStand, "last-stand", PowerTacticalFamily.Protection, PowerStatePresentation.Held, "Should I preserve an automatic rescue for a high-value route?", "HELD AUTO-RESCUE"),
        Entry(PowerKind.SlowMo, "slow-mo", PowerTacticalFamily.Tempo, PowerStatePresentation.Timed, "Do I want a longer control window?", "HALF STEP RATE"),
        Entry(PowerKind.Boost, "boost", PowerTacticalFamily.Tempo, PowerStatePresentation.Timed, "Do I want speed pressure for a more aggressive conversion?", "DOUBLE STEP RATE"),
        Entry(PowerKind.Magnet, "magnet", PowerTacticalFamily.Harvest, PowerStatePresentation.Timed, "Can food pull shorten a safe recovery route?", "FOOD PULL"),
        Entry(PowerKind.Bait, "bait", PowerTacticalFamily.Harvest, PowerStatePresentation.Held, "Where should I bias the next food sequence?", "NEXT FOOD MARK"),
        Entry(PowerKind.Gluttony, "gluttony", PowerTacticalFamily.Harvest, PowerStatePresentation.Timed, "Is score and hunger recovery worth giving up growth?", "NO-GROWTH SCORE"),
        Entry(PowerKind.SegmentDetach, "segment-detach", PowerTacticalFamily.Geometry, PowerStatePresentation.ImmediateWithTimedHazard, "Is immediate body relief worth temporary obstacles?", "TAIL HAZARD TRADE"),
    ];

    private static readonly IReadOnlyList<PowerSynergyScenario> Scenarios =
    [
        Scenario("boost-phase-shift", [PowerKind.Boost, PowerKind.PhaseShift], "Boost active before entering a body knot", "Phase Shift preserves an explicit collision window at double cadence."),
        Scenario("slow-mo-magnet", [PowerKind.SlowMo, PowerKind.Magnet], "Food outside the direct path", "Slow-Mo provides deliberate routing while Magnet pulls food."),
        Scenario("bait-boost", [PowerKind.Bait, PowerKind.Boost], "Bait marks the next spawn before Boost", "The preview makes the fast conversion plan learnable."),
        Scenario("gluttony-magnet", [PowerKind.Gluttony, PowerKind.Magnet], "Food is pulled into the next route", "Collection restores score and hunger without body growth."),
        Scenario("segment-detach-protection", [PowerKind.SegmentDetach, PowerKind.Shield], "Detach creates temporary obstacles while protection is active", "Protection covers a readable geometry trade without hiding obstacle expiry."),
        Scenario("last-stand-long-combo", [PowerKind.LastStand], "Last Stand is held after a combo of at least ten", "A fatal event consumes the held rescue and exposes recovery immunity."),
    ];

    private static readonly IReadOnlyList<PowerLifecycleStage> Lifecycle =
        Enum.GetValues<PowerLifecycleStage>();

    public static IReadOnlyList<PowerDecisionDefinition> All => Entries;

    public static IReadOnlyList<PowerSynergyScenario> RequiredSynergyScenarios => Scenarios;

    public static IReadOnlyList<PowerLifecycleStage> RequiredLifecycleStages => Lifecycle;

    public static MutationForkExperiment MutationFork { get; } = new(
        "mutation-fork-v1",
        EnabledByDefault: false,
        ChoiceCount: 2,
        WithdrawUnchosenOffer: true,
        EvidenceStatus: "automated-prototype-human-unverified");

    public static PowerDecisionDefinition Get(PowerKind kind) =>
        Entries.FirstOrDefault(entry => entry.Kind == kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown power kind.");

    /// <summary>
    /// Returns a stable enum-ordered offer set. Protection resources are not
    /// offered beside another protection resource, opposing tempo effects are
    /// not offered together, and exact active effects are never repeated.
    /// Harvest cross-kind combinations remain eligible by design.
    /// </summary>
    public static IReadOnlyList<PowerKind> EligibleOffers(
        IEnumerable<PowerKind> activePowers)
    {
        ArgumentNullException.ThrowIfNull(activePowers);
        var active = activePowers.ToHashSet();
        if (active.Any(kind => !Enum.IsDefined(kind)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(activePowers),
                "Active powers must use defined power kinds.");
        }

        var activeFamilies = active
            .Select(kind => Get(kind).Family)
            .ToHashSet();
        return Entries
            .Where(entry => !active.Contains(entry.Kind))
            .Where(entry => entry.Family switch
            {
                PowerTacticalFamily.Protection =>
                    !activeFamilies.Contains(PowerTacticalFamily.Protection),
                PowerTacticalFamily.Tempo =>
                    !activeFamilies.Contains(PowerTacticalFamily.Tempo),
                PowerTacticalFamily.Harvest or PowerTacticalFamily.Geometry => true,
                _ => throw new InvalidOperationException("Unknown tactical family."),
            })
            .Select(entry => entry.Kind)
            .ToArray();
    }

    public static bool IsEligibleOffer(
        PowerKind candidate,
        IEnumerable<PowerKind> activePowers)
    {
        _ = Get(candidate);
        return EligibleOffers(activePowers).Contains(candidate);
    }

    /// <summary>
    /// Pure default-off experiment prototype. The caller supplies deterministic
    /// rolls so no experimental random state enters production unless the flag
    /// is explicitly enabled. Resolving either choice withdraws the other.
    /// </summary>
    public static MutationForkOffer? CreateMutationFork(
        bool experimentEnabled,
        IEnumerable<PowerKind> activePowers,
        int firstRoll,
        int secondRoll)
    {
        if (!experimentEnabled)
        {
            return null;
        }

        if (firstRoll < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstRoll));
        }

        if (secondRoll < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondRoll));
        }

        var eligible = EligibleOffers(activePowers);
        if (eligible.Count < MutationFork.ChoiceCount)
        {
            return null;
        }

        var firstIndex = firstRoll % eligible.Count;
        var first = eligible[firstIndex];
        var remaining = eligible.Where((_, index) => index != firstIndex).ToArray();
        var second = remaining[secondRoll % remaining.Length];
        return new MutationForkOffer(first, second);
    }

    private static PowerDecisionDefinition Entry(
        PowerKind kind,
        string id,
        PowerTacticalFamily family,
        PowerStatePresentation statePresentation,
        string intendedQuestion,
        string offerTelegraph) =>
        new(kind, id, family, statePresentation, intendedQuestion, offerTelegraph);

    private static PowerSynergyScenario Scenario(
        string id,
        IReadOnlyList<PowerKind> powers,
        string setup,
        string expectedInteraction) =>
        new(id, powers, setup, expectedInteraction);
}
