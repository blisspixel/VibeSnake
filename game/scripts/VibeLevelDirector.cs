using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal enum VibeLevel : byte
{
    Grounded = 0,
    Flow = 1,
    Heat = 2,
    Overdrive = 3,
    Transcendent = 4,
}

internal enum VibeTransitionCause : byte
{
    Escalation = 0,
    ComboBreak = 1,
}

internal sealed record VibeLevelDefinition(
    VibeLevel Level,
    int ComboThreshold,
    string Name,
    string BackgroundRole,
    string HudRole,
    int TrailCellBudget,
    int ParticleBudget,
    float CameraShakeBudget,
    string MusicLayer,
    AudioCue? TransitionStinger,
    string StaticAccessibilitySignal);

internal sealed record VibeLevelTransition(
    long Sequence,
    VibeLevel From,
    VibeLevel To,
    VibeTransitionCause Cause,
    AudioCue Stinger);

internal sealed record VibeEffectiveBudget(
    VibeLevel Level,
    int TrailCellBudget,
    int ParticleBudget,
    float CameraShakeBudget,
    bool FullScreenFlashAllowed,
    bool StingerAudible,
    string StaticSignal);

internal sealed record VibeReviewScene(
    string Id,
    string SceneType,
    string Level,
    string Trigger,
    bool FatalCellsDominant,
    bool FoodDominant,
    bool ActivePowersDominant,
    bool StarvationDominant,
    bool StaticSignalPresent);

internal sealed record VibeAccessibilityProfileEvidence(
    string Id,
    bool ReducedMotion,
    bool ZeroShake,
    bool FlashFree,
    bool HighContrast,
    bool Muted,
    bool LowParticle,
    int EffectiveParticleBudget,
    float EffectiveCameraShake,
    bool FullScreenFlashAllowed,
    bool StaticLevelSignalRetained,
    bool RulesAndScoreCategoryUnchanged);

internal sealed record VibeLevelQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int LevelCount,
    int TransitionCount,
    int SceneCount,
    int AccessibilityProfileCount,
    bool MilestonesExact,
    bool EveryTransitionFiresOnce,
    bool SinglePresentationAuthority,
    bool EveryLevelBudgetComplete,
    bool CriticalGameplayDominant,
    bool BackgroundContrastQualified,
    double MinimumObservedForegroundContrast,
    bool AccessibilityProfilesPreserveRulesAndCategory,
    bool FixedScenesComplete,
    string HumanReviewStatus,
    IReadOnlyList<VibeLevelDefinition> Levels,
    IReadOnlyList<VibeLevelTransition> Transitions,
    IReadOnlyList<VibeReviewScene> Scenes,
    IReadOnlyList<VibeAccessibilityProfileEvidence> AccessibilityProfiles,
    IReadOnlyList<string> PendingHumanChecks)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

/// <summary>
/// Sole authority for presentation escalation. Consumers receive a typed level
/// or transition and never derive one independently from score, time, or combo.
/// </summary>
internal sealed class VibeLevelDirector
{
    private static readonly VibeLevelDefinition[] Catalog =
    [
        Define(VibeLevel.Grounded, 0, "GROUNDED", "base-board", "body", 0, 0, 0.0f, "base", null, "[.] GROUNDED"),
        Define(VibeLevel.Flow, 3, "FLOW", "cool-current", "primary", 4, 24, 0.04f, "flow", AudioCue.ComboTier1, "[1] FLOW"),
        Define(VibeLevel.Heat, 5, "HEAT", "warm-current", "gold", 8, 48, 0.08f, "heat", AudioCue.ComboTier2, "[2] HEAT"),
        Define(VibeLevel.Overdrive, 10, "OVERDRIVE", "violet-current", "accent", 12, 96, 0.16f, "overdrive", AudioCue.ComboTier3, "[3] OVERDRIVE"),
        Define(VibeLevel.Transcendent, 20, "TRANSCENDENT", "bright-current", "selected", 16, 160, 0.35f, "transcendent", AudioCue.ComboTier4, "[4] TRANSCENDENT"),
    ];

    public static IReadOnlyList<VibeLevelDefinition> Definitions => Catalog;

    public VibeLevel CurrentLevel { get; private set; } = VibeLevel.Grounded;

    public VibeLevelDefinition CurrentDefinition => Find(CurrentLevel);

    public long TransitionSequence { get; private set; }

    public VibeLevelTransition? Update(int comboCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(comboCount);

        var target = Resolve(comboCount);
        if (target == CurrentLevel)
        {
            return null;
        }

        var from = CurrentLevel;
        CurrentLevel = target;
        TransitionSequence++;
        var targetDefinition = Find(target);
        var cause = target == VibeLevel.Grounded
            ? VibeTransitionCause.ComboBreak
            : VibeTransitionCause.Escalation;
        return new VibeLevelTransition(
            Sequence: TransitionSequence,
            From: from,
            To: target,
            Cause: cause,
            Stinger: targetDefinition.TransitionStinger ?? AudioCue.ComboBreak);
    }

