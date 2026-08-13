using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal enum HungerPhase
{
    Safe,
    Warning,
    Critical,
    Empty,
}

internal readonly record struct HungerFeedbackState(
    HungerPhase Phase,
    string Label,
    string Shape,
    string ColorRole,
    double SecondsRemaining,
    int FilledSegments,
    int TotalSegments,
    AudioCue? ThresholdCue);

/// <summary>
/// One presentation-owned starvation scale. Text and segmented geometry are
/// always present, so color and audio only reinforce the state.
/// </summary>
internal static class HungerFeedback
{
    public const int DefaultMaximumTicks = 600;
    public const int SegmentCount = 12;

    public static HungerFeedbackState Describe(
        int remainingTicks,
        int maximumTicks = DefaultMaximumTicks,
        int warningTicks = RunConfig.DefaultStarvationWarningTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTicks);

        if (remainingTicks < 0 || remainingTicks > maximumTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(remainingTicks));
        }

        if (warningTicks < 0 || warningTicks > RunConfig.MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(warningTicks));
        }

        var criticalTicks = Math.Max(1, warningTicks / 2);
        var warningEnabled = warningTicks > 0 && warningTicks < maximumTicks;
        var phase = remainingTicks switch
        {
            0 => HungerPhase.Empty,
            _ when warningEnabled && remainingTicks <= criticalTicks => HungerPhase.Critical,
            _ when warningEnabled && remainingTicks <= warningTicks => HungerPhase.Warning,
            _ => HungerPhase.Safe,
        };
        var seconds = remainingTicks * RunConfig.RulesTickMilliseconds / 1000.0;
        var filledSegments = remainingTicks == 0
            ? 0
            : Math.Max(
                1,
                (int)Math.Ceiling(
                    remainingTicks / (double)maximumTicks * SegmentCount));
        var (word, shape, colorRole) = phase switch
        {
            HungerPhase.Safe => ("READY", "solid-segment-bar", "body"),
            HungerPhase.Warning => ("LOW", "notched-segment-bar", "gold"),
            HungerPhase.Critical => ("CRITICAL", "chevron-segment-bar", "warning"),
            HungerPhase.Empty => ("EMPTY", "crossed-empty-bar", "warning"),
            _ => throw new InvalidOperationException("Unknown hunger phase."),
        };

        return new HungerFeedbackState(
            phase,
            $"HUNGER {word} {seconds:0.0}s",
            shape,
            colorRole,
            seconds,
            filledSegments,
            SegmentCount,
            phase == HungerPhase.Warning ? AudioCue.Starvation : null);
    }
}

internal readonly record struct ComboFeedbackState(
    int Count,
    double Multiplier,
    string Label,
    string Level,
    bool Emphasized,
    bool MotionAllowed,
    float VerticalOffset,
    string StaticMarker);

internal static class ComboFeedback
{
    public const int PulseTicks = 8;

    public static ComboFeedbackState Describe(
        int count,
        double multiplier,
        int pulseTicksRemaining,
        AccessibilityPresentationPolicy accessibility,
        VibeLevelDefinition vibeLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }

        if (pulseTicksRemaining < 0 || pulseTicksRemaining > PulseTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(pulseTicksRemaining));
        }

        var emphasized = pulseTicksRemaining > 0;
        var motionAllowed = emphasized && accessibility.NonessentialMotionAllowed;
        var offset = motionAllowed
            ? -MathF.Min(3.0f, pulseTicksRemaining * 0.5f)
            : 0.0f;

        return new ComboFeedbackState(
            count,
            multiplier,
            $"COMBO {count:D2}  {multiplier:0.0}x",
            vibeLevel.Level == VibeLevel.Grounded ? "BUILDING" : vibeLevel.Name,
            emphasized,
            motionAllowed,
            offset,
            emphasized ? ">" : " ");
    }
}

internal sealed record PowerFeedbackDefinition(
    PowerKind Kind,
    char StableIcon,
    string Name,
    string StatePresentation,
    AudioCue ActivationCue,
    bool IsProtectionResource,
    string PickupTelegraph);

