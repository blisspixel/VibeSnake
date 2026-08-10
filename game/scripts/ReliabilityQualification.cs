using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record ReliabilityFirstDivergence(
    string ModeId,
    int RunIndex,
    int ComparedStep,
    int RunStep,
    string GameplaySeed,
    string ControllerSeed,
    string ExpectedDecision,
    string ActualDecision,
    bool ExpectedQueueOutcome,
    bool ActualQueueOutcome,
    string ExpectedStateHash,
    string ActualStateHash,
    string ExpectedStatus,
    string ActualStatus,
    string ExpectedDeathCause,
    string ActualDeathCause,
    IReadOnlyList<string> RecentCommands);

internal sealed record ReliabilitySimulationRow(
    string ModeId,
    int ModeVersion,
    string ScoreCategoryId,
    string ReferenceAiId,
    int RequiredComparedSteps,
    int ComparedSteps,
    int RunCount,
    int RestartCount,
    int StateHashCheckpointCount,
    bool DecisionsIdentical,
    bool QueueOutcomesIdentical,
    bool StepResultsIdentical,
    string DecisionAndStateTraceSha256,
    ReliabilityFirstDivergence? FirstDivergence);

internal sealed record EngineResourceCounts(
    int SceneNodeCount,
    long ObjectCount,
    long ResourceCount,
    long OrphanNodeCount);

internal sealed record EngineResourceSample(
    int CompletedRestarts,
    int SceneNodeCount,
    long ObjectCount,
    long ResourceCount,
    long OrphanNodeCount);

internal sealed record SpectatorRestartReliability(
    int RequiredRestarts,
    int CompletedRestarts,
    int StepsPerRestart,
    int CompletedSteps,
    int StateResetCount,
    bool EveryFreshSessionStartedPaused,
    bool EveryFreshSessionResetState,
    bool EverySessionAdvanced,
    int ManagedSessionReferencesRetained,
    bool EngineNodeCountStable,
    bool EngineObjectCountDidNotGrow,
    bool EngineResourceCountDidNotGrow,
    bool EngineOrphanNodeCountDidNotGrow,
    bool NoMonotonicStateOrResourceGrowth,
    IReadOnlyList<EngineResourceSample> ResourceSamples);

internal sealed record ReliabilityQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int RequiredStepsPerRuleset,
    int RulesetCount,
    int TotalComparedSimulationSteps,
    string ReferenceAiId,
    string AiAlgorithmId,
    string RandomAlgorithmId,
    IReadOnlyList<ReliabilitySimulationRow> Simulations,
    SpectatorRestartReliability SpectatorRestarts,
    IReadOnlyList<string> PendingGates)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

/// <summary>
/// Candidate-scale deterministic rules and spectator restart campaigns. This
/// runs only from qualification entry points and never from ordinary play.
/// </summary>
internal static class ReliabilityQualification
{
    public const int RequiredStepsPerRuleset = 100_000;
    public const int RequiredSpectatorRestarts = 100;
    public const int SpectatorStepsPerRestart = 8;
    public const int ResourceSampleInterval = 10;
    public const string ReferenceAiId = "balanced";

    private const ulong SimulationSeedBase = 0x0900_0400_0000_0001UL;
    private const ulong SimulationSeedStride = 0x0000_0001_0000_01B3UL;
    private const ulong ControllerSeedMask = 0xA180_0900_0400_0001UL;

