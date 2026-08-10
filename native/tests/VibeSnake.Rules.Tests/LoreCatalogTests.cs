namespace VibeSnake.Rules.Tests;

public sealed class LoreCatalogTests
{
    [Fact]
    public void Catalog_delivers_all_three_depths_and_complete_surface_entities()
    {
        var validation = LoreCatalog.Validate();

        Assert.True(validation.Passed, validation.ToString());
        Assert.Equal(41, validation.EntryCount);
        Assert.Equal(19, validation.SurfaceCount);
        Assert.Equal(14, validation.DiscoverableCount);
        Assert.Equal(8, validation.ArchiveCount);
        Assert.Equal(8, validation.SurfaceStationCount);
        Assert.Equal(10, validation.SurfaceRivalCount);
        Assert.Equal(9, validation.SurfaceMutationCount);
        Assert.Equal(6, validation.DiscoverableKindCount);
        Assert.Equal(4, validation.ArchiveKindCount);
        Assert.Equal(0, validation.DuplicateIdCount);
        Assert.Equal(0, validation.MissingCopyIdCount);
        Assert.Equal(0, validation.UnknownEntityCount);
        Assert.Equal(0, validation.BrokenContinuityCount);
        Assert.Equal(0, validation.InvalidUnlockCount);
        Assert.Equal(0, validation.UnsafeCriticalEntryCount);
    }

    [Fact]
    public void Empty_context_exposes_surface_only_and_full_context_is_complete()
    {
        var emptyUnlocked = LoreCatalog.All
            .Where(entry => LoreCatalog.IsUnlocked(entry, LoreUnlockContext.Empty))
            .ToArray();
        Assert.Equal(19, emptyUnlocked.Length);
        Assert.All(emptyUnlocked, entry => Assert.Equal(LoreDepth.Surface, entry.Depth));

        var rewards = ProgressionGoalCatalog.Goals.Select(item => item.Reward.Id)
            .Concat(BroadcastTourCatalog.Events.Select(item => item.Reward.Id))
            .ToHashSet(StringComparer.Ordinal);
        var milestones = new HashSet<string>(StringComparer.Ordinal)
        {
            "first-broadcast",
            "match-win",
            "score-100",
            "survive-500",
            "combo-5",
            "power-route",
            "collision-save",
        };
        var complete = new LoreUnlockContext(rewards, milestones, LocalReplayCount: 5);

        Assert.All(LoreCatalog.All, entry => Assert.True(LoreCatalog.IsUnlocked(entry, complete)));
    }

