using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal readonly record struct CapturePresentationState(bool Enabled)
{
    public static CapturePresentationState Visible => new(false);

    public bool ShowRunHud => !Enabled;

    public bool ShowReplayControls => !Enabled;

    public bool ShowTerminalOverlay => !Enabled;

    public bool ShowAudioStatus => !Enabled;

    public bool ShowDebugOverlays => !Enabled;

    public bool ShowSpectatorOverlays => !Enabled;

    public CapturePresentationState Toggle() => new(!Enabled);
}

internal sealed record CaptureSharingQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    bool DefaultCaptureModeOff,
    int HiddenOverlayFamilyCount,
    bool RunHudHidden,
    bool ReplayControlsHidden,
    bool TerminalOverlayHidden,
    bool AudioStatusHidden,
    bool DebugOverlayHidden,
    bool SpectatorOverlayHidden,
    bool KeyboardRouteComplete,
    bool ControllerRouteComplete,
    int ReplaySpeedCount,
    bool DeterministicReplayCaptureComplete,
    bool RulesStateUnchangedByCaptureMode,
    int RunSummarySchemaVersion,
    int RunSummaryFieldCount,
    bool VersionMetadataComplete,
    bool RulesMetadataComplete,
    bool ReplayVerificationMetadataComplete,
    bool SummaryExportComplete,
    bool SummaryAtomicAndIdempotent,
    bool PlayerIdentityExcluded,
    bool PrivatePathsExcluded,
    string HumanReviewStatus,
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

internal static class CaptureSharingQualification
{
    public const int HiddenOverlayFamilyCount = 6;
    public const int ReplaySpeedCount = 4;
    public const int RunSummaryFieldCount = 24;

