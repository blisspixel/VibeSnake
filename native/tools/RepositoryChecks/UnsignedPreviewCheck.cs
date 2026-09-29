using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

internal static class UnsignedPreviewCheck
{
    internal static int MaximumRadioPackCompressedBytes = 80 * 1024 * 1024;

    private const int MaximumJsonBytes = 1_048_576;
    private const int MaximumRadioPackFiles = 4_096;
    private const long MaximumRadioPackInstalledBytes = 120L * 1024 * 1024;
    private const ushort ZipStored = 0;
    private const int UnixCreateSystem = 3;
    private const uint UnixRegularFileType = 0x8000;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly string[] Platforms = ["windows-x64", "macos-universal", "linux-x64"];
    private static readonly string[] KnownLimitations =
    [
        "Windows and macOS packages are unsigned.",
        "macOS Gatekeeper may require an explicit local override.",
        "The approved radio pack is a separate download and is not embedded in the base-game archives.",
        "Alpha save and replay compatibility may change before stable release.",
    ];
    private static readonly HashSet<string> PlatformSet = new(Platforms, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.Ordinal)
    {
        ["windows-x64"] = ".zip",
        ["macos-universal"] = ".zip",
        ["linux-x64"] = ".tar.gz",
    };
    private static readonly HashSet<string> RadioAssemblyFields = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "kind",
        "passed",
        "releaseApproved",
        "packId",
        "packVersion",
        "stationId",
        "stationName",
        "curationDecisionStatus",
        "inventorySha256",
        "curationSha256",
        "manifestSha256",
        "packFileName",
        "packBytes",
        "packSha256",
        "trackCount",
        "trackIds",
    };
    private static readonly Regex SemverPattern = new(
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(alpha|beta|rc)\.([1-9][0-9]*))?\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AlphaPattern = new(
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-alpha\.([1-9][0-9]*)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RevisionPattern = new(
        @"\A[0-9a-f]{40}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(
        @"\A[0-9a-f]{64}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex IntegerPattern = new(
        @"\A-?(0|[1-9][0-9]*)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PackIdPattern = new(
        @"\Avibesnake\.radio\.[a-z0-9]+(?:-[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PackVersionPattern = new(
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex StationIdPattern = new(
        @"\A[a-z0-9]+(?:_[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex QualificationChecksumPattern = new(
        @"\A([0-9a-f]{64}) \*([^/\\]+)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RadioChecksumPattern = new(
        @"\A([0-9a-f]{64})  ([^/\\]+)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
    };

    internal readonly record struct Assembly(bool Passed, IReadOnlyList<string> Errors, string Json);

    internal static Assembly Assemble(
        string channelRoot,
        string provenanceRoot,
        string radioPackRoot,
        string matrixPath,
        string versionRoot,
        string tagName,
        string expectedRevision,
        string outputRoot)
    {
        var errors = new List<string>();
        var documents = new List<JsonDocument>();
        try
        {
            var productVersion = ReadProductVersion(versionRoot, errors);
            if (!AlphaPattern.IsMatch(productVersion))
            {
                errors.Add("unsigned preview publication requires a canonical alpha product version");
            }

            if (!string.Equals(tagName, "v" + productVersion, StringComparison.Ordinal))
            {
                errors.Add($"tag must exactly match canonical product version v{productVersion}");
            }

            if (expectedRevision is null || !RevisionPattern.IsMatch(expectedRevision))
            {
                errors.Add("expected revision must be a lowercase 40-character Git revision");
            }

            var approved = ReadApprovedRadio(radioPackRoot, errors, documents);
            var rows = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var matrix = ReadStrictJson(matrixPath, "release matrix", errors, documents);
            if (matrix is not null && matrix.RootElement.ValueKind == JsonValueKind.Object)
            {
                var matrixRoot = matrix.RootElement;
                ExpectInt(matrixRoot, "schemaVersion", 1, "matrix", errors);
                ExpectString(matrixRoot, "kind", "release-matrix-qualification-v1", "matrix", errors);
                ExpectBool(matrixRoot, "passed", true, "matrix", errors);
                ExpectString(matrixRoot, "sourceRevision", expectedRevision ?? string.Empty, "matrix", errors);
                ExpectString(matrixRoot, "buildMode", "Release", "matrix", errors);
                ExpectString(matrixRoot, "productVersion", productVersion, "matrix", errors);
                ExpectBool(matrixRoot, "publicationEligible", false, "matrix", errors);
                rows = PlatformRows(matrixRoot, errors);
            }

            var prepared = new List<PreparedPackage>();
            foreach (var platform in Platforms)
            {
                CollectPlatform(
                    platform,
                    channelRoot,
                    provenanceRoot,
                    productVersion,
                    expectedRevision ?? string.Empty,
                    rows,
                    errors,
                    documents,
                    prepared);
            }

            string? matrixHash = null;
            if (IsRegularFile(matrixPath))
            {
                try
                {
                    matrixHash = Sha256File(matrixPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"unreadable release matrix: {matrixPath}: {exception.Message}");
                }
            }

            var evidence = new PreviewEvidence
            {
                ProductVersion = productVersion,
                TagName = tagName ?? string.Empty,
                SourceRevision = expectedRevision ?? string.Empty,
                ReleaseMatrixSha256 = matrixHash,
                Errors = errors,
                Passed = errors.Count == 0,
            };
            if (errors.Count > 0)
            {
                return Complete(evidence);
            }

            if (OutputExists(outputRoot))
            {
                var error = $"preview output must not already exist: {outputRoot}";
                evidence.Passed = false;
                evidence.Errors = [error];
                return Complete(evidence);
            }

            try
            {
                WriteOutput(outputRoot, productVersion, prepared, approved, evidence);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                DeleteOutput(outputRoot);
                var message = $"could not assemble preview output: {exception.Message}";
                evidence.Passed = false;
                evidence.Errors = [message];
                return Complete(evidence);
            }

            return Complete(evidence);
        }
        finally
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }
        }
    }

    private static string ReadProductVersion(string versionRoot, List<string> errors)
    {
        var path = Path.Combine(versionRoot, "VERSION");
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not read canonical product version from {path}: {exception.Message}");
            return string.Empty;
        }

        string source;
        try
        {
            source = Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            errors.Add($"Could not read canonical product version from {path}: {exception.Message}");
            return string.Empty;
        }

        var newlines = 0;
        foreach (var character in source)
        {
            if (character == '\n')
            {
                newlines++;
            }
        }

        if (!source.EndsWith('\n') || newlines != 1 || source.Contains('\r', StringComparison.Ordinal))
        {
            errors.Add("VERSION must contain exactly one UTF-8 line terminated by LF");
            return string.Empty;
        }

        var version = source[..^1];
        if (!SemverPattern.IsMatch(version))
        {
            errors.Add(
                "VERSION must contain one canonical stable or prerelease SemVer; got "
                + StrictJsonFile.Quote(version));
            return string.Empty;
        }

        return version;
    }

    private static ApprovedRadio? ReadApprovedRadio(
        string root,
        List<string> errors,
        List<JsonDocument> documents)
    {
        var assemblyPath = Path.Combine(root, "radio_pack_assembly.json");
        var manifestPath = Path.Combine(root, "pack.json");
        var checksumsPath = Path.Combine(root, "SHA256SUMS.txt");
        var assemblyDocument = ReadStrictJson(assemblyPath, "radio-pack assembly evidence", errors, documents);
        var manifestDocument = ReadStrictJson(manifestPath, "radio-pack manifest", errors, documents);
        if (assemblyDocument is null
            || assemblyDocument.RootElement.ValueKind != JsonValueKind.Object
            || manifestDocument is null
            || manifestDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var assembly = assemblyDocument.RootElement;
        var manifest = manifestDocument.RootElement;
        if (!RadioAssemblyFields.SetEquals(PropertyNames(assembly)))
        {
            errors.Add("radio-pack assembly evidence has unexpected or missing fields");
            return null;
        }

        ExpectInt(assembly, "schemaVersion", 1, "radio-pack assembly", errors);
        ExpectString(assembly, "kind", "approved-radio-pack-assembly-v1", "radio-pack assembly", errors);
        ExpectBool(assembly, "passed", true, "radio-pack assembly", errors);
        ExpectBool(assembly, "releaseApproved", true, "radio-pack assembly", errors);
        ExpectString(assembly, "curationDecisionStatus", "approved-for-alpha-release", "radio-pack assembly", errors);

        var packId = Get(assembly, "packId");
        var packVersion = Get(assembly, "packVersion");
        var stationId = Get(assembly, "stationId");
        var stationName = Get(assembly, "stationName");
        var fileName = Get(assembly, "packFileName");
        var packIdValid = IsMatch(packId, PackIdPattern);
        var packVersionValid = IsMatch(packVersion, PackVersionPattern);
        var stationIdValid = IsMatch(stationId, StationIdPattern);
        if (!packIdValid)
        {
            errors.Add("radio-pack assembly packId is invalid");
        }

        if (!packVersionValid)
        {
            errors.Add("radio-pack assembly packVersion is invalid");
        }

        if (!stationIdValid)
        {
            errors.Add("radio-pack assembly stationId is invalid");
        }

        if (!IsValidStationName(stationName))
        {
            errors.Add("radio-pack assembly stationName is invalid");
        }

        string? expectedName = null;
        if (packIdValid && packVersionValid)
        {
            expectedName = packId.Value.GetString() + "-" + packVersion.Value.GetString() + ".vibesnake-pack.zip";
        }

        if (!PackFileNameEquals(fileName, expectedName))
        {
            errors.Add("radio-pack assembly packFileName must be " + (expectedName ?? "None"));
        }

        // A supplied name is opened only when it is exactly the canonical archive name.
        // Any other value, including ../ or an absolute path, resolves inside the root.
        var packPath = PackFileNameEquals(fileName, expectedName) && expectedName is not null
            ? Path.Combine(root, expectedName)
            : Path.Combine(root, ".invalid-radio-pack");
        var packBytes = Get(assembly, "packBytes");
        var packSha = Get(assembly, "packSha256");
        if (!IsPositiveIntegerWithin(packBytes, MaximumRadioPackCompressedBytes))
        {
            errors.Add("radio-pack assembly packBytes must be a positive integer within the compressed-size budget");
        }

        if (!IsSha256(packSha))
        {
            errors.Add("radio-pack assembly packSha256 must be a SHA-256 digest");
        }

        if (!IsRegularFile(packPath))
        {
            errors.Add($"missing approved radio pack: {packPath}");
        }
        else
        {
            if (!JsonEqualsLong(packBytes, new FileInfo(packPath).Length))
            {
                errors.Add("approved radio-pack byte count changed");
            }

            if (!JsonEqualsString(packSha, Sha256File(packPath)))
            {
                errors.Add("approved radio-pack hash changed");
            }
        }

        var manifestKind = Get(manifest, "kind");
        var manifestId = Get(manifest, "id");
        var manifestVersion = Get(manifest, "version");
        if (!JsonEqualsString(manifestKind, "radio")
            || !JsonFieldsEqual(manifestId, packId)
            || !JsonFieldsEqual(manifestVersion, packVersion))
        {
            errors.Add("radio-pack manifest identity does not match assembly evidence");
        }

        var radio = Get(manifest, "radio");
        var trackIdsField = Get(assembly, "trackIds");
        var trackCount = Get(assembly, "trackCount");
        if (!TryGetTrackIds(trackIdsField, out var trackIds))
        {
            errors.Add("radio-pack assembly trackIds must be a unique nonempty array");
        }
        else if (!IsPositiveIntegerWithin(trackCount, int.MaxValue)
            || !TryGetBigInteger(trackCount.Value, out var trackCountValue)
            || trackCountValue != trackIds.Length
            || !radio.Found
            || radio.Value.ValueKind != JsonValueKind.Object
            || !JsonFieldsEqual(Get(radio.Value, "stationId"), stationId)
            || !JsonFieldsEqual(Get(radio.Value, "stationName"), stationName)
            || !JsonFieldsEqual(Get(radio.Value, "trackIds"), trackIdsField))
        {
            errors.Add("radio-pack assembly track evidence does not match the manifest");
        }

        var manifestHash = Sha256File(manifestPath);
        if (!JsonEqualsString(Get(assembly, "manifestSha256"), manifestHash))
        {
            errors.Add("radio-pack manifest hash does not match assembly evidence");
        }

        foreach (var field in new[] { "inventorySha256", "curationSha256", "manifestSha256" })
        {
            if (!IsSha256(Get(assembly, field)))
            {
                errors.Add($"radio-pack assembly {field} must be a SHA-256 digest");
            }
        }

        var fileNameText = PythonStr(fileName);
        var expectedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            fileNameText,
            Path.GetFileName(assemblyPath),
            Path.GetFileName(manifestPath),
            Path.GetFileName(checksumsPath),
        };
        if (!RelativeFiles(root).SetEquals(expectedFiles))
        {
            errors.Add("approved radio-pack artifact contains an unexpected file set");
        }

        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var lines = SplitLines(Utf8.GetString(File.ReadAllBytes(checksumsPath)));
            for (var index = 0; index < lines.Count; index++)
            {
                var match = RadioChecksumPattern.Match(lines[index]);
                if (!match.Success || checksums.ContainsKey(match.Groups[2].Value))
                {
                    errors.Add($"radio-pack checksum line {index + 1} is malformed or repeated");
                    continue;
                }

                checksums.Add(match.Groups[2].Value, match.Groups[1].Value);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            errors.Add($"unreadable radio-pack checksums: {checksumsPath}: {exception.Message}");
        }

        var expectedChecksumFiles = new HashSet<string>(expectedFiles, StringComparer.Ordinal);
        expectedChecksumFiles.Remove(Path.GetFileName(checksumsPath));
        if (!new HashSet<string>(checksums.Keys, StringComparer.Ordinal).SetEquals(expectedChecksumFiles))
        {
            errors.Add("radio-pack checksums must cover exactly the pack, manifest, and assembly evidence");
        }

        foreach (var (name, digest) in checksums)
        {
            var target = Path.Combine(root, name);
            if (!IsRegularFile(target) || !string.Equals(Sha256File(target), digest, StringComparison.Ordinal))
            {
                errors.Add($"radio-pack checksum mismatch for {name}");
            }
        }

        var filesField = Get(manifest, "files");
        var filesAreValid = filesField.Found
            && filesField.Value.ValueKind == JsonValueKind.Array
            && filesField.Value.GetArrayLength() > 0
            && filesField.Value.GetArrayLength() <= MaximumRadioPackFiles;
        if (!filesAreValid)
        {
            errors.Add("radio-pack manifest files must be a bounded nonempty array");
        }

        if (IsRegularFile(packPath) && filesAreValid)
        {
            ValidateRadioArchive(packPath, manifestPath, filesField.Value, errors);
        }

        return new ApprovedRadio(packPath, manifestPath, assemblyPath, assembly);
    }

    private static void ValidateRadioArchive(
        string packPath,
        string manifestPath,
        JsonElement files,
        List<string> errors)
    {
        var expectedNames = new HashSet<string>(StringComparer.Ordinal) { "pack.json" };
        var expectedHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pack.json"] = Sha256File(manifestPath),
        };
        var expectedBytes = new Dictionary<string, BigInteger>(StringComparer.Ordinal)
        {
            ["pack.json"] = new BigInteger(new FileInfo(manifestPath).Length),
        };
        var casefoldedPaths = new HashSet<string>(StringComparer.Ordinal);
        BigInteger installedBytes = 0;
        foreach (var entry in files.EnumerateArray())
        {
            if (!TryReadArchiveEntry(entry, casefoldedPaths, out var path, out var size, out var digest))
            {
                errors.Add("radio-pack manifest contains an invalid file entry");
                continue;
            }

            installedBytes += size;
            expectedNames.Add(path);
            expectedHashes[path] = digest;
            expectedBytes[path] = size;
        }

        if (installedBytes > new BigInteger(MaximumRadioPackInstalledBytes))
        {
            errors.Add("radio-pack manifest exceeds the installed-size budget");
        }

        try
        {
            var bytes = File.ReadAllBytes(packPath);
            var members = ReadZipMembers(bytes);
            var names = new List<string>(members.Count);
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            var duplicated = false;
            foreach (var member in members)
            {
                names.Add(member.Name);
                if (!distinct.Add(member.Name))
                {
                    duplicated = true;
                }
            }

            if (duplicated || !distinct.SetEquals(expectedNames))
            {
                errors.Add("approved radio-pack archive does not match the manifest allowlist");
            }

            foreach (var member in members)
            {
                var unixFileType = (member.ExternalAttributes >> 16) & 0xF000;
                if (IsZipDirectory(member.Name)
                    || member.Method != ZipStored
                    || member.CreateSystem != UnixCreateSystem
                    || unixFileType != UnixRegularFileType)
                {
                    errors.Add($"approved radio-pack entry has an unsupported shape: {member.Name}");
                    continue;
                }

                if (!expectedBytes.TryGetValue(member.Name, out var expectedSize)
                    || expectedSize != new BigInteger(member.UncompressedSize))
                {
                    errors.Add($"approved radio-pack archive size mismatch for {member.Name}");
                    continue;
                }

                var payload = ExtractStored(bytes, member);
                var digest = Convert.ToHexStringLower(SHA256.HashData(payload));
                if (!expectedHashes.TryGetValue(member.Name, out var expectedHash)
                    || !string.Equals(digest, expectedHash, StringComparison.Ordinal))
                {
                    errors.Add($"approved radio-pack archive hash mismatch for {member.Name}");
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            errors.Add($"approved radio-pack archive is unreadable: {exception.Message}");
        }
    }

    private static bool TryReadArchiveEntry(
        JsonElement entry,
        HashSet<string> casefoldedPaths,
        out string path,
        out BigInteger size,
        out string digest)
    {
        path = string.Empty;
        size = 0;
        digest = string.Empty;
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var pathField = Get(entry, "path");
        var sizeField = Get(entry, "bytes");
        var digestField = Get(entry, "sha256");
        if (!pathField.Found || pathField.Value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        path = pathField.Value.GetString() ?? string.Empty;
        if (path.Contains('\\', StringComparison.Ordinal)
            || path.StartsWith('/')
            || path.EndsWith('/')
            || !casefoldedPaths.Add(CaseFold(path)))
        {
            return false;
        }

        foreach (var part in path.Split('/'))
        {
            if (part is "" or "." or "..")
            {
                casefoldedPaths.Remove(CaseFold(path));
                return false;
            }
        }

        if (!TryGetBigInteger(sizeField.Found ? sizeField.Value : default, out size) || size <= 0)
        {
            casefoldedPaths.Remove(CaseFold(path));
            return false;
        }

        if (!digestField.Found || !IsSha256(digestField))
        {
            casefoldedPaths.Remove(CaseFold(path));
            return false;
        }

        digest = digestField.Value.GetString() ?? string.Empty;
        return true;
    }

    private static void CollectPlatform(
        string platform,
        string channelRoot,
        string provenanceRoot,
        string productVersion,
        string expectedRevision,
        Dictionary<string, JsonElement> rows,
        List<string> errors,
        List<JsonDocument> documents,
        List<PreparedPackage> prepared)
    {
        var inputRoot = Path.Combine(channelRoot, $"vibesnake-{platform}-unsigned-channel-shape");
        var planPath = Path.Combine(inputRoot, "release_output_plan.json");
        var manifestPath = Path.Combine(inputRoot, "artifact-manifest.json");
        var checksumsPath = Path.Combine(inputRoot, "SHA256SUMS");
        var expectedQualificationName =
            $"VibeSnake-{productVersion}-{platform}-qualification{Extensions[platform]}";
        var packagePath = Path.Combine(inputRoot, expectedQualificationName);
        var planDocument = ReadStrictJson(planPath, $"{platform} output plan", errors, documents);
        var manifestDocument = ReadStrictJson(manifestPath, $"{platform} artifact manifest", errors, documents);
        var planIsObject = planDocument is not null && planDocument.RootElement.ValueKind == JsonValueKind.Object;
        var manifestIsObject = manifestDocument is not null
            && manifestDocument.RootElement.ValueKind == JsonValueKind.Object;
        JsonField packageBytes = default;
        JsonField packageSha = default;
        if (planIsObject)
        {
            var plan = planDocument!.RootElement;
            var planLabel = $"{platform} plan";
            ExpectInt(plan, "schemaVersion", 1, planLabel, errors);
            ExpectString(plan, "kind", "release-output-plan-v1", planLabel, errors);
            ExpectString(plan, "product", "Vibe Snake", planLabel, errors);
            ExpectString(plan, "productVersion", productVersion, planLabel, errors);
            ExpectString(plan, "platform", platform, planLabel, errors);
            ExpectString(plan, "directDownloadFileName", expectedQualificationName, planLabel, errors);
            ExpectBool(plan, "passed", true, planLabel, errors);
            ExpectBool(plan, "qualificationOnly", true, planLabel, errors);
            ExpectBool(plan, "assemblyEligible", true, planLabel, errors);
            ExpectBool(plan, "publicationEligible", false, planLabel, errors);
            ExpectBool(plan, "optionalPackOutputSeparate", true, planLabel, errors);
            ExpectBool(plan, "baseGameIncludesOptionalPacks", false, planLabel, errors);
            ExpectBool(plan, "playerDataExcluded", true, planLabel, errors);
            ExpectBool(plan, "uninstallPreservesPlayerData", true, planLabel, errors);
            ExpectBool(plan, "deterministicRepeatMatched", true, planLabel, errors);
            packageBytes = Get(plan, "packageBytes");
            packageSha = Get(plan, "packageSha256");
            if (!IsPositiveInteger(packageBytes))
            {
                errors.Add($"{platform} plan.packageBytes must be a positive integer");
            }

            if (!IsSha256(packageSha))
            {
                errors.Add($"{platform} plan.packageSha256 must be a SHA-256 digest");
            }

            if (!IsRegularFile(packagePath))
            {
                errors.Add($"missing {platform} qualification package: {packagePath}");
            }
            else
            {
                if (!JsonEqualsLong(packageBytes, new FileInfo(packagePath).Length))
                {
                    errors.Add($"{platform} qualification package byte count changed");
                }

                if (!JsonEqualsString(packageSha, Sha256File(packagePath)))
                {
                    errors.Add($"{platform} qualification package hash changed");
                }
            }

            if (rows.TryGetValue(platform, out var row))
            {
                var rowLabel = $"{platform} matrix row";
                ExpectValue(row, "packageSha256", packageSha, rowLabel, errors);
                ExpectValue(row, "packageBytes", packageBytes, rowLabel, errors);
                ExpectString(row, "directDownloadFileName", expectedQualificationName, rowLabel, errors);
            }
        }

        if (manifestIsObject)
        {
            var manifest = manifestDocument!.RootElement;
            var manifestLabel = $"{platform} artifact manifest";
            ExpectInt(manifest, "schemaVersion", 3, manifestLabel, errors);
            ExpectString(manifest, "product", "Vibe Snake", manifestLabel, errors);
            ExpectString(manifest, "platform", platform, manifestLabel, errors);
            ExpectString(manifest, "buildMode", "Release", manifestLabel, errors);
            ExpectString(manifest, "sourceRevision", expectedRevision, manifestLabel, errors);
            if (rows.TryGetValue(platform, out var row))
            {
                ExpectString(
                    row,
                    "artifactManifestSha256",
                    Sha256File(manifestPath),
                    $"{platform} matrix row",
                    errors);
            }
        }

        var checksums = ParseQualificationChecksums(checksumsPath, errors);
        var expectedChecksumNames = new HashSet<string>(StringComparer.Ordinal)
        {
            expectedQualificationName,
            Path.GetFileName(planPath),
            Path.GetFileName(manifestPath),
        };
        if (!new HashSet<string>(checksums.Keys, StringComparer.Ordinal).SetEquals(expectedChecksumNames))
        {
            errors.Add($"{platform} qualification checksums must cover exactly the package and manifests");
        }

        foreach (var (name, digest) in checksums)
        {
            var target = Path.Combine(inputRoot, name);
            if (!IsRegularFile(target) || !string.Equals(Sha256File(target), digest, StringComparison.Ordinal))
            {
                errors.Add($"{platform} qualification checksum mismatch for {name}");
            }
        }

        var expectedFiles = new HashSet<string>(expectedChecksumNames, StringComparer.Ordinal)
        {
            Path.GetFileName(checksumsPath),
        };
        if (!RelativeFiles(inputRoot).SetEquals(expectedFiles))
        {
            errors.Add($"{platform} channel-shape artifact contains an unexpected file set");
        }

        var provenancePath = OneProvenanceFile(provenanceRoot, platform, errors);
        if (planIsObject && manifestIsObject && IsRegularFile(packagePath) && provenancePath is not null)
        {
            long storedBytes = -1;
            if (packageBytes.Found
                && TryGetBigInteger(packageBytes.Value, out var integer)
                && integer >= 0
                && integer <= long.MaxValue)
            {
                storedBytes = (long)integer;
            }

            prepared.Add(new PreparedPackage(
                platform,
                packagePath,
                expectedQualificationName,
                packageSha.Found && packageSha.Value.ValueKind == JsonValueKind.String
                    ? packageSha.Value.GetString() ?? string.Empty
                    : string.Empty,
                storedBytes,
                provenancePath,
                Sha256File(manifestPath),
                Sha256File(planPath)));
        }
    }

    private static Dictionary<string, string> ParseQualificationChecksums(string path, List<string> errors)
    {
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        string text;
        try
        {
            text = Utf8.GetString(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            errors.Add($"unreadable qualification checksums: {path}: {exception.Message}");
            return checksums;
        }

        var lines = SplitLines(text);
        for (var index = 0; index < lines.Count; index++)
        {
            var match = QualificationChecksumPattern.Match(lines[index]);
            if (!match.Success)
            {
                errors.Add($"qualification checksum line {index + 1} is malformed");
                continue;
            }

            var name = match.Groups[2].Value;
            if (!checksums.TryAdd(name, match.Groups[1].Value))
            {
                errors.Add($"qualification checksums repeat {name}");
            }
        }

        return checksums;
    }

    private static Dictionary<string, JsonElement> PlatformRows(JsonElement matrix, List<string> errors)
    {
        var rows = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var platforms = Get(matrix, "platforms");
        if (!platforms.Found
            || platforms.Value.ValueKind != JsonValueKind.Array
            || platforms.Value.GetArrayLength() != Platforms.Length)
        {
            errors.Add("release matrix must contain exactly three platform rows");
            return rows;
        }

        var index = 0;
        foreach (var row in platforms.Value.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"release matrix platform row {index} must be an object");
                index++;
                continue;
            }

            var platform = Get(row, "platform");
            var name = platform.Found && platform.Value.ValueKind == JsonValueKind.String
                ? platform.Value.GetString()
                : null;
            if (name is null || !PlatformSet.Contains(name) || rows.ContainsKey(name))
            {
                errors.Add($"release matrix platform row {index} must have a unique supported platform");
                index++;
                continue;
            }

            rows.Add(name, row);
            index++;
        }

        return rows;
    }

    private static string? OneProvenanceFile(string root, string platform, List<string> errors)
    {
        var artifactRoot = Path.Combine(root, $"vibesnake-{platform}-provenance");
        var files = new List<string>();
        if (Directory.Exists(artifactRoot))
        {
            files.AddRange(Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories));
        }

        if (files.Count != 1
            || new FileInfo(files[0]).Length == 0
            || !string.Equals(Path.GetExtension(files[0]), ".jsonl", StringComparison.Ordinal))
        {
            errors.Add($"{platform} provenance artifact must contain exactly one nonempty JSONL file");
            return null;
        }

        return files[0];
    }

    private static void WriteOutput(
        string outputRoot,
        string productVersion,
        List<PreparedPackage> prepared,
        ApprovedRadio? approved,
        PreviewEvidence evidence)
    {
        Directory.CreateDirectory(outputRoot);
        var packageRows = new List<PackageRow>();
        var checksums = new List<(string Digest, string Name)>();
        foreach (var item in prepared)
        {
            var previewName = $"VibeSnake-{productVersion}-{item.Platform}-unsigned-preview{Extensions[item.Platform]}";
            var previewPath = Path.Combine(outputRoot, previewName);
            File.Copy(item.SourcePackage, previewPath);
            var packageDigest = Sha256File(previewPath);
            if (!string.Equals(packageDigest, item.PackageSha256, StringComparison.Ordinal)
                || new FileInfo(previewPath).Length != item.PackageBytes)
            {
                throw new IOException($"copied preview package changed for {item.Platform}");
            }

            var provenanceName = $"VibeSnake-{productVersion}-{item.Platform}-provenance.jsonl";
            var provenancePath = Path.Combine(outputRoot, provenanceName);
            File.Copy(item.Provenance, provenancePath);
            var provenanceDigest = Sha256File(provenancePath);
            packageRows.Add(new PackageRow(
                item.Platform,
                previewName,
                new FileInfo(previewPath).Length,
                packageDigest,
                item.SourcePackageName,
                item.ArtifactManifestSha256,
                item.OutputPlanSha256,
                provenanceName,
                provenanceDigest));
            checksums.Add((packageDigest, previewName));
            checksums.Add((provenanceDigest, provenanceName));
        }

        if (approved is null)
        {
            throw new IOException("approved radio-pack evidence disappeared during assembly");
        }

        var radio = approved.Value.Assembly;
        var packId = RequiredString(radio, "packId");
        var packVersion = RequiredString(radio, "packVersion");
        var radioPrefix = $"VibeSnake-{productVersion}-{packId}";
        var radioName = $"{radioPrefix}-{packVersion}.vibesnake-pack.zip";
        var radioPath = Path.Combine(outputRoot, radioName);
        File.Copy(approved.Value.SourcePack, radioPath);
        var radioManifestName = $"{radioPrefix}-manifest.json";
        var radioManifestPath = Path.Combine(outputRoot, radioManifestName);
        File.Copy(approved.Value.SourceManifest, radioManifestPath);
        var radioEvidenceName = $"{radioPrefix}-assembly.json";
        var radioEvidencePath = Path.Combine(outputRoot, radioEvidenceName);
        File.Copy(approved.Value.SourceAssembly, radioEvidencePath);
        var sourceAssemblyHash = Sha256File(approved.Value.SourceAssembly);
        var expectedRadioHashes = new (string Path, string Digest)[]
        {
            (radioPath, RequiredString(radio, "packSha256")),
            (radioManifestPath, RequiredString(radio, "manifestSha256")),
            (radioEvidencePath, sourceAssemblyHash),
        };
        foreach (var (path, digest) in expectedRadioHashes)
        {
            if (!string.Equals(Sha256File(path), digest, StringComparison.Ordinal))
            {
                throw new IOException($"copied approved radio-pack file changed: {Path.GetFileName(path)}");
            }

            checksums.Add((digest, Path.GetFileName(path)));
        }

        if (!TryGetBigInteger(Get(radio, "trackCount").Value, out var trackCount) || trackCount > int.MaxValue)
        {
            throw new IOException("approved radio-pack evidence disappeared during assembly");
        }

        evidence.RadioPack = new RadioRow(
            packId,
            packVersion,
            RequiredString(radio, "stationId"),
            RequiredString(radio, "stationName"),
            (long)trackCount,
            radioName,
            new FileInfo(radioPath).Length,
            RequiredString(radio, "packSha256"),
            radioManifestName,
            RequiredString(radio, "manifestSha256"),
            radioEvidenceName,
            Sha256File(radioEvidencePath));
        evidence.Packages = packageRows;
        var manifestPath = Path.Combine(outputRoot, "unsigned_preview_manifest.json");
        var json = Render(evidence);
        File.WriteAllText(manifestPath, json, Utf8);
        checksums.Add((Sha256File(manifestPath), Path.GetFileName(manifestPath)));
        checksums.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        var checksumBuilder = new StringBuilder();
        foreach (var (digest, name) in checksums)
        {
            checksumBuilder.Append(digest).Append("  ").Append(name).Append('\n');
        }

        File.WriteAllText(Path.Combine(outputRoot, "SHA256SUMS.txt"), checksumBuilder.ToString(), Utf8);
    }

    private static JsonDocument? ReadStrictJson(
        string path,
        string label,
        List<string> errors,
        List<JsonDocument> documents)
    {
        if (!IsRegularFile(path))
        {
            errors.Add($"missing {label}: {path}");
            return null;
        }

        try
        {
            var length = new FileInfo(path).Length;
            if (length > MaximumJsonBytes)
            {
                errors.Add($"{label} exceeds the {MaximumJsonBytes}-byte limit");
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > MaximumJsonBytes)
            {
                errors.Add($"{label} exceeds the {MaximumJsonBytes}-byte limit");
                return null;
            }

            var text = Utf8.GetString(bytes);
            if (Utf8.GetByteCount(text) > MaximumJsonBytes)
            {
                errors.Add($"{label} exceeds the {MaximumJsonBytes}-byte limit");
                return null;
            }

            RejectNonFinite(text);
            RejectDuplicateProperties(bytes);
            var document = JsonDocument.Parse(bytes, JsonOptions);
            documents.Add(document);
            return document;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or JsonException
                or InvalidDataException)
        {
            errors.Add($"unreadable {label}: {path}: {exception.Message}");
            return null;
        }
    }

    private static void RejectNonFinite(string text)
    {
        var inString = false;
        var escape = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (character == '\\')
                {
                    escape = true;
                    continue;
                }

                if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (IsNonFiniteToken(text, index, "-Infinity"))
            {
                throw new InvalidDataException("non-finite JSON number: -Infinity");
            }

            if (IsNonFiniteToken(text, index, "Infinity"))
            {
                throw new InvalidDataException("non-finite JSON number: Infinity");
            }

            if (IsNonFiniteToken(text, index, "NaN"))
            {
                throw new InvalidDataException("non-finite JSON number: NaN");
            }
        }
    }

    private static bool IsNonFiniteToken(string text, int index, string token)
    {
        if (index + token.Length > text.Length || !text.AsSpan(index, token.Length).SequenceEqual(token))
        {
            return false;
        }

        if (index > 0 && IsTokenCharacter(text[index - 1]))
        {
            return false;
        }

        var end = index + token.Length;
        return end >= text.Length || !IsTokenCharacter(text[end]);
    }

    private static bool IsTokenCharacter(char character)
        => character is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' or '.';

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(
            json,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objects.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    if (objects.Count > 0)
                    {
                        objects.Pop();
                    }

                    break;
                case JsonTokenType.PropertyName:
                    if (objects.Count == 0)
                    {
                        throw new InvalidDataException("JSON property is outside an object");
                    }

                    var name = reader.GetString() ?? string.Empty;
                    if (!objects.Peek().Add(name))
                    {
                        throw new InvalidDataException($"duplicate JSON field: {name}");
                    }

                    break;
            }
        }
    }

    private static List<ZipMember> ReadZipMembers(byte[] bytes)
    {
        var eocd = FindEocd(bytes);
        var entryCount = ReadU16(bytes, eocd + 10);
        var directorySize = ReadU32(bytes, eocd + 12);
        var directoryOffset = ReadU32(bytes, eocd + 16);
        if (entryCount == ushort.MaxValue || directorySize == uint.MaxValue || directoryOffset == uint.MaxValue)
        {
            throw new InvalidDataException("ZIP64 central directories are not supported");
        }

        if (directoryOffset > bytes.Length || directorySize > bytes.Length - directoryOffset)
        {
            throw new InvalidDataException("ZIP central directory is truncated");
        }

        var members = new List<ZipMember>(entryCount);
        var cursor = (int)directoryOffset;
        var end = cursor + (int)directorySize;
        for (var index = 0; index < entryCount; index++)
        {
            if (cursor < 0 || cursor > end - 46)
            {
                throw new InvalidDataException("ZIP central directory entry is truncated");
            }

            if (ReadU32(bytes, cursor) != 0x02014b50)
            {
                throw new InvalidDataException("ZIP central directory signature is invalid");
            }

            var madeBy = ReadU16(bytes, cursor + 4);
            var flags = ReadU16(bytes, cursor + 8);
            var method = ReadU16(bytes, cursor + 10);
            var crc = ReadU32(bytes, cursor + 16);
            var compressedSize = ReadU32(bytes, cursor + 20);
            var uncompressedSize = ReadU32(bytes, cursor + 24);
            var nameLength = ReadU16(bytes, cursor + 28);
            var extraLength = ReadU16(bytes, cursor + 30);
            var commentLength = ReadU16(bytes, cursor + 32);
            var externalAttributes = ReadU32(bytes, cursor + 38);
            var localOffset = ReadU32(bytes, cursor + 42);
            var nameStart = cursor + 46;
            var next = nameStart + nameLength + extraLength + commentLength;
            if (next > end)
            {
                throw new InvalidDataException("ZIP central directory name is truncated");
            }

            if (compressedSize == uint.MaxValue || uncompressedSize == uint.MaxValue || localOffset == uint.MaxValue)
            {
                throw new InvalidDataException("ZIP64 entries are not supported");
            }

            var name = DecodeZipName(bytes.AsSpan(nameStart, nameLength), flags);
            members.Add(new ZipMember(
                name,
                method,
                flags,
                madeBy >> 8,
                externalAttributes,
                compressedSize,
                uncompressedSize,
                localOffset,
                crc));
            cursor = next;
        }

        return members;
    }

    private static int FindEocd(byte[] bytes)
    {
        if (bytes.Length < 22)
        {
            throw new InvalidDataException("ZIP archive is truncated");
        }

        var start = Math.Max(0, bytes.Length - 22 - 65535);
        for (var offset = bytes.Length - 22; offset >= start; offset--)
        {
            if (ReadU32(bytes, offset) != 0x06054b50)
            {
                continue;
            }

            var commentLength = ReadU16(bytes, offset + 20);
            if (offset + 22 + commentLength == bytes.Length)
            {
                return offset;
            }
        }

        throw new InvalidDataException("ZIP end of central directory was not found");
    }

    private static byte[] ExtractStored(byte[] bytes, ZipMember member)
    {
        if ((member.Flags & 0x1) != 0)
        {
            throw new InvalidDataException($"File '{member.Name}' is encrypted");
        }

        if (member.CompressedSize != member.UncompressedSize)
        {
            throw new InvalidDataException($"ZIP entry {member.Name} has an inconsistent stored length");
        }

        if (member.LocalHeaderOffset > bytes.Length - 30)
        {
            throw new InvalidDataException("ZIP local header is truncated");
        }

        var local = (int)member.LocalHeaderOffset;
        if (ReadU32(bytes, local) != 0x04034b50)
        {
            throw new InvalidDataException("ZIP local header signature is invalid");
        }

        var nameLength = ReadU16(bytes, local + 26);
        var extraLength = ReadU16(bytes, local + 28);
        var dataOffset = (long)local + 30 + nameLength + extraLength;
        if (dataOffset < 0
            || dataOffset > bytes.Length
            || member.CompressedSize > bytes.Length - dataOffset
            || member.CompressedSize > int.MaxValue)
        {
            throw new InvalidDataException("ZIP entry data is truncated");
        }

        var data = bytes.AsSpan((int)dataOffset, (int)member.CompressedSize).ToArray();
        if (Crc32(data) != member.Crc32)
        {
            throw new InvalidDataException($"Bad CRC-32 for file '{member.Name}'");
        }

        return data;
    }

    private static string DecodeZipName(ReadOnlySpan<byte> bytes, ushort flags)
    {
        string name;
        if ((flags & 0x800) != 0)
        {
            try
            {
                name = Utf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("ZIP entry name is not valid UTF-8", exception);
            }
        }
        else if (IsAscii(bytes))
        {
            name = Utf8.GetString(bytes);
        }
        else
        {
            try
            {
                name = Encoding.GetEncoding(437).GetString(bytes);
            }
            catch (NotSupportedException exception)
            {
                throw new InvalidDataException("ZIP entry name requires CP437", exception);
            }
        }

        var nullIndex = name.IndexOf('\0', StringComparison.Ordinal);
        return nullIndex >= 0 ? name[..nullIndex] : name;
    }

    private static bool IsAscii(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsZipDirectory(string name)
    {
        if (name.EndsWith('/'))
        {
            return true;
        }

        return OperatingSystem.IsWindows() && name.EndsWith('\\');
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

    private static ushort ReadU16(byte[] bytes, int offset)
    {
        if (offset < 0 || bytes.Length < 2 || offset > bytes.Length - 2)
        {
            throw new InvalidDataException("ZIP header is truncated");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    }

    private static uint ReadU32(byte[] bytes, int offset)
    {
        if (offset < 0 || bytes.Length < 4 || offset > bytes.Length - 4)
        {
            throw new InvalidDataException("ZIP header is truncated");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static void ExpectBool(
        JsonElement document,
        string field,
        bool expected,
        string label,
        List<string> errors)
    {
        var actual = Get(document, field);
        var typeMismatch = !actual.Found || actual.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False);
        var equal = actual.Found && actual.Value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);
        if (typeMismatch || !equal)
        {
            errors.Add($"{label}.{field} must be {(expected ? "True" : "False")}; got {Format(actual)}");
        }
    }

    private static void ExpectInt(
        JsonElement document,
        string field,
        int expected,
        string label,
        List<string> errors)
    {
        var actual = Get(document, field);
        var typeMismatch = !actual.Found || !IsInteger(actual.Value);
        var equal = actual.Found
            && TryGetBigInteger(actual.Value, out var number)
            && number == new BigInteger(expected);
        if (typeMismatch || !equal)
        {
            errors.Add(
                $"{label}.{field} must be {expected.ToString(CultureInfo.InvariantCulture)}; got {Format(actual)}");
        }
    }

    private static void ExpectString(
        JsonElement document,
        string field,
        string expected,
        string label,
        List<string> errors)
    {
        var actual = Get(document, field);
        if (!JsonEqualsString(actual, expected))
        {
            errors.Add($"{label}.{field} must be {StrictJsonFile.Quote(expected)}; got {Format(actual)}");
        }
    }

    private static void ExpectValue(
        JsonElement document,
        string field,
        JsonField expected,
        string label,
        List<string> errors)
    {
        var actual = Get(document, field);
        var typeMismatch = false;
        if (expected.Found && expected.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            typeMismatch = !actual.Found || actual.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False);
        }
        else if (expected.Found && IsInteger(expected.Value))
        {
            typeMismatch = !actual.Found || !IsInteger(actual.Value);
        }

        if (typeMismatch || !JsonFieldsEqual(actual, expected))
        {
            errors.Add($"{label}.{field} must be {Format(expected)}; got {Format(actual)}");
        }
    }

    private static string Format(JsonField field)
        => field.Found ? StrictJsonFile.Format(field.Value) : "None";

    private static bool JsonFieldsEqual(JsonField left, JsonField right)
    {
        if (!left.Found && !right.Found)
        {
            return true;
        }

        if (!left.Found || !right.Found)
        {
            return left.Found == right.Found
                && false
                || (!left.Found && right.Value.ValueKind == JsonValueKind.Null)
                || (!right.Found && left.Value.ValueKind == JsonValueKind.Null);
        }

        return PythonEqual(left.Value, right.Value);
    }

    private static bool PythonEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Null && right.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (IsNumberLike(left) && IsNumberLike(right))
        {
            return NumericEqual(left, right);
        }

        if (left.ValueKind == JsonValueKind.String && right.ValueKind == JsonValueKind.String)
        {
            return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
        }

        if (left.ValueKind == JsonValueKind.Array && right.ValueKind == JsonValueKind.Array)
        {
            return ArraysEqual(left, right);
        }

        if (left.ValueKind == JsonValueKind.Object && right.ValueKind == JsonValueKind.Object)
        {
            return ObjectsEqual(left, right);
        }

        return false;
    }

    private static bool ArraysEqual(JsonElement left, JsonElement right)
    {
        if (left.GetArrayLength() != right.GetArrayLength())
        {
            return false;
        }

        var rightItems = new List<JsonElement>();
        foreach (var item in right.EnumerateArray())
        {
            rightItems.Add(item);
        }

        var index = 0;
        foreach (var item in left.EnumerateArray())
        {
            if (!PythonEqual(item, rightItems[index]))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool ObjectsEqual(JsonElement left, JsonElement right)
    {
        var leftNames = PropertyNames(left);
        if (!leftNames.SetEquals(PropertyNames(right)))
        {
            return false;
        }

        foreach (var name in leftNames)
        {
            if (!PythonEqual(left.GetProperty(name), right.GetProperty(name)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool NumericEqual(JsonElement left, JsonElement right)
    {
        if (TryGetDecimalNumber(left, out var leftNumber) && TryGetDecimalNumber(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return TryGetBigInteger(left, out var leftInteger)
            && TryGetBigInteger(right, out var rightInteger)
            && leftInteger == rightInteger;
    }

    private static bool TryGetDecimalNumber(JsonElement value, out decimal number)
    {
        if (value.ValueKind == JsonValueKind.True)
        {
            number = 1;
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            number = 0;
            return true;
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            number = 0;
            return false;
        }

        return decimal.TryParse(
            value.GetRawText(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool IsNumberLike(JsonElement value)
        => value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    private static bool JsonEqualsString(JsonField field, string expected)
        => field.Found
            && field.Value.ValueKind == JsonValueKind.String
            && string.Equals(field.Value.GetString(), expected, StringComparison.Ordinal);

    private static bool JsonEqualsLong(JsonField field, long size)
    {
        if (!field.Found)
        {
            return false;
        }

        if (field.Value.ValueKind == JsonValueKind.True)
        {
            return size == 1;
        }

        if (field.Value.ValueKind == JsonValueKind.False)
        {
            return size == 0;
        }

        if (TryGetBigInteger(field.Value, out var integer))
        {
            return integer == new BigInteger(size);
        }

        return field.Value.ValueKind == JsonValueKind.Number
            && TryGetDecimalNumber(field.Value, out var number)
            && number == size;
    }

    private static bool IsInteger(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && IntegerPattern.IsMatch(value.GetRawText());

    private static bool TryGetBigInteger(JsonElement value, out BigInteger integer)
    {
        integer = default;
        if (!IsInteger(value))
        {
            return false;
        }

        return BigInteger.TryParse(
            value.GetRawText(),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out integer);
    }

    private static bool IsPositiveInteger(JsonField field)
        => field.Found && TryGetBigInteger(field.Value, out var value) && value > 0;

    private static bool IsPositiveIntegerWithin(JsonField field, long maximum)
        => field.Found
            && TryGetBigInteger(field.Value, out var value)
            && value > 0
            && value <= new BigInteger(maximum);

    private static bool IsSha256(JsonField field) => Sha256Pattern.IsMatch(PythonStr(field));

    private static bool IsMatch(JsonField field, Regex pattern)
        => field.Found
            && field.Value.ValueKind == JsonValueKind.String
            && pattern.IsMatch(field.Value.GetString() ?? string.Empty);

    private static bool IsValidStationName(JsonField field)
    {
        if (!field.Found || field.Value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var name = field.Value.GetString() ?? string.Empty;
        return name.Trim().Length > 0 && name.EnumerateRunes().Count() <= 512;
    }

    private static bool PackFileNameEquals(JsonField field, string? expectedName)
    {
        if (expectedName is null)
        {
            return !field.Found || field.Value.ValueKind == JsonValueKind.Null;
        }

        return JsonEqualsString(field, expectedName);
    }

    private static bool TryGetTrackIds(JsonField field, out string[] trackIds)
    {
        trackIds = [];
        if (!field.Found || field.Value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var count = field.Value.GetArrayLength();
        if (count <= 0 || count > MaximumRadioPackFiles)
        {
            return false;
        }

        var values = new string[count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in field.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = item.GetString() ?? string.Empty;
            if (!text.StartsWith("asset:", StringComparison.Ordinal) || !seen.Add(text))
            {
                return false;
            }

            values[index] = text;
            index++;
        }

        trackIds = values;
        return true;
    }

    private static string PythonStr(JsonField field)
    {
        if (!field.Found || field.Value.ValueKind == JsonValueKind.Null)
        {
            return "None";
        }

        var value = field.Value;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Number when TryGetBigInteger(value, out var integer) =>
                integer.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.Number => value.GetRawText(),
            _ => StrictJsonFile.Format(value),
        };
    }

    private static string RequiredString(JsonElement document, string field)
    {
        var value = Get(document, field);
        if (value.Found && value.Value.ValueKind == JsonValueKind.String && value.Value.GetString() is { } text)
        {
            return text;
        }

        throw new IOException("approved radio-pack evidence disappeared during assembly");
    }

    private static JsonField Get(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
        {
            return new JsonField(true, value);
        }

        return default;
    }

    private static HashSet<string> PropertyNames(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names;
    }

    private static HashSet<string> RelativeFiles(string root)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            return files;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
        }

        return files;
    }

    private static string CaseFold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value == 0x00DF)
            {
                builder.Append("ss");
                continue;
            }

            builder.Append(rune.ToString().ToLowerInvariant());
        }

        return builder.ToString();
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (!IsLineBreak(character))
            {
                continue;
            }

            lines.Add(text[start..index]);
            if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }

            start = index + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    private static bool IsLineBreak(char character)
        => character is '\n' or '\r' or '\v' or '\f' or '\u001c' or '\u001d' or '\u001e' or '\u0085' or '\u2028' or '\u2029';

    private static bool IsRegularFile(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool OutputExists(string path)
    {
        try
        {
            return Directory.Exists(path) || File.Exists(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static void DeleteOutput(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Match shutil.rmtree(ignore_errors=True): a failed cleanup still returns the assembly error.
        }
    }

    private static string Sha256File(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static Assembly Complete(PreviewEvidence evidence)
        => new(evidence.Passed, evidence.Errors.ToArray(), Render(evidence));

    private static string Render(PreviewEvidence evidence)
    {
        var root = new EvidenceObject();
        root.Add("schemaVersion", EvidenceValue.Number(1));
        root.Add("kind", EvidenceValue.Text("unsigned-native-alpha-preview-v1"));
        root.Add("passed", EvidenceValue.Boolean(evidence.Passed));
        root.Add("product", EvidenceValue.Text("Vibe Snake"));
        root.Add("productVersion", EvidenceValue.Text(evidence.ProductVersion));
        root.Add("tagName", EvidenceValue.Text(evidence.TagName));
        root.Add("sourceRevision", EvidenceValue.Text(evidence.SourceRevision));
        root.Add("buildMode", EvidenceValue.Text("Release"));
        root.Add("channel", EvidenceValue.Text("github-prerelease"));
        root.Add("unsigned", EvidenceValue.Boolean(true));
        root.Add("stablePublicationEligible", EvidenceValue.Boolean(false));
        root.Add("optionalPackOutputSeparate", EvidenceValue.Boolean(true));
        root.Add("baseGameIncludesOptionalPacks", EvidenceValue.Boolean(false));
        root.Add(
            "releaseMatrixSha256",
            evidence.ReleaseMatrixSha256 is null
                ? EvidenceValue.Null()
                : EvidenceValue.Text(evidence.ReleaseMatrixSha256));
        var packages = new EvidenceArray();
        foreach (var package in evidence.Packages)
        {
            var row = new EvidenceObject();
            row.Add("platform", EvidenceValue.Text(package.Platform));
            row.Add("fileName", EvidenceValue.Text(package.FileName));
            row.Add("bytes", EvidenceValue.Number(package.Bytes));
            row.Add("sha256", EvidenceValue.Text(package.Sha256));
            row.Add("sourceQualificationFileName", EvidenceValue.Text(package.SourceQualificationFileName));
            row.Add("artifactManifestSha256", EvidenceValue.Text(package.ArtifactManifestSha256));
            row.Add("outputPlanSha256", EvidenceValue.Text(package.OutputPlanSha256));
            row.Add("provenanceFileName", EvidenceValue.Text(package.ProvenanceFileName));
            row.Add("provenanceSha256", EvidenceValue.Text(package.ProvenanceSha256));
            packages.Add(row);
        }

        root.Add("packages", packages);
        if (evidence.RadioPack is null)
        {
            root.Add("radioPack", EvidenceValue.Null());
        }
        else if (evidence.RadioPack is { } radio)
        {
            var row = new EvidenceObject();
            row.Add("packId", EvidenceValue.Text(radio.PackId));
            row.Add("packVersion", EvidenceValue.Text(radio.PackVersion));
            row.Add("stationId", EvidenceValue.Text(radio.StationId));
            row.Add("stationName", EvidenceValue.Text(radio.StationName));
            row.Add("trackCount", EvidenceValue.Number(radio.TrackCount));
            row.Add("fileName", EvidenceValue.Text(radio.FileName));
            row.Add("bytes", EvidenceValue.Number(radio.Bytes));
            row.Add("sha256", EvidenceValue.Text(radio.Sha256));
            row.Add("manifestFileName", EvidenceValue.Text(radio.ManifestFileName));
            row.Add("manifestSha256", EvidenceValue.Text(radio.ManifestSha256));
            row.Add("assemblyEvidenceFileName", EvidenceValue.Text(radio.AssemblyEvidenceFileName));
            row.Add("assemblyEvidenceSha256", EvidenceValue.Text(radio.AssemblyEvidenceSha256));
            root.Add("radioPack", row);
        }

        var limitations = new EvidenceArray();
        foreach (var limitation in KnownLimitations)
        {
            limitations.Add(EvidenceValue.Text(limitation));
        }

        root.Add("knownLimitations", limitations);
        var errorValues = new EvidenceArray();
        foreach (var error in evidence.Errors)
        {
            errorValues.Add(EvidenceValue.Text(error));
        }

        root.Add("errors", errorValues);
        var builder = new StringBuilder();
        root.Write(builder, 0);
        builder.Append('\n');
        return builder.ToString();
    }

    private static void AppendIndent(StringBuilder builder, int indent)
        => builder.Append(new string(' ', indent * 2));

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var rune in value.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (rune.Value < 0x20 || rune.Value > 0x7E)
                    {
                        AppendUnicodeEscape(builder, rune.Value);
                    }
                    else
                    {
                        builder.Append((char)rune.Value);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static void AppendUnicodeEscape(StringBuilder builder, int code)
    {
        if (code <= 0xFFFF)
        {
            builder.Append("\\u").Append(code.ToString("x4", CultureInfo.InvariantCulture));
            return;
        }

        var subtracted = code - 0x10000;
        var high = 0xD800 + (subtracted >> 10);
        var low = 0xDC00 + (subtracted & 0x3FF);
        builder.Append("\\u").Append(high.ToString("x4", CultureInfo.InvariantCulture));
        builder.Append("\\u").Append(low.ToString("x4", CultureInfo.InvariantCulture));
    }

    private readonly record struct JsonField(bool Found, JsonElement Value);

    private readonly record struct PreparedPackage(
        string Platform,
        string SourcePackage,
        string SourcePackageName,
        string PackageSha256,
        long PackageBytes,
        string Provenance,
        string ArtifactManifestSha256,
        string OutputPlanSha256);

    private readonly record struct ApprovedRadio(
        string SourcePack,
        string SourceManifest,
        string SourceAssembly,
        JsonElement Assembly);

    private readonly record struct PackageRow(
        string Platform,
        string FileName,
        long Bytes,
        string Sha256,
        string SourceQualificationFileName,
        string ArtifactManifestSha256,
        string OutputPlanSha256,
        string ProvenanceFileName,
        string ProvenanceSha256);

    private readonly record struct RadioRow(
        string PackId,
        string PackVersion,
        string StationId,
        string StationName,
        long TrackCount,
        string FileName,
        long Bytes,
        string Sha256,
        string ManifestFileName,
        string ManifestSha256,
        string AssemblyEvidenceFileName,
        string AssemblyEvidenceSha256);

    private readonly record struct ZipMember(
        string Name,
        ushort Method,
        ushort Flags,
        int CreateSystem,
        uint ExternalAttributes,
        long CompressedSize,
        long UncompressedSize,
        long LocalHeaderOffset,
        uint Crc32);

    private sealed class PreviewEvidence
    {
        public bool Passed { get; set; }

        public string ProductVersion { get; init; } = string.Empty;

        public string TagName { get; init; } = string.Empty;

        public string SourceRevision { get; init; } = string.Empty;

        public string? ReleaseMatrixSha256 { get; init; }

        public List<string> Errors { get; set; } = [];

        public List<PackageRow> Packages { get; set; } = [];

        public RadioRow? RadioPack { get; set; }
    }

    private abstract class EvidenceValue
    {
        public abstract void Write(StringBuilder builder, int indent);

        public static EvidenceText Text(string value) => new(value);

        public static EvidenceBool Boolean(bool value) => new(value);

        public static EvidenceNumber Number(long value) => new(value);

        public static EvidenceNull Null() => EvidenceNull.Instance;
    }

    private sealed class EvidenceObject : EvidenceValue
    {
        private readonly Dictionary<string, EvidenceValue> _values = new(StringComparer.Ordinal);

        public void Add(string name, EvidenceValue value) => _values.Add(name, value);

        public override void Write(StringBuilder builder, int indent)
        {
            if (_values.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            var keys = new string[_values.Count];
            _values.Keys.CopyTo(keys, 0);
            Array.Sort(keys, StringComparer.Ordinal);
            builder.Append("{\n");
            for (var index = 0; index < keys.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(",\n");
                }

                AppendIndent(builder, indent + 1);
                AppendJsonString(builder, keys[index]);
                builder.Append(": ");
                _values[keys[index]].Write(builder, indent + 1);
            }

            builder.Append('\n');
            AppendIndent(builder, indent);
            builder.Append('}');
        }
    }

    private sealed class EvidenceArray : EvidenceValue
    {
        private readonly List<EvidenceValue> _items = [];

        public void Add(EvidenceValue value) => _items.Add(value);

        public override void Write(StringBuilder builder, int indent)
        {
            if (_items.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append("[\n");
            for (var index = 0; index < _items.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(",\n");
                }

                AppendIndent(builder, indent + 1);
                _items[index].Write(builder, indent + 1);
            }

            builder.Append('\n');
            AppendIndent(builder, indent);
            builder.Append(']');
        }
    }

    private sealed class EvidenceText(string value) : EvidenceValue
    {
        public override void Write(StringBuilder builder, int indent) => AppendJsonString(builder, value);
    }

    private sealed class EvidenceBool(bool value) : EvidenceValue
    {
        public override void Write(StringBuilder builder, int indent)
            => builder.Append(value ? "true" : "false");
    }

    private sealed class EvidenceNumber(long value) : EvidenceValue
    {
        public override void Write(StringBuilder builder, int indent)
            => builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class EvidenceNull : EvidenceValue
    {
        public static readonly EvidenceNull Instance = new();

        public override void Write(StringBuilder builder, int indent) => builder.Append("null");
    }
}
