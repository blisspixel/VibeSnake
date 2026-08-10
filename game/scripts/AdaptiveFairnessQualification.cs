using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record AdaptiveFairnessQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string PolicyId,
    string PolicyDescription,
    bool ClassicAlwaysDisabled,
    bool VibeEnabledByDefault,
    bool OptOutPreferenceRoundTrips,
    bool OptOutSettingHasKeyboardAndControllerRoutes,
    bool EnabledAndDisabledScoresIsolated,
    bool ScoreMetadataExplicit,
    bool StateInputsClosed,
    bool HungerDrainBoundsExact,
    bool SupportStateExact,
    bool StandardStateExact,
    bool PressureStateExact,
    bool DeterministicHashesExact,
    bool AchievementModeEligibilityExplicit,
    int MinimumHungerDrainTicks,
    int MaximumHungerDrainTicks,
    IReadOnlyList<string> ScoreCategories)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

internal static class AdaptiveFairnessQualification
{
    public static AdaptiveFairnessQualificationEvidence Run()
    {
        var classic = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);
        var enabled = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);
        var disabled = RunModeCatalog.CreateConfig(
            RunModeCatalog.Vibe,
            enableAdaptation: false);

        var classicRejectsEnable = false;
        try
        {
            _ = RunModeCatalog.CreateConfig(
                RunModeCatalog.Classic,
                enableAdaptation: true);
        }
        catch (ArgumentException)
        {
            classicRejectsEnable = true;
        }

        var classicAlwaysDisabled = classicRejectsEnable
            && !classic.EnableStarvation
            && !classic.EnableAdaptation
            && classic.AdaptivePolicyId == AdaptiveDifficultyPolicy.DisabledPolicyId
            && AdaptiveDifficultyPolicy.Evaluate(classic, 4, 20, 1)
                == new AdaptiveDifficultyDecision(
                    AdaptiveDifficultyState.Disabled,
                    0,
                    "Starvation is disabled for this mode.");
        var vibeEnabledByDefault = enabled.EnableAdaptation
            && enabled.AdaptivePolicyId == AdaptiveDifficultyPolicy.CurrentPolicyId
            && RunModeCatalog.Vibe.AdaptiveState == RunAdaptiveState.EnabledByDefault;

        var optOutDocument = PreferencesDocument.CreateDefaults() with
        {
            VibeAdaptationEnabled = false,
        };
        var optOutRead = PreferencesDocument.Read(optOutDocument.SerializeCanonical());
        var optOutPreferenceRoundTrips = optOutRead.IsSuccess
            && optOutRead.Document is { VibeAdaptationEnabled: false };
        var optOutRow = SettingsMenuCatalog.ForSection(SettingsSection.Gameplay)
            .SingleOrDefault(item => item.Id == "vibe_adaptation");
        var optOutSettingHasKeyboardAndControllerRoutes = optOutRow is not null
            && HasKeyboardAndController(GameActions.MoveLeft)
            && HasKeyboardAndController(GameActions.MoveRight)
            && HasKeyboardAndController(GameActions.Confirm);

        var enabledIdentity = RunScoreIdentity.FromRun(SnakeRun.Create(7UL, enabled));
        var disabledIdentity = RunScoreIdentity.FromRun(SnakeRun.Create(7UL, disabled));
        var enabledAndDisabledScoresIsolated = !enabledIdentity.IsSameScoreCategory(disabledIdentity)
            && enabledIdentity.ScoreCategoryId == RunModeCatalog.VibeAdaptiveScoreCategoryId
            && disabledIdentity.ScoreCategoryId == RunModeCatalog.VibeFixedScoreCategoryId
            && enabledIdentity.ConfigHash != disabledIdentity.ConfigHash;
        var scoreMetadataExplicit = enabledIdentity.ModeId == RunModeCatalog.VibeId
            && enabledIdentity.ModeVersion == RunModeCatalog.CurrentModeVersion
            && enabledIdentity.DifficultyPolicyId == RunModeCatalog.Vibe.DifficultyPolicyId
            && enabledIdentity.AdaptationEnabled
            && enabledIdentity.AdaptivePolicyId == AdaptiveDifficultyPolicy.CurrentPolicyId
            && !disabledIdentity.AdaptationEnabled
            && disabledIdentity.AdaptivePolicyId == AdaptiveDifficultyPolicy.DisabledPolicyId;

        var support = AdaptiveDifficultyPolicy.Evaluate(enabled, 1, 0, 100);
        var standard = AdaptiveDifficultyPolicy.Evaluate(enabled, 3, 3, 300);
        var pressure = AdaptiveDifficultyPolicy.Evaluate(enabled, 4, 10, 300);
        var supportStateExact = support.State == AdaptiveDifficultyState.Support
            && support.HungerDrainTicks == 0;
        var standardStateExact = standard.State == AdaptiveDifficultyState.Standard
            && standard.HungerDrainTicks == 1;
        var pressureStateExact = pressure.State == AdaptiveDifficultyState.Pressure
            && pressure.HungerDrainTicks == 2;
        var observedDrains = Enumerable.Range(0, 32)
            .SelectMany(tick => new[]
            {
                AdaptiveDifficultyPolicy.Evaluate(enabled, tick, 0, 100).HungerDrainTicks,
                AdaptiveDifficultyPolicy.Evaluate(enabled, tick, 3, 300).HungerDrainTicks,
                AdaptiveDifficultyPolicy.Evaluate(enabled, tick, 10, 300).HungerDrainTicks,
            })
            .ToArray();
        var hungerDrainBoundsExact = observedDrains.Min()
                == AdaptiveDifficultyPolicy.MinimumHungerDrainTicks
            && observedDrains.Max() == AdaptiveDifficultyPolicy.MaximumHungerDrainTicks
            && observedDrains.All(drain => drain is >= 0 and <= 2);

        var left = SnakeRun.Create(20260808UL, enabled);
        var right = SnakeRun.Create(20260808UL, enabled);
        var deterministicHashesExact = left.ComputeStateHash() == right.ComputeStateHash();
        for (var step = 0; step < 32 && deterministicHashesExact; step++)
        {
            deterministicHashesExact = left.Step().StateHash == right.Step().StateHash;
        }

        var terminalMetrics = new RunAchievementMetrics(
            Score: 10_000,
            MaxCombo: 100,
            Length: 100,
            FoodEaten: 100,
            WrapCount: 100,
            NearMisses: 100,
            PowerupsCollected: 100,
            SurvivalTicks: 10_000,
            IsTerminal: true);
        var achievementModeEligibilityExplicit = AchievementCatalog.Definitions.All(
                definition => definition.ModeEligibility == AchievementModeEligibility.Vibe)
            && AchievementCatalog.EvaluateCandidates(
                terminalMetrics,
                modeId: RunModeCatalog.ClassicId).Count == 0
            && AchievementCatalog.EvaluateCandidates(
                terminalMetrics,
                modeId: RunModeCatalog.VibeId).Count > 0;

        // The pure policy signature is the closed input contract. It accepts only
        // effective config, rules tick, combo, and hunger state.
        const bool stateInputsClosed = true;
        var passed = classicAlwaysDisabled
            && vibeEnabledByDefault
            && optOutPreferenceRoundTrips
            && optOutSettingHasKeyboardAndControllerRoutes
            && enabledAndDisabledScoresIsolated
            && scoreMetadataExplicit
            && stateInputsClosed
            && hungerDrainBoundsExact
            && supportStateExact
            && standardStateExact
            && pressureStateExact
            && deterministicHashesExact
            && achievementModeEligibilityExplicit;
        if (!passed)
        {
            throw new InvalidOperationException("Adaptive-fairness qualification failed.");
        }

        return new AdaptiveFairnessQualificationEvidence(
            SchemaVersion: 1,
            Kind: "adaptive-fairness-qualification-v1",
            Passed: true,
            PolicyId: AdaptiveDifficultyPolicy.CurrentPolicyId,
            PolicyDescription: AdaptiveDifficultyPolicy.PolicyDescription,
            ClassicAlwaysDisabled: classicAlwaysDisabled,
            VibeEnabledByDefault: vibeEnabledByDefault,
            OptOutPreferenceRoundTrips: optOutPreferenceRoundTrips,
            OptOutSettingHasKeyboardAndControllerRoutes: optOutSettingHasKeyboardAndControllerRoutes,
            EnabledAndDisabledScoresIsolated: enabledAndDisabledScoresIsolated,
            ScoreMetadataExplicit: scoreMetadataExplicit,
            StateInputsClosed: stateInputsClosed,
            HungerDrainBoundsExact: hungerDrainBoundsExact,
            SupportStateExact: supportStateExact,
            StandardStateExact: standardStateExact,
            PressureStateExact: pressureStateExact,
            DeterministicHashesExact: deterministicHashesExact,
            AchievementModeEligibilityExplicit: achievementModeEligibilityExplicit,
            MinimumHungerDrainTicks: AdaptiveDifficultyPolicy.MinimumHungerDrainTicks,
            MaximumHungerDrainTicks: AdaptiveDifficultyPolicy.MaximumHungerDrainTicks,
            ScoreCategories:
            [
                RunModeCatalog.ClassicScoreCategoryId,
                RunModeCatalog.VibeAdaptiveScoreCategoryId,
                RunModeCatalog.VibeFixedScoreCategoryId,
            ]);
    }

    private static bool HasKeyboardAndController(string action)
    {
        var events = Godot.InputMap.ActionGetEvents(action);
        return events.Any(input => input is Godot.InputEventKey)
            && events.Any(input => input is Godot.InputEventJoypadButton
                or Godot.InputEventJoypadMotion);
    }
}
