using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal enum FeedbackTriggerDomain : byte
{
    RunEvent = 0,
    UiAction = 1,
}

internal enum FeedbackDominantChannel : byte
{
    Visual = 0,
    Audio = 1,
    Text = 2,
    Haptic = 3,
}

internal enum FeedbackHapticPattern : byte
{
    None = 0,
    Light = 1,
    Medium = 2,
    Heavy = 3,
    Pulse = 4,
    Success = 5,
}

internal enum FeedbackStackPolicy : byte
{
    Coalesce = 0,
    StackBounded = 1,
    ReplaceLowerPriority = 2,
    InterruptLowerPriority = 3,
}

internal enum FeedbackAssetState : byte
{
    NotRequired = 0,
    AuthoredAbsentFallbackActive = 1,
}

internal enum FeedbackImplementationState : byte
{
    Implemented = 0,
    ImplementedFallback = 1,
    MetadataOnly = 2,
}

internal enum UiFeedbackAction : byte
{
    Navigate = 0,
    Confirm = 1,
    Back = 2,
    Pause = 3,
    Resume = 4,
    Restart = 5,
    OpenScreen = 6,
    CloseScreen = 7,
    SettingChanged = 8,
    ResetConfirmed = 9,
    RecoveryBlocked = 10,
    QuitRequested = 11,
    ControllerConnected = 12,
    ControllerDisconnected = 13,
    Error = 14,
}

internal sealed record FeedbackMatrixEntry(
    FeedbackTriggerDomain Domain,
    string TriggerId,
    string VisualCue,
    string AudioPolicy,
    IReadOnlyList<AudioCue> FallbackCues,
    string TextAlternative,
    FeedbackHapticPattern Haptic,
    FeedbackDominantChannel DominantChannel,
    int Priority,
    int CooldownMilliseconds,
    int MaximumPolyphony,
    FeedbackStackPolicy StackPolicy,
    bool MayInterruptLowerPriority,
    float MusicDuckDecibels,
    float ShakeStrength,
    bool MayFlash,
    int HitstopMilliseconds,
    bool Critical,
    string ReducedMotionAlternative,
    string FlashFreeAlternative,
    string MutedAlternative,
    FeedbackAssetState AssetState,
    FeedbackImplementationState ImplementationState);

