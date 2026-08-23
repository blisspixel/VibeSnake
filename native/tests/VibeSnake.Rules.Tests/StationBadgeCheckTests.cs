using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class StationBadgeCheckTests
{
    private static readonly Dictionary<string, string> ExpectedHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chaos_theory"] = "07f209f40d3f5e4fb2c169c602dc37c3cd603b77c2cb3168080d0f2f22f77fea",
            ["flow_signal"] = "b61db22743b0ec264f16d159d63d3b6f39ce791e5a5d99842bf49d1d83b89535",
            ["global_coil"] = "3650310e9b946e14421542fe6dc2616204dc10a19f5552d0e362f11e9c3d53cb",
            ["ourotron"] = "3c0221bbd4bf44e41f97fbca7b31be8d4cff153fb6ad2702e4c051faadfdfc39",
            ["the_bureau"] = "81259c50a7ca193bae646b92b782e7331c632d54cc91878c7bfeaba057dc2f81",
            ["the_pit"] = "412572276f5e81eff22ec403f4b1f96ec0471afae0f1545272dfcb5b465eb539",
            ["the_strike"] = "7203f4932e293cecc71fda0d8d4bacaa2841b62c92a58a100ee1e2ca0e7c3aee",
            ["underground_scales"] = "2e0d01372211b0db4a32419ddcf805ee4e7273b9e31ceec14a2c589eabfd99fa",
        };

    [Fact]
    public void Badge_catalog_and_renderer_have_stable_closed_outputs()
    {
        Assert.Equal(8, StationBadgeCheck.Definitions.Count);
        Assert.Equal(
            StationBadgeCheck.Definitions.Count,
            StationBadgeCheck.Definitions.Select(definition => definition.Key).Distinct().Count());
        Assert.Equal(
            Enum.GetValues<StationBadgeCheck.StationBadgeStyle>().Order(),
            StationBadgeCheck.Definitions.Select(definition => definition.Style).Order());

        foreach (var definition in StationBadgeCheck.Definitions)
        {
            var pixels = StationBadgeCheck.RenderPixels(definition);
            var png = StationBadgeCheck.RenderPng(definition);

            Assert.Equal(
                StationBadgeCheck.BadgeWidth * StationBadgeCheck.BadgeHeight * 3,
                pixels.Length);
            Assert.Equal(270_388, png.Length);
            Assert.Equal(ExpectedHashes[definition.Key], Sha256(png));
            Assert.True(pixels.Distinct().Count() > 1);
        }
    }

    [Fact]
    public void Canonical_png_stream_is_valid_rgb_and_uses_unfiltered_rows()
    {
        var definition = StationBadgeCheck.Definitions.Single(item => item.Key == "flow_signal");
        var expectedPixels = StationBadgeCheck.RenderPixels(definition);
        var png = StationBadgeCheck.RenderPng(definition);
        var chunks = ReadChunks(png);

        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(chunk => chunk.Type));
        Assert.All(chunks, chunk => Assert.Equal(chunk.ExpectedCrc, chunk.ActualCrc));

        var header = chunks[0].Payload;
        Assert.Equal((uint)StationBadgeCheck.BadgeWidth, BinaryPrimitives.ReadUInt32BigEndian(header));
        Assert.Equal((uint)StationBadgeCheck.BadgeHeight, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
        Assert.Equal([8, 2, 0, 0, 0], header.AsSpan(8).ToArray());

        using var compressed = new MemoryStream(chunks[1].Payload);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        zlib.CopyTo(decoded);
        var scanlines = decoded.ToArray();
        var rowBytes = StationBadgeCheck.BadgeWidth * 3;
        Assert.Equal((rowBytes + 1) * StationBadgeCheck.BadgeHeight, scanlines.Length);
        var reconstructed = new byte[expectedPixels.Length];
        for (var row = 0; row < StationBadgeCheck.BadgeHeight; row++)
        {
            Assert.Equal(0, scanlines[row * (rowBytes + 1)]);
            scanlines.AsSpan((row * (rowBytes + 1)) + 1, rowBytes)
                .CopyTo(reconstructed.AsSpan(row * rowBytes));
        }

        Assert.Equal(expectedPixels, reconstructed);
    }

    [Fact]
    public void Writer_is_repeatable_atomic_and_self_verifying()
    {
        WithTemporaryDirectory(root =>
        {
            var first = StationBadgeCheck.Write(root);
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.Equal(
                "Station badges generated: files=8 size=300x300.",
                first.SuccessMessage);
            var firstHashes = BadgeFiles(root).ToDictionary(
                path => Path.GetFileName(path)!,
                path => Sha256(File.ReadAllBytes(path)),
                StringComparer.Ordinal);

            var second = StationBadgeCheck.Write(root);
            var inspection = StationBadgeCheck.Inspect(root);
            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.True(inspection.Passed, string.Join(Environment.NewLine, inspection.Failures));
            Assert.Equal("Station badges verified: files=8 size=300x300.", inspection.SuccessMessage);
            Assert.Equal(8, firstHashes.Count);
            Assert.Equal(firstHashes, BadgeFiles(root).ToDictionary(
                path => Path.GetFileName(path)!,
                path => Sha256(File.ReadAllBytes(path)),
                StringComparer.Ordinal));
            Assert.Empty(Directory.EnumerateFiles(BadgeDirectory(root), "*.tmp"));
        });
    }

    [Fact]
    public void Freshness_check_rejects_missing_changed_and_oversized_badges()
    {
        WithTemporaryDirectory(root =>
        {
            WriteBadges(root);
            var path = BadgeFiles(root).First();

            File.Delete(path);
            var missing = StationBadgeCheck.Inspect(root);
            Assert.False(missing.Passed);
            Assert.Contains(
                missing.Failures,
                failure => failure.EndsWith("required badge is missing", StringComparison.Ordinal));

            WriteBadges(root);
            var bytes = File.ReadAllBytes(path);
            bytes[^1] ^= 0xff;
            File.WriteAllBytes(path, bytes);
            var changed = StationBadgeCheck.Inspect(root);
            Assert.False(changed.Passed);
            Assert.Contains(
                changed.Failures,
                failure => failure.EndsWith("stale or noncanonical", StringComparison.Ordinal));

            File.WriteAllBytes(path, new byte[(512 * 1024) + 1]);
            var oversized = StationBadgeCheck.Inspect(root);
            Assert.False(oversized.Passed);
            Assert.Contains(
                oversized.Failures,
                failure => failure.Contains("524288-byte", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Exact_bytes_reject_valid_metadata_only_png_changes()
    {
        WithTemporaryDirectory(root =>
        {
            WriteBadges(root);
            var path = BadgeFiles(root).First();
            var original = File.ReadAllBytes(path);
            File.WriteAllBytes(path, InsertChunkBeforeIend(original, "tEXt", "generator\0alternate"u8));

            var chunks = ReadChunks(File.ReadAllBytes(path));
            Assert.Equal(["IHDR", "IDAT", "tEXt", "IEND"], chunks.Select(chunk => chunk.Type));
            Assert.All(chunks, chunk => Assert.Equal(chunk.ExpectedCrc, chunk.ActualCrc));
            var result = StationBadgeCheck.Inspect(root);
            Assert.False(result.Passed);
            Assert.Contains(
                result.Failures,
                failure => failure.EndsWith("stale or noncanonical", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Layout_check_rejects_unexpected_badges_and_preserves_existing_outputs()
    {
        WithTemporaryDirectory(root =>
        {
            WriteBadges(root);
            var before = BadgeFiles(root).ToDictionary(
                path => Path.GetFileName(path)!,
                path => Sha256(File.ReadAllBytes(path)),
                StringComparer.Ordinal);
            File.WriteAllBytes(Path.Combine(BadgeDirectory(root), "unknown_badge.png"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(BadgeDirectory(root), "notes.txt"), "permitted\n");

            var inspection = StationBadgeCheck.Inspect(root);
            var write = StationBadgeCheck.Write(root);
            Assert.False(inspection.Passed);
            Assert.False(write.Passed);
            Assert.Contains(
                inspection.Failures,
                failure => failure.EndsWith("unexpected station badge", StringComparison.Ordinal));
            Assert.Equal(before, BadgeFiles(root)
                .Where(path => Path.GetFileName(path) != "unknown_badge.png")
                .ToDictionary(
                    path => Path.GetFileName(path)!,
                    path => Sha256(File.ReadAllBytes(path)),
                    StringComparer.Ordinal));
        });
    }

    [Fact]
    public void Layout_check_is_bounded_and_requires_regular_files()
    {
        WithTemporaryDirectory(root =>
        {
            WriteBadges(root);
            for (var index = 0; index < 65; index++)
            {
                File.WriteAllText(Path.Combine(BadgeDirectory(root), $"entry-{index:00}.txt"), "x");
            }

            var bounded = StationBadgeCheck.Inspect(root);
            Assert.False(bounded.Passed);
            Assert.Contains(bounded.Failures, failure => failure.Contains("64-entry", StringComparison.Ordinal));
        });

        WithTemporaryDirectory(root =>
        {
            WriteBadges(root);
            var path = BadgeFiles(root).First();
            File.Delete(path);
            Directory.CreateDirectory(path);

            var result = StationBadgeCheck.Inspect(root);
            Assert.False(result.Passed);
            Assert.Contains(
                result.Failures,
                failure => failure.EndsWith("badge must be a regular file", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Fixed_directory_and_repository_root_fail_closed()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Contains(
            "existing directory",
            StationBadgeCheck.Inspect(missingRoot).Failures.Single(),
            StringComparison.Ordinal);
        Assert.Contains(
            "invalid",
            StationBadgeCheck.Inspect(null!).Failures.Single(),
            StringComparison.Ordinal);

        WithTemporaryDirectory(root =>
        {
            var path = BadgeDirectory(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "not a directory");

            var inspection = StationBadgeCheck.Inspect(root);
            var write = StationBadgeCheck.Write(root);
            Assert.False(inspection.Passed);
            Assert.False(write.Passed);
            Assert.Contains(inspection.Failures, failure => failure.Contains("must be a directory"));
        });
    }

    [Fact]
    public void Link_badges_and_linked_ancestors_are_rejected_when_supported()
    {
        WithTemporaryDirectory(root =>
        {
            WriteBadges(root);
            var files = BadgeFiles(root);
            File.Delete(files[0]);
            if (!TryCreateFileLink(files[0], files[1]))
            {
                return;
            }

            var result = StationBadgeCheck.Inspect(root);
            Assert.False(result.Passed);
            Assert.Contains(
                result.Failures,
                failure => failure.EndsWith("links are not allowed", StringComparison.Ordinal));
        });

        WithTemporaryDirectory(root =>
        {
            var target = Path.Combine(root, "real-assets");
            Directory.CreateDirectory(target);
            var assets = Path.Combine(root, "assets");
            if (!TryCreateDirectoryLink(assets, target))
            {
                return;
            }

            var result = StationBadgeCheck.Write(root);
            Assert.False(result.Passed);
            Assert.Contains(
                result.Failures,
                failure => failure.EndsWith("links are not allowed", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Renderer_rejects_unsupported_text_style_and_color()
    {
        var template = StationBadgeCheck.Definitions[0];
        var text = template with { Name = "BAD?" };
        var style = template with { Style = (StationBadgeCheck.StationBadgeStyle)999 };

        Assert.Contains(
            "unsupported characters",
            Assert.Throws<InvalidDataException>(() => StationBadgeCheck.RenderPixels(text)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Unknown station badge style",
            Assert.Throws<InvalidDataException>(() => StationBadgeCheck.RenderPixels(style)).Message,
            StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => StationBadgeCheck.Rgb.Parse("xyz"));
        Assert.Throws<InvalidDataException>(() => StationBadgeCheck.Rgb.Parse("fffffff"));
        Assert.Throws<ArgumentNullException>(() => StationBadgeCheck.RenderPixels(null!));
    }

    [Fact]
    public void Badge_commands_generate_check_report_and_validate_arguments()
    {
        WithTemporaryDirectory(root =>
        {
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(0, RepositoryCheckCommand.Run(["badge-write", root], output, error));
            Assert.Contains("Station badges generated", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            output = new StringWriter();
            Assert.Equal(0, RepositoryCheckCommand.Run(["badges", root], output, error));
            Assert.Contains("Station badges verified", output.ToString(), StringComparison.Ordinal);

            File.Delete(BadgeFiles(root).First());
            error = new StringWriter();
            Assert.Equal(1, RepositoryCheckCommand.Run(["badges", root], new StringWriter(), error));
            Assert.Contains("Station badges check failed", error.ToString(), StringComparison.Ordinal);

            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["badge-write", root, "extra"],
                    new StringWriter(),
                    error));
            Assert.Contains("Usage:", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static byte[] InsertChunkBeforeIend(
        byte[] png,
        string type,
        ReadOnlySpan<byte> payload)
    {
        var iendOffset = png.Length - 12;
        var chunk = new byte[12 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, checked((uint)payload.Length));
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        payload.CopyTo(chunk.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(
            chunk.AsSpan(8 + payload.Length),
            Crc32(chunk.AsSpan(4, 4 + payload.Length)));
        return [.. png.AsSpan(0, iendOffset), .. chunk, .. png.AsSpan(iendOffset)];
    }

    private static List<PngChunk> ReadChunks(byte[] png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        Assert.True(png.AsSpan(0, signature.Length).SequenceEqual(signature));
        var chunks = new List<PngChunk>();
        var offset = signature.Length;
        while (offset < png.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var payload = png.AsSpan(offset + 8, length).ToArray();
            var actualCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
            var expectedCrc = Crc32(png.AsSpan(offset + 4, 4 + length));
            chunks.Add(new PngChunk(type, payload, expectedCrc, actualCrc));
            offset += 12 + length;
        }

        Assert.Equal(png.Length, offset);
        return chunks;
    }

    private static uint Crc32(ReadOnlySpan<byte> payload)
    {
        var checksum = uint.MaxValue;
        foreach (var value in payload)
        {
            checksum ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                checksum = (checksum & 1) != 0
                    ? (checksum >> 1) ^ 0xedb88320u
                    : checksum >> 1;
            }
        }

        return ~checksum;
    }

    private static string Sha256(byte[] payload) =>
        Convert.ToHexStringLower(SHA256.HashData(payload));

    private static string BadgeDirectory(string root) =>
        Path.Combine(root, "assets", "images", "radio_badges");

    private static string[] BadgeFiles(string root) =>
        Directory.GetFiles(BadgeDirectory(root), "*_badge.png").Order(StringComparer.Ordinal).ToArray();

    private static void WriteBadges(string root)
    {
        var result = StationBadgeCheck.Write(root);
        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
    }

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

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-station-badges",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record PngChunk(
        string Type,
        byte[] Payload,
        uint ExpectedCrc,
        uint ActualCrc);
}
