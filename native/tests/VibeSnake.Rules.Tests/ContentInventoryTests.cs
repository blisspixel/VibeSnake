using VibeSnake.Persistence;
using System.Text.Json.Nodes;

namespace VibeSnake.Rules.Tests;

public sealed class ContentInventoryTests
{
    [Fact]
    public void Parses_public_inventory_and_rejects_export_until_approval()
    {
        var path = ResolveInventoryPath();
        var inventory = ContentInventory.LoadFromFile(path);

        Assert.Equal(1, inventory.SchemaVersion);
        Assert.Equal(inventory.FileCount, inventory.Assets.Count);
        Assert.Equal(0, inventory.ExportEligibleCount);
        Assert.All(inventory.Assets, asset => Assert.Equal("cleared", asset.RightsStatus));
        Assert.False(inventory.IsExportEligible("ai/custom/military_tactician.json"));
        Assert.Contains(
            inventory.Assets,
            asset => asset.RelativePath.EndsWith("logo.png", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_path_traversal_queries()
    {
        var inventory = ContentInventory.Parse(
            """
            {
              "schemaVersion": 1,
              "fileCount": 1,
              "assets": [
                {
                  "id": "asset:demo.json",
                  "path": "demo.json",
                  "mediaType": "application/json",
                  "bytes": 1,
                  "sha256": "00",
                  "exportEligible": false,
                  "shipStatus": "blocked",
                  "rights": { "status": "cleared" }
                }
              ]
            }
            """);

        Assert.Throws<ArgumentException>(() => inventory.IsExportEligible("../demo.json"));
        Assert.Throws<ArgumentException>(() => inventory.IsExportEligible("/demo.json"));
    }

    [Fact]
    public void Parses_optional_metadata_and_normalizes_safe_lookup_paths()
    {
        var document = ValidInventory(exportEligible: true);
        var inventory = ContentInventory.Parse(document.ToJsonString());

        Assert.Equal("assets", inventory.AssetRoot);
        Assert.Equal(new string('a', 64), inventory.PolicySha256);
        Assert.Equal(1, inventory.ExportEligibleCount);
        Assert.Equal(10, inventory.TotalBytes);
        Assert.Equal(10, inventory.ExportEligibleBytes);
        Assert.True(inventory.IsExportEligible("./demo/file.json"));
        Assert.True(inventory.TryGetAsset("demo\\file.json", out var byPath));
        Assert.True(inventory.TryGetAssetById("asset:demo/file.json", out var byId));
        Assert.Same(byPath, byId);
        Assert.False(inventory.TryGetAsset("missing.json", out _));
        Assert.False(inventory.TryGetAssetById("asset:missing", out _));
        Assert.Single(inventory.GetExportEligibleForPack("vibesnake.core"));
        Assert.Empty(inventory.GetExportEligibleForPack("vibesnake.radio.other"));
        Assert.Equal(1, inventory.CountByMediaTypePrefix("APPLICATION/"));

        Assert.Equal("core-config", byPath.Role);
        Assert.Equal("required", byPath.RuntimeUse);
        Assert.Equal("valid", byPath.IntegrityStatus);
        Assert.Equal("asset:source.json", byPath.DuplicateOf);
        Assert.Equal("project", byPath.Rights.Source);
        Assert.Equal("MIT", byPath.Rights.License);
        Assert.Equal("none", byPath.Rights.Attribution);
        Assert.Equal("reviewed", byPath.Rights.ReviewEvidence);
    }

    [Fact]
    public void Rejects_invalid_root_schema_count_and_assets_array_contracts()
    {
        foreach (var json in new[]
        {
            "[]",
            "{}",
            """{ "schemaVersion": "1", "fileCount": 1, "assets": [] }""",
            """{ "schemaVersion": 2, "fileCount": 1, "assets": [] }""",
            """{ "schemaVersion": 1, "assets": [] }""",
            """{ "schemaVersion": 1, "fileCount": "1", "assets": [] }""",
            """{ "schemaVersion": 1, "fileCount": 0, "assets": [] }""",
            """{ "schemaVersion": 1, "fileCount": 1 }""",
            """{ "schemaVersion": 1, "fileCount": 1, "assets": {} }""",
        })
        {
            Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(json));
        }

        var mismatch = ValidInventory(exportEligible: false);
        mismatch["fileCount"] = 2;
        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(mismatch.ToJsonString()));
    }

    [Fact]
    public void Rejects_duplicate_ids_paths_and_unsafe_asset_paths()
    {
        var duplicatePath = ValidInventory(exportEligible: false);
        var secondPath = duplicatePath["assets"]!.AsArray()[0]!.DeepClone();
        secondPath["id"] = "asset:second";
        duplicatePath["assets"]!.AsArray().Add(secondPath);
        duplicatePath["fileCount"] = 2;
        Assert.Throws<InvalidDataException>(
            () => ContentInventory.Parse(duplicatePath.ToJsonString()));

        var duplicateId = ValidInventory(exportEligible: false);
        var secondId = duplicateId["assets"]!.AsArray()[0]!.DeepClone();
        secondId["path"] = "second.json";
        duplicateId["assets"]!.AsArray().Add(secondId);
        duplicateId["fileCount"] = 2;
        Assert.Throws<InvalidDataException>(
            () => ContentInventory.Parse(duplicateId.ToJsonString()));

        foreach (var path in new[] { "/root.json", "../escape.json" })
        {
            var unsafePath = ValidInventory(exportEligible: false);
            unsafePath["assets"]!.AsArray()[0]!["path"] = path;
            Assert.Throws<InvalidDataException>(
                () => ContentInventory.Parse(unsafePath.ToJsonString()));
        }

        var missingPath = ValidInventory(exportEligible: false);
        missingPath["assets"]!.AsArray()[0]!["path"] = null;
        Assert.Throws<InvalidDataException>(
            () => ContentInventory.Parse(missingPath.ToJsonString()));
    }

    [Fact]
    public void Optional_inventory_metadata_defaults_to_empty_values()
    {
        var document = ValidInventory(exportEligible: false);
        document.Remove("assetRoot");
        document.Remove("policySha256");
        var asset = document["assets"]!.AsArray()[0]!.AsObject();
        foreach (var field in new[]
        {
            "packId", "role", "runtimeUse", "integrityStatus", "duplicateOf",
        })
        {
            asset.Remove(field);
        }
        var rights = asset["rights"]!.AsObject();
        foreach (var field in new[] { "source", "license", "attribution", "reviewNote" })
        {
            rights.Remove(field);
        }

        var inventory = ContentInventory.Parse(document.ToJsonString());
        var parsed = Assert.Single(inventory.Assets);
        Assert.Equal(string.Empty, inventory.AssetRoot);
        Assert.Equal(string.Empty, inventory.PolicySha256);
        Assert.Equal(string.Empty, parsed.PackId);
        Assert.Equal(string.Empty, parsed.Role);
        Assert.Null(parsed.DuplicateOf);
        Assert.Equal(string.Empty, parsed.Rights.Source);
    }

    private static JsonObject ValidInventory(bool exportEligible) => new()
    {
        ["schemaVersion"] = 1,
        ["assetRoot"] = "assets",
        ["policySha256"] = new string('a', 64),
        ["fileCount"] = 1,
        ["assets"] = new JsonArray(new JsonObject
        {
            ["id"] = "asset:demo/file.json",
            ["path"] = "demo/file.json",
            ["mediaType"] = "application/json",
            ["bytes"] = 10,
            ["sha256"] = new string('b', 64),
            ["exportEligible"] = exportEligible,
            ["shipStatus"] = "approved",
            ["packId"] = "vibesnake.core",
            ["role"] = "core-config",
            ["runtimeUse"] = "required",
            ["integrityStatus"] = "valid",
            ["duplicateOf"] = "asset:source.json",
            ["rights"] = new JsonObject
            {
                ["status"] = "cleared",
                ["source"] = "project",
                ["license"] = "MIT",
                ["attribution"] = "none",
                ["reviewNote"] = "reviewed",
            },
        }),
    };

    private static string ResolveInventoryPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "config", "content_inventory.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate config/content_inventory.json.");
    }
}
