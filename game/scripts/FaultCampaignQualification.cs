using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record CandidateFaultRow(
    string FaultId,
    string InjectionBoundary,
    bool FaultDetected,
    bool ExistingDataPreserved,
    bool RecoveryVerified,
    bool RulesStateUnchanged);

internal sealed record CandidateTriageProbe(
    string ReportKind,
    bool ReportRetained,
    bool SchemaValid,
    bool PrivacySafe,
    bool ReproductionFieldsComplete,
    string FileName,
    string Sha256);

internal sealed record FaultCampaignQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int RequiredFaultCount,
    int CompletedFaultCount,
    bool EveryFaultDetected,
    bool EveryExistingDataBoundaryPreserved,
    bool EveryRecoveryPathVerified,
    bool RulesStateUnchangedAcrossCampaign,
    IReadOnlyList<CandidateFaultRow> Faults,
    CandidateTriageProbe CrashTriage,
    CandidateTriageProbe DivergenceTriage,
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
/// Portable candidate fault injection through production persistence, content,
/// audio-recovery, and local-diagnostics boundaries. This runs only from the
/// explicit smoke qualification path.
/// </summary>
internal static class FaultCampaignQualification
{
    public const int RequiredFaultCount = 7;

    private const ulong RulesProbeSeed = 0x0900_04FA_0170_0001UL;
    private const int DiskFullHResult = unchecked((int)0x80070070);

    private static readonly string[] RequiredFaultIds =
    [
        "interrupted-write",
        "corrupt-json",
        "full-disk",
        "read-only-data-directory",
        "missing-resource",
        "invalid-content-pack",
        "unavailable-audio",
    ];

    public static FaultCampaignQualificationEvidence Run(
        string absoluteUserDataRoot,
        string platform,
        CoreOnlyOfflineQualificationEvidence contentEvidence,
        LocalDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteUserDataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentNullException.ThrowIfNull(contentEvidence);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!Path.IsPathFullyQualified(absoluteUserDataRoot))
        {
            throw new ArgumentException(
                "The candidate fault root must be absolute.",
                nameof(absoluteUserDataRoot));
        }

