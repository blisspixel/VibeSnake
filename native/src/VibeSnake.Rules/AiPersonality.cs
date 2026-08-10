namespace VibeSnake.Rules;

/// <summary>Stable behavior knobs consumed by the native AI controller.</summary>
public enum AiPersonalityTrait : byte
{
    Aggression = 0,
    RiskTolerance = 1,
    Patience = 2,
    Greed = 3,
    Chaos = 4,
    PowerUpPriority = 5,
}

public readonly record struct AiDisplayColor(byte Red, byte Green, byte Blue);

public enum AiPersonalityContentKind : byte
{
    BuiltIn = 0,
    Custom = 1,
}

public sealed record AiPersonalityProfile(
    AiPersonality Personality,
    AiPersonalityContentKind ContentKind,
    string StatusLabel,
    bool OfficialLeagueQualified);

public enum AiBehaviorMetric : byte
{
    ScoreP50 = 0,
    SurvivalP50 = 1,
    FoodEfficiencyPerThousandSteps = 2,
    PowerPreferenceBasisPoints = 3,
    RiskExposureBasisPoints = 4,
    DeadEndBasisPoints = 5,
    RouteEfficiencyBasisPoints = 6,
}

public sealed record AiBehaviorClaim(
    string PersonalityId,
    AiBehaviorMetric Metric,
    int InclusiveMinimum,
    int InclusiveMaximum,
    string PlayerFacingMeaning);

/// <summary>
/// An engine-independent AI definition. Trait values use an inclusive 0 to 100
/// scale so decisions do not depend on platform floating-point behavior.
/// </summary>
public sealed record AiPersonality(
    string Id,
    string Name,
    string Description,
    int Aggression,
    int RiskTolerance,
    int Patience,
    int Greed,
    int Chaos,
    int PowerUpPriority,
    AiDisplayColor Color)
{
    public int GetTrait(AiPersonalityTrait trait) => trait switch
    {
        AiPersonalityTrait.Aggression => Aggression,
        AiPersonalityTrait.RiskTolerance => RiskTolerance,
        AiPersonalityTrait.Patience => Patience,
        AiPersonalityTrait.Greed => Greed,
        AiPersonalityTrait.Chaos => Chaos,
        AiPersonalityTrait.PowerUpPriority => PowerUpPriority,
        _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, "Unknown AI trait."),
    };

    public AiPersonality WithTrait(AiPersonalityTrait trait, int value)
    {
        ValidateTrait(value, nameof(value));
        return trait switch
        {
            AiPersonalityTrait.Aggression => this with { Aggression = value },
            AiPersonalityTrait.RiskTolerance => this with { RiskTolerance = value },
            AiPersonalityTrait.Patience => this with { Patience = value },
            AiPersonalityTrait.Greed => this with { Greed = value },
            AiPersonalityTrait.Chaos => this with { Chaos = value },
            AiPersonalityTrait.PowerUpPriority => this with { PowerUpPriority = value },
            _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, "Unknown AI trait."),
        };
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);
        if (!IsValidId(Id))
        {
            throw new ArgumentException(
                "AI personality IDs must be 1 to 48 lowercase ASCII letters, digits, underscores, or hyphens and must start with a letter or digit.",
                nameof(Id));
        }

        if (Name.Length > 48)
        {
            throw new ArgumentException("AI personality names cannot exceed 48 characters.", nameof(Name));
        }

        if (Description.Length > 192)
        {
            throw new ArgumentException(
                "AI personality descriptions cannot exceed 192 characters.",
                nameof(Description));
        }

        ValidateTrait(Aggression, nameof(Aggression));
        ValidateTrait(RiskTolerance, nameof(RiskTolerance));
        ValidateTrait(Patience, nameof(Patience));
        ValidateTrait(Greed, nameof(Greed));
        ValidateTrait(Chaos, nameof(Chaos));
        ValidateTrait(PowerUpPriority, nameof(PowerUpPriority));
    }

    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 48 || !IsLowerLetterOrDigit(id[0]))
        {
            return false;
        }

        return id.All(character =>
            IsLowerLetterOrDigit(character) || character is '_' or '-');
    }

    private static bool IsLowerLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static void ValidateTrait(int value, string parameterName)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "AI trait values must be between 0 and 100 inclusive.");
        }
    }
}

/// <summary>The ten built-in personalities inherited from the frozen oracle.</summary>
public static class AiPersonalityCatalog
{
    public const string BuiltInStatusLabel = "BUILT-IN / LEAGUE-QUALIFIED";
    public const string CustomStatusLabel = "CUSTOM / UNOFFICIAL";

