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

    [Theory]
    [InlineData(4097)]
    [InlineData(int.MaxValue)]
    public void Rejects_declared_asset_counts_before_allocating_collections(int count)
    {
        var document = ValidInventory(exportEligible: false);
        document["fileCount"] = count;
        var exception = Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(document.ToJsonString()));
        Assert.Contains("4096-asset limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Published_assets_cannot_be_mutated_through_collection_casts()
    {
        var inventory = ContentInventory.Parse(ValidInventory(exportEligible: false).ToJsonString());
        var asset = Assert.Single(inventory.Assets);
        var collection = Assert.IsAssignableFrom<IList<ContentInventoryAsset>>(inventory.Assets);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Throws<NotSupportedException>(() => collection[0] = asset with { ExportEligible = true });
        Assert.False(inventory.IsExportEligible(asset.RelativePath));
        Assert.Equal(1, inventory.FileCount);
    }

    [Theory]
    [InlineData("shipStatus", "blocked")]
    [InlineData("shipStatus", "excluded")]
    [InlineData("integrityStatus", "invalid")]
    [InlineData("integrityStatus", "empty")]
    [InlineData("integrityStatus", "")]
    [InlineData("rightsStatus", "unverified")]
    [InlineData("rightsStatus", "not-applicable")]
    public void Export_eligibility_cannot_override_explicit_rejection(string field, string value)
    {
        var document = ValidInventory(exportEligible: true);
        var asset = document["assets"]!.AsArray()[0]!;
        if (field == "rightsStatus")
        {
            asset["rights"]!["status"] = value;
        }
        else
        {
            asset[field] = value;
        }

        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(document.ToJsonString()));
        asset["exportEligible"] = false;
        Assert.False(ContentInventory.Parse(document.ToJsonString()).IsExportEligible("demo/file.json"));
    }

    [Fact]
    public void Missing_legacy_integrity_metadata_is_supported_but_wrong_types_cannot_bypass_rejection()
    {
        var document = ValidInventory(exportEligible: true);
        var asset = document["assets"]!.AsArray()[0]!.AsObject();
        asset.Remove("integrityStatus");
        Assert.True(ContentInventory.Parse(document.ToJsonString()).IsExportEligible("demo/file.json"));
        asset["integrityStatus"] = 123;
        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(document.ToJsonString()));
    }

    [Theory]
    [InlineData("C:/escape.json")]
    [InlineData("C:\\escape.json")]
    [InlineData("//server/share.json")]
    [InlineData("demo//file.json")]
    [InlineData("demo/./file.json")]
    [InlineData("demo/evil\0.json")]
    [InlineData("")]
    public void Unsafe_paths_are_rejected_independently_of_host_platform(string path)
    {
        var document = ValidInventory(exportEligible: false);
        document["assets"]!.AsArray()[0]!["path"] = path;
        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(document.ToJsonString()));
        var inventory = ContentInventory.Parse(ValidInventory(exportEligible: false).ToJsonString());
        Assert.Throws<ArgumentException>(() => inventory.TryGetAsset(path, out _));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(268435457L)]
    [InlineData(long.MaxValue)]
    public void Asset_byte_counts_are_bounded_before_totals_are_computed(long bytes)
    {
        var document = ValidInventory(exportEligible: false);
        document["assets"]!.AsArray()[0]!["bytes"] = bytes;
        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(document.ToJsonString()));
    }

    [Fact]
    public void Aggregate_content_bytes_are_bounded()
    {
        var document = ValidInventory(exportEligible: false);
        var original = document["assets"]!.AsArray()[0]!.DeepClone();
        var assets = new JsonArray();
        for (var index = 0; index < 17; index++)
        {
            var asset = original.DeepClone();
            asset["id"] = "asset:" + index;
            asset["path"] = index + ".json";
            asset["bytes"] = 256L * 1024 * 1024;
            assets.Add(asset);
        }

        document["fileCount"] = 17;
        document["assets"] = assets;
        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(document.ToJsonString()));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("asset")]
    [InlineData("rights")]
    public void Repeated_json_fields_cannot_replace_trusted_values(string location)
    {
        var json = ValidInventory(exportEligible: false).ToJsonString();
        json = location switch
        {
            "root" => json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2,\"schemaVersion\":1", StringComparison.Ordinal),
            "asset" => json.Replace("\"exportEligible\":false", "\"exportEligible\":true,\"exportEligible\":false", StringComparison.Ordinal),
            _ => json.Replace("\"status\":\"cleared\"", "\"status\":\"unverified\",\"status\":\"cleared\"", StringComparison.Ordinal),
        };
        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(json));
    }

    [Fact]
    public void Document_limits_apply_to_utf8_bytes_and_file_loading()
    {
        const int maximumBytes = 8 * 1024 * 1024;
        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(new string('x', maximumBytes + 1)));
        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(new string('\u00e9', (maximumBytes / 2) + 1)));
        var root = Directory.CreateTempSubdirectory("vibesnake-inventory-loading-");
        try
        {
            var path = Path.Combine(root.FullName, "inventory.json");
            using (var stream = File.Create(path))
            {
                stream.SetLength(maximumBytes + 1L);
            }

            Assert.Throws<InvalidDataException>(() => ContentInventory.LoadFromFile(path));
            File.WriteAllBytes(path, [0xff]);
            Assert.Throws<InvalidDataException>(() => ContentInventory.LoadFromFile(path));
            File.WriteAllText(path, " \n\t");
            Assert.Throws<InvalidDataException>(() => ContentInventory.LoadFromFile(path));
            File.WriteAllText(path, ValidInventory(exportEligible: false).ToJsonString(), new System.Text.UTF8Encoding(true));
            Assert.Equal(1, ContentInventory.LoadFromFile(path).FileCount);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("syntax")]
    [InlineData("missing-rights")]
    [InlineData("null-rights")]
    [InlineData("wrong-bytes")]
    [InlineData("wrong-export")]
    [InlineData("wrong-root-metadata")]
    [InlineData("wrong-duplicate-evidence")]
    public void Malformed_inventory_errors_use_the_optional_content_recovery_contract(string variant)
    {
        var document = ValidInventory(exportEligible: false);
        var asset = document["assets"]!.AsArray()[0]!.AsObject();
        switch (variant)
        {
            case "missing-rights":
                asset.Remove("rights");
                break;
            case "null-rights":
                asset["rights"] = null;
                break;
            case "wrong-bytes":
                asset["bytes"] = "huge";
                break;
            case "wrong-export":
                asset["exportEligible"] = "approved";
                break;
            case "wrong-root-metadata":
                document["assetRoot"] = 123;
                break;
            case "wrong-duplicate-evidence":
                asset["duplicateOf"] = false;
                break;
        }

        var json = variant == "syntax" ? "{ invalid JSON" : document.ToJsonString();
        Assert.Throws<InvalidDataException>(() => ContentInventory.Parse(json));
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
