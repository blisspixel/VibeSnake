using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

internal static class ProductReviewPreparationCheck
{
    internal const int DefaultMaximumMatrixBytes = 4 * 1024 * 1024;

    private const int MaximumDepth = 64;
    private const string ContractRelativePath = "config/qa_manual_product_matrix_v2.json";
    private const string MatrixDirectoryName = "vibesnake-release-matrix";
    private const string MatrixFileName = "release_matrix.json";
    private const string CandidateName = "candidate.json";
    private const string ManifestName = "workspace-manifest.json";
    private const string ReviewGuideName = "REVIEW.md";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions RenderOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
    };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumDepth,
    };
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumDepth,
    };
    private static readonly Regex RevisionPattern = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RunIdPattern = new(
        "^[1-9][0-9]{0,17}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RepositoryPattern = new(
        "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SegmentPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PositiveIntegerPattern = new(
        "^[1-9][0-9]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal readonly record struct Preparation(string OutputDirectory, string Revision, int FileCount);

    internal delegate ReleaseMatrixCheck.Qualification MatrixQualifier(
        string evidenceRoot,
        string expectedRevision,
        string buildMode);

    internal static Preparation Prepare(
        string repositoryRoot,
        string releaseEvidenceRoot,
        string expectedRevision,
        string releaseRunId,
        string repository,
        string outputRoot) =>
        Prepare(
            repositoryRoot,
            releaseEvidenceRoot,
            expectedRevision,
            releaseRunId,
            repository,
            outputRoot,
            qualifier: null,
            DefaultMaximumMatrixBytes);

    internal static Preparation Prepare(
        string repositoryRoot,
        string releaseEvidenceRoot,
        string expectedRevision,
        string releaseRunId,
        string repository,
        string outputRoot,
        MatrixQualifier? qualifier,
        int maximumMatrixBytes)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(releaseEvidenceRoot);
        ArgumentNullException.ThrowIfNull(expectedRevision);
        ArgumentNullException.ThrowIfNull(releaseRunId);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(outputRoot);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMatrixBytes, 1);

        if (!RevisionPattern.IsMatch(expectedRevision))
        {
            throw new ProductReviewPreparationException(
                "expected revision must be a lowercase 40-character Git revision");
        }

        if (!RunIdPattern.IsMatch(releaseRunId))
        {
            throw new ProductReviewPreparationException("release run ID must be a positive integer");
        }

        if (!RepositoryPattern.IsMatch(repository))
        {
            throw new ProductReviewPreparationException("repository must use owner/name syntax");
        }

        var repositoryFull = ResolveFullPath(repositoryRoot, "repository root");
        var evidenceFull = ResolveFullPath(releaseEvidenceRoot, "release evidence root");
        var outputFull = ResolveFullPath(outputRoot, "review output");
        if (IsInside(repositoryFull, outputFull) && !IsInside(Path.Combine(repositoryFull, "TestResults"), outputFull))
        {
            throw new ProductReviewPreparationException(
                "review output inside the repository must be under ignored TestResults");
        }

        if (ExistsAndReparse(outputFull))
        {
            throw new ProductReviewPreparationException("review output must be a regular directory path");
        }

        var contract = ReadContract(Path.Combine(repositoryFull, ContractRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var qualification = Qualify(evidenceFull, expectedRevision, qualifier);
        if (!qualification.Passed || qualification.Errors.Count > 0)
        {
            var detail = qualification.Errors.Count > 0
                ? string.Join("; ", qualification.Errors)
                : "qualification did not pass";
            throw new ProductReviewPreparationException("Release matrix validation failed: " + detail);
        }

        var matrixPath = Path.Combine(evidenceFull, MatrixDirectoryName, MatrixFileName);
        if (!IsRegularFile(matrixPath))
        {
            throw new ProductReviewPreparationException("missing retained Release matrix: " + matrixPath);
        }

        var retained = ReadRetainedMatrix(matrixPath, maximumMatrixBytes);
        var recomputed = ParseObject(qualification.Json, "Release matrix qualification JSON");
        if (!JsonNode.DeepEquals(retained.Node, recomputed))
        {
            throw new ProductReviewPreparationException(
                "retained Release matrix does not match independently recomputed evidence");
        }

        var candidate = BuildCandidate(
            recomputed,
            retained.Sha256,
            expectedRevision,
            releaseRunId,
            repository,
            contract);
        var finalDirectory = Path.Combine(outputFull, expectedRevision);
        if (File.Exists(finalDirectory) || Directory.Exists(finalDirectory))
        {
            throw new ProductReviewPreparationException("review workspace already exists: " + finalDirectory);
        }

        var stagingDirectory = Path.Combine(
            outputFull,
            "." + expectedRevision + ".staging." + Guid.NewGuid().ToString("N"));
        var committed = false;
        try
        {
            Directory.CreateDirectory(outputFull);
            Directory.CreateDirectory(stagingDirectory);
            WriteJson(Path.Combine(stagingDirectory, CandidateName), candidate);
            var templatesDirectory = Path.Combine(stagingDirectory, "templates");
            var sessionsDirectory = Path.Combine(stagingDirectory, "sessions");
            Directory.CreateDirectory(templatesDirectory);
            Directory.CreateDirectory(sessionsDirectory);
            foreach (var row in candidate["artifactRows"]!.AsArray())
            {
                var rowObject = row!.AsObject();
                var platformRowId = rowObject["platformRowId"]!.GetValue<string>();
                WriteJson(
                    Path.Combine(templatesDirectory, platformRowId + ".session.json.template"),
                    BuildSessionTemplate(candidate, rowObject, contract.Flows));
                Directory.CreateDirectory(Path.Combine(sessionsDirectory, "evidence", platformRowId));
            }

            WriteText(Path.Combine(stagingDirectory, ReviewGuideName), BuildReviewGuide(candidate));
            var manifest = BuildManifest(stagingDirectory, expectedRevision);
            WriteJson(Path.Combine(stagingDirectory, ManifestName), manifest.Node);
            Directory.Move(stagingDirectory, finalDirectory);
            committed = true;
            return new Preparation(finalDirectory, expectedRevision, manifest.FileCount);
        }
        catch (ProductReviewPreparationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProductReviewPreparationException(
                "could not prepare manual review workspace: " + StrictJsonFile.SingleLine(exception.Message));
        }
        finally
        {
            if (!committed && Directory.Exists(stagingDirectory))
            {
                try
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The preparation error remains the operator-facing failure.
                }
            }
        }
    }

    private static ReleaseMatrixCheck.Qualification Qualify(
        string evidenceRoot,
        string expectedRevision,
        MatrixQualifier? qualifier)
    {
        try
        {
            return qualifier is null
                ? ReleaseMatrixCheck.Qualify(evidenceRoot, expectedRevision, "Release")
                : qualifier(evidenceRoot, expectedRevision, "Release");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new ProductReviewPreparationException(
                "Release matrix validation failed: " + StrictJsonFile.SingleLine(exception.Message));
        }
    }

    private static Contract ReadContract(string path)
    {
        if (!IsRegularFile(path))
        {
            throw new ProductReviewPreparationException("missing manual product matrix contract: " + path);
        }

        using var document = ReadStrict(path, "manual product matrix contract", DefaultMaximumMatrixBytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("platformRows", out var rowsElement)
            || rowsElement.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("requiredFlows", out var flowsElement)
            || flowsElement.ValueKind != JsonValueKind.Array)
        {
            throw new ProductReviewPreparationException(
                "manual product matrix contract platform rows and required flows are invalid");
        }

        if (rowsElement.GetArrayLength() == 0)
        {
            throw new ProductReviewPreparationException("manual product matrix contract platform rows are empty");
        }

        if (flowsElement.GetArrayLength() == 0)
        {
            throw new ProductReviewPreparationException("manual product matrix contract required flows are empty");
        }

        var rows = new List<PlatformRow>(rowsElement.GetArrayLength());
        var rowIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rowsElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !TrySegment(row, "id", out var id)
                || !TrySegment(row, "artifactPlatform", out var artifactPlatform)
                || !TrySegment(row, "architecture", out var architecture))
            {
                throw new ProductReviewPreparationException("manual product matrix contract platform row is invalid");
            }

            if (!rowIds.Add(id))
            {
                throw new ProductReviewPreparationException(
                    "manual product matrix contract contains a duplicate platform row: " + id);
            }

            rows.Add(new PlatformRow(id, artifactPlatform, architecture));
        }

        var flows = new List<string>(flowsElement.GetArrayLength());
        var flowIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flow in flowsElement.EnumerateArray())
        {
            var flowId = flow.ValueKind == JsonValueKind.String ? flow.GetString() : null;
            if (flowId is null || !SegmentPattern.IsMatch(flowId))
            {
                throw new ProductReviewPreparationException("manual product matrix contract required flow is invalid");
            }

            if (!flowIds.Add(flowId))
            {
                throw new ProductReviewPreparationException(
                    "manual product matrix contract contains a duplicate required flow: " + flowId);
            }

            flows.Add(flowId);
        }

        return new Contract(rows, flows);
    }

    private static bool TrySegment(JsonElement row, string name, out string value)
    {
        value = string.Empty;
        if (!row.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return SegmentPattern.IsMatch(value);
    }

    private static RetainedMatrix ReadRetainedMatrix(string path, int maximumMatrixBytes)
    {
        var errors = new List<string>();
        JsonDocument? document = null;
        try
        {
            document = ReadStrict(path, "retained Release matrix", maximumMatrixBytes, errors);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add(
                "unreadable retained Release matrix: "
                + path
                + ": "
                + StrictJsonFile.SingleLine(exception.Message));
        }

        if (errors.Count > 0 || document is null)
        {
            document?.Dispose();
            throw new ProductReviewPreparationException(
                "retained Release matrix is unreadable: " + string.Join("; ", errors));
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ProductReviewPreparationException(
                    "retained Release matrix is unreadable: " + path + ": JSON root must be an object");
            }

            var node = JsonNode.Parse(document.RootElement.GetRawText())
                ?? throw new ProductReviewPreparationException(
                    "retained Release matrix is unreadable: " + path + ": JSON root must be an object");
            return new RetainedMatrix(node, Sha256(path));
        }
    }

    private static JsonObject ParseObject(string json, string label)
    {
        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(json);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ProductReviewPreparationException(
                label + " is unreadable: " + StrictJsonFile.SingleLine(exception.Message));
        }

        if (HasBom(bytes))
        {
            throw new ProductReviewPreparationException(label + " must not contain a UTF-8 BOM");
        }

        var nonFinite = FindNonFiniteToken(json);
        if (nonFinite is not null)
        {
            throw new ProductReviewPreparationException(label + " is unreadable: non-finite JSON number: " + nonFinite);
        }

        try
        {
            RejectDuplicateProperties(bytes);
            var node = JsonNode.Parse(bytes, documentOptions: DocumentOptions);
            if (node is not JsonObject obj)
            {
                throw new ProductReviewPreparationException(label + " must be an object");
            }

            return obj;
        }
        catch (ProductReviewPreparationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new ProductReviewPreparationException(
                label + " is unreadable: " + StrictJsonFile.SingleLine(exception.Message));
        }
    }

    private static JsonObject BuildCandidate(
        JsonObject matrix,
        string matrixSha256,
        string expectedRevision,
        string releaseRunId,
        string repository,
        Contract contract)
    {
        var passed = matrix["passed"] is JsonValue passedValue
            && passedValue.TryGetValue<bool>(out var passedFlag)
            && passedFlag;
        if (!passed || StringOf(matrix["kind"]) != "release-matrix-qualification-v1")
        {
            throw new ProductReviewPreparationException("Release matrix must be a passing qualification record");
        }

        if (StringOf(matrix["buildMode"]) != "Release")
        {
            throw new ProductReviewPreparationException("manual product review requires a Release matrix");
        }

        var revision = StringOf(matrix["sourceRevision"]);
        if (revision is null || !RevisionPattern.IsMatch(revision))
        {
            throw new ProductReviewPreparationException("Release matrix source revision is invalid");
        }

        if (!string.Equals(revision, expectedRevision, StringComparison.Ordinal))
        {
            throw new ProductReviewPreparationException(
                "Release matrix source revision does not match the expected revision");
        }

        var version = StringOf(matrix["productVersion"]);
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version.Any(char.IsControl))
        {
            throw new ProductReviewPreparationException("Release matrix product version is invalid");
        }

        if (matrix["platforms"] is not JsonArray platforms)
        {
            throw new ProductReviewPreparationException("Release matrix platforms must be an array");
        }

        var byPlatform = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in platforms)
        {
            if (item is not JsonObject row)
            {
                continue;
            }

            var platform = StringOf(row["platform"]);
            if (platform is null)
            {
                continue;
            }

            byPlatform[platform] = row;
        }

        var expectedPlatforms = contract.Rows.Select(row => row.ArtifactPlatform).ToHashSet(StringComparer.Ordinal);
        if (!expectedPlatforms.SetEquals(byPlatform.Keys))
        {
            throw new ProductReviewPreparationException(
                "Release matrix does not contain the exact manual artifact platforms");
        }

        var artifacts = new JsonArray();
        foreach (var row in contract.Rows)
        {
            var source = byPlatform[row.ArtifactPlatform];
            var packageSha = StringOf(source["packageSha256"]);
            var manifestSha = StringOf(source["artifactManifestSha256"]);
            var fileName = StringOf(source["directDownloadFileName"]);
            if (packageSha is null || !Sha256Pattern.IsMatch(packageSha))
            {
                throw new ProductReviewPreparationException(row.ArtifactPlatform + " package SHA-256 is invalid");
            }

            if (manifestSha is null || !Sha256Pattern.IsMatch(manifestSha))
            {
                throw new ProductReviewPreparationException(row.ArtifactPlatform + " manifest SHA-256 is invalid");
            }

            if (!IsDownloadFileName(fileName))
            {
                throw new ProductReviewPreparationException(row.ArtifactPlatform + " download file name is invalid");
            }

            if (!TryPositiveInteger(source["packageBytes"], out var packageBytes))
            {
                throw new ProductReviewPreparationException(row.ArtifactPlatform + " package size is invalid");
            }

            artifacts.Add(new JsonObject
            {
                ["platformRowId"] = row.Id,
                ["artifactPlatform"] = row.ArtifactPlatform,
                ["architecture"] = row.Architecture,
                ["fileName"] = fileName,
                ["sha256"] = packageSha,
                ["bytes"] = packageBytes,
                ["artifactManifestSha256"] = manifestSha,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-manual-product-matrix-candidate-v1",
            ["releaseRunId"] = long.Parse(releaseRunId, CultureInfo.InvariantCulture),
            ["releaseRunUrl"] = "https://github.com/" + repository + "/actions/runs/" + releaseRunId,
            ["releaseMatrixSha256"] = matrixSha256,
            ["candidateRevision"] = revision,
            ["appVersion"] = version,
            ["buildMode"] = "Release",
            ["artifactRows"] = artifacts,
            ["humanReviewStatus"] = "pending",
            ["releaseAcceptance"] = false,
            ["publicationEligible"] = false,
        };
    }

    private static JsonObject BuildSessionTemplate(JsonObject candidate, JsonObject artifactRow, IReadOnlyList<string> flows)
    {
        var platformRowId = artifactRow["platformRowId"]!.GetValue<string>();
        var results = new JsonArray();
        foreach (var flowId in flows)
        {
            results.Add(new JsonObject
            {
                ["flowId"] = flowId,
                ["inputDeviceId"] = "REPLACE",
                ["inputCapabilityIds"] = new JsonArray(),
                ["settingsProfileIds"] = new JsonArray(),
                ["result"] = "pending",
                ["evidencePaths"] = new JsonArray("evidence/" + platformRowId + "/" + flowId + ".REPLACE"),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["kind"] = "vibesnake-manual-product-matrix-session-v2",
            ["sessionId"] = "product-matrix-REPLACE",
            ["candidateRevision"] = candidate["candidateRevision"]!.GetValue<string>(),
            ["artifactSha256"] = artifactRow["sha256"]!.GetValue<string>(),
            ["appVersion"] = candidate["appVersion"]!.GetValue<string>(),
            ["platformRowId"] = platformRowId,
            ["operatingSystemVersion"] = "REPLACE",
            ["hardwareClass"] = "REPLACE",
            ["renderer"] = "REPLACE",
            ["executedUtc"] = "REPLACE",
            ["results"] = results,
        };
    }

    private static string BuildReviewGuide(JsonObject candidate)
    {
        var lines = new List<string>();
        foreach (var row in candidate["artifactRows"]!.AsArray())
        {
            var item = row!.AsObject();
            lines.Add(
                "- `"
                + item["platformRowId"]!.GetValue<string>()
                + "`: `"
                + item["fileName"]!.GetValue<string>()
                + "`, `"
                + item["sha256"]!.GetValue<string>()
                + "`, "
                + item["bytes"]!.GetValue<long>().ToString(CultureInfo.InvariantCulture)
                + " bytes");
        }

        var artifactLines = string.Join('\n', lines);
        var revision = candidate["candidateRevision"]!.GetValue<string>();
        var version = candidate["appVersion"]!.GetValue<string>();
        var runUrl = candidate["releaseRunUrl"]!.GetValue<string>();
        return "# Exact Candidate Manual Review Workspace\n"
            + "\n"
            + "Status: prepared, physical execution pending.\n"
            + "\n"
            + "Candidate revision: `" + revision + "`\n"
            + "Application version: `" + version + "`\n"
            + "Release run: " + runUrl + "\n"
            + "\n"
            + "## Exact artifacts\n"
            + "\n"
            + artifactLines + "\n"
            + "\n"
            + "Before launching an artifact, hash the downloaded file and compare it with the exact value above. Do not\n"
            + "continue with a renamed, rebuilt, or mismatched package.\n"
            + "\n"
            + "## Record a session\n"
            + "\n"
            + "1. Copy the applicable file from `templates/` into `sessions/` with a unique name such as\n"
            + "   `product-matrix-001.json`.\n"
            + "2. Preserve the candidate revision, application version, platform row, and artifact SHA-256 exactly.\n"
            + "3. Replace every `REPLACE` value. Each result names the one input device that executed that flow, any mouse\n"
            + "   capability demonstrated, and every settings profile active for that observation. Record `pass`, `fail`, or\n"
            + "   `blocked` for each executed flow. Copy a platform template into additional sessions when another device or\n"
            + "   profile must execute the same flow.\n"
            + "4. Put sanitized screenshots, video, logs, and observations under `sessions/evidence/<platform-row>/`. Keep every\n"
            + "   evidence path relative and never record controller serials, accounts, private paths, or unrelated device data.\n"
            + "5. A failure or blocked flow is evidence, not a reason to discard the session.\n"
            + "\n"
            + "Validate the retained sessions from the repository root:\n"
            + "\n"
            + "```powershell\n"
            + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- manual-matrix-record `\n"
            + "  <workspace>/sessions `\n"
            + "  <workspace>/candidate.json `\n"
            + "  <workspace>/decision.json\n"
            + "```\n"
            + "\n"
            + "Review remains incomplete until the validator reports all 144 platform-flow cells, all 432 complete-device\n"
            + "flow cells, all 16 mouse-capability cells, and all 32 platform-profile cells passing. This workspace does not\n"
            + "sign, publish, approve, or modify candidate bytes.\n";
    }

    private static WorkspaceManifest BuildManifest(string stagingDirectory, string revision)
    {
        var files = Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new ManifestFile(path, RelativePosix(stagingDirectory, path)))
            .Where(file => !string.Equals(Path.GetFileName(file.Path), ManifestName, StringComparison.Ordinal))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var entries = new JsonArray();
        foreach (var file in files)
        {
            entries.Add(new JsonObject
            {
                ["path"] = file.RelativePath,
                ["bytes"] = new FileInfo(file.Path).Length,
                ["sha256"] = Sha256(file.Path),
            });
        }

        return new WorkspaceManifest(
            new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "vibesnake-manual-product-review-workspace-v1",
                ["candidateRevision"] = revision,
                ["humanReviewStatus"] = "pending",
                ["releaseAcceptance"] = false,
                ["files"] = entries,
            },
            files.Length);
    }

    private static string RelativePosix(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsDownloadFileName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= 255
        && name != "."
        && name != ".."
        && name.IndexOf('/') < 0
        && name.IndexOf('\\') < 0
        && name.IndexOf('\0') < 0
        && !name.Any(char.IsControl);

    private static bool TryPositiveInteger(JsonNode? node, out long value)
    {
        value = 0;
        if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<JsonElement>(out var element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = element.GetRawText();
        return PositiveIntegerPattern.IsMatch(raw)
            && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value > 0;
    }

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonDocument ReadStrict(string path, string label, int maximumBytes) =>
        ReadStrict(path, label, maximumBytes, errors: null)
        ?? throw new ProductReviewPreparationException("unreadable " + label + ": " + path);

    private static JsonDocument? ReadStrict(string path, string label, int maximumBytes, List<string>? errors)
    {
        byte[] bytes;
        try
        {
            var length = new FileInfo(path).Length;
            if (length > maximumBytes)
            {
                return FailRead(errors, LimitMessage(label, maximumBytes));
            }

            bytes = File.ReadAllBytes(path);
            var text = StrictUtf8.GetString(bytes);
            if (StrictUtf8.GetByteCount(text) > maximumBytes)
            {
                return FailRead(errors, LimitMessage(label, maximumBytes));
            }

            if (HasBom(bytes))
            {
                throw new InvalidDataException(label + " must not contain a UTF-8 BOM");
            }

            var nonFinite = FindNonFiniteToken(text);
            if (nonFinite is not null)
            {
                throw new InvalidDataException("non-finite JSON number: " + nonFinite);
            }

            RejectDuplicateProperties(bytes);
            return JsonDocument.Parse(bytes, DocumentOptions);
        }
        catch (ProductReviewPreparationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException
            or DecoderFallbackException
            or ArgumentException)
        {
            return FailRead(
                errors,
                "unreadable " + label + ": " + path + ": " + StrictJsonFile.SingleLine(exception.Message));
        }
    }

    private static JsonDocument? FailRead(List<string>? errors, string message)
    {
        if (errors is null)
        {
            throw new ProductReviewPreparationException(message);
        }

        errors.Add(message);
        return null;
    }

    private static string LimitMessage(string label, int maximumBytes) =>
        label + " exceeds the " + maximumBytes.ToString(CultureInfo.InvariantCulture) + "-byte limit";

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, ReaderOptions);
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
                        throw new InvalidDataException("duplicate JSON field: " + name);
                    }

                    break;
            }
        }
    }

    private static string? FindNonFiniteToken(string source)
    {
        var index = 0;
        while (index < source.Length)
        {
            if (source[index] == '"')
            {
                index = SkipString(source, index);
                continue;
            }

            if (MatchToken(source, index, "-Infinity"))
            {
                return "-Infinity";
            }

            if (MatchToken(source, index, "Infinity"))
            {
                return "Infinity";
            }

            if (MatchToken(source, index, "NaN"))
            {
                return "NaN";
            }

            index++;
        }

        return null;
    }

    private static bool MatchToken(string source, int index, string token) =>
        index + token.Length <= source.Length
        && string.CompareOrdinal(source, index, token, 0, token.Length) == 0
        && HasTokenBoundary(source, index, token.Length);

    private static bool HasTokenBoundary(string source, int index, int length)
    {
        if (index > 0 && IsTokenCharacter(source[index - 1]))
        {
            return false;
        }

        var end = index + length;
        return end >= source.Length || !IsTokenCharacter(source[end]);
    }

    private static bool IsTokenCharacter(char character) =>
        character is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' or '+' or '-' or '.';

    private static int SkipString(string source, int index)
    {
        index++;
        while (index < source.Length)
        {
            var character = source[index++];
            if (character == '\\' && index < source.Length)
            {
                index++;
                continue;
            }

            if (character == '"')
            {
                break;
            }
        }

        return index;
    }

    private static bool HasBom(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    private static bool IsRegularFile(string path)
    {
        if (!File.Exists(path) || Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static bool ExistsAndReparse(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static string ResolveFullPath(string path, string label)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ProductReviewPreparationException(label + " is invalid");
        }
    }

    private static bool IsInside(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        if (string.Equals(normalizedPath, normalizedRoot, comparison))
        {
            return true;
        }

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static void WriteJson(string path, JsonNode node)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(node, RenderOptions);
        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            Array.Resize(ref bytes, bytes.Length + 1);
            bytes[^1] = (byte)'\n';
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void WriteText(string path, string text) =>
        File.WriteAllBytes(path, StrictUtf8.GetBytes(text));

    private static string Sha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        var digest = SHA256.HashData(stream);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private readonly record struct PlatformRow(string Id, string ArtifactPlatform, string Architecture);

    private readonly record struct Contract(IReadOnlyList<PlatformRow> Rows, IReadOnlyList<string> Flows);

    private readonly record struct ManifestFile(string Path, string RelativePath);

    private readonly record struct WorkspaceManifest(JsonObject Node, int FileCount);

    private readonly record struct RetainedMatrix(JsonNode Node, string Sha256);
}

internal sealed class ProductReviewPreparationException : InvalidOperationException
{
    internal ProductReviewPreparationException(string message)
        : base(message)
    {
    }
}
