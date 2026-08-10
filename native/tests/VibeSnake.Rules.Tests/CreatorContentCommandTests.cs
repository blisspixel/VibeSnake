using System.Text.Json;
using System.Text.Json.Nodes;
using ValidateCreatorContent;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class CreatorContentCommandTests
{
    private const string PolicyHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Usage_errors_are_stable_and_do_not_emit_validation_reports()
    {
        foreach (IReadOnlyList<string>? arguments in new IReadOnlyList<string>?[]
        {
            null,
            [],
            ["unknown"],
            ["personality"],
            ["personality", "profile.json", "--wrong", "id"],
            ["pack-set", "inventory.json"],
            ["pack-set", "inventory.json", "1.0.0", "vibesnake-core", "zero", "core.json"],
            ["pack-set", "inventory.json", "1.0.0", "vibesnake-core", "0", "core.json"],
        })
        {
            var output = new StringWriter();
            var error = new StringWriter();

            Assert.Equal(2, CreatorContentCommand.Run(arguments, output, error));
            Assert.Empty(output.ToString());
            Assert.Contains("ValidateCreatorContent", error.ToString(), StringComparison.Ordinal);
        }

        Assert.Throws<ArgumentNullException>(
            () => CreatorContentCommand.Run([], null!, TextWriter.Null));
        Assert.Throws<ArgumentNullException>(
            () => CreatorContentCommand.Run([], TextWriter.Null, null!));
    }

    [Fact]
    public void Personality_command_reports_valid_unofficial_and_actionable_invalid_content()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var validPath = Path.Combine(directory, "route_planner.json");
            File.WriteAllText(
                validPath,
                """
                {
                  "schemaVersion": 1,
                  "name": "Route Planner",
                  "description": "Prefers measured routes.",
                  "aggression": 0.4,
                  "risk_tolerance": 0.2,
                  "patience": 0.9,
                  "greed": 0.3,
                  "chaos": 0.1,
                  "power_up_priority": 0.6,
                  "color": [80, 180, 255]
                }
                """);

            var valid = Invoke(["personality", validPath]);
            Assert.Equal(0, valid.ExitCode);
            Assert.True(valid.Report.GetProperty("passed").GetBoolean());
            Assert.Equal("personality-success", valid.Report.GetProperty("code").GetString());
            Assert.Equal("route_planner", valid.Report.GetProperty("contentId").GetString());
            Assert.False(valid.Report.GetProperty("executesContent").GetBoolean());
            Assert.False(valid.Report.GetProperty("arbitraryCodeSupported").GetBoolean());

            var explicitId = Invoke(["personality", validPath, "--id", "my_route"]);
            Assert.Equal(0, explicitId.ExitCode);
            Assert.Equal("my_route", explicitId.Report.GetProperty("contentId").GetString());

            var reserved = Invoke(["personality", validPath, "--id", "balanced"]);
            Assert.Equal(1, reserved.ExitCode);
            Assert.Equal("personality-reserved-id", reserved.Report.GetProperty("code").GetString());

            var invalidPath = Path.Combine(directory, "invalid.json");
            File.WriteAllText(invalidPath, "{ invalid");
            var invalid = Invoke(["personality", invalidPath]);
            Assert.Equal(1, invalid.ExitCode);
            Assert.Equal("personality-invalid-json", invalid.Report.GetProperty("code").GetString());

            var missing = Invoke(["personality", Path.Combine(directory, "missing.json")]);
            Assert.Equal(1, missing.ExitCode);
            Assert.Equal("personality-io-error", missing.Report.GetProperty("code").GetString());
            Assert.DoesNotContain(directory, missing.Output, StringComparison.OrdinalIgnoreCase);

            var unsafePath = Invoke(["personality", "bad\0path.json"]);
            Assert.Equal(1, unsafePath.ExitCode);
            Assert.Equal("personality-path-unsafe", unsafePath.Report.GetProperty("code").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Pack_set_command_validates_canonical_compatibility_and_no_override_order()
    {
        using var fixture = PackFixture.Create();

        var valid = Invoke(
        [
            "pack-set",
            fixture.InventoryPath,
            "0.3.0",
            RulesetIdentity.CurrentId,
            RulesetIdentity.CurrentVersion.ToString(),
            fixture.CorePath,
            fixture.RadioPath,
        ]);
        Assert.Equal(0, valid.ExitCode);
        Assert.True(valid.Report.GetProperty("passed").GetBoolean());
        Assert.Equal("pack-set-valid", valid.Report.GetProperty("code").GetString());
        Assert.Equal(2, valid.Report.GetProperty("packs").GetArrayLength());
        Assert.Equal(
            new[] { ContentPackManifest.CorePackId, "vibesnake.radio.flow-signal" },
            valid.Report.GetProperty("resolutionOrder")
                .EnumerateArray()
                .Select(item => item.GetString()));
        Assert.False(valid.Report.GetProperty("executesContent").GetBoolean());
        Assert.False(valid.Report.GetProperty("arbitraryCodeSupported").GetBoolean());

        var incompatibleArguments = new[]
        {
            "pack-set",
            fixture.InventoryPath,
            "2.0.0",
            RulesetIdentity.CurrentId,
            RulesetIdentity.CurrentVersion.ToString(),
            fixture.CorePath,
            fixture.RadioPath,
        };
        var incompatible = Invoke(incompatibleArguments);
        Assert.Equal(1, incompatible.ExitCode);
        Assert.Equal("pack-set-incompatible", incompatible.Report.GetProperty("code").GetString());
        Assert.Contains(
            incompatible.Report.GetProperty("packs").EnumerateArray(),
            pack => pack.GetProperty("code").GetString() == "game-version-too-new");

        var collision = Invoke(
        [
            "pack-set",
            fixture.InventoryPath,
            "0.3.0",
            RulesetIdentity.CurrentId,
            RulesetIdentity.CurrentVersion.ToString(),
            fixture.CorePath,
            fixture.RadioPath,
            fixture.RadioPath,
        ]);
        Assert.Equal(1, collision.ExitCode);
        Assert.Equal("pack-id-collision", collision.Report.GetProperty("code").GetString());
        Assert.Empty(collision.Report.GetProperty("resolutionOrder").EnumerateArray());

        var wrongFirst = Invoke(
        [
            "pack-set",
            fixture.InventoryPath,
            "0.3.0",
            RulesetIdentity.CurrentId,
            RulesetIdentity.CurrentVersion.ToString(),
            fixture.RadioPath,
        ]);
        Assert.Equal(1, wrongFirst.ExitCode);
        Assert.Equal("core-kind-required", wrongFirst.Report.GetProperty("code").GetString());

        var wrongOptional = Invoke(
        [
            "pack-set",
            fixture.InventoryPath,
            "0.3.0",
            RulesetIdentity.CurrentId,
            RulesetIdentity.CurrentVersion.ToString(),
            fixture.CorePath,
            fixture.CorePath,
        ]);
        Assert.Equal(1, wrongOptional.ExitCode);
        Assert.Equal("optional-kind-invalid", wrongOptional.Report.GetProperty("code").GetString());
    }

    [Fact]
    public void Pack_set_command_fails_closed_on_noncanonical_missing_and_invalid_inputs()
    {
        using var fixture = PackFixture.Create();
        var noncanonicalPath = Path.Combine(fixture.DirectoryPath, "noncanonical.json");
        File.WriteAllText(noncanonicalPath, " " + File.ReadAllText(fixture.RadioPath));

        foreach (var arguments in new[]
        {
            new[]
            {
                "pack-set", fixture.InventoryPath, "0.3.0", RulesetIdentity.CurrentId,
                RulesetIdentity.CurrentVersion.ToString(), fixture.CorePath, noncanonicalPath,
            },
            new[]
            {
                "pack-set", fixture.InventoryPath, "0.3.0", RulesetIdentity.CurrentId,
                RulesetIdentity.CurrentVersion.ToString(), fixture.CorePath,
                Path.Combine(fixture.DirectoryPath, "missing.json"),
            },
            new[]
            {
                "pack-set", Path.Combine(fixture.DirectoryPath, "missing-inventory.json"),
                "0.3.0", RulesetIdentity.CurrentId, RulesetIdentity.CurrentVersion.ToString(),
                fixture.CorePath,
            },
        })
        {
            var result = Invoke(arguments);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("pack-set-invalid", result.Report.GetProperty("code").GetString());
            Assert.False(result.Report.GetProperty("passed").GetBoolean());
            Assert.False(result.Report.GetProperty("executesContent").GetBoolean());
        }

        var invalidInventoryPath = Path.Combine(fixture.DirectoryPath, "invalid-inventory.json");
        File.WriteAllText(invalidInventoryPath, "{ invalid");
        var invalidInventory = Invoke(
        [
            "pack-set", invalidInventoryPath, "0.3.0", RulesetIdentity.CurrentId,
            RulesetIdentity.CurrentVersion.ToString(), fixture.CorePath,
        ]);
        Assert.Equal(1, invalidInventory.ExitCode);
        Assert.Equal("pack-set-invalid", invalidInventory.Report.GetProperty("code").GetString());
    }

    private static InvocationResult Invoke(IReadOnlyList<string> arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CreatorContentCommand.Run(arguments, output, error);
        Assert.Empty(error.ToString());
        var outputText = output.ToString();
        using var document = JsonDocument.Parse(outputText);
        return new InvocationResult(exitCode, outputText, document.RootElement.Clone());
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "vibesnake-creator-content-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record InvocationResult(int ExitCode, string Output, JsonElement Report);

    private sealed class PackFixture : IDisposable
    {
        private PackFixture(
            string directoryPath,
            string inventoryPath,
            string corePath,
            string radioPath)
        {
            DirectoryPath = directoryPath;
            InventoryPath = inventoryPath;
            CorePath = corePath;
            RadioPath = radioPath;
        }

        public string DirectoryPath { get; }

        public string InventoryPath { get; }

        public string CorePath { get; }

        public string RadioPath { get; }

        public static PackFixture Create()
        {
            var directory = CreateTemporaryDirectory();
            var inventoryPath = Path.Combine(directory, "inventory.json");
            var corePath = Path.Combine(directory, "core.json");
            var radioPath = Path.Combine(directory, "radio.json");
            var coreAsset = Asset(
                "config/core.json",
                ContentPackManifest.CorePackId,
                "core-config",
                "required",
                "application/json",
                new string('1', 64),
                10,
                "core-credit");
            var radioAsset = Asset(
                "audio/radio/track.mp3",
                "vibesnake.radio.flow-signal",
                "radio-track",
                "optional",
                "audio/mpeg",
                new string('2', 64),
                20,
                "radio-credit");
            var inventoryDocument = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["assetRoot"] = "assets",
                ["policySha256"] = PolicyHash,
                ["fileCount"] = 2,
                ["assets"] = new JsonArray(coreAsset, radioAsset),
            };
            var inventoryJson = inventoryDocument.ToJsonString();
            File.WriteAllText(inventoryPath, inventoryJson);
            var inventory = ContentInventory.Parse(inventoryJson);

            var core = Manifest(
                ContentPackManifest.CorePackId,
                "core",
                "Vibe Snake Core",
                coreAsset,
                "core-credit",
                dependencies: [],
                radio: null);
            var radio = Manifest(
                "vibesnake.radio.flow-signal",
                "radio",
                "The Flow Signal",
                radioAsset,
                "radio-credit",
                dependencies:
                [
                    new JsonObject
                    {
                        ["id"] = ContentPackManifest.CorePackId,
                        ["minInclusive"] = "1.0.0",
                        ["maxExclusive"] = "2.0.0",
                    },
                ],
                radio: new JsonObject
                {
                    ["stationId"] = "flow_signal",
                    ["stationName"] = "The Flow Signal",
                    ["trackIds"] = new JsonArray(radioAsset["id"]!.GetValue<string>()),
                });
            File.WriteAllText(
                corePath,
                ContentPackManifest.Parse(core.ToJsonString(), inventory).RenderCanonical());
            File.WriteAllText(
                radioPath,
                ContentPackManifest.Parse(radio.ToJsonString(), inventory).RenderCanonical());
            return new PackFixture(directory, inventoryPath, corePath, radioPath);
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);

        private static JsonObject Asset(
            string path,
            string packId,
            string role,
            string runtimeUse,
            string mediaType,
            string sha256,
            int bytes,
            string creditId) => new()
            {
                ["id"] = "asset:" + path,
                ["path"] = path,
                ["mediaType"] = mediaType,
                ["bytes"] = bytes,
                ["sha256"] = sha256,
                ["integrityStatus"] = "valid",
                ["role"] = role,
                ["packId"] = packId,
                ["runtimeUse"] = runtimeUse,
                ["shipStatus"] = "approved",
                ["exportEligible"] = true,
                ["rights"] = new JsonObject
                {
                    ["status"] = "cleared",
                    ["source"] = "fixture source",
                    ["license"] = "Apache-2.0",
                    ["attribution"] = "Fixture Contributors",
                    ["reviewNote"] = "fixture review",
                },
                ["duplicateOf"] = null,
                ["creditId"] = creditId,
            };

        private static JsonObject Manifest(
            string id,
            string kind,
            string displayName,
            JsonObject asset,
            string creditId,
            JsonNode[] dependencies,
            JsonObject? radio) => new()
            {
                ["schemaVersion"] = 1,
                ["id"] = id,
                ["version"] = "1.0.0",
                ["kind"] = kind,
                ["displayName"] = displayName,
                ["description"] = "Qualified creator fixture.",
                ["compatibility"] = new JsonObject
                {
                    ["gameVersion"] = new JsonObject
                    {
                        ["minInclusive"] = "0.3.0",
                        ["maxExclusive"] = "1.1.0",
                    },
                    ["ruleset"] = new JsonObject
                    {
                        ["id"] = RulesetIdentity.CurrentId,
                        ["minInclusive"] = RulesetIdentity.CurrentVersion,
                        ["maxExclusive"] = RulesetIdentity.CurrentVersion + 1,
                    },
                },
                ["inventory"] = new JsonObject
                {
                    ["schemaVersion"] = 1,
                    ["assetRoot"] = "assets",
                    ["policySha256"] = PolicyHash,
                },
                ["dependencies"] = new JsonArray(dependencies),
                ["files"] = new JsonArray(new JsonObject
                {
                    ["id"] = asset["id"]!.GetValue<string>(),
                    ["path"] = asset["path"]!.GetValue<string>(),
                    ["mediaType"] = asset["mediaType"]!.GetValue<string>(),
                    ["bytes"] = asset["bytes"]!.GetValue<int>(),
                    ["sha256"] = asset["sha256"]!.GetValue<string>(),
                    ["role"] = asset["role"]!.GetValue<string>(),
                    ["runtimeUse"] = asset["runtimeUse"]!.GetValue<string>(),
                    ["creditId"] = creditId,
                }),
                ["credits"] = new JsonArray(new JsonObject
                {
                    ["id"] = creditId,
                    ["source"] = "fixture source",
                    ["license"] = "Apache-2.0",
                    ["attribution"] = "Fixture Contributors",
                    ["reviewEvidence"] = "fixture review",
                }),
                ["radio"] = radio,
            };
    }
}