    public static ReliabilityQualificationEvidence Run(
        Func<EngineResourceCounts> captureEngineResources,
        Action<ReliabilityFirstDivergence>? captureFirstDivergence = null)
    {
        ArgumentNullException.ThrowIfNull(captureEngineResources);
        var simulations = RunSimulationCampaign(captureFirstDivergence);
        var spectatorRestarts = RunSpectatorRestartCampaign(captureEngineResources);
        var rulesetCount = RunModeCatalog.All.Count;
        var totalComparedSteps = simulations.Sum(row => row.ComparedSteps);
        var simulationsPassed = simulations.Count == rulesetCount
            && simulations.Select(row => row.ModeId)
                .SequenceEqual(RunModeCatalog.All.Select(mode => mode.Id))
            && simulations.All(row => row.RequiredComparedSteps == RequiredStepsPerRuleset
                && row.ComparedSteps == RequiredStepsPerRuleset
                && row.ReferenceAiId == ReferenceAiId
                && row.RunCount > 0
                && row.RestartCount == row.RunCount - 1
                && row.StateHashCheckpointCount >= 100
                && row.DecisionsIdentical
                && row.QueueOutcomesIdentical
                && row.StepResultsIdentical
                && row.FirstDivergence is null
                && row.DecisionAndStateTraceSha256.Length == 64);
        var spectatorPassed = spectatorRestarts.CompletedRestarts == RequiredSpectatorRestarts
            && spectatorRestarts.CompletedSteps
                == RequiredSpectatorRestarts * SpectatorStepsPerRestart
            && spectatorRestarts.StateResetCount == RequiredSpectatorRestarts
            && spectatorRestarts.EveryFreshSessionStartedPaused
            && spectatorRestarts.EveryFreshSessionResetState
            && spectatorRestarts.EverySessionAdvanced
            && spectatorRestarts.ManagedSessionReferencesRetained == 0
            && spectatorRestarts.NoMonotonicStateOrResourceGrowth;
        var passed = simulationsPassed
            && totalComparedSteps == RequiredStepsPerRuleset * rulesetCount
            && spectatorPassed;
        return new ReliabilityQualificationEvidence(
            SchemaVersion: 1,
            Kind: "candidate-reliability-qualification-v1",
            Passed: passed,
            RequiredStepsPerRuleset: RequiredStepsPerRuleset,
            RulesetCount: rulesetCount,
            TotalComparedSimulationSteps: totalComparedSteps,
            ReferenceAiId: ReferenceAiId,
            AiAlgorithmId: AiPersonalityController.AlgorithmId,
            RandomAlgorithmId: Pcg32.AlgorithmId,
            Simulations: simulations,
            SpectatorRestarts: spectatorRestarts,
            PendingGates:
            [
                "retained-release-execution-on-windows-macos-linux",
            ]);
    }

    private static IReadOnlyList<ReliabilitySimulationRow> RunSimulationCampaign(
        Action<ReliabilityFirstDivergence>? captureFirstDivergence) =>
        RunModeCatalog.All
            .Select(mode => RunSimulation(mode, captureFirstDivergence))
            .ToArray();

