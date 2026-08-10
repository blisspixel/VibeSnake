using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class OfflineChallengeTests
{
    [Fact]
    public void Seed_code_is_stable_tamper_evident_and_recreates_exact_rules()
    {
        var replay = CreateReplay(80_011UL);
        var challenge = SeedChallengeDescriptor.Create(replay);

        var first = challenge.Encode();
        var second = SeedChallengeDescriptor.Create(replay).Encode();
        var read = SeedChallengeDescriptor.Read(first);

        Assert.Equal(first, second);
        Assert.StartsWith("VS1.", first, StringComparison.Ordinal);
        Assert.True(read.IsValid, read.Message);
        Assert.Equal(challenge, read.Challenge);
        Assert.Equal(RulesetIdentity.Current.ContractId, $"{challenge.RulesetId}@{challenge.RulesVersion}");
        Assert.Equal(SeedChallengeDescriptor.CurrentContentContractId, challenge.ContentContractId);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, challenge.ConfigHashAlgorithm);
        Assert.Equal(replay.GameplaySeed, challenge.GameplaySeed);
        Assert.Equal(SeedChallengeDescriptor.AllOptions, challenge.AllowedOptions);

        var recreated = challenge.CreateRun(OfflineChallengeOptions.SameSeedRun);
        Assert.Equal(replay.InitialCanonicalState, recreated.SerializeCanonicalState());
        Assert.Equal(challenge.ConfigHash, recreated.ConfigHash);

        var changed = first[..^1] + (first[^1] == '0' ? '1' : '0');
        Assert.Equal(
            SeedCodeReadCode.IntegrityMismatch,
            SeedChallengeDescriptor.Read(changed).Code);
        Assert.Equal(SeedCodeReadCode.InvalidFormat, SeedChallengeDescriptor.Read(null).Code);
        Assert.Equal(SeedCodeReadCode.InvalidFormat, SeedChallengeDescriptor.Read("VS1.bad").Code);
        Assert.Throws<ArgumentException>(() => challenge.CreateRun(OfflineChallengeOptions.None));
        Assert.Throws<ArgumentException>(() => challenge.CreateRun(
            OfflineChallengeOptions.GhostRace | OfflineChallengeOptions.HouseholdRival));

        var noGhost = challenge with
        {
            AllowedOptions = OfflineChallengeOptions.SameSeedRun,
        };
        Assert.Throws<ArgumentException>(() => new GhostRaceSession(noGhost, replay));
    }

    [Fact]
    public void Seed_codes_fail_closed_for_malformed_payloads_and_identity_drift()
    {
        var replay = CreateReplay(808_011UL);
        var challenge = SeedChallengeDescriptor.Create(replay);

        static void AssertRejected(SeedChallengeDescriptor value) =>
            Assert.Throws<ArgumentException>(value.Validate);

        AssertRejected(challenge with { SchemaVersion = 2 });
        AssertRejected(challenge with { Kind = "future-seed-code" });
        AssertRejected(challenge with { RulesetId = "future-rules" });
        AssertRejected(challenge with { RulesVersion = RulesetIdentity.CurrentVersion + 1 });
        AssertRejected(challenge with { ContentContractId = "future-content@1" });
        AssertRejected(challenge with { ModeId = "future-mode" });
        AssertRejected(challenge with { ModeVersion = challenge.ModeVersion + 1 });
        AssertRejected(challenge with { ConfigHashAlgorithm = "future-config-hash" });
        AssertRejected(challenge with { ConfigHash = "bad" });
        AssertRejected(challenge with { ConfigHash = null! });
        AssertRejected(challenge with { AdaptivePolicyId = "future-policy" });
        AssertRejected(challenge with { AllowedOptions = OfflineChallengeOptions.None });
        AssertRejected(challenge with { AllowedOptions = OfflineChallengeOptions.GhostRace });
        AssertRejected(challenge with
        {
            AllowedOptions = SeedChallengeDescriptor.AllOptions | (OfflineChallengeOptions)128,
        });

        var invalidReplayJson = replay.Serialize().Replace(
            replay.PayloadHash,
            new string('0', 64),
            StringComparison.Ordinal);
        var invalidReplay = RunReplay.Read(invalidReplayJson).Replay!;
        Assert.Throws<ArgumentNullException>(() => SeedChallengeDescriptor.Create(invalidReplay));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SeedChallengeDescriptor.Create(replay, OfflineChallengeOptions.GhostRace));

        Assert.False(SeedChallengeDescriptor.Read(" ").IsValid);
        Assert.False(SeedChallengeDescriptor.Read(new string('a', 1_025)).IsValid);
        Assert.False(SeedChallengeDescriptor.Read("VS1.é.0000000000000000").IsValid);
        Assert.Equal(
            SeedCodeReadCode.InvalidFormat,
            SeedChallengeDescriptor.Read("NOPE.e30.0000000000000000").Code);
        Assert.Equal(
            SeedCodeReadCode.InvalidFormat,
            SeedChallengeDescriptor.Read("VS1.e30.A000000000000000").Code);
        Assert.Equal(
            SeedCodeReadCode.InvalidFormat,
            SeedChallengeDescriptor.Read("VS1.%.0000000000000000").Code);
        Assert.Equal(SeedCodeReadCode.InvalidFormat, SeedChallengeDescriptor.Read(
            EncodeSeedPayload("[]")).Code);
        Assert.Equal(SeedCodeReadCode.InvalidFormat, SeedChallengeDescriptor.Read(
            EncodeSeedPayload("{}")).Code);
        Assert.Equal(SeedCodeReadCode.InvalidFormat, SeedChallengeDescriptor.Read(
            EncodeSeedPayload("{")).Code);
    }

    [Fact]
    public void Ghost_race_advances_equal_rules_without_entering_player_state()
    {
        var replay = CreateReplay(918_273UL, stepCount: 8);
        var challenge = SeedChallengeDescriptor.Create(replay);
        var race = new GhostRaceSession(challenge, replay);

        for (var step = 1; step <= replay.Steps.Count; step++)
        {
            Assert.True(race.TryAdvance(out var frame));
            Assert.NotNull(frame);
            Assert.Equal(step, frame.StepIndex);
            Assert.Equal(frame.Player.StateHash, frame.Ghost.StateHash);
            Assert.Equal(0, frame.ScoreDelta);
            Assert.Equal(0, frame.LengthDelta);
        }

        Assert.True(race.GhostComplete);
        var isolated = new GhostRaceSession(challenge, replay);
        Assert.True(isolated.QueuePlayerDirection(Direction.Up));
        Assert.True(isolated.TryAdvance(out var divergent));
        Assert.NotNull(divergent);
        Assert.NotEqual(divergent.Player.StateHash, divergent.Ghost.StateHash);

        var expectedGhost = new RunReplayPlayback(replay);
        Assert.True(expectedGhost.TryAdvance(out _));
        Assert.Equal(expectedGhost.CurrentSnapshot.StateHash, divergent.Ghost.StateHash);
        Assert.Equal(challenge.ConfigHash, isolated.PlayerRun.ConfigHash);
    }

    [Fact]
    public void Household_slots_import_verified_ghosts_and_preserve_every_source()
    {
        using var temporary = new TemporaryDirectory();
        var replay = CreateReplay(441_144UL);
        var source = Path.Combine(temporary.Path, "shared replay.vibesnake-replay.json");
        var sourceBytes = new UTF8Encoding(false).GetBytes(replay.Serialize());
        File.WriteAllBytes(source, sourceBytes);
        var sourceHash = Sha256(sourceBytes);
        var store = new OfflineChallengeStore(temporary.Path);

        var imported = store.ImportGhost(source, slot: 1);
        var listed = store.ListSlots();

        Assert.True(imported.IsSuccess, imported.Message);
        Assert.Equal(GhostImportCode.Imported, imported.Code);
        Assert.Equal(replay.PayloadHash, imported.ReplayId);
        Assert.True(SeedChallengeDescriptor.Read(imported.SeedCode).IsValid);
        Assert.Equal(sourceHash, Sha256(File.ReadAllBytes(source)));
        Assert.Equal(sourceBytes, File.ReadAllBytes(source));
        Assert.True(listed.IsSuccess, listed.Message);
        Assert.Equal(OfflineChallengeStore.MaximumHouseholdRivalSlots, listed.Slots.Count);
        var occupied = Assert.Single(listed.Slots, slot => slot.Slot == 1);
        Assert.True(occupied.IsPlayable);
        Assert.Equal(GhostSlotState.Verified, occupied.State);
        Assert.Equal("HOUSEHOLD RIVAL 1", occupied.DisplayName);
        Assert.Equal(replay.GameplaySeed, occupied.GameplaySeed);
        Assert.Equal(replay.Outcome.Score, occupied.Score);
        Assert.All(listed.Slots.Where(slot => slot.Slot != 1), slot =>
            Assert.Equal(GhostSlotState.Empty, slot.State));

        var duplicate = store.ImportGhost(source, slot: 1);
        Assert.Equal(GhostImportCode.SlotOccupied, duplicate.Code);
        Assert.Equal(sourceHash, Sha256(File.ReadAllBytes(source)));
        Assert.Equal(GhostImportCode.InvalidSlot, store.ImportGhost(source, 0).Code);
        Assert.Equal(
            GhostImportCode.InvalidSource,
            store.ImportGhost("relative-replay.json", 2).Code);
        Assert.Equal(
            GhostImportCode.SourceNotFound,
            store.ImportGhost(Path.Combine(temporary.Path, "missing.json"), 2).Code);
    }

    [Fact]
    public void Modified_and_incompatible_imports_are_rejected_without_source_changes()
    {
        using var temporary = new TemporaryDirectory();
        var replay = CreateReplay(55_066UL);
        var store = new OfflineChallengeStore(temporary.Path);
        var modifiedPath = Path.Combine(temporary.Path, "modified.json");
        var serialized = replay.Serialize();
        var changedHash = replay.PayloadHash[..^1]
            + (replay.PayloadHash[^1] == '0' ? '1' : '0');
        File.WriteAllText(
            modifiedPath,
            serialized.Replace(replay.PayloadHash, changedHash, StringComparison.Ordinal),
            new UTF8Encoding(false));
        var modifiedBefore = File.ReadAllBytes(modifiedPath);

        var modified = store.ImportGhost(modifiedPath, 1);

        Assert.Equal(GhostImportCode.Modified, modified.Code);
        Assert.Equal(modifiedBefore, File.ReadAllBytes(modifiedPath));
        Assert.False(File.Exists(Path.Combine(
            store.GhostDirectory,
            $"household-rival-1{OfflineChallengeStore.GhostFileExtension}")));

        var incompatiblePath = Path.Combine(temporary.Path, "future.json");
        var incompatibleBytes = new UTF8Encoding(false).GetBytes("{\"schemaVersion\":999}\n");
        File.WriteAllBytes(incompatiblePath, incompatibleBytes);

        var incompatible = store.ImportGhost(incompatiblePath, 2);

        Assert.Equal(GhostImportCode.Incompatible, incompatible.Code);
        Assert.Equal(incompatibleBytes, File.ReadAllBytes(incompatiblePath));
        Assert.All(store.ListSlots().Slots, slot => Assert.Equal(GhostSlotState.Empty, slot.State));
    }

    [Fact]
    public void Run_card_is_readable_private_atomic_and_exactly_versioned()
    {
        using var temporary = new TemporaryDirectory();
        var replay = CreateReplay(654_321UL, stepCount: 12);
        var source = Path.Combine(temporary.Path, "card-source.json");
        File.WriteAllText(source, replay.Serialize(), new UTF8Encoding(false));
        var store = new OfflineChallengeStore(temporary.Path);
        Assert.True(store.ImportGhost(source, 3).IsSuccess);

        var first = store.ExportRunCard(
            3,
            "0.2.1",
            "flow_signal",
            "classic-signal");
        var second = store.ExportRunCard(
            3,
            "0.2.1",
            "flow_signal",
            "classic-signal");

        Assert.Equal(RunCardExportCode.Exported, first.Code);
        Assert.Equal(RunCardExportCode.AlreadyExists, second.Code);
        Assert.True(first.IsSuccess);
        Assert.NotNull(first.Card);
        Assert.Equal(OfflineRunCard.CurrentSchemaVersion, first.Card.SchemaVersion);
        Assert.Equal(OfflineRunCard.KindId, first.Card.Kind);
        Assert.Equal(OfflineRunCard.VerifiedState, first.Card.VerificationState);
        Assert.Equal(replay.Outcome.Score, first.Card.Score);
        Assert.Equal(replay.GameplaySeed, first.Card.GameplaySeed);
        Assert.Equal("flow_signal", first.Card.StationId);
        Assert.Equal("classic-signal", first.Card.SelectedLookId);
        Assert.False(first.Card.ContainsPlayerIdentity);
        Assert.False(first.Card.ContainsPrivatePaths);
        Assert.All(first.Card.ToDisplayLines(), line => Assert.InRange(line.Length, 1, 100));
        Assert.True(SeedChallengeDescriptor.Read(first.Card.SeedCode).IsValid);

        var exportedPath = Path.Combine(store.RunCardDirectory, first.FileName!);
        Assert.True(File.Exists(exportedPath));
        Assert.Equal(first.Sha256, Sha256(File.ReadAllBytes(exportedPath)));
        Assert.Empty(Directory.GetFiles(store.RunCardDirectory, "*.tmp-*"));
        using var json = JsonDocument.Parse(File.ReadAllText(exportedPath));
        Assert.Equal(OfflineRunCard.FieldCount, json.RootElement.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("powerIds").ValueKind);
        Assert.Equal(RunCardExportCode.InvalidSlot, store.ExportRunCard(
            5,
            "0.2.1",
            "flow_signal",
            "classic-signal").Code);
    }

    [Fact]
    public void Ghost_deletion_requires_fresh_exact_consent_and_preserves_import_source()
    {
        using var temporary = new TemporaryDirectory();
        var replay = CreateReplay(77_088UL);
        var source = Path.Combine(temporary.Path, "delete-source.json");
        File.WriteAllText(source, replay.Serialize(), new UTF8Encoding(false));
        var sourceBytes = File.ReadAllBytes(source);
        var store = new OfflineChallengeStore(temporary.Path);
        Assert.True(store.ImportGhost(source, 4).IsSuccess);

        var stale = store.PlanDeletion(4);
        Assert.True(stale.IsSuccess);
        var slotPath = Path.Combine(
            store.GhostDirectory,
            $"household-rival-4{OfflineChallengeStore.GhostFileExtension}");
        File.AppendAllText(slotPath, " ", new UTF8Encoding(false));
        Assert.Equal(GhostDeleteCode.ChangedSinceConsent, store.Delete(stale.Plan!).Code);
        Assert.True(File.Exists(slotPath));

        var fresh = store.PlanDeletion(4);
        Assert.True(fresh.IsSuccess);
        var deleted = store.Delete(fresh.Plan!);
        Assert.True(deleted.IsSuccess, deleted.Message);
        Assert.False(File.Exists(slotPath));
        Assert.Equal(sourceBytes, File.ReadAllBytes(source));
        Assert.Equal(GhostDeletionPlanCode.Empty, store.PlanDeletion(4).Code);
        Assert.Equal(GhostDeleteCode.InvalidPlan, store.Delete(
            fresh.Plan! with { Slot = 0 }).Code);
    }

    [Fact]
    public void Store_bounds_and_nonplayable_slot_states_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => new OfflineChallengeStore("relative"));
        using var temporary = new TemporaryDirectory();
        Assert.Throws<ArgumentOutOfRangeException>(() => new OfflineChallengeStore(
            temporary.Path,
            TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OfflineChallengeStore(
            temporary.Path,
            TimeSpan.FromSeconds(31)));
        var store = new OfflineChallengeStore(temporary.Path);

        Assert.Equal(ReplayLoadCode.InvalidName, store.LoadGhost(0).Code);
        var invalidPlan = store.PlanDeletion(5);
        Assert.Equal(GhostDeletionPlanCode.InvalidSlot, invalidPlan.Code);
        Assert.False(invalidPlan.IsSuccess);
        var emptyPlan = store.PlanDeletion(1);
        Assert.Equal(GhostDeletionPlanCode.Empty, emptyPlan.Code);
        Assert.False(emptyPlan.IsSuccess);
        var invalidExport = store.ExportRunCard(
            0,
            "0.2.1",
            "flow_signal",
            "classic-signal");
        Assert.Equal(RunCardExportCode.InvalidSlot, invalidExport.Code);
        Assert.False(invalidExport.IsSuccess);
        Assert.Equal(GhostDeleteCode.InvalidPlan, store.Delete(new GhostDeletionPlan(
            1,
            -1,
            new string('0', 64),
            "invalid")).Code);
        Assert.Equal(GhostDeleteCode.InvalidPlan, store.Delete(new GhostDeletionPlan(
            1,
            0,
            "bad",
            "invalid")).Code);
        Assert.Equal(GhostDeleteCode.InvalidPlan, store.Delete(new GhostDeletionPlan(
            1,
            0,
            new string('A', 64),
            "invalid")).Code);
        var emptyDelete = store.Delete(new GhostDeletionPlan(
            1,
            0,
            new string('0', 64),
            "empty"));
        Assert.Equal(GhostDeleteCode.Empty, emptyDelete.Code);
        Assert.False(emptyDelete.IsSuccess);

        Directory.CreateDirectory(store.GhostDirectory);
        File.WriteAllBytes(SlotPath(store, 1), [0xff]);
        File.WriteAllText(
            SlotPath(store, 2),
            "{\"schemaVersion\":999}\n",
            new UTF8Encoding(false));
        var replay = CreateReplay(222_333UL);
        var changedHash = replay.PayloadHash[..^1]
            + (replay.PayloadHash[^1] == '0' ? '1' : '0');
        File.WriteAllText(
            SlotPath(store, 3),
            replay.Serialize().Replace(replay.PayloadHash, changedHash, StringComparison.Ordinal),
            new UTF8Encoding(false));

        var listed = store.ListSlots();

        Assert.True(listed.IsSuccess);
        Assert.Equal(GhostSlotState.Unreadable, listed.Slots[0].State);
        Assert.Equal(GhostSlotState.Incompatible, listed.Slots[1].State);
        Assert.Equal(GhostSlotState.Modified, listed.Slots[2].State);
        Assert.Equal(GhostSlotState.Empty, listed.Slots[3].State);

        var tooLarge = Path.Combine(temporary.Path, "too-large.json");
        using (var stream = new FileStream(tooLarge, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(RunReplay.MaximumSerializedCharacters + 1L);
        }

        Assert.Equal(GhostImportCode.SourceTooLarge, store.ImportGhost(tooLarge, 4).Code);
    }

    [Fact]
    public void Store_locking_metadata_validation_and_no_overwrite_paths_are_bounded()
    {
        using var temporary = new TemporaryDirectory();
        var replay = CreateReplay(333_444UL);
        var source = Path.Combine(temporary.Path, "source.json");
        File.WriteAllText(source, replay.Serialize(), new UTF8Encoding(false));
        var store = new OfflineChallengeStore(temporary.Path, TimeSpan.FromMilliseconds(20));
        Assert.True(store.ImportGhost(source, 1).IsSuccess);
        var deletion = store.PlanDeletion(1);
        Assert.True(deletion.IsSuccess);

        using (var heldLock = new FileStream(
            Path.Combine(store.ChallengeDirectory, OfflineChallengeStore.StoreLockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            Assert.Equal(GhostImportCode.Busy, store.ImportGhost(source, 2).Code);
            Assert.Equal(GhostDeleteCode.Busy, store.Delete(deletion.Plan!).Code);
            Assert.Equal(RunCardExportCode.Busy, store.ExportRunCard(
                1,
                "0.2.1",
                "flow_signal",
                "classic-signal").Code);
        }

        Assert.Equal(RunCardExportCode.IoFailure, store.ExportRunCard(
            1,
            "bad version",
            "flow_signal",
            "classic-signal").Code);
        Assert.Equal(RunCardExportCode.IoFailure, store.ExportRunCard(
            1,
            "0.2.1",
            "missing-station",
            "classic-signal").Code);
        Assert.Equal(RunCardExportCode.IoFailure, store.ExportRunCard(
            1,
            "0.2.1",
            "flow_signal",
            "missing-look").Code);

        var challenge = SeedChallengeDescriptor.Create(replay);
        var card = OfflineRunCard.Create(
            replay,
            challenge,
            "0.2.1",
            "flow_signal",
            "classic-signal");
        var cardBytes = Encoding.UTF8.GetBytes(card.Serialize());
        Directory.CreateDirectory(store.RunCardDirectory);
        var destination = Path.Combine(
            store.RunCardDirectory,
            $"run-card_{replay.PayloadHash}{OfflineChallengeStore.RunCardFileExtension}");
        File.WriteAllText(destination, "different", new UTF8Encoding(false));
        Assert.Equal(RunCardExportCode.IoFailure, store.ExportRunCard(
            1,
            "0.2.1",
            "flow_signal",
            "classic-signal").Code);
        File.WriteAllBytes(destination, Enumerable.Repeat((byte)'x', cardBytes.Length).ToArray());
        Assert.Equal(RunCardExportCode.IoFailure, store.ExportRunCard(
            1,
            "0.2.1",
            "flow_signal",
            "classic-signal").Code);
    }

    [Fact]
    public void Run_card_count_and_byte_capacities_preserve_existing_files()
    {
        using var countRoot = new TemporaryDirectory();
        var countStore = ImportOneGhost(countRoot.Path, 444_555UL);
        Directory.CreateDirectory(countStore.RunCardDirectory);
        for (var index = 0; index < OfflineChallengeStore.MaximumRunCards; index++)
        {
            File.WriteAllText(
                Path.Combine(
                    countStore.RunCardDirectory,
                    $"existing-{index:D2}{OfflineChallengeStore.RunCardFileExtension}"),
                string.Empty);
        }

        Assert.Equal(RunCardExportCode.CapacityReached, countStore.ExportRunCard(
            1,
            "0.2.1",
            "flow_signal",
            "classic-signal").Code);
        File.WriteAllText(
            Path.Combine(
                countStore.RunCardDirectory,
                $"overflow{OfflineChallengeStore.RunCardFileExtension}"),
            string.Empty);
        Assert.Equal(RunCardExportCode.CapacityReached, countStore.ExportRunCard(
            1,
            "0.2.1",
            "flow_signal",
            "classic-signal").Code);

        using var byteRoot = new TemporaryDirectory();
        var byteStore = ImportOneGhost(byteRoot.Path, 555_666UL);
        Directory.CreateDirectory(byteStore.RunCardDirectory);
        WriteSizedFile(
            Path.Combine(
                byteStore.RunCardDirectory,
                $"oversized{OfflineChallengeStore.RunCardFileExtension}"),
            OfflineChallengeStore.MaximumRunCardBytes + 1L);
        Assert.Equal(RunCardExportCode.CapacityReached, byteStore.ExportRunCard(
            1,
            "0.2.1",
            "flow_signal",
            "classic-signal").Code);

        using var remainderRoot = new TemporaryDirectory();
        var remainderStore = ImportOneGhost(remainderRoot.Path, 666_777UL);
        Directory.CreateDirectory(remainderStore.RunCardDirectory);
        WriteSizedFile(
            Path.Combine(
                remainderStore.RunCardDirectory,
                $"nearly-full{OfflineChallengeStore.RunCardFileExtension}"),
            OfflineChallengeStore.MaximumRunCardBytes - 1L);
        Assert.Equal(RunCardExportCode.CapacityReached, remainderStore.ExportRunCard(
            1,
            "0.2.1",
            "flow_signal",
            "classic-signal").Code);
    }

    [Fact]
    public void Run_card_creation_rejects_untrusted_metadata_and_oversized_projection()
    {
        var replay = CreateReplay(777_888UL);
        var challenge = SeedChallengeDescriptor.Create(replay);
        Assert.Throws<ArgumentException>(() => OfflineRunCard.Create(
            replay,
            challenge,
            "bad version",
            "flow_signal",
            "classic-signal"));
        Assert.Throws<ArgumentException>(() => OfflineRunCard.Create(
            replay,
            SeedChallengeDescriptor.Create(CreateReplay(888_999UL)),
            "0.2.1",
            "flow_signal",
            "classic-signal"));
        Assert.Throws<ArgumentException>(() => OfflineRunCard.Create(
            replay,
            challenge,
            "0.2.1",
            "missing-station",
            "classic-signal"));
        Assert.Throws<ArgumentException>(() => OfflineRunCard.Create(
            replay,
            challenge,
            "0.2.1",
            "flow_signal",
            "missing-look"));

        var card = OfflineRunCard.Create(
            replay,
            challenge,
            "0.2.1",
            "flow_signal",
            "classic-signal");
        var withPower = card with { PowerIds = ["shield"] };
        Assert.Contains("SHIELD", withPower.ToDisplayLines()[4], StringComparison.Ordinal);
        var oversized = card with
        {
            PowerIds = Enumerable.Repeat(new string('x', 100), 400).ToArray(),
        };
        Assert.Throws<InvalidOperationException>(oversized.Serialize);
    }

    private static string SlotPath(OfflineChallengeStore store, int slot) =>
        Path.Combine(
            store.GhostDirectory,
            $"household-rival-{slot}{OfflineChallengeStore.GhostFileExtension}");

    private static OfflineChallengeStore ImportOneGhost(string root, ulong seed)
    {
        var replay = CreateReplay(seed);
        var source = Path.Combine(root, $"source-{seed}.json");
        File.WriteAllText(source, replay.Serialize(), new UTF8Encoding(false));
        var store = new OfflineChallengeStore(root);
        var imported = store.ImportGhost(source, 1);
        Assert.True(imported.IsSuccess, imported.Message);
        return store;
    }

    private static void WriteSizedFile(string path, long size)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        stream.SetLength(size);
    }

    private static RunReplay CreateReplay(ulong seed, int stepCount = 6)
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, enableAdaptation: false);
        var run = SnakeRun.Create(seed, config);
        var commands = Enumerable.Range(0, stepCount)
            .Select(_ => (IReadOnlyList<Direction>)Array.Empty<Direction>())
            .ToArray();
        return RunReplay.Capture(
            run,
            commands,
            checkpointInterval: 2,
            appVersion: "0.2.1",
            capturedAtUtc: "2026-08-08T12:34:56.789Z");
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string EncodeSeedPayload(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var encoded = Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var integrity = Sha256(payload)[..SeedChallengeDescriptor.IntegrityHexCharacters];
        return $"{SeedChallengeDescriptor.CodePrefix}.{encoded}.{integrity}";
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vibesnake-offline-challenge-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