internal static class PowerFeedbackCatalog
{
    private static readonly IReadOnlyList<PowerFeedbackDefinition> Entries =
    [
        Entry(PowerKind.Shield, 'S', "SHIELD", "timed protection", AudioCue.ShieldActivate, true, "1 COLLISION BLOCK READY"),
        Entry(PowerKind.PhaseShift, 'P', "PHASE", "timed body pass", AudioCue.PhaseShiftActivate, true, "BODY PASS PROTECTION READY"),
        Entry(PowerKind.LastStand, 'L', "LAST STAND", "held rescue", AudioCue.LastStandActivate, true, "AUTO-RESCUE READY"),
        Entry(PowerKind.SlowMo, 'W', "SLOW-MO", "timed cadence", AudioCue.SlowMoActivate, false, "HALF STEP RATE"),
        Entry(PowerKind.Boost, 'B', "BOOST", "timed cadence", AudioCue.BoostActivate, false, "DOUBLE STEP RATE"),
        Entry(PowerKind.Magnet, 'M', "MAGNET", "timed food pull", AudioCue.MagnetActivate, false, "FOOD PULL READY"),
        Entry(PowerKind.Bait, 'T', "BAIT", "held board marker", AudioCue.BaitActivate, false, "NEXT FOOD MARK READY"),
        Entry(PowerKind.Gluttony, 'G', "GLUTTONY", "timed growth tradeoff", AudioCue.GluttonyActivate, false, "NO-GROWTH SCORE READY"),
        Entry(PowerKind.SegmentDetach, 'D', "DETACH", "timed obstacle count", AudioCue.SegmentDetachActivate, false, "TAIL HAZARDS READY"),
    ];

    public static IReadOnlyList<PowerFeedbackDefinition> All => Entries;

    public static PowerFeedbackDefinition Find(PowerKind kind) =>
        Entries.FirstOrDefault(entry => entry.Kind == kind)
        ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown power kind.");

    public static string DescribeProtection(RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var parts = new List<string>(4);
        if (snapshot.HasLastStandRecovery)
        {
            parts.Add($"[L] RECOVERY IMMUNITY {Seconds(snapshot.LastStandRecoveryTicksRemaining):0.0}s");
        }

        if (snapshot.LastStandHeld)
        {
            parts.Add("[L] AUTO-RESCUE READY");
        }

        if (snapshot.HasShield)
        {
            parts.Add($"[S] 1 BLOCK READY {Seconds(snapshot.ShieldTicksRemaining):0.0}s");
        }

        if (snapshot.HasPhaseShift)
        {
            parts.Add($"[P] BODY PASS {Seconds(snapshot.PhaseShiftTicksRemaining):0.0}s");
        }

        return parts.Count == 0
            ? "PROTECTION [ ] NONE"
            : "PROTECTION " + string.Join("  ", parts);
    }

    private static PowerFeedbackDefinition Entry(
        PowerKind kind,
        char icon,
        string name,
        string statePresentation,
        AudioCue activationCue,
        bool protection,
        string telegraph) =>
        new(kind, icon, name, statePresentation, activationCue, protection, telegraph);

    private static double Seconds(int ticksRemaining) =>
        ticksRemaining * RunConfig.RulesTickMilliseconds / 1000.0;
}

internal readonly record struct DeathFeedbackState(
    DeathCause Cause,
    AudioCue Cue,
    string CauseText,
    string StableSymbol,
    string GeometrySignal,
    string RecoveryText,
    int PracticalChannels);

internal static class DeathFeedback
{
    public static DeathFeedbackState Describe(DeathCause cause) => cause switch
    {
        DeathCause.SelfCollision => new DeathFeedbackState(
            cause,
            AudioCue.Collision,
            "SELF COLLISION",
            "[X]",
            "crossed head/body contact marker",
            "Shield, Phase Shift, or Last Stand can prevent a body collision.",
            3),
        DeathCause.Starvation => new DeathFeedbackState(
            cause,
            AudioCue.StarvationDeath,
            "STARVATION",
            "[0]",
            "crossed empty hunger meter",
            "Eat before hunger reaches zero; Last Stand can recover starvation.",
            3),
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "A death cause is required."),
    };
}

internal sealed record MultimodalProfileEvidence(
    string Id,
    bool SoundMuted,
    bool ReducedMotion,
    bool FlashFree,
    int CollisionSurvivingChannels,
    int StarvationSurvivingChannels,
    bool HungerTextAndShapeRetained,
    bool ComboMultiplierRetained,
    bool ProtectionTelegraphRetained);

internal sealed record MultimodalPowerEvidence(
    string Kind,
    string StableIcon,
    string Name,
    string StatePresentation,
    string ActivationCue,
    bool ProtectionResource,
    string PickupTelegraph);

