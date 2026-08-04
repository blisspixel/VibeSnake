using System.Text;
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
                    new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero)));
            var secondStore = new ReplayStore(
                idempotentDirectory.Path,
                new FixedTimeProvider(
                    new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero)));

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
                new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero)));
        var capacityStoreB = new ReplayStore(
            capacityDirectory.Path,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 1, 4, 0, 0, TimeSpan.Zero)));
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
    }

    [Fact]
    public void Load_reports_missing_invalid_name_oversize_and_invalid_utf8_precisely()
    {
        using var temporary = new TemporaryDirectory("bounded imports");
        var store = new ReplayStore(temporary.Path);

        Assert.Equal(ReplayLoadCode.NotFound, store.LoadLatest().Code);
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