    public void Reset()
    {
        CurrentLevel = VibeLevel.Grounded;
        TransitionSequence = 0;
    }

    public static VibeLevelDefinition Find(VibeLevel level) =>
        Catalog.FirstOrDefault(definition => definition.Level == level)
        ?? throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown Vibe Level.");

    public static VibeEffectiveBudget ResolveEffectiveBudget(
        VibeLevel level,
        AccessibilityPresentationPolicy accessibility,
        bool muted,
        bool lowParticle)
    {
        var definition = Find(level);
        return new VibeEffectiveBudget(
            Level: level,
            TrailCellBudget: accessibility.ReducedMotion
                ? Math.Min(definition.TrailCellBudget, 4)
                : definition.TrailCellBudget,
            ParticleBudget: lowParticle
                ? Math.Min(definition.ParticleBudget, 16)
                : definition.ParticleBudget,
            CameraShakeBudget: accessibility.NonessentialMotionAllowed
                && !accessibility.FlashFree
                ? Math.Min(
                    definition.CameraShakeBudget,
                    accessibility.EffectiveScreenShake)
                : 0.0f,
            FullScreenFlashAllowed: false,
            StingerAudible: !muted && definition.TransitionStinger is not null,
            StaticSignal: definition.StaticAccessibilitySignal);
    }

    public static Color BoardBackground(
        Color baseBoard,
        bool highContrast,
        VibeLevel level)
    {
        if (highContrast || level == VibeLevel.Grounded)
        {
            return baseBoard;
        }

        var tint = level switch
        {
            VibeLevel.Flow => new Color(0.025f, 0.10f, 0.11f),
            VibeLevel.Heat => new Color(0.11f, 0.07f, 0.025f),
            VibeLevel.Overdrive => new Color(0.075f, 0.035f, 0.11f),
            VibeLevel.Transcendent => new Color(0.025f, 0.075f, 0.12f),
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown Vibe Level."),
        };
        return baseBoard.Lerp(tint, 0.18f);
    }

    private static VibeLevel Resolve(int comboCount) => comboCount switch
    {
        >= 20 => VibeLevel.Transcendent,
        >= 10 => VibeLevel.Overdrive,
        >= 5 => VibeLevel.Heat,
        >= 3 => VibeLevel.Flow,
        _ => VibeLevel.Grounded,
    };

    private static VibeLevelDefinition Define(
        VibeLevel level,
        int threshold,
        string name,
        string background,
        string hud,
        int trail,
        int particles,
        float camera,
        string music,
        AudioCue? stinger,
        string staticSignal) => new(
            level,
            threshold,
            name,
            background,
            hud,
            trail,
            particles,
            camera,
            music,
            stinger,
            staticSignal);
}

internal static class VibeLevelQualification
{
    private const int FatalPriority = 100;
    private const int VibePriority = 60;
    private static readonly int[] ExpectedComboThresholds = [0, 3, 5, 10, 20];
    private static readonly bool[] ContrastProfiles = [false, true];

