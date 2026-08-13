using System.Text;
using System.Text.Json;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ReplayStoreTests
{
    [Fact]
    public void Save_is_atomic_canonical_idempotent_and_strictly_loadable()
    {
        using var temporary = new TemporaryDirectory("atomic store");
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 1, 12, 34, 56, 789, TimeSpan.Zero));
        var store = new ReplayStore(temporary.Path, clock);
        var laterStore = new ReplayStore(
            temporary.Path,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 2, 1, 2, 3, 4, TimeSpan.Zero)));
        var replay = CreateReplay(701UL);

        var first = store.Save(replay);
        var second = laterStore.Save(replay);

        Assert.Equal(ReplaySaveCode.Saved, first.Code);
        Assert.Equal(ReplaySaveCode.AlreadyExists, second.Code);
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.FileName, second.FileName);
        Assert.NotNull(first.FileName);
        Assert.Matches(
            "^20260801T123456789Z_[0-9a-f]{64}\\.vibesnake-replay\\.json$",
            first.FileName);
        var files = Directory.GetFiles(
            store.ReplayDirectory,
            $"*{ReplayStore.ReplayFileExtension}");
        Assert.Single(files);
        Assert.Empty(Directory.GetFiles(store.ReplayDirectory, "*.tmp-*"));

        var bytes = File.ReadAllBytes(files[0]);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal(replay.Serialize(), Encoding.UTF8.GetString(bytes));

        var loaded = store.Load(first.FileName);
        Assert.Equal(ReplayLoadCode.Loaded, loaded.Code);
        Assert.True(loaded.IsSuccess);
        Assert.NotNull(loaded.Replay);
        Assert.Equal(replay.Serialize(), loaded.Replay.Serialize());
        Assert.NotNull(loaded.Verification);
        Assert.True(loaded.Verification.IsValid, loaded.Verification.Message);
    }

    [Fact]
    public void Save_does_not_treat_a_noncanonical_name_as_an_idempotent_save()
    {
        using var temporary = new TemporaryDirectory("noncanonical replay");
        var store = new ReplayStore(
            temporary.Path,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 1, 12, 34, 56, 789, TimeSpan.Zero)));
        var replay = CreateReplay(711UL);
        Directory.CreateDirectory(store.ReplayDirectory);
        var noncanonicalName = $"manual_{replay.PayloadHash}{ReplayStore.ReplayFileExtension}";
        File.WriteAllText(
            Path.Combine(store.ReplayDirectory, noncanonicalName),
            replay.Serialize(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var result = store.Save(replay);

        Assert.Equal(ReplaySaveCode.Saved, result.Code);
        Assert.Matches(
            "^20260801T123456789Z_[0-9a-f]{64}\\.vibesnake-replay\\.json$",
            result.FileName);
        Assert.Equal(
            2,
            Directory.GetFiles(
                store.ReplayDirectory,
                $"*{ReplayStore.ReplayFileExtension}").Length);
    }

    [Fact]
    public void Save_reports_busy_without_mutating_when_another_process_holds_the_store_lock()
    {
        using var temporary = new TemporaryDirectory("busy replay store");
        var store = new ReplayStore(
            temporary.Path,
            storeLockWait: TimeSpan.FromMilliseconds(20));
        Directory.CreateDirectory(store.ReplayDirectory);
        using var heldLock = new FileStream(
            Path.Combine(store.ReplayDirectory, ReplayStore.StoreLockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        var result = store.Save(CreateReplay(712UL));

        Assert.Equal(ReplaySaveCode.Busy, result.Code);
        Assert.False(result.IsSuccess);
        Assert.Empty(Directory.GetFiles(
            store.ReplayDirectory,
            $"*{ReplayStore.ReplayFileExtension}"));
    }

    [Fact]
    public async Task Concurrent_saves_preserve_idempotency_and_count_capacity()
    {
        using (var idempotentDirectory = new TemporaryDirectory("concurrent idempotence"))
        {
            var replay = CreateReplay(713UL);
            var firstStore = new ReplayStore(
                idempotentDirectory.Path,
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero)),
                storeLockWait: TimeSpan.FromSeconds(30));
            var secondStore = new ReplayStore(
                idempotentDirectory.Path,
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero)),
                storeLockWait: TimeSpan.FromSeconds(30));

            var idempotentResults = await RunConcurrently(
                () => firstStore.Save(replay),
                () => secondStore.Save(replay));

            Assert.Contains(idempotentResults, result => result.Code == ReplaySaveCode.Saved);
            Assert.Contains(
                idempotentResults,
                result => result.Code == ReplaySaveCode.AlreadyExists);
            Assert.Single(Directory.GetFiles(
                firstStore.ReplayDirectory,
                $"*{ReplayStore.ReplayFileExtension}"));
        }

        using var capacityDirectory = new TemporaryDirectory("concurrent capacity");
        var capacityStoreA = new ReplayStore(
            capacityDirectory.Path,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero)),
            storeLockWait: TimeSpan.FromSeconds(30));
        var capacityStoreB = new ReplayStore(
            capacityDirectory.Path,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 1, 4, 0, 0, TimeSpan.Zero)),
            storeLockWait: TimeSpan.FromSeconds(30));
        Directory.CreateDirectory(capacityStoreA.ReplayDirectory);
        for (var index = 0; index < ReplayStore.MaximumStoredReplays - 1; index++)
        {
            var name = $"20260101T{index / 3_600:00}{index / 60 % 60:00}{index % 60:00}000Z_{index:x64}{ReplayStore.ReplayFileExtension}";
            File.WriteAllBytes(Path.Combine(capacityStoreA.ReplayDirectory, name), []);
        }

        var capacityResults = await RunConcurrently(
            () => capacityStoreA.Save(CreateReplay(714UL)),
            () => capacityStoreB.Save(CreateReplay(715UL)));

        Assert.Contains(capacityResults, result => result.Code == ReplaySaveCode.Saved);
        Assert.Contains(
            capacityResults,
            result => result.Code == ReplaySaveCode.CapacityReached);
        Assert.Equal(
            ReplayStore.MaximumStoredReplays,
            Directory.GetFiles(
                capacityStoreA.ReplayDirectory,
                $"*{ReplayStore.ReplayFileExtension}").Length);
    }

    [Fact]
    public void Latest_uses_generated_name_order_and_supports_non_ascii_roots()
    {
        using var temporary = new TemporaryDirectory("signal path Ω");
        var earlier = new ReplayStore(
            temporary.Path,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero)));
        var later = new ReplayStore(
            temporary.Path,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero)));
        var firstReplay = CreateReplay(702UL);
        var secondReplay = CreateReplay(703UL);

        Assert.True(earlier.Save(firstReplay).IsSuccess);
        var laterSave = later.Save(secondReplay);
        Assert.True(laterSave.IsSuccess);

        var latest = earlier.LoadLatest();
        Assert.True(latest.IsSuccess, latest.Message);
        Assert.Equal(laterSave.FileName, latest.FileName);
        Assert.Equal(secondReplay.PayloadHash, latest.Replay?.PayloadHash);

        File.WriteAllText(
            Path.Combine(earlier.ReplayDirectory, "manual.vibesnake-replay.json"),
            "not a generated replay name",
            new UTF8Encoding(false));
        var listed = earlier.ListStored();
        Assert.True(listed.IsSuccess, listed.Message);
        Assert.Equal(ReplayListCode.Listed, listed.Code);
        Assert.Equal(2, listed.Replays.Count);
        Assert.Equal(laterSave.FileName, listed.Replays[0].FileName);
        Assert.Equal("2026-08-01T02:00:00.000Z", listed.Replays[0].StoredAtUtc);
        Assert.Equal(secondReplay.PayloadHash, listed.Replays[0].PayloadHash);
        Assert.True(listed.Replays[0].FileBytes > 0);
        Assert.Equal(firstReplay.PayloadHash, listed.Replays[1].PayloadHash);
    }

    [Fact]
    public void Load_reports_missing_invalid_name_oversize_and_invalid_utf8_precisely()
    {
        using var temporary = new TemporaryDirectory("bounded imports");
        var store = new ReplayStore(temporary.Path);

        Assert.Equal(ReplayLoadCode.NotFound, store.LoadLatest().Code);
        Assert.True(store.ListStored().IsSuccess);
        Assert.Empty(store.ListStored().Replays);
        Assert.Equal(ReplayLoadCode.NotFound, store.Load("missing.vibesnake-replay.json").Code);
        Assert.Equal(ReplayLoadCode.InvalidName, store.Load("../escape.vibesnake-replay.json").Code);
        Assert.Equal(ReplayLoadCode.InvalidName, store.Load("stream:escape.vibesnake-replay.json").Code);
        Assert.Equal(ReplayLoadCode.InvalidName, store.Load("wrong.json").Code);

        Directory.CreateDirectory(store.ReplayDirectory);
        var oversizedName = "oversized.vibesnake-replay.json";
        using (var stream = File.Create(Path.Combine(store.ReplayDirectory, oversizedName)))
        {
            stream.SetLength(RunReplay.MaximumSerializedCharacters + 1L);
        }

        Assert.Equal(ReplayLoadCode.TooLarge, store.Load(oversizedName).Code);

        var invalidUtf8Name = "invalid-utf8.vibesnake-replay.json";
        File.WriteAllBytes(
            Path.Combine(store.ReplayDirectory, invalidUtf8Name),
            [0xff, 0xfe, 0xfd]);
        Assert.Equal(ReplayLoadCode.InvalidEncoding, store.Load(invalidUtf8Name).Code);

        var bomName = "bom.vibesnake-replay.json";
        File.WriteAllBytes(
            Path.Combine(store.ReplayDirectory, bomName),
            [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(CreateReplay(704UL).Serialize())]);
        Assert.Equal(ReplayLoadCode.InvalidEncoding, store.Load(bomName).Code);
    }

    [Theory]
    [InlineData("{", ReplayCompatibilityCode.InvalidPayload)]
    [InlineData("future", ReplayCompatibilityCode.UnsupportedSchema)]
    [InlineData("tampered", ReplayCompatibilityCode.IntegrityMismatch)]
    public void InspectExternal_preserves_rejected_sources_and_surfaces_compatibility(
        string mutation,
        ReplayCompatibilityCode expectedCompatibility)
    {
        using var temporary = new TemporaryDirectory("external source");
        var store = new ReplayStore(temporary.Path);
        var source = Path.Combine(temporary.Path, "candidate.json");
        var valid = CreateReplay(705UL).Serialize();
        var payload = mutation switch
        {
            "{" => "{",
            "future" => ReplaceOnce(valid, "\"schemaVersion\":1", "\"schemaVersion\":2"),
            "tampered" => ReplaceOnce(valid, "\"commands\":[0]", "\"commands\":[1]"),
            _ => throw new InvalidOperationException("Unknown test mutation."),
        };
        File.WriteAllText(source, payload, new UTF8Encoding(false));
        var before = File.ReadAllBytes(source);

        var result = store.InspectExternal(source);

        Assert.Equal(ReplayLoadCode.Incompatible, result.Code);
        Assert.Equal(expectedCompatibility, result.Compatibility?.Code);
        Assert.Null(result.Replay);
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    [Fact]
    public void InspectExternal_requires_an_absolute_file_and_never_mutates_it()
    {
        using var temporary = new TemporaryDirectory("external valid");
        var store = new ReplayStore(temporary.Path);
        var source = Path.Combine(temporary.Path, "reviewed.json");
        var replay = CreateReplay(706UL);
        File.WriteAllText(source, replay.Serialize(), new UTF8Encoding(false));
        var before = File.ReadAllBytes(source);

        var result = store.InspectExternal(source);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(replay.PayloadHash, result.Replay?.PayloadHash);
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(
            ReplayLoadCode.InvalidName,
            store.InspectExternal("relative-replay.json").Code);
        Assert.Equal(
            ReplayLoadCode.NotFound,
            store.InspectExternal(Path.Combine(temporary.Path, "absent.json")).Code);
    }

    [Fact]
    public void Save_and_load_convert_io_failures_to_actionable_results()
    {
        using var temporary = new TemporaryDirectory("io boundary");
        var rootFile = Path.Combine(temporary.Path, "root-is-a-file");
        File.WriteAllText(rootFile, "occupied", new UTF8Encoding(false));
        var store = new ReplayStore(rootFile);

        var save = store.Save(CreateReplay(707UL));
        var load = store.Load("run.vibesnake-replay.json");

        Assert.Equal(ReplaySaveCode.IoFailure, save.Code);
        Assert.False(save.IsSuccess);
        Assert.Contains("could not", save.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ReplayLoadCode.IoFailure, load.Code);
        Assert.False(load.IsSuccess);
        Assert.Equal(ReplayLoadCode.IoFailure, store.LoadLatest().Code);
        Assert.Equal(ReplayListCode.IoFailure, store.ListStored().Code);
    }

    [Fact]
    public void Save_rejects_divergent_replays_and_never_creates_storage()
    {
        using var temporary = new TemporaryDirectory("invalid save");
        var store = new ReplayStore(temporary.Path);
        var replay = CreateDivergentReplay();

        var result = store.Save(replay);

        Assert.Equal(ReplaySaveCode.ReplayInvalid, result.Code);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Verification);
        Assert.False(result.Verification.IsValid);
        Assert.False(Directory.Exists(store.ReplayDirectory));
    }

    [Fact]
    public void Save_refuses_to_overwrite_a_conflicting_destination()
    {
        using var temporary = new TemporaryDirectory("conflicting save");
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var store = new ReplayStore(temporary.Path, clock);
        var replay = CreateReplay(708UL);
        var fileName = $"20260801T120000000Z_{replay.PayloadHash}{ReplayStore.ReplayFileExtension}";
        Directory.CreateDirectory(store.ReplayDirectory);
        var destination = Path.Combine(store.ReplayDirectory, fileName);
        var existing = Encoding.UTF8.GetBytes("different content");
        File.WriteAllBytes(destination, existing);

        var result = store.Save(replay);

        Assert.Equal(ReplaySaveCode.IoFailure, result.Code);
        Assert.False(result.IsSuccess);
        Assert.Contains("no file was overwritten", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(existing, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(store.ReplayDirectory, "*.tmp-*"));
    }

    [Fact]
    public void Load_distinguishes_compatible_but_divergent_replays()
    {
        using var temporary = new TemporaryDirectory("divergent load");
        var store = new ReplayStore(temporary.Path);
        var replay = CreateDivergentReplay();
        const string fileName = "divergent.vibesnake-replay.json";
        Directory.CreateDirectory(store.ReplayDirectory);
        File.WriteAllText(
            Path.Combine(store.ReplayDirectory, fileName),
            replay.Serialize(),
            new UTF8Encoding(false));

        var result = store.Load(fileName);

        Assert.Equal(ReplayLoadCode.VerificationFailed, result.Code);
        Assert.True(result.Compatibility?.IsCompatible);
        Assert.NotNull(result.Verification);
        Assert.False(result.Verification.IsValid);
        Assert.Null(result.Replay);
    }

    [Fact]
    public void InspectExternal_reports_malformed_absolute_paths()
    {
        using var temporary = new TemporaryDirectory("malformed path");
        var store = new ReplayStore(temporary.Path);
        var malformed = temporary.Path
            + System.IO.Path.DirectorySeparatorChar
            + "bad\0replay.json";

        var result = store.InspectExternal(malformed);

        Assert.Equal(ReplayLoadCode.InvalidName, result.Code);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Store_capacity_limits_preserve_every_existing_replay()
    {
        using var temporary = new TemporaryDirectory("count capacity");
        var store = new ReplayStore(temporary.Path);
        Directory.CreateDirectory(store.ReplayDirectory);
        var beginning = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < ReplayStore.MaximumStoredReplays; index++)
        {
            var timestamp = beginning.AddMilliseconds(index).ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                System.Globalization.CultureInfo.InvariantCulture);
            var name = $"{timestamp}_{index:x64}{ReplayStore.ReplayFileExtension}";
            File.WriteAllBytes(Path.Combine(store.ReplayDirectory, name), []);
        }

        var before = Directory.GetFiles(
            store.ReplayDirectory,
            $"*{ReplayStore.ReplayFileExtension}");
        var save = store.Save(CreateReplay(710UL));

        Assert.Equal(ReplaySaveCode.CapacityReached, save.Code);
        Assert.False(save.IsSuccess);
        Assert.Contains("preserved", save.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            before,
            Directory.GetFiles(
                store.ReplayDirectory,
                $"*{ReplayStore.ReplayFileExtension}"));

        var overflowName = $"20260102T000000000Z_{ReplayStore.MaximumStoredReplays:x64}{ReplayStore.ReplayFileExtension}";
        File.WriteAllBytes(Path.Combine(store.ReplayDirectory, overflowName), []);
        Assert.Equal(ReplayLoadCode.CapacityExceeded, store.LoadLatest().Code);
        Assert.Equal(ReplayListCode.CapacityExceeded, store.ListStored().Code);
    }

    [Fact]
    public void Store_byte_limit_is_checked_without_deleting_large_existing_files()
    {
        using var temporary = new TemporaryDirectory("byte capacity");
        var store = new ReplayStore(temporary.Path);
        Directory.CreateDirectory(store.ReplayDirectory);
        var existing = Path.Combine(
            store.ReplayDirectory,
            $"20260101T000000000Z_{0:x64}{ReplayStore.ReplayFileExtension}");
        using (var stream = File.Create(existing))
        {
            stream.SetLength(ReplayStore.MaximumStoredReplayBytes);
        }

        var save = store.Save(CreateReplay(711UL));

        Assert.Equal(ReplaySaveCode.CapacityReached, save.Code);
        Assert.Equal(ReplayStore.MaximumStoredReplayBytes, new FileInfo(existing).Length);
        Assert.Single(Directory.GetFiles(
            store.ReplayDirectory,
            $"*{ReplayStore.ReplayFileExtension}"));

        using (var stream = File.Open(existing, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(ReplayStore.MaximumStoredReplayBytes + 1L);
        }

        Assert.Equal(ReplayListCode.CapacityExceeded, store.ListStored().Code);
    }

    [Fact]
    public void Browser_inspects_complete_metadata_and_distinguishes_replay_states()
    {
        using var temporary = new TemporaryDirectory("replay browser states");
        var store = new ReplayStore(
            temporary.Path,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 8, 1, 2, 3, 4, TimeSpan.Zero)));
        IReadOnlyList<Direction>[] commands = [[Direction.Up], [Direction.Left]];
        var verifiedReplay = RunReplay.Capture(
            SnakeRun.Create(901UL, RunModeCatalog.CreateConfig(RunModeCatalog.Vibe)),
            commands,
            checkpointInterval: 1,
            appVersion: "0.2.1",
            capturedAtUtc: "2026-08-07T23:59:58.123Z");
        var saved = store.Save(verifiedReplay);
        Assert.True(saved.IsSuccess, saved.Message);
        Directory.CreateDirectory(store.ReplayDirectory);
        WriteGeneratedReplay(
            store,
            1,
            verifiedReplay.Serialize().Replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":2",
                StringComparison.Ordinal));
        WriteGeneratedReplay(
            store,
            2,
            verifiedReplay.Serialize().Replace(
                "\"commands\":[0]",
                "\"commands\":[1]",
                StringComparison.Ordinal));
        WriteGeneratedReplay(store, 3, "{");

        var result = store.BrowseStored();

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(4, result.Replays.Count);
        var verified = Assert.Single(result.Replays, entry => entry.IsPlayable);
        Assert.Equal(ReplayBrowserState.Verified, verified.State);
        Assert.Equal(nameof(ReplayVerificationCode.Verified), verified.StatusCode);
        Assert.Equal("2026-08-08T01:02:03.004Z", verified.StoredAtUtc);
        Assert.Equal("2026-08-07T23:59:58.123Z", verified.DisplayedAtUtc);
        Assert.Equal(RunModeCatalog.VibeId, verified.ModeId);
        Assert.Equal(RunModeCatalog.CurrentModeVersion, verified.ModeVersion);
        Assert.Equal(RulesetIdentity.CurrentId, verified.RulesetId);
        Assert.Equal(RulesetIdentity.CurrentVersion, verified.RulesVersion);
        Assert.Equal(verifiedReplay.Outcome.Score, verified.Score);
        Assert.Equal(901UL, verified.GameplaySeed);
        Assert.Equal(2, verified.StepCount);
        Assert.Contains(
            result.Replays,
            entry => entry.State == ReplayBrowserState.Incompatible
                && entry.StatusCode == nameof(ReplayCompatibilityCode.UnsupportedSchema));
        Assert.Contains(
            result.Replays,
            entry => entry.State == ReplayBrowserState.Modified
                && entry.StatusCode == nameof(ReplayCompatibilityCode.IntegrityMismatch));
        Assert.Contains(
            result.Replays,
            entry => entry.State == ReplayBrowserState.Unreadable
                && entry.StatusCode == nameof(ReplayCompatibilityCode.InvalidPayload));
    }

    [Fact]
    public void Browser_and_id_load_report_empty_invalid_missing_and_capacity_failures()
    {
        using var temporary = new TemporaryDirectory("replay browser boundaries");
        var store = new ReplayStore(temporary.Path);

        var empty = store.BrowseStored();
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Replays);
        Assert.Equal(
            ReplayLoadCode.InvalidName,
            store.LoadByReplayId("not-a-replay-id").Code);
        Assert.Equal(
            ReplayLoadCode.NotFound,
            store.LoadByReplayId(new string('0', 64)).Code);

        Directory.CreateDirectory(store.ReplayDirectory);
        for (var index = 0; index <= ReplayStore.MaximumStoredReplays; index++)
        {
            WriteGeneratedReplay(store, index, string.Empty);
        }

        var overflow = store.BrowseStored();
        Assert.False(overflow.IsSuccess);
        Assert.Equal(ReplayListCode.CapacityExceeded, overflow.Code);
        Assert.Equal(
            ReplayLoadCode.CapacityExceeded,
            store.LoadByReplayId(new string('0', 64)).Code);
    }

    [Fact]
    public void Export_is_verified_atomic_idempotent_bounded_and_source_preserving()
    {
        using var temporary = new TemporaryDirectory("replay export");
        var store = new ReplayStore(
            temporary.Path,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 8, 2, 3, 4, 5, TimeSpan.Zero)));
        var replay = CreateReplay(902UL);
        var saved = store.Save(replay);
        Assert.True(saved.IsSuccess, saved.Message);
        var sourcePath = Path.Combine(store.ReplayDirectory, saved.FileName!);
        var sourceBefore = File.ReadAllBytes(sourcePath);

        var first = store.Export(replay.PayloadHash);
        var second = store.Export(replay.PayloadHash);

        Assert.Equal(ReplayExportCode.Exported, first.Code);
        Assert.Equal(ReplayExportCode.AlreadyExists, second.Code);
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.FileName, second.FileName);
        Assert.Equal(replay.PayloadHash, first.PayloadHash);
        Assert.NotNull(first.FileName);
        Assert.Matches(
            "^replay_20260808T020304005Z_[0-9a-f]{64}\\.vibesnake-replay\\.json$",
            first.FileName);
        var exportedPath = Path.Combine(store.ReplayExportDirectory, first.FileName);
        Assert.Equal(replay.Serialize(), File.ReadAllText(exportedPath));
        Assert.True(store.InspectExternal(exportedPath).IsSuccess);
        Assert.Equal(sourceBefore, File.ReadAllBytes(sourcePath));
        Assert.Empty(Directory.GetFiles(store.ReplayExportDirectory, "*.tmp-*"));

        Assert.Equal(
            ReplayExportCode.InvalidReplayId,
            store.Export("invalid").Code);
        Assert.Equal(
            ReplayExportCode.NotFound,
            store.Export(new string('f', 64)).Code);

        File.WriteAllText(exportedPath, "different", new UTF8Encoding(false));
        Assert.Equal(ReplayExportCode.IoFailure, store.Export(replay.PayloadHash).Code);
        Assert.Equal("different", File.ReadAllText(exportedPath));
    }

    [Fact]
    public void Export_rejects_unverified_busy_capacity_and_unavailable_roots()
    {
        using (var invalidDirectory = new TemporaryDirectory("invalid replay export"))
        {
            var store = new ReplayStore(invalidDirectory.Path);
            Directory.CreateDirectory(store.ReplayDirectory);
            var replayId = 1.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
            WriteGeneratedReplay(store, 1, "{");
            Assert.Equal(ReplayExportCode.ReplayUnavailable, store.Export(replayId).Code);
        }

        using (var busyDirectory = new TemporaryDirectory("busy replay export"))
        {
            var store = new ReplayStore(
                busyDirectory.Path,
                storeLockWait: TimeSpan.FromMilliseconds(20));
            var replay = CreateReplay(903UL);
            Assert.True(store.Save(replay).IsSuccess);
            using var heldLock = new FileStream(
                Path.Combine(store.ReplayDirectory, ReplayStore.StoreLockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.Equal(ReplayExportCode.Busy, store.Export(replay.PayloadHash).Code);
        }

        using (var capacityDirectory = new TemporaryDirectory("full replay exports"))
        {
            var store = new ReplayStore(capacityDirectory.Path);
            var replay = CreateReplay(904UL);
            Assert.True(store.Save(replay).IsSuccess);
            Directory.CreateDirectory(store.ReplayExportDirectory);
            for (var index = 0; index < ReplayStore.MaximumReplayExports; index++)
            {
                File.WriteAllBytes(
                    Path.Combine(
                        store.ReplayExportDirectory,
                        $"replay_{index:000}_{index:x64}{ReplayStore.ReplayFileExtension}"),
                    []);
            }

            Assert.Equal(
                ReplayExportCode.CapacityReached,
                store.Export(replay.PayloadHash).Code);
        }

        using var unavailableDirectory = new TemporaryDirectory("unavailable replay exports");
        var unavailableStore = new ReplayStore(unavailableDirectory.Path);
        var unavailableReplay = CreateReplay(905UL);
        Assert.True(unavailableStore.Save(unavailableReplay).IsSuccess);
        File.WriteAllText(
            unavailableStore.ReplayExportDirectory,
            "occupied",
            new UTF8Encoding(false));
        Assert.Equal(
            ReplayExportCode.IoFailure,
            unavailableStore.Export(unavailableReplay.PayloadHash).Code);
    }

    [Fact]
    public void Capture_summary_is_closed_versioned_deterministic_and_privacy_safe()
    {
        IReadOnlyList<Direction>[] commands = [[Direction.Up], [Direction.Left]];
        var replay = RunReplay.Capture(
            SnakeRun.Create(906UL),
            commands,
            checkpointInterval: 1,
            appVersion: "0.2.1-replay",
            capturedAtUtc: "2026-08-08T02:03:04.005Z");

        var first = ReplayCaptureSummary.Create(replay, "0.2.1");
        var second = ReplayCaptureSummary.Create(replay, "0.2.1");
        var payload = first.Serialize();

        Assert.Equal(payload, second.Serialize());
        Assert.Equal(ReplayCaptureSummary.CurrentSchemaVersion, first.SchemaVersion);
        Assert.Equal(ReplayCaptureSummary.KindId, first.Kind);
        Assert.Equal("0.2.1", first.ExportingAppVersion);
        Assert.Equal("0.2.1-replay", first.ReplayAppVersion);
        Assert.Equal(SnakeRun.RulesetId, first.RulesetId);
        Assert.Equal(SnakeRun.RulesVersion, first.RulesVersion);
        Assert.Equal(RunModeCatalog.VibeId, first.ModeId);
        Assert.Equal(RunModeCatalog.CurrentModeVersion, first.ModeVersion);
        Assert.Equal(RunModeCatalog.VibeFixedScoreCategoryId, first.ScoreCategoryId);
        Assert.Equal(replay.PayloadHash, first.ReplayPayloadHash);
        Assert.Equal(906UL, first.GameplaySeed);
        Assert.False(first.ContainsPlayerIdentity);
        Assert.False(first.ContainsPrivatePaths);
        Assert.DoesNotContain("user://", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("playerName", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("displayName", payload, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(payload);
        string[] expectedFields =
        [
            "schemaVersion",
            "kind",
            "exportingAppVersion",
            "replayAppVersion",
            "rulesetId",
            "rulesVersion",
            "modeId",
            "modeVersion",
            "scoreCategoryId",
            "configHashAlgorithm",
            "configHash",
            "stateHashAlgorithm",
            "replayIntegrityAlgorithm",
            "replayPayloadHash",
            "capturedAtUtc",
            "gameplaySeed",
            "stepCount",
            "finalTick",
            "status",
            "deathCause",
            "score",
            "finalStateHash",
            "containsPlayerIdentity",
            "containsPrivatePaths",
        ];
        Assert.Equal(
            expectedFields,
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Throws<ArgumentNullException>(() =>
            ReplayCaptureSummary.Create(null!, "0.2.1"));
        Assert.Throws<ArgumentException>(() =>
            ReplayCaptureSummary.Create(replay, "bad version/with/path"));
        Assert.Throws<ArgumentException>(() =>
            ReplayCaptureSummary.Create(CreateDivergentReplay(), "0.2.1"));
    }

    [Fact]
    public void Capture_summary_export_is_atomic_idempotent_bounded_and_non_overwriting()
    {
        using var temporary = new TemporaryDirectory("capture summary export");
        var store = new ReplayStore(temporary.Path);
        var replay = CreateReplay(907UL);
        Assert.True(store.Save(replay).IsSuccess);

        var first = store.ExportCaptureSummary(replay.PayloadHash, "0.2.1");
        var second = store.ExportCaptureSummary(replay.PayloadHash, "0.2.1");

        Assert.Equal(ReplayCaptureSummaryExportCode.Exported, first.Code);
        Assert.Equal(ReplayCaptureSummaryExportCode.AlreadyExists, second.Code);
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.FileName, second.FileName);
        Assert.Matches("^[0-9a-f]{64}$", first.Sha256);
        Assert.Matches(
            "^run-summary_[0-9a-f]{64}\\.vibesnake-run-summary\\.json$",
            first.FileName);
        var path = Path.Combine(store.ReplayExportDirectory, first.FileName!);
        var original = File.ReadAllText(path);
        Assert.Equal(
            ReplayCaptureSummary.KindId,
            JsonDocument.Parse(original).RootElement.GetProperty("kind").GetString());
        Assert.Empty(Directory.GetFiles(store.ReplayExportDirectory, "*.tmp-*"));

        File.WriteAllText(path, "different", new UTF8Encoding(false));
        var conflict = store.ExportCaptureSummary(replay.PayloadHash, "0.2.1");
        Assert.Equal(ReplayCaptureSummaryExportCode.IoFailure, conflict.Code);
        Assert.Equal("different", File.ReadAllText(path));
        Assert.Equal(
            ReplayCaptureSummaryExportCode.InvalidReplayId,
            store.ExportCaptureSummary("invalid", "0.2.1").Code);
        Assert.Equal(
            ReplayCaptureSummaryExportCode.NotFound,
            store.ExportCaptureSummary(new string('f', 64), "0.2.1").Code);
        Assert.Throws<ArgumentException>(() =>
            store.ExportCaptureSummary(replay.PayloadHash, " "));
    }

    [Fact]
    public void Capture_summary_export_fails_closed_for_unavailable_busy_and_full_stores()
    {
        using (var invalidDirectory = new TemporaryDirectory("invalid capture summary"))
        {
            var store = new ReplayStore(invalidDirectory.Path);
            Directory.CreateDirectory(store.ReplayDirectory);
            var replayId = 1.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
            WriteGeneratedReplay(store, 1, "{");
            Assert.Equal(
                ReplayCaptureSummaryExportCode.ReplayUnavailable,
                store.ExportCaptureSummary(replayId, "0.2.1").Code);
        }

        using (var busyDirectory = new TemporaryDirectory("busy capture summary"))
        {
            var store = new ReplayStore(
                busyDirectory.Path,
                storeLockWait: TimeSpan.FromMilliseconds(20));
            var replay = CreateReplay(908UL);
            Assert.True(store.Save(replay).IsSuccess);
            using var heldLock = new FileStream(
                Path.Combine(store.ReplayDirectory, ReplayStore.StoreLockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.Equal(
                ReplayCaptureSummaryExportCode.Busy,
                store.ExportCaptureSummary(replay.PayloadHash, "0.2.1").Code);
        }

        using (var capacityDirectory = new TemporaryDirectory("full capture summaries"))
        {
            var store = new ReplayStore(capacityDirectory.Path);
            var replay = CreateReplay(909UL);
            Assert.True(store.Save(replay).IsSuccess);
            Directory.CreateDirectory(store.ReplayExportDirectory);
            for (var index = 0; index < ReplayStore.MaximumCaptureSummaryExports; index++)
            {
                File.WriteAllBytes(
                    Path.Combine(
                        store.ReplayExportDirectory,
                        $"run-summary_{index:x64}{ReplayStore.CaptureSummaryFileExtension}"),
                    []);
            }

            Assert.Equal(
                ReplayCaptureSummaryExportCode.CapacityReached,
                store.ExportCaptureSummary(replay.PayloadHash, "0.2.1").Code);
        }

        using var unavailableDirectory = new TemporaryDirectory("unavailable capture summary");
        var unavailableStore = new ReplayStore(unavailableDirectory.Path);
        var unavailableReplay = CreateReplay(910UL);
        Assert.True(unavailableStore.Save(unavailableReplay).IsSuccess);
        File.WriteAllText(
            unavailableStore.ReplayExportDirectory,
            "occupied",
            new UTF8Encoding(false));
        Assert.Equal(
            ReplayCaptureSummaryExportCode.IoFailure,
            unavailableStore.ExportCaptureSummary(unavailableReplay.PayloadHash, "0.2.1").Code);
    }

    [Fact]
    public void Deletion_requires_exact_fresh_consent_and_preserves_exports()
    {
        using var temporary = new TemporaryDirectory("replay delete consent");
        var store = new ReplayStore(
            temporary.Path,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 8, 3, 4, 5, 6, TimeSpan.Zero)));
        var replay = CreateReplay(906UL);
        var saved = store.Save(replay);
        Assert.True(saved.IsSuccess, saved.Message);
        var exported = store.Export(replay.PayloadHash);
        Assert.True(exported.IsSuccess, exported.Message);
        var storedPath = Path.Combine(store.ReplayDirectory, saved.FileName!);
        var exportPath = Path.Combine(store.ReplayExportDirectory, exported.FileName!);

        var planned = store.PlanDeletion(replay.PayloadHash);

        Assert.True(planned.IsSuccess, planned.Message);
        Assert.NotNull(planned.Plan);
        Assert.True(File.Exists(storedPath));
        Assert.Contains("Permanently delete", planned.Plan.ConfirmationText, StringComparison.Ordinal);
        Assert.Equal(
            ReplayDeletionPlanCode.InvalidReplayId,
            store.PlanDeletion("invalid").Code);
        Assert.Equal(
            ReplayDeletionPlanCode.NotFound,
            store.PlanDeletion(new string('f', 64)).Code);

        var invalidPlan = planned.Plan with { ContentSha256 = "invalid" };
        Assert.Equal(ReplayDeleteCode.InvalidPlan, store.Delete(invalidPlan).Code);
        Assert.True(File.Exists(storedPath));

        var bytes = File.ReadAllBytes(storedPath);
        bytes[^1] = bytes[^1] == (byte)'\n' ? (byte)' ' : (byte)'\n';
        File.WriteAllBytes(storedPath, bytes);
        Assert.Equal(
            ReplayDeleteCode.ChangedSinceConsent,
            store.Delete(planned.Plan).Code);
        Assert.True(File.Exists(storedPath));

        var refreshed = store.PlanDeletion(replay.PayloadHash);
        Assert.True(refreshed.IsSuccess, refreshed.Message);
        var deleted = store.Delete(refreshed.Plan!);
        Assert.True(deleted.IsSuccess, deleted.Message);
        Assert.False(File.Exists(storedPath));
        Assert.True(File.Exists(exportPath));
        Assert.Equal(
            ReplayDeleteCode.NotFound,
            store.Delete(refreshed.Plan!).Code);
    }

    [Fact]
    public void Deletion_reports_busy_missing_and_unavailable_storage_without_mutation()
    {
        using (var missingDirectory = new TemporaryDirectory("missing replay deletion"))
        {
            var store = new ReplayStore(missingDirectory.Path);
            var plan = new ReplayDeletionPlan(
                new string('1', 64),
                "2026-08-08T00:00:00.000Z",
                0,
                new string('2', 64),
                "confirm");
            Assert.Equal(ReplayDeleteCode.NotFound, store.Delete(plan).Code);
        }

        using (var busyDirectory = new TemporaryDirectory("busy replay deletion"))
        {
            var store = new ReplayStore(
                busyDirectory.Path,
                storeLockWait: TimeSpan.FromMilliseconds(20));
            var replay = CreateReplay(907UL);
            Assert.True(store.Save(replay).IsSuccess);
            var plan = store.PlanDeletion(replay.PayloadHash).Plan!;
            using var heldLock = new FileStream(
                Path.Combine(store.ReplayDirectory, ReplayStore.StoreLockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.Equal(ReplayDeleteCode.Busy, store.Delete(plan).Code);
            Assert.True(store.LoadByReplayId(replay.PayloadHash).IsSuccess);
        }

        using var unavailableDirectory = new TemporaryDirectory("unavailable replay deletion");
        var rootFile = Path.Combine(unavailableDirectory.Path, "root-file");
        File.WriteAllText(rootFile, "occupied", new UTF8Encoding(false));
        var unavailableStore = new ReplayStore(rootFile);
        Assert.Equal(
            ReplayDeletionPlanCode.IoFailure,
            unavailableStore.PlanDeletion(new string('1', 64)).Code);
    }

    [Fact]
    public void Constructor_and_save_reject_invalid_inputs()
    {
        using var temporary = new TemporaryDirectory("null replay");
        Assert.Throws<ArgumentException>(() => new ReplayStore("relative"));
        Assert.Throws<ArgumentException>(() => new ReplayStore(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayStore(
            temporary.Path,
            storeLockWait: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayStore(
            temporary.Path,
            storeLockWait: TimeSpan.FromSeconds(31)));

        var store = new ReplayStore(temporary.Path);
        Assert.Throws<ArgumentNullException>(() => store.Save(null!));
        Assert.Throws<ArgumentException>(() => store.Load(" "));
        Assert.Throws<ArgumentException>(() => store.LoadByReplayId(" "));
        Assert.Throws<ArgumentException>(() => store.Export(" "));
        Assert.Throws<ArgumentException>(() => store.ExportCaptureSummary(" ", "0.2.1"));
        Assert.Throws<ArgumentException>(() => store.PlanDeletion(" "));
        Assert.Throws<ArgumentNullException>(() => store.Delete(null!));
        Assert.Throws<ArgumentException>(() => store.InspectExternal(" "));
    }

    private static async Task<IReadOnlyList<ReplaySaveResult>> RunConcurrently(
        Func<ReplaySaveResult> first,
        Func<ReplaySaveResult> second)
    {
        using var ready = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        Task<ReplaySaveResult> Start(Func<ReplaySaveResult> operation) =>
            Task.Factory.StartNew(
                () =>
                {
                    ready.Signal();
                    // Bound wait so a stuck peer cannot hang the suite forever.
                    if (!release.Wait(TimeSpan.FromSeconds(30)))
                    {
                        throw new TimeoutException(
                            "Concurrent save peer did not release within 30 seconds.");
                    }

                    return operation();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

        var tasks = new[] { Start(first), Start(second) };
        // Hosted Linux runners can delay threadpool work under load; wait longer than 5s.
        Assert.True(
            ready.Wait(TimeSpan.FromSeconds(30)),
            "Concurrent save workers did not become ready within 30 seconds.");
        release.Set();
        return await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static RunReplay CreateReplay(ulong seed)
    {
        IReadOnlyList<Direction>[] commands = [[Direction.Up], [Direction.Left]];
        return RunReplay.Capture(
            SnakeRun.Create(seed),
            commands,
            checkpointInterval: 1);
    }

    private static RunReplay CreateDivergentReplay()
    {
        var valid = CreateReplay(709UL);
        var finalCheckpoint = valid.Checkpoints[^1];
        var alternateHash = (finalCheckpoint.StateHash[0] == '0' ? "1" : "0")
            + finalCheckpoint.StateHash[1..];
        return RunReplay.CreateForTesting(
            valid.InitialCanonicalState,
            valid.Steps,
            valid.CheckpointInterval,
            [
                .. valid.Checkpoints.Take(valid.Checkpoints.Count - 1),
                new ReplayCheckpoint(finalCheckpoint.StepIndex, alternateHash),
            ],
            valid.Outcome);
    }

    private static void WriteGeneratedReplay(
        ReplayStore store,
        int index,
        string payload)
    {
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(index)
            .ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                System.Globalization.CultureInfo.InvariantCulture);
        var replayId = index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(
            Path.Combine(
                store.ReplayDirectory,
                $"{timestamp}_{replayId}{ReplayStore.ReplayFileExtension}"),
            payload,
            new UTF8Encoding(false));
    }

    private static string ReplaceOnce(
        string value,
        string current,
        string replacement)
    {
        var index = value.IndexOf(current, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Expected replay fragment was not found: {current}");
        return string.Concat(
            value.AsSpan(0, index),
            replacement,
            value.AsSpan(index + current.Length));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _ownedRoot;

        public TemporaryDirectory(string suffix)
        {
            _ownedRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "vibesnake-tests",
                $"{Guid.NewGuid():N}-{suffix}");
            Directory.CreateDirectory(_ownedRoot);
            Path = _ownedRoot;
        }

        public string Path { get; }

        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(_ownedRoot);
            var safeParent = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vibesnake-tests"))
                .TrimEnd(
                    System.IO.Path.DirectorySeparatorChar,
                    System.IO.Path.AltDirectorySeparatorChar)
                + System.IO.Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing to delete an unowned test path: {resolved}");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
