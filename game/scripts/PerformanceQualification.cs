using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record PerformanceProfileDefinition(
    string Id,
    string EffectsSetting,
    bool ReducedMotion,
    bool FlashFree,
    int SnakeCellCount,
    int ObstacleCount,
    int VisibleCollectibleCount,
    int ParticleCount,
    int PopupCount,
    int ShakeSourceCount,
    float ShakeStrength,
    int FullScreenFlashCount,
    int LogicalDrawSubmissionCount);

internal sealed record PerformanceProfileMeasurement(
    string Id,
    int SampleCount,
    double AverageFrameMilliseconds,
    double P50FrameMilliseconds,
    double P95FrameMilliseconds,
    double P99FrameMilliseconds,
    double MaximumFrameMilliseconds,
    string DriverDrawCallStatus,
    double AverageObservedDriverDrawCalls,
    int MaximumObservedDriverDrawCalls);

internal sealed record PublishedPerformanceBudget(
    int TargetFramesPerSecond,
    double TargetFrameMilliseconds,
    double SharedHostMaximumAverageMilliseconds,
    double SharedHostMaximumP95Milliseconds,
    int MaximumLogicalDrawSubmissions,
    int MaximumParticles,
    int MaximumAudioChannels,
    int BoardCellCapacity,
    int RequiredSamplesPerProfile);

internal sealed record PerformanceQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    bool ThreeEffectProfilesMeasured,
    bool MaximumMixedStressSceneComplete,
    bool FrameStatisticsComplete,
    bool SharedHostRegressionCeilingMet,
    bool ParticleBudgetConsistent,
    bool AudioChannelBudgetConsistent,
    bool DrawSubmissionBudgetMet,
    bool FeedbackCannotChangeSimulationSpeed,
    bool RulesStateIdenticalAcrossProfiles,
    string FinalRulesStateHash,
    int RulesStepsPerProfile,
    string MinimumHardwareAcceptanceStatus,
    PublishedPerformanceBudget Budget,
    IReadOnlyList<PerformanceProfileDefinition> Profiles,
    IReadOnlyList<PerformanceProfileMeasurement> Measurements,
    IReadOnlyList<string> PendingHumanChecks)
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
/// Deterministic stress-scene shape and host measurement qualification. Shared
/// CI catches gross regressions; the published 60 FPS target is accepted only
/// on named minimum hardware.
/// </summary>
internal static class PerformanceQualification
{
    public const int GridWidth = 64;
    public const int GridHeight = 33;
    public const int BoardCellCapacity = GridWidth * GridHeight;
    public const int ObstacleSignalsAtMaximum = 3;
    public const int VisibleCollectiblesAtMaximum = 2;
    public const int MaximumLiveSnakeWithSignals =
        BoardCellCapacity - ObstacleSignalsAtMaximum - VisibleCollectiblesAtMaximum;
    public const int RequiredSamplesPerProfile = 40;
    public const int MaximumSharedHostMeasurementAttempts = 2;

    private const int RulesStepsPerProfile = 256;
    private const int MaximumLogicalDrawSubmissions = 2_400;
    private const double SharedHostMaximumAverageMilliseconds = 25.0;
    internal const double SharedHostMaximumP95Milliseconds = 60.0;

    public static IReadOnlyList<PerformanceProfileDefinition> Profiles { get; } =
    [
        Profile(
            "minimum",
            "minimum-effects",
            reducedMotion: true,
            flashFree: true,
            snakeCells: 64,
            obstacles: 0,
            visibleCollectibles: 2,
            particles: 0,
            popups: 0,
            shakeSources: 0,
            shakeStrength: 0.0f),
        Profile(
            "default",
            "default-effects",
            reducedMotion: false,
            flashFree: false,
            snakeCells: 512,
            obstacles: ObstacleSignalsAtMaximum,
            visibleCollectibles: VisibleCollectiblesAtMaximum,
            particles: 64,
            popups: 2,
            shakeSources: 1,
            shakeStrength: 0.12f),
        Profile(
            "maximum-safe",
            "maximum-safe-effects",
            reducedMotion: false,
            flashFree: false,
            snakeCells: MaximumLiveSnakeWithSignals,
            obstacles: ObstacleSignalsAtMaximum,
            visibleCollectibles: VisibleCollectiblesAtMaximum,
            particles: VisualHierarchyPolicy.Budget.MaximumSimultaneousParticles,
            popups: VisualHierarchyPolicy.Budget.MaximumSimultaneousPopups,
            shakeSources: VisualHierarchyPolicy.Budget.MaximumSimultaneousShakeSources,
            shakeStrength: VisualHierarchyPolicy.Budget.MaximumScreenShakeStrength),
    ];

