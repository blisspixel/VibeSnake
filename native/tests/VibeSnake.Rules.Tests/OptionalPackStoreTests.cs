using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class OptionalPackStoreTests
{
    private const string PackId = "vibesnake.radio.flow-signal";
    private const string AssetPath = "audio/radio/flow/track-01.mp3";
    private const string PolicyHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Inspects_a_canonical_hash_verified_optional_pack()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();

            var report = store.InspectInstalled(fixture.Inventory);

            var installed = Assert.Single(report.Installed);
            Assert.Equal(PackId, installed.Id);
            Assert.Equal("1.0.0", installed.Version);
            Assert.Empty(report.Rejected);

            var radio = store.InspectRadioCatalog(fixture.Inventory);
            var station = Assert.Single(radio.Catalog.Stations);
            Assert.Equal("flow_signal", station.StationId);
            Assert.Equal("The Flow Signal", station.StationName);
            Assert.Equal($"asset:{AssetPath}", Assert.Single(station.Tracks).TrackId);
            Assert.Empty(radio.Rejected);
        });
    }

    [Fact]
    public void Reads_only_manifest_addressed_hash_verified_asset_bytes()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();

            var result = store.ReadAsset(
                PackId,
                $"asset:{AssetPath}",
                fixture.Inventory);

            Assert.True(result.IsSuccess);
            var asset = Assert.IsType<InstalledOptionalPackAsset>(result.Asset);
            Assert.Equal(PackId, asset.PackId);
            Assert.Equal("1.0.0", asset.PackVersion);
            Assert.Equal("audio/mpeg", asset.MediaType);
            Assert.Equal(fixture.Payload, asset.Bytes);
        });
    }

    [Fact]
    public void Asset_reads_fail_closed_for_unknown_invalid_or_tampered_requests()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();
            var missing = store.ReadAsset(
                PackId,
                "asset:audio/radio/flow/missing.mp3",
                fixture.Inventory);
            var invalid = store.ReadAsset(
                "../escape",
                $"asset:{AssetPath}",
                fixture.Inventory);
            File.WriteAllBytes(fixture.PayloadPath, [9, 9, 9, 9]);
            var tampered = store.ReadAsset(
                PackId,
                $"asset:{AssetPath}",
                fixture.Inventory);

            Assert.Equal(OptionalPackAssetReadCode.AssetNotFound, missing.Code);
            Assert.Equal(OptionalPackAssetReadCode.InvalidRequest, invalid.Code);
            Assert.Equal(OptionalPackAssetReadCode.InvalidPack, tampered.Code);
            Assert.Null(tampered.Asset);
            Assert.DoesNotContain(fixture.UserDataRoot, tampered.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Inspection_isolates_tampered_or_unmanifested_payloads()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();
            File.WriteAllBytes(fixture.PayloadPath, [9, 9, 9, 9]);

            var tampered = store.InspectInstalled(fixture.Inventory);
            var tamperedRadio = store.InspectRadioCatalog(fixture.Inventory);

            Assert.Empty(tampered.Installed);
            Assert.Contains("hash mismatch", tampered.Rejected[PackId]);
            Assert.Empty(tamperedRadio.Catalog.Stations);
            Assert.Contains("hash mismatch", tamperedRadio.Rejected[PackId]);

            fixture.Install(overwrite: true);
            File.WriteAllText(Path.Combine(fixture.PackDirectory, "extra.txt"), "extra");
            var extra = store.InspectInstalled(fixture.Inventory);

            Assert.Empty(extra.Installed);
            Assert.Contains("allowlist", extra.Rejected[PackId]);
        });
    }

    [Fact]
    public void Quarantine_and_restore_are_recoverable_and_preserve_player_data()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();
            var replayDirectory = Path.Combine(fixture.UserDataRoot, "replays");
            Directory.CreateDirectory(replayDirectory);
            var replayPath = Path.Combine(replayDirectory, "keep.vibesnake-replay.json");
            File.WriteAllText(replayPath, "keep");
            var installed = store.InspectInstalled(fixture.Inventory).Installed;
            var consent = OptionalPackRemovalConsent.Request(installed, PackId).Consent!;

            var removed = store.Quarantine(consent, fixture.Inventory);

            Assert.True(removed.IsSuccess);
            Assert.NotNull(removed.Receipt);
            Assert.False(Directory.Exists(fixture.PackDirectory));
            Assert.True(File.Exists(replayPath));
            Assert.Equal("keep", File.ReadAllText(replayPath));
            Assert.Empty(store.InspectInstalled(fixture.Inventory).Installed);
            var quarantineInspection = store.InspectQuarantined(fixture.Inventory);
            var discovered = Assert.Single(quarantineInspection.Available);
            Assert.Empty(quarantineInspection.Rejected);
            Assert.Equal(removed.Receipt, discovered.Receipt);

            var restored = store.Restore(discovered.Receipt, fixture.Inventory);

            Assert.True(restored.IsSuccess);
            Assert.True(Directory.Exists(fixture.PackDirectory));
            Assert.True(File.Exists(replayPath));
            Assert.Single(store.InspectInstalled(fixture.Inventory).Installed);
            Assert.Empty(store.InspectQuarantined(fixture.Inventory).Available);
        });
    }

    [Fact]
    public void Quarantine_rejects_stale_consent_without_moving_the_pack()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();
            var installed = store.InspectInstalled(fixture.Inventory).Installed;
            var consent = OptionalPackRemovalConsent.Request(installed, PackId).Consent!;
            File.WriteAllText(
                fixture.ManifestPath,
                fixture.RenderManifest("1.1.0"),
                new UTF8Encoding(false));

            var result = store.Quarantine(consent, fixture.Inventory);

            Assert.False(result.IsSuccess);
            Assert.Equal(OptionalPackQuarantineCode.StaleConsent, result.Code);
            Assert.True(Directory.Exists(fixture.PackDirectory));
            Assert.Equal(
                "1.1.0",
                Assert.Single(store.InspectInstalled(fixture.Inventory).Installed).Version);
        });
    }

    [Fact]
    public void Quarantine_inspection_and_restore_isolate_tampered_payloads()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();
            var installed = store.InspectInstalled(fixture.Inventory).Installed;
            var consent = OptionalPackRemovalConsent.Request(installed, PackId).Consent!;
            var removed = store.Quarantine(consent, fixture.Inventory);
            var receipt = Assert.IsType<OptionalPackQuarantineReceipt>(removed.Receipt);
            var quarantinedPayload = Path.Combine(
                store.PacksRoot,
                ".removed",
                receipt.QuarantineName,
                AssetPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(quarantinedPayload, [9, 9, 9, 9]);

            var inspection = store.InspectQuarantined(fixture.Inventory);
            var restore = store.Restore(receipt, fixture.Inventory);

            Assert.Empty(inspection.Available);
            Assert.Contains(receipt.QuarantineName, inspection.Rejected);
            Assert.Equal(OptionalPackQuarantineCode.InvalidInstalledPack, restore.Code);
            Assert.False(Directory.Exists(fixture.PackDirectory));
            Assert.True(File.Exists(quarantinedPayload));
        });
    }

    [Fact]
    public void Restore_rejects_an_untrusted_receipt_and_relative_roots()
    {
        Assert.Throws<ArgumentException>(() => new OptionalPackStore("relative/path"));
        WithFixture((fixture, store) =>
        {
            var result = store.Restore(
                new OptionalPackQuarantineReceipt(PackId, "1.0.0", "../escape"),
                fixture.Inventory);

            Assert.False(result.IsSuccess);
            Assert.Equal(OptionalPackQuarantineCode.InvalidInstalledPack, result.Code);
        });
    }

    [Fact]
    public void Empty_and_missing_pack_roots_return_safe_empty_or_not_installed_results()
    {
        WithFixture((fixture, store) =>
        {
            Assert.Empty(store.InspectInstalled(fixture.Inventory).Installed);
            Assert.Empty(store.InspectQuarantined(fixture.Inventory).Available);

            var absentRoot = store.ReadAsset(PackId, $"asset:{AssetPath}", fixture.Inventory);
            Assert.Equal(OptionalPackAssetReadCode.PackNotInstalled, absentRoot.Code);

            Directory.CreateDirectory(store.PacksRoot);
            var absentPack = store.ReadAsset(PackId, $"asset:{AssetPath}", fixture.Inventory);
            Assert.Equal(OptionalPackAssetReadCode.PackNotInstalled, absentPack.Code);
        });
    }

    [Fact]
    public void Asset_requests_reject_every_unsafe_identity_and_enforce_read_size_ceiling()
    {
        WithFixture((fixture, store) =>
        {
            foreach (var (packId, assetId) in new[]
            {
                ("", $"asset:{AssetPath}"),
                ("vibesnake.core", $"asset:{AssetPath}"),
                ("vibesnake.radio.", $"asset:{AssetPath}"),
                ("vibesnake.radio.UPPER", $"asset:{AssetPath}"),
                (PackId, ""),
                (PackId, "not-an-asset"),
                (PackId, "asset:" + new string('x', 640)),
            })
            {
                Assert.Equal(
                    OptionalPackAssetReadCode.InvalidRequest,
                    store.ReadAsset(packId, assetId, fixture.Inventory).Code);
            }

            Assert.False(
                new OptionalPackAssetReadResult(
                    OptionalPackAssetReadCode.Success,
                    "missing").IsSuccess);
        });

        WithFixture(
            (fixture, store) =>
            {
                fixture.Install();
                var result = store.ReadAsset(PackId, $"asset:{AssetPath}", fixture.Inventory);
                Assert.Equal(OptionalPackAssetReadCode.AssetTooLarge, result.Code);
            },
            new byte[OptionalPackStore.MaximumReadableAssetBytes + 1]);
    }

    [Fact]
    public void Inspection_isolates_missing_size_mismatched_and_folder_mismatched_packs()
    {
        WithFixture((fixture, store) =>
        {
            var missingManifestDirectory = Path.Combine(store.PacksRoot, "vibesnake.radio.missing");
            Directory.CreateDirectory(missingManifestDirectory);
            var missing = store.InspectInstalled(fixture.Inventory);
            Assert.Contains("vibesnake.radio.missing", missing.Rejected);

            Directory.Delete(missingManifestDirectory);
            fixture.Install();
            File.WriteAllBytes(fixture.PayloadPath, [1, 2, 3]);
            var wrongSize = store.InspectInstalled(fixture.Inventory);
            Assert.Contains("size mismatch", wrongSize.Rejected[PackId]);

            fixture.Install(overwrite: true);
            var mismatchedDirectory = Path.Combine(store.PacksRoot, "vibesnake.radio.other");
            Directory.Move(fixture.PackDirectory, mismatchedDirectory);
            var wrongFolder = store.InspectInstalled(fixture.Inventory);
            Assert.Contains("folder name", wrongFolder.Rejected["vibesnake.radio.other"]);
        });
    }

    [Fact]
    public void Quarantine_rejects_invalid_installs_and_discovers_bad_quarantine_names()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();
            var consent = OptionalPackRemovalConsent.Request(
                store.InspectInstalled(fixture.Inventory).Installed,
                PackId).Consent!;
            File.WriteAllBytes(fixture.PayloadPath, [1, 2, 3]);
            var invalid = store.Quarantine(consent, fixture.Inventory);
            Assert.Equal(OptionalPackQuarantineCode.InvalidInstalledPack, invalid.Code);

            fixture.Install(overwrite: true);
            var removedRoot = Path.Combine(store.PacksRoot, ".removed");
            Directory.CreateDirectory(removedRoot);
            Directory.Move(fixture.PackDirectory, Path.Combine(removedRoot, "bad-name"));
            var inspection = store.InspectQuarantined(fixture.Inventory);
            Assert.Empty(inspection.Available);
            Assert.Contains("bad-name", inspection.Rejected);
        });
    }

    [Fact]
    public void Restore_reports_unavailable_conflict_and_receipt_version_mismatch()
    {
        WithFixture((fixture, store) =>
        {
            var unavailableReceipt = new OptionalPackQuarantineReceipt(
                PackId,
                "1.0.0",
                PackId + "-" + new string('a', 32));
            Assert.Equal(
                OptionalPackQuarantineCode.AlreadyRemoved,
                store.Restore(unavailableReceipt, fixture.Inventory).Code);

            fixture.Install();
            var consent = OptionalPackRemovalConsent.Request(
                store.InspectInstalled(fixture.Inventory).Installed,
                PackId).Consent!;
            var removed = store.Quarantine(consent, fixture.Inventory);
            var receipt = Assert.IsType<OptionalPackQuarantineReceipt>(removed.Receipt);

            Directory.CreateDirectory(fixture.PackDirectory);
            Assert.Equal(
                OptionalPackQuarantineCode.RestoreConflict,
                store.Restore(receipt, fixture.Inventory).Code);
            Directory.Delete(fixture.PackDirectory);

            var wrongVersion = receipt with { PackVersion = "2.0.0" };
            Assert.Equal(
                OptionalPackQuarantineCode.InvalidInstalledPack,
                store.Restore(wrongVersion, fixture.Inventory).Code);
        });
    }

    [Fact]
    public void Discovery_enforces_installed_and_quarantine_count_limits_before_parsing()
    {
        WithFixture((fixture, store) =>
        {
            Directory.CreateDirectory(store.PacksRoot);
            for (var index = 0; index <= OptionalPackStore.MaximumInstalledPacks; index++)
            {
                Directory.CreateDirectory(Path.Combine(store.PacksRoot, $"pack-{index:D3}"));
            }
            Assert.Throws<InvalidDataException>(() => store.InspectInstalled(fixture.Inventory));
        });

        WithFixture((fixture, store) =>
        {
            var removedRoot = Path.Combine(store.PacksRoot, ".removed");
            Directory.CreateDirectory(removedRoot);
            for (var index = 0; index <= OptionalPackStore.MaximumQuarantinedPacks; index++)
            {
                Directory.CreateDirectory(Path.Combine(removedRoot, $"pack-{index:D3}"));
            }
            Assert.Throws<InvalidDataException>(() => store.InspectQuarantined(fixture.Inventory));
        });
    }

    private static void WithFixture(
        Action<Fixture, OptionalPackStore> action,
        byte[]? payload = null)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"VibeSnake packs café {Guid.NewGuid():N}");
        try
        {
            var fixture = Fixture.Create(root, payload);
            action(fixture, new OptionalPackStore(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed record Fixture(
        string UserDataRoot,
        ContentInventory Inventory,
        byte[] Payload,
        JsonObject ManifestTemplate)
    {
        public string PackDirectory => Path.Combine(
            UserDataRoot,
            OptionalPackStore.PacksDirectoryName,
            PackId);

        public string ManifestPath => Path.Combine(
            PackDirectory,
            OptionalPackStore.ManifestFileName);

        public string PayloadPath => Path.Combine(
            PackDirectory,
            AssetPath.Replace('/', Path.DirectorySeparatorChar));

        public static Fixture Create(string root, byte[]? payloadOverride = null)
        {
            byte[] payload = payloadOverride ?? [1, 2, 3, 4];
            var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            var asset = new JsonObject
            {
                ["id"] = $"asset:{AssetPath}",
                ["path"] = AssetPath,
                ["mediaType"] = "audio/mpeg",
                ["bytes"] = payload.Length,
                ["sha256"] = sha256,
                ["integrityStatus"] = "valid",
                ["role"] = "radio-track",
                ["packId"] = PackId,
                ["runtimeUse"] = "optional",
                ["shipStatus"] = "approved",
                ["exportEligible"] = true,
                ["rights"] = new JsonObject
                {
                    ["status"] = "cleared",
                    ["source"] = "licensed fixture",
                    ["license"] = "CC-BY-4.0",
                    ["attribution"] = "Fixture Artist",
                    ["reviewNote"] = "fixture license review",
                },
                ["duplicateOf"] = null,
            };
            var inventoryJson = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["assetRoot"] = "assets",
                ["policySha256"] = PolicyHash,
                ["fileCount"] = 1,
                ["assets"] = new JsonArray(asset),
            };
            var inventory = ContentInventory.Parse(ToJson(inventoryJson));
            var manifest = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["id"] = PackId,
                ["version"] = "1.0.0",
                ["kind"] = "radio",
                ["displayName"] = "The Flow Signal",
                ["description"] = "Optional radio fixture.",
                ["compatibility"] = new JsonObject
                {
                    ["gameVersion"] = new JsonObject
                    {
                        ["minInclusive"] = "0.3.0",
                        ["maxExclusive"] = "1.1.0",
                    },
                    ["ruleset"] = new JsonObject
                    {
                        ["id"] = "vibesnake-core",
                        ["minInclusive"] = 4,
                        ["maxExclusive"] = 5,
                    },
                },
                ["inventory"] = new JsonObject
                {
                    ["schemaVersion"] = 1,
                    ["assetRoot"] = "assets",
                    ["policySha256"] = PolicyHash,
                },
                ["dependencies"] = new JsonArray(new JsonObject
                {
                    ["id"] = ContentPackManifest.CorePackId,
                    ["minInclusive"] = "1.0.0",
                    ["maxExclusive"] = "2.0.0",
                }),
                ["files"] = new JsonArray(new JsonObject
                {
                    ["id"] = asset["id"]!.GetValue<string>(),
                    ["path"] = AssetPath,
                    ["mediaType"] = "audio/mpeg",
                    ["bytes"] = payload.Length,
                    ["sha256"] = sha256,
                    ["role"] = "radio-track",
                    ["runtimeUse"] = "optional",
                    ["creditId"] = "radio-rights",
                }),
                ["credits"] = new JsonArray(new JsonObject
                {
                    ["id"] = "radio-rights",
                    ["source"] = "licensed fixture",
                    ["license"] = "CC-BY-4.0",
                    ["attribution"] = "Fixture Artist",
                    ["reviewEvidence"] = "fixture license review",
                }),
                ["radio"] = new JsonObject
                {
                    ["stationId"] = "flow_signal",
                    ["stationName"] = "The Flow Signal",
                    ["trackIds"] = new JsonArray(asset["id"]!.GetValue<string>()),
                },
            };
            return new Fixture(root, inventory, payload, manifest);
        }

        public string RenderManifest(string version)
        {
            var document = (JsonObject)ManifestTemplate.DeepClone();
            document["version"] = version;
            return ContentPackManifest.Parse(ToJson(document), Inventory).RenderCanonical();
        }

        public void Install(bool overwrite = false)
        {
            if (overwrite && Directory.Exists(PackDirectory))
            {
                Directory.Delete(PackDirectory, recursive: true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(PayloadPath)!);
            File.WriteAllBytes(PayloadPath, Payload);
            File.WriteAllText(
                ManifestPath,
                RenderManifest("1.0.0"),
                new UTF8Encoding(false));
        }
    }

    private static string ToJson(JsonNode document) =>
        document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