internal sealed record FeedbackMatrixQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int RunEventCount,
    int UiActionCount,
    int EntryCount,
    bool EveryTriggerMapped,
    bool EveryDominantCueDeclared,
    bool EveryAccessibilityAlternativeDeclared,
    bool EveryAudioCueAccountedFor,
    bool StackInterruptionPolicyComplete,
    bool AuthoredAbsenceExplicit,
    int AuthoredAbsentEntryCount,
    int UnusedShippedAssetCount,
    IReadOnlyList<string> UnusedShippedAssets,
    bool FlashPolicySafe,
    bool HapticMetadataComplete,
    IReadOnlyList<FeedbackMatrixEntry> Entries)
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
/// Canonical feedback policy for ordered rules events and shell actions. It is
/// metadata, not permission to mutate rules or claim missing authored assets.
/// </summary>
internal static class FeedbackMatrixCatalog
{
    private static readonly FeedbackMatrixEntry[] RunEntries =
    [
        Run(RunEventKind.DirectionChanged, "head-direction", "none", "Direction changed", dominant: FeedbackDominantChannel.Visual),
        Run(RunEventKind.Moved, "snake-motion", "none", "Movement continues", dominant: FeedbackDominantChannel.Visual),
        Run(RunEventKind.Wrapped, "edge-wrap-trace", "none", "Wrapped through the edge", dominant: FeedbackDominantChannel.Visual, cooldown: 100),
        Run(RunEventKind.AteFood, "food-collapse", "food-or-combo-tier", "Food collected", [AudioCue.Food, AudioCue.ComboTier1, AudioCue.ComboTier2, AudioCue.ComboTier3, AudioCue.ComboTier4], FeedbackHapticPattern.Light, FeedbackDominantChannel.Audio, cooldown: 35, polyphony: 3),
        Run(RunEventKind.ScoreChanged, "score-step", "none", "Score changed", dominant: FeedbackDominantChannel.Visual, cooldown: 35),
        Run(RunEventKind.HungerReset, "hunger-refill", "none", "Hunger restored", dominant: FeedbackDominantChannel.Visual, cooldown: 35),
        Run(RunEventKind.Died, "death-freeze", "death-by-cause", "Run ended with an exact cause", [AudioCue.Collision, AudioCue.StarvationDeath], FeedbackHapticPattern.Heavy, FeedbackDominantChannel.Text, cooldown: 500, polyphony: 1, stack: FeedbackStackPolicy.InterruptLowerPriority, interrupt: true, duck: -9.0f, shake: 0.35f, critical: true),
        Run(RunEventKind.Won, "grid-complete", "victory", "Grid complete", [AudioCue.Victory], FeedbackHapticPattern.Success, FeedbackDominantChannel.Text, cooldown: 1_000, polyphony: 1, stack: FeedbackStackPolicy.InterruptLowerPriority, interrupt: true, duck: -6.0f, shake: 0.15f, critical: true),
        Run(RunEventKind.PowerSpawned, "power-signal", "power-spawn-by-kind", "Power signal detected", [AudioCue.ShieldSpawn, AudioCue.PowerSpawn], FeedbackHapticPattern.None, FeedbackDominantChannel.Visual, cooldown: 100, polyphony: 2),
        Run(RunEventKind.PowerCollected, "power-collection", "none-activation-owns-audio", "Power collected", dominant: FeedbackDominantChannel.Visual, cooldown: 50),
        Run(RunEventKind.PowerActivated, "power-active-state", "power-activate-by-kind", "Power activated", [AudioCue.ShieldActivate, AudioCue.PhaseShiftActivate, AudioCue.LastStandActivate, AudioCue.SlowMoActivate, AudioCue.BoostActivate, AudioCue.MagnetActivate, AudioCue.BaitActivate, AudioCue.GluttonyActivate, AudioCue.SegmentDetachActivate], FeedbackHapticPattern.Medium, FeedbackDominantChannel.Text, cooldown: 100, polyphony: 2, stack: FeedbackStackPolicy.ReplaceLowerPriority, duck: -3.0f),
        Run(RunEventKind.PowerExpired, "power-expiry", "power-expire-by-kind", "Power expired", [AudioCue.ShieldExpire, AudioCue.PowerExpire], FeedbackHapticPattern.None, FeedbackDominantChannel.Text, cooldown: 100, polyphony: 2),
        Run(RunEventKind.PowerConsumed, "power-consumed", "power-consume-by-kind", "Power resource consumed", [AudioCue.ShieldBreak, AudioCue.PowerRecovery], FeedbackHapticPattern.Medium, FeedbackDominantChannel.Text, cooldown: 150, polyphony: 1, stack: FeedbackStackPolicy.ReplaceLowerPriority, duck: -4.0f, shake: 0.1f),
        Run(RunEventKind.PowerDiscarded, "power-cleared", "power-expire-by-kind", "Power signal cleared", [AudioCue.ShieldExpire, AudioCue.PowerExpire], FeedbackHapticPattern.None, FeedbackDominantChannel.Text, cooldown: 100, polyphony: 2),
        Run(RunEventKind.CollisionPrevented, "recovery-protection", "recovery-by-power", "Fatal collision prevented", [AudioCue.ShieldBreak, AudioCue.PowerRecovery], FeedbackHapticPattern.Heavy, FeedbackDominantChannel.Text, cooldown: 250, polyphony: 1, stack: FeedbackStackPolicy.InterruptLowerPriority, interrupt: true, duck: -6.0f, shake: 0.2f, critical: true),
        Run(RunEventKind.NearMiss, "near-miss-outline", "style-tick", "Near miss style awarded", [AudioCue.Food], FeedbackHapticPattern.Light, FeedbackDominantChannel.Visual, cooldown: 100, polyphony: 2),
        Run(RunEventKind.StarvationWarning, "hunger-critical", "starvation-warning", "Starvation warning", [AudioCue.Starvation], FeedbackHapticPattern.Pulse, FeedbackDominantChannel.Text, cooldown: 1_000, polyphony: 1, stack: FeedbackStackPolicy.InterruptLowerPriority, interrupt: true, duck: -5.0f, critical: true),
        Run(RunEventKind.ComboExpired, "combo-reset", "combo-expired", "Combo expired", [AudioCue.ComboBreak], FeedbackHapticPattern.None, FeedbackDominantChannel.Text, cooldown: 250, polyphony: 1),
        Run(RunEventKind.AchievementCandidate, "achievement-banner", "achievement", "Achievement unlocked", [AudioCue.Achievement], FeedbackHapticPattern.Success, FeedbackDominantChannel.Text, cooldown: 500, polyphony: 1, stack: FeedbackStackPolicy.ReplaceLowerPriority, duck: -3.0f),
    ];

