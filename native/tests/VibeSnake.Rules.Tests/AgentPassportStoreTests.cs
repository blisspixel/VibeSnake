using System.Text;
using System.Text.Json;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// AA-07's persistent public identity. The passport a caller declares is a
/// claim; this store keeps only what verified receipts earned.
/// </summary>
[Collection(AgentHostIntegrationGroup.Name)]
public sealed class AgentPassportStoreTests
{
    [Fact]
    public void A_record_is_built_only_from_a_receipt_that_verifies_itself()
    {
        using var temporary = new PassportTemporaryDirectory();
        var receipt = PlayVerified(temporary.Path, "match_pass1", seed: 101UL);

        var record = AgentPassportRecordV1.FromReceipt(receipt);

        Assert.Equal(AgentPassportRecordV1.Contract, record.Schema);
        Assert.Equal(receipt.Passport.AgentId, record.AgentId);
        Assert.Equal(1, record.Exhibitions);
        Assert.Equal(receipt.Score, record.BestScore);
        Assert.Equal(receipt.ReceiptHash, record.FirstReceiptHash);
        Assert.Equal(receipt.ReceiptHash, record.LatestReceiptHash);
        Assert.Equal(receipt.ReceiptHash, Assert.Single(record.ReceiptHashes));
        Assert.True(record.IsSelfConsistent());
        Assert.DoesNotContain(
            "display_name",
            JsonSerializer.Serialize(record),
            StringComparison.Ordinal);

        var tampered = receipt with { Score = receipt.Score + 1 };
        Assert.Throws<ArgumentException>(() => AgentPassportRecordV1.FromReceipt(tampered));
        Assert.Throws<ArgumentException>(() => record.WithReceipt(tampered));
        Assert.Throws<ArgumentNullException>(() => AgentPassportRecordV1.FromReceipt(null!));
    }

    [Fact]
    public void A_record_never_absorbs_another_agents_exhibition()
    {
        using var temporary = new PassportTemporaryDirectory();
        var mine = PlayVerified(temporary.Path, "match_passmine", seed: 102UL);
        var theirs = PlayVerified(
            temporary.Path,
            "match_passtheirs",
            seed: 103UL,
            agentId: "other-agent");

        var record = AgentPassportRecordV1.FromReceipt(mine);

        Assert.NotEqual(mine.Passport.AgentId, theirs.Passport.AgentId);
        Assert.Throws<ArgumentException>(() => record.WithReceipt(theirs));
    }

    [Fact]
    public void Milestones_point_back_at_the_exhibition_that_earned_them()
    {
        using var temporary = new PassportTemporaryDirectory();
        var solo = PlayVerified(temporary.Path, "match_passsolo", seed: 104UL);
        var rivalry = PlayVerified(
            temporary.Path,
            "match_passrival",
            seed: 105UL,
            rivalPersonalityId: "optimal");

        var record = AgentPassportRecordV1.FromReceipt(solo).WithReceipt(rivalry);

        var first = Assert.Single(
            record.Milestones,
            milestone => milestone.MilestoneId == AgentPassportMilestoneV1.FirstExhibitionId);
        Assert.Equal(AgentPassportMilestoneV1.Contract, first.Schema);
        Assert.Equal(solo.ReceiptHash, first.ReceiptHash);
        Assert.Equal(solo.RouteIdentityHash, first.RouteIdentityHash);

        var rivalryMilestone = Assert.Single(
            record.Milestones,
            milestone => milestone.MilestoneId == AgentPassportMilestoneV1.FirstRivalryId);
        Assert.Equal(rivalry.ReceiptHash, rivalryMilestone.ReceiptHash);

        var again = PlayVerified(
            temporary.Path,
            "match_passrival2",
            seed: 106UL,
            rivalPersonalityId: "optimal");
        var grown = record.WithReceipt(again);
        Assert.Single(
            grown.Milestones,
            milestone => milestone.MilestoneId == AgentPassportMilestoneV1.FirstRivalryId);
        Assert.Equal(rivalry.ReceiptHash, grown.Milestones
            .Single(m => m.MilestoneId == AgentPassportMilestoneV1.FirstRivalryId)
            .ReceiptHash);
        Assert.Equal(3, grown.ReceiptHashes.Count);
        Assert.True(grown.IsSelfConsistent());
    }