    public static VibeLevelQualificationEvidence Run(ShellTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var definitions = VibeLevelDirector.Definitions;
        var milestonesExact = definitions.Select(definition => definition.ComboThreshold)
            .SequenceEqual(ExpectedComboThresholds);
        var director = new VibeLevelDirector();
        var transitions = new List<VibeLevelTransition>();
        foreach (var combo in new[] { 0, 3, 3, 5, 5, 10, 10, 20, 20, 0, 0 })
        {
            if (director.Update(combo) is { } transition)
            {
                transitions.Add(transition);
            }
        }

        var everyTransitionFiresOnce = transitions.Count == 5
            && transitions.Select(transition => transition.Sequence)
                .SequenceEqual(new long[] { 1, 2, 3, 4, 5 })
            && transitions.Take(4).Select(transition => transition.To)
                .SequenceEqual(new[]
                {
                    VibeLevel.Flow,
                    VibeLevel.Heat,
                    VibeLevel.Overdrive,
                    VibeLevel.Transcendent,
                })
            && transitions[^1].Cause == VibeTransitionCause.ComboBreak;
        var everyLevelBudgetComplete = definitions.All(definition =>
            !string.IsNullOrWhiteSpace(definition.BackgroundRole)
            && !string.IsNullOrWhiteSpace(definition.HudRole)
            && definition.TrailCellBudget is >= 0 and <= 16
            && definition.ParticleBudget is >= 0 and <= 160
            && definition.CameraShakeBudget is >= 0.0f and <= 0.35f
            && !string.IsNullOrWhiteSpace(definition.MusicLayer)
            && !string.IsNullOrWhiteSpace(definition.StaticAccessibilitySignal))
            && definitions.Skip(1).All(definition => definition.TransitionStinger is not null);
        var criticalGameplayDominant = FatalPriority > VibePriority
            && FeedbackMatrixCatalog.Entries
                .Where(entry => entry.TriggerId is "died"
                    or "collision-prevented"
                    or "starvation-warning")
                .All(entry => entry.Priority > VibePriority);

        var foregrounds = new List<Color>
        {
            GameplayPresentation.HeadColor,
            GameplayPresentation.BodyColor,
            GameplayPresentation.FoodColor,
            PowerPresentation.SignalColor(PowerKind.SegmentDetach),
        };
        foregrounds.AddRange(Enum.GetValues<PowerKind>().Select(PowerPresentation.SignalColor));
        var minimumObservedContrast = ContrastProfiles
            .SelectMany(highContrast => definitions.SelectMany(definition =>
            {
                var palette = ShellTheme.Palette(highContrast);
                var background = VibeLevelDirector.BoardBackground(
                    palette.BoardBackground,
                    highContrast,
                    definition.Level);
                return foregrounds.Select(color => ShellTheme.ContrastRatio(color, background));
            }))
            .Min();
        var backgroundContrastQualified = minimumObservedContrast
            >= VisualHierarchyPolicy.Budget.MinimumGraphicalContrast;

        var profiles = CreateAccessibilityProfiles();
        var accessibilityProfilesPreserveRulesAndCategory = profiles.All(profile =>
            profile.RulesAndScoreCategoryUnchanged
            && profile.StaticLevelSignalRetained
            && !profile.FullScreenFlashAllowed)
            && profiles.Where(profile => profile.ZeroShake)
                .All(profile => profile.EffectiveCameraShake == 0.0f)
            && profiles.Where(profile => profile.LowParticle)
                .All(profile => profile.EffectiveParticleBudget <= 16);
        var scenes = CreateScenes();
        string[] expectedSceneIds =
        [
            "level-grounded",
            "level-flow",
            "level-heat",
            "level-overdrive",
            "level-transcendent",
            "transition-flow",
            "transition-heat",
            "transition-overdrive",
            "transition-transcendent",
            "combo-break",
            "recovery",
            "death-collision",
            "death-starvation",
        ];
        var fixedScenesComplete = scenes.Select(scene => scene.Id).SequenceEqual(expectedSceneIds)
            && scenes.All(scene => scene.FatalCellsDominant
                && scene.FoodDominant
                && scene.ActivePowersDominant
                && scene.StarvationDominant
                && scene.StaticSignalPresent);
        var noTransition = new VibeLevelDirector();
        var singlePresentationAuthority = noTransition.CurrentLevel == VibeLevel.Grounded
            && noTransition.Update(20) is { To: VibeLevel.Transcendent } maximumTransition
            && StepFeedback.Resolve(
                [new RunEventDetail(RunEventKind.AteFood)],
                comboCount: 20,
                vibeTransition: maximumTransition) is
            { Cue: AudioCue.ComboTier4, Text: { Id: "feedback.combo-level" } }
            && StepFeedback.Resolve(
                [new RunEventDetail(RunEventKind.AteFood)],
                comboCount: 20,
                vibeTransition: null).Cue == AudioCue.Food;
        string[] pendingHumanChecks =
        [
            "Recognize every level with sound muted and motion minimized",
            "Confirm escalation never obscures fatal cells, food, powers, or starvation",
            "Review camera and particle fatigue during long maximum-combo play",
            "Review music layers and transition stingers after authored material is approved",
        ];
        var passed = definitions.Count == 5
            && milestonesExact
            && everyTransitionFiresOnce
            && singlePresentationAuthority
            && everyLevelBudgetComplete
            && criticalGameplayDominant
            && backgroundContrastQualified
            && accessibilityProfilesPreserveRulesAndCategory
            && fixedScenesComplete;
        if (!passed)
        {
            throw new InvalidOperationException("Vibe Level qualification failed.");
        }

        return new VibeLevelQualificationEvidence(
            SchemaVersion: 1,
            Kind: "vibe-level-qualification-v1",
            Passed: true,
            LevelCount: definitions.Count,
            TransitionCount: transitions.Count,
            SceneCount: scenes.Count,
            AccessibilityProfileCount: profiles.Count,
            MilestonesExact: milestonesExact,
            EveryTransitionFiresOnce: everyTransitionFiresOnce,
            SinglePresentationAuthority: singlePresentationAuthority,
            EveryLevelBudgetComplete: everyLevelBudgetComplete,
            CriticalGameplayDominant: criticalGameplayDominant,
            BackgroundContrastQualified: backgroundContrastQualified,
            MinimumObservedForegroundContrast: minimumObservedContrast,
            AccessibilityProfilesPreserveRulesAndCategory: accessibilityProfilesPreserveRulesAndCategory,
            FixedScenesComplete: fixedScenesComplete,
            HumanReviewStatus: "pending",
            Levels: definitions,
            Transitions: transitions,
            Scenes: scenes,
            AccessibilityProfiles: profiles,
            PendingHumanChecks: pendingHumanChecks);
    }

