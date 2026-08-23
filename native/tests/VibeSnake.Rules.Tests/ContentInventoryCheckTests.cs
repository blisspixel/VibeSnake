using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class ContentInventoryCheckTests
{
    [Fact]
    public void Build_is_sorted_hashed_canonical_and_reports_duplicates()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "assets/notes/b.txt", "same");
            WriteFile(root, "assets/notes/a.txt", "same");
            WritePolicy(root, Rule("notes", "notes/*.txt"));

            var first = ContentInventoryCheck.BuildInventoryJson(root);
            var second = ContentInventoryCheck.BuildInventoryJson(root);
            using var document = JsonDocument.Parse(first);
            var value = document.RootElement;
            var assets = value.GetProperty("assets").EnumerateArray().ToArray();

            Assert.Equal(first, second);
            Assert.EndsWith("\n", first, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", first, StringComparison.Ordinal);
            Assert.Equal(ContentInventoryCheck.InventorySchemaVersion, value.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(2, value.GetProperty("fileCount").GetInt32());
            Assert.Equal(8, value.GetProperty("totalBytes").GetInt64());
            Assert.Equal(["notes/a.txt", "notes/b.txt"], assets.Select(asset => asset.GetProperty("path").GetString()));
            Assert.Equal(JsonValueKind.Null, assets[0].GetProperty("duplicateOf").ValueKind);
            Assert.Equal("asset:notes/a.txt", assets[1].GetProperty("duplicateOf").GetString());
            Assert.Equal(1, value.GetProperty("summary").GetProperty("duplicateGroupCount").GetInt32());
            Assert.Equal(1, value.GetProperty("summary").GetProperty("duplicateFileCount").GetInt32());
            Assert.Equal(64, assets[0].GetProperty("sha256").GetString()!.Length);
            Assert.Equal(
                ["notes/b.txt: export-eligible duplicate of asset:notes/a.txt"],
                ContentInventoryCheck.FindReleaseBlockers(root));
        });
    }

    [Fact]
    public void Write_is_atomic_repeatable_and_check_requires_exact_current_bytes()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "assets/config.json", "{}\n");
            WritePolicy(root, Rule("config", "config.json"));

            var first = ContentInventoryCheck.Write(root);
            var path = Path.Combine(root, "config", "content_inventory.json");
            var firstBytes = File.ReadAllBytes(path);
            var second = ContentInventoryCheck.Write(root);

            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.Equal(firstBytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "content_inventory.json.tmp-*"));
            Assert.True(ContentInventoryCheck.Inspect(root).Passed);

            WriteFile(root, "assets/config.json", "{\"changed\":true}\n");
            var stale = ContentInventoryCheck.Inspect(root);

            Assert.False(stale.Passed);
            Assert.Contains("stale", Assert.Single(stale.Failures), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Release_readiness_reports_runtime_integrity_and_duplicate_blockers()
    {
        WithTemporaryDirectory(root =>
        {
            WriteBytes(root, "assets/empty.mp3", []);
            WriteFile(root, "assets/a.txt", "same");
            WriteFile(root, "assets/b.txt", "same");
            WritePolicy(
                root,
                Rule("blocked", "empty.mp3", shipStatus: "blocked", rights: UnverifiedRights()),
                Rule("approved", "*.txt"));
            Assert.True(ContentInventoryCheck.Write(root).Passed);

            var ordinary = ContentInventoryCheck.Inspect(root);
            var release = ContentInventoryCheck.Inspect(root, requireReleaseReady: true);

            Assert.True(ordinary.Passed);
            Assert.False(release.Passed);
            Assert.Equal(3, release.Failures.Count);
            Assert.Contains(release.Failures, failure => failure.Contains("runtime asset is blocked", StringComparison.Ordinal));
            Assert.Contains(release.Failures, failure => failure.Contains("runtime asset integrity is empty", StringComparison.Ordinal));
            Assert.Contains(release.Failures, failure => failure.Contains("export-eligible duplicate", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Approved_unique_valid_assets_are_release_ready()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "assets/file.txt", "value");
            WritePolicy(root, Rule("text", "file.txt"));
            Assert.True(ContentInventoryCheck.Write(root).Passed);

            var result = ContentInventoryCheck.Inspect(root, requireReleaseReady: true);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            Assert.Contains("release-ready", result.SuccessMessage, StringComparison.Ordinal);
            Assert.Contains("release_blockers=0", result.SuccessMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Policy_rejects_invalid_or_ambiguous_contracts()
    {
        string[] variants =
        [
            "schema",
            "schema-string",
            "extra-policy-field",
            "rules-not-array",
            "empty-rules",
            "too-many-rules",
            "rule-not-object",
            "unsafe-root",
            "absolute-root",
            "glob-root",
            "trailing-root",
            "backslash-root",
            "colon-root",
            "double-slash-root",
            "extra-rule-field",
            "missing-rule-field",
            "duplicate-rule-id",
            "patterns-not-array",
            "empty-patterns",
            "too-many-patterns",
            "pattern-not-string",
            "duplicate-pattern",
            "unsafe-pattern",
            "trailing-pattern",
            "backslash-pattern",
            "colon-pattern",
            "double-slash-pattern",
            "invalid-runtime",
            "invalid-ship",
            "invalid-rights",
            "approved-uncleared",
            "excluded-runtime",
            "missing-rights-field",
            "rights-not-object",
            "blank-text",
            "long-text",
        ];
        foreach (var variant in variants)
        {
            WithTemporaryDirectory(root =>
            {
                WriteFile(root, "assets/file.txt", "value");
                var policy = Policy(Rule("files", "*.txt"));
                ApplyPolicyVariant(policy, variant);
                WriteJson(root, "config/content_policy.json", policy);

                var result = ContentInventoryCheck.Write(root);

                Assert.False(result.Passed);
                Assert.NotEmpty(result.Failures);
            });
        }
    }

    [Fact]
    public void Policy_rejects_duplicate_json_fields_invalid_utf8_and_oversize()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "assets/file.txt", "value");
            WriteFile(
                root,
                "config/content_policy.json",
                "{\"schemaVersion\":1,\"schemaVersion\":1,\"assetRoot\":\"assets\",\"rules\":[]}");

            var duplicate = ContentInventoryCheck.Write(root);

            Assert.False(duplicate.Passed);
            Assert.Contains("repeats JSON field", Assert.Single(duplicate.Failures), StringComparison.Ordinal);

            WriteBytes(root, "config/content_policy.json", [0xff]);
            var invalidUtf8 = ContentInventoryCheck.Write(root);
            Assert.False(invalidUtf8.Passed);
            Assert.Contains("unreadable", Assert.Single(invalidUtf8.Failures), StringComparison.Ordinal);

            using (var output = new FileStream(
                Path.Combine(root, "config", "content_policy.json"),
                FileMode.Create,
                FileAccess.Write))
            {
                output.SetLength((1024 * 1024) + 1);
            }

            var oversized = ContentInventoryCheck.Write(root);
            Assert.False(oversized.Passed);
            Assert.Contains("byte validation limit", Assert.Single(oversized.Failures), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Build_rejects_unmatched_ambiguous_unused_unsupported_and_empty_approved_assets()
    {
        foreach (var variant in new[] { "unmatched", "ambiguous", "unused", "unsupported", "empty" })
        {
            WithTemporaryDirectory(root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "assets"));
                switch (variant)
                {
                    case "unmatched":
                        WriteFile(root, "assets/file.txt", "value");
                        WritePolicy(root, Rule("json", "*.json"));
                        break;
                    case "ambiguous":
                        WriteFile(root, "assets/file.txt", "value");
                        WritePolicy(root, Rule("all", "*.txt"), Rule("exact", "file.txt"));
                        break;
                    case "unused":
                        WriteFile(root, "assets/file.txt", "value");
                        WritePolicy(root, Rule("exact", "file.txt"), Rule("unused", "other.txt"));
                        break;
                    case "unsupported":
                        WriteBytes(root, "assets/file.bin", [1]);
                        WritePolicy(root, Rule("binary", "file.bin"));
                        break;
                    default:
                        WriteBytes(root, "assets/empty.txt", []);
                        WritePolicy(root, Rule("empty", "empty.txt"));
                        break;
                }

                var result = ContentInventoryCheck.Write(root);

                Assert.False(result.Passed);
                Assert.NotEmpty(result.Failures);
            });
        }
    }

    [Fact]
    public void Basic_json_text_png_wav_and_mp3_integrity_checks_pass()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "assets/config.json", "{\"valid\":true}\n");
            WriteFile(root, "assets/notes.md", "# Valid\n");
            WriteFile(root, "assets/table.csv", "name,value\nvalid,1\n");
            WriteBytes(root, "assets/image.png", Png(includeC2pa: true, splitImageData: true));
            WriteBytes(root, "assets/indexed.png", IndexedPng());
            WriteBytes(root, "assets/grayscale.png", SimplePng(PngHeader(1, 1, 8, 0), [0, 0]));
            WriteBytes(root, "assets/gray-alpha.png", SimplePng(PngHeader(1, 1, 8, 4), [0, 0, 0]));
            WriteBytes(root, "assets/interlaced.png", SimplePng(PngHeader(1, 1, 8, 2, interlace: 1), [0, 0, 0, 0]));
            WriteBytes(root, "assets/cue.wav", Wav());
            WriteBytes(root, "assets/cue.mp3", Mp3Frame().Concat(Mp3Frame()).ToArray());
            WriteBytes(root, "assets/id3.mp3", Id3Mp3(includeFooter: true));
            WritePolicy(
                root,
                Rule("json", "config.json"),
                Rule("text", "notes.md"),
                Rule("csv", "table.csv"),
                Rule("png", "image.png"),
                Rule("indexed", "indexed.png"),
                Rule("grayscale", "grayscale.png"),
                Rule("gray-alpha", "gray-alpha.png"),
                Rule("interlaced", "interlaced.png"),
                Rule("wav", "cue.wav"),
                Rule("mp3", "cue.mp3"),
                Rule("id3", "id3.mp3"));

            using var document = JsonDocument.Parse(ContentInventoryCheck.BuildInventoryJson(root));
            var assets = document.RootElement.GetProperty("assets").EnumerateArray().ToArray();

            Assert.All(assets, asset => Assert.Equal("valid", asset.GetProperty("integrityStatus").GetString()));
            Assert.Contains(
                assets,
                asset => asset.GetProperty("integrityDetail").GetString()!.Contains("C2PA/JUMBF", StringComparison.Ordinal));
            Assert.Empty(ContentInventoryCheck.FindReleaseBlockers(root));
        });
    }

    [Fact]
    public void Invalid_media_structures_are_recorded_for_excluded_assets()
    {
        var validPng = Png();
        var badCrc = validPng.ToArray();
        badCrc[^1] ^= 0xff;
        var compressed = Compress([0, 0]);
        var split = compressed.Length / 2;
        var indexedHeader = PngHeader(2, 1, 1, 3);
        var duplicateAdler = compressed[^4..];
        var compressionHeader = PngHeader(1, 1, 8, 2);
        compressionHeader[10] = 1;
        var filteringHeader = PngHeader(1, 1, 8, 2);
        filteringHeader[11] = 1;
        var interlaceHeader = PngHeader(1, 1, 8, 2);
        interlaceHeader[12] = 2;
        var cases = new (string Name, byte[] Value, string Detail)[]
        {
            ("signature.png", "not a png"u8.ToArray(), "signature"),
            ("truncated.png", PngSignature().Concat(new byte[] { 0, 0, 0, 13 }).Concat("IHDR"u8.ToArray()).ToArray(), "truncated"),
            ("bad-crc.png", badCrc, "CRC"),
            ("bad-type.png", PngFromChunks(PngChunk("IH1R", [])), "chunk type"),
            ("first.png", PngFromChunks(PngChunk("tEXt", []), PngChunk("IEND", [])), "first chunk"),
            ("short-header.png", PngFromChunks(PngChunk("IHDR", new byte[12]), PngChunk("IEND", [])), "IHDR"),
            ("duplicate-header.png", PngFromChunks(PngChunk("IHDR", PngHeader(1, 1, 8, 2)), PngChunk("IHDR", PngHeader(1, 1, 8, 2)), PngChunk("IEND", [])), "IHDR"),
            ("giant.png", Png(width: 65_536, height: 1), "dimension"),
            ("zero-width.png", SimplePng(PngHeader(0, 1, 8, 2), [0]), "positive"),
            ("pixel-count.png", SimplePng(PngHeader(9000, 9000, 8, 2), [0]), "pixel count"),
            ("bad-depth.png", SimplePng(PngHeader(1, 1, 4, 2), [0]), "incompatible"),
            ("bad-compression.png", SimplePng(compressionHeader, [0]), "unsupported"),
            ("bad-filtering.png", SimplePng(filteringHeader, [0]), "unsupported"),
            ("bad-interlace.png", SimplePng(interlaceHeader, [0]), "unsupported"),
            ("decoded-limit.png", SimplePng(PngHeader(8192, 8192, 16, 6), [0]), "decoded image"),
            ("missing-palette.png", IndexedPng(includePalette: false), "requires PLTE"),
            ("oversized-palette.png", PngFromChunks(PngChunk("IHDR", indexedHeader), PngChunk("PLTE", new byte[769]), PngChunk("IEND", [])), "PLTE"),
            ("duplicate-palette.png", PngFromChunks(PngChunk("IHDR", indexedHeader), PngChunk("PLTE", [0, 0, 0]), PngChunk("PLTE", [0, 0, 0]), PngChunk("IEND", [])), "at most once"),
            ("late-palette.png", PngFromChunks(PngChunk("IHDR", PngHeader(1, 1, 8, 2)), PngChunk("IDAT", Compress([0, 0, 0, 0])), PngChunk("PLTE", [0, 0, 0]), PngChunk("IEND", [])), "before IDAT"),
            ("invalid-zlib.png", IndexedPng(imageData: "not-zlib"u8.ToArray()), "zlib"),
            ("short-zlib.png", IndexedPng(imageData: [1, 2, 3, 4, 5]), "incomplete zlib"),
            ("bad-zlib-header.png", IndexedPng(imageData: [0, 0, 0, 0, 0, 0]), "zlib header"),
            ("invalid-filter.png", IndexedPng(imageData: Compress([5, 0])), "filter method"),
            ("extra-scanline.png", IndexedPng(imageData: Compress([0, 0, 0])), "exceeds"),
            ("trailing-zlib.png", IndexedPng(imageData: [.. compressed, 0x42]), "zlib"),
            ("duplicate-adler.png", IndexedPng(imageData: [.. compressed, .. duplicateAdler]), "zlib stream"),
            ("incomplete-zlib.png", IndexedPng(imageData: compressed[..^1]), "zlib"),
            ("split-idat.png", PngFromChunks(
                PngChunk("IHDR", indexedHeader),
                PngChunk("PLTE", [0, 0, 0, 255, 255, 255]),
                PngChunk("IDAT", compressed[..split]),
                PngChunk("tEXt", "break"u8.ToArray()),
                PngChunk("IDAT", compressed[split..]),
                PngChunk("IEND", [])), "consecutive"),
            ("critical.png", PngFromChunks(PngChunk("IHDR", PngHeader(1, 1, 8, 2)), PngChunk("ABCD", []), PngChunk("IEND", [])), "critical"),
            ("gray-palette.png", PngFromChunks(PngChunk("IHDR", PngHeader(1, 1, 8, 0)), PngChunk("PLTE", [0, 0, 0]), PngChunk("IDAT", Compress([0, 0])), PngChunk("IEND", [])), "grayscale"),
            ("large-palette.png", PngFromChunks(PngChunk("IHDR", indexedHeader), PngChunk("PLTE", [0, 0, 0, 1, 1, 1, 2, 2, 2]), PngChunk("IDAT", compressed), PngChunk("IEND", [])), "bit depth"),
            ("zero-idat.png", PngFromChunks(PngChunk("IHDR", PngHeader(1, 1, 8, 2)), PngChunk("IDAT", []), PngChunk("IEND", [])), "no image data"),
            ("missing-end.png", PngFromChunks(PngChunk("IHDR", PngHeader(1, 1, 8, 2)), PngChunk("IDAT", Compress([0, 0, 0, 0]))), "no IEND"),
            ("nonempty-end.png", PngFromChunks(PngChunk("IHDR", PngHeader(1, 1, 8, 2)), PngChunk("IDAT", Compress([0, 0, 0, 0])), PngChunk("IEND", [1])), "IEND chunk must be empty"),
            ("trailing-end.png", [.. validPng, 1], "trailing bytes"),
            ("one-frame.mp3", Mp3Frame(), "consecutive"),
            ("truncated-frame.mp3", Mp3Frame()[..32], "complete"),
            ("invalid-id3.mp3", [.. "ID3"u8.ToArray(), 4, 0, 0, 0x80, 0, 0, 0], "ID3"),
            ("incompatible.mp3", [.. Mp3Frame(), .. Mp3Frame([0xff, 0xfb, 0x94, 0x64], 500)], "incompatible"),
            ("reserved-version.mp3", [0xff, 0xeb, 0x90, 0x64], "consecutive"),
            ("reserved-layer.mp3", [0xff, 0xf9, 0x90, 0x64], "consecutive"),
            ("free-bitrate.mp3", [0xff, 0xfb, 0x00, 0x64], "consecutive"),
            ("bad-bitrate.mp3", [0xff, 0xfb, 0xf0, 0x64], "consecutive"),
            ("bad-rate.mp3", [0xff, 0xfb, 0x9c, 0x64], "consecutive"),
            ("bad.wav", "not a wav"u8.ToArray(), "RIFF"),
            ("short-fmt.wav", Wav(formatSize: 8), "format chunk"),
            ("no-data.wav", Wav(includeData: false), "audio data"),
            ("no-format.wav", WavDataOnly(), "supported format"),
            ("bad-format.wav", Wav(formatMutationOffset: 0), "supported format"),
            ("bad-channels.wav", Wav(formatMutationOffset: 2), "supported format"),
            ("bad-sample-rate.wav", Wav(formatMutationOffset: 4), "supported format"),
            ("bad-block-align.wav", Wav(formatMutationOffset: 12), "supported format"),
            ("bad-bits.wav", Wav(formatMutationOffset: 14), "supported format"),
            ("beyond.wav", WavChunkBeyondFile(), "beyond the file"),
            ("bad.json", "{"u8.ToArray(), "unreadable"),
            ("duplicate.json", "{\"x\":1,\"x\":2}"u8.ToArray(), "repeats JSON field"),
            ("bad.txt", [0xff], "DecoderFallbackException"),
        };
        foreach (var item in cases)
        {
            WithTemporaryDirectory(root =>
            {
                WriteBytes(root, "assets/" + item.Name, item.Value);
                WritePolicy(
                    root,
                    Rule(
                        "excluded-media",
                        item.Name,
                        runtimeUse: "none",
                        shipStatus: "excluded",
                        rights: NotApplicableRights()));

                using var document = JsonDocument.Parse(ContentInventoryCheck.BuildInventoryJson(root));
                var asset = document.RootElement.GetProperty("assets")[0];

                Assert.True(
                    asset.GetProperty("integrityStatus").GetString() == "invalid",
                    $"{item.Name} was unexpectedly accepted: "
                        + asset.GetProperty("integrityDetail").GetString());
                Assert.Contains(
                    item.Detail,
                    asset.GetProperty("integrityDetail").GetString()!,
                    StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    [Fact]
    public void Missing_empty_oversized_and_nonfile_layouts_fail_closed()
    {
        WithTemporaryDirectory(root =>
        {
            WritePolicy(root, Rule("files", "*.txt"));
            var missing = ContentInventoryCheck.Write(root);
            Assert.False(missing.Passed);
            Assert.Contains("asset root does not exist", Assert.Single(missing.Failures), StringComparison.Ordinal);

            Directory.CreateDirectory(Path.Combine(root, "assets"));
            var empty = ContentInventoryCheck.Write(root);
            Assert.False(empty.Passed);
            Assert.Contains("contains no files", Assert.Single(empty.Failures), StringComparison.Ordinal);

            var largePath = Path.Combine(root, "assets", "large.txt");
            using (var output = new FileStream(largePath, FileMode.Create, FileAccess.Write))
            {
                output.SetLength((256L * 1024 * 1024) + 1);
            }

            var oversized = ContentInventoryCheck.Write(root);
            Assert.False(oversized.Passed);
            Assert.Contains("file limit", Assert.Single(oversized.Failures), StringComparison.Ordinal);
        });

        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "assets/file.txt", "value");
            WritePolicy(root, Rule("files", "*.txt"));
            Assert.True(ContentInventoryCheck.Write(root).Passed);
            var inventory = Path.Combine(root, "config", "content_inventory.json");
            File.Delete(inventory);
            Directory.CreateDirectory(inventory);

            var result = ContentInventoryCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Contains("does not exist", Assert.Single(result.Failures), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Tree_entry_bound_rejects_path_explosion()
    {
        WithTemporaryDirectory(root =>
        {
            for (var index = 0; index < 4097; index++)
            {
                WriteFile(root, $"assets/{index:D4}.txt", "x");
            }

            WritePolicy(root, Rule("files", "*.txt"));

            var result = ContentInventoryCheck.Write(root);

            Assert.False(result.Passed);
            Assert.Contains("4096-entry", Assert.Single(result.Failures), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Glob_double_star_and_question_have_closed_posix_semantics()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "assets/one/a.json", "{}");
            WriteFile(root, "assets/two/nested/b1.txt", "text");
            WritePolicy(
                root,
                Rule("json", "**/*.json"),
                Rule("text", "two/**/b?.txt"));

            using var document = JsonDocument.Parse(ContentInventoryCheck.BuildInventoryJson(root));

            Assert.Equal(2, document.RootElement.GetProperty("fileCount").GetInt32());
        });
    }

    [Fact]
    public void Links_in_asset_policy_and_inventory_paths_fail_closed_when_supported()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "outside/file.txt", "value");
            WritePolicy(root, Rule("files", "*.txt"));
            var assetLink = Path.Combine(root, "assets");
            if (!TryCreateDirectoryLink(assetLink, Path.Combine(root, "outside")))
            {
                return;
            }

            var result = ContentInventoryCheck.Write(root);

            Assert.False(result.Passed);
            Assert.Contains("link", Assert.Single(result.Failures), StringComparison.Ordinal);
        });

        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "assets/file.txt", "value");
            WriteFile(root, "outside-policy.json", "{}");
            var policyLink = Path.Combine(root, "config", "content_policy.json");
            Directory.CreateDirectory(Path.GetDirectoryName(policyLink)!);
            if (!TryCreateFileLink(policyLink, Path.Combine(root, "outside-policy.json")))
            {
                return;
            }

            var result = ContentInventoryCheck.Write(root);

            Assert.False(result.Passed);
            Assert.Contains("link", Assert.Single(result.Failures), StringComparison.Ordinal);
        });

        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "assets/file.txt", "value");
            WritePolicy(root, Rule("files", "*.txt"));
            WriteFile(root, "outside-inventory.json", "{}");
            var inventoryLink = Path.Combine(root, "config", "content_inventory.json");
            if (!TryCreateFileLink(inventoryLink, Path.Combine(root, "outside-inventory.json")))
            {
                return;
            }

            var result = ContentInventoryCheck.Write(root);

            Assert.False(result.Passed);
            Assert.Contains("link", Assert.Single(result.Failures), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Missing_and_invalid_repository_roots_return_deterministic_failures()
    {
        foreach (var root in new[]
        {
            string.Empty,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            "bad\0root",
        })
        {
            var result = ContentInventoryCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Single(result.Failures);
        }
    }

    [Fact]
    public void Current_repository_inventory_matches_the_native_snapshot()
    {
        var root = ResolveRepositoryRoot();

        var result = ContentInventoryCheck.Inspect(root);
        var json = ContentInventoryCheck.BuildInventoryJson(root);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        Assert.Contains("files=114", result.SuccessMessage, StringComparison.Ordinal);
        Assert.Contains("bytes=342510815", result.SuccessMessage, StringComparison.Ordinal);
        Assert.Equal("02c16c26bbbd960814546653d846cd24fa2c210b4696a3b8125d6ad56af1ff4f", hash);
        Assert.Equal(106, ContentInventoryCheck.FindReleaseBlockers(root).Count);
    }

    private static void ApplyPolicyVariant(JsonObject policy, string variant)
    {
        var rules = policy["rules"]!.AsArray();
        var rule = rules[0]!.AsObject();
        switch (variant)
        {
            case "schema":
                policy["schemaVersion"] = 2;
                break;
            case "schema-string":
                policy["schemaVersion"] = "1";
                break;
            case "extra-policy-field":
                policy["extra"] = true;
                break;
            case "rules-not-array":
                policy["rules"] = new JsonObject();
                break;
            case "empty-rules":
                policy["rules"] = new JsonArray();
                break;
            case "too-many-rules":
                policy["rules"] = new JsonArray(
                    Enumerable.Range(0, 1025)
                        .Select(index => (JsonNode?)Rule($"rule-{index}", $"{index}.txt"))
                        .ToArray());
                break;
            case "rule-not-object":
                policy["rules"] = new JsonArray("not-an-object");
                break;
            case "unsafe-root":
                policy["assetRoot"] = "../assets";
                break;
            case "absolute-root":
                policy["assetRoot"] = "/assets";
                break;
            case "glob-root":
                policy["assetRoot"] = "asset*";
                break;
            case "trailing-root":
                policy["assetRoot"] = "assets/";
                break;
            case "backslash-root":
                policy["assetRoot"] = "assets\\nested";
                break;
            case "colon-root":
                policy["assetRoot"] = "C:assets";
                break;
            case "double-slash-root":
                policy["assetRoot"] = "assets//nested";
                break;
            case "extra-rule-field":
                rule["extra"] = true;
                break;
            case "missing-rule-field":
                rule.Remove("role");
                break;
            case "duplicate-rule-id":
                rules.Add(Rule("files", "other.txt"));
                break;
            case "patterns-not-array":
                rule["patterns"] = "*.txt";
                break;
            case "empty-patterns":
                rule["patterns"] = new JsonArray();
                break;
            case "too-many-patterns":
                rule["patterns"] = new JsonArray(
                    Enumerable.Range(0, 257)
                        .Select(index => (JsonNode?)$"{index}.txt")
                        .ToArray());
                break;
            case "pattern-not-string":
                rule["patterns"] = new JsonArray(1);
                break;
            case "duplicate-pattern":
                rule["patterns"] = new JsonArray("*.txt", "*.txt");
                break;
            case "unsafe-pattern":
                rule["patterns"] = new JsonArray("../*.txt");
                break;
            case "trailing-pattern":
                rule["patterns"] = new JsonArray("folder/");
                break;
            case "backslash-pattern":
                rule["patterns"] = new JsonArray("folder\\*.txt");
                break;
            case "colon-pattern":
                rule["patterns"] = new JsonArray("C:*.txt");
                break;
            case "double-slash-pattern":
                rule["patterns"] = new JsonArray("folder//*.txt");
                break;
            case "invalid-runtime":
                rule["runtimeUse"] = "sometimes";
                break;
            case "invalid-ship":
                rule["shipStatus"] = "maybe";
                break;
            case "invalid-rights":
                rule["rights"]!["status"] = "unknown";
                break;
            case "approved-uncleared":
                rule["rights"] = UnverifiedRights();
                break;
            case "excluded-runtime":
                rule["shipStatus"] = "excluded";
                break;
            case "missing-rights-field":
                rule["rights"]!.AsObject().Remove("source");
                break;
            case "rights-not-object":
                rule["rights"] = "not-an-object";
                break;
            case "blank-text":
                rule["role"] = " ";
                break;
            case "long-text":
                rule["role"] = new string('x', 4097);
                break;
            default:
                throw new InvalidOperationException(variant);
        }
    }

    private static JsonObject Policy(params JsonObject[] rules) =>
        new()
        {
            ["schemaVersion"] = 1,
            ["assetRoot"] = "assets",
            ["rules"] = new JsonArray(rules.Select(rule => (JsonNode?)rule).ToArray()),
        };

    private static JsonObject Rule(
        string id,
        string pattern,
        string runtimeUse = "required",
        string shipStatus = "approved",
        JsonObject? rights = null) =>
        new()
        {
            ["id"] = id,
            ["patterns"] = new JsonArray(pattern),
            ["role"] = "test-role",
            ["packId"] = "test-pack",
            ["runtimeUse"] = runtimeUse,
            ["shipStatus"] = shipStatus,
            ["rights"] = rights ?? ClearedRights(),
        };

    private static JsonObject ClearedRights() =>
        new()
        {
            ["status"] = "cleared",
            ["source"] = "test fixture",
            ["license"] = "MIT",
            ["attribution"] = "none",
            ["reviewNote"] = "fixture rights are explicit",
        };

    private static JsonObject UnverifiedRights() =>
        new()
        {
            ["status"] = "unverified",
            ["source"] = "unknown fixture source",
            ["license"] = "UNVERIFIED",
            ["attribution"] = "REVIEW_REQUIRED",
            ["reviewNote"] = "fixture is intentionally blocked",
        };

    private static JsonObject NotApplicableRights() =>
        new()
        {
            ["status"] = "not-applicable",
            ["source"] = "test fixture",
            ["license"] = "NOT_FOR_DISTRIBUTION",
            ["attribution"] = "none",
            ["reviewNote"] = "invalid on purpose",
        };

    private static void WritePolicy(string root, params JsonObject[] rules) =>
        WriteJson(root, "config/content_policy.json", Policy(rules));

    private static void WriteJson(string root, string relativePath, JsonNode value) =>
        WriteFile(root, relativePath, value.ToJsonString() + "\n");

    private static byte[] Png(
        uint width = 2,
        uint height = 3,
        bool includeC2pa = false,
        bool splitImageData = false)
    {
        var scanlines = new byte[checked((int)(height * ((width * 3) + 1)))];
        for (var row = 0; row < height; row++)
        {
            scanlines[checked((int)(row * ((width * 3) + 1)))] = 0;
        }

        var imageData = Compress(scanlines);
        var chunks = new List<byte[]> { PngChunk("IHDR", PngHeader(width, height, 8, 2)) };
        if (includeC2pa)
        {
            chunks.Add(PngChunk("caBX", "test-jumbf"u8.ToArray()));
        }

        if (splitImageData)
        {
            var split = Math.Max(1, imageData.Length / 2);
            chunks.Add(PngChunk("IDAT", imageData[..split]));
            chunks.Add(PngChunk("IDAT", imageData[split..]));
        }
        else
        {
            chunks.Add(PngChunk("IDAT", imageData));
        }

        chunks.Add(PngChunk("IEND", []));
        return PngFromChunks(chunks.ToArray());
    }

    private static byte[] IndexedPng(bool includePalette = true, byte[]? imageData = null)
    {
        var chunks = new List<byte[]> { PngChunk("IHDR", PngHeader(2, 1, 1, 3)) };
        if (includePalette)
        {
            chunks.Add(PngChunk("PLTE", [0, 0, 0, 255, 255, 255]));
        }

        chunks.Add(PngChunk("IDAT", imageData ?? Compress([0, 0])));
        chunks.Add(PngChunk("IEND", []));
        return PngFromChunks(chunks.ToArray());
    }

    private static byte[] SimplePng(byte[] header, byte[] scanlines) =>
        PngFromChunks(
            PngChunk("IHDR", header),
            PngChunk("IDAT", Compress(scanlines)),
            PngChunk("IEND", []));

    private static byte[] PngHeader(
        uint width,
        uint height,
        byte bitDepth,
        byte colorType,
        byte interlace = 0)
    {
        var value = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(value, width);
        BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(4), height);
        value[8] = bitDepth;
        value[9] = colorType;
        value[12] = interlace;
        return value;
    }

    private static byte[] PngChunk(string type, byte[] value)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var output = new byte[checked(12 + value.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(output, checked((uint)value.Length));
        typeBytes.CopyTo(output, 4);
        value.CopyTo(output, 8);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8 + value.Length), Crc32(typeBytes, value));
        return output;
    }

    private static byte[] PngFromChunks(params byte[][] chunks) =>
        PngSignature().Concat(chunks.SelectMany(chunk => chunk)).ToArray();

    private static byte[] PngSignature() => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private static uint Crc32(params byte[][] values)
    {
        var crc = uint.MaxValue;
        foreach (var value in values.SelectMany(item => item))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }

    private static byte[] Compress(byte[] value)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(value);
        }

        return output.ToArray();
    }

    private static byte[] Wav(
        int formatSize = 16,
        bool includeData = true,
        int? formatMutationOffset = null)
    {
        using var output = new MemoryStream();
        output.Write("RIFF"u8);
        output.Write(new byte[4]);
        output.Write("WAVE"u8);
        output.Write("fmt "u8);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, checked((uint)formatSize));
        output.Write(size);
        var format = new byte[Math.Max(formatSize, 0)];
        if (format.Length >= 16)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(format, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(4), 8000);
            BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(8), 8000);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), 8);
            if (formatMutationOffset is not null)
            {
                format[formatMutationOffset.Value] = 0;
                if (formatMutationOffset.Value + 1 < format.Length)
                {
                    format[formatMutationOffset.Value + 1] = 0;
                }

                if (formatMutationOffset.Value == 4)
                {
                    format.AsSpan(4, 4).Clear();
                }
            }
        }

        output.Write(format);
        if (includeData)
        {
            output.Write("data"u8);
            BinaryPrimitives.WriteUInt32LittleEndian(size, 1);
            output.Write(size);
            output.WriteByte(0x80);
        }

        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)(result.Length - 8)));
        return result;
    }

    private static byte[] WavDataOnly()
    {
        var value = new byte[21];
        "RIFF"u8.CopyTo(value);
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4), 13);
        "WAVE"u8.CopyTo(value.AsSpan(8));
        "data"u8.CopyTo(value.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(16), 1);
        value[20] = 0x80;
        return value;
    }

    private static byte[] WavChunkBeyondFile()
    {
        var value = new byte[20];
        "RIFF"u8.CopyTo(value);
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4), 12);
        "WAVE"u8.CopyTo(value.AsSpan(8));
        "fmt "u8.CopyTo(value.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(16), 100);
        return value;
    }

    private static byte[] Mp3Frame(byte[]? header = null, int? minimumLength = null)
    {
        header ??= [0xff, 0xfb, 0x90, 0x64];
        var length = minimumLength ?? (144 * 128_000) / 44_100;
        var frame = new byte[length];
        header.CopyTo(frame, 0);
        return frame;
    }

    private static byte[] Id3Mp3(bool includeFooter)
    {
        var header = new byte[10];
        "ID3"u8.CopyTo(header);
        header[3] = 4;
        if (includeFooter)
        {
            header[5] = 0x10;
        }

        return includeFooter
            ? [.. header, .. new byte[10], .. Mp3Frame(), .. Mp3Frame()]
            : [.. header, .. Mp3Frame(), .. Mp3Frame()];
    }

    private static void WriteFile(string root, string relativePath, string value)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value, new UTF8Encoding(false));
    }

    private static void WriteBytes(string root, string relativePath, byte[] value)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, value);
    }

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-content-inventory-checks",
            Guid.NewGuid().ToString("N"));
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

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VERSION"))
                && Directory.Exists(Path.Combine(directory.FullName, "native")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
