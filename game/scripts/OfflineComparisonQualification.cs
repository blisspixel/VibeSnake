using System.Text.Json;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record OfflineComparisonQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int SeedCodeSchemaVersion,
    bool SeedCodeStable,
    bool SeedCodeTamperDetected,
    bool RulesIdentityComplete,
    bool ContentIdentityComplete,
    bool ConfigIdentityComplete,
    bool ExactSeedRoundTrip,
    int AllowedOptionCount,
    int HouseholdSlotCount,
    long MaximumImportBytes,
    bool ExplicitSourcePreservingImport,
    bool AtomicNoOverwriteImport,
    bool ModifiedImportRejected,
    bool IncompatibleImportRejected,
    bool KeyboardRouteComplete,
    bool ControllerRouteComplete,
    bool EqualRulesGhostComplete,
    bool ActualGameGhostRouteComplete,
    bool GhostStateIsolated,
    int RunCardSchemaVersion,
    int RunCardFieldCount,
    bool RunCardReadable,
    bool RunCardAtomicAndIdempotent,
    bool PlayerIdentityExcluded,
    bool PrivatePathsExcluded,
    bool DeletionRequiresExactConfirmation,
    bool DeleteCancelLossless,
    bool ConfirmedDeleteExact,
    bool ProgressionAwardsExcluded,
    bool CoreOffline,
    string HumanReviewStatus,
    IReadOnlyList<string> PendingHumanChecks)
{
    public string Serialize() => JsonSerializer.Serialize(
        this,
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }) + "\n";
}

internal static class OfflineComparisonQualification
{
    public static OfflineComparisonQualificationEvidence Run(
        RunReplay replay,
        OfflineRunCard runCard,
        bool explicitSourcePreservingImport,
        bool atomicNoOverwriteImport,
        bool modifiedImportRejected,
        bool incompatibleImportRejected,
        bool keyboardRouteComplete,
        bool controllerRouteComplete,
        bool actualGameGhostRouteComplete,
        bool runCardAtomicAndIdempotent,
        bool deletionRequiresExactConfirmation,
        bool deleteCancelLossless,
        bool confirmedDeleteExact,
        bool progressionAwardsExcluded)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(runCard);
        var challenge = SeedChallengeDescriptor.Create(replay);
        var code = challenge.Encode();
        var read = SeedChallengeDescriptor.Read(code);
        var changedCode = code[..^1] + (code[^1] == '0' ? '1' : '0');
        var seedCodeStable = code == SeedChallengeDescriptor.Create(replay).Encode();
        var seedCodeTamperDetected = SeedChallengeDescriptor.Read(changedCode).Code
            == SeedCodeReadCode.IntegrityMismatch;
        var rulesIdentityComplete = challenge.RulesetId == RulesetIdentity.CurrentId
            && challenge.RulesVersion == RulesetIdentity.CurrentVersion;
        var contentIdentityComplete = challenge.ContentContractId
            == SeedChallengeDescriptor.CurrentContentContractId;
        var configIdentityComplete = challenge.ConfigHashAlgorithm
                == RunConfig.ConfigHashAlgorithmId
            && challenge.ConfigHash == replay.ConfigHash;
        var recreated = challenge.CreateRun(OfflineChallengeOptions.SameSeedRun);
        var exactSeedRoundTrip = read.IsValid
            && read.Challenge == challenge
            && recreated.SerializeCanonicalState() == replay.InitialCanonicalState;

        var equalRace = new GhostRaceSession(challenge, replay);
        var equalRulesGhostComplete = true;
        while (equalRace.TryAdvance(out var frame))
        {
            if (frame is null || frame.Player.StateHash != frame.Ghost.StateHash)
            {
                equalRulesGhostComplete = false;
                break;
            }

            if (equalRace.GhostComplete)
            {
                break;
            }
        }

        equalRulesGhostComplete = equalRulesGhostComplete && equalRace.GhostComplete;
        var isolatedRace = new GhostRaceSession(challenge, replay);
        var expectedGhost = new RunReplayPlayback(replay);
        var directionAccepted = isolatedRace.QueuePlayerDirection(Direction.Up);
        var playerAdvanced = isolatedRace.TryAdvance(out var divergentFrame);
        var ghostAdvanced = expectedGhost.TryAdvance(out _);
        var ghostStateIsolated = directionAccepted
            && playerAdvanced
            && ghostAdvanced
            && divergentFrame is not null
            && divergentFrame.Player.StateHash != divergentFrame.Ghost.StateHash
            && divergentFrame.Ghost.StateHash == expectedGhost.CurrentSnapshot.StateHash;