    [Fact]
    public void A_completed_lesson_earns_its_milestone_and_a_lesson_record()
    {
        using var temporary = new PassportTemporaryDirectory();
        var receipt = PlayFirstTurn(temporary.Path, "match_passlesson");

        var record = AgentPassportRecordV1.FromReceipt(receipt);

        Assert.True(receipt.LessonOutcome is { AllRequirementsSatisfied: true });
        var lesson = Assert.Single(record.Lessons);
        Assert.Equal(AgentPassportLessonRecordV1.Contract, lesson.Schema);
        Assert.Equal(receipt.LessonOutcome!.LessonId, lesson.LessonId);
        Assert.Equal(1, lesson.Exhibitions);
        Assert.Equal(1, lesson.AllRequirementsSatisfied);
        Assert.Equal(
            receipt.LessonOutcome.RequirementsSatisfied,
            lesson.BestRequirementsSatisfied);
        var milestone = Assert.Single(
            record.Milestones,
            item => item.MilestoneId == AgentPassportMilestoneV1.FirstCompletedLessonId);
        Assert.Equal(receipt.ReceiptHash, milestone.ReceiptHash);
        Assert.DoesNotContain(
            record.Milestones,
            item => item.MilestoneId == AgentPassportMilestoneV1.FirstAllStyleThresholdsId);
        Assert.Empty(record.Styles);
    }

    [Fact]
    public void A_rival_record_counts_the_three_outcomes_and_nothing_more()
    {
        using var temporary = new PassportTemporaryDirectory();
        var first = PlayVerified(
            temporary.Path,
            "match_passr1",
            seed: 107UL,
            rivalPersonalityId: "optimal");
        var second = PlayVerified(
            temporary.Path,
            "match_passr2",
            seed: 108UL,
            rivalPersonalityId: "optimal");

        var record = AgentPassportRecordV1.FromReceipt(first).WithReceipt(second);

        var rival = Assert.Single(record.Rivals);
        Assert.Equal(AgentPassportRivalRecordV1.Contract, rival.Schema);
        Assert.Equal("optimal", rival.RivalPersonalityId);
        Assert.Equal(2, rival.Faced);
        Assert.Equal(rival.Faced, rival.Ahead + rival.Level + rival.Behind);
        Assert.True(record.IsSelfConsistent());
    }

    [Fact]
    public void Recording_the_same_exhibition_twice_never_inflates_a_count()
    {
        using var temporary = new PassportTemporaryDirectory();
        var receipt = PlayVerified(temporary.Path, "match_passdup", seed: 109UL);
        var store = new AgentPassportStore(temporary.Path);

        var created = store.Record(receipt);
        var bytes = File.ReadAllBytes(store.DocumentPath);
        var repeated = store.Record(receipt);

        Assert.Equal(AgentPassportWriteCode.Created, created.Code);
        Assert.True(created.Recorded);
        Assert.Empty(created.Evicted);
        Assert.Equal(AgentPassportWriteCode.AlreadyRecorded, repeated.Code);
        Assert.False(repeated.Recorded);
        Assert.Equal(bytes, File.ReadAllBytes(store.DocumentPath));
        Assert.Equal(1, Assert.Single(store.Read().Records).Exhibitions);
    }

