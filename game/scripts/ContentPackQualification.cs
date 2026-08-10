using VibeSnake.Persistence;

namespace VibeSnake.Game;

internal sealed record CoreOnlyOfflineQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool CoreOnlyReady,
    bool OptionalAbsenceNormal,
    bool OptionalRemovalIsolated,
    bool TamperIsolated,
    bool IncompatibilityIsolated,
    bool DuplicateIsolated,
    bool RemovalRequiresExplicitConfirmation,
    bool RemovalCancelPreservesPack,
    bool RemovalConfirmIsTargeted,
    bool PlayerDataRemovalSeparated,
    bool InstalledOptionalValidated,
    bool InstalledAssetReadValidated,
    bool RemovalQuarantinedRecoverably,
    bool QuarantineRediscovered,
    bool RestoreRevalidated,
    bool PlayerDataPreservedByFilesystemLifecycle,
    int AcceptedOptionalBeforeRemoval,
    int AcceptedOptionalAfterRemoval,
    bool FullOfflineFlowExercised,
    IReadOnlyList<string> ExercisedFlows)
{
    public bool Passed =>
        CoreOnlyReady
        && OptionalAbsenceNormal
        && OptionalRemovalIsolated
        && TamperIsolated
        && IncompatibilityIsolated
        && DuplicateIsolated
        && RemovalRequiresExplicitConfirmation
        && RemovalCancelPreservesPack
        && RemovalConfirmIsTargeted
        && PlayerDataRemovalSeparated
        && InstalledOptionalValidated
        && InstalledAssetReadValidated
        && RemovalQuarantinedRecoverably
        && QuarantineRediscovered
        && RestoreRevalidated
        && PlayerDataPreservedByFilesystemLifecycle
        && AcceptedOptionalBeforeRemoval == 1
        && AcceptedOptionalAfterRemoval == 0
        && FullOfflineFlowExercised;
}

internal static class ContentPackQualification
{
    internal static RadioCatalog CreateValidatedRadioCatalog()
    {
        var inventory = ContentInventory.Parse(InventoryJson);
        var manifest = ContentPackManifest.Parse(RadioManifestJson, inventory);
        return RadioCatalog.FromValidatedManifests([manifest]);
    }

    public static CoreOnlyOfflineQualificationEvidence Run(string absoluteUserDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteUserDataRoot);
        var service = new ContentService(ContentInventory.Parse(InventoryJson));
        var coreOnly = service.ResolvePackSet(CoreManifestJson, [], "0.3.0");
        var installed = service.ResolvePackSet(
            CoreManifestJson,
            [RadioManifestJson],
            "0.3.0");
        var removed = service.ResolvePackSet(CoreManifestJson, [], "0.3.0");

        var tamperedRadio = RadioManifestJson.Replace(
            new string('2', 64),
            new string('f', 64),
            StringComparison.Ordinal);
        var tampered = service.ResolvePackSet(
            CoreManifestJson,
            [tamperedRadio],
            "0.3.0");

        var incompatibleRadio = RadioManifestJson.Replace(
            "\"minInclusive\": \"0.3.0\"",
            "\"minInclusive\": \"0.4.0\"",
            StringComparison.Ordinal);
        var incompatible = service.ResolvePackSet(
            CoreManifestJson,
            [incompatibleRadio],
            "0.3.0");
        var duplicate = service.ResolvePackSet(
            CoreManifestJson,
            [RadioManifestJson, RadioManifestJson],
            "0.3.0");

        InstalledOptionalPack[] installedPacks =
        [
            new("vibesnake.radio.flow-signal", "1.0.0", "The Flow Signal"),
            new("vibesnake.radio.neon-night", "1.0.0", "Neon Night"),
        ];
        var removalRequest = OptionalPackRemovalConsent.Request(
            installedPacks,
            "vibesnake.radio.flow-signal");
        var removalConsent = removalRequest.Consent
            ?? throw new InvalidOperationException(
                "Optional-pack removal qualification did not create consent.");
        var cancelledRemoval = removalConsent.Cancel(installedPacks);
        var confirmedRemoval = removalConsent.Confirm(installedPacks);
        var filesystemLifecycle = ExerciseFilesystemLifecycle(absoluteUserDataRoot);