        var cardPayload = runCard.Serialize();
        using var cardJson = JsonDocument.Parse(cardPayload);
        var runCardFieldCount = cardJson.RootElement.EnumerateObject().Count();
        var runCardReadable = runCard.ToDisplayLines().All(line =>
            line.Length is >= 1 and <= 100);
        var playerIdentityExcluded = !runCard.ContainsPlayerIdentity
            && !cardPayload.Contains("playerName", StringComparison.OrdinalIgnoreCase)
            && !cardPayload.Contains("displayName", StringComparison.OrdinalIgnoreCase)
            && !cardPayload.Contains("profile", StringComparison.OrdinalIgnoreCase);
        var privatePathsExcluded = !runCard.ContainsPrivatePaths
            && !cardPayload.Contains("user://", StringComparison.OrdinalIgnoreCase)
            && !cardPayload.Contains(":\\", StringComparison.Ordinal)
            && !cardPayload.Contains("/home/", StringComparison.OrdinalIgnoreCase);
        var coreOffline = typeof(OfflineChallengeStore)
            .GetMethods()
            .All(method =>
                !method.Name.Contains("Upload", StringComparison.OrdinalIgnoreCase)
                && !method.Name.Contains("Network", StringComparison.OrdinalIgnoreCase));
        string[] pendingHumanChecks =
        [
            "Test household replay handoff, slot language, and deletion consent with players who share one device",
            "Review maximum text scale and controller prompts on Windows, macOS, and Linux",
            "Confirm the ghost silhouette, score delta, and race pacing stay readable without distracting from play",
        ];
        var passed = challenge.SchemaVersion == SeedChallengeDescriptor.CurrentSchemaVersion
            && seedCodeStable
            && seedCodeTamperDetected
            && rulesIdentityComplete
            && contentIdentityComplete
            && configIdentityComplete
            && exactSeedRoundTrip
            && challenge.AllowedOptions == SeedChallengeDescriptor.AllOptions
            && OfflineChallengeStore.MaximumHouseholdRivalSlots == 4
            && RunReplay.MaximumSerializedCharacters == 16L * 1024L * 1024L
            && explicitSourcePreservingImport
            && atomicNoOverwriteImport
            && modifiedImportRejected
            && incompatibleImportRejected
            && keyboardRouteComplete
            && controllerRouteComplete
            && equalRulesGhostComplete
            && actualGameGhostRouteComplete
            && ghostStateIsolated
            && runCard.SchemaVersion == OfflineRunCard.CurrentSchemaVersion
            && runCard.Kind == OfflineRunCard.KindId
            && runCardFieldCount == OfflineRunCard.FieldCount
            && runCardReadable
            && runCardAtomicAndIdempotent
            && playerIdentityExcluded
            && privatePathsExcluded
            && deletionRequiresExactConfirmation
            && deleteCancelLossless
            && confirmedDeleteExact
            && progressionAwardsExcluded
            && coreOffline;
        if (!passed)
        {
            throw new InvalidOperationException("Offline comparison qualification failed.");
        }

        return new OfflineComparisonQualificationEvidence(
            SchemaVersion: 1,
            Kind: "offline-comparison-qualification-v1",
            Passed: true,
            SeedCodeSchemaVersion: challenge.SchemaVersion,
            SeedCodeStable: seedCodeStable,
            SeedCodeTamperDetected: seedCodeTamperDetected,
            RulesIdentityComplete: rulesIdentityComplete,
            ContentIdentityComplete: contentIdentityComplete,
            ConfigIdentityComplete: configIdentityComplete,
            ExactSeedRoundTrip: exactSeedRoundTrip,
            AllowedOptionCount: 3,
            HouseholdSlotCount: OfflineChallengeStore.MaximumHouseholdRivalSlots,
            MaximumImportBytes: RunReplay.MaximumSerializedCharacters,
            ExplicitSourcePreservingImport: explicitSourcePreservingImport,
            AtomicNoOverwriteImport: atomicNoOverwriteImport,
            ModifiedImportRejected: modifiedImportRejected,
            IncompatibleImportRejected: incompatibleImportRejected,
            KeyboardRouteComplete: keyboardRouteComplete,
            ControllerRouteComplete: controllerRouteComplete,
            EqualRulesGhostComplete: equalRulesGhostComplete,
            ActualGameGhostRouteComplete: actualGameGhostRouteComplete,
            GhostStateIsolated: ghostStateIsolated,
            RunCardSchemaVersion: runCard.SchemaVersion,
            RunCardFieldCount: runCardFieldCount,
            RunCardReadable: runCardReadable,
            RunCardAtomicAndIdempotent: runCardAtomicAndIdempotent,
            PlayerIdentityExcluded: playerIdentityExcluded,
            PrivatePathsExcluded: privatePathsExcluded,
            DeletionRequiresExactConfirmation: deletionRequiresExactConfirmation,
            DeleteCancelLossless: deleteCancelLossless,
            ConfirmedDeleteExact: confirmedDeleteExact,
            ProgressionAwardsExcluded: progressionAwardsExcluded,
            CoreOffline: coreOffline,
            HumanReviewStatus: "pending-household-platform-and-playability-review",
            PendingHumanChecks: pendingHumanChecks);
    }
}