    [Fact]
    public void A_tampered_receipt_is_refused_rather_than_thrown()
    {
        using var temporary = new PassportTemporaryDirectory();
        var receipt = PlayVerified(temporary.Path, "match_passtamper", seed: 115UL);
        var store = new AgentPassportStore(temporary.Path);
        var tampered = receipt with { Score = receipt.Score + 1 };

        var refused = store.Record(tampered);

        Assert.Equal(AgentPassportWriteCode.NoVerifiedReceipt, refused.Code);
        Assert.False(refused.Recorded);
        Assert.False(File.Exists(store.DocumentPath));
    }

    [Fact]
    public void A_record_survives_the_process_that_earned_it()
    {
        using var temporary = new PassportTemporaryDirectory();
        var first = PlayVerified(temporary.Path, "match_passdur1", seed: 110UL);
        new AgentPassportStore(temporary.Path).Record(first);

        var second = PlayVerified(temporary.Path, "match_passdur2", seed: 111UL);
        var reopened = new AgentPassportStore(temporary.Path);
        var updated = reopened.Record(second);

        Assert.Equal(AgentPassportWriteCode.Updated, updated.Code);
        var record = Assert.Single(reopened.Read().Records);
        Assert.Equal(2, record.Exhibitions);
        Assert.Equal(first.ReceiptHash, record.FirstReceiptHash);
        Assert.Equal(second.ReceiptHash, record.LatestReceiptHash);
        Assert.Equal(
            [first.ReceiptHash, second.ReceiptHash],
            record.ReceiptHashes);
        Assert.Equal(
            AgentPassportDocumentV1.CurrentSchemaVersion,
            reopened.Read().SchemaVersion);
        var inspected = reopened.Inspect();
        Assert.Equal(inspected.BytesUsed, File.ReadAllBytes(reopened.DocumentPath).Length);
        Assert.Equal(inspected.BytesUsed, inspected.BytesProjected);
        Assert.Equal(
            AgentPassportDocumentV1.CurrentSchemaVersion,
            inspected.StoredSchemaVersion);
    }

    [Fact]
    public void A_public_record_can_be_deleted()
    {
        using var temporary = new PassportTemporaryDirectory();
        var receipt = PlayVerified(temporary.Path, "match_passdel", seed: 112UL);
        var store = new AgentPassportStore(temporary.Path);
        store.Record(receipt);

        var missing = store.Forget("no-such-agent");
        Assert.Equal(AgentPassportForgetCode.NotRecorded, missing.Code);
        Assert.Empty(missing.Forgotten);
        Assert.Single(store.Read().Records);

        var forgotten = store.Forget(receipt.Passport.AgentId);
        Assert.Equal(AgentPassportForgetCode.Forgotten, forgotten.Code);
        Assert.Equal(receipt.Passport.AgentId, Assert.Single(forgotten.Forgotten).AgentId);
        Assert.Empty(store.Read().Records);
        Assert.Empty(store.Read().RecordedReceiptHashes);

        var again = store.Forget(null);
        Assert.Equal(AgentPassportForgetCode.NotRecorded, again.Code);

        Assert.Equal(AgentPassportWriteCode.Created, store.Record(receipt).Code);
    }

    [Fact]
    public void Forgetting_one_agent_does_not_block_another_agents_receipts()
    {
        using var temporary = new PassportTemporaryDirectory();
        var first = PlayVerified(temporary.Path, "match_passkeep1", seed: 116UL, agentId: "keep");
        var second = PlayVerified(temporary.Path, "match_passdrop1", seed: 117UL, agentId: "drop");
        var store = new AgentPassportStore(temporary.Path);
        store.Record(first);
        store.Record(second);

        var forgotten = store.Forget("drop");

        Assert.Equal(AgentPassportForgetCode.Forgotten, forgotten.Code);
        var remaining = Assert.Single(store.Read().Records);
        Assert.Equal("keep", remaining.AgentId);
        Assert.Equal(first.ReceiptHash, Assert.Single(store.Read().RecordedReceiptHashes));
        Assert.Equal(AgentPassportWriteCode.AlreadyRecorded, store.Record(first).Code);
        Assert.Equal(AgentPassportWriteCode.Created, store.Record(second).Code);
    }