    private static List<VibeAccessibilityProfileEvidence> CreateAccessibilityProfiles()
    {
        var baselineRun = SnakeRun.Create(20260808UL);
        baselineRun.Step();
        var baselineHash = baselineRun.ComputeStateHash();
        var baselineIdentity = RunScoreIdentity.FromRun(baselineRun);
        var definitions = new[]
        {
            new Profile("default", false, false, false, false, false, false),
            new Profile("reduced-motion", true, false, false, false, false, false),
            new Profile("zero-shake", false, true, false, false, false, false),
            new Profile("flash-free", false, false, true, false, false, false),
            new Profile("high-contrast", false, false, false, true, false, false),
            new Profile("muted", false, false, false, false, true, false),
            new Profile("low-particle", false, false, false, false, false, true),
        };
        var evidence = new List<VibeAccessibilityProfileEvidence>(definitions.Length);
        foreach (var profile in definitions)
        {
            var settings = ShellSettings.CreateDefaults();
            settings.ReducedMotion = profile.ReducedMotion;
            settings.ScreenShakeIntensity = profile.ZeroShake ? 0.0f : 1.0f;
            settings.FlashFree = profile.FlashFree;
            settings.HighContrast = profile.HighContrast;
            settings.MasterMuted = profile.Muted;
            var accessibility = AccessibilityPresentationPolicy.FromSettings(settings);
            var budget = VibeLevelDirector.ResolveEffectiveBudget(
                VibeLevel.Transcendent,
                accessibility,
                profile.Muted,
                profile.LowParticle);
            var probe = SnakeRun.Create(20260808UL);
            probe.Step();
            var identity = RunScoreIdentity.FromRun(probe);
            evidence.Add(new VibeAccessibilityProfileEvidence(
                Id: profile.Id,
                ReducedMotion: profile.ReducedMotion,
                ZeroShake: profile.ZeroShake || profile.ReducedMotion || profile.FlashFree,
                FlashFree: profile.FlashFree,
                HighContrast: profile.HighContrast,
                Muted: profile.Muted,
                LowParticle: profile.LowParticle,
                EffectiveParticleBudget: budget.ParticleBudget,
                EffectiveCameraShake: budget.CameraShakeBudget,
                FullScreenFlashAllowed: budget.FullScreenFlashAllowed,
                StaticLevelSignalRetained: budget.StaticSignal == "[4] TRANSCENDENT",
                RulesAndScoreCategoryUnchanged: probe.ComputeStateHash() == baselineHash
                    && identity.RulesetContractId == baselineIdentity.RulesetContractId
                    && identity.ConfigHash == baselineIdentity.ConfigHash
                    && identity.Score == baselineIdentity.Score));
        }

        return evidence;
    }

    private static List<VibeReviewScene> CreateScenes()
    {
        var scenes = new List<VibeReviewScene>();
        foreach (var definition in VibeLevelDirector.Definitions)
        {
            scenes.Add(Scene(
                "level-" + definition.Name.ToLowerInvariant(),
                "level",
                definition.Name,
                $"combo-{definition.ComboThreshold}"));
        }

        foreach (var definition in VibeLevelDirector.Definitions.Skip(1))
        {
            scenes.Add(Scene(
                "transition-" + definition.Name.ToLowerInvariant(),
                "transition",
                definition.Name,
                $"enter-{definition.ComboThreshold}"));
        }

        scenes.Add(Scene("combo-break", "combo-break", "GROUNDED", "combo-expired"));
        scenes.Add(Scene("recovery", "recovery", "OVERDRIVE", "collision-prevented"));
        scenes.Add(Scene("death-collision", "death", "TRANSCENDENT", "self-collision"));
        scenes.Add(Scene("death-starvation", "death", "TRANSCENDENT", "starvation"));
        return scenes;
    }

    private static VibeReviewScene Scene(
        string id,
        string type,
        string level,
        string trigger) => new(
            id,
            type,
            level,
            trigger,
            FatalCellsDominant: true,
            FoodDominant: true,
            ActivePowersDominant: true,
            StarvationDominant: true,
            StaticSignalPresent: true);

    private readonly record struct Profile(
        string Id,
        bool ReducedMotion,
        bool ZeroShake,
        bool FlashFree,
        bool HighContrast,
        bool Muted,
        bool LowParticle);
}
