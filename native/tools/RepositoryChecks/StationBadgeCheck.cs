using System.Buffers.Binary;
using System.Text;

namespace RepositoryChecks;

public static class StationBadgeCheck
{
    public const string RelativeDirectory = "assets/images/radio_badges";
    public const int BadgeWidth = 300;
    public const int BadgeHeight = 300;

    private const int MaximumDirectoryEntries = 64;
    private const int MaximumBadgeBytes = 512 * 1024;
    private const int SineScale = 10_000;

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private static readonly Dictionary<char, string> Glyphs =
        new Dictionary<char, string>
        {
            [' '] = "00000000000000000000000000000000000",
            ['A'] = "01110100011000111111100011000110001",
            ['B'] = "11110100011000111110100011000111110",
            ['C'] = "01111100001000010000100001000001111",
            ['D'] = "11110100011000110001100011000111110",
            ['E'] = "11111100001000011110100001000011111",
            ['F'] = "11111100001000011110100001000010000",
            ['G'] = "01111100001000010111100011000101111",
            ['H'] = "10001100011000111111100011000110001",
            ['I'] = "11111001000010000100001000010011111",
            ['K'] = "10001100101010011000101001001010001",
            ['L'] = "10000100001000010000100001000011111",
            ['M'] = "10001110111010110001100011000110001",
            ['N'] = "10001110011100110101100111001110001",
            ['O'] = "01110100011000110001100011000101110",
            ['P'] = "11110100011000111110100001000010000",
            ['R'] = "11110100011000111110101001001010001",
            ['S'] = "01111100001000001110000010000111110",
            ['T'] = "11111001000010000100001000010000100",
            ['U'] = "10001100011000110001100011000101110",
            ['V'] = "10001100011000110001100010101000100",
            ['W'] = "10001100011000110001101011101110001",
            ['Y'] = "10001100010101000100001000010000100",
        };

    private static readonly int[] SineDegrees =
    [
        0, 175, 349, 523, 698, 872, 1045, 1219, 1392, 1564,
        1736, 1908, 2079, 2250, 2419, 2588, 2756, 2924, 3090, 3256,
        3420, 3584, 3746, 3907, 4067, 4226, 4384, 4540, 4695, 4848,
        5000, 5150, 5299, 5446, 5592, 5736, 5878, 6018, 6157, 6293,
        6428, 6561, 6691, 6820, 6947, 7071, 7193, 7314, 7431, 7547,
        7660, 7771, 7880, 7986, 8090, 8192, 8290, 8387, 8480, 8572,
        8660, 8746, 8829, 8910, 8988, 9063, 9135, 9205, 9272, 9336,
        9397, 9455, 9511, 9563, 9613, 9659, 9703, 9744, 9781, 9816,
        9848, 9877, 9903, 9925, 9945, 9962, 9976, 9986, 9994, 9998,
        10000,
    ];

    private static readonly int[] PitWaveformSpikes =
    [
        64, 35, 65, 35, 87, 33, 87, 87, 23, 68,
        78, 80, 30, 82, 57, 60, 58, 37, 48, 89,
        24, 60, 63, 34, 45, 39, 78, 51, 62, 69,
    ];

