using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class UnsignedPreviewCheckTests
{
    private const string Version = "0.3.0-alpha.1";
    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TrackId = "asset:audio/radio/flow_signal_track.mp3";
    private const string TrackPath = "audio/radio/flow_signal_track.mp3";
    private const string PackName = "vibesnake.radio.flow-signal-1.0.0.vibesnake-pack.zip";

    private static readonly byte[] TrackBytes = "approved radio track"u8.ToArray();
    private static readonly string[] Platforms = ["windows-x64", "macos-universal", "linux-x64"];
    private static readonly string[] ShapeKinds = ["bool", "string", "empty", "null", "float", "negative"];
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };

    [Fact]
    public void Complete_alpha_matrix_assembles_explicit_unsigned_preview()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);

        var assembly = Assemble(fixture);

        Assert.Empty(assembly.Errors);
        using var evidence = JsonDocument.Parse(assembly.Json);
        var root = evidence.RootElement;
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.True(root.GetProperty("unsigned").GetBoolean());
        Assert.False(root.GetProperty("stablePublicationEligible").GetBoolean());
        Assert.Equal(Sha256(fixture.MatrixPath), root.GetProperty("releaseMatrixSha256").GetString());
        Assert.Equal(3, root.GetProperty("packages").GetArrayLength());
        Assert.Equal("flow_signal", root.GetProperty("radioPack").GetProperty("stationId").GetString());
        Assert.True(File.Exists(Path.Combine(fixture.OutputRoot, "unsigned_preview_manifest.json")));
        var checksums = File.ReadAllText(Path.Combine(fixture.OutputRoot, "SHA256SUMS.txt"), Utf8);
        Assert.Contains("-windows-x64-unsigned-preview.zip", checksums, StringComparison.Ordinal);
        Assert.Contains("-macos-universal-unsigned-preview.zip", checksums, StringComparison.Ordinal);
        Assert.Contains("-linux-x64-unsigned-preview.tar.gz", checksums, StringComparison.Ordinal);
        Assert.Contains(".vibesnake-pack.zip", checksums, StringComparison.Ordinal);
        Assert.DoesNotContain("qualification", checksums, StringComparison.Ordinal);
    }

    [Fact]
    public void Nonmatching_or_nonalpha_tag_is_rejected_without_output()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);

        var assembly = Assemble(fixture, "v0.3.0");

        Assert.Contains(assembly.Errors, error => error.Contains("tag must exactly match", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(evidence.RootElement.GetProperty("stablePublicationEligible").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public void Stable_version_cannot_use_unsigned_preview_path()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        File.WriteAllBytes(Path.Combine(fixture.VersionRoot, "VERSION"), "0.3.0\n"u8.ToArray());

        var assembly = Assemble(fixture, "v0.3.0");

        Assert.Contains(
            assembly.Errors,
            error => error.Contains("requires a canonical alpha", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public void Tampered_package_and_unexpected_input_file_are_rejected()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var windowsRoot = Path.Combine(fixture.ChannelRoot, "vibesnake-windows-x64-unsigned-channel-shape");
        var packagePath = Directory.GetFiles(windowsRoot, "*qualification.zip").Single();
        File.WriteAllBytes(packagePath, "tampered"u8.ToArray());
        File.WriteAllText(Path.Combine(windowsRoot, "unexpected.txt"), "unexpected\n", Utf8);

        var assembly = Assemble(fixture);

        Assert.Contains(assembly.Errors, error => error.Contains("byte count changed", StringComparison.Ordinal));
        Assert.Contains(assembly.Errors, error => error.Contains("unexpected file set", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public void Matrix_revision_and_provenance_must_match_complete_set()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        File.Delete(Path.Combine(fixture.ProvenanceRoot, "vibesnake-linux-x64-provenance", "attestation.jsonl"));

        var assembly = UnsignedPreviewCheck.Assemble(
            fixture.ChannelRoot,
            fixture.ProvenanceRoot,
            fixture.RadioRoot,
            fixture.MatrixPath,
            fixture.VersionRoot,
            "v" + Version,
            new string('b', 40),
            fixture.OutputRoot);

        Assert.Contains(assembly.Errors, error => error.Contains("matrix.sourceRevision", StringComparison.Ordinal));
        Assert.Contains(assembly.Errors, error => error.Contains("linux-x64 provenance", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public void Missing_manifest_fails_cleanly_without_partial_output()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        File.Delete(
            Path.Combine(
                fixture.ChannelRoot,
                "vibesnake-macos-universal-unsigned-channel-shape",
                "artifact-manifest.json"));

        var assembly = Assemble(fixture);

        Assert.Contains(
            assembly.Errors,
            error => error.Contains("missing macos-universal artifact manifest", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public void Missing_approved_radio_pack_blocks_preview_publication()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        File.Delete(Directory.GetFiles(fixture.RadioRoot, "*.vibesnake-pack.zip").Single());

        var assembly = Assemble(fixture);

        Assert.Contains(assembly.Errors, error => error.Contains("missing approved radio pack", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public void Tampered_radio_archive_or_extra_content_evidence_is_rejected()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        File.WriteAllBytes(Directory.GetFiles(fixture.RadioRoot, "*.vibesnake-pack.zip").Single(), "tampered radio"u8.ToArray());
        File.WriteAllText(Path.Combine(fixture.RadioRoot, "unexpected.txt"), "unexpected\n", Utf8);

        var assembly = Assemble(fixture);

        Assert.Contains(assembly.Errors, error => error.Contains("radio-pack byte count changed", StringComparison.Ordinal));
        Assert.Contains(assembly.Errors, error => error.Contains("unexpected file set", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public void Duplicate_radio_evidence_fields_fail_closed()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var assemblyPath = Path.Combine(fixture.RadioRoot, "radio_pack_assembly.json");
        var source = File.ReadAllText(assemblyPath, Utf8);
        File.WriteAllText(
            assemblyPath,
            source.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schemaVersion\": 1,", StringComparison.Ordinal),
            Utf8);

        var assembly = Assemble(fixture);

        Assert.Contains(assembly.Errors, error => error.Contains("duplicate JSON field", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public void Preview_rejects_boolean_schema_versions_in_qualified_evidence()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var planPath = Path.Combine(
            fixture.ChannelRoot,
            "vibesnake-windows-x64-unsigned-channel-shape",
            "release_output_plan.json");
        var plan = ReadObject(planPath);
        plan["schemaVersion"] = JsonValue.Create(true);
        WriteJson(planPath, plan);
        var assemblyPath = Path.Combine(fixture.RadioRoot, "radio_pack_assembly.json");
        var radioAssembly = ReadObject(assemblyPath);
        radioAssembly["schemaVersion"] = JsonValue.Create(true);
        WriteJson(assemblyPath, radioAssembly);
        RefreshRadioChecksums(fixture.RadioRoot);

        var assembly = Assemble(fixture);

        Assert.Contains(
            assembly.Errors,
            error => error.Contains("radio-pack assembly.schemaVersion must be 1", StringComparison.Ordinal));
        Assert.Contains(
            assembly.Errors,
            error => error.Contains("windows-x64 plan.schemaVersion must be 1", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Theory]
    [InlineData("station")]
    [InlineData("track-count")]
    public void Radio_track_evidence_requires_exact_types_and_station_identity(string mode)
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var manifestPath = Path.Combine(fixture.RadioRoot, "pack.json");
        var manifest = ReadObject(manifestPath);
        if (mode == "station")
        {
            manifest["radio"]!.AsObject()["stationName"] = "Conflicting Name";
            WriteJson(manifestPath, manifest);
        }

        var assemblyPath = Path.Combine(fixture.RadioRoot, "radio_pack_assembly.json");
        var radioAssembly = ReadObject(assemblyPath);
        if (mode == "track-count")
        {
            radioAssembly["trackCount"] = JsonValue.Create(true);
        }

        radioAssembly["manifestSha256"] = Sha256(manifestPath);
        WriteJson(assemblyPath, radioAssembly);
        RefreshRadioChecksums(fixture.RadioRoot);

        var assembly = Assemble(fixture);

        Assert.Contains(assembly.Errors, error => error.Contains("track evidence does not match", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public void Invalid_radio_evidence_filename_never_reads_outside_artifact_root()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var outside = Path.Combine(directory.Path, "outside.vibesnake-pack.zip");
        File.WriteAllBytes(outside, "outside artifact"u8.ToArray());
        var assemblyPath = Path.Combine(fixture.RadioRoot, "radio_pack_assembly.json");
        var radioAssembly = ReadObject(assemblyPath);
        radioAssembly["packFileName"] = "../outside.vibesnake-pack.zip";
        radioAssembly["packBytes"] = new FileInfo(outside).Length;
        radioAssembly["packSha256"] = Sha256(outside);
        WriteJson(assemblyPath, radioAssembly);

        // A resolver that follows packFileName out of the radio root has to open this file.
        using var outsideLock = new FileStream(outside, FileMode.Open, FileAccess.Read, FileShare.None);
        var assembly = Assemble(fixture);

        Assert.Contains(assembly.Errors, error => error.Contains("packFileName", StringComparison.Ordinal));
        Assert.DoesNotContain(
            assembly.Errors,
            error => error.Contains("outside.vibesnake-pack.zip", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(assembly.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(Directory.Exists(fixture.OutputRoot));
        Assert.True(outsideLock.CanRead);
    }

    [Fact]
    public void Radio_pack_compressed_budget_is_rechecked_before_publication()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var original = UnsignedPreviewCheck.MaximumRadioPackCompressedBytes;
        try
        {
            UnsignedPreviewCheck.MaximumRadioPackCompressedBytes = 1;

            var assembly = Assemble(fixture);

            Assert.Contains(assembly.Errors, error => error.Contains("compressed-size budget", StringComparison.Ordinal));
            using var evidence = JsonDocument.Parse(assembly.Json);
            Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
            Assert.False(Directory.Exists(fixture.OutputRoot));
        }
        finally
        {
            UnsignedPreviewCheck.MaximumRadioPackCompressedBytes = original;
        }
    }

    [Fact]
    public void Command_assembles_unsigned_preview_and_keeps_failed_output_absent()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var output = new StringWriter();
        var error = new StringWriter();

        var code = RepositoryCheckCommand.Run(
            [
                "unsigned-preview",
                fixture.ChannelRoot,
                fixture.ProvenanceRoot,
                fixture.RadioRoot,
                fixture.MatrixPath,
                fixture.VersionRoot,
                "v" + Version,
                Revision,
                fixture.OutputRoot,
            ],
            output,
            error);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains(
            "Unsigned native alpha preview assembled: version=" + Version + " platforms=3",
            output.ToString(),
            StringComparison.Ordinal);
        using var manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(fixture.OutputRoot, "unsigned_preview_manifest.json")));
        Assert.True(manifest.RootElement.GetProperty("unsigned").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("stablePublicationEligible").GetBoolean());

        var failedRoot = Path.Combine(directory.Path, "failed-preview");
        var failedOutput = new StringWriter();
        var failedError = new StringWriter();
        var failedCode = RepositoryCheckCommand.Run(
            [
                "unsigned-preview",
                fixture.ChannelRoot,
                fixture.ProvenanceRoot,
                fixture.RadioRoot,
                fixture.MatrixPath,
                fixture.VersionRoot,
                "v0.3.0",
                Revision,
                failedRoot,
            ],
            failedOutput,
            failedError);

        Assert.Equal(1, failedCode);
        Assert.Equal(string.Empty, failedOutput.ToString());
        Assert.Contains("Unsigned native alpha preview assembly failed:", failedError.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(failedRoot));
    }

    [Fact]
    public void Preview_rejects_version_checksum_encoding_and_blocked_output()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var versionPath = Path.Combine(fixture.VersionRoot, "VERSION");
        (byte[] Bytes, string Fragment)[] versions =
        [
            ([], "exactly one UTF-8 line"),
            ("0.3.0-alpha.1"u8.ToArray(), "exactly one UTF-8 line"),
            ("0.3.0-alpha.1\r\n"u8.ToArray(), "exactly one UTF-8 line"),
            ("0.3.0-alpha.1\nextra\n"u8.ToArray(), "exactly one UTF-8 line"),
            ("not-a-version\n"u8.ToArray(), "canonical stable or prerelease"),
            ([0xFF], "Could not read canonical product version"),
        ];
        foreach (var (bytes, fragment) in versions)
        {
            File.WriteAllBytes(versionPath, bytes);
            var assembly = Assemble(fixture);
            Assert.Contains(assembly.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
            AssertPublicationStaysClosed(assembly);
            Assert.False(Directory.Exists(fixture.OutputRoot));
        }

        File.Delete(versionPath);
        Directory.CreateDirectory(versionPath);
        var directoryVersion = Assemble(fixture);
        Assert.Contains(
            directoryVersion.Errors,
            error => error.Contains("Could not read canonical product version", StringComparison.Ordinal));

        if (Directory.Exists(versionPath))
        {
            Directory.Delete(versionPath);
        }

        File.WriteAllBytes(versionPath, Encoding.UTF8.GetBytes(Version + "\n"));
        var checksums = Path.Combine(
            fixture.ChannelRoot,
            "vibesnake-windows-x64-unsigned-channel-shape",
            "SHA256SUMS");
        var originalChecksums = File.ReadAllText(checksums, Utf8);
        File.WriteAllText(checksums, originalChecksums.Replace("\n", "\v\f\u001c\u001d\u001e\u0085\u2028\u2029\n", StringComparison.Ordinal), Utf8);
        var brokenLines = Assemble(fixture);
        Assert.Contains(brokenLines.Errors, error => error.Contains("is malformed", StringComparison.Ordinal));

        var firstLine = originalChecksums.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        var repeatedName = firstLine[(firstLine.IndexOf('*', StringComparison.Ordinal) + 1)..];
        File.WriteAllText(checksums, firstLine + "\n" + firstLine + "\n", Utf8);
        var repeated = Assemble(fixture);
        Assert.Contains(repeated.Errors, error => error.Contains("repeat " + repeatedName, StringComparison.Ordinal));

        File.WriteAllBytes(checksums, [0xFF]);
        var badEncoding = Assemble(fixture);
        Assert.Contains(badEncoding.Errors, error => error.Contains("unreadable qualification checksums", StringComparison.Ordinal));
        File.WriteAllText(checksums, originalChecksums, Utf8);

        var radioChecksums = Path.Combine(fixture.RadioRoot, "SHA256SUMS.txt");
        File.WriteAllText(radioChecksums, "not-a-checksum\nnot-a-checksum\n", Utf8);
        var radioRepeated = Assemble(fixture);
        Assert.Contains(
            radioRepeated.Errors,
            error => error.Contains("malformed or repeated", StringComparison.Ordinal));

        var weird = Assemble(fixture, "v0.3.0-alpha.1-\t\"\\\b\f\n\r\u0001é\U0001F40D");
        Assert.Contains("\\u00e9", weird.Json, StringComparison.Ordinal);
        Assert.Contains("\\ud83d\\udc0d", weird.Json, StringComparison.Ordinal);
        Assert.Contains("\\u0001", weird.Json, StringComparison.Ordinal);
        Assert.Contains("\\\"", weird.Json, StringComparison.Ordinal);
        Assert.Contains("\\\\", weird.Json, StringComparison.Ordinal);
        Assert.Contains("\\b", weird.Json, StringComparison.Ordinal);
        Assert.Contains("\\f", weird.Json, StringComparison.Ordinal);
        Assert.Contains("\\n", weird.Json, StringComparison.Ordinal);
        Assert.Contains("\\r", weird.Json, StringComparison.Ordinal);
        Assert.Contains("\\t", weird.Json, StringComparison.Ordinal);
        AssertPublicationStaysClosed(weird);

        var nullArguments = UnsignedPreviewCheck.Assemble(
            fixture.ChannelRoot,
            fixture.ProvenanceRoot,
            fixture.RadioRoot,
            fixture.MatrixPath,
            fixture.VersionRoot,
            null!,
            null!,
            fixture.OutputRoot);
        Assert.Contains(nullArguments.Errors, error => error.Contains("tag must exactly match", StringComparison.Ordinal));
        Assert.Contains(nullArguments.Errors, error => error.Contains("lowercase 40-character", StringComparison.Ordinal));
        AssertPublicationStaysClosed(nullArguments);

        RefreshRadioChecksums(fixture.RadioRoot);
        var blocked = Path.Combine(directory.Path, "blocked-parent");
        File.WriteAllBytes(blocked, [1]);
        var blockedOutput = UnsignedPreviewCheck.Assemble(
            fixture.ChannelRoot,
            fixture.ProvenanceRoot,
            fixture.RadioRoot,
            fixture.MatrixPath,
            fixture.VersionRoot,
            "v" + Version,
            Revision,
            Path.Combine(blocked, "preview"));
        Assert.Contains(blockedOutput.Errors, error => error.Contains("could not assemble preview output", StringComparison.Ordinal));
        AssertPublicationStaysClosed(blockedOutput);
    }

    [Fact]
    public void Radio_archive_rejections_cover_zip_shapes()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var packJson = File.ReadAllBytes(Path.Combine(fixture.RadioRoot, "pack.json"));
        (string Name, byte[] Data)[] standard = [("pack.json", packJson), (TrackPath, TrackBytes)];

        void Expect(string fragment, ZipShape? shape = null, IReadOnlyList<(string Name, byte[] Data)>? entries = null, byte[]? raw = null)
        {
            var packPath = Path.Combine(fixture.RadioRoot, PackName);
            if (raw is null)
            {
                WriteStoredZip(packPath, entries ?? standard, shape);
            }
            else
            {
                File.WriteAllBytes(packPath, raw);
            }

            RefreshRadioChecksums(fixture.RadioRoot);
            var assembly = Assemble(fixture);
            Assert.Contains(assembly.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
            AssertPublicationStaysClosed(assembly);
            Assert.False(Directory.Exists(fixture.OutputRoot));
        }

        Expect("unsupported shape", new ZipShape { CreateSystem = 0 });
        Expect("unsupported shape", new ZipShape { ExternalAttributes = 0 });
        Expect("unsupported shape", new ZipShape { Method = 8 });
        Expect("unsupported shape: audio/", entries: [("audio/", TrackBytes), ("pack.json", packJson)]);
        Expect("does not match the manifest allowlist", entries: [("pack.json", packJson), ("pack.json", TrackBytes)]);
        Expect("is encrypted", new ZipShape { Flags = 1 });
        Expect("Bad CRC-32", new ZipShape { CorruptCrc = true });
        Expect("inconsistent stored length", new ZipShape { InconsistentLength = true });
        Expect("local header signature is invalid", new ZipShape { BadLocalSignature = true });
        Expect("ZIP entry data is truncated", new ZipShape { LocalExtraLength = 5000 });
        Expect(
            "central directory name is truncated",
            new ZipShape { ExtraLength = 50 },
            entries: [("pack.json", packJson)]);
        Expect("not valid UTF-8", new ZipShape { Flags = 0x800, RawName = [0xFF] });
        Expect("end of central directory was not found", raw: new byte[23]);
        Expect("ZIP archive is truncated", raw: new byte[10]);

        using var zip64 = new MemoryStream();
        WriteU32(zip64, 0x06054b50);
        WriteU16(zip64, 0);
        WriteU16(zip64, 0);
        WriteU16(zip64, ushort.MaxValue);
        WriteU16(zip64, ushort.MaxValue);
        WriteU32(zip64, uint.MaxValue);
        WriteU32(zip64, uint.MaxValue);
        WriteU16(zip64, 0);
        Expect("ZIP64", raw: zip64.ToArray());

        WriteStoredZip(Path.Combine(fixture.RadioRoot, PackName), standard);
        var valid = File.ReadAllBytes(Path.Combine(fixture.RadioRoot, PackName));
        var shifted = new byte[valid.Length + 1];
        valid.CopyTo(shifted, 0);
        Expect("end of central directory was not found", raw: shifted);
        Expect("requires CP437", new ZipShape { RawName = [0x81] });
        Expect(
            "does not match the manifest allowlist",
            new ZipShape { Flags = 0x800, RawName = [0x61, 0x00, 0x62] });
    }

    [Fact]
    public void Radio_manifest_entries_cover_rejected_path_and_identity_shapes()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        var manifestPath = Path.Combine(fixture.RadioRoot, "pack.json");
        var assemblyPath = Path.Combine(fixture.RadioRoot, "radio_pack_assembly.json");
        var digest = new string('a', 64);
        var files = new JsonArray
        {
            JsonValue.Create("not-an-entry"),
            Entry("a\\b", 1, digest),
            Entry("/absolute.mp3", 1, digest),
            Entry("trail/", 1, digest),
            Entry("a/../b.mp3", 1, digest),
            Entry("a/./b.mp3", 1, digest),
            Entry("a//b.mp3", 1, digest),
            Entry("", 1, digest),
            Entry("audio/raß.mp3", 0, digest),
            Entry("audio/negative.mp3", -1, digest),
            Entry("audio/dup.mp3", 1, digest),
            Entry("audio/DUP.mp3", 1, digest),
            Entry("audio/nohash.mp3", 1, "zz"),
            Entry("audio/huge.mp3", (120L * 1024 * 1024) + 1, digest),
            Entry("audio/" + char.ConvertFromUtf32(0x1F40D) + ".mp3", 1, digest),
        };
        var manifest = ReadObject(manifestPath);
        manifest["files"] = files;
        WriteJson(manifestPath, manifest);
        var rejectedPaths = Assemble(fixture);
        Assert.Contains(rejectedPaths.Errors, error => error.Contains("invalid file entry", StringComparison.Ordinal));
        Assert.Contains(rejectedPaths.Errors, error => error.Contains("installed-size budget", StringComparison.Ordinal));
        AssertPublicationStaysClosed(rejectedPaths);

        foreach (var literal in new[] { "\"   \"", "\"\"", "null", "\"" + new string('n', 513) + "\"" })
        {
            var assembly = ReadObject(assemblyPath);
            assembly["stationName"] = JsonNode.Parse(literal);
            WriteJson(assemblyPath, assembly);
            var result = Assemble(fixture);
            Assert.Contains(result.Errors, error => error.Contains("stationName is invalid", StringComparison.Ordinal));
        }

        var identity = ReadObject(assemblyPath);
        identity["stationName"] = "The Flow Signal";
        identity["packId"] = "BAD";
        identity["packFileName"] = JsonNode.Parse("null");
        WriteJson(assemblyPath, identity);
        var nullName = Assemble(fixture);
        Assert.Contains(nullName.Errors, error => error.Contains("packId is invalid", StringComparison.Ordinal));

        identity = ReadObject(assemblyPath);
        identity["packFileName"] = JsonValue.Create(1);
        WriteJson(assemblyPath, identity);
        var numericName = Assemble(fixture);
        Assert.Contains(numericName.Errors, error => error.Contains("must be None", StringComparison.Ordinal));
    }

    [Fact]
    public void Compared_evidence_covers_nested_equality()
    {
        (string Plan, string Matrix)[] pairs =
        [
            ("[1, {\"a\": true}]", "[1, {\"a\": true}]"),
            ("[1, {\"a\": true}]", "[1, {\"a\": false}]"),
            ("[1, 2]", "[1]"),
            ("{\"a\": 1}", "{\"b\": 1}"),
            ("{\"a\": 1}", "{\"a\": 2}"),
            ("true", "true"),
            ("false", "false"),
            ("true", "1"),
            ("1.5", "1.5"),
            ("9999999999999999999999999999999999999999", "9999999999999999999999999999999999999999"),
        ];
        foreach (var (planLiteral, matrixLiteral) in pairs)
        {
            using var directory = new TemporaryDirectory();
            var fixture = WriteFixture(directory.Path);
            SetPackageBytes(fixture, planLiteral, matrixLiteral);
            AssertPublicationStaysClosed(Assemble(fixture));
        }

        using var missingDirectory = new TemporaryDirectory();
        var missingFixture = WriteFixture(missingDirectory.Path);
        var planPath = PackagePlanPath(missingFixture);
        var plan = ReadObject(planPath);
        plan.Remove("packageBytes");
        WriteJson(planPath, plan);
        var matrix = ReadObject(missingFixture.MatrixPath);
        matrix["platforms"]![0]!.AsObject().Remove("packageBytes");
        WriteJson(missingFixture.MatrixPath, matrix);
        AssertPublicationStaysClosed(Assemble(missingFixture));

        plan = ReadObject(planPath);
        matrix = ReadObject(missingFixture.MatrixPath);
        matrix["platforms"]![0]!["packageBytes"] = JsonNode.Parse("null");
        WriteJson(missingFixture.MatrixPath, matrix);
        AssertPublicationStaysClosed(Assemble(missingFixture));

        plan["packageBytes"] = JsonNode.Parse("null");
        WriteJson(planPath, plan);
        matrix = ReadObject(missingFixture.MatrixPath);
        matrix["platforms"]![0]!.AsObject().Remove("packageBytes");
        WriteJson(missingFixture.MatrixPath, matrix);
        AssertPublicationStaysClosed(Assemble(missingFixture));
    }

    [Fact]
    public void Preview_rejects_shape_drift_across_channel_radio_and_matrix_json()
    {
        using var directory = new TemporaryDirectory();
        var fixture = WriteFixture(directory.Path);
        string[] files =
        [
            fixture.MatrixPath,
            Path.Combine(fixture.RadioRoot, "pack.json"),
            Path.Combine(fixture.RadioRoot, "radio_pack_assembly.json"),
            Path.Combine(fixture.ChannelRoot, "vibesnake-windows-x64-unsigned-channel-shape", "artifact-manifest.json"),
            Path.Combine(fixture.ChannelRoot, "vibesnake-windows-x64-unsigned-channel-shape", "release_output_plan.json"),
        ];
        var attempts = 0;
        var failures = 0;
        foreach (var file in files)
        {
            var original = File.ReadAllBytes(file);
            try
            {
                foreach (var jsonPath in JsonPaths(JsonNode.Parse(original)!))
                {
                    foreach (var kind in ShapeKinds)
                    {
                        var copy = JsonNode.Parse(original)!;
                        SetJsonPath(copy, jsonPath, JsonReplacement(kind));
                        WriteJson(file, copy);
                        if (Directory.Exists(fixture.OutputRoot))
                        {
                            Directory.Delete(fixture.OutputRoot, recursive: true);
                        }

                        var assembly = Assemble(fixture);
                        attempts++;
                        AssertPublicationStaysClosed(assembly);
                        if (!assembly.Passed)
                        {
                            failures++;
                        }

                        if (Directory.Exists(fixture.OutputRoot))
                        {
                            Directory.Delete(fixture.OutputRoot, recursive: true);
                        }
                    }
                }
            }
            finally
            {
                File.WriteAllBytes(file, original);
            }
        }

        Assert.True(attempts > 20, attempts.ToString(CultureInfo.InvariantCulture));
        Assert.True(failures > 20, failures.ToString(CultureInfo.InvariantCulture));
    }

    private static UnsignedPreviewCheck.Assembly Assemble(PreviewFixture fixture, string? tag = null)
        => UnsignedPreviewCheck.Assemble(
            fixture.ChannelRoot,
            fixture.ProvenanceRoot,
            fixture.RadioRoot,
            fixture.MatrixPath,
            fixture.VersionRoot,
            tag ?? "v" + Version,
            Revision,
            fixture.OutputRoot);

    private static PreviewFixture WriteFixture(string root)
    {
        var versionRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(versionRoot);
        File.WriteAllBytes(Path.Combine(versionRoot, "VERSION"), Encoding.UTF8.GetBytes(Version + "\n"));
        var channelRoot = Path.Combine(root, "channels");
        var provenanceRoot = Path.Combine(root, "provenance");
        var rows = new JsonArray();
        foreach (var platform in Platforms)
        {
            var artifactRoot = Path.Combine(channelRoot, $"vibesnake-{platform}-unsigned-channel-shape");
            Directory.CreateDirectory(artifactRoot);
            var extension = platform == "linux-x64" ? ".tar.gz" : ".zip";
            var packageName = $"VibeSnake-{Version}-{platform}-qualification{extension}";
            var packagePath = Path.Combine(artifactRoot, packageName);
            File.WriteAllBytes(packagePath, Encoding.UTF8.GetBytes(platform + " qualified player"));
            var manifestPath = Path.Combine(artifactRoot, "artifact-manifest.json");
            WriteJson(
                manifestPath,
                new JsonObject
                {
                    ["schemaVersion"] = 3,
                    ["product"] = "Vibe Snake",
                    ["platform"] = platform,
                    ["buildMode"] = "Release",
                    ["sourceRevision"] = Revision,
                });
            var planPath = Path.Combine(artifactRoot, "release_output_plan.json");
            WriteJson(
                planPath,
                new JsonObject
                {
                    ["schemaVersion"] = 1,
                    ["kind"] = "release-output-plan-v1",
                    ["product"] = "Vibe Snake",
                    ["productVersion"] = Version,
                    ["platform"] = platform,
                    ["directDownloadFileName"] = packageName,
                    ["passed"] = true,
                    ["qualificationOnly"] = true,
                    ["assemblyEligible"] = true,
                    ["publicationEligible"] = false,
                    ["optionalPackOutputSeparate"] = true,
                    ["baseGameIncludesOptionalPacks"] = false,
                    ["playerDataExcluded"] = true,
                    ["uninstallPreservesPlayerData"] = true,
                    ["deterministicRepeatMatched"] = true,
                    ["packageBytes"] = new FileInfo(packagePath).Length,
                    ["packageSha256"] = Sha256(packagePath),
                });
            var checksums = new StringBuilder();
            foreach (var path in new[] { packagePath, manifestPath, planPath })
            {
                checksums.Append(Sha256(path)).Append(" *").Append(Path.GetFileName(path)).Append('\n');
            }

            File.WriteAllText(Path.Combine(artifactRoot, "SHA256SUMS"), checksums.ToString(), Utf8);
            var provenancePath = Path.Combine(provenanceRoot, $"vibesnake-{platform}-provenance", "attestation.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(provenancePath)!);
            File.WriteAllText(provenancePath, $"provenance for {platform}\n", Utf8);
            rows.Add(new JsonObject
            {
                ["platform"] = platform,
                ["artifactManifestSha256"] = Sha256(manifestPath),
                ["packageSha256"] = Sha256(packagePath),
                ["packageBytes"] = new FileInfo(packagePath).Length,
                ["directDownloadFileName"] = packageName,
            });
        }

        var matrixPath = Path.Combine(root, "matrix.json");
        WriteJson(
            matrixPath,
            new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "release-matrix-qualification-v1",
                ["passed"] = true,
                ["sourceRevision"] = Revision,
                ["buildMode"] = "Release",
                ["productVersion"] = Version,
                ["publicationEligible"] = false,
                ["platforms"] = rows,
            });
        return new PreviewFixture(
            channelRoot,
            provenanceRoot,
            WriteRadio(root),
            matrixPath,
            versionRoot,
            Path.Combine(root, "preview"));
    }

    private static string WriteRadio(string root)
    {
        var radioRoot = Path.Combine(root, "radio");
        Directory.CreateDirectory(radioRoot);
        var manifestPath = Path.Combine(radioRoot, "pack.json");
        WriteJson(manifestPath, RadioManifest());
        var packPath = Path.Combine(radioRoot, PackName);
        WriteStoredZip(
            packPath,
            [
                ("pack.json", File.ReadAllBytes(manifestPath)),
                (TrackPath, TrackBytes),
            ]);
        var assemblyPath = Path.Combine(radioRoot, "radio_pack_assembly.json");
        WriteJson(
            assemblyPath,
            new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "approved-radio-pack-assembly-v1",
                ["passed"] = true,
                ["releaseApproved"] = true,
                ["packId"] = "vibesnake.radio.flow-signal",
                ["packVersion"] = "1.0.0",
                ["stationId"] = "flow_signal",
                ["stationName"] = "The Flow Signal",
                ["curationDecisionStatus"] = "approved-for-alpha-release",
                ["inventorySha256"] = new string('b', 64),
                ["curationSha256"] = new string('c', 64),
                ["manifestSha256"] = Sha256(manifestPath),
                ["packFileName"] = PackName,
                ["packBytes"] = new FileInfo(packPath).Length,
                ["packSha256"] = Sha256(packPath),
                ["trackCount"] = 1,
                ["trackIds"] = new JsonArray(JsonValue.Create(TrackId)),
            });
        RefreshRadioChecksums(radioRoot);
        return radioRoot;
    }

    private static JsonObject RadioManifest()
    {
        var trackIds = new JsonArray();
        trackIds.Add(JsonValue.Create(TrackId));
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = "vibesnake.radio.flow-signal",
            ["version"] = "1.0.0",
            ["kind"] = "radio",
            ["files"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = TrackId,
                    ["path"] = TrackPath,
                    ["bytes"] = TrackBytes.Length,
                    ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(TrackBytes)),
                },
            },
            ["radio"] = new JsonObject
            {
                ["stationId"] = "flow_signal",
                ["stationName"] = "The Flow Signal",
                ["trackIds"] = trackIds,
            },
        };
    }

    private static void RefreshRadioChecksums(string radioRoot)
    {
        var paths = new[]
        {
            Directory.GetFiles(radioRoot, "*.vibesnake-pack.zip").Single(),
            Path.Combine(radioRoot, "pack.json"),
            Path.Combine(radioRoot, "radio_pack_assembly.json"),
        };
        Array.Sort(paths, (left, right) => StringComparer.Ordinal.Compare(Path.GetFileName(left), Path.GetFileName(right)));
        var checksums = new StringBuilder();
        foreach (var path in paths)
        {
            checksums.Append(Sha256(path)).Append("  ").Append(Path.GetFileName(path)).Append('\n');
        }

        File.WriteAllText(Path.Combine(radioRoot, "SHA256SUMS.txt"), checksums.ToString(), Utf8);
    }

    private static void WriteStoredZip(
        string path,
        IReadOnlyList<(string Name, byte[] Data)> entries,
        ZipShape? shape = null)
    {
        var encoded = new List<(byte[] Name, byte[] Data)>(entries.Count);
        foreach (var (name, data) in entries)
        {
            encoded.Add((Encoding.UTF8.GetBytes(name), data));
        }

        if (shape?.RawName is not null && encoded.Count > 0)
        {
            encoded[0] = (shape.RawName, encoded[0].Data);
        }

        using var stream = new MemoryStream();
        var locals = new List<(byte[] Name, byte[] Data, uint Crc, long Offset, uint Compressed, uint Uncompressed)>(encoded.Count);
        foreach (var (name, data) in encoded)
        {
            var offset = stream.Position;
            var size = (uint)data.Length;
            var compressed = size;
            var uncompressed = size;
            if (shape?.InconsistentLength == true)
            {
                compressed = size + 1;
            }

            var crc = Crc32(data);
            if (shape?.CorruptCrc == true)
            {
                crc ^= 1;
            }

            WriteU32(stream, shape?.BadLocalSignature == true ? 0u : 0x04034b50u);
            WriteU16(stream, 20);
            WriteU16(stream, shape?.Flags ?? 0);
            WriteU16(stream, shape?.Method ?? 0);
            WriteU16(stream, 0);
            WriteU16(stream, 0x21);
            WriteU32(stream, crc);
            WriteU32(stream, size);
            WriteU32(stream, size);
            WriteU16(stream, (ushort)name.Length);
            WriteU16(stream, shape?.LocalExtraLength ?? 0);
            stream.Write(name);
            stream.Write(data);
            locals.Add((name, data, crc, offset, compressed, uncompressed));
        }

        var directoryOffset = stream.Position;
        foreach (var local in locals)
        {
            WriteU32(stream, 0x02014b50);
            WriteU16(stream, (ushort)(((shape?.CreateSystem ?? 3) << 8) | 20));
            WriteU16(stream, 20);
            WriteU16(stream, shape?.Flags ?? 0);
            WriteU16(stream, shape?.Method ?? 0);
            WriteU16(stream, 0);
            WriteU16(stream, 0x21);
            WriteU32(stream, local.Crc);
            WriteU32(stream, local.Compressed);
            WriteU32(stream, local.Uncompressed);
            WriteU16(stream, (ushort)local.Name.Length);
            WriteU16(stream, shape?.ExtraLength ?? 0);
            WriteU16(stream, 0);
            WriteU16(stream, 0);
            WriteU16(stream, 0);
            WriteU32(stream, shape?.ExternalAttributes ?? ((0x8000u | 0x1A4u) << 16));
            WriteU32(stream, (uint)local.Offset);
            stream.Write(local.Name);
        }

        var directorySize = stream.Position - directoryOffset;
        WriteU32(stream, 0x06054b50);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU16(stream, (ushort)locals.Count);
        WriteU16(stream, (ushort)locals.Count);
        WriteU32(stream, (uint)directorySize);
        WriteU32(stream, (uint)directoryOffset);
        WriteU16(stream, 0);
        File.WriteAllBytes(path, stream.ToArray());
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (crc >> 1) ^ 0xEDB88320u;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        return ~crc;
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        bytes[0] = (byte)value;
        bytes[1] = (byte)(value >> 8);
        stream.Write(bytes);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)value;
        bytes[1] = (byte)(value >> 8);
        bytes[2] = (byte)(value >> 16);
        bytes[3] = (byte)(value >> 24);
        stream.Write(bytes);
    }

    private static void WriteJson(string path, JsonNode node)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var json = node.ToJsonString(JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }

        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(json));
    }

    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path, Utf8))!.AsObject();

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void AssertPublicationStaysClosed(UnsignedPreviewCheck.Assembly assembly)
    {
        Assert.False(assembly.Json.Contains("\"stablePublicationEligible\": true", StringComparison.Ordinal));
        Assert.False(assembly.Json.Contains("\"unsigned\": false", StringComparison.Ordinal));
    }

    private static string PackagePlanPath(PreviewFixture fixture) =>
        Path.Combine(fixture.ChannelRoot, "vibesnake-windows-x64-unsigned-channel-shape", "release_output_plan.json");

    private static void SetPackageBytes(PreviewFixture fixture, string planLiteral, string matrixLiteral)
    {
        var planPath = PackagePlanPath(fixture);
        var plan = ReadObject(planPath);
        plan["packageBytes"] = JsonNode.Parse(planLiteral);
        WriteJson(planPath, plan);
        var matrix = ReadObject(fixture.MatrixPath);
        matrix["platforms"]![0]!["packageBytes"] = JsonNode.Parse(matrixLiteral);
        WriteJson(fixture.MatrixPath, matrix);
    }

    private static JsonObject Entry(string path, long bytes, string digest) => new()
    {
        ["path"] = path,
        ["bytes"] = JsonValue.Create(bytes),
        ["sha256"] = digest,
    };

    private static List<string[]> JsonPaths(JsonNode node)
    {
        var paths = new List<string[]>();
        CollectJsonPaths(node, [], paths);
        return paths;
    }

    private static void CollectJsonPaths(JsonNode? node, List<string> prefix, List<string[]> paths)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                prefix.Add(property.Key);
                paths.Add([.. prefix]);
                CollectJsonPaths(property.Value, prefix, paths);
                prefix.RemoveAt(prefix.Count - 1);
            }

            return;
        }

        if (node is not JsonArray array || array.Count == 0)
        {
            return;
        }

        prefix.Add("0");
        paths.Add([.. prefix]);
        CollectJsonPaths(array[0], prefix, paths);
        prefix.RemoveAt(prefix.Count - 1);
    }

    private static void SetJsonPath(JsonNode root, string[] path, JsonNode value)
    {
        JsonNode current = root;
        for (var index = 0; index < path.Length - 1; index++)
        {
            current = NextJson(current, path[index]);
        }

        if (current is JsonArray array)
        {
            array[int.Parse(path[^1], CultureInfo.InvariantCulture)] = value;
            return;
        }

        current.AsObject()[path[^1]] = value;
    }

    private static JsonNode NextJson(JsonNode current, string segment) => current is JsonArray array
        ? array[int.Parse(segment, CultureInfo.InvariantCulture)]!
        : current[segment]!;

    private static JsonNode JsonReplacement(string kind) => kind switch
    {
        "bool" => JsonValue.Create(true)!,
        "string" => JsonValue.Create("bad")!,
        "empty" => new JsonArray(),
        "null" => JsonNode.Parse("null")!,
        "float" => JsonValue.Create(1.5d)!,
        "negative" => JsonValue.Create(-1)!,
        _ => throw new InvalidOperationException(kind),
    };

    private sealed class ZipShape
    {
        public ushort CreateSystem { get; init; } = 3;

        public uint ExternalAttributes { get; init; } = (0x8000u | 0x1A4u) << 16;

        public bool CorruptCrc { get; init; }

        public ushort Method { get; init; }

        public ushort Flags { get; init; }

        public bool InconsistentLength { get; init; }

        public bool BadLocalSignature { get; init; }

        public ushort ExtraLength { get; init; }

        public ushort LocalExtraLength { get; init; }

        public byte[]? RawName { get; init; }
    }

    private readonly record struct PreviewFixture(
        string ChannelRoot,
        string ProvenanceRoot,
        string RadioRoot,
        string MatrixPath,
        string VersionRoot,
        string OutputRoot);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "vibesnake-unsigned-preview-" + Guid.NewGuid().ToString("N"));
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
