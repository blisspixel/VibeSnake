using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        Assert.Equal(AgentExhibitionArchiveStatusV2.Contract, status.Schema);
        Assert.True(status.Archived);
        Assert.Equal(AgentExhibitionArchiveCode.Archived, status.Code);
        Assert.Equal(exhibition.Receipt.ReceiptHash, status.ReceiptHash);
        Assert.Equal(exhibition.Receipt.RouteIdentityHash, status.RouteIdentityHash);
        Assert.Equal(1, status.Archive.EntryCount);
        Assert.Equal(AgentExhibitionArchiveV2.MaximumEntries, status.Archive.Capacity);
        Assert.Empty(status.Evicted);
        Assert.False(status.Archive.RecoveredFromCorruption);

        var listed = Assert.Single(status.Archive.Entries);
        Assert.Equal(AgentArchivedExhibitionIndexEntryV3.Contract, listed.Schema);
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

        Assert.Equal(AgentExhibitionArchiveV2.Contract, reopened.Schema);
        Assert.Equal(AgentExhibitionArchiveV2.CurrentSchemaVersion, reopened.SchemaVersion);
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
        Assert.Equal(1, second.Archive.EntryCount);
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
        Assert.Equal(0, status.Archive.EntryCount);
        Assert.Empty(status.Archive.Entries);
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
        Assert.Equal(0, status.Archive.EntryCount);
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
        Assert.True(status.Archive.RecoveredFromCorruption);
        Assert.Equal(1, status.Archive.EntryCount);
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
        Assert.False(status.Archive.RecoveredFromCorruption);
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
        for (var index = 0; index <= AgentExhibitionArchiveV2.MaximumEntries; index++)
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
                index < AgentExhibitionArchiveV2.MaximumEntries ? 0 : 1,
                status.Evicted.Count);
        }

        var entries = store.Read().Entries;
        Assert.Equal(AgentExhibitionArchiveV2.MaximumEntries, entries.Count);
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
        Assert.Throws<ArgumentException>(() => AgentArchivedExhibitionV2.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: null));

        var status = exhibition.Registry.ArchiveExhibition(exhibition.Handle);

        Assert.True(status.Archived, status.Message);
        var listed = Assert.Single(status.Archive.Entries);
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
        var entry = AgentArchivedExhibitionV2.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: null);
        Assert.True(entry.IsSelfConsistent());

        // Every promoted field is a copy of a receipt value. A reader that
        // trusted a copy without checking it would report something the receipt
        // never said, so each one has to break self-consistency on its own.
        var drifted = new (string Field, AgentArchivedExhibitionV2 Entry)[]
        {
            ("schema", entry with { Schema = "vibesnake-agent-archived-exhibition-v3" }),
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
        var entry = AgentArchivedExhibitionV2.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: null);
        var archive = AgentExhibitionArchiveV2.Empty with
        {
            Entries = new[] { entry },
        };
        Assert.True(archive.IsWellFormed());

        Assert.False((archive with { Schema = "other" }).IsWellFormed());
        Assert.False((archive with { SchemaVersion = 3 }).IsWellFormed());
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
                .Range(0, AgentExhibitionArchiveV2.MaximumEntries + 1)
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

        Assert.Throws<ArgumentNullException>(() => AgentArchivedExhibitionV2.Create(
            null!,
            exhibition.SavedFileName,
            null));
        Assert.Throws<ArgumentException>(() => AgentArchivedExhibitionV2.Create(
            exhibition.Receipt,
            "  ",
            null));
        Assert.Throws<ArgumentException>(() => AgentArchivedExhibitionV2.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: "   "));
        // No rival in this receipt, so naming a rival lane is a contradiction.
        Assert.Throws<ArgumentException>(() => AgentArchivedExhibitionV2.Create(
            exhibition.Receipt,
            exhibition.SavedFileName,
            rivalReplayFileName: "rival.json"));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentExhibitionArchiveStore(temporary.Path).Archive(
                null!,
                exhibition.SavedFileName,
                null));
        Assert.Throws<ArgumentNullException>(() =>
            AgentArchivedExhibitionV2
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

    [Fact]
    public void A_schema_one_archive_is_migrated_forward_rather_than_quarantined()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_migrate", seed: 60UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.ArchivePath)!);
        File.WriteAllText(store.ArchivePath, LegacyDocument(exhibition));

        var read = store.Inspect();

        Assert.True(read.MigratedFromLegacySchema);
        Assert.False(read.RecoveredFromCorruption);
        Assert.False(read.Blocked);
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(store.ArchivePath)!,
            "*" + AgentExhibitionArchiveStore.CorruptFileExtension));
        Assert.Equal(
            AgentExhibitionArchiveV2.CurrentSchemaVersion,
            read.Archive.SchemaVersion);

        // Every field the new schema promotes is rebuilt from the receipt the
        // old schema already stored, so nothing is invented and nothing is lost.
        var entry = Assert.Single(read.Archive.Entries);
        Assert.True(entry.IsSelfConsistent());
        Assert.Equal(exhibition.Receipt.ReceiptHash, entry.ReceiptHash);
        Assert.Equal(exhibition.Receipt.Division.ModeId, entry.ModeId);
        Assert.Equal(exhibition.Receipt.EndReason, entry.EndReason);
        Assert.Equal(exhibition.Receipt.RunStatus, entry.RunStatus);
        Assert.Equal(exhibition.SavedFileName, entry.AgentReplayFileName);
    }

    [Fact]
    public void A_legacy_entry_whose_receipt_does_not_verify_is_not_migrated()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_badmigrate", seed: 61UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.ArchivePath)!);

        // Raise the archived score without touching the canonical hash. A
        // migration that trusted the old document would carry the lie forward.
        var tampered = LegacyDocument(exhibition).Replace(
            "\"receipt_hash\": \"" + exhibition.Receipt.ReceiptHash + "\"",
            "\"receipt_hash\": \"" + new string('e', 64) + "\"",
            StringComparison.Ordinal);
        File.WriteAllText(store.ArchivePath, tampered);

        var read = store.Inspect();

        Assert.False(read.MigratedFromLegacySchema);
        Assert.True(read.RecoveredFromCorruption);
        Assert.Empty(read.Archive.Entries);
        Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(store.ArchivePath)!,
            "*" + AgentExhibitionArchiveStore.CorruptFileExtension));
    }

    [Fact]
    public void Listing_reads_the_archive_without_writing_and_can_narrow_to_one_line()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var first = PlayAndSave(temporary.Path, "match_list1", seed: 62UL);
        first.Registry.ArchiveExhibition(first.Handle);
        first.Registry.Dispose();
        var second = PlayAndSave(temporary.Path, "match_list2", seed: 63UL);
        second.Registry.ArchiveExhibition(second.Handle);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        var before = File.ReadAllBytes(store.ArchivePath);

        var all = second.Registry.ListExhibitions(null);
        var narrowed = second.Registry.ListExhibitions(first.Receipt.RouteIdentityHash);

        Assert.Equal(before, File.ReadAllBytes(store.ArchivePath));
        Assert.Equal(AgentExhibitionArchiveListingV1.Contract, all.Schema);
        Assert.Null(all.RouteIdentityHashFilter);
        Assert.Equal(2, all.MatchedCount);
        Assert.Equal(2, all.Archive.EntryCount);
        Assert.Equal(2, all.Archive.Entries.Count);

        Assert.Equal(first.Receipt.RouteIdentityHash, narrowed.RouteIdentityHashFilter);
        Assert.Equal(1, narrowed.MatchedCount);
        // Narrowing filters what is listed, never what is stored.
        Assert.Equal(2, narrowed.Archive.EntryCount);
        Assert.Equal(
            first.Receipt.ReceiptHash,
            Assert.Single(narrowed.Archive.Entries).ReceiptHash);
        Assert.Empty(second.Registry.ListExhibitions(new string('f', 64)).Archive.Entries);
    }

    [Fact]
    public void A_listing_reports_both_bounds_and_the_bytes_actually_used()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_bytes", seed: 64UL);
        var status = exhibition.Registry.ArchiveExhibition(exhibition.Handle);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        var onDisk = File.ReadAllBytes(store.ArchivePath).Length;

        Assert.Equal(onDisk, status.Archive.BytesUsed);
        Assert.Equal(AgentExhibitionArchiveV2.MaximumBytes, status.Archive.MaximumBytes);
        Assert.Equal(
            AgentExhibitionArchiveV2.MaximumEntries - 1,
            status.Archive.RemainingEntries);
        Assert.Equal(
            AgentExhibitionArchiveV2.MaximumBytes - onDisk,
            status.Archive.RemainingBytes);

        Assert.Equal(onDisk, exhibition.Registry.ListExhibitions(null).Archive.BytesUsed);
    }

    [Fact]
    public void A_listing_reports_whether_a_named_lane_file_is_still_present()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_orphan", seed: 65UL);
        exhibition.Registry.ArchiveExhibition(exhibition.Handle);

        Assert.True(Assert.Single(
            exhibition.Registry.ListExhibitions(null).Archive.Entries).AgentReplayPresent);

        // An entry names a file rather than embedding it, so deleting the file
        // leaves the entry pointing at nothing. A caller choosing what to open
        // has to be told, rather than discovering it on open.
        File.Delete(Path.Combine(
            temporary.Path,
            ReplayStore.ReplayDirectoryName,
            exhibition.SavedFileName));

        var listed = Assert.Single(
            exhibition.Registry.ListExhibitions(null).Archive.Entries);
        Assert.False(listed.AgentReplayPresent);
        Assert.Null(listed.RivalReplayPresent);
    }

    [Fact]
    public void Forgetting_removes_one_exhibition_and_names_what_it_took()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var first = PlayAndSave(temporary.Path, "match_forget1", seed: 66UL);
        first.Registry.ArchiveExhibition(first.Handle);
        first.Registry.Dispose();
        var second = PlayAndSave(temporary.Path, "match_forget2", seed: 67UL);
        second.Registry.ArchiveExhibition(second.Handle);

        var forgotten = second.Registry.ForgetExhibition(first.Receipt.ReceiptHash);

        Assert.Equal(AgentExhibitionForgetStatusV1.Contract, forgotten.Schema);
        Assert.True(forgotten.Forgotten);
        Assert.Equal(AgentExhibitionForgetCode.Forgotten, forgotten.Code);
        var removed = Assert.Single(forgotten.Removed);
        Assert.Equal(AgentExhibitionArchiveDropV1.Contract, removed.Schema);
        Assert.Equal(first.Receipt.ReceiptHash, removed.ReceiptHash);
        Assert.Equal(first.Receipt.RouteIdentityHash, removed.RouteIdentityHash);
        Assert.Equal(1, forgotten.Archive.EntryCount);
        Assert.Equal(
            second.Receipt.ReceiptHash,
            Assert.Single(forgotten.Archive.Entries).ReceiptHash);

        // Removal touches archive entries only.
        Assert.True(File.Exists(Path.Combine(
            temporary.Path,
            ReplayStore.ReplayDirectoryName,
            first.SavedFileName)));
    }

    [Fact]
    public void Forgetting_is_safe_to_repeat_and_can_clear_the_archive()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_clear", seed: 68UL);
        exhibition.Registry.ArchiveExhibition(exhibition.Handle);

        var missing = exhibition.Registry.ForgetExhibition(new string('a', 64));
        Assert.False(missing.Forgotten);
        Assert.Equal(AgentExhibitionForgetCode.NotArchived, missing.Code);
        Assert.Empty(missing.Removed);
        Assert.Equal(1, missing.Archive.EntryCount);

        var cleared = exhibition.Registry.ForgetExhibition(null);
        Assert.True(cleared.Forgotten);
        Assert.Single(cleared.Removed);
        Assert.Equal(0, cleared.Archive.EntryCount);

        var again = exhibition.Registry.ForgetExhibition(null);
        Assert.False(again.Forgotten);
        Assert.Equal(AgentExhibitionForgetCode.NotArchived, again.Code);
        Assert.Equal(0, again.Archive.EntryCount);

        // A cleared archive is an empty archive, not a missing one.
        Assert.Equal(
            AgentExhibitionArchiveV2.CurrentSchemaVersion,
            new AgentExhibitionArchiveStore(temporary.Path).Read().SchemaVersion);
    }

    [Fact]
    public void An_eviction_names_every_exhibition_it_dropped()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        var ordered = new List<string>();
        AgentExhibitionArchiveStatusV2? last = null;
        for (var index = 0; index <= AgentExhibitionArchiveV2.MaximumEntries; index++)
        {
            var exhibition = PlayAndSave(
                temporary.Path,
                $"match_named{index}",
                seed: 2000UL + (ulong)index);
            last = exhibition.Registry.ArchiveExhibition(exhibition.Handle);
            exhibition.Registry.Dispose();
            ordered.Add(exhibition.Receipt.ReceiptHash);
        }

        Assert.NotNull(last);
        var dropped = Assert.Single(last.Evicted);
        Assert.Equal(ordered[0], dropped.ReceiptHash);
        Assert.DoesNotContain(
            store.Read().Entries,
            entry => string.Equals(entry.ReceiptHash, ordered[0], StringComparison.Ordinal));
    }

    private static readonly JsonSerializerOptions LegacySerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
        },
    };

    /// <summary>
    /// The exact schema-1 document an earlier host would have written, built from
    /// a real receipt so migration is exercised against true bytes rather than a
    /// hand-written fixture that could drift from what shipped.
    /// </summary>
    private static string LegacyDocument(ArchivedExhibitionFixture exhibition)
    {
        var receipt = JsonSerializer.Serialize(exhibition.Receipt, LegacySerializerOptions);
        var builder = new StringBuilder();
        builder.Append("{\n");
        builder.Append("  \"schema\": \"")
            .Append(AgentExhibitionArchiveV2.LegacyContract).Append("\",\n");
        builder.Append("  \"schema_version\": ")
            .Append(AgentExhibitionArchiveV2.LegacySchemaVersion).Append(",\n");
        builder.Append("  \"capacity\": ")
            .Append(AgentExhibitionArchiveV2.MaximumEntries).Append(",\n");
        builder.Append("  \"entries\": [\n    {\n");
        builder.Append("      \"schema\": \"")
            .Append(AgentExhibitionArchiveV2.LegacyEntryContract).Append("\",\n");
        builder.Append("      \"receipt_hash\": \"")
            .Append(exhibition.Receipt.ReceiptHash).Append("\",\n");
        builder.Append("      \"route_identity_hash\": \"")
            .Append(exhibition.Receipt.RouteIdentityHash).Append("\",\n");
        builder.Append("      \"division_id\": \"")
            .Append(exhibition.Receipt.Division.DivisionId).Append("\",\n");
        builder.Append("      \"gameplay_seed\": \"")
            .Append(exhibition.Receipt.GameplaySeed).Append("\",\n");
        builder.Append("      \"score\": ")
            .Append(exhibition.Receipt.Score.ToString(CultureInfo.InvariantCulture))
            .Append(",\n");
        builder.Append("      \"agent_replay_file_name\": \"")
            .Append(exhibition.SavedFileName).Append("\",\n");
        builder.Append("      \"rival_replay_file_name\": null,\n");
        builder.Append("      \"rival_personality_id\": null,\n");
        builder.Append("      \"rival_score\": null,\n");
        builder.Append("      \"receipt\": ").Append(receipt).Append('\n');
        builder.Append("    }\n  ]\n}\n");
        return builder.ToString();
    }

    [Fact]
    public void A_migrate_on_read_reports_the_file_it_has_not_rewritten_yet()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var exhibition = PlayAndSave(temporary.Path, "match_pending", seed: 70UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.ArchivePath)!);
        File.WriteAllText(store.ArchivePath, LegacyDocument(exhibition));
        var legacyBytes = File.ReadAllBytes(store.ArchivePath).Length;

        var listed = exhibition.Registry.ListExhibitions(null);

        // A playtester compared bytes_used against the file after a migration and
        // found them disagreeing, because reading never writes. bytes_used now
        // describes the file that exists; bytes_projected describes the write
        // that has not happened yet, and the ceiling binds on the latter.
        Assert.True(listed.Archive.MigratedFromLegacySchema);
        Assert.Equal(legacyBytes, listed.Archive.BytesUsed);
        Assert.NotEqual(listed.Archive.BytesUsed, listed.Archive.BytesProjected);
        Assert.Equal(
            AgentExhibitionArchiveV2.MaximumBytes - listed.Archive.BytesProjected,
            listed.Archive.RemainingBytes);

        // The two schema versions say one to two rather than leaving a boolean
        // to imply it, and the stored one still reports the file on disk.
        Assert.Equal(AgentExhibitionArchiveV2.CurrentSchemaVersion, listed.Archive.SchemaVersion);
        Assert.Equal(AgentExhibitionArchiveV2.LegacySchemaVersion, listed.Archive.StoredSchemaVersion);
        Assert.Equal(legacyBytes, File.ReadAllBytes(store.ArchivePath).Length);
    }

    [Fact]
    public void The_next_write_settles_both_sizes_and_both_schema_versions()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var legacy = PlayAndSave(temporary.Path, "match_settle1", seed: 71UL);
        var store = new AgentExhibitionArchiveStore(temporary.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.ArchivePath)!);
        File.WriteAllText(store.ArchivePath, LegacyDocument(legacy));
        legacy.Registry.Dispose();

        var fresh = PlayAndSave(temporary.Path, "match_settle2", seed: 72UL);
        var status = fresh.Registry.ArchiveExhibition(fresh.Handle);

        var onDisk = File.ReadAllBytes(store.ArchivePath).Length;
        Assert.Equal(onDisk, status.Archive.BytesUsed);
        Assert.Equal(onDisk, status.Archive.BytesProjected);
        Assert.Equal(
            AgentExhibitionArchiveV2.CurrentSchemaVersion,
            status.Archive.StoredSchemaVersion);
        Assert.Equal(
            AgentExhibitionArchiveV2.MaximumBytes - onDisk,
            status.Archive.RemainingBytes);
        Assert.Equal(2, status.Archive.EntryCount);
    }

    [Fact]
    public void Every_listed_entry_carries_its_place_in_the_whole_store()
    {
        using var temporary = new ArchiveTemporaryDirectory();
        var receipts = new List<string>();
        var routes = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var exhibition = PlayAndSave(
                temporary.Path,
                $"match_place{index}",
                seed: 3000UL + (ulong)index);
            exhibition.Registry.ArchiveExhibition(exhibition.Handle);
            receipts.Add(exhibition.Receipt.ReceiptHash);
            routes.Add(exhibition.Receipt.RouteIdentityHash);
            if (index < 2)
            {
                exhibition.Registry.Dispose();
            }
            else
            {
                _ = exhibition;
            }
        }

        using var registry = CreateRegistry(temporary.Path, "match_placeread", seed: 3100UL);
        var all = registry.ListExhibitions(null);
        Assert.Equal([0, 1, 2], all.Archive.Entries.Select(entry => entry.Position).ToArray());
        Assert.Equal(
            receipts,
            all.Archive.Entries.Select(entry => entry.ReceiptHash).ToArray());

        // Filtering used to lose the ordering entirely. Position is the store's
        // order, so eviction order stays visible through a narrowed listing.
        var narrowed = registry.ListExhibitions(routes[2]);
        var listed = Assert.Single(narrowed.Archive.Entries);
        Assert.Equal(2, listed.Position);
        Assert.Equal(3, narrowed.Archive.EntryCount);

        // final_tick rides along so a browser can order or label without opening
        // a receipt. It comes from the stored receipt, not from a new stored field.
        Assert.All(
            all.Archive.Entries,
            entry => Assert.True(entry.FinalTick >= 0));
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