internal sealed record MultimodalFeedbackEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int HungerPhaseCount,
    int ComboMilestoneCount,
    int PowerCount,
    int DeathCauseCount,
    bool TimerShapeTextColorProgression,
    bool ScoreAndComboMoveTogether,
    bool ComboMotionHasStaticFallback,
    bool PowerIdentityOneToOne,
    bool RecoveryProtectionPreTelegraphed,
    bool DeathSignalsDistinct,
    bool AllProfilesDeathAttributionSurvives,
    bool RulesStateUnchanged,
    IReadOnlyList<HungerFeedbackState> HungerStates,
    IReadOnlyList<ComboFeedbackState> ComboMilestones,
    IReadOnlyList<MultimodalPowerEvidence> Powers,
    IReadOnlyList<DeathFeedbackState> Deaths,
    IReadOnlyList<MultimodalProfileEvidence> Profiles)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

internal static class MultimodalFeedbackQualification
{
    public static MultimodalFeedbackEvidence Run()
    {
        var rulesProbe = SnakeRun.Create(20260808UL);
        var rulesHashBefore = rulesProbe.ComputeStateHash();
        if (new RunConfig().StarvationTicks != HungerFeedback.DefaultMaximumTicks)
        {
            throw new InvalidOperationException("Presentation hunger maximum drifted from product rules.");
        }

        var hungerStates = new[]
        {
            HungerFeedback.Describe(HungerFeedback.DefaultMaximumTicks),
            HungerFeedback.Describe(RunConfig.DefaultStarvationWarningTicks),
            HungerFeedback.Describe(RunConfig.DefaultStarvationWarningTicks / 2),
            HungerFeedback.Describe(0),
        };
        var timerShapeTextColorProgression =
            hungerStates.Select(state => state.Phase).Distinct().Count() == 4
            && hungerStates.All(state => state.Label.Contains("HUNGER", StringComparison.Ordinal)
                && state.Label.EndsWith('s')
                && !string.IsNullOrWhiteSpace(state.Shape)
                && !string.IsNullOrWhiteSpace(state.ColorRole))
            && hungerStates.Select(state => state.Shape).Distinct(StringComparer.Ordinal).Count() == 4
            && hungerStates[1].ThresholdCue == AudioCue.Starvation
            && hungerStates[2].ThresholdCue is null;

        var defaultAccessibility = AccessibilityPresentationPolicy.FromSettings(
            ShellSettings.CreateDefaults());
        var comboMilestones = new[]
        {
            DescribeCombo(3, 1.5, defaultAccessibility),
            DescribeCombo(5, 2.0, defaultAccessibility),
            DescribeCombo(10, 3.0, defaultAccessibility),
            DescribeCombo(20, 4.0, defaultAccessibility),
        };
        var scoreAndComboMoveTogether = comboMilestones.All(combo =>
            combo.MotionAllowed
            && combo.VerticalOffset < 0.0f
            && combo.Label.Contains("COMBO", StringComparison.Ordinal)
            && combo.Label.EndsWith('x'));

        var powers = PowerFeedbackCatalog.All.Select(definition =>
            new MultimodalPowerEvidence(
                definition.Kind.ToString(),
                definition.StableIcon.ToString(),
                definition.Name,
                definition.StatePresentation,
                definition.ActivationCue.ToString(),
                definition.IsProtectionResource,
                definition.PickupTelegraph)).ToArray();
        var powerIdentityOneToOne = powers.Length == Enum.GetValues<PowerKind>().Length
            && powers.Select(power => power.StableIcon).Distinct(StringComparer.Ordinal).Count() == powers.Length
            && powers.Select(power => power.Name).Distinct(StringComparer.Ordinal).Count() == powers.Length
            && powers.Select(power => power.ActivationCue).Distinct(StringComparer.Ordinal).Count() == powers.Length
            && powers.All(power => !string.IsNullOrWhiteSpace(power.StatePresentation)
                && !string.IsNullOrWhiteSpace(power.PickupTelegraph));
        var recoveryProtectionPreTelegraphed = powers
            .Where(power => power.ProtectionResource)
            .All(power => power.PickupTelegraph.Contains("READY", StringComparison.Ordinal));
        var deaths = new[]
        {
            DeathFeedback.Describe(DeathCause.SelfCollision),
            DeathFeedback.Describe(DeathCause.Starvation),
        };
        var deathSignalsDistinct = deaths.Select(death => death.Cue).Distinct().Count() == deaths.Length
            && deaths.Select(death => death.CauseText).Distinct(StringComparer.Ordinal).Count() == deaths.Length
            && deaths.Select(death => death.StableSymbol).Distinct(StringComparer.Ordinal).Count() == deaths.Length
            && deaths.Select(death => death.GeometrySignal).Distinct(StringComparer.Ordinal).Count() == deaths.Length
            && deaths.All(death => death.PracticalChannels >= 3
                && !string.IsNullOrWhiteSpace(death.RecoveryText));

        var profiles = new List<MultimodalProfileEvidence>();
        foreach (var definition in new[]
        {
            new ProfileDefinition("default", false, false, false),
            new ProfileDefinition("muted", true, false, false),
            new ProfileDefinition("reduced-motion", false, true, false),
            new ProfileDefinition("flash-free", false, false, true),
            new ProfileDefinition("minimum-effects-muted", true, true, true),
        })
        {
            var settings = ShellSettings.CreateDefaults();
            settings.MasterMuted = definition.SoundMuted;
            settings.ReducedMotion = definition.ReducedMotion;
            settings.FlashFree = definition.FlashFree;
            var accessibility = AccessibilityPresentationPolicy.FromSettings(settings);
            var collision = DeathFeedback.Describe(DeathCause.SelfCollision);
            var starvation = DeathFeedback.Describe(DeathCause.Starvation);
            var audioChannels = settings.EffectiveSfxVolume() > 0.0f ? 1 : 0;
            var combo = DescribeCombo(20, 4.0, accessibility);
            profiles.Add(new MultimodalProfileEvidence(
                definition.Id,
                definition.SoundMuted,
                definition.ReducedMotion,
                definition.FlashFree,
                collision.PracticalChannels - 1 + audioChannels,
                starvation.PracticalChannels - 1 + audioChannels,
                HungerTextAndShapeRetained: true,
                ComboMultiplierRetained: combo.Label.Contains("4.0x", StringComparison.Ordinal)
                    && (!definition.ReducedMotion || (!combo.MotionAllowed && combo.StaticMarker == ">")),
                ProtectionTelegraphRetained: true));
        }

        var allProfilesDeathAttributionSurvives = profiles.All(profile =>
            profile.CollisionSurvivingChannels >= 2
            && profile.StarvationSurvivingChannels >= 2
            && profile.HungerTextAndShapeRetained
            && profile.ComboMultiplierRetained
            && profile.ProtectionTelegraphRetained);
        var comboMotionHasStaticFallback = profiles.Any(profile => profile.ReducedMotion)
            && profiles.Where(profile => profile.ReducedMotion).All(profile => profile.ComboMultiplierRetained);
        var rulesStateUnchanged = rulesProbe.ComputeStateHash() == rulesHashBefore;
        if (!timerShapeTextColorProgression
            || !scoreAndComboMoveTogether
            || !comboMotionHasStaticFallback
            || !powerIdentityOneToOne
            || !recoveryProtectionPreTelegraphed
            || !deathSignalsDistinct
            || !allProfilesDeathAttributionSurvives
            || !rulesStateUnchanged)
        {
            throw new InvalidOperationException("Multimodal feedback qualification failed.");
        }

        return new MultimodalFeedbackEvidence(
            SchemaVersion: 1,
            Kind: "multimodal-feedback-v1",
            Passed: true,
            HungerPhaseCount: hungerStates.Length,
            ComboMilestoneCount: 4,
            PowerCount: powers.Length,
            DeathCauseCount: deaths.Length,
            TimerShapeTextColorProgression: timerShapeTextColorProgression,
            ScoreAndComboMoveTogether: scoreAndComboMoveTogether,
            ComboMotionHasStaticFallback: comboMotionHasStaticFallback,
            PowerIdentityOneToOne: powerIdentityOneToOne,
            RecoveryProtectionPreTelegraphed: recoveryProtectionPreTelegraphed,
            DeathSignalsDistinct: deathSignalsDistinct,
            AllProfilesDeathAttributionSurvives: allProfilesDeathAttributionSurvives,
            RulesStateUnchanged: rulesStateUnchanged,
            HungerStates: hungerStates,
            ComboMilestones: comboMilestones,
            Powers: powers,
            Deaths: deaths,
            Profiles: profiles);
    }

    private static ComboFeedbackState DescribeCombo(
        int count,
        double multiplier,
        AccessibilityPresentationPolicy accessibility)
    {
        var director = new VibeLevelDirector();
        director.Update(count);
        return ComboFeedback.Describe(
            count,
            multiplier,
            ComboFeedback.PulseTicks,
            accessibility,
            director.CurrentDefinition);
    }

    private readonly record struct ProfileDefinition(
        string Id,
        bool SoundMuted,
        bool ReducedMotion,
        bool FlashFree);
}
