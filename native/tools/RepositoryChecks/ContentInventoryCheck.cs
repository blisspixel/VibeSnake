using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public static class ContentInventoryCheck
{
    public const int InventorySchemaVersion = 1;
    public const int PolicySchemaVersion = 1;
    public const string PolicyRelativePath = "config/content_policy.json";
    public const string InventoryRelativePath = "config/content_inventory.json";

    private const int MaximumTreeEntries = 4096;
    private const int MaximumRules = 1024;
    private const int MaximumPatternsPerRule = 256;
    private const int MaximumPathCharacters = 512;
    private const int MaximumTextCharacters = 4096;
    private const int MaximumPngChunks = 4096;
    private const long MaximumPolicyBytes = 1024 * 1024;
    private const long MaximumInventoryBytes = 8 * 1024 * 1024;
    private const long MaximumJsonAssetBytes = 8 * 1024 * 1024;
    private const long MaximumInspectedFileBytes = 256L * 1024 * 1024;
    private const long MaximumTreeBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumPngDecodedBytes = 256L * 1024 * 1024;
    private const uint MaximumPngDimension = 16_384;
    private const ulong MaximumPngPixels = 67_108_864;

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private static readonly HashSet<string> PolicyFields = new(
        ["schemaVersion", "assetRoot", "rules"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> RuleFields = new(
        ["id", "patterns", "role", "packId", "runtimeUse", "shipStatus", "rights"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> RightsFields = new(
        ["status", "source", "license", "attribution", "reviewNote"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> RuntimeUses = new(
        ["none", "optional", "required"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> ShipStatuses = new(
        ["approved", "blocked", "excluded"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> RightsStatuses = new(
        ["cleared", "not-applicable", "unverified"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> TextMediaTypes = new(
        ["text/csv", "text/markdown", "text/plain"],
        StringComparer.Ordinal);

    private static readonly HashSet<int> ValidPngColorDepths =
    [
        (0 << 8) | 1,
        (0 << 8) | 2,
        (0 << 8) | 4,
        (0 << 8) | 8,
        (0 << 8) | 16,
        (2 << 8) | 8,
        (2 << 8) | 16,
        (3 << 8) | 1,
        (3 << 8) | 2,
        (3 << 8) | 4,
        (3 << 8) | 8,
        (4 << 8) | 8,
        (4 << 8) | 16,
        (6 << 8) | 8,
        (6 << 8) | 16,
    ];

    private static readonly Type[] ExpectedFailureTypes =
    [
        typeof(IOException),
        typeof(UnauthorizedAccessException),
        typeof(InvalidDataException),
        typeof(DecoderFallbackException),
        typeof(JsonException),
        typeof(OverflowException),
        typeof(NotSupportedException),
    ];

    private static readonly Dictionary<string, string> MediaTypes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".md"] = "text/markdown",
        [".mp3"] = "audio/mpeg",
        [".png"] = "image/png",
        [".txt"] = "text/plain",
        [".wav"] = "audio/wav",
    };

    private static readonly JsonSerializerOptions RenderOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static RepositoryCheckResult Inspect(
        string repositoryRoot,
        bool requireReleaseReady = false)
    {
        try
        {
            var build = Build(repositoryRoot);
            var inventoryPath = ResolveFixedFile(
                build.RepositoryRoot,
                InventoryRelativePath,
                MaximumInventoryBytes,
                "content inventory");
            var actual = ReadStrictUtf8(inventoryPath, MaximumInventoryBytes, "content inventory");
            if (!string.Equals(actual, build.Json, StringComparison.Ordinal))
            {
                return Failed(
                    [
                        "content inventory is stale; run "
                            + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
                            + "-- inventory-write .",
                    ]);
            }

            var blockers = ReleaseBlockers(build.Assets);
            if (requireReleaseReady && blockers.Count > 0)
            {
                return Failed(
                    blockers.Select(blocker => "release blocker: " + blocker).ToArray());
            }

            return new RepositoryCheckResult(
                "Content inventory",
                true,
                Summary(requireReleaseReady ? "release-ready" : "verified", build, blockers.Count),
                []);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed([SingleLine(exception.Message)]);
        }
    }

    public static RepositoryCheckResult Write(string repositoryRoot)
    {
        try
        {
            var build = Build(repositoryRoot);
            var inventoryPath = ResolveWritableInventoryPath(build.RepositoryRoot);
            WriteAtomic(inventoryPath, StrictUtf8.GetBytes(build.Json));
            var verification = Inspect(build.RepositoryRoot);
            if (!verification.Passed)
            {
                return Failed(
                    verification.Failures
                        .Select(failure => "write verification failed: " + failure)
                        .ToArray());
            }

            var blockers = ReleaseBlockers(build.Assets);
            return new RepositoryCheckResult(
                "Content inventory",
                true,
                Summary("written", build, blockers.Count),
                []);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed([SingleLine(exception.Message)]);
        }
    }

    internal static string BuildInventoryJson(string repositoryRoot) =>
        Build(repositoryRoot).Json;

    internal static IReadOnlyList<string> FindReleaseBlockers(string repositoryRoot) =>
        ReleaseBlockers(Build(repositoryRoot).Assets);

    private static InventoryBuild Build(string repositoryRoot)
    {
        var root = ResolveRepositoryRoot(repositoryRoot);
        var policyPath = ResolveFixedFile(
            root,
            PolicyRelativePath,
            MaximumPolicyBytes,
            "content policy");
        var policy = LoadPolicy(policyPath, out var policySha256);
        var assetRoot = ResolveAssetRoot(root, policy.AssetRoot);
        var files = InventoryFiles(assetRoot);
        var compiledRules = policy.Rules
            .Select(rule => new CompiledRule(
                rule,
                rule.Patterns.Select(CompileGlob).ToArray()))
            .ToArray();
        var ruleHits = policy.Rules.ToDictionary(rule => rule.Id, _ => 0, StringComparer.Ordinal);
        var entries = new List<AssetEntry>(files.Count);
        long totalBytes = 0;

        foreach (var file in files)
        {
            var matching = compiledRules
                .Where(rule => rule.Patterns.Any(pattern => pattern.IsMatch(file.RelativePath)))
                .Select(rule => rule.Rule)
                .ToArray();
            if (matching.Length == 0)
            {
                throw new InvalidDataException(
                    $"asset has no content policy rule: {file.RelativePath}");
            }

            if (matching.Length > 1)
            {
                throw new InvalidDataException(
                    $"asset matches multiple content policy rules: {file.RelativePath}: "
                    + string.Join(", ", matching.Select(rule => rule.Id)));
            }

            var rule = matching[0];
            ruleHits[rule.Id]++;
            var extension = Path.GetExtension(file.Path);
            if (!MediaTypes.TryGetValue(extension, out var mediaType))
            {
                throw new InvalidDataException(
                    $"asset has an unsupported media extension: {file.RelativePath}");
            }

            var before = new FileInfo(file.Path);
            var size = before.Length;
            if (size > MaximumInspectedFileBytes)
            {
                throw new InvalidDataException(
                    $"asset exceeds the {MaximumInspectedFileBytes}-byte file limit: "
                    + file.RelativePath);
            }

            totalBytes = checked(totalBytes + size);
            if (totalBytes > MaximumTreeBytes)
            {
                throw new InvalidDataException(
                    $"asset tree exceeds the {MaximumTreeBytes}-byte validation limit");
            }

            var integrity = InspectIntegrity(file.Path, mediaType, size);
            if (rule.ShipStatus == "approved" && integrity.Status != "valid")
            {
                throw new InvalidDataException(
                    $"approved asset failed integrity validation: {file.RelativePath}: "
                    + integrity.Detail);
            }

            var sha256 = HashFile(file.Path, size, file.RelativePath);
            EnsureStableRegularFile(file, before, size);
            entries.Add(
                new AssetEntry(
                    $"asset:{file.RelativePath}",
                    file.RelativePath,
                    mediaType,
                    size,
                    sha256,
                    integrity.Status,
                    integrity.Detail,
                    rule.Role,
                    rule.PackId,
                    rule.RuntimeUse,
                    rule.ShipStatus,
                    rule.ShipStatus == "approved",
                    rule.Rights,
                    rule.Id,
                    null));
        }

        var unused = ruleHits
            .Where(pair => pair.Value == 0)
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unused.Length > 0)
        {
            throw new InvalidDataException(
                "content policy rules match no assets: " + string.Join(", ", unused));
        }

        AssignDuplicates(entries);
        var rootNode = RenderInventory(
            policy,
            policySha256,
            entries,
            totalBytes);
        var json = rootNode
            .ToJsonString(RenderOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            + "\n";
        return new InventoryBuild(root, json, entries, totalBytes);
    }

    private static ContentPolicy LoadPolicy(string path, out string sha256)
    {
        using var document = LoadStrictJson(
            path,
            MaximumPolicyBytes,
            "content policy",
            out sha256);
        var root = document.RootElement;
        RequireObject(root, "content policy");
        RequireExactFields(root, PolicyFields, "content policy");
        var schema = root.GetProperty("schemaVersion");
        if (schema.ValueKind != JsonValueKind.Number
            || !schema.TryGetInt32(out var schemaVersion)
            || schemaVersion != PolicySchemaVersion)
        {
            throw new InvalidDataException(
                "unsupported content policy schema: "
                + schema.GetRawText());
        }

        var assetRoot = ValidateRelativePath(
            RequireText(root, "assetRoot", "content policy assetRoot"),
            "content policy assetRoot",
            allowGlob: false);
        var rulesElement = root.GetProperty("rules");
        if (rulesElement.ValueKind != JsonValueKind.Array
            || rulesElement.GetArrayLength() == 0)
        {
            throw new InvalidDataException(
                "content policy rules must be a non-empty array");
        }

        if (rulesElement.GetArrayLength() > MaximumRules)
        {
            throw new InvalidDataException(
                $"content policy rules exceed the {MaximumRules}-item limit");
        }

        var seenRuleIds = new HashSet<string>(StringComparer.Ordinal);
        var rules = new List<PolicyRule>(rulesElement.GetArrayLength());
        var index = 0;
        foreach (var item in rulesElement.EnumerateArray())
        {
            var location = $"content policy rule {index}";
            RequireObject(item, location);
            RequireExactFields(item, RuleFields, location);
            var id = RequireText(item, "id", $"{location} id");
            if (!seenRuleIds.Add(id))
            {
                throw new InvalidDataException(
                    $"duplicate content policy rule id: {id}");
            }

            var patternsElement = item.GetProperty("patterns");
            if (patternsElement.ValueKind != JsonValueKind.Array
                || patternsElement.GetArrayLength() == 0)
            {
                throw new InvalidDataException(
                    $"{location} patterns must be a non-empty array");
            }

            if (patternsElement.GetArrayLength() > MaximumPatternsPerRule)
            {
                throw new InvalidDataException(
                    $"{location} patterns exceed the {MaximumPatternsPerRule}-item limit");
            }

            var patterns = new List<string>(patternsElement.GetArrayLength());
            var seenPatterns = new HashSet<string>(StringComparer.Ordinal);
            var patternIndex = 0;
            foreach (var patternElement in patternsElement.EnumerateArray())
            {
                var pattern = ValidateRelativePath(
                    RequireText(patternElement, $"{location} pattern {patternIndex}"),
                    $"{location} pattern {patternIndex}",
                    allowGlob: true);
                if (!seenPatterns.Add(pattern))
                {
                    throw new InvalidDataException(
                        $"{location} repeats pattern: {pattern}");
                }

                patterns.Add(pattern);
                patternIndex++;
            }

            var role = RequireText(item, "role", $"{location} role");
            var packId = RequireText(item, "packId", $"{location} packId");
            var runtimeUse = RequireText(item, "runtimeUse", $"{location} runtimeUse");
            if (!RuntimeUses.Contains(runtimeUse))
            {
                throw new InvalidDataException(
                    $"{location} has invalid runtimeUse: {runtimeUse}");
            }

            var shipStatus = RequireText(item, "shipStatus", $"{location} shipStatus");
            if (!ShipStatuses.Contains(shipStatus))
            {
                throw new InvalidDataException(
                    $"{location} has invalid shipStatus: {shipStatus}");
            }

            var rightsElement = item.GetProperty("rights");
            RequireObject(rightsElement, $"{location} rights");
            RequireExactFields(rightsElement, RightsFields, $"{location} rights");
            var rights = new ContentRights(
                RequireText(rightsElement, "status", $"{location} rights status"),
                RequireText(rightsElement, "source", $"{location} rights source"),
                RequireText(rightsElement, "license", $"{location} rights license"),
                RequireText(rightsElement, "attribution", $"{location} rights attribution"),
                RequireText(rightsElement, "reviewNote", $"{location} rights reviewNote"));
            if (!RightsStatuses.Contains(rights.Status))
            {
                throw new InvalidDataException(
                    $"{location} has invalid rights status: {rights.Status}");
            }

            if (shipStatus == "approved" && rights.Status != "cleared")
            {
                throw new InvalidDataException(
                    $"{location} cannot approve shipping without cleared rights");
            }

            if (shipStatus == "excluded" && runtimeUse != "none")
            {
                throw new InvalidDataException(
                    $"{location} cannot exclude an asset used by the runtime");
            }

            rules.Add(
                new PolicyRule(
                    id,
                    patterns,
                    role,
                    packId,
                    runtimeUse,
                    shipStatus,
                    rights));
            index++;
        }

        return new ContentPolicy(assetRoot, rules);
    }

    private static List<InventoryFile> InventoryFiles(string assetRoot)
    {
        var files = new List<InventoryFile>();
        var pending = new Stack<string>();
        pending.Push(assetRoot);
        var entryCount = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
                Array.Sort(entries, StringComparer.Ordinal);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
            {
                throw new InvalidDataException(
                    $"asset tree could not enumerate {RelativePath(assetRoot, directory)}: "
                    + SingleLine(exception.Message),
                    exception);
            }

            for (var index = entries.Length - 1; index >= 0; index--)
            {
                var entry = entries[index];
                entryCount++;
                if (entryCount > MaximumTreeEntries)
                {
                    throw new InvalidDataException(
                        $"asset tree exceeds the {MaximumTreeEntries}-entry validation limit");
                }

                var relative = RelativePath(assetRoot, entry);
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException)
                {
                    throw new InvalidDataException(
                        $"asset tree could not inspect {relative}: "
                        + SingleLine(exception.Message),
                        exception);
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"asset tree cannot contain a link: {relative}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                if (!File.Exists(entry))
                {
                    throw new InvalidDataException(
                        $"asset tree contains an unsupported entry: {relative}");
                }

                if (relative.Length > MaximumPathCharacters)
                {
                    throw new InvalidDataException(
                        $"asset path exceeds {MaximumPathCharacters} characters: {relative}");
                }

                files.Add(new InventoryFile(entry, relative));
            }
        }

        if (files.Count == 0)
        {
            throw new InvalidDataException(
                $"asset root contains no files: {assetRoot}");
        }

        files.Sort(
            (left, right) =>
            {
                var folded = StringComparer.OrdinalIgnoreCase.Compare(
                    left.RelativePath,
                    right.RelativePath);
                return folded != 0
                    ? folded
                    : StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
            });
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (seen.TryGetValue(file.RelativePath, out var previous))
            {
                throw new InvalidDataException(
                    $"asset paths collide on case-insensitive systems: "
                    + $"{previous} and {file.RelativePath}");
            }

            seen.Add(file.RelativePath, file.RelativePath);
        }

        return files;
    }

    private static IntegrityResult InspectIntegrity(string path, string mediaType, long size)
    {
        if (size == 0)
        {
            return new IntegrityResult("empty", "file contains zero bytes");
        }

        try
        {
            if (mediaType == "application/json")
            {
                return InspectJsonAsset(path, size);
            }

            if (TextMediaTypes.Contains(mediaType))
            {
                return InspectText(path, size);
            }

            if (mediaType == "image/png")
            {
                return InspectPng(path, size);
            }

            if (mediaType == "audio/wav")
            {
                return InspectWav(path, size);
            }

            if (mediaType == "audio/mpeg")
            {
                return InspectMp3(path, size);
            }

            throw new InvalidDataException($"unsupported media type: {mediaType}");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or JsonException
                or InvalidDataException
                or OverflowException)
        {
            return new IntegrityResult(
                "invalid",
                $"{exception.GetType().Name}: {SingleLine(exception.Message)}");
        }
    }

    private static IntegrityResult InspectJsonAsset(string path, long size)
    {
        if (size > MaximumJsonAssetBytes)
        {
            return new IntegrityResult(
                "invalid",
                $"JSON asset exceeds the {MaximumJsonAssetBytes}-byte validation limit");
        }

        using var document = LoadStrictJson(path, MaximumJsonAssetBytes, "JSON asset");
        _ = document.RootElement.ValueKind;
        return new IntegrityResult("valid", "basic structure check passed");
    }

    private static IntegrityResult InspectText(string path, long expectedSize)
    {
        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        if (source.Length != expectedSize)
        {
            throw new InvalidDataException("text asset changed before validation");
        }

        var decoder = StrictUtf8.GetDecoder();
        var input = new byte[64 * 1024];
        var output = new char[StrictUtf8.GetMaxCharCount(input.Length)];
        long total = 0;
        int count;
        while ((count = source.Read(input, 0, input.Length)) > 0)
        {
            total = checked(total + count);
            if (total > expectedSize)
            {
                throw new InvalidDataException("text asset grew during validation");
            }

            decoder.Convert(
                input.AsSpan(0, count),
                output,
                flush: false,
                out _,
                out _,
                out _);
        }

        if (total != expectedSize)
        {
            throw new InvalidDataException("text asset changed during validation");
        }

        decoder.Convert(
            ReadOnlySpan<byte>.Empty,
            output,
            flush: true,
            out _,
            out _,
            out _);

        return new IntegrityResult("valid", "basic structure check passed");
    }

    private static IntegrityResult InspectPng(string path, long size)
    {
        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        Span<byte> signature = stackalloc byte[PngSignature.Length];
        if (!ReadExact(source, signature) || !signature.SequenceEqual(PngSignature))
        {
            return Invalid("PNG signature is invalid");
        }

        var chunkIndex = 0;
        var sawHeader = false;
        var sawPalette = false;
        var sawImageData = false;
        var imageDataClosed = false;
        var sawEnd = false;
        var sawC2pa = false;
        PngHeader? header = null;
        var imageSegments = new List<FileSegment>();
        var scratch = new byte[64 * 1024];
        var chunkHeader = new byte[8];
        var rawChecksum = new byte[4];
        while (source.Position < size)
        {
            chunkIndex++;
            if (chunkIndex > MaximumPngChunks)
            {
                return Invalid(
                    $"PNG exceeds the {MaximumPngChunks}-chunk validation limit");
            }

            if (!ReadExact(source, chunkHeader))
            {
                return Invalid("PNG chunk header is truncated");
            }

            var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader);
            var chunkType = chunkHeader.AsSpan(4, 4);
            if (!IsAsciiLetters(chunkType))
            {
                return Invalid("PNG chunk type is invalid");
            }

            if ((long)chunkSize > size - source.Position - 4)
            {
                return Invalid("PNG chunk data is truncated");
            }

            var type = Encoding.ASCII.GetString(chunkType);
            if (chunkIndex == 1 && type != "IHDR")
            {
                return Invalid("PNG IHDR must be the first chunk");
            }

            if ((chunkType[0] & 0x20) == 0
                && type is not ("IHDR" or "PLTE" or "IDAT" or "IEND"))
            {
                return Invalid("PNG contains an unsupported critical chunk");
            }

            if (type == "PLTE" && (sawPalette || sawImageData))
            {
                return Invalid("PNG PLTE must occur at most once and before IDAT");
            }

            if (type == "IDAT" && header is { ColorType: 3 } && !sawPalette)
            {
                return Invalid("PNG indexed-color image requires PLTE before IDAT");
            }

            if (type == "IHDR" && chunkSize != 13)
            {
                return Invalid("PNG IHDR chunk is invalid");
            }

            if (type == "PLTE" && chunkSize > 768)
            {
                return Invalid("PNG PLTE must contain between 1 and 256 RGB entries");
            }

            var captured = type is "IHDR" or "PLTE"
                ? new byte[checked((int)chunkSize)]
                : null;
            var dataOffset = source.Position;
            var checksum = UpdateCrc(uint.MaxValue, chunkType);
            var remaining = (long)chunkSize;
            var capturedOffset = 0;
            while (remaining > 0)
            {
                var count = source.Read(
                    scratch,
                    0,
                    checked((int)Math.Min(remaining, scratch.Length)));
                if (count == 0)
                {
                    return Invalid("PNG chunk data is truncated");
                }

                checksum = UpdateCrc(checksum, scratch.AsSpan(0, count));
                if (captured is not null)
                {
                    scratch.AsSpan(0, count).CopyTo(captured.AsSpan(capturedOffset));
                    capturedOffset += count;
                }

                remaining -= count;
            }

            if (!ReadExact(source, rawChecksum))
            {
                return Invalid("PNG chunk CRC is truncated");
            }

            if (BinaryPrimitives.ReadUInt32BigEndian(rawChecksum) != ~checksum)
            {
                return Invalid($"PNG {type} chunk CRC is invalid");
            }

            if (type == "IHDR")
            {
                if (sawHeader || captured is null)
                {
                    return Invalid("PNG IHDR chunk is invalid");
                }

                var headerResult = ValidatePngHeader(captured);
                if (headerResult.Header is null)
                {
                    return Invalid(headerResult.Detail);
                }

                header = headerResult.Header;
                sawHeader = true;
            }
            else if (type == "PLTE")
            {
                if (header is null || captured is null)
                {
                    return Invalid("PNG PLTE occurs before a valid IHDR");
                }

                var paletteError = ValidatePngPalette(header, captured);
                if (paletteError is not null)
                {
                    return Invalid(paletteError);
                }

                sawPalette = true;
            }
            else if (type == "IDAT")
            {
                if (header is null)
                {
                    return Invalid("PNG IDAT occurs before a valid IHDR");
                }

                if (imageDataClosed)
                {
                    return Invalid("PNG IDAT chunks must be consecutive");
                }

                if (chunkSize > 0)
                {
                    sawImageData = true;
                    imageSegments.Add(new FileSegment(dataOffset, chunkSize));
                }
            }
            else if (type == "caBX")
            {
                sawC2pa = true;
                if (sawImageData)
                {
                    imageDataClosed = true;
                }
            }
            else if (sawImageData)
            {
                imageDataClosed = true;
            }

            if (type == "IEND")
            {
                if (chunkSize != 0)
                {
                    return Invalid("PNG IEND chunk must be empty");
                }

                if (source.Position != size)
                {
                    return Invalid("PNG contains trailing bytes after IEND");
                }

                sawEnd = true;
                break;
            }
        }

        if (!sawHeader)
        {
            return Invalid("PNG stream has no IHDR chunk");
        }

        if (!sawImageData)
        {
            return Invalid("PNG stream has no image data");
        }

        if (!sawEnd)
        {
            return Invalid("PNG stream has no IEND chunk");
        }

        var dataResult = ValidatePngImageData(path, header!, imageSegments);
        if (dataResult is not null)
        {
            return Invalid(dataResult);
        }

        var detail = "PNG container, palette, compressed scanlines, and chunk CRCs are valid";
        if (sawC2pa)
        {
            detail += "; caBX C2PA/JUMBF provenance container is present";
        }

        return new IntegrityResult("valid", detail);
    }

    internal static string? ValidatePngForRepositoryCheck(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var size = new FileInfo(path).Length;
            var result = InspectPng(path, size);
            return result.Status == "valid" ? null : result.Detail;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or OverflowException)
        {
            return "PNG validation failed: " + SingleLine(exception.Message);
        }
    }

    private static PngHeaderResult ValidatePngHeader(ReadOnlySpan<byte> value)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(value);
        var height = BinaryPrimitives.ReadUInt32BigEndian(value[4..]);
        var bitDepth = value[8];
        var colorType = value[9];
        var compression = value[10];
        var filtering = value[11];
        var interlace = value[12];
        if (width == 0 || height == 0)
        {
            return new PngHeaderResult(null, "PNG dimensions must be positive");
        }

        if (width > MaximumPngDimension || height > MaximumPngDimension)
        {
            return new PngHeaderResult(
                null,
                $"PNG dimension exceeds {MaximumPngDimension} pixels");
        }

        if ((ulong)width * height > MaximumPngPixels)
        {
            return new PngHeaderResult(
                null,
                $"PNG pixel count exceeds {MaximumPngPixels}");
        }

        var validDepth = ValidPngColorDepths.Contains((colorType << 8) | bitDepth);
        if (!validDepth)
        {
            return new PngHeaderResult(
                null,
                "PNG color type and bit depth are incompatible");
        }

        if (compression != 0 || filtering != 0 || interlace is not (0 or 1))
        {
            return new PngHeaderResult(
                null,
                "PNG compression, filter, or interlace method is unsupported");
        }

        return new PngHeaderResult(
            new PngHeader(width, height, bitDepth, colorType, interlace),
            "PNG IHDR is valid");
    }

    private static string? ValidatePngPalette(PngHeader header, ReadOnlySpan<byte> value)
    {
        if (header.ColorType is 0 or 4)
        {
            return "PNG grayscale images cannot contain PLTE";
        }

        if (value.Length == 0 || value.Length % 3 != 0 || value.Length > 768)
        {
            return "PNG PLTE must contain between 1 and 256 RGB entries";
        }

        if (header.ColorType == 3 && value.Length / 3 > 1 << header.BitDepth)
        {
            return "PNG PLTE has more entries than its indexed bit depth allows";
        }

        return null;
    }

    private static string? ValidatePngImageData(
        string path,
        PngHeader header,
        IReadOnlyList<FileSegment> segments)
    {
        try
        {
            var envelope = ReadZlibEnvelope(path, segments);
            using var compressed = new SegmentedReadStream(path, segments);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            var validator = new PngScanlineValidator(header);
            var output = new byte[64 * 1024];
            int count;
            while ((count = zlib.Read(output, 0, output.Length)) > 0)
            {
                validator.Consume(output.AsSpan(0, count));
            }

            validator.Finish();
            if (validator.Adler32 != envelope.Adler32)
            {
                return "PNG image data zlib Adler-32 checksum is invalid";
            }

            if (compressed.BytesRead != compressed.TotalBytes)
            {
                return "PNG image data contains bytes after the zlib stream";
            }

            return null;
        }
        catch (InvalidDataException exception)
        {
            return exception.Message.StartsWith("PNG ", StringComparison.Ordinal)
                ? SingleLine(exception.Message)
                : "PNG image data is not valid zlib data: " + SingleLine(exception.Message);
        }
    }

    private static ZlibEnvelope ReadZlibEnvelope(
        string path,
        IReadOnlyList<FileSegment> segments)
    {
        var totalBytes = segments.Sum(segment => segment.Length);
        if (totalBytes < 6)
        {
            throw new InvalidDataException("PNG image data has an incomplete zlib stream");
        }

        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.RandomAccess);
        var compression = ReadLogicalByte(source, segments, 0);
        var flags = ReadLogicalByte(source, segments, 1);
        if ((compression & 0x0f) != 8
            || (compression >> 4) > 7
            || (((compression << 8) | flags) % 31) != 0
            || (flags & 0x20) != 0)
        {
            throw new InvalidDataException("PNG image data has an invalid zlib header");
        }

        var adler = 0U;
        for (var index = 4; index > 0; index--)
        {
            adler = (adler << 8)
                | ReadLogicalByte(source, segments, totalBytes - index);
        }

        return new ZlibEnvelope(adler);
    }

    private static byte ReadLogicalByte(
        FileStream source,
        IReadOnlyList<FileSegment> segments,
        long offset)
    {
        foreach (var segment in segments)
        {
            if (offset < segment.Length)
            {
                source.Position = segment.Offset + offset;
                var value = source.ReadByte();
                if (value < 0)
                {
                    throw new InvalidDataException(
                        "PNG image data has an incomplete zlib stream");
                }

                return checked((byte)value);
            }

            offset -= segment.Length;
        }

        throw new InvalidDataException("PNG image data has an incomplete zlib stream");
    }

    private static IntegrityResult InspectWav(string path, long size)
    {
        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        Span<byte> header = stackalloc byte[12];
        if (!ReadExact(source, header)
            || !header[..4].SequenceEqual("RIFF"u8)
            || !header[8..].SequenceEqual("WAVE"u8))
        {
            return Invalid("WAV RIFF header is invalid");
        }

        var formatValid = false;
        long dataBytes = 0;
        var cursor = 12L;
        var chunkCount = 0;
        var chunkHeader = new byte[8];
        var format = new byte[16];
        while (cursor + 8 <= size)
        {
            chunkCount++;
            if (chunkCount > MaximumPngChunks)
            {
                return Invalid(
                    $"WAV exceeds the {MaximumPngChunks}-chunk validation limit");
            }

            source.Position = cursor;
            if (!ReadExact(source, chunkHeader))
            {
                return Invalid("WAV chunk header is truncated");
            }

            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4));
            var chunkStart = cursor + 8;
            var chunkEnd = checked(chunkStart + chunkSize);
            if (chunkEnd > size)
            {
                return Invalid("WAV chunk extends beyond the file");
            }

            if (chunkHeader.AsSpan(0, 4).SequenceEqual("fmt "u8))
            {
                if (chunkSize < 16)
                {
                    return Invalid("WAV format chunk is too short");
                }

                source.Position = chunkStart;
                if (!ReadExact(source, format))
                {
                    return Invalid("WAV format chunk is truncated");
                }

                var audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(format);
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2));
                var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(4));
                var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(12));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14));
                formatValid = audioFormat is 1 or 3 or 0xfffe
                    && channels > 0
                    && sampleRate > 0
                    && blockAlign > 0
                    && bits > 0;
            }
            else if (chunkHeader.AsSpan(0, 4).SequenceEqual("data"u8))
            {
                dataBytes = checked(dataBytes + chunkSize);
            }

            cursor = checked(chunkEnd + (chunkSize % 2));
        }

        if (!formatValid)
        {
            return Invalid("WAV stream has no supported format chunk");
        }

        return dataBytes == 0
            ? Invalid("WAV stream has no audio data")
            : new IntegrityResult("valid", "basic structure check passed");
    }

    private static IntegrityResult InspectMp3(string path, long size)
    {
        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        Span<byte> first = stackalloc byte[10];
        var firstCount = source.Read(first);
        long frameOffset = 0;
        if (firstCount >= 3 && first[..3].SequenceEqual("ID3"u8))
        {
            if (firstCount < 10 || first[6..10].ToArray().Any(value => (value & 0x80) != 0))
            {
                return Invalid("MP3 ID3 header is invalid");
            }

            frameOffset = 10L
                + ((long)first[6] << 21)
                + ((long)first[7] << 14)
                + ((long)first[8] << 7)
                + first[9];
            if ((first[5] & 0x10) != 0)
            {
                frameOffset += 10;
            }
        }

        if (frameOffset + 4 > size)
        {
            return Invalid("MP3 stream has no complete MPEG audio frame");
        }

        (int Version, int Layer, int SampleRate)? expected = null;
        Span<byte> frameHeader = stackalloc byte[4];
        for (var index = 0; index < 2; index++)
        {
            source.Position = frameOffset;
            if (!ReadExact(source, frameHeader)
                || !TryGetMp3Frame(frameHeader, out var frame))
            {
                return Invalid("MP3 stream lacks two consecutive MPEG audio frames");
            }

            if (frameOffset + frame.Length > size)
            {
                return Invalid("MP3 stream has an incomplete MPEG audio frame");
            }

            var format = (frame.Version, frame.Layer, frame.SampleRate);
            if (expected is not null && format != expected.Value)
            {
                return Invalid(
                    "MP3 consecutive frames use incompatible stream parameters");
            }

            expected = format;
            frameOffset += frame.Length;
        }

        return new IntegrityResult(
            "valid",
            "MP3 stream contains two consecutive complete MPEG audio frames");
    }

    private static bool TryGetMp3Frame(ReadOnlySpan<byte> header, out Mp3Frame frame)
    {
        frame = default;
        if (header.Length != 4)
        {
            return false;
        }

        var value = BinaryPrimitives.ReadUInt32BigEndian(header);
        if ((value & 0xffe00000) != 0xffe00000)
        {
            return false;
        }

        var version = (int)((value >> 19) & 0x03);
        var layer = (int)((value >> 17) & 0x03);
        var bitrateIndex = (int)((value >> 12) & 0x0f);
        var sampleRateIndex = (int)((value >> 10) & 0x03);
        var padding = (int)((value >> 9) & 0x01);
        if (version == 1
            || layer == 0
            || bitrateIndex is 0 or 15
            || sampleRateIndex == 3)
        {
            return false;
        }

        int[] bitrates = version == 3
            ? layer switch
            {
                3 => [32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448],
                2 => [32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],
                _ => [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],
            }
            : layer switch
            {
                3 => [32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],
                _ => [8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
            };
        var bitrate = bitrates[bitrateIndex - 1] * 1000;
        var baseSampleRate = new[] { 44_100, 48_000, 32_000 }[sampleRateIndex];
        var sampleRate = baseSampleRate / (version == 3 ? 1 : version == 2 ? 2 : 4);
        var frameLength = layer == 3
            ? (((12 * bitrate) / sampleRate) + padding) * 4
            : layer == 1 && version != 3
                ? ((72 * bitrate) / sampleRate) + padding
                : ((144 * bitrate) / sampleRate) + padding;
        if (frameLength < 4)
        {
            return false;
        }

        frame = new Mp3Frame(frameLength, version, layer, sampleRate);
        return true;
    }

    private static JsonObject RenderInventory(
        ContentPolicy policy,
        string policySha256,
        IReadOnlyList<AssetEntry> entries,
        long totalBytes)
    {
        var duplicates = entries
            .Where(entry => entry.DuplicateOf is not null)
            .ToArray();
        var assets = new JsonArray();
        foreach (var entry in entries)
        {
            assets.Add(
                new JsonObject
                {
                    ["id"] = entry.Id,
                    ["path"] = entry.Path,
                    ["mediaType"] = entry.MediaType,
                    ["bytes"] = entry.Bytes,
                    ["sha256"] = entry.Sha256,
                    ["integrityStatus"] = entry.IntegrityStatus,
                    ["integrityDetail"] = entry.IntegrityDetail,
                    ["role"] = entry.Role,
                    ["packId"] = entry.PackId,
                    ["runtimeUse"] = entry.RuntimeUse,
                    ["shipStatus"] = entry.ShipStatus,
                    ["exportEligible"] = entry.ExportEligible,
                    ["rights"] = new JsonObject
                    {
                        ["status"] = entry.Rights.Status,
                        ["source"] = entry.Rights.Source,
                        ["license"] = entry.Rights.License,
                        ["attribution"] = entry.Rights.Attribution,
                        ["reviewNote"] = entry.Rights.ReviewNote,
                    },
                    ["policyRule"] = entry.PolicyRule,
                    ["duplicateOf"] = entry.DuplicateOf,
                });
        }

        var duplicateGroups = entries
            .GroupBy(entry => (entry.Bytes, entry.Sha256))
            .Count(group => group.Count() > 1);
        var summary = new JsonObject
        {
            ["byIntegrityStatus"] = CountBy(entries.Select(entry => entry.IntegrityStatus)),
            ["byMediaType"] = CountBy(entries.Select(entry => entry.MediaType)),
            ["byPackId"] = CountBy(entries.Select(entry => entry.PackId)),
            ["byRightsStatus"] = CountBy(entries.Select(entry => entry.Rights.Status)),
            ["byRole"] = CountBy(entries.Select(entry => entry.Role)),
            ["byShipStatus"] = CountBy(entries.Select(entry => entry.ShipStatus)),
            ["duplicateFileCount"] = duplicates.Length,
            ["duplicateGroupCount"] = duplicateGroups,
            ["exportEligibleBytes"] = entries
                .Where(entry => entry.ExportEligible)
                .Sum(entry => entry.Bytes),
            ["exportEligibleFileCount"] = entries.Count(entry => entry.ExportEligible),
        };
        return new JsonObject
        {
            ["schemaVersion"] = InventorySchemaVersion,
            ["assetRoot"] = policy.AssetRoot,
            ["policyPath"] = PolicyRelativePath,
            ["policySha256"] = policySha256,
            ["fileCount"] = entries.Count,
            ["totalBytes"] = totalBytes,
            ["summary"] = summary,
            ["assets"] = assets,
        };
    }

    private static JsonObject CountBy(IEnumerable<string> values)
    {
        var result = new JsonObject();
        foreach (var group in values
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            result[group.Key] = group.Count();
        }

        return result;
    }

    private static void AssignDuplicates(IList<AssetEntry> entries)
    {
        foreach (var group in entries.GroupBy(entry => (entry.Bytes, entry.Sha256)))
        {
            if (group.Count() < 2)
            {
                continue;
            }

            var repeated = group.ToArray();
            for (var index = 1; index < repeated.Length; index++)
            {
                var entryIndex = entries.IndexOf(repeated[index]);
                entries[entryIndex] = repeated[index] with
                {
                    DuplicateOf = repeated[0].Id,
                };
            }
        }
    }

    private static List<string> ReleaseBlockers(
        IReadOnlyList<AssetEntry> entries)
    {
        var blockers = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.RuntimeUse != "none" && entry.ShipStatus != "approved")
            {
                blockers.Add(
                    $"{entry.Path}: runtime asset is {entry.ShipStatus} for shipping");
            }

            if (entry.RuntimeUse != "none" && entry.IntegrityStatus != "valid")
            {
                blockers.Add(
                    $"{entry.Path}: runtime asset integrity is {entry.IntegrityStatus}");
            }

            if (entry.ExportEligible && entry.Rights.Status != "cleared")
            {
                blockers.Add(
                    $"{entry.Path}: export-eligible asset lacks cleared rights");
            }

            if (entry.ExportEligible && entry.DuplicateOf is not null)
            {
                blockers.Add(
                    $"{entry.Path}: export-eligible duplicate of {entry.DuplicateOf}");
            }
        }

        return blockers;
    }

    private static JsonDocument LoadStrictJson(string path, long maximumBytes, string label)
        => LoadStrictJson(path, maximumBytes, label, out _);

    private static JsonDocument LoadStrictJson(
        string path,
        long maximumBytes,
        string label,
        out string sha256)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    $"{label} exceeds the {maximumBytes}-byte validation limit");
            }

            bytes = ReadBoundedBytes(path, maximumBytes, label);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"{label} is unreadable: {path}: {SingleLine(exception.Message)}",
                exception);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            throw new InvalidDataException($"{label} must not contain a UTF-8 BOM");
        }

        try
        {
            _ = StrictUtf8.GetString(bytes);
            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            RejectDuplicateJsonFields(document.RootElement, label);
            return document;
        }
        catch (Exception exception) when (
            exception is JsonException or DecoderFallbackException or InvalidDataException)
        {
            sha256 = string.Empty;
            throw new InvalidDataException(
                $"{label} is unreadable: {path}: {SingleLine(exception.Message)}",
                exception);
        }
    }

    private static void RejectDuplicateJsonFields(JsonElement value, string location)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!fields.Add(property.Name))
                {
                    throw new InvalidDataException(
                        $"{location} repeats JSON field: {property.Name}");
                }

                RejectDuplicateJsonFields(property.Value, $"{location}.{property.Name}");
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateJsonFields(item, $"{location}[{index}]");
                index++;
            }
        }
    }

    private static void RequireObject(JsonElement value, string location)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{location} must be a JSON object");
        }
    }

    private static void RequireExactFields(
        JsonElement value,
        IReadOnlySet<string> expected,
        string location)
    {
        var actual = value.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (actual.SetEquals(expected))
        {
            return;
        }

        var details = new List<string>();
        var missing = expected.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var unknown = actual.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        if (missing.Any())
        {
            details.Add("missing " + string.Join(", ", missing));
        }

        if (unknown.Any())
        {
            details.Add("unknown " + string.Join(", ", unknown));
        }

        throw new InvalidDataException(
            $"{location} has invalid fields: {string.Join("; ", details)}");
    }

    private static string RequireText(JsonElement parent, string field, string location)
        => RequireText(parent.GetProperty(field), location);

    private static string RequireText(JsonElement value, string location)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"{location} must be a non-empty string");
        }

        var text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text)
            || text.EnumerateRunes().Take(MaximumTextCharacters + 1).Count()
                > MaximumTextCharacters)
        {
            throw new InvalidDataException(
                $"{location} must be a non-empty string up to "
                + $"{MaximumTextCharacters} characters");
        }

        return text;
    }

    private static string ValidateRelativePath(
        string value,
        string location,
        bool allowGlob)
    {
        if (value.Length > MaximumPathCharacters
            || value[0] == '/'
            || value[^1] == '/'
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains(':', StringComparison.Ordinal)
            || value.Contains("//", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{location} must use a relative POSIX path up to "
                + $"{MaximumPathCharacters} characters");
        }

        if (value.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"{location} contains an unsafe path segment");
        }

        if (!allowGlob && value.IndexOfAny(['*', '?', '[']) >= 0)
        {
            throw new InvalidDataException($"{location} cannot contain glob characters");
        }

        return value;
    }

    private static Regex CompileGlob(string pattern)
    {
        var source = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '*')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    source.Append(".*");
                    index++;
                }
                else
                {
                    source.Append("[^/]*");
                }
            }
            else if (character == '?')
            {
                source.Append("[^/]");
            }
            else
            {
                source.Append(Regex.Escape(character.ToString()));
            }
        }

        source.Append('$');
        return new Regex(
            source.ToString(),
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }

    private static string ResolveRepositoryRoot(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new InvalidDataException("repository root is invalid");
        }

        string root;
        try
        {
            root = Path.GetFullPath(repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new InvalidDataException("repository root is invalid", exception);
        }

        if (!Directory.Exists(root))
        {
            throw new InvalidDataException(
                "repository root must be an existing directory");
        }

        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("repository root cannot be a link");
        }

        return root;
    }

    private static string ResolveAssetRoot(string repositoryRoot, string relativePath)
    {
        var path = ResolveContainedPath(repositoryRoot, relativePath, "asset root");
        EnsureNoLinks(repositoryRoot, path, "asset root");
        if (!Directory.Exists(path))
        {
            throw new InvalidDataException($"asset root does not exist: {path}");
        }

        return path;
    }

    private static string ResolveFixedFile(
        string repositoryRoot,
        string relativePath,
        long maximumBytes,
        string label)
    {
        var path = ResolveContainedPath(repositoryRoot, relativePath, label);
        EnsureNoLinks(repositoryRoot, path, label);
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                $"{label} does not exist: {path}; regenerate it");
        }

        var info = new FileInfo(path);
        if (info.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"{label} exceeds the {maximumBytes}-byte validation limit");
        }

        return path;
    }

    private static string ResolveWritableInventoryPath(string repositoryRoot)
    {
        var path = ResolveContainedPath(
            repositoryRoot,
            InventoryRelativePath,
            "content inventory");
        var parent = Path.GetDirectoryName(path)!;
        EnsureNoLinks(repositoryRoot, parent, "content inventory parent");
        if (Path.Exists(path))
        {
            EnsureNoLinks(repositoryRoot, path, "content inventory");
            if (!File.Exists(path))
            {
                throw new InvalidDataException(
                    "content inventory fixed location must be a regular file");
            }
        }

        return path;
    }

    private static string ResolveContainedPath(
        string repositoryRoot,
        string relativePath,
        string label)
    {
        var path = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = repositoryRoot.EndsWith(Path.DirectorySeparatorChar)
            ? repositoryRoot
            : repositoryRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, PathComparison()))
        {
            throw new InvalidDataException($"{label} must be inside the repository");
        }

        return path;
    }

    private static void EnsureNoLinks(string root, string path, string label)
    {
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Path.Exists(current))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"{label} path cannot contain a link");
            }
        }
    }

    private static void EnsureStableRegularFile(
        InventoryFile file,
        FileInfo before,
        long expectedSize)
    {
        var attributes = File.GetAttributes(file.Path);
        var after = new FileInfo(file.Path);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || (attributes & FileAttributes.Directory) != 0
            || !after.Exists)
        {
            throw new InvalidDataException(
                $"asset changed type while it was inspected: {file.RelativePath}");
        }

        if (after.Length != expectedSize
            || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        {
            throw new InvalidDataException(
                $"asset changed while it was inspected: {file.RelativePath}");
        }
    }

    private static string ReadStrictUtf8(string path, long maximumBytes, string label)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    $"{label} exceeds the {maximumBytes}-byte validation limit");
            }

            bytes = ReadBoundedBytes(path, maximumBytes, label);
            return StrictUtf8.GetString(bytes);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException)
        {
            throw new InvalidDataException(
                $"{label} is unreadable: {path}: {SingleLine(exception.Message)}",
                exception);
        }
    }

    private static byte[] ReadBoundedBytes(string path, long maximumBytes, string label)
    {
        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        if (source.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"{label} exceeds the {maximumBytes}-byte validation limit");
        }

        var bytes = new byte[checked((int)source.Length)];
        if (!ReadExact(source, bytes) || source.ReadByte() != -1)
        {
            throw new InvalidDataException($"{label} changed while it was read");
        }

        return bytes;
    }

    private static string HashFile(string path, long expectedSize, string relativePath)
    {
        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        if (source.Length != expectedSize)
        {
            throw new InvalidDataException(
                $"asset changed before hashing: {relativePath}");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        while (total < expectedSize)
        {
            var count = source.Read(
                buffer,
                0,
                checked((int)Math.Min(buffer.Length, expectedSize - total)));
            if (count == 0)
            {
                throw new InvalidDataException(
                    $"asset changed while it was hashed: {relativePath}");
            }

            hash.AppendData(buffer.AsSpan(0, count));
            total += count;
        }

        if (source.ReadByte() != -1)
        {
            throw new InvalidDataException(
                $"asset grew while it was hashed: {relativePath}");
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void WriteAtomic(string path, ReadOnlySpan<byte> value)
    {
        var temporary = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough))
            {
                output.Write(value);
                output.Flush(flushToDisk: true);
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

    private static bool ReadExact(Stream source, Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var count = source.Read(destination[offset..]);
            if (count == 0)
            {
                return false;
            }

            offset += count;
        }

        return true;
    }

    private static bool IsAsciiLetters(ReadOnlySpan<byte> value)
    {
        foreach (var item in value)
        {
            if (item is not (>= (byte)'A' and <= (byte)'Z')
                and not (>= (byte)'a' and <= (byte)'z'))
            {
                return false;
            }
        }

        return true;
    }

    private static uint UpdateCrc(uint current, ReadOnlySpan<byte> value)
    {
        var crc = current;
        foreach (var item in value)
        {
            crc = CrcTable[(crc ^ item) & 0xff] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? 0xedb88320 ^ (value >> 1)
                    : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    private static string Summary(
        string action,
        InventoryBuild build,
        int blockerCount)
    {
        var eligible = build.Assets.Count(entry => entry.ExportEligible);
        var duplicates = build.Assets.Count(entry => entry.DuplicateOf is not null);
        return "Content inventory "
            + $"{action}: files={build.Assets.Count} bytes={build.TotalBytes} "
            + $"eligible={eligible} duplicates={duplicates} "
            + $"release_blockers={blockerCount}.";
    }

    private static RepositoryCheckResult Failed(IReadOnlyList<string> failures) =>
        new(
            "Content inventory",
            false,
            string.Empty,
            failures
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());

    private static IntegrityResult Invalid(string detail) => new("invalid", detail);

    private static bool IsExpectedFailure(Exception exception) =>
        ExpectedFailureTypes.Any(type => type.IsInstanceOfType(exception));

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private sealed record ContentPolicy(
        string AssetRoot,
        IReadOnlyList<PolicyRule> Rules);

    private sealed record PolicyRule(
        string Id,
        IReadOnlyList<string> Patterns,
        string Role,
        string PackId,
        string RuntimeUse,
        string ShipStatus,
        ContentRights Rights);

    private sealed record ContentRights(
        string Status,
        string Source,
        string License,
        string Attribution,
        string ReviewNote);

    private sealed record CompiledRule(
        PolicyRule Rule,
        IReadOnlyList<Regex> Patterns);

    private sealed record InventoryFile(string Path, string RelativePath);

    private sealed record AssetEntry(
        string Id,
        string Path,
        string MediaType,
        long Bytes,
        string Sha256,
        string IntegrityStatus,
        string IntegrityDetail,
        string Role,
        string PackId,
        string RuntimeUse,
        string ShipStatus,
        bool ExportEligible,
        ContentRights Rights,
        string PolicyRule,
        string? DuplicateOf);

    private sealed record InventoryBuild(
        string RepositoryRoot,
        string Json,
        IReadOnlyList<AssetEntry> Assets,
        long TotalBytes);

    private readonly record struct IntegrityResult(string Status, string Detail);

    private sealed record PngHeader(
        uint Width,
        uint Height,
        byte BitDepth,
        byte ColorType,
        byte Interlace);

    private readonly record struct PngHeaderResult(PngHeader? Header, string Detail);

    private readonly record struct FileSegment(long Offset, long Length);

    private readonly record struct ZlibEnvelope(uint Adler32);

    private readonly record struct Mp3Frame(
        int Length,
        int Version,
        int Layer,
        int SampleRate);

    private sealed class PngScanlineValidator
    {
        private readonly int[] rowPayloadBytes;
        private int rowIndex;
        private int rowOffset;
        private uint adlerA = 1;
        private uint adlerB;

        public PngScanlineValidator(PngHeader header)
        {
            rowPayloadBytes = ScanlineLengths(header);
            var decodedBytes = rowPayloadBytes.Sum(value => (long)value + 1);
            if (decodedBytes > MaximumPngDecodedBytes)
            {
                throw new InvalidDataException(
                    $"PNG decoded image exceeds the {MaximumPngDecodedBytes}-byte limit");
            }
        }

        public uint Adler32 => (adlerB << 16) | adlerA;

        public void Consume(ReadOnlySpan<byte> value)
        {
            foreach (var item in value)
            {
                adlerA = (adlerA + item) % 65_521;
                adlerB = (adlerB + adlerA) % 65_521;
            }

            var cursor = 0;
            while (cursor < value.Length)
            {
                if (rowIndex >= rowPayloadBytes.Length)
                {
                    throw new InvalidDataException(
                        "PNG image data exceeds the expected decoded size");
                }

                var rowTotal = rowPayloadBytes[rowIndex] + 1;
                if (rowOffset == 0)
                {
                    var filter = value[cursor];
                    if (filter > 4)
                    {
                        throw new InvalidDataException(
                            $"PNG scanline uses invalid filter method {filter}");
                    }

                    cursor++;
                    rowOffset = 1;
                }

                var consumed = Math.Min(value.Length - cursor, rowTotal - rowOffset);
                cursor += consumed;
                rowOffset += consumed;
                if (rowOffset == rowTotal)
                {
                    rowIndex++;
                    rowOffset = 0;
                }
            }
        }

        public void Finish()
        {
            if (rowIndex != rowPayloadBytes.Length || rowOffset != 0)
            {
                throw new InvalidDataException(
                    "PNG image data does not contain every expected scanline");
            }
        }

        private static int[] ScanlineLengths(PngHeader header)
        {
            var channels = header.ColorType switch
            {
                0 or 3 => 1,
                2 => 3,
                4 => 2,
                6 => 4,
                _ => throw new InvalidDataException("PNG color type is unsupported"),
            };
            var bitsPerPixel = channels * header.BitDepth;
            if (header.Interlace == 0)
            {
                var rowBytes = checked((int)(((ulong)header.Width * (uint)bitsPerPixel + 7) / 8));
                return Enumerable.Repeat(rowBytes, checked((int)header.Height)).ToArray();
            }

            var rows = new List<int>();
            foreach (var pass in new[]
            {
                (StartX: 0U, StartY: 0U, StepX: 8U, StepY: 8U),
                (StartX: 4U, StartY: 0U, StepX: 8U, StepY: 8U),
                (StartX: 0U, StartY: 4U, StepX: 4U, StepY: 8U),
                (StartX: 2U, StartY: 0U, StepX: 4U, StepY: 4U),
                (StartX: 0U, StartY: 2U, StepX: 2U, StepY: 4U),
                (StartX: 1U, StartY: 0U, StepX: 2U, StepY: 2U),
                (StartX: 0U, StartY: 1U, StepX: 1U, StepY: 2U),
            })
            {
                if (header.Width <= pass.StartX || header.Height <= pass.StartY)
                {
                    continue;
                }

                var passWidth = (header.Width - pass.StartX + pass.StepX - 1) / pass.StepX;
                var passHeight = (header.Height - pass.StartY + pass.StepY - 1) / pass.StepY;
                var rowBytes = checked((int)(((ulong)passWidth * (uint)bitsPerPixel + 7) / 8));
                rows.AddRange(Enumerable.Repeat(rowBytes, checked((int)passHeight)));
            }

            return rows.ToArray();
        }
    }

    private sealed class SegmentedReadStream : Stream
    {
        private readonly FileStream source;
        private readonly IReadOnlyList<FileSegment> segments;
        private int segmentIndex;
        private long segmentOffset;

        public SegmentedReadStream(string path, IReadOnlyList<FileSegment> segments)
        {
            source = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.RandomAccess);
            this.segments = segments;
            TotalBytes = segments.Sum(segment => segment.Length);
        }

        public long BytesRead { get; private set; }

        public long TotalBytes { get; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => TotalBytes;

        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            while (segmentIndex < segments.Count
                && segmentOffset == segments[segmentIndex].Length)
            {
                segmentIndex++;
                segmentOffset = 0;
            }

            if (segmentIndex >= segments.Count)
            {
                return 0;
            }

            var segment = segments[segmentIndex];
            source.Position = segment.Offset + segmentOffset;
            var logicalRemaining = TotalBytes - BytesRead;
            var maximumRead = logicalRemaining > 16
                ? Math.Max(1, logicalRemaining - 16)
                : 1;
            var count = source.Read(
                buffer[..checked((int)Math.Min(
                    Math.Min(buffer.Length, segment.Length - segmentOffset),
                    maximumRead))]);
            segmentOffset += count;
            BytesRead += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                source.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
