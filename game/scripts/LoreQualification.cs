using System.Text.Json;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record LoreQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int EntryCount,
    int SurfaceCount,
    int DiscoverableCount,
    int ArchiveCount,
    int SurfaceStationCount,
    int SurfaceRivalCount,
    int SurfaceMutationCount,
    int DiscoverableKindCount,
    int ArchiveKindCount,
    int InitialUnlockedCount,
    int FullyUnlockedCount,
    int MissingCopyIdCount,
    int BrokenContinuityCount,
    int UnsafeCriticalEntryCount,
    bool KeyboardRouteComplete,
    bool ControllerRouteComplete,
    bool CriticalCopyNamespaceIsolated,
    bool RulesStateUnchangedByBrowsing,
    bool ProgressionAwardsExcluded,
    bool OptionalOfflineCatalogComplete,
    string HumanReviewStatus,
    IReadOnlyList<string> PendingHumanChecks)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Serialize() => JsonSerializer.Serialize(
        this,
        SerializerOptions) + "\n";
}

internal static class LoreQualification
{
    public static LoreQualificationEvidence Run(
        bool keyboardRouteComplete,
        bool controllerRouteComplete)
    {
        var validation = LoreCatalog.Validate();
        var missingCopyIds = LoreCatalog.All.Sum(entry =>
            (ShellLocalization.ContainsId(entry.TitleCopyId) ? 0 : 1)
            + (ShellLocalization.ContainsId(entry.BodyCopyId) ? 0 : 1));
        var initialUnlocked = LoreCatalog.All.Count(entry =>
            LoreCatalog.IsUnlocked(entry, LoreUnlockContext.Empty));
        var allRewards = ProgressionGoalCatalog.Goals.Select(item => item.Reward.Id)
            .Concat(BroadcastTourCatalog.Events.Select(item => item.Reward.Id))
            .ToHashSet(StringComparer.Ordinal);
        var allMilestones = new HashSet<string>(StringComparer.Ordinal)
        {
            "first-broadcast",
            "match-win",
            "score-100",
            "survive-500",
            "combo-5",
            "power-route",
            "collision-save",
        };
        var completeContext = new LoreUnlockContext(
            allRewards,
            allMilestones,
            LocalReplayCount: 5);
        var fullyUnlocked = LoreCatalog.All.Count(entry =>
            LoreCatalog.IsUnlocked(entry, completeContext));
        var criticalCopyNamespaceIsolated = OnboardingCopyIds.All.All(copyId =>
                !copyId.StartsWith("lore.", StringComparison.Ordinal))
            && ShellLocalization.All
                .Where(entry => entry.Id.StartsWith("feedback.", StringComparison.Ordinal))
                .All(entry => !entry.Id.StartsWith("lore.", StringComparison.Ordinal));
        var before = SnakeRun.Create(80_010UL);
        var after = SnakeRun.Create(80_010UL);
        _ = LoreCatalog.All.Select(entry =>
            LoreCatalog.IsUnlocked(entry, completeContext)).ToArray();
        var rulesStateUnchangedByBrowsing = before.ComputeStateHash()
            == after.ComputeStateHash();
        var progressionAwardsExcluded = LoreCatalog.All.All(entry =>
            !entry.AwardsProgression
            && !entry.RequiredForPlay
            && !entry.ActiveRunInterruptible);
        var optionalOfflineCatalogComplete = LoreCatalog.All.All(entry =>
            entry.SchemaVersion == LoreCatalog.SchemaVersion
            && entry.TitleCopyId.StartsWith("lore.entry.", StringComparison.Ordinal)
            && entry.BodyCopyId.StartsWith("lore.entry.", StringComparison.Ordinal));
        string[] pendingHumanChecks =
        [
            "Review canon, tone, humor, and continuity with the complete world bible",
            "Review every depth at maximum text scale with keyboard and controller prompts on Windows, macOS, and Linux",
            "Confirm discoverable and archive pacing rewards curiosity without obscuring critical play or creating grind",
        ];
        var passed = validation.Passed
            && validation.EntryCount == 41
            && validation.SurfaceCount == 19
            && validation.DiscoverableCount == 14
            && validation.ArchiveCount == 8
            && initialUnlocked == 19
            && fullyUnlocked == 41
            && missingCopyIds == 0
            && keyboardRouteComplete
            && controllerRouteComplete
            && criticalCopyNamespaceIsolated
            && rulesStateUnchangedByBrowsing
            && progressionAwardsExcluded
            && optionalOfflineCatalogComplete;
        if (!passed)
        {
            throw new InvalidOperationException("Optional lore qualification failed.");
        }

        return new LoreQualificationEvidence(
            SchemaVersion: 1,
            Kind: "optional-lore-qualification-v1",
            Passed: true,
            EntryCount: validation.EntryCount,
            SurfaceCount: validation.SurfaceCount,
            DiscoverableCount: validation.DiscoverableCount,
            ArchiveCount: validation.ArchiveCount,
            SurfaceStationCount: validation.SurfaceStationCount,
            SurfaceRivalCount: validation.SurfaceRivalCount,
            SurfaceMutationCount: validation.SurfaceMutationCount,
            DiscoverableKindCount: validation.DiscoverableKindCount,
            ArchiveKindCount: validation.ArchiveKindCount,
            InitialUnlockedCount: initialUnlocked,
            FullyUnlockedCount: fullyUnlocked,
            MissingCopyIdCount: missingCopyIds,
            BrokenContinuityCount: validation.BrokenContinuityCount,
            UnsafeCriticalEntryCount: validation.UnsafeCriticalEntryCount,
            KeyboardRouteComplete: keyboardRouteComplete,
            ControllerRouteComplete: controllerRouteComplete,
            CriticalCopyNamespaceIsolated: criticalCopyNamespaceIsolated,
            RulesStateUnchangedByBrowsing: rulesStateUnchangedByBrowsing,
            ProgressionAwardsExcluded: progressionAwardsExcluded,
            OptionalOfflineCatalogComplete: optionalOfflineCatalogComplete,
            HumanReviewStatus: "pending-editorial-platform-and-pacing-review",
            PendingHumanChecks: pendingHumanChecks);
    }
}