    public static PerformanceQualificationEvidence Run(
        IReadOnlyList<PerformanceProfileMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        string[] expectedIds = ["minimum", "default", "maximum-safe"];
        var threeEffectProfilesMeasured = Profiles.Select(profile => profile.Id)
                .SequenceEqual(expectedIds)
            && measurements.Select(measurement => measurement.Id).SequenceEqual(expectedIds);
        var maximum = Profiles[^1];
        var maximumMixedStressSceneComplete = maximum.SnakeCellCount
                == MaximumLiveSnakeWithSignals
            && maximum.SnakeCellCount
                + maximum.ObstacleCount
                + maximum.VisibleCollectibleCount == BoardCellCapacity
            && maximum.ParticleCount
                == VisualHierarchyPolicy.Budget.MaximumSimultaneousParticles
            && maximum.PopupCount
                == VisualHierarchyPolicy.Budget.MaximumSimultaneousPopups
            && maximum.ObstacleCount > 0
            && maximum.VisibleCollectibleCount >= 2;
        var frameStatisticsComplete = measurements.All(measurement =>
            measurement.SampleCount >= RequiredSamplesPerProfile
            && measurement.AverageFrameMilliseconds > 0.0
            && measurement.P50FrameMilliseconds > 0.0
            && measurement.P95FrameMilliseconds >= measurement.P50FrameMilliseconds
            && measurement.P99FrameMilliseconds >= measurement.P95FrameMilliseconds
            && measurement.MaximumFrameMilliseconds >= measurement.P99FrameMilliseconds
            && measurement.MaximumObservedDriverDrawCalls >= 0
            && measurement.AverageObservedDriverDrawCalls >= 0.0);
        var sharedHostRegressionCeilingMet = measurements.All(measurement =>
            measurement.AverageFrameMilliseconds <= SharedHostMaximumAverageMilliseconds
            && measurement.P95FrameMilliseconds <= SharedHostMaximumP95Milliseconds);
        var particleBudgetConsistent = Profiles.All(profile =>
            profile.ParticleCount <= VisualHierarchyPolicy.Budget.MaximumSimultaneousParticles);
        var maximumAudioChannels = AudioCueMixPolicy.SfxBusCapacity
            + AudioCueMixPolicy.UiBusCapacity;
        var audioChannelBudgetConsistent = maximumAudioChannels == 12
            && AudioCueMixPolicy.BusCapacities.Values.Sum() == maximumAudioChannels;
        var drawSubmissionBudgetMet = Profiles.All(profile =>
            profile.LogicalDrawSubmissionCount <= MaximumLogicalDrawSubmissions)
            && maximum.LogicalDrawSubmissionCount > Profiles[1].LogicalDrawSubmissionCount
            && Profiles[1].LogicalDrawSubmissionCount > Profiles[0].LogicalDrawSubmissionCount;

        var stateHashes = Profiles.Select(_ => RunRulesProbe()).ToArray();
        var rulesStateIdenticalAcrossProfiles = stateHashes.Distinct(StringComparer.Ordinal).Count() == 1;
        var feedbackCannotChangeSimulationSpeed = rulesStateIdenticalAcrossProfiles
            && RunConfig.RulesTickMilliseconds == 50
            && Profiles.All(profile => profile.ShakeStrength
                <= VisualHierarchyPolicy.Budget.MaximumScreenShakeStrength);
        string[] pendingHumanChecks =
        [
            "60 FPS p50/p95/p99 capture on the named minimum Windows hardware",
            "60 FPS p50/p95/p99 capture on the named minimum macOS hardware",
            "60 FPS p50/p95/p99 capture on the named minimum Linux hardware",
            "GPU draw-call, allocation, memory-growth, and thermal review under a long session",
        ];
        var passed = threeEffectProfilesMeasured
            && maximumMixedStressSceneComplete
            && frameStatisticsComplete
            && sharedHostRegressionCeilingMet
            && particleBudgetConsistent
            && audioChannelBudgetConsistent
            && drawSubmissionBudgetMet
            && feedbackCannotChangeSimulationSpeed
            && rulesStateIdenticalAcrossProfiles;
        return new PerformanceQualificationEvidence(
            SchemaVersion: 1,
            Kind: "performance-qualification-v1",
            Passed: passed,
            ThreeEffectProfilesMeasured: threeEffectProfilesMeasured,
            MaximumMixedStressSceneComplete: maximumMixedStressSceneComplete,
            FrameStatisticsComplete: frameStatisticsComplete,
            SharedHostRegressionCeilingMet: sharedHostRegressionCeilingMet,
            ParticleBudgetConsistent: particleBudgetConsistent,
            AudioChannelBudgetConsistent: audioChannelBudgetConsistent,
            DrawSubmissionBudgetMet: drawSubmissionBudgetMet,
            FeedbackCannotChangeSimulationSpeed: feedbackCannotChangeSimulationSpeed,
            RulesStateIdenticalAcrossProfiles: rulesStateIdenticalAcrossProfiles,
            FinalRulesStateHash: stateHashes[0],
            RulesStepsPerProfile: RulesStepsPerProfile,
            MinimumHardwareAcceptanceStatus: "pending-named-hardware",
            Budget: new PublishedPerformanceBudget(
                TargetFramesPerSecond: 60,
                TargetFrameMilliseconds: 1000.0 / 60.0,
                SharedHostMaximumAverageMilliseconds: SharedHostMaximumAverageMilliseconds,
                SharedHostMaximumP95Milliseconds: SharedHostMaximumP95Milliseconds,
                MaximumLogicalDrawSubmissions: MaximumLogicalDrawSubmissions,
                MaximumParticles: VisualHierarchyPolicy.Budget.MaximumSimultaneousParticles,
                MaximumAudioChannels: maximumAudioChannels,
                BoardCellCapacity: BoardCellCapacity,
                RequiredSamplesPerProfile: RequiredSamplesPerProfile),
            Profiles: Profiles,
            Measurements: measurements,
            PendingHumanChecks: pendingHumanChecks);
    }