    public static IReadOnlyList<AiPersonality> BuiltIn { get; } =
    [
        new(
            "speed_demon",
            "Redline",
            "Takes aggressive target lines, tolerates nearby hazards, and breaks course unpredictably.",
            95, 80, 20, 90, 30, 40,
            new AiDisplayColor(255, 50, 50)),
        new(
            "coward",
            "Shelter Coil",
            "Protects onward choices, avoids hazards, and keeps deliberate routes.",
            10, 10, 95, 20, 5, 10,
            new AiDisplayColor(150, 150, 255)),
        new(
            "greedy",
            "Crownchaser",
            "Weights food progress heavily and accepts measured risk for points.",
            70, 60, 40, 100, 20, 30,
            new AiDisplayColor(255, 215, 0)),
        new(
            "power_hunter",
            "Mutagenist",
            "Prioritizes visible powers and accepts tighter routes to reach them.",
            80, 70, 50, 40, 10, 100,
            new AiDisplayColor(255, 0, 255)),
        new(
            "drunk",
            "Noise Coil",
            "Uses frequent bounded chaos while respecting legal movement.",
            50, 50, 10, 50, 65, 50,
            new AiDisplayColor(255, 100, 200)),
        new(
            "optimal",
            "The Proof",
            "Favors efficient target progress, open exits, and repeatable low-chaos routes.",
            60, 40, 90, 60, 0, 70,
            new AiDisplayColor(100, 255, 255)),
        new(
            "yolo",
            "Edge Prophet",
            "Accepts the highest hazard exposure and aggressively diverts to visible powers.",
            100, 100, 0, 80, 50, 90,
            new AiDisplayColor(255, 140, 0)),
        new(
            "balanced",
            "Meanline",
            "Balances survival, food, and visible powers without one absolute preference.",
            50, 50, 50, 50, 10, 50,
            new AiDisplayColor(100, 255, 100)),
        new(
            "wall_hugger",
            "Rimkeeper",
            "Keeps guarded, continuous routes and makes limited power detours.",
            30, 20, 70, 30, 20, 20,
            new AiDisplayColor(139, 69, 19)),
        new(
            "zen_master",
            "Stillwater",
            "Preserves options, waits for clean openings, and avoids panic turns.",
            30, 30, 100, 30, 0, 60,
            new AiDisplayColor(200, 255, 200)),
    ];

    public static IReadOnlyList<AiPersonalityProfile> BuiltInProfiles { get; } =
        BuiltIn
            .Select(personality => new AiPersonalityProfile(
                personality,
                AiPersonalityContentKind.BuiltIn,
                BuiltInStatusLabel,
                OfficialLeagueQualified: true))
            .ToArray();

    /// <summary>
    /// Reviewed claims over the twelve-seed V080 league. Ranges are intentionally
    /// wider than one observed run so the gate catches semantic drift without
    /// pretending these AI samples are human balance targets.
    /// </summary>
    public static IReadOnlyList<AiBehaviorClaim> BehaviorClaims { get; } =
    [
        new("speed_demon", AiBehaviorMetric.RiskExposureBasisPoints, 1_500, 3_500, "accepts narrow recovery windows"),
        new("coward", AiBehaviorMetric.RiskExposureBasisPoints, 0, 200, "avoids crowded routes"),
        new("greedy", AiBehaviorMetric.FoodEfficiencyPerThousandSteps, 20, 40, "weights food progress heavily"),
        new("power_hunter", AiBehaviorMetric.PowerPreferenceBasisPoints, 7_500, 9_500, "prioritizes visible powers"),
        new("drunk", AiBehaviorMetric.RouteEfficiencyBasisPoints, 5_000, 7_000, "uses bounded unpredictability"),
        new("optimal", AiBehaviorMetric.RouteEfficiencyBasisPoints, 9_000, 10_000, "favors efficient target progress"),
        new("yolo", AiBehaviorMetric.RiskExposureBasisPoints, 2_000, 4_000, "accepts the highest hazard exposure"),
        new("balanced", AiBehaviorMetric.PowerPreferenceBasisPoints, 4_000, 6_000, "has no absolute food or power preference"),
        new("wall_hugger", AiBehaviorMetric.PowerPreferenceBasisPoints, 2_500, 4_500, "makes limited power detours"),
        new("zen_master", AiBehaviorMetric.DeadEndBasisPoints, 0, 25, "preserves options and waits for clean openings"),
    ];

    public static AiPersonality GetBuiltIn(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return BuiltIn.SingleOrDefault(personality =>
                string.Equals(personality.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException("The built-in AI personality is unknown.", nameof(id));
    }
}
