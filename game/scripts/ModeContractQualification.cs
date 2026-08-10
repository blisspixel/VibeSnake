using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record ModeContractQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int ModeCount,
    string ConfigHashAlgorithm,
    bool StableIdentitiesComplete,
    bool DescriptionsComplete,
    bool ScoreCategoriesSeparated,
    bool BoardRulesExact,
    bool PauseRulesExact,
    bool SeedRulesExact,
    bool RestartRulesExact,
    bool ClassicFeatureBoundaryExact,
    bool VibeFeatureBoundaryExact,
    bool ClassicStarvationDisabled,
    bool ClassicPowerSpawningDisabled,
    bool ClassicMinimalScoreExact,
    bool VibePressureAndScoringActive,
    bool RestartRetainsModeAndBoard,
    bool KeyboardAndControllerSelectionRoutesComplete,
    bool DeterministicPerMode,
    bool CrossModeScoreIsolation,
    bool VibeAdaptationDefaultEnabled,
    bool VibeOptOutScoreIsolation,
    string AdaptiveImplementationStatus,
    IReadOnlyList<RunModeDefinition> Modes)
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

internal static class ModeContractQualification
{
    public static ModeContractQualificationEvidence Run()
    {
        var modes = RunModeCatalog.All;
        var classic = RunModeCatalog.Classic;
        var vibe = RunModeCatalog.Vibe;
        var classicConfig = RunModeCatalog.CreateConfig(classic);
        var vibeConfig = RunModeCatalog.CreateConfig(vibe);
        var stableIdentitiesComplete = modes.Count == 2
            && modes.Select(mode => mode.ContractId).Distinct(StringComparer.Ordinal).Count() == 2
            && modes.All(mode => mode.Version == 1);
        var descriptionsComplete = modes.All(mode =>
            !string.IsNullOrWhiteSpace(mode.DisplayName)
            && !string.IsNullOrWhiteSpace(mode.Description)
            && !string.IsNullOrWhiteSpace(mode.ScoreModelDescription));
        var scoreCategoriesSeparated = modes
                .Select(mode => mode.ScoreCategoryId)
                .Distinct(StringComparer.Ordinal)
                .Count() == 2
            && classicConfig.ComputeConfigHash() != vibeConfig.ComputeConfigHash();
        var boardRulesExact = modes.All(mode => mode.BoardWidth == 64 && mode.BoardHeight == 33)
            && classicConfig.Width == classic.BoardWidth
            && classicConfig.Height == classic.BoardHeight
            && vibeConfig.Width == vibe.BoardWidth
            && vibeConfig.Height == vibe.BoardHeight;
        var pauseRulesExact = modes.All(mode =>
            mode.PauseRule == RunPauseRule.FreezeRulesAndBufferedInput);
        var seedRulesExact = modes.All(mode =>
            mode.SeedRule == RunSeedRule.FreshLocalSeedPerRun);
        var restartRulesExact = modes.All(mode =>
            mode.RestartRule == RunRestartRule.FreshSeedSameModeAndBoard);

        var classicExpected = RunModeFeatures.Movement
            | RunModeFeatures.Wrapping
            | RunModeFeatures.FoodAndGrowth
            | RunModeFeatures.FixedSpeed
            | RunModeFeatures.SelfCollision
            | RunModeFeatures.Pause;
        var classicFeatureBoundaryExact = classic.Features == classicExpected
            && classic.AdaptiveState == RunAdaptiveState.Disabled
            && classic.AdaptivePolicyId == "none";
        var vibeFeatureBoundaryExact = vibe.Includes(
                classicExpected
                | RunModeFeatures.Starvation
                | RunModeFeatures.ComboScoring
                | RunModeFeatures.NearMisses
                | RunModeFeatures.PowerUps
                | RunModeFeatures.Progression
                | RunModeFeatures.FullFeedback
                | RunModeFeatures.AdaptivePolicy)
            && vibe.AdaptiveState == RunAdaptiveState.EnabledByDefault
            && vibe.AdaptivePolicyId == AdaptiveDifficultyPolicy.CurrentPolicyId;

        var classicProbeConfig = classicConfig with { Width = 20, Height = 4 };
        var longClassic = SnakeRun.CreateForTesting(
            classicProbeConfig,
            [new GridPoint(2, 2)],
            Direction.Right,
            food: new GridPoint(19, 3),
            hungerTicksRemaining: 1);
        var classicLifecycleClean = true;
        for (var step = 0; step < 1_200; step++)
        {
            var result = longClassic.Step();
            classicLifecycleClean &= result.Status == RunStatus.Running
                && result.OrderedEvents.All(item => item.Kind is not (
                    RunEventKind.StarvationWarning or RunEventKind.PowerSpawned));
        }

        var classicStarvationDisabled = classicLifecycleClean
            && longClassic.HungerTicksRemaining == 1;
        var classicPowerSpawningDisabled = longClassic.PowerPickup is null
            && classicConfig.PowerSpawnIntervalTicks == 0;

        var body = Enumerable.Range(0, 11).Select(x => new GridPoint(x, 1)).ToArray();
        var classicFood = SnakeRun.CreateForTesting(
            classicProbeConfig,
            body,
            Direction.Right,
            food: new GridPoint(11, 1),
            hungerTicksRemaining: 1,
            comboCount: 20);
        var classicFoodResult = classicFood.Step();
        var classicMinimalScoreExact = classicFood.Score == 10
            && classicFood.ComboCount == 0
            && classicFoodResult.OrderedEvents.Any(item =>
                item.Kind == RunEventKind.ScoreChanged && item.Value == 10)
            && classicFoodResult.OrderedEvents.All(item => item.Kind != RunEventKind.HungerReset);

        var vibeProbeConfig = vibeConfig with
        {
            Width = 20,
            Height = 4,
            PowerSpawnIntervalTicks = 0,
        };
        var vibeFood = SnakeRun.CreateForTesting(
            vibeProbeConfig,
            body,
            Direction.Right,
            food: new GridPoint(11, 1),
            hungerTicksRemaining: 1,
            comboCount: 20);
        var vibeFoodResult = vibeFood.Step();
        var starvingVibe = SnakeRun.CreateForTesting(
            vibeProbeConfig,
            [new GridPoint(2, 2)],
            Direction.Right,
            food: new GridPoint(19, 3),
            hungerTicksRemaining: 1,
            comboCount: 3);
        starvingVibe.Step();
        var vibePressureAndScoringActive = vibeFood.Score > classicFood.Score
            && vibeFood.ComboCount == 21
            && vibeFoodResult.OrderedEvents.Any(item => item.Kind == RunEventKind.HungerReset)
            && starvingVibe.Status == RunStatus.Dead
            && starvingVibe.DeathCause == DeathCause.Starvation;

        var terminalClassic = SnakeRun.CreateForTesting(
            classicConfig,
            [
                new GridPoint(1, 1),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(2, 1),
            ],
            Direction.Down,
            food: new GridPoint(10, 10),
            hungerTicksRemaining: classicConfig.StarvationTicks);
        terminalClassic.Step();
        var restartedClassic = terminalClassic.Restart(99UL);
        var restartRetainsModeAndBoard = restartedClassic.Mode == classic
            && restartedClassic.Configuration.Width == classic.BoardWidth
            && restartedClassic.Configuration.Height == classic.BoardHeight
            && restartedClassic.ConfigHash == terminalClassic.ConfigHash
            && restartedClassic.MasterSeed == 99UL;

        var keyboardAndControllerSelectionRoutesComplete = HasKeyboardAndController(GameActions.MoveLeft)
            && HasKeyboardAndController(GameActions.MoveRight)
            && HasKeyboardAndController(GameActions.Confirm);
        const ulong deterministicSeed = 20260808UL;
        var deterministicPerMode = modes.All(mode =>
        {
            var config = RunModeCatalog.CreateConfig(mode);
            return SnakeRun.Create(deterministicSeed, config).ComputeStateHash()
                == SnakeRun.Create(deterministicSeed, config).ComputeStateHash();
        });
        var crossModeScoreIsolation = !RunScoreIdentity.FromRun(
                SnakeRun.Create(deterministicSeed, classicConfig))
            .IsSameScoreCategory(RunScoreIdentity.FromRun(
                SnakeRun.Create(deterministicSeed, vibeConfig)));
        var vibeOptOutConfig = RunModeCatalog.CreateConfig(vibe, enableAdaptation: false);
        var vibeAdaptationDefaultEnabled = vibeConfig.EnableAdaptation
            && vibeConfig.AdaptivePolicyId == AdaptiveDifficultyPolicy.CurrentPolicyId
            && RunModeCatalog.GetScoreCategoryId(vibeConfig)
                == RunModeCatalog.VibeAdaptiveScoreCategoryId;
        var vibeOptOutScoreIsolation = !vibeOptOutConfig.EnableAdaptation
            && vibeOptOutConfig.AdaptivePolicyId == AdaptiveDifficultyPolicy.DisabledPolicyId
            && RunModeCatalog.GetScoreCategoryId(vibeOptOutConfig)
                == RunModeCatalog.VibeFixedScoreCategoryId
            && !RunScoreIdentity.FromRun(SnakeRun.Create(deterministicSeed, vibeConfig))
                .IsSameScoreCategory(
                    RunScoreIdentity.FromRun(
                        SnakeRun.Create(deterministicSeed, vibeOptOutConfig)));

        var passed = stableIdentitiesComplete
            && descriptionsComplete
            && scoreCategoriesSeparated
            && boardRulesExact
            && pauseRulesExact
            && seedRulesExact
            && restartRulesExact
            && classicFeatureBoundaryExact
            && vibeFeatureBoundaryExact
            && classicStarvationDisabled
            && classicPowerSpawningDisabled
            && classicMinimalScoreExact
            && vibePressureAndScoringActive
            && restartRetainsModeAndBoard
            && keyboardAndControllerSelectionRoutesComplete
            && deterministicPerMode
            && crossModeScoreIsolation
            && vibeAdaptationDefaultEnabled
            && vibeOptOutScoreIsolation;
        if (!passed)
        {
            throw new InvalidOperationException("Mode-contract qualification failed.");
        }

        return new ModeContractQualificationEvidence(
            SchemaVersion: 2,
            Kind: "mode-contract-qualification-v2",
            Passed: true,
            ModeCount: modes.Count,
            ConfigHashAlgorithm: RunConfig.ConfigHashAlgorithmId,
            StableIdentitiesComplete: stableIdentitiesComplete,
            DescriptionsComplete: descriptionsComplete,
            ScoreCategoriesSeparated: scoreCategoriesSeparated,
            BoardRulesExact: boardRulesExact,
            PauseRulesExact: pauseRulesExact,
            SeedRulesExact: seedRulesExact,
            RestartRulesExact: restartRulesExact,
            ClassicFeatureBoundaryExact: classicFeatureBoundaryExact,
            VibeFeatureBoundaryExact: vibeFeatureBoundaryExact,
            ClassicStarvationDisabled: classicStarvationDisabled,
            ClassicPowerSpawningDisabled: classicPowerSpawningDisabled,
            ClassicMinimalScoreExact: classicMinimalScoreExact,
            VibePressureAndScoringActive: vibePressureAndScoringActive,
            RestartRetainsModeAndBoard: restartRetainsModeAndBoard,
            KeyboardAndControllerSelectionRoutesComplete: keyboardAndControllerSelectionRoutesComplete,
            DeterministicPerMode: deterministicPerMode,
            CrossModeScoreIsolation: crossModeScoreIsolation,
            VibeAdaptationDefaultEnabled: vibeAdaptationDefaultEnabled,
            VibeOptOutScoreIsolation: vibeOptOutScoreIsolation,
            AdaptiveImplementationStatus: "enabled-bounded-vibe-default-with-opt-out",
            Modes: modes);
    }

    private static bool HasKeyboardAndController(string action)
    {
        var events = Godot.InputMap.ActionGetEvents(action);
        return events.Any(input => input is Godot.InputEventKey)
            && events.Any(input => input is Godot.InputEventJoypadButton
                or Godot.InputEventJoypadMotion);
    }
}