        var root = Path.Combine(
            Path.GetFullPath(absoluteUserDataRoot),
            "qualification",
            "candidate-faults");
        Directory.CreateDirectory(root);
        var rulesProbe = SnakeRun.Create(RulesProbeSeed);
        var rulesHash = rulesProbe.ComputeStateHash();
        CandidateFaultRow[] faults =
        [
            ProbeInterruptedWrite(Path.Combine(root, "interrupted-write"), rulesProbe, rulesHash),
            ProbeCorruptJson(Path.Combine(root, "corrupt-json"), rulesProbe, rulesHash),
            ProbeFullDisk(Path.Combine(root, "full-disk"), rulesProbe, rulesHash),
            ProbeReadOnlyDirectory(Path.Combine(root, "read-only"), rulesProbe, rulesHash),
            ProbeMissingResource(contentEvidence, rulesProbe, rulesHash),
            ProbeInvalidContentPack(contentEvidence, rulesProbe, rulesHash),
            ProbeUnavailableAudio(rulesProbe, rulesHash),
        ];
        var crashTriage = ProbeCrashTriage(diagnostics, platform);
        var divergenceTriage = ProbeDivergenceTriage(diagnostics, platform);
        var everyFaultDetected = faults.All(row => row.FaultDetected);
        var everyDataBoundaryPreserved = faults.All(row => row.ExistingDataPreserved);
        var everyRecoveryVerified = faults.All(row => row.RecoveryVerified);
        var rulesStateUnchanged = faults.All(row => row.RulesStateUnchanged)
            && rulesProbe.ComputeStateHash() == rulesHash;
        var passed = faults.Length == RequiredFaultCount
            && faults.Select(row => row.FaultId).SequenceEqual(RequiredFaultIds)
            && everyFaultDetected
            && everyDataBoundaryPreserved
            && everyRecoveryVerified
            && rulesStateUnchanged
            && TriagePassed(crashTriage)
            && TriagePassed(divergenceTriage);
        return new FaultCampaignQualificationEvidence(
            SchemaVersion: 1,
            Kind: "candidate-fault-campaign-v1",
            Passed: passed,
            RequiredFaultCount: RequiredFaultCount,
            CompletedFaultCount: faults.Length,
            EveryFaultDetected: everyFaultDetected,
            EveryExistingDataBoundaryPreserved: everyDataBoundaryPreserved,
            EveryRecoveryPathVerified: everyRecoveryVerified,
            RulesStateUnchangedAcrossCampaign: rulesStateUnchanged,
            Faults: faults,
            CrashTriage: crashTriage,
            DivergenceTriage: divergenceTriage,
            PendingGates:
            [
                "retained-release-execution-on-windows-macos-linux",
            ]);
    }

    private static CandidateFaultRow ProbeInterruptedWrite(
        string root,
        SnakeRun rulesProbe,
        string rulesHash)
    {
        var baseline = PreferencesDocument.CreateDefaults() with { MusicVolume = 0.25f };
        var physical = new PreferencesStore(root);
        physical.Save(baseline);
        var before = File.ReadAllBytes(physical.PreferencesPath);
        var injected = new PreferencesStore(
            root,
            new FaultingPreferencesWriteOperations(PreferenceWriteFault.InterruptedMove));
        var detected = false;
        try
        {
            injected.Save(baseline with { MusicVolume = 0.75f });
        }
        catch (IOException exception)
        {
            detected = exception.Message.Contains(
                "interrupted",
                StringComparison.OrdinalIgnoreCase);
        }

        var preserved = before.SequenceEqual(File.ReadAllBytes(physical.PreferencesPath));
        var temporaryRetained = File.Exists(physical.PreferencesPath + ".tmp");
        var original = physical.Load();
        physical.Save(baseline with { MusicVolume = 0.5f });
        var recovered = physical.Load();
        return Row(
            "interrupted-write",
            "preferences-atomic-replace",
            detected && temporaryRetained,
            preserved,
            original.IsSuccess
                && original.Document?.MusicVolume == 0.25f
                && recovered.IsSuccess
                && recovered.Document?.MusicVolume == 0.5f
                && !File.Exists(physical.PreferencesPath + ".tmp"),
            rulesProbe,
            rulesHash);
    }

    private static CandidateFaultRow ProbeCorruptJson(
        string root,
        SnakeRun rulesProbe,
        string rulesHash)
    {
        var store = new PreferencesStore(root);
        store.Save(PreferencesDocument.CreateDefaults());
        File.WriteAllText(store.PreferencesPath, "{", new UTF8Encoding(false));
        var corruptBytes = File.ReadAllBytes(store.PreferencesPath);
        var rejected = store.Load();
        var preserved = corruptBytes.SequenceEqual(File.ReadAllBytes(store.PreferencesPath));
        store.Save(PreferencesDocument.CreateDefaults() with { MusicVolume = 0.4f });
        var recovered = store.Load();
        return Row(
            "corrupt-json",
            "preferences-load-and-explicit-recovery",
            rejected.Code == PreferencesLoadCode.InvalidJson && rejected.Document is null,
            preserved,
            recovered.IsSuccess && recovered.Document?.MusicVolume == 0.4f,
            rulesProbe,
            rulesHash);
    }

    private static CandidateFaultRow ProbeFullDisk(
        string root,
        SnakeRun rulesProbe,
        string rulesHash)
    {
        var baseline = PreferencesDocument.CreateDefaults() with { MusicVolume = 0.3f };
        var physical = new PreferencesStore(root);
        physical.Save(baseline);
        var before = File.ReadAllBytes(physical.PreferencesPath);
        var injected = new PreferencesStore(
            root,
            new FaultingPreferencesWriteOperations(PreferenceWriteFault.FullDisk));
        var detected = false;
        try
        {
            injected.Save(baseline with { MusicVolume = 0.9f });
        }
        catch (IOException exception)
        {
            detected = exception.HResult == DiskFullHResult;
        }

        var loaded = physical.Load();
        return Row(
            "full-disk",
            "preferences-temporary-write-hresult-0x80070070",
            detected,
            before.SequenceEqual(File.ReadAllBytes(physical.PreferencesPath)),
            loaded.IsSuccess
                && loaded.Document?.MusicVolume == 0.3f
                && !File.Exists(physical.PreferencesPath + ".tmp"),
            rulesProbe,
            rulesHash);
    }

    private static CandidateFaultRow ProbeReadOnlyDirectory(
        string root,
        SnakeRun rulesProbe,
        string rulesHash)
    {
        var baseline = PreferencesDocument.CreateDefaults() with { MusicVolume = 0.35f };
        var physical = new PreferencesStore(root);
        physical.Save(baseline);
        var before = File.ReadAllBytes(physical.PreferencesPath);
        var injected = new PreferencesStore(
            root,
            new FaultingPreferencesWriteOperations(PreferenceWriteFault.ReadOnlyDirectory));
        var detected = false;
        try
        {
            injected.Save(baseline with { MusicVolume = 0.95f });
        }
        catch (UnauthorizedAccessException)
        {
            detected = true;
        }

        var loaded = physical.Load();
        return Row(
            "read-only-data-directory",
            "preferences-temporary-write-access-denied",
            detected,
            before.SequenceEqual(File.ReadAllBytes(physical.PreferencesPath)),
            loaded.IsSuccess
                && loaded.Document?.MusicVolume == 0.35f
                && !File.Exists(physical.PreferencesPath + ".tmp"),
            rulesProbe,
            rulesHash);
    }

    private static CandidateFaultRow ProbeMissingResource(
        CoreOnlyOfflineQualificationEvidence contentEvidence,
        SnakeRun rulesProbe,
        string rulesHash)
    {
        var policy = new RadioPlaybackPolicy(
            RadioCatalog.Empty,
            new RandomStreamBank(RulesProbeSeed).Radio);
        var snapshot = policy.PlayOrResume();
        var detected = contentEvidence.Passed
            && contentEvidence.CoreOnlyReady
            && contentEvidence.OptionalAbsenceNormal
            && snapshot.Mode == RadioPlaybackMode.NoStations
            && snapshot.PackState == RadioPackState.Missing;
        return Row(
            "missing-resource",
            "optional-pack-and-radio-catalog-absence",
            detected,
            true,
            !snapshot.IsAudible && snapshot.StationCount == 0,
            rulesProbe,
            rulesHash);
    }

    private static CandidateFaultRow ProbeInvalidContentPack(
        CoreOnlyOfflineQualificationEvidence contentEvidence,
        SnakeRun rulesProbe,
        string rulesHash) => Row(
            "invalid-content-pack",
            "manifest-tamper-incompatibility-and-duplicate-isolation",
            contentEvidence.TamperIsolated
                && contentEvidence.IncompatibilityIsolated
                && contentEvidence.DuplicateIsolated,
            contentEvidence.PlayerDataPreservedByFilesystemLifecycle,
            contentEvidence.Passed,
            rulesProbe,
            rulesHash);

    private static CandidateFaultRow ProbeUnavailableAudio(
        SnakeRun rulesProbe,
        string rulesHash)
    {
        var tracker = new AudioOutputRecoveryTracker();
        var unavailable = tracker.NoteFailure(100, "Injected output device loss.");
        var backoffActive = !tracker.ShouldAttemptPlayback(101)
            && tracker.ShouldAttemptPlayback(100 + AudioOutputRecoveryTracker.RetryDelayMilliseconds);
        var recovered = tracker.NoteSuccess();
        return Row(
            "unavailable-audio",
            "audio-output-recovery-policy",
            unavailable is
            {
                Kind: AudioOutputRecoveryKind.Unavailable,
                Caption: "AUDIO UNAVAILABLE: VISUAL CUES ACTIVE",
            },
            true,
            backoffActive
                && recovered is
                {
                    Kind: AudioOutputRecoveryKind.Recovered,
                    Caption: "AUDIO RESTORED",
                }
                && tracker.IsAvailable,
            rulesProbe,
            rulesHash);
    }

    private static CandidateTriageProbe ProbeCrashTriage(
        LocalDiagnostics diagnostics,
        string platform)
    {
        var configHash = new RunConfig().ComputeConfigHash();
        var path = diagnostics.WriteCrashReport(
            appVersion: ProductIdentity.AppVersion,
            platform: platform,
            rulesetId: SnakeRun.RulesetId,
            rulesVersion: SnakeRun.RulesVersion,
            screenState: "Qualification",
            exception: new InvalidOperationException(
                "Injected candidate crash at C:\\Users\\qualification\\private\\save.json"),
            timeProvider: new FixedQualificationTimeProvider(
                new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero)),
            configHash: configHash,
            configHashAlgorithm: RunConfig.ConfigHashAlgorithmId);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var payload = File.ReadAllText(path);
        return Triage(
            "crash-report",
            path,
            root.GetProperty("schemaVersion").GetInt32() == 1
                && root.GetProperty("kind").GetString() == "crash-report",
            !payload.Contains("C:\\Users\\qualification", StringComparison.Ordinal)
                && payload.Contains("<path>", StringComparison.Ordinal),
            root.GetProperty("rulesetId").GetString() == SnakeRun.RulesetId
                && root.GetProperty("rulesVersion").GetInt32() == SnakeRun.RulesVersion
                && root.GetProperty("screenState").GetString() == "Qualification"
                && root.GetProperty("configHash").GetString() == configHash);
    }

    private static CandidateTriageProbe ProbeDivergenceTriage(
        LocalDiagnostics diagnostics,
        string platform)
    {
        const ulong gameplaySeed = 0x1111_2222_3333_4444UL;
        const ulong controllerSeed = 0x5555_6666_7777_8888UL;
        var path = diagnostics.WriteDivergenceReport(
            appVersion: ProductIdentity.AppVersion,
            platform: platform,
            rulesetId: SnakeRun.RulesetId,
            rulesVersion: SnakeRun.RulesVersion,
            campaignId: "candidate-reliability",
            modeId: "vibe",
            gameplaySeed: gameplaySeed,
            controllerSeed: controllerSeed,
            runIndex: 3,
            firstDivergentStep: 17,
            expectedStateHash: "1111111111111111",
            actualStateHash: "2222222222222222",
            recentCommands: ["Up", "Left", "Down"],
            timeProvider: new FixedQualificationTimeProvider(
                new DateTimeOffset(2026, 8, 9, 12, 0, 1, TimeSpan.Zero)));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var payload = File.ReadAllText(path);
        return Triage(
            "deterministic-divergence-report-v1",
            path,
            root.GetProperty("schemaVersion").GetInt32() == 1
                && root.GetProperty("kind").GetString()
                    == "deterministic-divergence-report-v1",
            !payload.Contains("C:\\Users\\", StringComparison.Ordinal),
            root.GetProperty("gameplaySeed").GetString() == "1111222233334444"
                && root.GetProperty("controllerSeed").GetString() == "5555666677778888"
                && root.GetProperty("runIndex").GetInt32() == 3
                && root.GetProperty("firstDivergentStep").GetInt32() == 17
                && root.GetProperty("recentCommandCount").GetInt32() == 3);
    }

    private static CandidateFaultRow Row(
        string faultId,
        string boundary,
        bool detected,
        bool preserved,
        bool recovered,
        SnakeRun rulesProbe,
        string rulesHash) => new(
            FaultId: faultId,
            InjectionBoundary: boundary,
            FaultDetected: detected,
            ExistingDataPreserved: preserved,
            RecoveryVerified: recovered,
            RulesStateUnchanged: rulesProbe.ComputeStateHash() == rulesHash);

    private static CandidateTriageProbe Triage(
        string kind,
        string path,
        bool schemaValid,
        bool privacySafe,
        bool reproductionFieldsComplete) => new(
            ReportKind: kind,
            ReportRetained: File.Exists(path),
            SchemaValid: schemaValid,
            PrivacySafe: privacySafe,
            ReproductionFieldsComplete: reproductionFieldsComplete,
            FileName: Path.GetFileName(path),
            Sha256: Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                .ToLowerInvariant());

    private static bool TriagePassed(CandidateTriageProbe triage) =>
        triage.ReportRetained
        && triage.SchemaValid
        && triage.PrivacySafe
        && triage.ReproductionFieldsComplete
        && !Path.IsPathFullyQualified(triage.FileName)
        && triage.Sha256.Length == 64;

    private enum PreferenceWriteFault : byte
    {
        InterruptedMove,
        FullDisk,
        ReadOnlyDirectory,
    }

    private sealed class FaultingPreferencesWriteOperations(PreferenceWriteFault fault)
        : IPreferencesWriteOperations
    {
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteAllText(string path, string contents, Encoding encoding)
        {
            switch (fault)
            {
                case PreferenceWriteFault.FullDisk:
                    throw new DiskFullIOException();
                case PreferenceWriteFault.ReadOnlyDirectory:
                    throw new UnauthorizedAccessException(
                        "Injected read-only player-data directory.");
                case PreferenceWriteFault.InterruptedMove:
                    File.WriteAllText(path, contents, encoding);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite)
        {
            if (fault == PreferenceWriteFault.InterruptedMove)
            {
                throw new IOException("Injected interrupted atomic replacement.");
            }

            File.Move(sourcePath, destinationPath, overwrite);
        }
    }

    private sealed class DiskFullIOException : IOException
    {
        public DiskFullIOException()
            : base("Injected disk-full write failure.")
        {
            HResult = DiskFullHResult;
        }
    }

    private sealed class FixedQualificationTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
