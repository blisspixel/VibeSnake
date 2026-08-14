using System.Text.Json.Nodes;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class PlayerDataRecoveryServiceTests
{
    [Fact]
    public void Planning_and_inspection_are_read_only_and_backup_ids_are_stable()
    {
        var root = NewRoot();
        try
        {
            var service = new PlayerDataRecoveryService(root);
            var id = PlayerDataRecoveryService.CreateBackupId(
                new DateTimeOffset(2026, 8, 8, 12, 34, 56, 789, TimeSpan.FromHours(-7)),
                Guid.Parse("11111111-2222-3333-4444-555555555555"));

            var plan = service.CreateResetPlan(
                [PlayerDataCategory.Progression, PlayerDataCategory.Preferences],
                id);

            Assert.Equal(
                "reset-20260808T193456789Z-11111111222233334444555555555555",
                id);
            Assert.Equal(
                [PlayerDataCategory.Preferences, PlayerDataCategory.Progression],
                plan.Categories);
            Assert.Equal(
                [
                    "achievements.json",
                    "input",
                    "onboarding.json",
                    "preferences.json",
                    "progression.json",
                    "spectator-league.json",
                ],
                plan.RelativeTargets);
            Assert.Empty(service.InspectBackups());
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData(PlayerDataCategory.Preferences, "preferences.json", "input/keyboard.input_bindings.json", null, null)]
    [InlineData(PlayerDataCategory.Progression, "achievements.json", "onboarding.json", "progression.json", "spectator-league.json")]
    [InlineData(PlayerDataCategory.PersonalBests, "personal_bests.json", "score_history.json", null, null)]
    [InlineData(PlayerDataCategory.Replays, "replays/run.vibesnake-replay.json", "replay-exports/export.vibesnake-replay.json", "offline-challenges/ghosts/household-rival-1.vibesnake-ghost.json", null)]
    [InlineData(PlayerDataCategory.OptionalContent, "packs/example/pack.json", null, null, null)]
    public void Each_reset_category_is_separate_and_preserves_unselected_data(
        PlayerDataCategory category,
        string selectedPath,
        string? secondSelectedPath,
        string? thirdSelectedPath,
        string? fourthSelectedPath)
    {
        using var fixture = new RecoveryFixture();
        fixture.PopulateAllCategories();
        var preservedPath = category == PlayerDataCategory.PersonalBests
            ? "preferences.json"
            : "personal_bests.json";
        var preservedPayload = File.ReadAllText(fixture.Resolve(preservedPath));
        var plan = fixture.Service.CreateResetPlan([category], "separate-" + (byte)category);

        var result = fixture.Service.Reset(plan);

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(File.Exists(fixture.Resolve(selectedPath)));
        if (secondSelectedPath is not null)
        {
            Assert.False(File.Exists(fixture.Resolve(secondSelectedPath)));
        }

        if (thirdSelectedPath is not null)
        {
            Assert.False(File.Exists(fixture.Resolve(thirdSelectedPath)));
        }

        if (fourthSelectedPath is not null)
        {
            Assert.False(File.Exists(fixture.Resolve(fourthSelectedPath)));
        }

        Assert.Equal(preservedPayload, File.ReadAllText(fixture.Resolve(preservedPath)));
        var backup = Assert.Single(fixture.Service.InspectBackups());
        Assert.True(backup.CanRestore, backup.Message);
        Assert.Equal([category], backup.Categories);
        Assert.Equal(result.RemovedFileCount, backup.FileCount);
        Assert.Equal("backups/" + plan.BackupId, result.BackupLocation);
    }

    [Fact]
    public void Full_reset_verifies_backup_then_restores_without_overwrite()
    {
        using var fixture = new RecoveryFixture();
        var expected = fixture.PopulateAllCategories();
        var categories = Enum.GetValues<PlayerDataCategory>();
        var plan = fixture.Service.CreateResetPlan(categories, "all-data");

        var reset = fixture.Service.Reset(plan);

        Assert.True(reset.IsSuccess, reset.Message);
        Assert.Equal(expected.Count, reset.RemovedFileCount);
        foreach (var path in expected.Keys)
        {
            Assert.False(File.Exists(fixture.Resolve(path)), path);
        }

        var backup = Assert.Single(fixture.Service.InspectBackups());
        Assert.Equal(PlayerDataBackupStatus.Valid, backup.Status);
        Assert.Equal(expected.Values.Sum(value => value.Length), backup.TotalBytes);

        var restore = fixture.Service.Restore(plan.BackupId);

        Assert.True(restore.IsSuccess, restore.Message);
        Assert.Equal(expected.Count, restore.RestoredFileCount);
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, File.ReadAllText(fixture.Resolve(pair.Key)));
        }

        var conflict = fixture.Service.Restore(plan.BackupId);
        Assert.Equal(PlayerDataRestoreCode.Conflict, conflict.Code);
        Assert.Contains("Keep current data", conflict.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Resolve("backups/all-data")));
    }

    [Fact]
    public void Empty_reset_still_retains_a_verified_recovery_point()
    {
        using var fixture = new RecoveryFixture();
        var plan = fixture.Service.CreateResetPlan(
            [PlayerDataCategory.PersonalBests],
            "empty-data");

        var reset = fixture.Service.Reset(plan);
        var restore = fixture.Service.Restore(plan.BackupId);

        Assert.True(reset.IsSuccess, reset.Message);
        Assert.Equal(0, reset.RemovedFileCount);
        Assert.Contains("empty verified backup", reset.Message, StringComparison.Ordinal);
        Assert.True(restore.IsSuccess, restore.Message);
        Assert.Equal(0, restore.RestoredFileCount);
    }

    [Fact]
    public void Tampered_payload_is_detected_and_never_restored()
    {
        using var fixture = new RecoveryFixture();
        fixture.Write("preferences.json", "safe");
        var plan = fixture.Service.CreateResetPlan(
            [PlayerDataCategory.Preferences],
            "tampered");
        Assert.True(fixture.Service.Reset(plan).IsSuccess);
        fixture.Write("backups/tampered/payload/preferences.json", "changed");

        var inspection = Assert.Single(fixture.Service.InspectBackups());
        var restore = fixture.Service.Restore(plan.BackupId);

        Assert.Equal(PlayerDataBackupStatus.Corrupt, inspection.Status);
        Assert.Contains("will not be restored", inspection.Message, StringComparison.Ordinal);
        Assert.Equal(PlayerDataRestoreCode.Corrupt, restore.Code);
        Assert.False(File.Exists(fixture.Resolve("preferences.json")));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("schema")]
    [InlineData("unknown-root")]
    [InlineData("missing-root")]
    [InlineData("bad-id")]
    [InlineData("categories-kind")]
    [InlineData("categories-empty")]
    [InlineData("categories-duplicate")]
    [InlineData("categories-order")]
    [InlineData("categories-unknown")]
    [InlineData("files-kind")]
    [InlineData("file-kind")]
    [InlineData("file-unknown")]
    [InlineData("file-missing")]
    [InlineData("file-category-unknown")]
    [InlineData("file-category-unselected")]
    [InlineData("file-path-unsafe")]
    [InlineData("file-path-wrong-category")]
    [InlineData("file-length-kind")]
    [InlineData("file-length-negative")]
    [InlineData("file-length-large")]
    [InlineData("file-sha-length")]
    [InlineData("file-sha-uppercase")]
    [InlineData("file-duplicate")]
    [InlineData("file-order")]
    public void Strict_manifest_rejects_noncanonical_or_unsafe_forms(string mutation)
    {
        using var fixture = new RecoveryFixture();
        fixture.Write("preferences.json", "preferences");
        fixture.Write("input/keyboard.input_bindings.json", "bindings");
        var plan = fixture.Service.CreateResetPlan(
            [PlayerDataCategory.Preferences],
            "manifest");
        Assert.True(fixture.Service.Reset(plan).IsSuccess);
        var manifestPath = fixture.Resolve("backups/manifest/backup.json");

        MutateManifest(manifestPath, mutation);

        var inspection = Assert.Single(fixture.Service.InspectBackups());
        Assert.Equal(PlayerDataBackupStatus.Corrupt, inspection.Status);
        Assert.False(inspection.CanRestore);
    }

    [Fact]
    public void Interrupted_and_unexpected_backup_entries_are_actionable()
    {
        using var fixture = new RecoveryFixture();
        fixture.Write("backups/.building-interrupted/payload/partial", "partial");
        fixture.Write("preferences.json", "preferences");
        var plan = fixture.Service.CreateResetPlan(
            [PlayerDataCategory.Preferences],
            "unexpected");
        Assert.True(fixture.Service.Reset(plan).IsSuccess);
        fixture.Write("backups/unexpected/extra.txt", "unexpected");

        var inspections = fixture.Service.InspectBackups();

        Assert.Contains(
            inspections,
            backup => backup.Status == PlayerDataBackupStatus.Incomplete
                && backup.RelativeLocation == "backups/.building-interrupted");
        Assert.Contains(
            inspections,
            backup => backup.BackupId == "unexpected"
                && backup.Status == PlayerDataBackupStatus.Corrupt);
    }

    [Fact]
    public void Invalid_plans_ids_and_existing_backups_do_not_expand_reset_scope()
    {
        using var fixture = new RecoveryFixture();
        fixture.Write("preferences.json", "keep");
        Assert.Throws<ArgumentException>(() =>
            fixture.Service.CreateResetPlan([], "empty"));
        Assert.Throws<ArgumentException>(() =>
            fixture.Service.CreateResetPlan(
                [(PlayerDataCategory)byte.MaxValue],
                "unknown"));
        Assert.Throws<ArgumentException>(() =>
            fixture.Service.CreateResetPlan(
                [PlayerDataCategory.Preferences],
                "../escape"));

        var valid = fixture.Service.CreateResetPlan(
            [PlayerDataCategory.Preferences],
            "valid");
        var forged = valid with { RelativeTargets = ["preferences.json", "packs"] };
        var rejected = fixture.Service.Reset(forged);
        Assert.Equal(PlayerDataResetCode.InvalidPlan, rejected.Code);
        Assert.Equal("keep", File.ReadAllText(fixture.Resolve("preferences.json")));

        Assert.True(fixture.Service.Reset(valid).IsSuccess);
        var duplicate = fixture.Service.Reset(valid);
        Assert.Equal(PlayerDataResetCode.BackupAlreadyExists, duplicate.Code);
        Assert.Equal(PlayerDataRestoreCode.NotFound, fixture.Service.Restore("missing").Code);
        Assert.Equal(PlayerDataRestoreCode.NotFound, fixture.Service.Restore("../bad").Code);
    }

    [Fact]
    public void Bounded_and_locked_failures_leave_player_data_intact()
    {
        using var fixture = new RecoveryFixture();
        var oversized = fixture.Resolve("preferences.json");
        Directory.CreateDirectory(Path.GetDirectoryName(oversized)!);
        using (var stream = new FileStream(oversized, FileMode.Create, FileAccess.Write))
        {
            stream.SetLength(PlayerDataRecoveryService.MaximumFileBytes + 1);
        }

        var plan = fixture.Service.CreateResetPlan(
            [PlayerDataCategory.Preferences],
            "oversized");
        var bounded = fixture.Service.Reset(plan);
        Assert.Equal(PlayerDataResetCode.UnsafeEntry, bounded.Code);
        Assert.True(File.Exists(oversized));

        File.Delete(oversized);
        fixture.Write("preferences.json", "locked");
        using var heldLock = new FileStream(
            fixture.Resolve(".player-data-recovery.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        var locked = fixture.Service.Reset(
            fixture.Service.CreateResetPlan(
                [PlayerDataCategory.Preferences],
                "locked"));
        Assert.Equal(PlayerDataResetCode.IoError, locked.Code);
        Assert.Equal("locked", File.ReadAllText(fixture.Resolve("preferences.json")));
    }

    [Fact]
    public void Leftover_reset_staging_path_keeps_live_data_after_verified_backup()
    {
        using var fixture = new RecoveryFixture();
        fixture.Write("preferences.json", "keep");
        fixture.Write("input/keyboard.input_bindings.json", "keyboard");
        fixture.Write(".resetting-blocked", "block");
        var plan = fixture.Service.CreateResetPlan(
            [PlayerDataCategory.Preferences],
            "blocked");

        var result = fixture.Service.Reset(plan);

        Assert.Equal(PlayerDataResetCode.IoError, result.Code);
        Assert.Equal("backups/blocked", result.BackupLocation);
        Assert.Equal("keep", File.ReadAllText(fixture.Resolve("preferences.json")));
        Assert.Equal(
            "keyboard",
            File.ReadAllText(fixture.Resolve("input/keyboard.input_bindings.json")));
        Assert.True(File.Exists(fixture.Resolve(".resetting-blocked")));
        Assert.True(Directory.Exists(fixture.Resolve("backups/blocked")));
    }

    [Fact]
    public void Partial_target_removal_restores_already_removed_files()
    {
        using var fixture = new RecoveryFixture();
        fixture.Write("preferences.json", "preferences");
        fixture.Write("input/keyboard.input_bindings.json", "keyboard");
        var staging = fixture.Resolve(".resetting-partial-remove");
        Directory.CreateDirectory(staging);
        File.Move(
            fixture.Resolve("preferences.json"),
            Path.Combine(staging, "preferences.json"));

        fixture.Service.RollbackRemovedTargets(
            staging,
            [("preferences.json", false)]);

        Assert.Equal("preferences", File.ReadAllText(fixture.Resolve("preferences.json")));
        Assert.Equal(
            "keyboard",
            File.ReadAllText(fixture.Resolve("input/keyboard.input_bindings.json")));
        Assert.False(File.Exists(Path.Combine(staging, "preferences.json")));
    }

    [Fact]
    public void Restore_staging_conflict_and_invalid_roots_fail_closed()
    {
        using var fixture = new RecoveryFixture();
        fixture.Write("personal_bests.json", "best");
        var plan = fixture.Service.CreateResetPlan(
            [PlayerDataCategory.PersonalBests],
            "staged");
        Assert.True(fixture.Service.Reset(plan).IsSuccess);
        Directory.CreateDirectory(fixture.Resolve(".restoring-staged"));

        var staged = fixture.Service.Restore(plan.BackupId);

        Assert.Equal(PlayerDataRestoreCode.Conflict, staged.Code);
        Assert.False(File.Exists(fixture.Resolve("personal_bests.json")));
        Assert.Throws<ArgumentException>(() => new PlayerDataRecoveryService("relative"));
        Assert.Throws<ArgumentException>(() =>
            new PlayerDataRecoveryService(Path.GetPathRoot(fixture.Root)!));

        var blockingFile = fixture.Resolve("not-a-directory");
        File.WriteAllText(blockingFile, "block");
        var blockedService = new PlayerDataRecoveryService(Path.Combine(blockingFile, "child"));
        var blocked = blockedService.Reset(
            blockedService.CreateResetPlan(
                [PlayerDataCategory.Preferences],
                "blocked"));
        Assert.Equal(PlayerDataResetCode.IoError, blocked.Code);
    }

    private static void MutateManifest(string path, string mutation)
    {
        if (mutation == "empty")
        {
            File.WriteAllText(path, string.Empty);
            return;
        }

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var categories = root["categories"]!.AsArray();
        var files = root["files"]!.AsArray();
        var first = files[0]!.AsObject();
        switch (mutation)
        {
            case "schema":
                root["schemaVersion"] = 2;
                break;
            case "unknown-root":
                root["unknown"] = true;
                break;
            case "missing-root":
                root.Remove("files");
                break;
            case "bad-id":
                root["backupId"] = "../bad";
                break;
            case "categories-kind":
                root["categories"] = "preferences";
                break;
            case "categories-empty":
                root["categories"] = new JsonArray();
                break;
            case "categories-duplicate":
                categories.Add("preferences");
                break;
            case "categories-order":
                root["categories"] = new JsonArray("progression", "preferences");
                break;
            case "categories-unknown":
                categories[0] = "unknown";
                break;
            case "files-kind":
                root["files"] = "file";
                break;
            case "file-kind":
                files[0] = "file";
                break;
            case "file-unknown":
                first["unknown"] = true;
                break;
            case "file-missing":
                first.Remove("sha256");
                break;
            case "file-category-unknown":
                first["category"] = "unknown";
                break;
            case "file-category-unselected":
                first["category"] = "progression";
                break;
            case "file-path-unsafe":
                first["path"] = "../escape";
                break;
            case "file-path-wrong-category":
                first["path"] = "achievements.json";
                break;
            case "file-length-kind":
                first["length"] = "long";
                break;
            case "file-length-negative":
                first["length"] = -1;
                break;
            case "file-length-large":
                first["length"] = PlayerDataRecoveryService.MaximumFileBytes + 1;
                break;
            case "file-sha-length":
                first["sha256"] = "abc";
                break;
            case "file-sha-uppercase":
                first["sha256"] = new string('A', 64);
                break;
            case "file-duplicate":
                files.Add(first.DeepClone());
                break;
            case "file-order":
                var last = files[^1];
                files.RemoveAt(files.Count - 1);
                files.Insert(0, last);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        File.WriteAllText(path, root.ToJsonString());
    }

    private static string NewRoot() => Path.Combine(
        Path.GetTempPath(),
        "VibeSnakeRecoveryTests",
        Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecoveryFixture : IDisposable
    {
        public RecoveryFixture()
        {
            Root = NewRoot();
            Service = new PlayerDataRecoveryService(Root);
        }

        public string Root { get; }

        public PlayerDataRecoveryService Service { get; }

        public Dictionary<string, string> PopulateAllCategories()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["preferences.json"] = "preferences",
                ["input/keyboard.input_bindings.json"] = "keyboard",
                ["achievements.json"] = "achievements",
                ["onboarding.json"] = "onboarding",
                ["progression.json"] = "progression",
                ["spectator-league.json"] = "spectator-league",
                ["personal_bests.json"] = "personal-bests",
                ["score_history.json"] = "score-history",
                ["replays/run.vibesnake-replay.json"] = "replay",
                ["replay-exports/export.vibesnake-replay.json"] = "replay-export",
                ["offline-challenges/ghosts/household-rival-1.vibesnake-ghost.json"] = "household-rival",
                ["packs/example/pack.json"] = "optional-pack",
            };
            foreach (var pair in files)
            {
                Write(pair.Key, pair.Value);
            }

            Write("unrelated.txt", "unrelated");
            return files;
        }

        public void Write(string relativePath, string payload)
        {
            var path = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, payload);
        }

        public string Resolve(string relativePath) => Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose() => DeleteRoot(Root);
    }
}