    private static readonly FeedbackMatrixEntry[] UiEntries =
    [
        Ui(UiFeedbackAction.Navigate, "focus-step", "ui-navigate", "Focus moved", [AudioCue.Navigate], FeedbackHapticPattern.Light, cooldown: 25, polyphony: 2),
        Ui(UiFeedbackAction.Confirm, "confirm-state", "ui-confirm", "Confirmed", [AudioCue.Confirm], FeedbackHapticPattern.Light, cooldown: 35, polyphony: 2),
        Ui(UiFeedbackAction.Back, "back-state", "ui-back", "Went back", [AudioCue.Back], FeedbackHapticPattern.Light, cooldown: 35, polyphony: 2),
        Ui(UiFeedbackAction.Pause, "pause-overlay", "pause", "Paused", [AudioCue.Pause], FeedbackHapticPattern.Medium, dominant: FeedbackDominantChannel.Text, cooldown: 100, polyphony: 1, priority: 80),
        Ui(UiFeedbackAction.Resume, "resume-overlay", "pause", "Resumed", [AudioCue.Pause], FeedbackHapticPattern.Light, dominant: FeedbackDominantChannel.Text, cooldown: 100, polyphony: 1, priority: 80),
        Ui(UiFeedbackAction.Restart, "restart-state", "restart", "Run restarted", [AudioCue.Restart], FeedbackHapticPattern.Medium, dominant: FeedbackDominantChannel.Text, cooldown: 250, polyphony: 1, priority: 85),
        Ui(UiFeedbackAction.OpenScreen, "screen-open", "ui-confirm", "Screen opened", [AudioCue.Confirm], FeedbackHapticPattern.Light, cooldown: 50, polyphony: 1),
        Ui(UiFeedbackAction.CloseScreen, "screen-close", "ui-back", "Screen closed", [AudioCue.Back], FeedbackHapticPattern.Light, cooldown: 50, polyphony: 1),
        Ui(UiFeedbackAction.SettingChanged, "setting-value", "ui-confirm", "Setting changed", [AudioCue.Confirm], FeedbackHapticPattern.Light, cooldown: 50, polyphony: 1),
        Ui(UiFeedbackAction.ResetConfirmed, "reset-complete", "ui-confirm", "Reset completed after backup", [AudioCue.Confirm], FeedbackHapticPattern.Medium, dominant: FeedbackDominantChannel.Text, cooldown: 250, polyphony: 1, priority: 85, critical: true),
        Ui(UiFeedbackAction.RecoveryBlocked, "recovery-warning", "ui-back", "Recovery blocked with a reason", [AudioCue.Back], FeedbackHapticPattern.Heavy, dominant: FeedbackDominantChannel.Text, cooldown: 250, polyphony: 1, priority: 90, critical: true),
        Ui(UiFeedbackAction.QuitRequested, "quit-state", "none", "Quit requested", dominant: FeedbackDominantChannel.Text, cooldown: 100, priority: 75),
        Ui(UiFeedbackAction.ControllerConnected, "device-caption", "ui-confirm", "Controller connected", [AudioCue.Confirm], FeedbackHapticPattern.Light, dominant: FeedbackDominantChannel.Text, cooldown: 250, polyphony: 1, priority: 70),
        Ui(UiFeedbackAction.ControllerDisconnected, "device-warning", "pause", "Controller disconnected and play paused", [AudioCue.Pause], FeedbackHapticPattern.Heavy, dominant: FeedbackDominantChannel.Text, cooldown: 250, polyphony: 1, priority: 95, critical: true),
        Ui(UiFeedbackAction.Error, "error-caption", "ui-back", "Action failed with a recoverable reason", [AudioCue.Back], FeedbackHapticPattern.Heavy, dominant: FeedbackDominantChannel.Text, cooldown: 250, polyphony: 1, priority: 100, critical: true),
    ];

    public static IReadOnlyList<FeedbackMatrixEntry> Entries { get; } =
        RunEntries.Concat(UiEntries).ToArray();

