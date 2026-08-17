using System.Globalization;
using System.Text;
using System.Text.Json;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// AA-06's optional archive. A match is ephemeral until someone explicitly asks
/// for it to be kept, and what gets kept must still be true after the host that
/// played it has exited.
/// </summary>
[Collection(AgentHostIntegrationGroup.Name)]
public sealed class AgentExhibitionArchiveTests
{
    [Fact]
    public void Archiving_keeps_a_verified_exhibition_and_publishes_its_index()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_keep", seed: 41UL);

        var status = exhibition.Registry.ArchiveExhibition(exhibition.Handle);

        Assert.Equal(AgentExhibitionArchiveStatusV1.Contract, status.Schema);
        Assert.True(status.Archived);
        Assert.Equal(AgentExhibitionArchiveCode.Archived, status.Code);
        Assert.Equal(exhibition.Receipt.ReceiptHash, status.ReceiptHash);
        Assert.Equal(exhibition.Receipt.RouteIdentityHash, status.RouteIdentityHash);
        Assert.Equal(1, status.EntryCount);
        Assert.Equal(AgentExhibitionArchiveV1.MaximumEntries, status.Capacity);
        Assert.Equal(0, status.EvictedCount);
        Assert.False(status.RecoveredFromCorruption);

        var listed = Assert.Single(status.Entries);
        Assert.Equal(AgentArchivedExhibitionIndexEntryV1.Contract, listed.Schema);
        Assert.Equal(exhibition.Receipt.ReceiptHash, listed.ReceiptHash);
        Assert.Equal(exhibition.Receipt.RouteIdentityHash, listed.RouteIdentityHash);
        Assert.Equal(exhibition.Receipt.Division.DivisionId, listed.DivisionId);
        Assert.Equal(exhibition.Receipt.GameplaySeed, listed.GameplaySeed);
        Assert.Equal(exhibition.Receipt.Score, listed.Score);
        Assert.Equal(exhibition.SavedFileName, listed.AgentReplayFileName);
        Assert.Null(listed.RivalReplayFileName);