    [Fact]
    public void Each_unlock_family_is_read_only_bounded_and_exact()
    {
        var rewardEntry = LoreCatalog.All.Single(item =>
            item.Id == "history-shelter-coil");
        var milestoneEntry = LoreCatalog.All.Single(item =>
            item.Id == "history-redline");
        var replayOne = LoreCatalog.All.Single(item =>
            item.Id == "replay-first-echo");
        var replayFive = LoreCatalog.All.Single(item =>
            item.Id == "replay-five-echoes");
        var rewardContext = new LoreUnlockContext(
            new HashSet<string>(["dossier:shelter-coil"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            0);
        var milestoneContext = new LoreUnlockContext(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(["match-win"], StringComparer.Ordinal),
            0);
        var replayContext = new LoreUnlockContext(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            1);

        Assert.True(LoreCatalog.IsUnlocked(rewardEntry, rewardContext));
        Assert.False(LoreCatalog.IsUnlocked(milestoneEntry, rewardContext));
        Assert.True(LoreCatalog.IsUnlocked(milestoneEntry, milestoneContext));
        Assert.True(LoreCatalog.IsUnlocked(replayOne, replayContext));
        Assert.False(LoreCatalog.IsUnlocked(replayFive, replayContext));
        Assert.True(LoreCatalog.IsUnlocked(
            replayFive,
            replayContext with { LocalReplayCount = 5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoreCatalog.IsUnlocked(
            rewardEntry with { UnlockKind = (LoreUnlockKind)255 },
            LoreUnlockContext.Empty));
        Assert.Throws<ArgumentNullException>(() => LoreCatalog.IsUnlocked(
            null!,
            LoreUnlockContext.Empty));
        Assert.Throws<ArgumentNullException>(() => LoreCatalog.IsUnlocked(
            rewardEntry,
            null!));
    }

    [Fact]
    public void Lore_is_optional_nonmechanical_and_localization_addressable()
    {
        Assert.All(LoreCatalog.All, entry =>
        {
            Assert.False(entry.RequiredForPlay);
            Assert.False(entry.ActiveRunInterruptible);
            Assert.False(entry.AwardsProgression);
            Assert.StartsWith("lore.entry.", entry.TitleCopyId);
            Assert.EndsWith(".title", entry.TitleCopyId);
            Assert.StartsWith("lore.entry.", entry.BodyCopyId);
            Assert.EndsWith(".body", entry.BodyCopyId);
            Assert.DoesNotContain("control", entry.Id, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("danger", entry.Id, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("score-rule", entry.Id, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Equal(
            Enum.GetValues<LoreContentKind>(),
            LoreCatalog.All.Select(item => item.Kind).Distinct().Order().ToArray());
    }

    [Fact]
    public void Catalog_validation_rejects_each_malformed_contract_family()
    {
        Assert.Throws<ArgumentNullException>(() => LoreCatalog.ValidateEntries(null!));

        AssertInvalid("station-flow-signal", entry => entry with
        {
            Id = "station-chaos-theory",
        }, validation => validation.DuplicateIdCount == 1);
        AssertInvalid("station-flow-signal", entry => entry with
        {
            TitleCopyId = string.Empty,
        }, validation => validation.MissingCopyIdCount == 1);
        AssertInvalid("station-flow-signal", entry => entry with
        {
            BodyCopyId = string.Empty,
        }, validation => validation.MissingCopyIdCount == 1);
        AssertInvalid("station-flow-signal", entry => entry with
        {
            TitleCopyId = "lore.entry.wrong.title",
        }, validation => validation.MissingCopyIdCount == 1);
        AssertInvalid("station-flow-signal", entry => entry with
        {
            BodyCopyId = "lore.entry.wrong.body",
        }, validation => validation.MissingCopyIdCount == 1);
        AssertInvalid("station-flow-signal", entry => entry with
        {
            EntityIds = ["entity:unknown"],
        }, validation => validation.UnknownEntityCount == 1);
        AssertInvalid("history-redline", entry => entry with
        {
            ContinuityEntryIds = ["entry-that-does-not-exist"],
        }, validation => validation.BrokenContinuityCount == 1);
        AssertInvalid("history-redline", entry => entry with
        {
            ContinuityEntryIds = [entry.Id],
        }, validation => validation.BrokenContinuityCount == 1);

        AssertInvalid("station-flow-signal", entry => entry with
        {
            UnlockId = "unexpected",
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("station-flow-signal", entry => entry with
        {
            UnlockThreshold = 1,
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("history-shelter-coil", entry => entry with
        {
            UnlockId = null,
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("history-shelter-coil", entry => entry with
        {
            UnlockId = "reward:unknown",
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("history-shelter-coil", entry => entry with
        {
            UnlockThreshold = 1,
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("history-redline", entry => entry with
        {
            UnlockId = null,
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("history-redline", entry => entry with
        {
            UnlockId = "milestone:unknown",
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("history-redline", entry => entry with
        {
            UnlockThreshold = 1,
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("replay-first-echo", entry => entry with
        {
            UnlockId = "unexpected",
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("replay-first-echo", entry => entry with
        {
            UnlockThreshold = 0,
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("replay-first-echo", entry => entry with
        {
            UnlockThreshold = 101,
        }, validation => validation.InvalidUnlockCount == 1);
        AssertInvalid("replay-first-echo", entry => entry with
        {
            UnlockKind = (LoreUnlockKind)255,
        }, validation => validation.InvalidUnlockCount == 1);

        AssertUnsafe(entry => entry with { SchemaVersion = 2 });
        AssertUnsafe(entry => entry with { Depth = (LoreDepth)255 });
        AssertUnsafe(entry => entry with { Kind = (LoreContentKind)255 });
        AssertUnsafe(entry => entry with { CanonTier = (LoreCanonTier)255 });
        AssertUnsafe(entry => entry with { Id = " " });
        AssertUnsafe(entry => entry with { RequiredForPlay = true });
        AssertUnsafe(entry => entry with { ActiveRunInterruptible = true });
        AssertUnsafe(entry => entry with { AwardsProgression = true });
        AssertUnsafe(entry => entry with { StationId = "station:unknown" });
        AssertUnsafe(entry => entry with { SpeakerId = "speaker:unknown" });

        AssertInvalid("station-flow-signal", entry => entry with
        {
            EntityIds = ["station:chaos_theory", "host:cadence-vale"],
        }, validation => validation.SurfaceStationCount == 7);
        AssertInvalid("rival-redline", entry => entry with
        {
            EntityIds = ["rival:coward"],
        }, validation => validation.SurfaceRivalCount == 9);
        AssertInvalid("mutation-glossary", entry => entry with
        {
            EntityIds = entry.EntityIds.Where(id => id != "mutation:magnet").ToArray(),
        }, validation => validation.SurfaceMutationCount == 8);
        var collapsedDiscoverableKinds = LoreCatalog.All
            .Select(entry => entry.Kind == LoreContentKind.TrackNote
                ? entry with { Kind = LoreContentKind.RivalHistory }
                : entry)
            .ToArray();
        var collapsedDiscoverableValidation = LoreCatalog.ValidateEntries(
            collapsedDiscoverableKinds);
        Assert.False(collapsedDiscoverableValidation.Passed);
        Assert.Equal(5, collapsedDiscoverableValidation.DiscoverableKindCount);
        AssertInvalid("mystery-ninth-frequency", entry => entry with
        {
            Kind = LoreContentKind.Timeline,
        }, validation => validation.ArchiveKindCount == 3);

        var tooFew = LoreCatalog.All.SkipLast(1).ToArray();
        Assert.False(LoreCatalog.ValidateEntries(tooFew).Passed);

        AssertInvalid("station-flow-signal", entry => entry with
        {
            Depth = LoreDepth.Discoverable,
        }, validation => validation.SurfaceCount == 18);
        AssertInvalid("history-redline", entry => entry with
        {
            Depth = LoreDepth.Archive,
        }, validation => validation.DiscoverableCount == 13);
        AssertInvalid("mystery-ninth-frequency", entry => entry with
        {
            Depth = (LoreDepth)255,
        }, validation => validation.ArchiveCount == 7);

        static void AssertUnsafe(Func<LoreEntry, LoreEntry> mutate) => AssertInvalid(
            "station-flow-signal",
            mutate,
            validation => validation.UnsafeCriticalEntryCount == 1);

        static void AssertInvalid(
            string entryId,
            Func<LoreEntry, LoreEntry> mutate,
            Func<LoreCatalogValidation, bool> expectedFailure)
        {
            var entries = LoreCatalog.All
                .Select(entry => entry.Id == entryId ? mutate(entry) : entry)
                .ToArray();
            var validation = LoreCatalog.ValidateEntries(entries);

            Assert.False(validation.Passed);
            Assert.True(expectedFailure(validation), validation.ToString());
        }
    }
}
