using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using VibeSnake.Persistence;

namespace RepositoryChecks;

internal sealed record RadioPackAssemblyEvidence(
    string PackFileName,
    int TrackCount,
    string PackSha256);

internal static class RadioPackAssemblyCheck
{
    internal const long DefaultMaximumCurationBytes = 1_048_576;
    private const ushort ZipVersionNeeded = 20;
    private const ushort ZipDosDate1980 = 0x0021;
    private const uint ZipLocalSignature = 0x04034b50;
    private const uint ZipCentralSignature = 0x02014b50;
    private const uint ZipEndSignature = 0x06054b50;
    private const uint UnixRegularFileAttributes = (0x8000u | 0x1A4u) << 16;
    private const string PackExtension = ".vibesnake-pack.zip";
    private const string ManifestName = "pack.json";
    private const string AssemblyName = "radio_pack_assembly.json";
    private const string ChecksumName = "SHA256SUMS.txt";
    private const string CurationStatus = "approved-for-alpha-release";
    private const string CurationPlanId = "vibesnake-content-curation-v1";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly uint[] CrcTable = CreateCrcTable();
    private static readonly Regex StationIdPattern = new(
        @"\A[a-z0-9]+(?:_[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly HashSet<string> CurationFields = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "planId",
        "inventoryPolicySha256",
        "decisionStatus",
        "coreMusic",
        "stations",
    };
    private static readonly HashSet<string> DecisionFields = new(StringComparer.Ordinal)
    {
        "pendingAssetIds",
        "approvedAssetIds",
        "rejectedAssetIds",
    };
    private static readonly HashSet<string> StationFields = new(DecisionFields, StringComparer.Ordinal)
    {
        "id",
    };
    private static readonly string[] DecisionFieldOrder =
    [
        "approvedAssetIds",
        "pendingAssetIds",
        "rejectedAssetIds",
    ];
    private static readonly JsonWriterOptions EvidenceOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
    };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32,
    };

    internal static RadioPackAssemblyEvidence Assemble(
        string repositoryRoot,
        string manifestPath,
        string curationPath,
        string inventoryPath,
        string outputRoot,
        long maximumInstalledBytes = ContentPackBudgets.RadioStationInstalledBytesMaximum,
        long maximumCompressedBytes = ContentPackBudgets.RadioStationCompressedBytesMaximum,
        long maximumCurationBytes = DefaultMaximumCurationBytes,
        bool failAfterOutputCreation = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(curationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inventoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumInstalledBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCurationBytes);

        var root = Path.GetFullPath(repositoryRoot);
        var manifestFull = Path.GetFullPath(manifestPath);
        var curationFull = Path.GetFullPath(curationPath);
        var inventoryFull = Path.GetFullPath(inventoryPath);
        var outputFull = Path.GetFullPath(outputRoot);
        if (File.Exists(outputFull) || Directory.Exists(outputFull))
        {
            throw new InvalidDataException(
                "radio pack output must not already exist: " + outputFull);
        }

        var manifestInfo = new FileInfo(manifestFull);
        if (!manifestInfo.Exists)
        {
            throw new FileNotFoundException("Content pack does not exist.", manifestFull);
        }

        if (manifestInfo.Length > ContentPackManifest.MaximumManifestBytes)
        {
            throw new InvalidDataException(
                "Content pack exceeds the "
                + ContentPackManifest.MaximumManifestBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "-byte limit.");
        }

        var inventory = FreshContentInventory.Load(root, inventoryFull);
        var manifest = ContentPackManifest.CheckCanonicalFile(manifestFull, inventory);
        if (manifest.Kind != ContentPackKind.Radio || manifest.Radio is null)
        {
            throw new InvalidDataException(
                "release radio-pack assembly requires one radio manifest");
        }

        var curation = LoadCuration(curationFull, maximumCurationBytes);
        if (!string.Equals(curation.PolicySha256, inventory.PolicySha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "content curation policy hash does not match the inventory");
        }

        var approvedTracks = ApprovedStation(curation, manifest.Radio.StationId, inventory);
        var trackIds = manifest.Radio.TrackIds;
        if (!trackIds.ToHashSet(StringComparer.Ordinal).SetEquals(approvedTracks))
        {
            throw new InvalidDataException(
                "radio manifest trackIds must equal the station's approved listening decisions");
        }

        var radioFileIds = manifest.Files
            .Where(file => file.Role == "radio-track" && file.MediaType == "audio/mpeg")
            .Select(file => file.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (!trackIds.ToHashSet(StringComparer.Ordinal).SetEquals(radioFileIds))
        {
            throw new InvalidDataException(
                "radio manifest trackIds must list every packaged radio-track file");
        }

        long installedBytes = 0;
        foreach (var file in manifest.Files)
        {
            installedBytes += file.Bytes;
        }

        if (installedBytes > maximumInstalledBytes)
        {
            throw new InvalidDataException("radio pack exceeds the installed-size budget");
        }

        var manifestBytes = File.ReadAllBytes(manifestFull);
        var archiveBytes = RenderArchive(root, manifest, manifestBytes);
        if (archiveBytes.LongLength > maximumCompressedBytes)
        {
            throw new InvalidDataException("radio pack exceeds the compressed-size budget");
        }

        var fileName = manifest.Id + "-" + manifest.Version + PackExtension;
        var evidenceJson = RenderEvidence(
            manifest,
            curation.DecisionStatus,
            inventoryFull,
            curationFull,
            manifestFull,
            fileName,
            archiveBytes,
            trackIds);
        var created = false;
        try
        {
            Directory.CreateDirectory(outputFull);
            created = true;
            if (failAfterOutputCreation)
            {
                throw new IOException("forced output failure");
            }

            File.WriteAllBytes(Path.Combine(outputFull, fileName), archiveBytes);
            File.WriteAllBytes(Path.Combine(outputFull, ManifestName), manifestBytes);
            var evidencePath = Path.Combine(outputFull, AssemblyName);
            File.WriteAllText(evidencePath, evidenceJson, Utf8);
            var checksumRows = new List<(string Digest, string Name)>
            {
                (Sha256File(Path.Combine(outputFull, fileName)), fileName),
                (Sha256File(Path.Combine(outputFull, ManifestName)), ManifestName),
                (Sha256File(evidencePath), AssemblyName),
            };
            checksumRows.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            var checksumBuilder = new StringBuilder();
            foreach (var (digest, name) in checksumRows)
            {
                checksumBuilder.Append(digest);
                checksumBuilder.Append("  ");
                checksumBuilder.Append(name);
                checksumBuilder.Append('\n');
            }

            File.WriteAllText(Path.Combine(outputFull, ChecksumName), checksumBuilder.ToString(), Utf8);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            if (created)
            {
                TryDelete(outputFull);
            }

            throw new InvalidDataException(
                "could not assemble radio pack: " + exception.Message,
                exception);
        }

        return new RadioPackAssemblyEvidence(
            fileName,
            trackIds.Count,
            Sha256(archiveBytes));
    }

    internal static byte[] RenderStoredArchive(IReadOnlyList<(string Name, byte[] Data)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > ushort.MaxValue)
        {
            throw new InvalidDataException("radio pack contains too many archive entries");
        }

        using var output = new MemoryStream();
        var locals = new List<StoredEntry>(entries.Count);
        foreach (var (name, data) in entries)
        {
            var nameBytes = EncodeEntryName(name);
            var crc = Crc32(data);
            var offset = output.Position;
            WriteLocal(output, nameBytes, data, crc);
            locals.Add(new StoredEntry(nameBytes, data.Length, crc, offset));
        }

        var centralOffset = output.Position;
        foreach (var entry in locals)
        {
            WriteCentral(output, entry);
        }

        var centralSize = output.Position - centralOffset;
        WriteEnd(output, locals.Count, centralSize, centralOffset);
        return output.ToArray();
    }

    internal static string ResolvePackAsset(
        string repositoryRoot,
        string assetRootRelative,
        string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRootRelative);
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Contains('\\', StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            || relativePath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException(
                "pack asset is missing or escapes the asset root: " + relativePath);
        }

        var repository = Path.GetFullPath(repositoryRoot);
        var assetRoot = Path.GetFullPath(
            Path.Combine(repository, assetRootRelative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideOrEqual(repository, assetRoot) || !Directory.Exists(assetRoot))
        {
            throw new InvalidDataException(
                "pack asset is missing or escapes the asset root: " + relativePath);
        }

        var candidate = Path.GetFullPath(
            Path.Combine(assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsStrictlyInside(assetRoot, candidate) || !Path.Exists(candidate))
        {
            throw new InvalidDataException(
                "pack asset is missing or escapes the asset root: " + relativePath);
        }

        var current = candidate;
        while (!PathsEqual(current, assetRoot))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "pack asset path cannot contain a symbolic link: " + relativePath);
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || PathsEqual(parent, current))
            {
                throw new InvalidDataException(
                    "pack asset is missing or escapes the asset root: " + relativePath);
            }

            current = parent;
        }

        if ((File.GetAttributes(candidate) & FileAttributes.Directory) != 0)
        {
            throw new InvalidDataException(
                "pack asset is not a regular file: " + relativePath);
        }

        return candidate;
    }

    internal static byte[] ReadMatchingPayload(
        string assetPath,
        long expectedBytes,
        string expectedSha256,
        string relativePath)
    {
        var bytes = File.ReadAllBytes(assetPath);
        var digest = Sha256(bytes);
        if (bytes.LongLength != expectedBytes
            || !string.Equals(digest, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "pack asset changed after inventory validation: " + relativePath);
        }

        return bytes;
    }

    private static byte[] RenderArchive(
        string repositoryRoot,
        ContentPackManifest manifest,
        byte[] manifestBytes)
    {
        var entries = new List<(string Name, byte[] Data)>
        {
            (ManifestName, manifestBytes),
        };
        foreach (var file in manifest.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            var assetPath = ResolvePackAsset(repositoryRoot, manifest.Inventory.AssetRoot, file.Path);
            var payload = ReadMatchingPayload(assetPath, file.Bytes, file.Sha256, file.Path);
            entries.Add((file.Path, payload));
        }

        return RenderStoredArchive(entries);
    }

    private static string RenderEvidence(
        ContentPackManifest manifest,
        string decisionStatus,
        string inventoryPath,
        string curationPath,
        string manifestPath,
        string fileName,
        byte[] archiveBytes,
        IReadOnlyList<string> trackIds)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, EvidenceOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("curationDecisionStatus", decisionStatus);
            writer.WriteString("curationSha256", Sha256File(curationPath));
            writer.WriteString("inventorySha256", Sha256File(inventoryPath));
            writer.WriteString("kind", "approved-radio-pack-assembly-v1");
            writer.WriteString("manifestSha256", Sha256File(manifestPath));
            writer.WriteNumber("packBytes", archiveBytes.LongLength);
            writer.WriteString("packFileName", fileName);
            writer.WriteString("packId", manifest.Id);
            writer.WriteString("packSha256", Sha256(archiveBytes));
            writer.WriteString("packVersion", manifest.Version);
            writer.WriteBoolean("passed", true);
            writer.WriteBoolean("releaseApproved", true);
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("stationId", manifest.Radio!.StationId);
            writer.WriteString("stationName", manifest.Radio.StationName);
            writer.WriteNumber("trackCount", trackIds.Count);
            writer.WritePropertyName("trackIds");
            writer.WriteStartArray();
            foreach (var trackId in trackIds)
            {
                writer.WriteStringValue(trackId);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static IReadOnlyList<string> ApprovedStation(
        CurationDocument curation,
        string stationId,
        ContentInventory inventory)
    {
        var inventoryIds = inventory.Assets.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var radioIds = inventory.Assets
            .Where(asset => asset.MediaType == "audio/mpeg"
                && asset.RelativePath.StartsWith("audio/radio/", StringComparison.Ordinal))
            .Select(asset => asset.Id)
            .ToHashSet(StringComparer.Ordinal);
        var coreIds = IdsOf(curation.CoreMusic);
        if (!coreIds.IsSubsetOf(inventoryIds) || coreIds.Overlaps(radioIds))
        {
            throw new InvalidDataException(
                "content curation coreMusic contains unknown or radio asset IDs");
        }

        var accounted = new HashSet<string>(StringComparer.Ordinal);
        StationDecisions? selected = null;
        var selectedCount = 0;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < curation.Stations.Count; index++)
        {
            var station = curation.Stations[index];
            if (!StationIdPattern.IsMatch(station.Id) || station.Id.Length > 128)
            {
                throw new InvalidDataException(
                    "content curation station " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".id is invalid");
            }

            if (!seenIds.Add(station.Id))
            {
                throw new InvalidDataException("content curation repeats station ID: " + station.Id);
            }

            var decisions = IdsOf(station.Decisions);
            if (!decisions.IsSubsetOf(radioIds))
            {
                throw new InvalidDataException(
                    "content curation station "
                    + station.Id
                    + " contains unknown or non-radio asset IDs");
            }

            if (accounted.Overlaps(decisions))
            {
                throw new InvalidDataException(
                    "content curation assigns a radio asset to multiple stations");
            }

            accounted.UnionWith(decisions);
            if (station.Id == stationId)
            {
                selected = station;
                selectedCount++;
            }
        }

        if (!accounted.SetEquals(radioIds))
        {
            throw new InvalidDataException(
                "content curation must account for every inventoried radio asset exactly once");
        }

        if (selectedCount != 1 || selected is null)
        {
            throw new InvalidDataException(
                "content curation must contain exactly one station " + stationId);
        }

        if (selected.Decisions.Pending.Count > 0)
        {
            throw new InvalidDataException(
                "station " + stationId + " still has pending listening decisions");
        }

        if (selected.Decisions.Approved.Count == 0)
        {
            throw new InvalidDataException(
                "station " + stationId + " has no approved radio tracks");
        }

        return selected.Decisions.Approved;
    }

    private static CurationDocument LoadCuration(string path, long maximumBytes)
    {
        var info = new FileInfo(path);
        byte[] bytes;
        try
        {
            if (info.Length > maximumBytes)
            {
                throw new InvalidDataException(
                    "content curation exceeds the "
                    + maximumBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "-byte limit");
            }

            bytes = File.ReadAllBytes(path);
            _ = Utf8.GetString(bytes);
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
                "content curation is unreadable: " + path + ": " + exception.Message,
                exception);
        }

        JsonDocument document;
        try
        {
            RejectDuplicateFields(bytes);
            document = JsonDocument.Parse(bytes, DocumentOptions);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "content curation is unreadable: " + path + ": " + exception.Message,
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !FieldNames(root).SetEquals(CurationFields))
            {
                throw new InvalidDataException("content curation must use the exact schema 1 fields");
            }

            var schema = root.GetProperty("schemaVersion");
            var planId = root.GetProperty("planId");
            if (schema.ValueKind != JsonValueKind.Number
                || !schema.TryGetInt32(out var schemaVersion)
                || schemaVersion != 1
                || planId.ValueKind != JsonValueKind.String
                || planId.GetString() != CurationPlanId)
            {
                throw new InvalidDataException("content curation identity is unsupported");
            }

            var decisionStatus = root.GetProperty("decisionStatus");
            if (decisionStatus.ValueKind != JsonValueKind.String
                || decisionStatus.GetString() != CurationStatus)
            {
                throw new InvalidDataException("content curation must be " + CurationStatus);
            }

            var policyHash = root.GetProperty("inventoryPolicySha256");
            if (policyHash.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(policyHash.GetString()))
            {
                throw new InvalidDataException("content curation must use the exact schema 1 fields");
            }

            var stationsElement = root.GetProperty("stations");
            if (stationsElement.ValueKind != JsonValueKind.Array || stationsElement.GetArrayLength() == 0)
            {
                throw new InvalidDataException("content curation stations must be a nonempty array");
            }

            var core = ReadDecisions(root.GetProperty("coreMusic"), "content curation coreMusic", station: false);
            var stations = new List<StationDecisions>(stationsElement.GetArrayLength());
            var index = 0;
            foreach (var station in stationsElement.EnumerateArray())
            {
                var label = "content curation station " + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (station.ValueKind != JsonValueKind.Object || !FieldNames(station).SetEquals(StationFields))
                {
                    throw new InvalidDataException(label + " must use the exact decision fields");
                }

                var idElement = station.GetProperty("id");
                if (idElement.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(idElement.GetString()))
                {
                    throw new InvalidDataException(label + ".id must be a nonempty station ID");
                }

                var decisions = ReadDecisions(station, label, station: true);
                var name = idElement.GetString() ?? string.Empty;
                stations.Add(new StationDecisions(name, decisions));
                index++;
            }

            return new CurationDocument(
                decisionStatus.GetString() ?? string.Empty,
                policyHash.GetString() ?? string.Empty,
                core,
                stations);
        }
    }

    private static DecisionSet ReadDecisions(JsonElement element, string label, bool station)
    {
        var expected = station ? StationFields : DecisionFields;
        if (element.ValueKind != JsonValueKind.Object || !FieldNames(element).SetEquals(expected))
        {
            throw new InvalidDataException(label + " must use the exact decision fields");
        }

        var sets = new List<HashSet<string>>(DecisionFieldOrder.Length);
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var field in DecisionFieldOrder)
        {
            var items = element.GetProperty(field);
            if (items.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    label + "." + field + " must contain unique nonempty asset IDs");
            }

            var values = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString()))
                {
                    throw new InvalidDataException(
                        label + "." + field + " must contain unique nonempty asset IDs");
                }

                var value = item.GetString() ?? string.Empty;
                if (!unique.Add(value))
                {
                    throw new InvalidDataException(
                        label + "." + field + " must contain unique nonempty asset IDs");
                }

                values.Add(value);
            }

            lists.Add(field, values);
            sets.Add(unique);
        }

        for (var left = 0; left < sets.Count; left++)
        {
            for (var right = left + 1; right < sets.Count; right++)
            {
                if (sets[left].Overlaps(sets[right]))
                {
                    throw new InvalidDataException(label + " decisions must be disjoint");
                }
            }
        }

        return new DecisionSet(
            lists["pendingAssetIds"],
            lists["approvedAssetIds"],
            lists["rejectedAssetIds"]);
    }

    private static void RejectDuplicateFields(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(
            json,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
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
                    var fieldName = reader.GetString() ?? string.Empty;
                    if (objects.Count == 0 || !objects.Peek().Add(fieldName))
                    {
                        throw new InvalidDataException(
                            "content curation repeats JSON field: " + fieldName);
                    }

                    break;
            }
        }
    }

    private static HashSet<string> IdsOf(DecisionSet decisions)
    {
        var ids = new HashSet<string>(decisions.Pending, StringComparer.Ordinal);
        ids.UnionWith(decisions.Approved);
        ids.UnionWith(decisions.Rejected);
        return ids;
    }

    private static HashSet<string> FieldNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

    private static byte[] EncodeEntryName(string name)
    {
        if (string.IsNullOrEmpty(name)
            || name.Contains('\\', StringComparison.Ordinal)
            || name.StartsWith('/'))
        {
            throw new InvalidDataException("pack archive name is not a relative archive path: " + name);
        }

        foreach (var character in name)
        {
            if (character > 127)
            {
                throw new InvalidDataException("pack archive name must be ASCII: " + name);
            }
        }

        var bytes = Encoding.ASCII.GetBytes(name);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new InvalidDataException("pack archive name is too long: " + name);
        }

        return bytes;
    }

    private static void WriteLocal(Stream stream, byte[] name, byte[] data, uint crc)
    {
        WriteU32(stream, ZipLocalSignature);
        WriteU16(stream, ZipVersionNeeded);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU16(stream, ZipDosDate1980);
        WriteU32(stream, crc);
        WriteU32(stream, (uint)data.Length);
        WriteU32(stream, (uint)data.Length);
        WriteU16(stream, (ushort)name.Length);
        WriteU16(stream, 0);
        stream.Write(name);
        stream.Write(data);
    }

    private static void WriteCentral(Stream stream, StoredEntry entry)
    {
        WriteU32(stream, ZipCentralSignature);
        // Version-made-by is one little-endian uint16: low byte 20, high byte Unix (3).
        stream.WriteByte((byte)ZipVersionNeeded);
        stream.WriteByte(3);
        WriteU16(stream, ZipVersionNeeded);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU16(stream, ZipDosDate1980);
        WriteU32(stream, entry.Crc);
        WriteU32(stream, (uint)entry.Size);
        WriteU32(stream, (uint)entry.Size);
        WriteU16(stream, (ushort)entry.Name.Length);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU32(stream, UnixRegularFileAttributes);
        WriteU32(stream, (uint)entry.Offset);
        stream.Write(entry.Name);
    }

    private static void WriteEnd(Stream stream, int count, long centralSize, long centralOffset)
    {
        WriteU32(stream, ZipEndSignature);
        WriteU16(stream, 0);
        WriteU16(stream, 0);
        WriteU16(stream, (ushort)count);
        WriteU16(stream, (ushort)count);
        WriteU32(stream, (uint)centralSize);
        WriteU32(stream, (uint)centralOffset);
        WriteU16(stream, 0);
    }

    private static void WriteU16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var crc = index;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }

            table[index] = crc;
        }

        return table;
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    private static string Sha256File(string path) =>
        Sha256(File.ReadAllBytes(path));

    private static bool IsInsideOrEqual(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !Escapes(relative);
    }

    private static bool IsStrictlyInside(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative != "." && !Escapes(relative);
    }

    private static bool Escapes(string relative) =>
        relative == ".."
        || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
        || Path.IsPathRooted(relative);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct StoredEntry(byte[] Name, int Size, uint Crc, long Offset);

    private sealed record DecisionSet(
        IReadOnlyList<string> Pending,
        IReadOnlyList<string> Approved,
        IReadOnlyList<string> Rejected);

    private sealed record StationDecisions(string Id, DecisionSet Decisions);

    private sealed record CurationDocument(
        string DecisionStatus,
        string PolicySha256,
        DecisionSet CoreMusic,
        IReadOnlyList<StationDecisions> Stations);
}