    internal static IReadOnlyList<StationBadgeDefinition> Definitions { get; } =
    [
        new(
            "flow_signal",
            "Flow Signal",
            "Future Focus",
            Rgb.Parse("1a0033"),
            Rgb.Parse("ff00ff"),
            Rgb.Parse("00ffff"),
            Rgb.Parse("ffffff"),
            StationBadgeStyle.GradientWave),
        new(
            "chaos_theory",
            "Chaos Theory",
            "All Hiss",
            Rgb.Parse("000000"),
            Rgb.Parse("ffd700"),
            Rgb.Parse("ff4500"),
            Rgb.Parse("ffffff"),
            StationBadgeStyle.Vinyl),
        new(
            "global_coil",
            "Global Coil",
            "One Rhythm",
            Rgb.Parse("004d00"),
            Rgb.Parse("00ff00"),
            Rgb.Parse("ffff00"),
            Rgb.Parse("ffffff"),
            StationBadgeStyle.Radial),
        new(
            "ourotron",
            "Ourotron",
            "Retrowave",
            Rgb.Parse("0a0020"),
            Rgb.Parse("ff006e"),
            Rgb.Parse("8338ec"),
            Rgb.Parse("00f5ff"),
            StationBadgeStyle.RetroGrid),
        new(
            "the_pit",
            "The Pit",
            "Venom Bass",
            Rgb.Parse("000000"),
            Rgb.Parse("00ff00"),
            Rgb.Parse("39ff14"),
            Rgb.Parse("ffffff"),
            StationBadgeStyle.Waveform),
        new(
            "the_bureau",
            "The Bureau",
            "Signal News",
            Rgb.Parse("1a1a2e"),
            Rgb.Parse("ff0000"),
            Rgb.Parse("ffffff"),
            Rgb.Parse("ffffff"),
            StationBadgeStyle.News),
        new(
            "the_strike",
            "The Strike",
            "Molten Rock",
            Rgb.Parse("2d1b2e"),
            Rgb.Parse("ff6b9d"),
            Rgb.Parse("c9ada7"),
            Rgb.Parse("faf3dd"),
            StationBadgeStyle.TapeDeck),
        new(
            "underground_scales",
            "Underground",
            "Scales",
            Rgb.Parse("0d1b2a"),
            Rgb.Parse("00b4d8"),
            Rgb.Parse("90e0ef"),
            Rgb.Parse("caf0f8"),
            StationBadgeStyle.Enso),
    ];

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        var failures = new List<string>();
        if (!TryResolveDirectory(repositoryRoot, create: false, failures, out var directory))
        {
            return Failed(failures);
        }

        var entries = InspectLayout(directory, failures);
        foreach (var definition in Definitions)
        {
            var fileName = FileName(definition);
            if (!entries.TryGetValue(fileName, out var path))
            {
                failures.Add($"{RelativeDirectory}/{fileName}: required badge is missing");
                continue;
            }

            byte[] actual;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaximumBadgeBytes)
                {
                    failures.Add(
                        $"{RelativeDirectory}/{fileName}: badge exceeds the "
                        + $"{MaximumBadgeBytes}-byte limit");
                    continue;
                }