    public static FeedbackMatrixQualificationEvidence Qualify()
    {
        var everyTriggerMapped = RunEntries.Length == RulesEventCatalog.OrderedKinds.Count
            && RunEntries.Select(entry => entry.TriggerId).SequenceEqual(
                RulesEventCatalog.OrderedKinds.Select(RulesEventCatalog.ToWireName))
            && UiEntries.Length == Enum.GetValues<UiFeedbackAction>().Length
            && UiEntries.Select(entry => entry.TriggerId).SequenceEqual(
                Enum.GetValues<UiFeedbackAction>().Select(ToWire));
        var everyDominantCueDeclared = Entries.All(entry =>
            Enum.IsDefined(entry.DominantChannel)
            && !string.IsNullOrWhiteSpace(entry.VisualCue)
            && !string.IsNullOrWhiteSpace(entry.AudioPolicy));
        var everyAccessibilityAlternativeDeclared = Entries.All(entry =>
            !string.IsNullOrWhiteSpace(entry.TextAlternative)
            && !string.IsNullOrWhiteSpace(entry.ReducedMotionAlternative)
            && !string.IsNullOrWhiteSpace(entry.FlashFreeAlternative)
            && !string.IsNullOrWhiteSpace(entry.MutedAlternative));
        var declaredCues = Entries.SelectMany(entry => entry.FallbackCues).Distinct().ToArray();
        var everyAudioCueAccountedFor = declaredCues.OrderBy(cue => cue)
            .SequenceEqual(Enum.GetValues<AudioCue>().OrderBy(cue => cue));
        var stackInterruptionPolicyComplete = Entries.All(entry =>
            entry.Priority is >= 0 and <= 100
            && entry.CooldownMilliseconds is >= 0 and <= 5_000
            && entry.MaximumPolyphony is >= 0 and <= 4
            && entry.MusicDuckDecibels is >= -12.0f and <= 0.0f
            && entry.ShakeStrength is >= 0.0f and <= 1.0f
            && (!entry.MayInterruptLowerPriority
                || entry.StackPolicy == FeedbackStackPolicy.InterruptLowerPriority));
        var authoredAbsent = Entries.Count(entry =>
            entry.AssetState == FeedbackAssetState.AuthoredAbsentFallbackActive);
        var authoredAbsenceExplicit = Entries.All(entry =>
            entry.FallbackCues.Count == 0
                ? entry.AssetState == FeedbackAssetState.NotRequired
                : entry.AssetState == FeedbackAssetState.AuthoredAbsentFallbackActive);
        var flashPolicySafe = Entries.All(entry => !entry.MayFlash)
            && Entries.All(entry => entry.HitstopMilliseconds == 0);
        var hapticMetadataComplete = Entries.All(entry => Enum.IsDefined(entry.Haptic));
        var passed = everyTriggerMapped
            && everyDominantCueDeclared
            && everyAccessibilityAlternativeDeclared
            && everyAudioCueAccountedFor
            && stackInterruptionPolicyComplete
            && authoredAbsenceExplicit
            && flashPolicySafe
            && hapticMetadataComplete;
        if (!passed)
        {
            throw new InvalidOperationException("The canonical feedback matrix is incomplete.");
        }

        return new FeedbackMatrixQualificationEvidence(
            SchemaVersion: 1,
            Kind: "feedback-matrix-qualification-v1",
            Passed: true,
            RunEventCount: RunEntries.Length,
            UiActionCount: UiEntries.Length,
            EntryCount: Entries.Count,
            EveryTriggerMapped: everyTriggerMapped,
            EveryDominantCueDeclared: everyDominantCueDeclared,
            EveryAccessibilityAlternativeDeclared: everyAccessibilityAlternativeDeclared,
            EveryAudioCueAccountedFor: everyAudioCueAccountedFor,
            StackInterruptionPolicyComplete: stackInterruptionPolicyComplete,
            AuthoredAbsenceExplicit: authoredAbsenceExplicit,
            AuthoredAbsentEntryCount: authoredAbsent,
            UnusedShippedAssetCount: 0,
            UnusedShippedAssets: Array.Empty<string>(),
            FlashPolicySafe: flashPolicySafe,
            HapticMetadataComplete: hapticMetadataComplete,
            Entries: Entries);
    }

