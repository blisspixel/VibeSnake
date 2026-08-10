using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ContentCurationQualificationTests
{
    private static readonly HashSet<string> SuspiciousFilenameTokens = new(
        ["copy", "draft", "old", "temp", "test", "tmp"],
        StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Public_catalog_has_an_exact_fail_closed_curation_handoff()
    {
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var inventory = ContentInventory.LoadFromFile(
            Path.Combine(repositoryRoot, "config", "content_inventory.json"));
        var planPath = Path.Combine(repositoryRoot, "config", "content_curation_v1.json");
        var planJson = File.ReadAllText(planPath, new UTF8Encoding(false, true));
        using var planDocument = JsonDocument.Parse(planJson);
        AssertExactProperties(
            planDocument.RootElement,
            "schemaVersion", "planId", "inventoryPolicySha256", "decisionStatus",
            "coreMusic", "stations");
        AssertExactProperties(
            planDocument.RootElement.GetProperty("coreMusic"),
            "pendingAssetIds", "approvedAssetIds", "rejectedAssetIds");
        foreach (var stationElement in planDocument.RootElement.GetProperty("stations").EnumerateArray())
        {
            AssertExactProperties(
                stationElement,
                "id", "pendingAssetIds", "approvedAssetIds", "rejectedAssetIds");
        }
        var plan = JsonSerializer.Deserialize<ContentCurationPlanSource>(
            planJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(1, plan.SchemaVersion);
        Assert.Equal("vibesnake-content-curation-v1", plan.PlanId);
        Assert.Equal(inventory.PolicySha256, plan.InventoryPolicySha256);
        Assert.Equal("pending-human-listening-review", plan.DecisionStatus);
        AssertDecisionGroup(plan.CoreMusic);
        Assert.Empty(plan.CoreMusic.PendingAssetIds);
        Assert.Empty(plan.CoreMusic.ApprovedAssetIds);
        Assert.Empty(plan.CoreMusic.RejectedAssetIds);

        var stationIdentities = BroadcastStationCatalog.All;
        Assert.Equal(8, stationIdentities.Count);
        Assert.Equal(
            stationIdentities.Select(station => station.StationId),
            plan.Stations.Select(station => station.Id));
        Assert.Equal(8, stationIdentities.Select(station => station.StationName).Distinct().Count());
        Assert.Equal(8, stationIdentities.Select(station => station.HostName).Distinct().Count());
        Assert.Equal(8, stationIdentities.Select(station => station.VisualIdentity).Distinct().Count());
        Assert.All(stationIdentities, station =>
        {
            Assert.NotEmpty(station.MusicalInclusionRule);
            Assert.Equal(BroadcastStationApproval.PlannedUnapproved, station.Approval);
        });

        var runtimeRadio = inventory.Assets
            .Where(asset => asset.Role == "runtime-radio-track")
            .OrderBy(asset => asset.Id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(95, runtimeRadio.Length);
        var accountedAssetIds = new List<string>();
        var stationEvidence = new List<object>();
        foreach (var station in plan.Stations)
        {
            AssertDecisionGroup(station);
            Assert.Empty(station.ApprovedAssetIds);
            Assert.Empty(station.RejectedAssetIds);
            Assert.InRange(station.PendingAssetIds.Count, 11, 13);
            Assert.Equal(station.PendingAssetIds.Count, station.PendingAssetIds.Distinct().Count());
            foreach (var assetId in station.PendingAssetIds)
            {
                Assert.True(inventory.TryGetAssetById(assetId, out var asset));
                Assert.Equal("runtime-radio-track", asset.Role);
                Assert.Equal("audio/mpeg", asset.MediaType);
                Assert.Equal("valid", asset.IntegrityStatus);
                Assert.Equal("cleared", asset.RightsStatus);
                Assert.False(asset.ExportEligible);
                Assert.Null(asset.DuplicateOf);
                Assert.Equal("vibesnake-radio", asset.PackId);
                Assert.DoesNotContain(
                    Path.GetFileNameWithoutExtension(asset.RelativePath).Split('_'),
                    token => SuspiciousFilenameTokens.Contains(token));
                accountedAssetIds.Add(assetId);
            }

            stationEvidence.Add(new
            {
                station.Id,
                PendingCount = station.PendingAssetIds.Count,
                ApprovedCount = station.ApprovedAssetIds.Count,
                RejectedCount = station.RejectedAssetIds.Count,
            });
        }

        Assert.Equal(95, accountedAssetIds.Count);
        Assert.Equal(95, accountedAssetIds.Distinct().Count());
        Assert.Equal(
            runtimeRadio.Select(asset => asset.Id),
            accountedAssetIds.OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(2, plan.Stations.Max(station => station.PendingAssetIds.Count)
            - plan.Stations.Min(station => station.PendingAssetIds.Count));
        Assert.DoesNotContain(runtimeRadio, asset => asset.DuplicateOf is not null);
        Assert.Equal(0, inventory.ExportEligibleCount);

        var evidence = new
        {
            SchemaVersion = 1,
            Kind = "content-curation-qualification-v1",
            Passed = true,
            AutomatedFoundationPassed = true,
            ReleaseReady = false,
            plan.PlanId,
            plan.DecisionStatus,
            InventoryPolicySha256 = inventory.PolicySha256,
            RuntimeRadioAssetCount = runtimeRadio.Length,
            PendingRadioTrackCount = accountedAssetIds.Count,
            ApprovedRadioTrackCount = 0,
            RejectedRadioTrackCount = 0,
            CoreMusicCandidateCount = 0,
            StationCount = plan.Stations.Count,
            MinimumStationCandidateCount = plan.Stations.Min(station => station.PendingAssetIds.Count),
            MaximumStationCandidateCount = plan.Stations.Max(station => station.PendingAssetIds.Count),
            DistinctStationIdentityCount = stationIdentities.Count,
            DuplicateRadioAssetCount = runtimeRadio.Count(asset => asset.DuplicateOf is not null),
            SuspiciousFilenameCount = 0,
            StructuralMediaIntegrityCount = runtimeRadio.Count(asset => asset.IntegrityStatus == "valid"),
            FullDecodeEvidenceCount = 0,
            LoudnessEvidenceCount = 0,
            HumanListeningReviewCount = 0,
            ExportEligibleFileCount = inventory.ExportEligibleCount,
            CreditsContract = ContentCreditsDocument.DocumentId,
            Stations = stationEvidence,
            ReleaseBlockers = new[]
            {
                "No authored core music candidate is selected.",
                "All 95 radio tracks still require retained full-decode, loudness, clipping, and listening evidence.",
                "No source asset is export eligible and no production content-pack manifest exists.",
                "Station badges still require visual approval and final pack assignment.",
            },
        };
        var evidenceDirectory = Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        evidenceDirectory = string.IsNullOrWhiteSpace(evidenceDirectory)
            ? Path.Combine(repositoryRoot, "TestResults", "native")
            : Path.GetFullPath(evidenceDirectory);
        Directory.CreateDirectory(evidenceDirectory);
        var evidencePath = Path.Combine(evidenceDirectory, "content_curation.json");
        File.WriteAllText(
            evidencePath,
            JsonSerializer.Serialize(
                evidence,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }) + "\n",
            new UTF8Encoding(false));

        Assert.True(File.Exists(evidencePath));
        using var written = JsonDocument.Parse(File.ReadAllText(evidencePath));
        Assert.True(written.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(written.RootElement.GetProperty("releaseReady").GetBoolean());
        Assert.Equal(0, written.RootElement.GetProperty("humanListeningReviewCount").GetInt32());
    }

    private static void AssertDecisionGroup(IContentCurationDecisionGroupSource group)
    {
        Assert.NotNull(group);
        Assert.NotNull(group.PendingAssetIds);
        Assert.NotNull(group.ApprovedAssetIds);
        Assert.NotNull(group.RejectedAssetIds);
        Assert.Empty(group.PendingAssetIds.Intersect(group.ApprovedAssetIds));
        Assert.Empty(group.PendingAssetIds.Intersect(group.RejectedAssetIds));
        Assert.Empty(group.ApprovedAssetIds.Intersect(group.RejectedAssetIds));
    }

    private static void AssertExactProperties(JsonElement element, params string[] names)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal(
            names.OrderBy(name => name, StringComparer.Ordinal),
            element.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    private sealed record ContentCurationPlanSource(
        int SchemaVersion,
        string PlanId,
        string InventoryPolicySha256,
        string DecisionStatus,
        ContentCurationDecisionGroupSource CoreMusic,
        IReadOnlyList<ContentCurationStationSource> Stations);

    private interface IContentCurationDecisionGroupSource
    {
        IReadOnlyList<string> PendingAssetIds { get; }

        IReadOnlyList<string> ApprovedAssetIds { get; }

        IReadOnlyList<string> RejectedAssetIds { get; }
    }

    private sealed record ContentCurationDecisionGroupSource(
        IReadOnlyList<string> PendingAssetIds,
        IReadOnlyList<string> ApprovedAssetIds,
        IReadOnlyList<string> RejectedAssetIds) : IContentCurationDecisionGroupSource;

    private sealed record ContentCurationStationSource(
        string Id,
        IReadOnlyList<string> PendingAssetIds,
        IReadOnlyList<string> ApprovedAssetIds,
        IReadOnlyList<string> RejectedAssetIds)
        : IContentCurationDecisionGroupSource;
}
