using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class ContentPackManifestTests
{
    private const string PolicyHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Parses_core_and_radio_against_exact_inventory_allowlists()
    {
        var fixture = CreateFixture();

        var core = Parse(fixture.Core, fixture.Inventory);
        var radio = Parse(fixture.Radio, fixture.Inventory);

        Assert.Equal(ContentPackManifest.CorePackId, core.Id);
        Assert.Equal(ContentPackKind.Core, core.Kind);
        Assert.Equal(2, core.Files.Count);
        Assert.Null(core.Radio);
        Assert.Equal(ContentPackKind.Radio, radio.Kind);
        Assert.Equal("flow_signal", radio.Radio!.StationId);
        Assert.Single(radio.Radio.TrackIds);
    }

    [Fact]
    public void Canonical_render_round_trips_and_file_check_rejects_drift()
    {
        var fixture = CreateFixture();
        var manifest = Parse(fixture.Core, fixture.Inventory);
        var canonical = manifest.RenderCanonical();
        var reparsed = ContentPackManifest.Parse(canonical, fixture.Inventory);
        Assert.Equal(canonical, reparsed.RenderCanonical());
        Assert.EndsWith("\n", canonical, StringComparison.Ordinal);

        var path = Path.Combine(
            Path.GetTempPath(),
            $"vibesnake-content-pack-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, canonical);
            var checkedManifest = ContentPackManifest.CheckCanonicalFile(
                path,
                fixture.Inventory);
            Assert.Equal(manifest.Id, checkedManifest.Id);
            Assert.Equal(canonical, checkedManifest.RenderCanonical());

            File.WriteAllText(path, ToJson(fixture.Core));
            Assert.Throws<InvalidDataException>(
                () => ContentPackManifest.CheckCanonicalFile(path, fixture.Inventory));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Rejects_duplicate_missing_and_unknown_json_fields()
    {
        var fixture = CreateFixture();
        var json = ToJson(fixture.Core);
        var duplicate = "{\"id\":\"duplicate\"," + json[1..];
        Assert.Throws<InvalidDataException>(
            () => ContentPackManifest.Parse(duplicate, fixture.Inventory));

        var missing = Clone(fixture.Core);
        missing.Remove("displayName");
        Assert.Throws<InvalidDataException>(() => Parse(missing, fixture.Inventory));

        var unknown = Clone(fixture.Core);
        unknown["unexpected"] = true;
        Assert.Throws<InvalidDataException>(() => Parse(unknown, fixture.Inventory));
    }

    [Fact]
    public void Rejects_unsafe_paths_case_collisions_and_mismatched_metadata()
    {
        var fixture = CreateFixture();

        var unsafePath = Clone(fixture.Core);
        FileAt(unsafePath, 0)["id"] = "asset:../escape.json";
        FileAt(unsafePath, 0)["path"] = "../escape.json";
        Assert.Throws<InvalidDataException>(() => Parse(unsafePath, fixture.Inventory));

        var collision = Clone(fixture.Core);
        FileAt(collision, 1)["id"] = "asset:CONFIG/core.json";
        FileAt(collision, 1)["path"] = "CONFIG/core.json";
        Assert.Throws<InvalidDataException>(() => Parse(collision, fixture.Inventory));

        var mismatch = Clone(fixture.Core);
        FileAt(mismatch, 0)["bytes"] = 999;
        Assert.Throws<InvalidDataException>(() => Parse(mismatch, fixture.Inventory));
    }

    [Fact]
    public void Rejects_incomplete_allowlist_and_rights_drift()
    {
        var fixture = CreateFixture();

        var incomplete = Clone(fixture.Core);
        Files(incomplete).RemoveAt(1);
        Assert.Throws<InvalidDataException>(() => Parse(incomplete, fixture.Inventory));

        var rightsDrift = Clone(fixture.Core);
        Credits(rightsDrift)[0]!["license"] = "changed-license";
        Assert.Throws<InvalidDataException>(() => Parse(rightsDrift, fixture.Inventory));
    }

    [Fact]
    public void Rejects_invalid_core_dependency_and_radio_contracts()
    {
        var fixture = CreateFixture();

        var coreDependency = Clone(fixture.Core);
        Dependencies(coreDependency).Add(new JsonObject
        {
            ["id"] = "vibesnake.radio.flow-signal",
            ["minInclusive"] = "1.0.0",
            ["maxExclusive"] = "2.0.0",
        });
        Assert.Throws<InvalidDataException>(() => Parse(coreDependency, fixture.Inventory));

        var stationMismatch = Clone(fixture.Radio);
        stationMismatch["id"] = "vibesnake.radio.other";
        Assert.Throws<InvalidDataException>(() => Parse(stationMismatch, fixture.Inventory));

        var invalidStationId = Clone(fixture.Radio);
        invalidStationId["radio"]!["stationId"] = "flow-signal";
        Assert.Throws<InvalidDataException>(() => Parse(invalidStationId, fixture.Inventory));

        var wrongTrackRole = Clone(fixture.Radio);
        FileAt(wrongTrackRole, 0)["role"] = "music";
        Assert.Throws<InvalidDataException>(() => Parse(wrongTrackRole, fixture.Inventory));
    }

    [Fact]
    public void Rejects_empty_ranges_and_resource_excess()
    {
        var fixture = CreateFixture();
        var range = Clone(fixture.Core);
        range["compatibility"]!["gameVersion"]!["maxExclusive"] = "0.3.0";
        Assert.Throws<InvalidDataException>(() => Parse(range, fixture.Inventory));

        var excessive = new string('x', ContentPackManifest.MaximumManifestBytes + 1);
        Assert.Throws<InvalidDataException>(
            () => ContentPackManifest.Parse(excessive, fixture.Inventory));
    }

    [Theory]
    [InlineData("0.2.9", "vibesnake-core", 4, "game-version-too-old")]
    [InlineData("1.1.0", "vibesnake-core", 4, "game-version-too-new")]
    [InlineData("0.3.0", "other-rules", 4, "ruleset-mismatch")]
    [InlineData("0.3.0", "vibesnake-core", 3, "rules-version-too-old")]
    [InlineData("0.3.0", "vibesnake-core", 5, "rules-version-too-new")]
    public void Compatibility_reports_actionable_game_and_rules_failures(
        string gameVersion,
        string rulesetId,
        int rulesVersion,
        string expectedCode)
    {
        var fixture = CreateFixture();
        var core = Parse(fixture.Core, fixture.Inventory);

        var result = ContentPackResolver.Evaluate(
            core,
            gameVersion,
            rulesetId,
            rulesVersion,
            new Dictionary<string, string>
            {
                [ContentPackManifest.CorePackId] = "1.0.0",
            });

        Assert.False(result.Compatible);
        Assert.Equal(expectedCode, result.Code);
        Assert.DoesNotContain("\\", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "missing-dependency")]
    [InlineData("0.9.0", "dependency-version-too-old")]
    [InlineData("2.0.0", "dependency-version-too-new")]
    public void Compatibility_reports_dependency_failures(
        string? coreVersion,
        string expectedCode)
    {
        var fixture = CreateFixture();
        var radio = Parse(fixture.Radio, fixture.Inventory);
        var installed = new Dictionary<string, string>();
        if (coreVersion is not null)
        {
            installed[ContentPackManifest.CorePackId] = coreVersion;
        }

        var result = ContentPackResolver.Evaluate(
            radio,
            "0.3.0",
            RulesetIdentity.CurrentId,
            RulesetIdentity.CurrentVersion,
            installed);

        Assert.False(result.Compatible);
        Assert.Equal(expectedCode, result.Code);
    }

    [Fact]
    public void Resolver_accepts_valid_optional_and_keeps_core_ready_when_optional_is_invalid()
    {
        var fixture = CreateFixture();
        var resolution = ContentPackResolver.Resolve(
            ToJson(fixture.Core),
            [ToJson(fixture.Radio), "not-json"],
            fixture.Inventory,
            "0.3.0");

        Assert.True(resolution.CoreReady);
        Assert.Equal(["vibesnake.radio.flow-signal"], resolution.AcceptedOptional);
        Assert.Equal("invalid-pack", resolution.RejectedOptional["optional[1]"].Code);
    }

    [Fact]
    public void Resolver_rejects_duplicate_optional_ids_without_accepting_either()
    {
        var fixture = CreateFixture();
        var json = ToJson(fixture.Radio);

        var resolution = ContentPackResolver.Resolve(
            ToJson(fixture.Core),
            [json, json],
            fixture.Inventory,
            "0.3.0");

        Assert.True(resolution.CoreReady);
        Assert.Empty(resolution.AcceptedOptional);
        Assert.Equal(
            "invalid-pack",
            resolution.RejectedOptional["vibesnake.radio.flow-signal"].Code);
    }

    [Fact]
    public void Resolver_isolates_incompatible_optional_and_fails_closed_on_core()
    {
        var fixture = CreateFixture();
        var incompatibleRadio = Clone(fixture.Radio);
        incompatibleRadio["compatibility"]!["gameVersion"]!["minInclusive"] = "0.4.0";
        var optionalResolution = ContentPackResolver.Resolve(
            ToJson(fixture.Core),
            [ToJson(incompatibleRadio)],
            fixture.Inventory,
            "0.3.0");
        Assert.True(optionalResolution.CoreReady);
        Assert.Equal(
            "game-version-too-old",
            optionalResolution.RejectedOptional["vibesnake.radio.flow-signal"].Code);

        var incompatibleCore = Clone(fixture.Core);
        incompatibleCore["compatibility"]!["gameVersion"]!["minInclusive"] = "0.4.0";
        var coreResolution = ContentPackResolver.Resolve(
            ToJson(incompatibleCore),
            [ToJson(fixture.Radio)],
            fixture.Inventory,
            "0.3.0");
        Assert.False(coreResolution.CoreReady);
        Assert.Empty(coreResolution.AcceptedOptional);
        Assert.Equal(
            "core-unavailable",
            coreResolution.RejectedOptional["vibesnake.radio.flow-signal"].Code);
    }

    [Fact]
    public void Rejects_invalid_root_schema_kind_and_core_or_radio_contracts()
    {
        var fixture = CreateFixture();
        Assert.Throws<InvalidDataException>(
            () => ContentPackManifest.Parse("[]", fixture.Inventory));
        Assert.Throws<InvalidDataException>(
            () => ContentPackManifest.Parse("{", fixture.Inventory));

        foreach (var (field, value) in new Dictionary<string, JsonNode?>
        {
            ["schemaVersion"] = 2,
            ["id"] = "UPPERCASE",
            ["version"] = "1.0",
            ["kind"] = "video",
            ["displayName"] = " ",
            ["description"] = new string('x', 513),
        })
        {
            var document = Clone(fixture.Core);
            document[field] = value?.DeepClone();
            AssertRejects(document, fixture.Inventory);
        }

        var wrongCoreId = Clone(fixture.Core);
        wrongCoreId["id"] = "vibesnake.other";
        AssertRejects(wrongCoreId, fixture.Inventory);

        var noRequiredCore = Clone(fixture.Core);
        foreach (var file in Files(noRequiredCore))
        {
            file!["runtimeUse"] = "optional";
        }
        AssertRejects(noRequiredCore, fixture.Inventory);

        var missingCoreDependency = Clone(fixture.Radio);
        Dependencies(missingCoreDependency).Clear();
        AssertRejects(missingCoreDependency, fixture.Inventory);

        var wrongCoreDependency = Clone(fixture.Radio);
        Dependencies(wrongCoreDependency)[0]!["id"] = "vibesnake.other";
        AssertRejects(wrongCoreDependency, fixture.Inventory);

        var requiredRadioFile = Clone(fixture.Radio);
        FileAt(requiredRadioFile, 0)["runtimeUse"] = "required";
        AssertRejects(requiredRadioFile, fixture.Inventory);
    }

    [Fact]
    public void Rejects_malformed_compatibility_inventory_and_dependency_contracts()
    {
        var fixture = CreateFixture();

        foreach (var location in new[] { "compatibility", "inventory", "dependencies" })
        {
            var document = Clone(fixture.Core);
            document[location] = true;
            AssertRejects(document, fixture.Inventory);
        }

        var compatibility = Clone(fixture.Core);
        compatibility["compatibility"]!["unexpected"] = true;
        AssertRejects(compatibility, fixture.Inventory);

        var badGameRangeType = Clone(fixture.Core);
        badGameRangeType["compatibility"]!["gameVersion"] = true;
        AssertRejects(badGameRangeType, fixture.Inventory);

        var badGameMinimum = Clone(fixture.Core);
        badGameMinimum["compatibility"]!["gameVersion"]!["minInclusive"] = "01.0.0";
        AssertRejects(badGameMinimum, fixture.Inventory);

        var badRulesType = Clone(fixture.Core);
        badRulesType["compatibility"]!["ruleset"] = true;
        AssertRejects(badRulesType, fixture.Inventory);

        var badRulesMinimum = Clone(fixture.Core);
        badRulesMinimum["compatibility"]!["ruleset"]!["minInclusive"] = 0;
        AssertRejects(badRulesMinimum, fixture.Inventory);

        var emptyRulesRange = Clone(fixture.Core);
        emptyRulesRange["compatibility"]!["ruleset"]!["maxExclusive"] = 4;
        AssertRejects(emptyRulesRange, fixture.Inventory);

        foreach (var (field, value) in new Dictionary<string, JsonNode?>
        {
            ["schemaVersion"] = 2,
            ["assetRoot"] = "different",
            ["policySha256"] = new string('A', 64),
        })
        {
            var document = Clone(fixture.Core);
            document["inventory"]![field] = value?.DeepClone();
            AssertRejects(document, fixture.Inventory);
        }

        var wrongPolicy = Clone(fixture.Core);
        wrongPolicy["inventory"]!["policySha256"] = new string('b', 64);
        AssertRejects(wrongPolicy, fixture.Inventory);

        var selfDependency = Clone(fixture.Radio);
        Dependencies(selfDependency)[0]!["id"] = "vibesnake.radio.flow-signal";
        AssertRejects(selfDependency, fixture.Inventory);

        var duplicateDependency = Clone(fixture.Radio);
        Dependencies(duplicateDependency).Add(Dependencies(duplicateDependency)[0]!.DeepClone());
        AssertRejects(duplicateDependency, fixture.Inventory);

        var emptyDependencyRange = Clone(fixture.Radio);
        Dependencies(emptyDependencyRange)[0]!["maxExclusive"] = "1.0.0";
        AssertRejects(emptyDependencyRange, fixture.Inventory);
    }

    [Fact]
    public void Rejects_malformed_credit_file_and_radio_entries()
    {
        var fixture = CreateFixture();

        var noCredits = Clone(fixture.Core);
        Credits(noCredits).Clear();
        AssertRejects(noCredits, fixture.Inventory);

        var duplicateCredit = Clone(fixture.Core);
        Credits(duplicateCredit).Add(Credits(duplicateCredit)[0]!.DeepClone());
        AssertRejects(duplicateCredit, fixture.Inventory);

        var noFiles = Clone(fixture.Core);
        Files(noFiles).Clear();
        AssertRejects(noFiles, fixture.Inventory);

        var duplicateFile = Clone(fixture.Core);
        Files(duplicateFile).Add(Files(duplicateFile)[0]!.DeepClone());
        AssertRejects(duplicateFile, fixture.Inventory);

        foreach (var (field, value) in new Dictionary<string, JsonNode?>
        {
            ["id"] = "not-an-asset",
            ["path"] = "other/path.json",
            ["sha256"] = new string('A', 64),
            ["runtimeUse"] = "development",
            ["creditId"] = "missing-credit",
            ["mediaType"] = 1,
            ["bytes"] = 0,
            ["role"] = " ",
        })
        {
            var document = Clone(fixture.Core);
            FileAt(document, 0)[field] = value?.DeepClone();
            AssertRejects(document, fixture.Inventory);
        }

        var coreWithRadio = Clone(fixture.Core);
        coreWithRadio["radio"] = new JsonObject();
        AssertRejects(coreWithRadio, fixture.Inventory);

        var radioNoTracks = Clone(fixture.Radio);
        radioNoTracks["radio"]!["trackIds"] = new JsonArray();
        AssertRejects(radioNoTracks, fixture.Inventory);

        var radioNonStringTrack = Clone(fixture.Radio);
        radioNonStringTrack["radio"]!["trackIds"] = new JsonArray(1);
        AssertRejects(radioNonStringTrack, fixture.Inventory);

        var radioDuplicateTrack = Clone(fixture.Radio);
        var track = radioDuplicateTrack["radio"]!["trackIds"]![0]!.GetValue<string>();
        radioDuplicateTrack["radio"]!["trackIds"] = new JsonArray(track, track);
        AssertRejects(radioDuplicateTrack, fixture.Inventory);

        var radioUnknownTrack = Clone(fixture.Radio);
        radioUnknownTrack["radio"]!["trackIds"] = new JsonArray("asset:missing.mp3");
        AssertRejects(radioUnknownTrack, fixture.Inventory);
    }

    [Fact]
    public void Canonical_file_loading_rejects_missing_oversized_and_noncanonical_files()
    {
        var fixture = CreateFixture();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        Assert.Throws<FileNotFoundException>(
            () => ContentPackManifest.LoadFromFile(missing, fixture.Inventory));

        var path = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-pack-boundary-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, new string('x', ContentPackManifest.MaximumManifestBytes + 1));
            Assert.Throws<InvalidDataException>(
                () => ContentPackManifest.LoadFromFile(path, fixture.Inventory));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    [InlineData("utf32-le")]
    [InlineData("invalid-utf8")]
    public void File_loading_and_canonical_checks_reject_non_utf8_or_bom_hidden_bytes(string encoding)
    {
        var fixture = CreateFixture();
        var canonical = Parse(fixture.Core, fixture.Inventory).RenderCanonical();
        Encoding encoder = encoding switch
        {
            "utf8-bom" => new UTF8Encoding(true),
            "utf16-le" => new UnicodeEncoding(false, true),
            "utf16-be" => new UnicodeEncoding(true, true),
            "utf32-le" => new UTF32Encoding(false, true),
            _ => Encoding.UTF8,
        };
        var bytes = encoding == "invalid-utf8"
            ? new byte[] { 0xff, 0xfe, 0x80 }
            : encoder.GetPreamble().Concat(encoder.GetBytes(canonical)).ToArray();
        var path = Path.Combine(Path.GetTempPath(), "vibesnake-pack-encoding-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.Throws<InvalidDataException>(() => ContentPackManifest.LoadFromFile(path, fixture.Inventory));
            Assert.Throws<InvalidDataException>(() => ContentPackManifest.CheckCanonicalFile(path, fixture.Inventory));
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(canonical));
            Assert.Equal(canonical, ContentPackManifest.CheckCanonicalFile(path, fixture.Inventory).RenderCanonical());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validated_manifest_collections_cannot_be_modified_through_mutable_interfaces()
    {
        var fixture = CreateFixture();
        var radio = Parse(fixture.Radio, fixture.Inventory);
        var before = radio.RenderCanonical();
        var files = Assert.IsAssignableFrom<IList<ContentPackFile>>(radio.Files);
        var dependencies = Assert.IsAssignableFrom<IList<ContentPackDependency>>(radio.Dependencies);
        var credits = Assert.IsAssignableFrom<IList<ContentPackCredit>>(radio.Credits);
        var tracks = Assert.IsAssignableFrom<IList<string>>(radio.Radio!.TrackIds);
        Assert.Throws<NotSupportedException>(() => files[0] = files[0] with { Sha256 = new string('f', 64) });
        Assert.Throws<NotSupportedException>(() => dependencies.Clear());
        Assert.Throws<NotSupportedException>(() => credits[0] = credits[0] with { License = "changed" });
        Assert.Throws<NotSupportedException>(() => tracks[0] = "asset:unapproved.mp3");
        fixture.Radio["displayName"] = "changed after validation";
        Files(fixture.Radio).Clear();
        Assert.Equal(before, radio.RenderCanonical());
    }

    [Theory]
    [InlineData("dependencies", ContentPackManifest.MaximumDependencies)]
    [InlineData("credits", ContentPackManifest.MaximumCredits)]
    [InlineData("files", ContentPackManifest.MaximumFiles)]
    public void Rejects_collection_overflow_before_inspecting_untrusted_entries(string field, int maximum)
    {
        var fixture = CreateFixture();
        var entries = new JsonArray();
        for (var index = 0; index <= maximum; index++)
        {
            entries.Add(new JsonObject());
        }
        fixture.Core[field] = entries;
        var error = Assert.Throws<InvalidDataException>(() => Parse(fixture.Core, fixture.Inventory));
        Assert.Contains(maximum.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("description", "surrogate-text")]
    [InlineData("version", "semver-overflow")]
    [InlineData("schemaVersion", "boolean")]
    [InlineData("rules-version", "integer-overflow")]
    public void Enforces_native_numeric_and_utf16_text_bounds(string field, string value)
    {
        var fixture = CreateFixture();
        switch (value)
        {
            case "surrogate-text":
                fixture.Core[field] = string.Concat(Enumerable.Repeat("\U00010000", 257));
                break;
            case "semver-overflow":
                fixture.Core[field] = "2147483648.0.0";
                break;
            case "boolean":
                fixture.Core[field] = true;
                break;
            default:
                fixture.Core["compatibility"]!["ruleset"]!["minInclusive"] = 2147483648L;
                break;
        }
        AssertRejects(fixture.Core, fixture.Inventory);
    }

    [Theory]
    [InlineData("sha256", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    [InlineData("bytes", "99")]
    [InlineData("mediaType", "text/plain")]
    [InlineData("role", "wrong-role")]
    [InlineData("runtimeUse", "required")]
    public void Rejects_validly_typed_file_metadata_that_drifts_from_inventory(string field, string value)
    {
        var fixture = CreateFixture();
        var file = FileAt(fixture.Core, field == "runtimeUse" ? 1 : 0);
        file[field] = field == "bytes" ? JsonValue.Create(99) : JsonValue.Create(value);
        var error = Assert.Throws<InvalidDataException>(() => Parse(fixture.Core, fixture.Inventory));
        Assert.Contains("metadata does not match inventory", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("duplicateOf", "asset:other")]
    [InlineData("integrityStatus", "invalid")]
    [InlineData("shipStatus", "blocked")]
    [InlineData("exportEligible", "false")]
    [InlineData("rights.status", "unverified")]
    public void Rejects_inventory_approval_evidence_drift(string field, string value)
    {
        var fixture = CreateFixture();
        var asset = fixture.InventoryJson["assets"]![0]!;
        if (field == "rights.status")
        {
            asset["rights"]!["status"] = value;
        }
        else
        {
            asset[field] = field == "exportEligible" ? JsonValue.Create(false) : JsonValue.Create(value);
        }
        Assert.Throws<InvalidDataException>(() =>
        {
            var changedInventory = ContentInventory.Parse(ToJson(fixture.InventoryJson));
            Parse(fixture.Core, changedInventory);
        });
    }

    [Theory]
    [InlineData("source")]
    [InlineData("license")]
    [InlineData("attribution")]
    [InlineData("reviewEvidence")]
    public void Every_credit_field_must_reproduce_cleared_inventory_rights(string field)
    {
        var fixture = CreateFixture();
        Credits(fixture.Core)[0]![field] = "changed-rights-evidence";
        var error = Assert.Throws<InvalidDataException>(() => Parse(fixture.Core, fixture.Inventory));
        Assert.Contains("does not match inventory rights", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolver_never_resurrects_a_duplicate_id_when_one_claim_is_invalid(bool invalidFirst)
    {
        var fixture = CreateFixture();
        var valid = ToJson(fixture.Radio);
        FileAt(fixture.Radio, 0)["sha256"] = new string('f', 64);
        var invalid = ToJson(fixture.Radio);
        var resolution = ContentPackResolver.Resolve(ToJson(fixture.Core),
            invalidFirst ? [invalid, valid, valid] : [valid, invalid, valid], fixture.Inventory, "0.3.0");
        Assert.True(resolution.CoreReady);
        Assert.Empty(resolution.AcceptedOptional);
        Assert.Equal("invalid-pack", resolution.RejectedOptional["vibesnake.radio.flow-signal"].Code);
    }

    [Fact]
    public void Resolver_keeps_core_ready_without_optional_content()
    {
        var fixture = CreateFixture();
        var resolution = ContentPackResolver.Resolve(ToJson(fixture.Core), [], fixture.Inventory, "0.3.0");
        Assert.True(resolution.CoreReady);
        Assert.Empty(resolution.AcceptedOptional);
        Assert.Empty(resolution.RejectedOptional);
    }

    [Fact]
    public void Resolver_treats_invalid_or_wrong_kind_core_as_fatal()
    {
        var fixture = CreateFixture();
        FileAt(fixture.Core, 0)["sha256"] = new string('f', 64);
        Assert.Throws<InvalidDataException>(() => ContentPackResolver.Resolve(
            ToJson(fixture.Core), [ToJson(fixture.Radio)], fixture.Inventory, "0.3.0"));
        Assert.Throws<InvalidDataException>(() => ContentPackResolver.Resolve(
            ToJson(fixture.Radio), [], fixture.Inventory, "0.3.0"));
    }

    private static Fixture CreateFixture()
    {
        var coreCredit = Credit(
            "core-rights",
            "project-owned fixture",
            "MIT",
            "none",
            "fixture approval record");
        var radioCredit = Credit(
            "radio-rights",
            "licensed fixture",
            "CC-BY-4.0",
            "Fixture Artist",
            "fixture license review");
        var coreConfig = Asset(
            "config/core.json",
            ContentPackManifest.CorePackId,
            "core-config",
            "required",
            new string('1', 64),
            coreCredit,
            "application/json",
            10);
        var coreImage = Asset(
            "images/logo.png",
            ContentPackManifest.CorePackId,
            "core-image",
            "optional",
            new string('2', 64),
            coreCredit,
            "image/png",
            20);
        var radioTrack = Asset(
            "audio/radio/flow/track-01.mp3",
            "vibesnake.radio.flow-signal",
            "radio-track",
            "optional",
            new string('3', 64),
            radioCredit,
            "audio/mpeg",
            30);
        var assets = new JsonArray(coreConfig, coreImage, radioTrack);
        var inventoryJson = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["assetRoot"] = "assets",
            ["policySha256"] = PolicyHash,
            ["fileCount"] = assets.Count,
            ["assets"] = assets,
        };
        var inventory = ContentInventory.Parse(ToJson(inventoryJson));

        var core = ManifestBase(
            ContentPackManifest.CorePackId,
            "core",
            "Vibe Snake Core",
            [FileEntry(coreConfig, "core-rights"), FileEntry(coreImage, "core-rights")],
            [coreCredit.DeepClone()],
            []);
        core["radio"] = null;

        var radio = ManifestBase(
            "vibesnake.radio.flow-signal",
            "radio",
            "The Flow Signal",
            [FileEntry(radioTrack, "radio-rights")],
            [radioCredit.DeepClone()],
            [new JsonObject
            {
                ["id"] = ContentPackManifest.CorePackId,
                ["minInclusive"] = "1.0.0",
                ["maxExclusive"] = "2.0.0",
            }]);
        radio["radio"] = new JsonObject
        {
            ["stationId"] = "flow_signal",
            ["stationName"] = "The Flow Signal",
            ["trackIds"] = new JsonArray(radioTrack["id"]!.GetValue<string>()),
        };
        return new Fixture(inventory, core, radio, inventoryJson);
    }

    private static JsonObject ManifestBase(
        string id,
        string kind,
        string displayName,
        JsonNode[] files,
        JsonNode[] credits,
        JsonNode[] dependencies) => new()
        {
            ["schemaVersion"] = 1,
            ["id"] = id,
            ["version"] = "1.0.0",
            ["kind"] = kind,
            ["displayName"] = displayName,
            ["description"] = "Qualified fixture content.",
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
            ["files"] = new JsonArray(files),
            ["credits"] = new JsonArray(credits),
        };

    private static JsonObject Asset(
        string path,
        string packId,
        string role,
        string runtimeUse,
        string sha256,
        JsonObject credit,
        string mediaType,
        int bytes) => new()
        {
            ["id"] = $"asset:{path}",
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
                ["source"] = credit["source"]!.GetValue<string>(),
                ["license"] = credit["license"]!.GetValue<string>(),
                ["attribution"] = credit["attribution"]!.GetValue<string>(),
                ["reviewNote"] = credit["reviewEvidence"]!.GetValue<string>(),
            },
            ["duplicateOf"] = null,
        };

    private static JsonObject Credit(
        string id,
        string source,
        string license,
        string attribution,
        string reviewEvidence) => new()
        {
            ["id"] = id,
            ["source"] = source,
            ["license"] = license,
            ["attribution"] = attribution,
            ["reviewEvidence"] = reviewEvidence,
        };

    private static JsonObject FileEntry(JsonObject asset, string creditId) => new()
    {
        ["id"] = asset["id"]!.GetValue<string>(),
        ["path"] = asset["path"]!.GetValue<string>(),
        ["mediaType"] = asset["mediaType"]!.GetValue<string>(),
        ["bytes"] = asset["bytes"]!.GetValue<int>(),
        ["sha256"] = asset["sha256"]!.GetValue<string>(),
        ["role"] = asset["role"]!.GetValue<string>(),
        ["runtimeUse"] = asset["runtimeUse"]!.GetValue<string>(),
        ["creditId"] = creditId,
    };

    private static ContentPackManifest Parse(JsonObject document, ContentInventory inventory) =>
        ContentPackManifest.Parse(ToJson(document), inventory);

    private static void AssertRejects(JsonObject document, ContentInventory inventory) =>
        Assert.Throws<InvalidDataException>(() => Parse(document, inventory));

    private static JsonObject Clone(JsonObject value) =>
        (JsonObject)value.DeepClone();

    private static JsonArray Files(JsonObject manifest) =>
        manifest["files"]!.AsArray();

    private static JsonObject FileAt(JsonObject manifest, int index) =>
        Files(manifest)[index]!.AsObject();

    private static JsonArray Credits(JsonObject manifest) =>
        manifest["credits"]!.AsArray();

    private static JsonArray Dependencies(JsonObject manifest) =>
        manifest["dependencies"]!.AsArray();

    private static string ToJson(JsonNode document) =>
        document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private sealed record Fixture(
        ContentInventory Inventory,
        JsonObject Core,
        JsonObject Radio,
        JsonObject InventoryJson);
}