        // The named replay must exist, because naming a file that is not there
        // is the failure this archive is designed to make impossible.
        Assert.True(File.Exists(Path.Combine(
            temporary.Path,
            ReplayStore.ReplayDirectoryName,
            listed.AgentReplayFileName)));
    }

    [Fact]
    public void An_archived_exhibition_outlives_the_host_process_that_played_it()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_durable", seed: 42UL);
        exhibition.Registry.ArchiveExhibition(exhibition.Handle);
        exhibition.Registry.Dispose();

        // A separate store over the same user-data root stands in for a later
        // host process: no shared memory, no live handle, only the file.
        var reopened = new AgentExhibitionArchiveStore(temporary.Path).Read();

        Assert.Equal(AgentExhibitionArchiveV1.Contract, reopened.Schema);
        Assert.Equal(AgentExhibitionArchiveV1.CurrentSchemaVersion, reopened.SchemaVersion);
        var entry = Assert.Single(reopened.Entries);
        Assert.True(entry.IsSelfConsistent());
        Assert.Equal(exhibition.Receipt.ReceiptHash, entry.Receipt.ReceiptHash);
        Assert.Equal(
            exhibition.Receipt.RouteIdentityHash,
            entry.Receipt.RouteIdentityHash);
        Assert.True(AgentExhibitionReceipt.HasCanonicalHash(entry.Receipt));
    }

    [Fact]
    public void Archiving_the_same_exhibition_again_writes_nothing()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_repeat", seed: 43UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);

        var first = exhibition.Registry.ArchiveExhibition(exhibition.Handle);
        var firstBytes = File.ReadAllBytes(store.ArchivePath);
        var second = exhibition.Registry.ArchiveExhibition(exhibition.Handle);
        var secondBytes = File.ReadAllBytes(store.ArchivePath);

        Assert.True(first.Archived);
        Assert.False(second.Archived);
        Assert.Equal(AgentExhibitionArchiveCode.AlreadyArchived, second.Code);
        Assert.Equal(1, second.EntryCount);
        Assert.Equal(firstBytes, secondBytes);
    }

    [Fact]
    public void An_exhibition_cannot_be_archived_before_its_replay_is_saved()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var registry = CreateRegistry(temporary.Path, "match_unsaved", seed: 44UL);
        var handle = PlayToFinish(registry, "match_unsaved");

        var status = registry.ArchiveExhibition(handle);

        Assert.False(status.Archived);
        Assert.Equal(AgentExhibitionArchiveCode.ReplayNotSaved, status.Code);
        Assert.Equal(0, status.EntryCount);
        Assert.Empty(status.Entries);
        // The receipt exists; only the durable lane file does not.
        Assert.NotNull(status.ReceiptHash);
        Assert.False(File.Exists(
            new AgentExhibitionArchiveStore(temporary.Path).ArchivePath));
    }

    [Fact]
    public void A_live_match_has_no_exhibition_to_archive()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        using var registry = CreateRegistry(temporary.Path, "match_live", seed: 45UL);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            "45",
            maximumSteps: 8);

        var status = registry.ArchiveExhibition(started.MatchHandle);

        Assert.False(status.Archived);
        Assert.Equal(AgentExhibitionArchiveCode.NoVerifiedReceipt, status.Code);
        Assert.Null(status.ReceiptHash);
        Assert.Equal(0, status.EntryCount);
    }

    [Fact]
    public void A_different_exhibition_never_overwrites_an_existing_receipt_hash()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_conflict", seed: 46UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);

        var first = store.Archive(exhibition.Receipt, "lane-one.json", null);
        var conflicting = store.Archive(exhibition.Receipt, "lane-two.json", null);

        Assert.True(first.Archived);
        Assert.False(conflicting.Archived);
        Assert.Equal(AgentExhibitionArchiveCode.ConflictingReceipt, conflicting.Code);
        var kept = Assert.Single(store.Read().Entries);
        Assert.Equal("lane-one.json", kept.AgentReplayFileName);
    }

    [Fact]
    public void A_corrupt_archive_is_quarantined_rather_than_repaired()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_corrupt", seed: 47UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.ArchivePath)!);
        var damaged = Encoding.UTF8.GetBytes("{\"schema\":\"not-an-archive\"");
        File.WriteAllBytes(store.ArchivePath, damaged);

        var status = exhibition.Registry.ArchiveExhibition(exhibition.Handle);

        Assert.True(status.Archived);
        Assert.True(status.RecoveredFromCorruption);
        Assert.Equal(1, status.EntryCount);
        var quarantined = Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(store.ArchivePath)!,
            "*" + AgentExhibitionArchiveStore.CorruptFileExtension));
        // The unreadable bytes are preserved exactly, not rewritten.
        Assert.Equal(damaged, File.ReadAllBytes(quarantined));
    }

    [Fact]
    public void A_tampered_entry_is_treated_as_corruption_rather_than_as_data()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_tamper", seed: 48UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        store.Archive(exhibition.Receipt, exhibition.SavedFileName, null);

        // Raise the archived score without touching either canonical hash. A
        // reader that trusted the promoted field would report a better run than
        // the receipt describes.
        var document = JsonSerializer.Deserialize<JsonElement>(
            File.ReadAllText(store.ArchivePath));
        var mutated = document.GetRawText().Replace(
            $"\"score\": {exhibition.Receipt.Score.ToString(CultureInfo.InvariantCulture)},",
            "\"score\": 99999,",
            StringComparison.Ordinal);
        Assert.NotEqual(document.GetRawText(), mutated);
        File.WriteAllText(store.ArchivePath, mutated);

        Assert.Empty(store.Read().Entries);
    }

    [Fact]
    public void A_full_quarantine_blocks_the_write_instead_of_discarding_evidence()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_blocked", seed: 51UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        var directory = Path.GetDirectoryName(store.ArchivePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(store.ArchivePath, "{");
        for (var slot = 0; slot < AgentExhibitionArchiveStore.MaximumQuarantineSlots; slot++)
        {
            var occupied = slot == 0
                ? store.ArchivePath + AgentExhibitionArchiveStore.CorruptFileExtension
                : $"{store.ArchivePath}.{slot}{AgentExhibitionArchiveStore.CorruptFileExtension}";
            File.WriteAllText(occupied, "{");
        }

        Assert.True(store.IsBlocked());
        var status = exhibition.Registry.ArchiveExhibition(exhibition.Handle);

        Assert.False(status.Archived);
        Assert.Equal(AgentExhibitionArchiveCode.ArchiveUnavailable, status.Code);
        Assert.False(status.RecoveredFromCorruption);
        // The unreadable document is still exactly where it was.
        Assert.Equal("{", File.ReadAllText(store.ArchivePath));
        Assert.Equal(
            AgentExhibitionArchiveStore.MaximumQuarantineSlots,
            Directory.GetFiles(
                directory,
                "*" + AgentExhibitionArchiveStore.CorruptFileExtension).Length);
    }

    [Fact]
    public void The_archive_evicts_the_oldest_exhibition_at_capacity()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        var ordered = new List<string>();
        for (var index = 0; index <= AgentExhibitionArchiveV1.MaximumEntries; index++)
        {
            var exhibition = PlayAndSave(
                temporary.Path,
                $"match_capacity{index}",
                seed: 1000UL + (ulong)index);
            var status = exhibition.Registry.ArchiveExhibition(exhibition.Handle);
            exhibition.Registry.Dispose();
            ordered.Add(exhibition.Receipt.ReceiptHash);
            Assert.True(status.Archived, status.Message);
            Assert.Equal(
                index < AgentExhibitionArchiveV1.MaximumEntries ? 0 : 1,
                status.EvictedCount);
        }

        var entries = store.Read().Entries;
        Assert.Equal(AgentExhibitionArchiveV1.MaximumEntries, entries.Count);
        // Oldest first out, insertion order preserved for everything kept.
        Assert.Equal(
            ordered.Skip(1).ToArray(),
            entries.Select(entry => entry.ReceiptHash).ToArray());
    }

    [Fact]
    public void A_receipted_rival_lane_must_be_archived_with_its_saved_replay()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(
            temporary.Path,
            "match_rival",
            seed: 49UL,
            rivalPersonalityId: "optimal");

        Assert.NotNull(exhibition.Receipt.RivalReplayPayloadHash);
        Assert.NotNull(exhibition.SavedRivalFileName);
        Assert.Throws<ArgumentException>(() => AgentArchivedExhibitionV1.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: null));

        var status = exhibition.Registry.ArchiveExhibition(exhibition.Handle);

        Assert.True(status.Archived, status.Message);
        var listed = Assert.Single(status.Entries);
        Assert.Equal(exhibition.SavedRivalFileName, listed.RivalReplayFileName);
        Assert.Equal(exhibition.Receipt.RivalPersonalityId, listed.RivalPersonalityId);
        Assert.Equal(exhibition.Receipt.RivalScore, listed.RivalScore);
        Assert.True(File.Exists(Path.Combine(
            temporary.Path,
            ReplayStore.ReplayDirectoryName,
            listed.RivalReplayFileName!)));
    }

    [Fact]
    public void Archiving_strips_presentation_display_time_from_the_stored_receipt()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_display", seed: 50UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        var shown = exhibition.Receipt.WithDisplayTime("2026-08-17T20:00:00Z");

        var status = store.Archive(shown, exhibition.SavedFileName, null);

        Assert.True(status.Archived);
        var entry = Assert.Single(store.Read().Entries);
        Assert.Null(entry.Receipt.DisplayTimeUtc);
        Assert.Equal(exhibition.Receipt.ReceiptHash, entry.ReceiptHash);
        // Display time was never part of identity, so a shown receipt and an
        // unshown one are the same exhibition and archive only once.
        Assert.Equal(
            AgentExhibitionArchiveCode.AlreadyArchived,
            store.Archive(exhibition.Receipt, exhibition.SavedFileName, null).Code);
    }

    [Fact]
    public void The_archive_store_refuses_a_root_it_did_not_receive()
    {
        Assert.Throws<ArgumentException>(() => new AgentExhibitionArchiveStore("relative/root"));
        Assert.Throws<ArgumentException>(() => new AgentExhibitionArchiveStore("   "));
        Assert.Throws<ArgumentNullException>(() => new AgentExhibitionArchiveStore(null!));
    }

    [Fact]
    public void An_entry_stops_describing_itself_when_any_promoted_field_drifts()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_drift", seed: 52UL);
        var entry = AgentArchivedExhibitionV1.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: null);
        Assert.True(entry.IsSelfConsistent());

        // Every promoted field is a copy of a receipt value. A reader that
        // trusted a copy without checking it would report something the receipt
        // never said, so each one has to break self-consistency on its own.
        var drifted = new (string Field, AgentArchivedExhibitionV1 Entry)[]
        {
            ("schema", entry with { Schema = "vibesnake-agent-archived-exhibition-v2" }),
            ("receipt_hash", entry with { ReceiptHash = new string('a', 64) }),
            ("route_identity_hash", entry with { RouteIdentityHash = new string('b', 64) }),
            ("division_id", entry with { DivisionId = "classic@1|open|x|y" }),
            ("gameplay_seed", entry with { GameplaySeed = "999" }),
            ("score", entry with { Score = entry.Score + 1 }),
            ("agent_replay_file_name", entry with { AgentReplayFileName = "   " }),
            ("rival_replay_file_name", entry with { RivalReplayFileName = "unexpected.json" }),
            ("rival_personality_id", entry with { RivalPersonalityId = "optimal" }),
            ("rival_score", entry with { RivalScore = 7 }),
            ("display_time", entry with
            {
                Receipt = entry.Receipt.WithDisplayTime("2026-08-17T20:00:00Z"),
            }),
            ("receipt_hash_inside", entry with
            {
                Receipt = entry.Receipt with { Score = entry.Receipt.Score + 1 },
            }),
        };
        foreach (var (field, candidate) in drifted)
        {
            Assert.False(candidate.IsSelfConsistent(), field);
        }
    }

    [Fact]
    public void A_document_that_is_not_well_formed_is_never_loaded_as_data()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_wellformed", seed: 53UL);
        var entry = AgentArchivedExhibitionV1.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: null);
        var archive = AgentExhibitionArchiveV1.Empty with
        {
            Entries = new[] { entry },
        };
        Assert.True(archive.IsWellFormed());

        Assert.False((archive with { Schema = "other" }).IsWellFormed());
        Assert.False((archive with { SchemaVersion = 2 }).IsWellFormed());
        Assert.False((archive with { Capacity = 8 }).IsWellFormed());
        Assert.False((archive with
        {
            Entries = new[] { entry, entry },
        }).IsWellFormed());
        Assert.False((archive with
        {
            Entries = new[] { entry with { Score = entry.Score + 1 } },
        }).IsWellFormed());
        Assert.False((archive with
        {
            Entries = Enumerable
                .Range(0, AgentExhibitionArchiveV1.MaximumEntries + 1)
                .Select(index => entry with
                {
                    ReceiptHash = index.ToString("x64", CultureInfo.InvariantCulture),
                })
                .ToArray(),
        }).IsWellFormed());
    }

    [Fact]
    public void An_entry_refuses_a_lane_name_it_cannot_stand_behind()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_lane", seed: 54UL);

        Assert.Throws<ArgumentNullException>(() => AgentArchivedExhibitionV1.Create(
            null!,
            exhibition.SavedFileName,
            null));
        Assert.Throws<ArgumentException>(() => AgentArchivedExhibitionV1.Create(
            exhibition.Receipt,
            "  ",
            null));
        Assert.Throws<ArgumentException>(() => AgentArchivedExhibitionV1.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: "   "));
        // No rival in this receipt, so naming a rival lane is a contradiction.
        Assert.Throws<ArgumentException>(() => AgentArchivedExhibitionV1.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: "rival.json"));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentExhibitionArchiveStore(temporary.Path).Archive(
                null!,
                exhibition.SavedFileName,
                null));
        Assert.Throws<ArgumentNullException>(() =>
            AgentArchivedExhibitionV1
                .Create(exhibition.Receipt, exhibition.SavedFileName, null)
                .DescribesSameExhibitionAs(null!));
    }

    [Fact]
    public void One_oversized_exhibition_is_refused_rather_than_written()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_oversize", seed: 55UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        store.Archive(exhibition.Receipt, exhibition.SavedFileName, null);

        // A receipt carries one accepted presentation event per accepted step,
        // so a pathological exhibition can exceed the byte ceiling on its own.
        // It must be refused without disturbing what is already archived.
        var swollen = exhibition.Receipt with
        {
            ReceiptHash = new string('c', 64),
            AcceptedPresentationEvents = Enumerable
                .Range(0, 90_000)
                .Select(index => new AgentAcceptedPresentationEventV1(
                    index,
                    index,
                    AgentAction.Continue,
                    AgentPublicIntent.Undeclared))
                .ToArray(),
        };

        var status = store.Archive(swollen, exhibition.SavedFileName, null);

        Assert.False(status.Archived);
        Assert.Equal(AgentExhibitionArchiveCode.ArchiveUnavailable, status.Code);
        var kept = Assert.Single(store.Read().Entries);
        Assert.Equal(exhibition.Receipt.ReceiptHash, kept.ReceiptHash);
    }

    private static AgentSessionRegistry CreateRegistry(
        string root,
        string handle,
        ulong seed) =>
        new(
            new ReplayStore(root),
            () => handle,
            () => seed,
            archiveStore: new AgentExhibitionArchiveStore(root));

    private static string PlayToFinish(AgentSessionRegistry registry, string handle)
    {
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            maximumSteps: 1);
        Assert.Equal(handle, started.MatchHandle);
        var moved = registry.PlayMove(
            started.MatchHandle,
            handle + "-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Continue);
        Assert.True(moved.Accepted);
        return started.MatchHandle;
    }

    private static ArchivedExhibitionFixture PlayAndSave(
        string root,
        string handle,
        ulong seed,
        string? rivalPersonalityId = null)
    {
        var registry = CreateRegistry(root, handle, seed);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            maximumSteps: 1,
            rivalPersonalityId: rivalPersonalityId);
        var moved = registry.PlayMove(
            started.MatchHandle,
            handle + "-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Continue);
        Assert.True(moved.Accepted);
        var saved = registry.SaveVerifiedReplay(started.MatchHandle);
        Assert.True(saved.IsSuccess, saved.Message);
        var receiptStatus = registry.GetExhibitionReceipt(started.MatchHandle);
        Assert.True(receiptStatus.IsAvailable);
        return new ArchivedExhibitionFixture(
            registry,
            started.MatchHandle,
            Assert.IsType<AgentExhibitionReceiptV2>(receiptStatus.Receipt),
            Assert.IsType<string>(saved.FileName),
            saved.RivalFileName);
    }

    private sealed record ArchivedExhibitionFixture(
        AgentSessionRegistry Registry,
        string Handle,
        AgentExhibitionReceiptV2 Receipt,
        string SavedFileName,
        string? SavedRivalFileName);

    private sealed class ArchiveTemporaryDirectory : IDisposable
    {
        public ArchiveTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentArchiveTests",
                Guid.NewGuid().ToString("N"));
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