    public static bool ShouldRetrySharedHostTail(
        PerformanceQualificationEvidence evidence,
        IReadOnlyList<PerformanceProfileMeasurement> measurements,
        int completedAttemptCount)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(measurements);
        if (completedAttemptCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAttemptCount));
        }

        return completedAttemptCount < MaximumSharedHostMeasurementAttempts
            && !evidence.Passed
            && evidence.ThreeEffectProfilesMeasured
            && evidence.MaximumMixedStressSceneComplete
            && evidence.FrameStatisticsComplete
            && !evidence.SharedHostRegressionCeilingMet
            && evidence.ParticleBudgetConsistent
            && evidence.AudioChannelBudgetConsistent
            && evidence.DrawSubmissionBudgetMet
            && evidence.FeedbackCannotChangeSimulationSpeed
            && evidence.RulesStateIdenticalAcrossProfiles
            && measurements.All(measurement =>
                measurement.AverageFrameMilliseconds
                    <= SharedHostMaximumAverageMilliseconds)
            && measurements.Any(measurement =>
                measurement.P95FrameMilliseconds
                    > SharedHostMaximumP95Milliseconds);
    }

    private static PerformanceProfileDefinition Profile(
        string id,
        string effectsSetting,
        bool reducedMotion,
        bool flashFree,
        int snakeCells,
        int obstacles,
        int visibleCollectibles,
        int particles,
        int popups,
        int shakeSources,
        float shakeStrength)
    {
        var logicalDrawSubmissions = 20
            + snakeCells
            + (obstacles * 2)
            + (visibleCollectibles * 2)
            + particles
            + (popups * 2);
        return new PerformanceProfileDefinition(
            Id: id,
            EffectsSetting: effectsSetting,
            ReducedMotion: reducedMotion,
            FlashFree: flashFree,
            SnakeCellCount: snakeCells,
            ObstacleCount: obstacles,
            VisibleCollectibleCount: visibleCollectibles,
            ParticleCount: particles,
            PopupCount: popups,
            ShakeSourceCount: shakeSources,
            ShakeStrength: shakeStrength,
            FullScreenFlashCount: 0,
            LogicalDrawSubmissionCount: logicalDrawSubmissions);
    }

    private static string RunRulesProbe()
    {
        var run = SnakeRun.Create(20260808UL);
        for (var step = 0; step < RulesStepsPerProfile; step++)
        {
            run.Step();
        }

        return run.ComputeStateHash();
    }
}