    private static ReliabilitySimulationRow RunSimulation(
        RunModeDefinition mode,
        Action<ReliabilityFirstDivergence>? captureFirstDivergence)
    {
        var config = RunModeCatalog.CreateConfig(mode);
        var personality = AiPersonalityCatalog.GetBuiltIn(ReferenceAiId);
        using var trace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var directionBuffer = new byte[8_192];
        var bufferedDirections = 0;
        var comparedSteps = 0;
        var runCount = 0;
        var checkpointCount = 0;
        var decisionsIdentical = true;
        var queueOutcomesIdentical = true;
        var stepResultsIdentical = true;
        ReliabilityFirstDivergence? firstDivergence = null;

        while (comparedSteps < RequiredStepsPerRuleset)
        {
            var runIndex = runCount;
            var seed = SimulationSeedBase
                + ((ulong)mode.Version << 56)
                + ((ulong)runIndex * SimulationSeedStride);
            var controllerSeed = seed ^ ControllerSeedMask;
            var left = SnakeRun.Create(seed, config);
            var right = SnakeRun.Create(seed, config);
            var leftController = new AiPersonalityController(personality, controllerSeed);
            var rightController = new AiPersonalityController(personality, controllerSeed);
            var recentCommands = new Queue<string>(LocalDiagnostics.MaximumRecentCommands);
            var runStep = 0;
            runCount++;

            while (left.Status == RunStatus.Running
                && comparedSteps < RequiredStepsPerRuleset)
            {
                var leftDecision = leftController.SelectDecision(left);
                var rightDecision = rightController.SelectDecision(right);
                var decisionMatched = leftDecision == rightDecision;
                decisionsIdentical &= decisionMatched;
                if (recentCommands.Count == LocalDiagnostics.MaximumRecentCommands)
                {
                    recentCommands.Dequeue();
                }

                recentCommands.Enqueue(leftDecision.Direction.ToString());
                directionBuffer[bufferedDirections++] = (byte)leftDecision.Direction;
                if (bufferedDirections == directionBuffer.Length)
                {
                    trace.AppendData(directionBuffer);
                    bufferedDirections = 0;
                }

                var leftQueued = left.QueueDirection(leftDecision.Direction);
                var rightQueued = right.QueueDirection(rightDecision.Direction);
                var queueMatched = leftQueued == rightQueued;
                queueOutcomesIdentical &= queueMatched;
                var leftStep = left.Step();
                var rightStep = right.Step();
                comparedSteps++;
                runStep++;
                var stepMatched = leftStep.StateHash == rightStep.StateHash
                    && leftStep.Events == rightStep.Events
                    && leftStep.Status == rightStep.Status
                    && leftStep.DeathCause == rightStep.DeathCause
                    && leftStep.OrderedEvents.SequenceEqual(rightStep.OrderedEvents);
                stepResultsIdentical &= stepMatched;
                if (firstDivergence is null
                    && (!decisionMatched || !queueMatched || !stepMatched))
                {
                    firstDivergence = new ReliabilityFirstDivergence(
                        ModeId: mode.Id,
                        RunIndex: runIndex,
                        ComparedStep: comparedSteps,
                        RunStep: runStep,
                        GameplaySeed: seed.ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
                        ControllerSeed: controllerSeed.ToString(
                            "x16",
                            System.Globalization.CultureInfo.InvariantCulture),
                        ExpectedDecision: leftDecision.ToString(),
                        ActualDecision: rightDecision.ToString(),
                        ExpectedQueueOutcome: leftQueued,
                        ActualQueueOutcome: rightQueued,
                        ExpectedStateHash: leftStep.StateHash,
                        ActualStateHash: rightStep.StateHash,
                        ExpectedStatus: leftStep.Status.ToString(),
                        ActualStatus: rightStep.Status.ToString(),
                        ExpectedDeathCause: leftStep.DeathCause.ToString(),
                        ActualDeathCause: rightStep.DeathCause.ToString(),
                        RecentCommands: recentCommands.ToArray());
                    captureFirstDivergence?.Invoke(firstDivergence);
                }

                if (comparedSteps % 1_000 == 0 || left.Status != RunStatus.Running)
                {
                    trace.AppendData(Encoding.ASCII.GetBytes(leftStep.StateHash));
                    checkpointCount++;
                }
            }
        }

        if (bufferedDirections > 0)
        {
            trace.AppendData(directionBuffer.AsSpan(0, bufferedDirections));
        }

        return new ReliabilitySimulationRow(
            ModeId: mode.Id,
            ModeVersion: mode.Version,
            ScoreCategoryId: RunModeCatalog.GetScoreCategoryId(config),
            ReferenceAiId: personality.Id,
            RequiredComparedSteps: RequiredStepsPerRuleset,
            ComparedSteps: comparedSteps,
            RunCount: runCount,
            RestartCount: runCount - 1,
            StateHashCheckpointCount: checkpointCount,
            DecisionsIdentical: decisionsIdentical,
            QueueOutcomesIdentical: queueOutcomesIdentical,
            StepResultsIdentical: stepResultsIdentical,
            DecisionAndStateTraceSha256: Convert.ToHexString(trace.GetHashAndReset())
                .ToLowerInvariant(),
            FirstDivergence: firstDivergence);
    }