                actual = File.ReadAllBytes(path);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failures.Add(
                    $"{RelativeDirectory}/{fileName}: badge could not be read: "
                    + SingleLine(exception.Message));
                continue;
            }

            var expected = RenderPng(definition);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                failures.Add(
                    $"{RelativeDirectory}/{fileName}: badge bytes are stale or noncanonical");
            }
        }

        var ordered = failures
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return ordered.Length == 0
            ? new RepositoryCheckResult(
                "Station badges",
                true,
                $"Station badges verified: files={Definitions.Count} size={BadgeWidth}x{BadgeHeight}.",
                [])
            : Failed(ordered);
    }

    public static RepositoryCheckResult Write(string repositoryRoot)
    {
        var failures = new List<string>();
        if (!TryResolveDirectory(repositoryRoot, create: true, failures, out var directory))
        {
            return Failed(failures);
        }

        _ = InspectLayout(directory, failures);
        if (failures.Count > 0)
        {
            return Failed(
                failures
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
        }

        foreach (var definition in Definitions)
        {
            var path = Path.Combine(directory, FileName(definition));
            try
            {
                WriteAtomic(path, RenderPng(definition));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failures.Add(
                    $"{RelativeDirectory}/{Path.GetFileName(path)}: badge could not be written: "
                    + SingleLine(exception.Message));
                break;
            }
        }

        if (failures.Count > 0)
        {
            return Failed(failures);
        }

        var verification = Inspect(repositoryRoot);
        return verification.Passed
            ? new RepositoryCheckResult(
                "Station badges",
                true,
                $"Station badges generated: files={Definitions.Count} size={BadgeWidth}x{BadgeHeight}.",
                [])
            : verification;
    }

    internal static byte[] RenderPixels(StationBadgeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var surface = new BadgeSurface(BadgeWidth, BadgeHeight, definition.Background);
        DrawStyle(surface, definition);
        surface.DrawRectangleOutline(5, 5, BadgeWidth - 6, BadgeHeight - 6, definition.Text, 3);
        DrawCenteredText(surface, definition.Name, BadgeHeight / 4, definition.Text, 4);
        DrawCenteredText(surface, definition.Tagline, 3 * BadgeHeight / 4, definition.Text, 3);
        return surface.Pixels;
    }

    internal static byte[] RenderPng(StationBadgeDefinition definition) =>
        EncodePng(RenderPixels(definition), BadgeWidth, BadgeHeight);

    private static bool TryResolveDirectory(
        string repositoryRoot,
        bool create,
        List<string> failures,
        out string directory)
    {
        directory = string.Empty;
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            failures.Add("repository root is invalid");
            return false;
        }

        string root;
        try
        {
            root = Path.GetFullPath(repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            failures.Add("repository root is invalid");
            return false;
        }

        if (!Directory.Exists(root))
        {
            failures.Add("repository root must be an existing directory");
            return false;
        }

        var current = root;
        foreach (var segment in RelativeDirectory.Split('/'))
        {
            current = Path.Combine(current, segment);
            if (!Path.Exists(current))
            {
                continue;
            }

            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    failures.Add(
                        $"{Path.GetRelativePath(root, current).Replace('\\', '/')}: links are not allowed");
                    return false;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failures.Add(
                    $"{Path.GetRelativePath(root, current).Replace('\\', '/')}: "
                    + $"path could not be inspected: {SingleLine(exception.Message)}");
                return false;
            }
        }

        directory = Path.Combine(root, RelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(directory))
        {
            return true;
        }

        if (Path.Exists(directory))
        {
            failures.Add($"{RelativeDirectory}: fixed badge location must be a directory");
            return false;
        }

        if (!create)
        {
            failures.Add($"{RelativeDirectory}: badge directory is missing");
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failures.Add(
                $"{RelativeDirectory}: badge directory could not be created: "
                + SingleLine(exception.Message));
            return false;
        }
    }

    private static Dictionary<string, string> InspectLayout(
        string directory,
        List<string> failures)
    {
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(directory);
            Array.Sort(entries, StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failures.Add(
                $"{RelativeDirectory}: badge directory could not be enumerated: "
                + SingleLine(exception.Message));
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        if (entries.Length > MaximumDirectoryEntries)
        {
            failures.Add(
                $"{RelativeDirectory}: directory exceeds the "
                + $"{MaximumDirectoryEntries}-entry validation limit");
        }

        var expectedNames = Definitions
            .Select(FileName)
            .ToHashSet(StringComparer.Ordinal);
        var badgeEntries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries.Take(MaximumDirectoryEntries))
        {
            var name = Path.GetFileName(entry);
            if (!name.EndsWith("_badge.png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failures.Add(
                    $"{RelativeDirectory}/{name}: badge path could not be inspected: "
                    + SingleLine(exception.Message));
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                failures.Add($"{RelativeDirectory}/{name}: links are not allowed");
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0 || !File.Exists(entry))
            {
                failures.Add($"{RelativeDirectory}/{name}: badge must be a regular file");
                continue;
            }

            if (!expectedNames.Contains(name))
            {
                failures.Add($"{RelativeDirectory}/{name}: unexpected station badge");
                continue;
            }

            badgeEntries[name] = entry;
        }

        return badgeEntries;
    }

    private static void DrawStyle(BadgeSurface surface, StationBadgeDefinition station)
    {
        switch (station.Style)
        {
            case StationBadgeStyle.GradientWave:
                surface.FillGradient(station.Background, station.Accent1);
                for (var waveIndex = 0; waveIndex < 3; waveIndex++)
                {
                    var points = Enumerable.Range(0, surface.Width)
                        .Select(x => new PixelPoint(
                            x,
                            surface.Height / 2
                                + (waveIndex * 20)
                                + RoundFraction(
                                    30L * SinScaled(RoundFraction(x * 180L, 63) + (waveIndex * 115)),
                                    SineScale)))
                        .ToArray();
                    surface.DrawPolyline(points, station.Accent2, 3);
                }

                break;

            case StationBadgeStyle.Vinyl:
                foreach (var radius in new[] { 140, 120, 100, 40 })
                {
                    surface.DrawEllipseOutline(
                        surface.Width / 2 - radius,
                        surface.Height / 2 - radius,
                        surface.Width / 2 + radius,
                        surface.Height / 2 + radius,
                        radius > 50 ? station.Accent1 : station.Background,
                        3);
                }

                break;

            case StationBadgeStyle.Radial:
                foreach (var angle in new[] { 30, 90, 150, 210, 270, 330 })
                {
                    var endpoint = new PixelPoint(
                        surface.Width / 2
                            + RoundFraction(120L * SinScaled(angle + 90), SineScale),
                        surface.Height / 2
                            + RoundFraction(120L * SinScaled(angle), SineScale));
                    surface.DrawLine(
                        new PixelPoint(surface.Width / 2, surface.Height / 2),
                        endpoint,
                        station.Accent1,
                        4);
                }

                break;

            case StationBadgeStyle.RetroGrid:
                surface.FillGradient(station.Background, station.Accent2);
                var vanishingY = surface.Height / 3;
                for (var index = 0; index < 5; index++)
                {
                    var y = surface.Height - (index * (surface.Height / 6));
                    if (y > vanishingY)
                    {
                        surface.DrawPolyline(
                            [
                                new PixelPoint(0, y),
                                new PixelPoint(surface.Width / 2, vanishingY),
                                new PixelPoint(surface.Width, y),
                            ],
                            station.Accent1,
                            2);
                    }
                }

                for (var index = 0; index < 8; index++)
                {
                    var x = index * (surface.Width / 7);
                    surface.DrawLine(
                        new PixelPoint(x, vanishingY),
                        new PixelPoint(x, surface.Height),
                        station.Accent1,
                        2);
                }

                break;

            case StationBadgeStyle.TapeDeck:
                foreach (var reelX in new[] { surface.Width / 3, 2 * surface.Width / 3 })
                {
                    surface.DrawEllipseOutline(reelX - 40, 110, reelX + 40, 190, station.Accent1, 4);
                    surface.FillEllipse(reelX - 15, 135, reelX + 15, 165, station.Accent2);
                }

                break;

            case StationBadgeStyle.Waveform:
                for (var index = 0; index < PitWaveformSpikes.Length; index++)
                {
                    var x = index * 10;
                    var spike = PitWaveformSpikes[index];
                    surface.DrawLine(
                        new PixelPoint(x, surface.Height / 2 - spike),
                        new PixelPoint(x, surface.Height / 2 + spike),
                        station.Accent1,
                        3);
                }

                break;

            case StationBadgeStyle.News:
                surface.FillRectangle(0, 120, surface.Width, 180, station.Accent1);
                surface.DrawLine(
                    new PixelPoint(0, 114),
                    new PixelPoint(surface.Width, 114),
                    station.Accent2,
                    3);
                surface.DrawLine(
                    new PixelPoint(0, 186),
                    new PixelPoint(surface.Width, 186),
                    station.Accent2,
                    3);
                break;

            case StationBadgeStyle.Enso:
                surface.FillGradient(station.Background, station.Accent1);
                var arc = Enumerable.Range(0, 150)
                    .Select(index => 30 + (index * 2))
                    .Select(angle => new PixelPoint(
                        surface.Width / 2
                            + RoundFraction(100L * SinScaled(angle + 90), SineScale),
                        surface.Height / 2
                            + RoundFraction(100L * SinScaled(angle), SineScale)))
                    .ToArray();
                surface.DrawPolyline(arc, station.Accent2, 8);
                break;

            default:
                throw new InvalidDataException($"Unknown station badge style: {station.Style}");
        }
    }

    private static void DrawCenteredText(
        BadgeSurface surface,
        string text,
        int y,
        Rgb color,
        int preferredScale)
    {
        var normalized = text.ToUpperInvariant();
        var missing = normalized
            .Where(character => !Glyphs.ContainsKey(character))
            .Distinct()
            .Order()
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"Badge text contains unsupported characters: {string.Join(", ", missing)}");
        }

        var unitWidth = (normalized.Length * 5) + Math.Max(0, normalized.Length - 1);
        var maximumWidth = surface.Width - 30;
        var scale = Math.Min(preferredScale, Math.Max(1, maximumWidth / Math.Max(1, unitWidth)));
        var x = (surface.Width - (unitWidth * scale)) / 2;
        DrawPixelText(surface, normalized, x + 3, y + 3, Rgb.Black, scale);
        DrawPixelText(surface, normalized, x, y, color, scale);
    }

    private static void DrawPixelText(
        BadgeSurface surface,
        string text,
        int x,
        int y,
        Rgb color,
        int scale)
    {
        for (var characterIndex = 0; characterIndex < text.Length; characterIndex++)
        {
            var glyph = Glyphs[text[characterIndex]];
            var left = x + (characterIndex * 6 * scale);
            for (var row = 0; row < 7; row++)
            {
                for (var column = 0; column < 5; column++)
                {
                    if (glyph[(row * 5) + column] == '1')
                    {
                        surface.FillRectangle(
                            left + (column * scale),
                            y + (row * scale),
                            left + ((column + 1) * scale) - 1,
                            y + ((row + 1) * scale) - 1,
                            color);
                    }
                }
            }
        }
    }

    private static int SinScaled(int degrees)
    {
        var normalized = degrees % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized switch
        {
            <= 90 => SineDegrees[normalized],
            <= 180 => SineDegrees[180 - normalized],
            <= 270 => -SineDegrees[normalized - 180],
            _ => -SineDegrees[360 - normalized],
        };
    }

    private static int RoundFraction(long numerator, int denominator)
    {
        var sign = Math.Sign(numerator);
        var absolute = Math.Abs(numerator);
        var quotient = absolute / denominator;
        var remainder = absolute % denominator;
        if ((remainder * 2) > denominator
            || ((remainder * 2) == denominator && (quotient & 1) != 0))
        {
            quotient++;
        }

        return checked((int)(sign * quotient));
    }

    private static byte[] EncodePng(byte[] pixels, int width, int height)
    {
        if (pixels.Length != checked(width * height * 3))
        {
            throw new ArgumentException("RGB pixel payload has the wrong length.", nameof(pixels));
        }

        var scanlines = new byte[checked((width * 3 + 1) * height)];
        for (var row = 0; row < height; row++)
        {
            var destination = row * ((width * 3) + 1);
            scanlines[destination] = 0;
            pixels.AsSpan(row * width * 3, width * 3)
                .CopyTo(scanlines.AsSpan(destination + 1));
        }

        var imageData = EncodeStoredZlib(scanlines);
        using var output = new MemoryStream();
        output.Write(PngSignature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), checked((uint)height));
        header[8] = 8;
        header[9] = 2;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", imageData);
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static byte[] EncodeStoredZlib(byte[] payload)
    {
        using var output = new MemoryStream(payload.Length + 64);
        output.WriteByte(0x78);
        output.WriteByte(0x01);
        var offset = 0;
        Span<byte> blockHeader = stackalloc byte[4];
        while (offset < payload.Length)
        {
            var length = Math.Min(ushort.MaxValue, payload.Length - offset);
            var final = offset + length == payload.Length;
            output.WriteByte(final ? (byte)0x01 : (byte)0x00);
            BinaryPrimitives.WriteUInt16LittleEndian(blockHeader[..2], checked((ushort)length));
            BinaryPrimitives.WriteUInt16LittleEndian(
                blockHeader[2..],
                checked((ushort)(ushort.MaxValue - length)));
            output.Write(blockHeader);
            output.Write(payload, offset, length);
            offset += length;
        }

        var adler = Adler32(payload);
        Span<byte> trailer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(trailer, adler);
        output.Write(trailer);
        return output.ToArray();
    }

    private static uint Adler32(ReadOnlySpan<byte> payload)
    {
        const uint modulus = 65_521;
        uint first = 1;
        uint second = 0;
        foreach (var value in payload)
        {
            first = (first + value) % modulus;
            second = (second + first) % modulus;
        }

        return (second << 16) | first;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> payload)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)payload.Length));
        output.Write(length);
        output.Write(typeBytes);
        output.Write(payload);

        var checksum = Crc32(typeBytes, payload);
        Span<byte> checksumBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksumBytes, checksum);
        output.Write(checksumBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var checksum = uint.MaxValue;
        checksum = UpdateCrc32(checksum, first);
        checksum = UpdateCrc32(checksum, second);
        return ~checksum;
    }

    private static uint UpdateCrc32(uint checksum, ReadOnlySpan<byte> payload)
    {
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

        return checksum;
    }

    private static void WriteAtomic(string path, byte[] payload)
    {
        var temporary = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string FileName(StationBadgeDefinition definition) =>
        definition.Key + "_badge.png";

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static RepositoryCheckResult Failed(IReadOnlyList<string> failures) =>
        new("Station badges", false, string.Empty, failures);

    internal sealed record StationBadgeDefinition(
        string Key,
        string Name,
        string Tagline,
        Rgb Background,
        Rgb Accent1,
        Rgb Accent2,
        Rgb Text,
        StationBadgeStyle Style);

    internal enum StationBadgeStyle
    {
        GradientWave,
        Vinyl,
        Radial,
        RetroGrid,
        Waveform,
        News,
        TapeDeck,
        Enso,
    }

    internal readonly record struct Rgb(byte Red, byte Green, byte Blue)
    {
        public static Rgb Black => new(0, 0, 0);

        public static Rgb Parse(string value)
        {
            if (value.Length != 6
                || !byte.TryParse(value[..2], System.Globalization.NumberStyles.HexNumber, null, out var red)
                || !byte.TryParse(value.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var green)
                || !byte.TryParse(value[4..], System.Globalization.NumberStyles.HexNumber, null, out var blue))
            {
                throw new InvalidDataException($"Invalid badge color: {value}");
            }

            return new Rgb(red, green, blue);
        }
    }

    private readonly record struct PixelPoint(int X, int Y);

    private sealed class BadgeSurface
    {
        public BadgeSurface(int width, int height, Rgb background)
        {
            Width = width;
            Height = height;
            Pixels = new byte[checked(width * height * 3)];
            FillRectangle(0, 0, width - 1, height - 1, background);
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Pixels { get; }

        public void FillGradient(Rgb first, Rgb second)
        {
            var denominator = Math.Max(1, Height - 1);
            for (var y = 0; y < Height; y++)
            {
                var color = new Rgb(
                    checked((byte)RoundFraction(
                        ((long)first.Red * (denominator - y)) + ((long)second.Red * y),
                        denominator)),
                    checked((byte)RoundFraction(
                        ((long)first.Green * (denominator - y)) + ((long)second.Green * y),
                        denominator)),
                    checked((byte)RoundFraction(
                        ((long)first.Blue * (denominator - y)) + ((long)second.Blue * y),
                        denominator)));
                FillRectangle(0, y, Width - 1, y, color);
            }
        }

        public void SetPixel(int x, int y, Rgb color)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                return;
            }

            var index = ((y * Width) + x) * 3;
            Pixels[index] = color.Red;
            Pixels[index + 1] = color.Green;
            Pixels[index + 2] = color.Blue;
        }

        public void FillRectangle(int left, int top, int right, int bottom, Rgb color)
        {
            var boundedLeft = Math.Max(0, Math.Min(left, right));
            var boundedRight = Math.Min(Width - 1, Math.Max(left, right));
            var boundedTop = Math.Max(0, Math.Min(top, bottom));
            var boundedBottom = Math.Min(Height - 1, Math.Max(top, bottom));
            for (var y = boundedTop; y <= boundedBottom; y++)
            {
                for (var x = boundedLeft; x <= boundedRight; x++)
                {
                    SetPixel(x, y, color);
                }
            }
        }

        public void DrawRectangleOutline(
            int left,
            int top,
            int right,
            int bottom,
            Rgb color,
            int width)
        {
            for (var offset = 0; offset < width; offset++)
            {
                FillRectangle(left + offset, top + offset, right - offset, top + offset, color);
                FillRectangle(left + offset, bottom - offset, right - offset, bottom - offset, color);
                FillRectangle(left + offset, top + offset, left + offset, bottom - offset, color);
                FillRectangle(right - offset, top + offset, right - offset, bottom - offset, color);
            }
        }

        public void DrawLine(PixelPoint start, PixelPoint end, Rgb color, int width)
        {
            var x = start.X;
            var y = start.Y;
            var deltaX = Math.Abs(end.X - start.X);
            var stepX = start.X < end.X ? 1 : -1;
            var deltaY = -Math.Abs(end.Y - start.Y);
            var stepY = start.Y < end.Y ? 1 : -1;
            var error = deltaX + deltaY;
            while (true)
            {
                DrawBrush(x, y, color, width);
                if (x == end.X && y == end.Y)
                {
                    break;
                }

                var doubled = 2 * error;
                if (doubled >= deltaY)
                {
                    error += deltaY;
                    x += stepX;
                }

                if (doubled <= deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }
            }
        }

        public void DrawPolyline(IReadOnlyList<PixelPoint> points, Rgb color, int width)
        {
            for (var index = 1; index < points.Count; index++)
            {
                DrawLine(points[index - 1], points[index], color, width);
            }
        }

        public void DrawEllipseOutline(
            int left,
            int top,
            int right,
            int bottom,
            Rgb color,
            int width)
        {
            for (var y = Math.Max(0, top); y <= Math.Min(Height - 1, bottom); y++)
            {
                for (var x = Math.Max(0, left); x <= Math.Min(Width - 1, right); x++)
                {
                    if (InsideEllipse(x, y, left, top, right, bottom)
                        && !InsideEllipse(
                            x,
                            y,
                            left + width,
                            top + width,
                            right - width,
                            bottom - width))
                    {
                        SetPixel(x, y, color);
                    }
                }
            }
        }

        public void FillEllipse(int left, int top, int right, int bottom, Rgb color)
        {
            for (var y = Math.Max(0, top); y <= Math.Min(Height - 1, bottom); y++)
            {
                for (var x = Math.Max(0, left); x <= Math.Min(Width - 1, right); x++)
                {
                    if (InsideEllipse(x, y, left, top, right, bottom))
                    {
                        SetPixel(x, y, color);
                    }
                }
            }
        }

        private static bool InsideEllipse(
            int x,
            int y,
            int left,
            int top,
            int right,
            int bottom)
        {
            if (left > right || top > bottom)
            {
                return false;
            }

            var radiusX = right - left;
            var radiusY = bottom - top;
            if (radiusX == 0 || radiusY == 0)
            {
                return x >= left && x <= right && y >= top && y <= bottom;
            }

            var deltaX = (2L * x) - left - right;
            var deltaY = (2L * y) - top - bottom;
            var radiusXSquared = (long)radiusX * radiusX;
            var radiusYSquared = (long)radiusY * radiusY;
            return (deltaX * deltaX * radiusYSquared)
                    + (deltaY * deltaY * radiusXSquared)
                <= radiusXSquared * radiusYSquared;
        }

        private void DrawBrush(int x, int y, Rgb color, int width)
        {
            var before = (width - 1) / 2;
            var after = width / 2;
            FillRectangle(x - before, y - before, x + after, y + after, color);
        }
    }
}
