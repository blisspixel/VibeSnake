using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class ContentPackToolTests
{
    private const string CorePackId = "vibesnake.core";
    private const string RadioPackId = "vibesnake.radio.flow-signal";
    private const string RadioAssetId = "asset:audio/radio/flow_signal_track.mp3";
    private const string OtherRadioAssetId = "asset:audio/radio/other_track.mp3";
    private const string PythonZipHex =
        "504b03041400000000000000210043bfa6a30200000002000000090000007061636b2e6a736f6e7b7d"
        + "504b010214031400000000000000210043bfa6a30200000002000000090000000000000000000000a481000000007061636b2e6a736f6e"
        + "504b0506000000000100010037000000290000000000";

    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly string[] EvidenceFieldNames =
    [
        "curationDecisionStatus",
        "curationSha256",
        "inventorySha256",
        "kind",
        "manifestSha256",
        "packBytes",
        "packFileName",
        "packId",
        "packSha256",
        "packVersion",
        "passed",
        "releaseApproved",
        "schemaVersion",
        "stationId",
        "stationName",
        "trackCount",
        "trackIds",
    ];

    [Fact]
    public void Stored_archive_matches_the_measured_python_zip()
    {
        var archive = RadioPackAssemblyCheck.RenderStoredArchive([("pack.json", "{}"u8.ToArray())]);

        Assert.Equal(PythonZipHex, Convert.ToHexStringLower(archive));
        AssertStoredZip(archive, "pack.json");
        Assert.Throws<InvalidDataException>(
            () => RadioPackAssemblyCheck.RenderStoredArchive([("", Array.Empty<byte>())]));
        Assert.Throws<InvalidDataException>(
            () => RadioPackAssemblyCheck.RenderStoredArchive([("\\pack.json", Array.Empty<byte>())]));
        Assert.Throws<InvalidDataException>(
            () => RadioPackAssemblyCheck.RenderStoredArchive([("/pack.json", Array.Empty<byte>())]));
        Assert.Throws<InvalidDataException>(
            () => RadioPackAssemblyCheck.RenderStoredArchive([("caf\u00e9.json", Array.Empty<byte>())]));
        Assert.Throws<InvalidDataException>(
            () => RadioPackAssemblyCheck.RenderStoredArchive([(new string('a', 70_000), Array.Empty<byte>())]));
        Assert.Throws<ArgumentNullException>(
            () => RadioPackAssemblyCheck.RenderStoredArchive(null!));
    }

    [Fact]
    public void Approved_radio_pack_is_deterministic_and_install_shaped()
    {
        WithRoot(firstRoot =>
        {
            var first = WriteFixture(firstRoot);
            WithRoot(secondRoot =>
            {
                var second = WriteFixture(secondRoot);
                var firstEvidence = RadioPackAssemblyCheck.Assemble(
                    first.Root,
                    first.RadioManifest,
                    first.Curation,
                    first.Inventory,
                    Path.Combine(first.Root, "output"));
                var secondEvidence = RadioPackAssemblyCheck.Assemble(
                    second.Root,
                    second.RadioManifest,
                    second.Curation,
                    second.Inventory,
                    Path.Combine(second.Root, "output"));

                Assert.Equal(firstEvidence.PackSha256, secondEvidence.PackSha256);
                Assert.Equal(1, firstEvidence.TrackCount);
                Assert.EndsWith(".vibesnake-pack.zip", firstEvidence.PackFileName, StringComparison.Ordinal);
                var output = Path.Combine(first.Root, "output");
                var archivePath = Path.Combine(output, firstEvidence.PackFileName);
                var archive = File.ReadAllBytes(archivePath);
                AssertStoredZip(archive, "pack.json", "audio/radio/flow_signal_track.mp3");
                Assert.Equal(File.ReadAllBytes(Path.Combine(output, "pack.json")), ReadZipEntry(archive, "pack.json"));
                var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "radio_pack_assembly.json"), Utf8));
                var fields = evidence.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
                Assert.Equal(EvidenceFieldNames, fields.Order(StringComparer.Ordinal));
                Assert.True(evidence.RootElement.GetProperty("releaseApproved").GetBoolean());
                Assert.True(evidence.RootElement.GetProperty("passed").GetBoolean());
                Assert.Equal("approved-radio-pack-assembly-v1", evidence.RootElement.GetProperty("kind").GetString());
                Assert.Equal(RadioPackId, evidence.RootElement.GetProperty("packId").GetString());
                Assert.Equal("The Flow Signal", evidence.RootElement.GetProperty("stationName").GetString());
                Assert.Equal(RadioAssetId, evidence.RootElement.GetProperty("trackIds")[0].GetString());
                Assert.Equal(firstEvidence.PackSha256, evidence.RootElement.GetProperty("packSha256").GetString());
                var checksums = File.ReadAllText(Path.Combine(output, "SHA256SUMS.txt"), Utf8);
                Assert.Contains(Sha256(archive) + "  " + firstEvidence.PackFileName, checksums, StringComparison.Ordinal);
                Assert.Contains("radio_pack_assembly.json", checksums, StringComparison.Ordinal);
                Assert.Contains("pack.json", checksums, StringComparison.Ordinal);

                var command = Run("radio-pack", second.Root, second.RadioManifest, Path.Combine(second.Root, "cli-output"));
                Assert.Equal(0, command.Code);
                Assert.Contains("Approved radio pack assembled: " + secondEvidence.PackFileName, command.Output, StringComparison.Ordinal);
                Assert.Contains(secondEvidence.PackSha256, command.Output, StringComparison.Ordinal);
                Assert.Equal(string.Empty, command.Error);
            });
        });
    }

    [Fact]
    public void Pending_curation_and_listening_decisions_block_assembly()
    {
        WithRoot(root =>
        {
            var fixture = WriteFixture(root);
            File.WriteAllText(
                fixture.Curation,
                File.ReadAllText(fixture.Curation, Utf8).Replace(
                    "\"decisionStatus\": \"approved-for-alpha-release\"",
                    "\"decisionStatus\": \"pending-human-listening-review\"",
                    StringComparison.Ordinal),
                Utf8);
            var status = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("approved-for-alpha-release", status.Message, StringComparison.Ordinal);

            WriteCuration(fixture, Station("flow_signal", Quote(RadioAssetId), string.Empty, string.Empty));
            var pending = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("pending listening", pending.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Unknown_or_unaccounted_radio_decisions_are_rejected()
    {
        WithRoot(root =>
        {
            var fixture = WriteFixture(root);
            WriteCuration(fixture, Station("flow_signal", string.Empty, Quote("asset:audio/radio/other.mp3"), string.Empty));
            var unknown = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("unknown or non-radio asset IDs", unknown.Message, StringComparison.Ordinal);

            WriteCuration(fixture, Station("flow_signal", string.Empty, string.Empty, string.Empty));
            var missing = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("every inventoried radio asset exactly once", missing.Message, StringComparison.Ordinal);

            WriteCuration(
                fixture,
                Station("flow_signal", string.Empty, Quote(RadioAssetId), string.Empty)
                + "    ,\n"
                + Station("duplicate_station", string.Empty, Quote(RadioAssetId), string.Empty));
            var collision = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("multiple stations", collision.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Stale_inventory_and_existing_output_fail_closed()
    {
        WithRoot(root =>
        {
            var fixture = WriteFixture(root);
            var track = Path.Combine(root, "assets", "audio", "radio", "flow_signal_track.mp3");
            var bytes = File.ReadAllBytes(track);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(track, bytes);
            var stale = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("stale", stale.Message, StringComparison.Ordinal);

            File.WriteAllBytes(track, TrackBytes());
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(output);
            var existing = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.Assemble(
                    fixture.Root,
                    fixture.RadioManifest,
                    fixture.Curation,
                    fixture.Inventory,
                    output));
            Assert.Contains("must not already exist", existing.Message, StringComparison.Ordinal);

            var copy = Path.Combine(root, "inventory-copy.json");
            File.WriteAllText(copy, "{}", Utf8);
            var mismatch = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.Assemble(
                    fixture.Root,
                    fixture.RadioManifest,
                    fixture.Curation,
                    copy,
                    Path.Combine(root, "other-output")));
            Assert.Contains("stale", mismatch.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Duplicate_fields_oversized_inputs_and_cleanup_are_rejected()
    {
        WithRoot(root =>
        {
            var fixture = WriteFixture(root);
            var source = File.ReadAllText(fixture.Curation, Utf8);
            File.WriteAllText(
                fixture.Curation,
                source.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schemaVersion\": 1,", StringComparison.Ordinal),
                Utf8);
            var duplicate = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("repeats JSON field", duplicate.Message, StringComparison.Ordinal);

            File.WriteAllText(fixture.Curation, source, Utf8);
            var oversized = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.Assemble(
                    fixture.Root,
                    fixture.RadioManifest,
                    fixture.Curation,
                    fixture.Inventory,
                    Path.Combine(root, "curation-output"),
                    maximumCurationBytes: 1));
            Assert.Contains("byte limit", oversized.Message, StringComparison.Ordinal);

            var huge = Path.Combine(root, "huge.json");
            using (var stream = new FileStream(huge, FileMode.CreateNew))
            {
                stream.SetLength(ContentPackManifest.MaximumManifestBytes + 1);
            }

            var manifestLimit = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.Assemble(
                    fixture.Root,
                    huge,
                    fixture.Curation,
                    fixture.Inventory,
                    Path.Combine(root, "huge-output")));
            Assert.Contains("byte limit", manifestLimit.Message, StringComparison.Ordinal);

            var cleanup = Path.Combine(root, "cleanup-output");
            var removed = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.Assemble(
                    fixture.Root,
                    fixture.RadioManifest,
                    fixture.Curation,
                    fixture.Inventory,
                    cleanup,
                    failAfterOutputCreation: true));
            Assert.Contains("could not assemble radio pack", removed.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(cleanup));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RadioPackAssemblyCheck.Assemble(
                    fixture.Root,
                    fixture.RadioManifest,
                    fixture.Curation,
                    fixture.Inventory,
                    Path.Combine(root, "negative-output"),
                    maximumInstalledBytes: -1));
        });
    }

    [Fact]
    public void Installed_and_compressed_budgets_fail_before_leaving_output()
    {
        WithRoot(root =>
        {
            var installed = WriteFixture(Path.Combine(root, "installed"));
            var installedOutput = Path.Combine(installed.Root, "output");
            var installedFailure = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.Assemble(
                    installed.Root,
                    installed.RadioManifest,
                    installed.Curation,
                    installed.Inventory,
                    installedOutput,
                    maximumInstalledBytes: 1));
            Assert.Contains("installed-size budget", installedFailure.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(installedOutput));

            var compressed = WriteFixture(Path.Combine(root, "compressed"));
            var compressedOutput = Path.Combine(compressed.Root, "output");
            var compressedFailure = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.Assemble(
                    compressed.Root,
                    compressed.RadioManifest,
                    compressed.Curation,
                    compressed.Inventory,
                    compressedOutput,
                    maximumCompressedBytes: 1));
            Assert.Contains("compressed-size budget", compressedFailure.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(compressedOutput));
        });
    }

    [Fact]
    public void Track_identity_policy_hash_and_pack_kind_fail_closed()
    {
        WithRoot(root =>
        {
            var paired = WriteFixture(root, radioTracks: 2);
            RewriteRadio(paired, trackIds: [RadioAssetId]);
            WriteCuration(
                paired,
                Station("flow_signal", string.Empty, Quote(RadioAssetId), Quote(OtherRadioAssetId)));
            var files = Assert.Throws<InvalidDataException>(() => Assemble(paired));
            Assert.Contains("every packaged radio-track file", files.Message, StringComparison.Ordinal);

            RewriteRadio(paired, trackIds: [RadioAssetId, OtherRadioAssetId]);
            var decisions = Assert.Throws<InvalidDataException>(() => Assemble(paired));
            Assert.Contains("approved listening decisions", decisions.Message, StringComparison.Ordinal);

            var single = WriteFixture(Path.Combine(root, "single"));
            var curation = File.ReadAllText(single.Curation, Utf8);
            File.WriteAllText(
                single.Curation,
                curation.Replace(single.PolicySha256, new string('a', 64), StringComparison.Ordinal),
                Utf8);
            var hash = Assert.Throws<InvalidDataException>(() => Assemble(single));
            Assert.Contains("policy hash", hash.Message, StringComparison.Ordinal);

            var core = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.Assemble(
                    single.Root,
                    single.CoreManifest,
                    single.Curation,
                    single.Inventory,
                    Path.Combine(single.Root, "core-output")));
            Assert.Contains("requires one radio manifest", core.Message, StringComparison.Ordinal);

            WriteCuration(single, Station("flow_signal", Quote(RadioAssetId), Quote(RadioAssetId), string.Empty));
            var overlap = Assert.Throws<InvalidDataException>(() => Assemble(single));
            Assert.Contains("decisions must be disjoint", overlap.Message, StringComparison.Ordinal);

            WriteCuration(single, Station("BAD", string.Empty, Quote(RadioAssetId), string.Empty));
            var station = Assert.Throws<InvalidDataException>(() => Assemble(single));
            Assert.Contains(".id is invalid", station.Message, StringComparison.Ordinal);

            WriteCuration(
                single,
                Station("flow_signal", string.Empty, Quote(RadioAssetId), string.Empty)
                + "    ,\n"
                + Station("flow_signal", string.Empty, string.Empty, string.Empty));
            var repeated = Assert.Throws<InvalidDataException>(() => Assemble(single));
            Assert.Contains("repeats station ID", repeated.Message, StringComparison.Ordinal);

            WriteCuration(
                single,
                Station("flow_signal", string.Empty, Quote(RadioAssetId), string.Empty),
                coreApproved: RadioAssetId);
            var coreMusic = Assert.Throws<InvalidDataException>(() => Assemble(single));
            Assert.Contains("unknown or radio asset IDs", coreMusic.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Malformed_curation_schema_is_rejected()
    {
        WithRoot(root =>
        {
            var fixture = WriteFixture(root);
            File.WriteAllText(fixture.Curation, "{\"schemaVersion\": 1}\n", Utf8);
            var fields = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("exact schema 1 fields", fields.Message, StringComparison.Ordinal);

            File.WriteAllText(fixture.Curation, "[]\n", Utf8);
            var array = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("exact schema 1 fields", array.Message, StringComparison.Ordinal);

            File.WriteAllText(fixture.Curation, "{", Utf8);
            var unreadable = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("unreadable", unreadable.Message, StringComparison.Ordinal);

            Directory.CreateDirectory(Path.Combine(root, "curation-directory"));
            var directory = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.Assemble(
                    fixture.Root,
                    fixture.RadioManifest,
                    Path.Combine(root, "curation-directory"),
                    fixture.Inventory,
                    Path.Combine(root, "directory-output")));
            Assert.Contains("unreadable", directory.Message, StringComparison.Ordinal);

            WriteCuration(fixture, string.Empty);
            var stations = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("nonempty array", stations.Message, StringComparison.Ordinal);

            File.WriteAllText(
                fixture.Curation,
                File.ReadAllText(fixture.Curation, Utf8).Replace(
                    "\"planId\": \"vibesnake-content-curation-v1\"",
                    "\"planId\": \"other-plan\"",
                    StringComparison.Ordinal),
                Utf8);
            var identity = Assert.Throws<InvalidDataException>(() => Assemble(fixture));
            Assert.Contains("identity is unsupported", identity.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Content_packs_qualify_a_core_and_compatible_radio_pack()
    {
        WithRoot(root =>
        {
            var fixture = WriteFixture(root);
            var direct = ContentPackQualificationCheck.Qualify(
                fixture.Root,
                [fixture.CoreManifest, fixture.RadioManifest],
                null,
                "0.3.0",
                RulesetIdentity.CurrentId,
                RulesetIdentity.CurrentVersion);
            Assert.True(direct.Passed);
            Assert.Contains("core=vibesnake.core optional=1", direct.Lines[0], StringComparison.Ordinal);

            var copy = Path.Combine(root, "inventory-copy.json");
            File.Copy(fixture.Inventory, copy);
            var command = Run(
                "content-packs",
                fixture.Root,
                fixture.RadioManifest,
                fixture.CoreManifest,
                "--inventory",
                copy,
                "--game-version",
                "0.3.0",
                "--ruleset-id",
                RulesetIdentity.CurrentId,
                "--ruleset-version",
                "4");
            Assert.Equal(0, command.Code);
            Assert.Contains("optional=1", command.Output, StringComparison.Ordinal);
            Assert.Equal(string.Empty, command.Error);

            var coreOnly = ContentPackQualificationCheck.Qualify(
                fixture.Root,
                [fixture.CoreManifest],
                fixture.Inventory,
                "0.3.0",
                RulesetIdentity.CurrentId,
                4);
            Assert.Contains("optional=0", coreOnly.Lines[0], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Core_rejection_hides_optional_isolation_and_incompatible_radio_is_reported()
    {
        WithRoot(root =>
        {
            var fixture = WriteFixture(root);
            var core = Run(
                "content-packs",
                fixture.Root,
                fixture.CoreManifest,
                fixture.RadioManifest,
                "--game-version",
                "1.0.0");
            Assert.Equal(1, core.Code);
            Assert.Contains("Content pack core rejected:", core.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("rejected optional", core.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("core-unavailable", core.Output, StringComparison.Ordinal);
            Assert.Equal(string.Empty, core.Error);

            RewriteRadio(fixture, rulesetMinimum: 5);
            var radio = Run("content-packs", fixture.Root, fixture.CoreManifest, fixture.RadioManifest);
            Assert.Equal(1, radio.Code);
            Assert.Contains("Content pack qualification rejected optional content:", radio.Output, StringComparison.Ordinal);
            Assert.Contains(RadioPackId + ": rules-version-too-old:", radio.Output, StringComparison.Ordinal);
            Assert.Equal(string.Empty, radio.Error);

            var duplicate = Run(
                "content-packs",
                fixture.Root,
                fixture.CoreManifest,
                fixture.RadioManifest,
                fixture.RadioManifest);
            Assert.Equal(1, duplicate.Code);
            Assert.Contains("invalid-pack", duplicate.Output, StringComparison.Ordinal);
            Assert.Contains("Duplicate optional pack id", duplicate.Output, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Invalid_pack_sets_and_stale_inventory_fail_closed()
    {
        WithRoot(root =>
        {
            var fixture = WriteFixture(root);
            var none = Assert.Throws<InvalidDataException>(
                () => ContentPackQualificationCheck.Qualify(
                    fixture.Root,
                    [],
                    null,
                    "0.3.0",
                    RulesetIdentity.CurrentId,
                    4));
            Assert.Contains("found 0", none.Message, StringComparison.Ordinal);

            var twoCores = Run("content-packs", fixture.Root, fixture.CoreManifest, fixture.CoreManifest);
            Assert.Equal(1, twoCores.Code);
            Assert.Contains("expected exactly one core manifest, found 2", twoCores.Error, StringComparison.Ordinal);

            File.AppendAllText(fixture.RadioManifest, " ", Utf8);
            var canonical = Run("content-packs", fixture.Root, fixture.CoreManifest, fixture.RadioManifest);
            Assert.Equal(1, canonical.Code);
            Assert.Contains("not canonically encoded", canonical.Error, StringComparison.Ordinal);
            Assert.Contains("Content pack qualification failed:", canonical.Error, StringComparison.Ordinal);

            var track = Path.Combine(root, "assets", "audio", "radio", "flow_signal_track.mp3");
            var bytes = File.ReadAllBytes(track);
            bytes[^1] ^= 0x02;
            File.WriteAllBytes(track, bytes);
            var stale = Run("content-packs", fixture.Root, fixture.CoreManifest);
            Assert.Equal(1, stale.Code);
            Assert.Contains("stale", stale.Error, StringComparison.Ordinal);

            File.WriteAllBytes(track, TrackBytes());
            var missing = Assert.Throws<InvalidDataException>(
                () => ContentPackQualificationCheck.Qualify(
                    fixture.Root,
                    [fixture.CoreManifest],
                    Path.Combine(root, "missing-inventory.json"),
                    "0.3.0",
                    RulesetIdentity.CurrentId,
                    4));
            Assert.Contains("does not exist", missing.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentNullException>(
                () => ContentPackQualificationCheck.Qualify(
                    fixture.Root,
                    null!,
                    null,
                    "0.3.0",
                    RulesetIdentity.CurrentId,
                    4));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ContentPackQualificationCheck.Qualify(
                    fixture.Root,
                    [fixture.CoreManifest],
                    null,
                    "0.3.0",
                    RulesetIdentity.CurrentId,
                    0));
        });
    }

    [Fact]
    public void Command_usage_rejects_incomplete_content_and_radio_arguments()
    {
        WithRoot(root =>
        {
            var fixture = WriteFixture(root);
            Assert.Equal(2, Run("content-packs").Code);
            Assert.Equal(2, Run("content-packs", fixture.Root, fixture.CoreManifest, "--ruleset-version", "0").Code);
            Assert.Equal(2, Run("content-packs", fixture.Root, fixture.CoreManifest, "--ruleset-version", "no").Code);
            Assert.Equal(2, Run("content-packs", fixture.Root, fixture.CoreManifest, "--game-version", " ").Code);
            Assert.Equal(2, Run("content-packs", fixture.Root, fixture.CoreManifest, "--not-a-flag", "x").Code);
            Assert.Equal(2, Run("content-packs", fixture.Root, fixture.CoreManifest, "--inventory").Code);
            Assert.Equal(2, Run("radio-pack").Code);
            Assert.Equal(2, Run("radio-pack", fixture.Root, fixture.RadioManifest).Code);
            Assert.Equal(2, Run("radio-pack", fixture.Root, fixture.RadioManifest, Path.Combine(root, "out"), "--nope", "x").Code);
            var invalidPath = Run("radio-pack", fixture.Root, fixture.RadioManifest, "bad\u0000path");
            Assert.Equal(2, invalidPath.Code);
            Assert.Contains("Radio pack input or output path is invalid.", invalidPath.Error, StringComparison.Ordinal);

            var failed = Run("radio-pack", fixture.Root, fixture.CoreManifest, Path.Combine(root, "radio-error"));
            Assert.Equal(1, failed.Code);
            Assert.Contains("Radio pack assembly failed:", failed.Error, StringComparison.Ordinal);
            Assert.Contains("requires one radio manifest", failed.Error, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Asset_paths_reject_escape_directories_links_and_payload_drift()
    {
        WithRoot(root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "assets", "audio", "radio"));
            var escape = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.ResolvePackAsset(root, "assets", "../outside.txt"));
            Assert.Contains("escapes", escape.Message, StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(() => RadioPackAssemblyCheck.ResolvePackAsset(root, "assets", "."));
            Assert.Throws<InvalidDataException>(() => RadioPackAssemblyCheck.ResolvePackAsset(root, "assets", ".."));
            Assert.Throws<InvalidDataException>(() => RadioPackAssemblyCheck.ResolvePackAsset(root, "assets", "foo/../../outside.txt"));
            Assert.Throws<InvalidDataException>(() => RadioPackAssemblyCheck.ResolvePackAsset(root, "../outside", "file.txt"));
            var missing = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.ResolvePackAsset(root, "assets", "audio/missing.mp3"));
            Assert.Contains("missing or escapes", missing.Message, StringComparison.Ordinal);

            var directory = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.ResolvePackAsset(root, "assets", "audio/radio"));
            Assert.Contains("not a regular file", directory.Message, StringComparison.Ordinal);

            var target = Path.Combine(root, "outside.mp3");
            File.WriteAllBytes(target, TrackBytes());
            var link = Path.Combine(root, "assets", "audio", "radio", "linked.mp3");
            if (TryCreateFileLink(link, target))
            {
                var symbolic = Assert.Throws<InvalidDataException>(
                    () => RadioPackAssemblyCheck.ResolvePackAsset(root, "assets", "audio/radio/linked.mp3"));
                Assert.Contains("symbolic link", symbolic.Message, StringComparison.Ordinal);
            }

            var fixture = WriteFixture(Path.Combine(root, "payload"));
            var asset = fixture.ContentInventory.Assets.Single(item => item.Id == RadioAssetId);
            var assetPath = Path.Combine(fixture.Root, "assets", asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var payload = File.ReadAllBytes(assetPath);
            payload[^1] ^= 0x04;
            File.WriteAllBytes(assetPath, payload);
            var drifted = Assert.Throws<InvalidDataException>(
                () => RadioPackAssemblyCheck.ReadMatchingPayload(assetPath, asset.Bytes, asset.Sha256, asset.RelativePath));
            Assert.Contains("changed after inventory validation", drifted.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => RadioPackAssemblyCheck.ResolvePackAsset(" ", "assets", "file.txt"));
        });
    }

    private static RadioPackAssemblyEvidence Assemble(Fixture fixture) =>
        RadioPackAssemblyCheck.Assemble(
            fixture.Root,
            fixture.RadioManifest,
            fixture.Curation,
            fixture.Inventory,
            Path.Combine(fixture.Root, "output-" + Guid.NewGuid().ToString("N")));

    private static (int Code, string Output, string Error) Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = RepositoryCheckCommand.Run(arguments, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private static Fixture WriteFixture(string root, int radioTracks = 1, int rulesetMinimum = 4)
    {
        Directory.CreateDirectory(root);
        WriteBytes(root, "assets/text/core.txt", "core fixture\n"u8.ToArray());
        WriteBytes(root, "assets/audio/radio/flow_signal_track.mp3", TrackBytes());
        var rules = new List<JsonObject>
        {
            Rule(
                "approved-core-text",
                "text/core.txt",
                "core-text",
                CorePackId,
                "required",
                ClearedRights("MIT", "none", "fixture rights are explicit")),
            Rule(
                "approved-flow-signal-radio",
                "audio/radio/flow_signal_track.mp3",
                "radio-track",
                RadioPackId,
                "optional",
                ClearedRights("Apache-2.0", "fixture rights", "fixture listening review passed")),
        };
        if (radioTracks == 2)
        {
            var otherTrack = TrackBytes();
            otherTrack[^1] ^= 0x01;
            WriteBytes(root, "assets/audio/radio/other_track.mp3", otherTrack);
            rules.Add(
                Rule(
                    "approved-other-radio",
                    "audio/radio/other_track.mp3",
                    "radio-track",
                    RadioPackId,
                    "optional",
                    ClearedRights("Apache-2.0", "fixture rights", "fixture listening review passed")));
        }

        WritePolicy(root, rules);
        var written = ContentInventoryCheck.Write(root);
        Assert.True(written.Passed, string.Join(Environment.NewLine, written.Failures));
        var inventoryPath = Path.Combine(root, "config", "content_inventory.json");
        var inventory = ContentInventory.LoadFromFile(inventoryPath);
        var coreAsset = inventory.Assets.Single(asset => asset.PackId == CorePackId);
        var radioAssets = inventory.Assets
            .Where(asset => asset.PackId == RadioPackId)
            .OrderBy(asset => asset.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var coreManifest = new ContentPackManifest(
            1,
            CorePackId,
            "1.0.0",
            ContentPackKind.Core,
            "Core Fixture",
            "Approved core fixture.",
            Compatibility(RulesetIdentity.CurrentVersion),
            new ContentPackInventoryBinding(1, inventory.AssetRoot, inventory.PolicySha256),
            [],
            [FileOf(coreAsset, "core-rights")],
            [CreditOf("core-rights", coreAsset)],
            null);
        var radioManifest = RadioManifest(inventory, radioAssets, radioAssets.Select(asset => asset.Id).ToArray(), rulesetMinimum);
        var corePath = Path.Combine(root, "config", "packs", "vibesnake.core.json");
        var radioPath = Path.Combine(root, "config", "packs", RadioPackId + ".json");
        WriteText(root, "config/packs/vibesnake.core.json", coreManifest.RenderCanonical());
        WriteText(root, "config/packs/" + RadioPackId + ".json", radioManifest.RenderCanonical());
        var curationPath = Path.Combine(root, "config", "content_curation_v1.json");
        File.WriteAllText(
            curationPath,
            Curation(inventory.PolicySha256, Station("flow_signal", string.Empty, QuotedIds(radioAssets.Select(asset => asset.Id)), string.Empty)),
            Utf8);
        return new Fixture(root, corePath, radioPath, curationPath, inventoryPath, inventory, inventory.PolicySha256);
    }

    private static void RewriteRadio(Fixture fixture, string[]? trackIds = null, int? rulesetMinimum = null)
    {
        var inventory = ContentInventory.LoadFromFile(fixture.Inventory);
        var radioAssets = inventory.Assets
            .Where(asset => asset.PackId == RadioPackId)
            .OrderBy(asset => asset.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var manifest = RadioManifest(
            inventory,
            radioAssets,
            trackIds ?? radioAssets.Select(asset => asset.Id).ToArray(),
            rulesetMinimum ?? RulesetIdentity.CurrentVersion);
        File.WriteAllText(fixture.RadioManifest, manifest.RenderCanonical(), Utf8);
    }

    private static ContentPackManifest RadioManifest(
        ContentInventory inventory,
        ContentInventoryAsset[] radioAssets,
        string[] trackIds,
        int rulesetMinimum) =>
        new(
            1,
            RadioPackId,
            "1.0.0",
            ContentPackKind.Radio,
            "The Flow Signal",
            "Approved optional radio fixture.",
            Compatibility(rulesetMinimum),
            new ContentPackInventoryBinding(1, inventory.AssetRoot, inventory.PolicySha256),
            [new ContentPackDependency(CorePackId, "1.0.0", "2.0.0")],
            radioAssets.Select(asset => FileOf(asset, "flow-signal-rights")).ToArray(),
            [CreditOf("flow-signal-rights", radioAssets[0])],
            new ContentPackRadio("flow_signal", "The Flow Signal", trackIds));

    private static ContentPackCompatibility Compatibility(int rulesetMinimum) =>
        new(
            new ContentPackVersionRange("0.3.0", "1.0.0"),
            new ContentPackRulesetRange(RulesetIdentity.CurrentId, rulesetMinimum, rulesetMinimum + 1));

    private static ContentPackFile FileOf(ContentInventoryAsset asset, string creditId) =>
        new(
            asset.Id,
            asset.RelativePath,
            asset.MediaType,
            asset.Bytes,
            asset.Sha256,
            asset.Role,
            asset.RuntimeUse,
            creditId);

    private static ContentPackCredit CreditOf(string id, ContentInventoryAsset asset) =>
        new(id, asset.Rights.Source, asset.Rights.License, asset.Rights.Attribution, asset.Rights.ReviewEvidence);

    private static void WriteCuration(Fixture fixture, string stations, string? coreApproved = null)
    {
        File.WriteAllText(fixture.Curation, Curation(fixture.PolicySha256, stations, coreApproved), Utf8);
    }

    private static string Curation(string policySha256, string stations, string? coreApproved = null) =>
        "{\n"
        + "  \"schemaVersion\": 1,\n"
        + "  \"planId\": \"vibesnake-content-curation-v1\",\n"
        + "  \"inventoryPolicySha256\": \"" + policySha256 + "\",\n"
        + "  \"decisionStatus\": \"approved-for-alpha-release\",\n"
        + "  \"coreMusic\": {\"pendingAssetIds\": [], \"approvedAssetIds\": ["
        + (coreApproved is null ? string.Empty : Quote(coreApproved))
        + "], \"rejectedAssetIds\": []},\n"
        + "  \"stations\": [\n"
        + stations
        + "  ]\n"
        + "}\n";

    private static string Station(string id, string pending, string approved, string rejected) =>
        "    {\n"
        + "      \"id\": \"" + id + "\",\n"
        + "      \"pendingAssetIds\": [" + pending + "],\n"
        + "      \"approvedAssetIds\": [" + approved + "],\n"
        + "      \"rejectedAssetIds\": [" + rejected + "]\n"
        + "    }\n";

    private static string QuotedIds(IEnumerable<string> ids) =>
        string.Join(", ", ids.Select(Quote));

    private static string Quote(string value) => "\"" + value + "\"";

    private static void WritePolicy(string root, IReadOnlyList<JsonObject> rules)
    {
        var policy = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["assetRoot"] = "assets",
            ["rules"] = new JsonArray(rules.Select(rule => (JsonNode?)rule).ToArray()),
        };
        WriteText(root, "config/content_policy.json", policy.ToJsonString() + "\n");
    }

    private static JsonObject Rule(
        string id,
        string pattern,
        string role,
        string packId,
        string runtimeUse,
        JsonObject rights) =>
        new()
        {
            ["id"] = id,
            ["patterns"] = new JsonArray(pattern),
            ["role"] = role,
            ["packId"] = packId,
            ["runtimeUse"] = runtimeUse,
            ["shipStatus"] = "approved",
            ["rights"] = rights,
        };

    private static JsonObject ClearedRights(string license, string attribution, string reviewNote) =>
        new()
        {
            ["status"] = "cleared",
            ["source"] = "test fixture",
            ["license"] = license,
            ["attribution"] = attribution,
            ["reviewNote"] = reviewNote,
        };

    private static byte[] TrackBytes()
    {
        var frame = Mp3Frame();
        var bytes = new byte[frame.Length * 2];
        frame.CopyTo(bytes, 0);
        frame.CopyTo(bytes, frame.Length);
        return bytes;
    }

    private static byte[] Mp3Frame()
    {
        var frame = new byte[(144 * 128_000) / 44_100];
        new byte[] { 0xff, 0xfb, 0x90, 0x64 }.CopyTo(frame, 0);
        return frame;
    }

    private static void AssertStoredZip(byte[] zip, params string[] names)
    {
        Assert.True(zip.Length >= 22);
        var eocd = zip.Length - 22;
        Assert.Equal(0x06054b50u, BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd, 4)));
        Assert.Equal(names.Length, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(eocd + 8, 2)));
        Assert.Equal(names.Length, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(eocd + 10, 2)));
        var centralSize = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd + 12, 4));
        var centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd + 16, 4));
        var cursor = (int)centralOffset;
        for (var index = 0; index < names.Length; index++)
        {
            Assert.Equal(0x02014b50u, BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(cursor, 4)));
            Assert.Equal((ushort)0x0314, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 4, 2)));
            Assert.Equal((ushort)20, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 6, 2)));
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 8, 2)));
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 10, 2)));
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 12, 2)));
            Assert.Equal((ushort)0x0021, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 14, 2)));
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 28, 2));
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 30, 2)));
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 32, 2)));
            Assert.Equal(0x81A40000u, BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(cursor + 38, 4)));
            var name = Encoding.ASCII.GetString(zip, cursor + 46, nameLength);
            Assert.Equal(names[index], name);
            var local = (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(cursor + 42, 4));
            Assert.Equal(0x04034b50u, BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(local, 4)));
            Assert.Equal((ushort)20, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(local + 4, 2)));
            Assert.Equal((ushort)0x0021, BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(local + 12, 2)));
            cursor += 46 + nameLength;
        }

        Assert.Equal(centralOffset + centralSize, (uint)cursor);
    }

    private static byte[] ReadZipEntry(byte[] zip, string name)
    {
        var eocd = zip.Length - 22;
        var centralOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd + 16, 4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(eocd + 10, 2));
        var cursor = centralOffset;
        for (var index = 0; index < count; index++)
        {
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(cursor + 28, 2));
            var entryName = Encoding.ASCII.GetString(zip, cursor + 46, nameLength);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(cursor + 24, 4));
            var local = (int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(cursor + 42, 4));
            var localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(local + 26, 2));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(local + 28, 2));
            if (entryName == name)
            {
                var dataOffset = local + 30 + localNameLength + extraLength;
                return zip.AsSpan(dataOffset, (int)size).ToArray();
            }

            cursor += 46 + nameLength;
        }

        throw new InvalidOperationException("missing zip entry " + name);
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void WriteText(string root, string relativePath, string value)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value, Utf8);
    }

    private static void WriteBytes(string root, string relativePath, byte[] value)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, value);
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-content-packs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed record Fixture(
        string Root,
        string CoreManifest,
        string RadioManifest,
        string Curation,
        string Inventory,
        ContentInventory ContentInventory,
        string PolicySha256);
}