    [Fact]
    public void A_seventeenth_agent_is_refused_rather_than_silently_dropped()
    {
        using var temporary = new PassportTemporaryDirectory();
        var store = new AgentPassportStore(temporary.Path);
        AgentExhibitionReceiptV2? first = null;
        for (var index = 0; index < AgentPassportDocumentV1.MaximumRecords; index++)
        {
            var receipt = PlayVerified(
                temporary.Path,
                $"match_passcap{index}",
                seed: 200UL + (ulong)index,
                agentId: $"agent-{index}");
            first ??= receipt;
            Assert.True(store.Record(receipt).Recorded);
        }

        var extra = PlayVerified(
            temporary.Path,
            "match_passcapx",
            seed: 300UL,
            agentId: "agent-new");
        var before = File.ReadAllBytes(store.DocumentPath);
        var refused = store.Record(extra);

        Assert.False(refused.Recorded);
        Assert.Equal(AgentPassportWriteCode.CapacityReached, refused.Code);
        Assert.Empty(refused.Evicted);
        Assert.Equal(before, File.ReadAllBytes(store.DocumentPath));
        Assert.Equal(AgentPassportDocumentV1.MaximumRecords, store.Read().Records.Count);
        Assert.Contains(
            store.Read().Records,
            record => record.AgentId == first!.Passport.AgentId);
    }

    [Fact]
    public void A_corrupt_passport_store_is_quarantined_rather_than_repaired()
    {
        using var temporary = new PassportTemporaryDirectory();
        var receipt = PlayVerified(temporary.Path, "match_passcorrupt", seed: 113UL);
        var store = new AgentPassportStore(temporary.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.DocumentPath)!);
        var damaged = Encoding.UTF8.GetBytes("{\"schema\":\"not-a-passport-store\"");
        File.WriteAllBytes(store.DocumentPath, damaged);

        var written = store.Record(receipt);

