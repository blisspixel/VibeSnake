using System.IO.Compression;
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
    public void Installs_a_verified_archive_atomically_and_preserves_the_download()
    {
        WithFixture((fixture, store) =>
        {
            var archivePath = fixture.CreateArchive();

            var installed = store.InstallArchive(archivePath, fixture.Inventory);
            var duplicate = store.InstallArchive(archivePath, fixture.Inventory);

            Assert.True(installed.IsSuccess);
            Assert.Equal(PackId, installed.Pack!.Id);
            Assert.Equal(OptionalPackInstallCode.AlreadyInstalled, duplicate.Code);
            Assert.True(File.Exists(archivePath));
            Assert.Single(store.InspectInstalled(fixture.Inventory).Installed);
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
            var staging = Path.Combine(store.PacksRoot, ".staging");
            Assert.True(Directory.Exists(staging));
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging));
        });
    }

    [Fact]
    public void Archive_install_rejects_bad_requests_without_writing_player_data()
    {
        WithFixture((fixture, store) =>
        {
            var relative = store.InstallArchive("relative.vibesnake-pack.zip", fixture.Inventory);
            var wrongExtension = store.InstallArchive(
                Path.Combine(fixture.UserDataRoot, "pack.zip"),
                fixture.Inventory);
            var missing = store.InstallArchive(
                Path.Combine(fixture.UserDataRoot, "missing.vibesnake-pack.zip"),
                fixture.Inventory);
            var oversizedPath = Path.Combine(
                fixture.UserDataRoot,
                "oversized.vibesnake-pack.zip");
            Directory.CreateDirectory(fixture.UserDataRoot);
            using (var oversized = new FileStream(
                oversizedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                oversized.SetLength(ContentPackBudgets.RadioStationCompressedBytesMaximum + 1);
            }
            var oversizedArchive = store.InstallArchive(oversizedPath, fixture.Inventory);

            Assert.Equal(OptionalPackInstallCode.InvalidRequest, relative.Code);
            Assert.Equal(OptionalPackInstallCode.InvalidRequest, wrongExtension.Code);
            Assert.Equal(OptionalPackInstallCode.InvalidRequest, missing.Code);
            Assert.Equal(OptionalPackInstallCode.InvalidArchive, oversizedArchive.Code);
            Assert.False(Directory.Exists(fixture.PackDirectory));
        });
    }

    [Fact]
    public void Archive_install_rejects_tampering_extras_and_noncanonical_manifests()
    {
        WithFixture((fixture, store) =>
        {
            var tampered = fixture.CreateArchive(
                "tampered.vibesnake-pack.zip",
                payload: [9, 9, 9, 9]);
            Assert.Equal(
                OptionalPackInstallCode.InvalidArchive,
                store.InstallArchive(tampered, fixture.Inventory).Code);
            var staging = Path.Combine(store.PacksRoot, ".staging");
            Assert.True(Directory.Exists(staging));
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging));

            var extra = fixture.CreateArchive(
                "extra.vibesnake-pack.zip",
                append: archive => WriteArchiveEntry(archive, "../escape.txt", [1]));
            Assert.Equal(
                OptionalPackInstallCode.InvalidArchive,
                store.InstallArchive(extra, fixture.Inventory).Code);

            var noncanonical = fixture.CreateArchive(
                "noncanonical.vibesnake-pack.zip",
                manifest: ToJson(fixture.ManifestTemplate));
            Assert.Equal(
                OptionalPackInstallCode.InvalidArchive,
                store.InstallArchive(noncanonical, fixture.Inventory).Code);

            Assert.False(Directory.Exists(fixture.PackDirectory));
            Assert.False(File.Exists(Path.Combine(fixture.UserDataRoot, "escape.txt")));
        });
    }

    [Fact]
    public void Archive_install_rejects_malformed_duplicate_and_special_entries()
    {
        WithFixture((fixture, store) =>
        {
            var invalidZip = Path.Combine(
                fixture.UserDataRoot,
                "invalid.vibesnake-pack.zip");
            Directory.CreateDirectory(fixture.UserDataRoot);
            File.WriteAllBytes(invalidZip, [1, 2, 3, 4]);

            var missingManifest = fixture.CreateRawArchive(
                "missing-manifest.vibesnake-pack.zip",
                archive => WriteArchiveEntry(archive, AssetPath, fixture.Payload));
            var duplicate = fixture.CreateArchive(
                "duplicate.vibesnake-pack.zip",
                append: archive => WriteArchiveEntry(
                    archive,
                    OptionalPackStore.ManifestFileName,
                    Encoding.UTF8.GetBytes(fixture.RenderManifest("1.0.0"))));
            var caseCollision = fixture.CreateArchive(
                "case-collision.vibesnake-pack.zip",
                append: archive => WriteArchiveEntry(
                    archive,
                    AssetPath.ToUpperInvariant(),
                    fixture.Payload));
            var directoryEntry = fixture.CreateArchive(
                "directory.vibesnake-pack.zip",
                append: archive => archive.CreateEntry("unexpected/"));
            var symbolicLink = fixture.CreateArchive(
                "symlink.vibesnake-pack.zip",
                append: archive =>
                {
                    var entry = archive.CreateEntry("link");
                    entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                });
            var invalidUtf8 = fixture.CreateRawArchive(
                "invalid-utf8.vibesnake-pack.zip",
                archive =>
                {
                    WriteArchiveEntry(archive, OptionalPackStore.ManifestFileName, [0xFF]);
                    WriteArchiveEntry(archive, AssetPath, fixture.Payload);
                });

            foreach (var path in new[]
            {
                invalidZip,
                missingManifest,
                duplicate,
                caseCollision,
                directoryEntry,
                symbolicLink,
                invalidUtf8,
            })
            {
                Assert.Equal(
                    OptionalPackInstallCode.InvalidArchive,
                    store.InstallArchive(path, fixture.Inventory).Code);
            }
            Assert.False(Directory.Exists(fixture.PackDirectory));
        });
    }

    [Fact]
    public void Archive_install_honors_the_installed_pack_limit()
    {
        WithFixture((fixture, store) =>
        {
            var archivePath = fixture.CreateArchive();
            Directory.CreateDirectory(store.PacksRoot);
            for (var index = 0; index < OptionalPackStore.MaximumInstalledPacks; index++)
            {
                Directory.CreateDirectory(Path.Combine(store.PacksRoot, $"occupied-{index:D3}"));
            }

            var result = store.InstallArchive(archivePath, fixture.Inventory);

            Assert.Equal(OptionalPackInstallCode.StorageLimit, result.Code);
            Assert.False(Directory.Exists(fixture.PackDirectory));
            Assert.False(Directory.Exists(Path.Combine(store.PacksRoot, ".staging")));
        });
    }

    [Fact]
    public void Archive_install_reports_a_busy_store_without_partial_extraction()
    {
        WithFixture((fixture, store) =>
        {
            var archivePath = fixture.CreateArchive();
            Directory.CreateDirectory(store.PacksRoot);
            var lockPath = Path.Combine(store.PacksRoot, ".optional-pack-store.lock");
            using var heldLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            var result = store.InstallArchive(archivePath, fixture.Inventory);

            Assert.Equal(OptionalPackInstallCode.StoreBusy, result.Code);
            Assert.False(Directory.Exists(fixture.PackDirectory));
        });
    }

    [Fact]
    public void Archive_install_rejects_a_nonfile_lock_without_throwing()
    {
        WithFixture((fixture, store) =>
        {
            var archivePath = fixture.CreateArchive();
            Directory.CreateDirectory(store.PacksRoot);
            Directory.CreateDirectory(
                Path.Combine(store.PacksRoot, ".optional-pack-store.lock"));

            var result = store.InstallArchive(archivePath, fixture.Inventory);

            Assert.Equal(OptionalPackInstallCode.IoError, result.Code);
            Assert.False(Directory.Exists(fixture.PackDirectory));
        });
    }

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
            Assert.False(Directory.Exists(store.PacksRoot));
        });
    }

    [Fact]
    public void Quarantine_and_restore_report_a_busy_store_without_moving_data()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();
            var consent = OptionalPackRemovalConsent.Request(
                store.InspectInstalled(fixture.Inventory).Installed,
                PackId).Consent!;
            var lockPath = Path.Combine(store.PacksRoot, ".optional-pack-store.lock");
            using (var heldLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                var busyQuarantine = store.Quarantine(consent, fixture.Inventory);
                Assert.Equal(OptionalPackQuarantineCode.StoreBusy, busyQuarantine.Code);
                Assert.True(Directory.Exists(fixture.PackDirectory));
            }
            File.Delete(lockPath);
            Directory.CreateDirectory(lockPath);
            var invalidLockQuarantine = store.Quarantine(consent, fixture.Inventory);
            Assert.Equal(OptionalPackQuarantineCode.IoError, invalidLockQuarantine.Code);
            Assert.True(Directory.Exists(fixture.PackDirectory));
            Directory.Delete(lockPath);

            var removed = store.Quarantine(consent, fixture.Inventory);
            var receipt = Assert.IsType<OptionalPackQuarantineReceipt>(removed.Receipt);
            using (var heldLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                var busyRestore = store.Restore(receipt, fixture.Inventory);
                Assert.Equal(OptionalPackQuarantineCode.StoreBusy, busyRestore.Code);
                Assert.False(Directory.Exists(fixture.PackDirectory));
            }
            File.Delete(lockPath);
            Directory.CreateDirectory(lockPath);
            var invalidLockRestore = store.Restore(receipt, fixture.Inventory);
            Assert.Equal(OptionalPackQuarantineCode.IoError, invalidLockRestore.Code);
            Assert.False(Directory.Exists(fixture.PackDirectory));
            Directory.Delete(lockPath);

            Assert.True(store.Restore(receipt, fixture.Inventory).IsSuccess);
        });
    }

    [Fact]
    public void Quarantine_limit_does_not_move_an_installed_pack()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();
            var consent = OptionalPackRemovalConsent.Request(
                store.InspectInstalled(fixture.Inventory).Installed,
                PackId).Consent!;
            var removedRoot = Path.Combine(store.PacksRoot, ".removed");
            Directory.CreateDirectory(removedRoot);
            for (var index = 0; index < OptionalPackStore.MaximumQuarantinedPacks; index++)
            {
                Directory.CreateDirectory(Path.Combine(removedRoot, $"occupied-{index:D3}"));
            }

            var result = store.Quarantine(consent, fixture.Inventory);

            Assert.Equal(OptionalPackQuarantineCode.StorageLimit, result.Code);
            Assert.True(Directory.Exists(fixture.PackDirectory));
            Assert.Equal(
                OptionalPackStore.MaximumQuarantinedPacks,
                Directory.EnumerateDirectories(removedRoot).Count());
        });
    }

    [Fact]
    public void Restore_limit_keeps_the_valid_pack_in_quarantine()
    {
        WithFixture((fixture, store) =>
        {
            fixture.Install();
            var consent = OptionalPackRemovalConsent.Request(
                store.InspectInstalled(fixture.Inventory).Installed,
                PackId).Consent!;
            var removed = store.Quarantine(consent, fixture.Inventory);
            var receipt = Assert.IsType<OptionalPackQuarantineReceipt>(removed.Receipt);
            for (var index = 0; index < OptionalPackStore.MaximumInstalledPacks; index++)
            {
                Directory.CreateDirectory(Path.Combine(store.PacksRoot, $"occupied-{index:D3}"));
            }

            var result = store.Restore(receipt, fixture.Inventory);

            Assert.Equal(OptionalPackQuarantineCode.StorageLimit, result.Code);
            Assert.False(Directory.Exists(fixture.PackDirectory));
            Assert.True(Directory.Exists(Path.Combine(
                store.PacksRoot,
                ".removed",
                receipt.QuarantineName)));
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

        public string CreateArchive(
            string fileName = "flow-signal.vibesnake-pack.zip",
            string? manifest = null,
            byte[]? payload = null,
            Action<ZipArchive>? append = null)
        {
            Directory.CreateDirectory(UserDataRoot);
            var path = Path.Combine(UserDataRoot, fileName);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            WriteArchiveEntry(
                archive,
                OptionalPackStore.ManifestFileName,
                Encoding.UTF8.GetBytes(manifest ?? RenderManifest("1.0.0")));
            WriteArchiveEntry(archive, AssetPath, payload ?? Payload);
            append?.Invoke(archive);
            return path;
        }

        public string CreateRawArchive(string fileName, Action<ZipArchive> write)
        {
            Directory.CreateDirectory(UserDataRoot);
            var path = Path.Combine(UserDataRoot, fileName);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            write(archive);
            return path;
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

    private static void WriteArchiveEntry(
        ZipArchive archive,
        string name,
        byte[] contents)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(contents);
    }

    private static string ToJson(JsonNode document) =>
        document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