    public static CaptureSharingQualificationEvidence Run(
        bool keyboardRouteComplete,
        bool controllerRouteComplete,
        bool summaryExportComplete,
        bool summaryAtomicAndIdempotent)
    {
        var visible = CapturePresentationState.Visible;
        var clean = visible.Toggle();
        var restored = clean.Toggle();
        var run = SnakeRun.Create(20260808UL);
        var before = run.ComputeStateHash();
        IReadOnlyList<Direction>[] commands =
        [
            [Direction.Up],
            [Direction.Left],
            [Direction.Down],
            [Direction.Right],
        ];
        var replay = RunReplay.Capture(
            run,
            commands,
            checkpointInterval: 1,
            appVersion: ProductIdentity.AppVersion,
            capturedAtUtc: "2026-08-08T08:08:08.008Z");
        var playback = new RunReplayPlayback(replay);
        playback.Seek(3);
        var firstCaptureHash = playback.CurrentSnapshot.StateHash;
        playback.Reset();
        playback.Seek(3);
        var secondCaptureHash = playback.CurrentSnapshot.StateHash;
        var summary = ReplayCaptureSummary.Create(replay, ProductIdentity.AppVersion);
        var summaryPayload = summary.Serialize();
        using var summaryJson = JsonDocument.Parse(summaryPayload);
        var summaryFields = summaryJson.RootElement.EnumerateObject().Count();
        var versionMetadataComplete = summary.ExportingAppVersion == ProductIdentity.AppVersion
            && summary.ReplayAppVersion == ProductIdentity.AppVersion;
        var rulesMetadataComplete = summary.RulesetId == SnakeRun.RulesetId
            && summary.RulesVersion == SnakeRun.RulesVersion
            && RunModeCatalog.IsSupportedIdentity(summary.ModeId, summary.ModeVersion)
            && summary.ScoreCategoryId == RunModeCatalog.VibeFixedScoreCategoryId
            && summary.ConfigHash == replay.ConfigHash
            && summary.ConfigHashAlgorithm == replay.ConfigHashAlgorithm;
        var replayVerificationMetadataComplete = summary.ReplayPayloadHash == replay.PayloadHash
            && summary.ReplayIntegrityAlgorithm == RunReplay.IntegrityAlgorithmId
            && summary.FinalStateHash == replay.Outcome.StateHash
            && summary.StepCount == replay.Outcome.StepCount;
        var playerIdentityExcluded = !summary.ContainsPlayerIdentity
            && !summaryPayload.Contains("playerName", StringComparison.OrdinalIgnoreCase)
            && !summaryPayload.Contains("displayName", StringComparison.OrdinalIgnoreCase)
            && !summaryPayload.Contains("profile", StringComparison.OrdinalIgnoreCase);
        var privatePathsExcluded = !summary.ContainsPrivatePaths
            && !summaryPayload.Contains("user://", StringComparison.OrdinalIgnoreCase)
            && !summaryPayload.Contains(":\\", StringComparison.Ordinal)
            && !summaryPayload.Contains("/home/", StringComparison.OrdinalIgnoreCase);
        var deterministicReplayCaptureComplete = firstCaptureHash == secondCaptureHash
            && playback.StepIndex == 3
            && replay.Verify().IsValid;
        var rulesStateUnchangedByCaptureMode = run.ComputeStateHash() == before
            && restored == visible;
        string[] pendingHumanChecks =
        [
            "Retain clean gameplay and replay captures on Windows, macOS, and Linux",
            "Review capture composition at minimum, 4:3, 16:9, ultrawide, and high-density sizes",
            "Confirm player-facing export language clearly distinguishes local files from uploads",
            "Capture final trailer footage only from an approved release candidate and content pack",
        ];
        var passed = !visible.Enabled
            && clean.Enabled
            && !clean.ShowRunHud
            && !clean.ShowReplayControls
            && !clean.ShowTerminalOverlay
            && !clean.ShowAudioStatus
            && !clean.ShowDebugOverlays
            && !clean.ShowSpectatorOverlays
            && keyboardRouteComplete
            && controllerRouteComplete
            && ReplaySpeedCount == 4
            && deterministicReplayCaptureComplete
            && rulesStateUnchangedByCaptureMode
            && summary.SchemaVersion == ReplayCaptureSummary.CurrentSchemaVersion
            && summaryFields == RunSummaryFieldCount
            && versionMetadataComplete
            && rulesMetadataComplete
            && replayVerificationMetadataComplete
            && summaryExportComplete
            && summaryAtomicAndIdempotent
            && playerIdentityExcluded
            && privatePathsExcluded;
        if (!passed)
        {
            throw new InvalidOperationException("Capture and sharing qualification failed.");
        }

        return new CaptureSharingQualificationEvidence(
            SchemaVersion: 1,
            Kind: "capture-sharing-qualification-v1",
            Passed: true,
            DefaultCaptureModeOff: true,
            HiddenOverlayFamilyCount: HiddenOverlayFamilyCount,
            RunHudHidden: true,
            ReplayControlsHidden: true,
            TerminalOverlayHidden: true,
            AudioStatusHidden: true,
            DebugOverlayHidden: true,
            SpectatorOverlayHidden: true,
            KeyboardRouteComplete: keyboardRouteComplete,
            ControllerRouteComplete: controllerRouteComplete,
            ReplaySpeedCount: ReplaySpeedCount,
            DeterministicReplayCaptureComplete: deterministicReplayCaptureComplete,
            RulesStateUnchangedByCaptureMode: rulesStateUnchangedByCaptureMode,
            RunSummarySchemaVersion: summary.SchemaVersion,
            RunSummaryFieldCount: summaryFields,
            VersionMetadataComplete: versionMetadataComplete,
            RulesMetadataComplete: rulesMetadataComplete,
            ReplayVerificationMetadataComplete: replayVerificationMetadataComplete,
            SummaryExportComplete: summaryExportComplete,
            SummaryAtomicAndIdempotent: summaryAtomicAndIdempotent,
            PlayerIdentityExcluded: playerIdentityExcluded,
            PrivatePathsExcluded: privatePathsExcluded,
            HumanReviewStatus: "pending-platform-capture-review",
            PendingHumanChecks: pendingHumanChecks);
    }
}