        Assert.True(written.Recorded);
        Assert.True(written.RecoveredFromCorruption);
        var quarantined = Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(store.DocumentPath)!,
            "*" + AgentPassportStore.CorruptFileExtension));
        Assert.Equal(damaged, File.ReadAllBytes(quarantined));
    }

    [Fact]
    public void An_inconsistent_stored_record_is_never_loaded_as_data()
    {
        using var temporary = new PassportTemporaryDirectory();
        var receipt = PlayVerified(temporary.Path, "match_passbad", seed: 114UL);
        var record = AgentPassportRecordV1.FromReceipt(receipt);
        Assert.True(record.IsSelfConsistent());

        Assert.False((record with { Exhibitions = 0 }).IsSelfConsistent());
        Assert.False((record with { Schema = "other" }).IsSelfConsistent());
        Assert.False((record with { AgentId = "   " }).IsSelfConsistent());
        Assert.False((record with { ReceiptHashes = Array.Empty<string>() }).IsSelfConsistent());
        Assert.False((record with
        {
            Milestones = new[]
            {
                new AgentPassportMilestoneV1(
                    AgentPassportMilestoneV1.Contract,
                    "invented-milestone",
                    receipt.ReceiptHash,
                    receipt.RouteIdentityHash),
            },
        }).IsSelfConsistent());
        Assert.False((record with
        {
            Rivals = new[]
            {
                new AgentPassportRivalRecordV1(
                    AgentPassportRivalRecordV1.Contract,
                    "optimal",
                    Faced: 1,
                    Ahead: 1,
                    Level: 1,
                    Behind: 1),
            },
        }).IsSelfConsistent());

        var document = AgentPassportDocumentV1.Empty with
        {
            Records = new[] { record with { Exhibitions = 0 } },
        };
        Assert.False(document.IsWellFormed());
    }

    [Fact]
    public void Recording_a_passport_does_not_write_human_player_data()
    {
        using var temporary = new PassportTemporaryDirectory();
        var receipt = PlayVerified(temporary.Path, "match_passiso", seed: 118UL);
        var store = new AgentPassportStore(temporary.Path);
        store.Record(receipt);

        Assert.True(File.Exists(store.DocumentPath));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "preferences.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "achievements.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "progression.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "personal_bests.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "score_history.json")));
        Assert.DoesNotContain(
            "display_name",
            File.ReadAllText(store.DocumentPath),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            receipt.Passport.DisplayName,
            File.ReadAllText(store.DocumentPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_records_lists_and_forgets_through_the_same_store()
    {
        using var temporary = new PassportTemporaryDirectory();
        using var registry = CreateRegistry(temporary.Path, "match_passhost", seed: 119UL);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            maximumSteps: 1);
        var live = registry.RecordPassport(started.MatchHandle, receiptHash: null);
        Assert.Equal(AgentPassportWriteCode.NoVerifiedReceipt, live.Code);

        registry.PlayMove(
            started.MatchHandle,
            "match_passhost-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Continue);

        var recorded = registry.RecordPassport(started.MatchHandle, receiptHash: null);
        Assert.True(recorded.Recorded);
        Assert.Equal(AgentPassportWriteCode.Created, recorded.Code);
        Assert.Equal(
            AgentPassportIndexV1.Contract,
            recorded.Passports.Schema);
        Assert.Equal(1, recorded.Passports.RecordCount);
        Assert.Equal(
            AgentPassportDocumentV1.MaximumRecords,
            recorded.Passports.Capacity);
        Assert.Equal(
            AgentPassportDocumentV1.MaximumBytes,
            recorded.Passports.MaximumBytes);
        Assert.Equal(
            AgentPassportDocumentV1.MaximumRecords - 1,
            recorded.Passports.RemainingRecords);
        Assert.Single(recorded.Passports.Entries);

        var listed = registry.ListPassports(null);
        Assert.Equal(AgentPassportListingV1.Contract, listed.Schema);
        Assert.Equal(1, listed.MatchedCount);
        Assert.Equal(recorded.Passports.BytesUsed, listed.Passports.BytesUsed);

        var filteredMissing = registry.ListPassports("no-such-agent");
        Assert.Equal(0, filteredMissing.MatchedCount);
        Assert.Equal(1, filteredMissing.Passports.RecordCount);

        var recordedHash = Assert.Single(recorded.Passports.Entries).FirstReceiptHash;
        var forgotten = registry.ForgetPassport(recorded.AgentId);
        Assert.True(forgotten.Forgotten);
        Assert.Equal(AgentPassportForgetCode.Forgotten, forgotten.Code);
        Assert.Empty(registry.ListPassports(null).Passports.Entries);

        Assert.Throws<ArgumentException>(
            () => registry.RecordPassport(null, null));
        Assert.Throws<ArgumentException>(
            () => registry.RecordPassport(started.MatchHandle, recordedHash));
    }

    [Fact]
    public void A_passport_can_be_recorded_from_an_archived_receipt()
    {
        using var temporary = new PassportTemporaryDirectory();
        using var registry = CreateRegistry(temporary.Path, "match_passarch", seed: 120UL);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            maximumSteps: 1);
        registry.PlayMove(
            started.MatchHandle,
            "match_passarch-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Continue);
        var saved = registry.SaveVerifiedReplay(started.MatchHandle);
        Assert.True(saved.IsSuccess, saved.Message);
        var archived = registry.ArchiveExhibition(started.MatchHandle);
        Assert.True(archived.Archived);
        var receiptHash = archived.ReceiptHash;
        Assert.False(string.IsNullOrWhiteSpace(receiptHash));

        var missing = registry.RecordPassport(null, "0".PadLeft(64, '0'));
        Assert.Equal(AgentPassportWriteCode.NotArchived, missing.Code);
        Assert.False(missing.Recorded);

        var recorded = registry.RecordPassport(null, receiptHash);
        Assert.True(recorded.Recorded);
        Assert.Equal(AgentPassportWriteCode.Created, recorded.Code);
        Assert.Null(recorded.MatchHandle);
        Assert.Equal(receiptHash, Assert.Single(recorded.Passports.Entries).FirstReceiptHash);
    }

    [Fact]
    public void The_store_refuses_a_root_it_did_not_receive()
    {
        Assert.Throws<ArgumentException>(() => new AgentPassportStore("relative/root"));
        Assert.Throws<ArgumentException>(() => new AgentPassportStore("  "));
        Assert.Throws<ArgumentNullException>(() => new AgentPassportStore(null!));
        using var temporary = new PassportTemporaryDirectory();
        Assert.Throws<ArgumentNullException>(
            () => new AgentPassportStore(temporary.Path).Record(null!));
        Assert.Throws<ArgumentException>(
            () => new AgentPassportStore(temporary.Path).Forget("   "));
    }

    private static AgentExhibitionReceiptV2 PlayVerified(
        string root,
        string handle,
        ulong seed,
        string? rivalPersonalityId = null,
        string? agentId = null)
    {
        using var registry = CreateRegistry(root, handle, seed);
        var anonymous = AgentPassportV4.Anonymous;
        var passport = agentId is null
            ? null
            : new AgentPassportV4(
                AgentPassportV4.Contract,
                agentId,
                anonymous.PolicyVersion,
                anonymous.DisplayName,
                anonymous.AvatarId,
                anonymous.AccentId,
                anonymous.StationId);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            maximumSteps: 1,
            rivalPersonalityId: rivalPersonalityId,
            passport: passport);
        var moved = registry.PlayMove(
            started.MatchHandle,
            handle + "-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Continue);
        Assert.True(moved.Accepted);
        var status = registry.GetExhibitionReceipt(started.MatchHandle);
        Assert.True(status.IsAvailable);
        return Assert.IsType<AgentExhibitionReceiptV2>(status.Receipt);
    }

    private static AgentExhibitionReceiptV2 PlayFirstTurn(string root, string handle)
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        using var registry = CreateRegistry(root, handle, definition.PracticeSeed);
        var started = registry.StartLesson(definition.Id);
        var observation = started.Observation;
        var opposite = observation.Direction switch
        {
            Direction.Up => AgentAction.Down,
            Direction.Down => AgentAction.Up,
            Direction.Left => AgentAction.Right,
            _ => AgentAction.Left,
        };
        var rejected = registry.PlayMove(
            started.MatchHandle,
            handle + "-reversal",
            observation.Tick,
            observation.StateHash,
            opposite);
        Assert.False(rejected.Accepted);
        observation = rejected.Observation;
        AgentActionResponseV5? last = null;
        for (var step = 0; step < definition.MaximumSteps; step++)
        {
            if (observation.LessonProgress!.AllRequirementsSatisfied)
            {
                break;
            }

            last = registry.PlayMove(
                started.MatchHandle,
                handle + "-" + step,
                observation.Tick,
                observation.StateHash,
                AgentLessonRouteDriver.ChooseAction(definition.Id, observation));
            observation = last.Observation;
        }

        if (last?.MatchResult is null)
        {
            registry.Finish(started.MatchHandle);
        }

        var status = registry.GetExhibitionReceipt(started.MatchHandle);
        Assert.True(status.IsAvailable);
        return Assert.IsType<AgentExhibitionReceiptV2>(status.Receipt);
    }

    private static AgentSessionRegistry CreateRegistry(
        string root,
        string handle,
        ulong seed) =>
        new(
            new ReplayStore(root),
            () => handle,
            () => seed,
            archiveStore: new AgentExhibitionArchiveStore(root),
            passportStore: new AgentPassportStore(root));

    private sealed class PassportTemporaryDirectory : IDisposable
    {
        public PassportTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentPassportTests",
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