        const string radioId = "vibesnake.radio.flow-signal";
        return new CoreOnlyOfflineQualificationEvidence(
            SchemaVersion: 1,
            Kind: "core-only-offline-v1",
            CoreOnlyReady: coreOnly.CoreReady,
            OptionalAbsenceNormal:
                coreOnly.AcceptedOptional.Count == 0
                && coreOnly.RejectedOptional.Count == 0,
            OptionalRemovalIsolated:
                installed.CoreReady
                && removed.CoreReady
                && installed.AcceptedOptional.SequenceEqual([radioId])
                && removed.AcceptedOptional.Count == 0,
            TamperIsolated:
                tampered.CoreReady
                && tampered.AcceptedOptional.Count == 0
                && tampered.RejectedOptional.TryGetValue(radioId, out var tamperResult)
                && tamperResult.Code == "invalid-pack",
            IncompatibilityIsolated:
                incompatible.CoreReady
                && incompatible.AcceptedOptional.Count == 0
                && incompatible.RejectedOptional.TryGetValue(
                    radioId,
                    out var incompatibilityResult)
                && incompatibilityResult.Code == "game-version-too-old",
            DuplicateIsolated:
                duplicate.CoreReady
                && duplicate.AcceptedOptional.Count == 0
                && duplicate.RejectedOptional.TryGetValue(radioId, out var duplicateResult)
                && duplicateResult.Code == "invalid-pack",
            RemovalRequiresExplicitConfirmation:
                removalRequest.IsReady && removalConsent.RequiresExplicitConfirmation,
            RemovalCancelPreservesPack:
                cancelledRemoval.RemainingPacks.SequenceEqual(installedPacks),
            RemovalConfirmIsTargeted:
                confirmedRemoval.IsSuccess
                && confirmedRemoval.RemainingPacks.Count == 1
                && confirmedRemoval.RemainingPacks[0].Id == "vibesnake.radio.neon-night",
            PlayerDataRemovalSeparated:
                !removalConsent.RemovesSaveData
                && !removalConsent.RemovesProfiles
                && !removalConsent.RemovesReplays,
            InstalledOptionalValidated: filesystemLifecycle.InstalledValidated,
            InstalledAssetReadValidated: filesystemLifecycle.AssetReadValidated,
            RemovalQuarantinedRecoverably: filesystemLifecycle.QuarantinedRecoverably,
            QuarantineRediscovered: filesystemLifecycle.QuarantineRediscovered,
            RestoreRevalidated: filesystemLifecycle.RestoreRevalidated,
            PlayerDataPreservedByFilesystemLifecycle: filesystemLifecycle.PlayerDataPreserved,
            AcceptedOptionalBeforeRemoval: installed.AcceptedOptional.Count,
            AcceptedOptionalAfterRemoval: removed.AcceptedOptional.Count,
            FullOfflineFlowExercised: false,
            ExercisedFlows:
            [
                "launch",
                "menu",
                "run",
                "critical-feedback",
                "settings",
                "content-packs",
                "death",
                "restart",
                "recovery",
            ]);
    }

    private static OptionalPackFilesystemLifecycle ExerciseFilesystemLifecycle(
        string absoluteUserDataRoot)
    {
        var qualificationRoot = Path.Combine(
            absoluteUserDataRoot,
            "qualification",
            $"optional-pack-{Guid.NewGuid():N}");
        var payload = Enumerable.Repeat((byte)0x5a, 30).ToArray();
        var payloadHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(payload))
            .ToLowerInvariant();
        var inventory = ContentInventory.Parse(
            InventoryJson.Replace(new string('2', 64), payloadHash, StringComparison.Ordinal));
        var manifest = ContentPackManifest.Parse(
            RadioManifestJson.Replace(
                new string('2', 64),
                payloadHash,
                StringComparison.Ordinal),
            inventory);
        var store = new OptionalPackStore(qualificationRoot);
        var packDirectory = Path.Combine(store.PacksRoot, manifest.Id);
        var payloadPath = Path.Combine(
            packDirectory,
            manifest.Files[0].Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        File.WriteAllBytes(payloadPath, payload);
        File.WriteAllText(
            Path.Combine(packDirectory, OptionalPackStore.ManifestFileName),
            manifest.RenderCanonical(),
            new System.Text.UTF8Encoding(false));

        var playerDataDirectory = Path.Combine(qualificationRoot, "replays");
        Directory.CreateDirectory(playerDataDirectory);
        var playerDataPath = Path.Combine(playerDataDirectory, "keep.txt");
        File.WriteAllText(playerDataPath, "player-data", new System.Text.UTF8Encoding(false));

        var before = store.InspectInstalled(inventory);
        var assetRead = new ContentService(inventory).ReadInstalledOptionalAsset(
            store,
            manifest.Id,
            manifest.Files[0].Id);
        var removalRequest = OptionalPackRemovalConsent.Request(
            before.Installed,
            manifest.Id);
        var consent = removalRequest.Consent
            ?? throw new InvalidOperationException(
                "Filesystem removal qualification did not create consent.");
        var quarantine = store.Quarantine(consent, inventory);
        var receipt = quarantine.Receipt
            ?? throw new InvalidOperationException(
                "Filesystem removal qualification did not create a receipt.");
        var quarantineDirectory = Path.Combine(
            store.PacksRoot,
            ".removed",
            receipt.QuarantineName);
        var afterQuarantine = store.InspectInstalled(inventory);
        var playerDataPreserved = File.Exists(playerDataPath)
            && File.ReadAllText(playerDataPath) == "player-data";
        var quarantinedRecoverably = quarantine.IsSuccess
            && !Directory.Exists(packDirectory)
            && Directory.Exists(quarantineDirectory)
            && afterQuarantine.Installed.Count == 0;
        var quarantineInspection = store.InspectQuarantined(inventory);
        var discoveredReceipt = quarantineInspection.Available.Count == 1
            ? quarantineInspection.Available[0].Receipt
            : null;

        var restore = discoveredReceipt is null
            ? null
            : store.Restore(discoveredReceipt, inventory);
        var afterRestore = store.InspectInstalled(inventory);
        return new OptionalPackFilesystemLifecycle(
            InstalledValidated:
                before.Rejected.Count == 0
                && before.Installed.Count == 1
                && before.Installed[0].Id == manifest.Id,
            AssetReadValidated:
                assetRead.IsSuccess
                && assetRead.Asset?.MediaType == "audio/mpeg"
                && assetRead.Asset.Bytes.SequenceEqual(payload),
            QuarantinedRecoverably: quarantinedRecoverably,
            QuarantineRediscovered:
                quarantineInspection.Rejected.Count == 0
                && discoveredReceipt == receipt,
            RestoreRevalidated:
                restore?.IsSuccess == true
                && Directory.Exists(packDirectory)
                && !Directory.Exists(quarantineDirectory)
                && afterRestore.Rejected.Count == 0
                && afterRestore.Installed.Count == 1
                && afterRestore.Installed[0].Id == manifest.Id,
            PlayerDataPreserved:
                playerDataPreserved
                && File.Exists(playerDataPath)
                && File.ReadAllText(playerDataPath) == "player-data");
    }

    private readonly record struct OptionalPackFilesystemLifecycle(
        bool InstalledValidated,
        bool AssetReadValidated,
        bool QuarantinedRecoverably,
        bool QuarantineRediscovered,
        bool RestoreRevalidated,
        bool PlayerDataPreserved);

    private const string InventoryJson =
        """
        {
          "schemaVersion": 1,
          "assetRoot": "assets",
          "policySha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "fileCount": 2,
          "assets": [
            {
              "id": "asset:config/core.json",
              "path": "config/core.json",
              "mediaType": "application/json",
              "bytes": 10,
              "sha256": "1111111111111111111111111111111111111111111111111111111111111111",
              "integrityStatus": "valid",
              "role": "core-config",
              "packId": "vibesnake.core",
              "runtimeUse": "required",
              "shipStatus": "approved",
              "exportEligible": true,
              "rights": {
                "status": "cleared",
                "source": "project-owned fixture",
                "license": "MIT",
                "attribution": "none",
                "reviewNote": "fixture approval record"
              },
              "duplicateOf": null
            },
            {
              "id": "asset:audio/radio/flow/track-01.mp3",
              "path": "audio/radio/flow/track-01.mp3",
              "mediaType": "audio/mpeg",
              "bytes": 30,
              "sha256": "2222222222222222222222222222222222222222222222222222222222222222",
              "integrityStatus": "valid",
              "role": "radio-track",
              "packId": "vibesnake.radio.flow-signal",
              "runtimeUse": "optional",
              "shipStatus": "approved",
              "exportEligible": true,
              "rights": {
                "status": "cleared",
                "source": "licensed fixture",
                "license": "CC-BY-4.0",
                "attribution": "Fixture Artist",
                "reviewNote": "fixture license review"
              },
              "duplicateOf": null
            }
          ]
        }
        """;

    private const string CoreManifestJson =
        """
        {
          "schemaVersion": 1,
          "id": "vibesnake.core",
          "version": "1.0.0",
          "kind": "core",
          "displayName": "Vibe Snake Core",
          "description": "Required offline fixture.",
          "compatibility": {
            "gameVersion": { "minInclusive": "0.3.0", "maxExclusive": "1.1.0" },
            "ruleset": { "id": "vibesnake-core", "minInclusive": 4, "maxExclusive": 5 }
          },
          "inventory": {
            "schemaVersion": 1,
            "assetRoot": "assets",
            "policySha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
          },
          "dependencies": [],
          "files": [
            {
              "id": "asset:config/core.json",
              "path": "config/core.json",
              "mediaType": "application/json",
              "bytes": 10,
              "sha256": "1111111111111111111111111111111111111111111111111111111111111111",
              "role": "core-config",
              "runtimeUse": "required",
              "creditId": "core-rights"
            }
          ],
          "credits": [
            {
              "id": "core-rights",
              "source": "project-owned fixture",
              "license": "MIT",
              "attribution": "none",
              "reviewEvidence": "fixture approval record"
            }
          ],
          "radio": null
        }
        """;

    private const string RadioManifestJson =
        """
        {
          "schemaVersion": 1,
          "id": "vibesnake.radio.flow-signal",
          "version": "1.0.0",
          "kind": "radio",
          "displayName": "The Flow Signal",
          "description": "Optional radio fixture.",
          "compatibility": {
            "gameVersion": { "minInclusive": "0.3.0", "maxExclusive": "1.1.0" },
            "ruleset": { "id": "vibesnake-core", "minInclusive": 4, "maxExclusive": 5 }
          },
          "inventory": {
            "schemaVersion": 1,
            "assetRoot": "assets",
            "policySha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
          },
          "dependencies": [
            { "id": "vibesnake.core", "minInclusive": "1.0.0", "maxExclusive": "2.0.0" }
          ],
          "files": [
            {
              "id": "asset:audio/radio/flow/track-01.mp3",
              "path": "audio/radio/flow/track-01.mp3",
              "mediaType": "audio/mpeg",
              "bytes": 30,
              "sha256": "2222222222222222222222222222222222222222222222222222222222222222",
              "role": "radio-track",
              "runtimeUse": "optional",
              "creditId": "radio-rights"
            }
          ],
          "credits": [
            {
              "id": "radio-rights",
              "source": "licensed fixture",
              "license": "CC-BY-4.0",
              "attribution": "Fixture Artist",
              "reviewEvidence": "fixture license review"
            }
          ],
          "radio": {
            "stationId": "flow_signal",
            "stationName": "The Flow Signal",
            "trackIds": ["asset:audio/radio/flow/track-01.mp3"]
          }
        }
        """;
}