    private static SpectatorRestartReliability RunSpectatorRestartCampaign(
        Func<EngineResourceCounts> captureEngineResources)
    {
        var selection = SpectatorSelection.CreateDefault();
        var expectedInitialStateHash = new SpectatorMatchSession(selection).ViewedSnapshot.StateHash;
        var weakReferences = new List<WeakReference<SpectatorMatchSession>>(
            RequiredSpectatorRestarts);
        var samples = new List<EngineResourceSample>(
            (RequiredSpectatorRestarts / ResourceSampleInterval) + 1)
        {
            Sample(0, captureEngineResources()),
        };
        var stateResetCount = 0;
        var everyFreshSessionStartedPaused = true;
        var everyFreshSessionResetState = true;
        var everySessionAdvanced = true;
        var completedSteps = 0;

        for (var restart = 1; restart <= RequiredSpectatorRestarts; restart++)
        {
            var probe = RunSpectatorRestartProbe(selection, expectedInitialStateHash);
            weakReferences.Add(probe.Reference);
            stateResetCount += probe.StateReset ? 1 : 0;
            everyFreshSessionStartedPaused &= probe.StartedPaused;
            everyFreshSessionResetState &= probe.StateReset;
            everySessionAdvanced &= probe.Advanced;
            completedSteps += probe.CompletedSteps;
            if (restart % ResourceSampleInterval == 0)
            {
                samples.Add(Sample(restart, captureEngineResources()));
            }
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        var retained = weakReferences.Count(reference => reference.TryGetTarget(out _));
        var baseline = samples[0];
        var engineNodeCountStable = samples.All(sample =>
            sample.SceneNodeCount == baseline.SceneNodeCount);
        var engineObjectCountDidNotGrow = samples.All(sample =>
            sample.ObjectCount <= baseline.ObjectCount);
        var engineResourceCountDidNotGrow = samples.All(sample =>
            sample.ResourceCount <= baseline.ResourceCount);
        var engineOrphanNodeCountDidNotGrow = samples.All(sample =>
            sample.OrphanNodeCount <= baseline.OrphanNodeCount);
        var noGrowth = engineNodeCountStable
            && engineObjectCountDidNotGrow
            && engineResourceCountDidNotGrow
            && engineOrphanNodeCountDidNotGrow
            && retained == 0;

        return new SpectatorRestartReliability(
            RequiredRestarts: RequiredSpectatorRestarts,
            CompletedRestarts: RequiredSpectatorRestarts,
            StepsPerRestart: SpectatorStepsPerRestart,
            CompletedSteps: completedSteps,
            StateResetCount: stateResetCount,
            EveryFreshSessionStartedPaused: everyFreshSessionStartedPaused,
            EveryFreshSessionResetState: everyFreshSessionResetState,
            EverySessionAdvanced: everySessionAdvanced,
            ManagedSessionReferencesRetained: retained,
            EngineNodeCountStable: engineNodeCountStable,
            EngineObjectCountDidNotGrow: engineObjectCountDidNotGrow,
            EngineResourceCountDidNotGrow: engineResourceCountDidNotGrow,
            EngineOrphanNodeCountDidNotGrow: engineOrphanNodeCountDidNotGrow,
            NoMonotonicStateOrResourceGrowth: noGrowth,
            ResourceSamples: samples);
    }

    private static SpectatorRestartProbe RunSpectatorRestartProbe(
        SpectatorSelection selection,
        string expectedInitialStateHash)
    {
        var session = new SpectatorMatchSession(selection);
        var startedPaused = session.Paused && session.StepCount == 0;
        var stateReset = session.ViewedSnapshot.StateHash == expectedInitialStateHash
            && session.ViewedPersonalityId == selection.PersonalityId
            && session.PlaybackSpeedIndex == selection.PlaybackSpeedIndex;
        session.SetPaused(false);
        var completedSteps = 0;
        for (var step = 0; step < SpectatorStepsPerRestart; step++)
        {
            var advance = session.Advance();
            if (!advance.RulesAdvanced)
            {
                break;
            }

            completedSteps++;
        }

        return new SpectatorRestartProbe(
            new WeakReference<SpectatorMatchSession>(session),
            startedPaused,
            stateReset,
            completedSteps == SpectatorStepsPerRestart,
            completedSteps);
    }

    private static EngineResourceSample Sample(
        int completedRestarts,
        EngineResourceCounts counts) => new(
            CompletedRestarts: completedRestarts,
            SceneNodeCount: counts.SceneNodeCount,
            ObjectCount: counts.ObjectCount,
            ResourceCount: counts.ResourceCount,
            OrphanNodeCount: counts.OrphanNodeCount);

    private sealed record SpectatorRestartProbe(
        WeakReference<SpectatorMatchSession> Reference,
        bool StartedPaused,
        bool StateReset,
        bool Advanced,
        int CompletedSteps);
}