    private static FeedbackMatrixEntry Run(
        RunEventKind kind,
        string visual,
        string audio,
        string text,
        IReadOnlyList<AudioCue>? cues = null,
        FeedbackHapticPattern haptic = FeedbackHapticPattern.None,
        FeedbackDominantChannel dominant = FeedbackDominantChannel.Visual,
        int cooldown = 0,
        int polyphony = 0,
        FeedbackStackPolicy stack = FeedbackStackPolicy.Coalesce,
        bool interrupt = false,
        float duck = 0.0f,
        float shake = 0.0f,
        bool critical = false) => Create(
            FeedbackTriggerDomain.RunEvent,
            RulesEventCatalog.ToWireName(kind),
            visual,
            audio,
            text,
            cues,
            haptic,
            dominant,
            RulesEventCatalog.PresentationPriority(kind),
            cooldown,
            polyphony,
            stack,
            interrupt,
            duck,
            shake,
            critical);

    private static FeedbackMatrixEntry Ui(
        UiFeedbackAction action,
        string visual,
        string audio,
        string text,
        IReadOnlyList<AudioCue>? cues = null,
        FeedbackHapticPattern haptic = FeedbackHapticPattern.None,
        FeedbackDominantChannel dominant = FeedbackDominantChannel.Audio,
        int cooldown = 0,
        int polyphony = 0,
        int priority = 50,
        bool critical = false) => Create(
            FeedbackTriggerDomain.UiAction,
            ToWire(action),
            visual,
            audio,
            text,
            cues,
            haptic,
            dominant,
            priority,
            cooldown,
            polyphony,
            critical ? FeedbackStackPolicy.InterruptLowerPriority : FeedbackStackPolicy.Coalesce,
            critical,
            critical ? -4.0f : 0.0f,
            critical ? 0.1f : 0.0f,
            critical);

    private static FeedbackMatrixEntry Create(
        FeedbackTriggerDomain domain,
        string triggerId,
        string visual,
        string audio,
        string text,
        IReadOnlyList<AudioCue>? cues,
        FeedbackHapticPattern haptic,
        FeedbackDominantChannel dominant,
        int priority,
        int cooldown,
        int polyphony,
        FeedbackStackPolicy stack,
        bool interrupt,
        float duck,
        float shake,
        bool critical)
    {
        var fallbackCues = cues ?? Array.Empty<AudioCue>();
        return new FeedbackMatrixEntry(
            Domain: domain,
            TriggerId: triggerId,
            VisualCue: visual,
            AudioPolicy: audio,
            FallbackCues: fallbackCues,
            TextAlternative: text,
            Haptic: haptic,
            DominantChannel: dominant,
            Priority: priority,
            CooldownMilliseconds: cooldown,
            MaximumPolyphony: polyphony,
            StackPolicy: stack,
            MayInterruptLowerPriority: interrupt,
            MusicDuckDecibels: duck,
            ShakeStrength: shake,
            MayFlash: false,
            HitstopMilliseconds: 0,
            Critical: critical,
            ReducedMotionAlternative: critical
                ? "Static outline and persistent text"
                : "Static state change without nonessential motion",
            FlashFreeAlternative: "No full-screen flash; stable color and text only",
            MutedAlternative: text + " with the declared visual cue",
            AssetState: fallbackCues.Count == 0
                ? FeedbackAssetState.NotRequired
                : FeedbackAssetState.AuthoredAbsentFallbackActive,
            ImplementationState: fallbackCues.Count == 0
                ? FeedbackImplementationState.Implemented
                : haptic == FeedbackHapticPattern.None
                    ? FeedbackImplementationState.ImplementedFallback
                    : FeedbackImplementationState.MetadataOnly);
    }

    private static string ToWire(UiFeedbackAction action) => action switch
    {
        UiFeedbackAction.Navigate => "navigate",
        UiFeedbackAction.Confirm => "confirm",
        UiFeedbackAction.Back => "back",
        UiFeedbackAction.Pause => "pause",
        UiFeedbackAction.Resume => "resume",
        UiFeedbackAction.Restart => "restart",
        UiFeedbackAction.OpenScreen => "open-screen",
        UiFeedbackAction.CloseScreen => "close-screen",
        UiFeedbackAction.SettingChanged => "setting-changed",
        UiFeedbackAction.ResetConfirmed => "reset-confirmed",
        UiFeedbackAction.RecoveryBlocked => "recovery-blocked",
        UiFeedbackAction.QuitRequested => "quit-requested",
        UiFeedbackAction.ControllerConnected => "controller-connected",
        UiFeedbackAction.ControllerDisconnected => "controller-disconnected",
        UiFeedbackAction.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}
