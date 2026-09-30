using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

internal static class RadioListeningReviewCheck
{
    internal const int MaximumJsonBytes = 4 * 1024 * 1024;

    private const int MaximumTracks = 128;
    private const string ManifestName = "review-copy-manifest.json";
    private const string ReviewSetKind = "vibesnake-radio-review-copy-set-v1";
    private const string ReviewRecordKind = "vibesnake-radio-listening-review-v1";
    private const string DecisionKind = "vibesnake-radio-listening-decision-v1";
    private const string HandoffKind = "vibesnake-radio-listening-handoff-v1";
    private const string PendingGate = "human-listening-and-source-replacement-approval";

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
        MaxDepth = StrictJsonFile.MaximumDepth,
    };
    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex StationIdPattern = new(
        "^[a-z][a-z0-9_]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReviewerIdPattern = new(
        "^radio-reviewer-[0-9]{3}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FindingIdPattern = new(
        "^radio-finding-[0-9]{3}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex UtcPattern = new(
        "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}Z$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PositiveIntegerPattern = new(
        "^[1-9][0-9]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly string[] DeviceIds = ["headphones", "speakers"];
    private static readonly string[] Criteria =
    [
        "complete-playback",
        "no-audible-clipping-or-distortion",
        "clean-start-and-end",
        "relative-level-consistency",
        "station-identity-fit",
        "sustained-listening-comfort",
    ];
    private static readonly string[] Decisions =
    [
        "approve-source-replacement",
        "reject-source-replacement",
        "blocked",
        "pending",
    ];
    private static readonly string[] CriterionResults = ["pass", "fail", "blocked", "pending"];
    private static readonly string[] RecordFields =
    [
        "schemaVersion",
        "kind",
        "stationId",
        "reviewCopyManifestSha256",
        "reviewerId",
        "executedUtc",
        "trackReviews",
        "confirmations",
    ];
    private static readonly string[] TrackReviewFields =
    [
        "assetId",
        "outputFile",
        "outputSha256",
        "reviewedDeviceIds",
        "criteria",
        "decision",
        "findingIds",
    ];
    private static readonly string[] CriterionFields = ["criterionId", "result"];
    private static readonly string[] ConfirmationFields =
    [
        "listenedToEveryTrackInFull",
        "comparedRelativeLevels",
        "reviewedEveryTrackOnAllDeclaredDevices",
        "understandsNoSourceOrReleaseStateChangesAutomatically",
    ];

    internal readonly record struct TemplatePreparation(string StationId, int TrackCount, string OutputPath);

    internal readonly record struct HandoffVerification(string StationId, int TrackCount, string OutputPath);

    internal readonly record struct ListeningValidation(IReadOnlyList<string> Errors, JsonObject Evidence);

    internal static TemplatePreparation PrepareTemplate(
        string repositoryRoot,
        string reviewDirectory,
        string outputPath,
        int maximumJsonBytes = MaximumJsonBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumJsonBytes);
        var directory = RequireReviewDirectory(repositoryRoot, reviewDirectory);
        var output = ResolveFullPath(outputPath, "listening template");
        if (!PathsEqual(Path.GetDirectoryName(output), directory))
        {
            throw new RadioListeningReviewException(
                "listening template must be written directly inside its review directory");
        }

        if (EntryExists(output))
        {
            throw new RadioListeningReviewException(
                "refusing to overwrite existing listening template: " + output);
        }

        var review = LoadReviewSet(directory, maximumJsonBytes);
        var template = BuildTemplate(review);
        try
        {
            var bytes = Serialize(template);
            using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RadioListeningReviewException(
                "could not write listening template: " + StrictJsonFile.SingleLine(exception.Message));
        }

        return new TemplatePreparation(review.StationId, review.Tracks.Count, output);
    }

    internal static HandoffVerification VerifyInputs(
        string repositoryRoot,
        string reviewDirectory,
        string outputPath,
        int maximumJsonBytes = MaximumJsonBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumJsonBytes);
        var directory = RequireReviewDirectory(repositoryRoot, reviewDirectory);
        var output = RequireOutput(repositoryRoot, outputPath, Path.Combine(directory, ManifestName));
        var review = LoadReviewSet(directory, maximumJsonBytes);
        WriteAtomic(output, BuildHandoff(review));
        return new HandoffVerification(review.StationId, review.Tracks.Count, output);
    }

    internal static ListeningValidation ValidateListeningRecord(
        string repositoryRoot,
        string reviewDirectory,
        string recordPath,
        int maximumJsonBytes = MaximumJsonBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumJsonBytes);
        ReviewSet review;
        JsonNode record;
        try
        {
            var directory = RequireReviewDirectory(repositoryRoot, reviewDirectory);
            review = LoadReviewSet(directory, maximumJsonBytes);
            var resolvedRecord = ResolveFullPath(recordPath, "radio listening record");
            if (!PathsEqual(Path.GetDirectoryName(resolvedRecord), directory))
            {
                throw new RadioListeningReviewException(
                    "radio listening record must be directly inside its review directory");
            }

            record = ReadJson(resolvedRecord, "radio listening record", maximumJsonBytes).Node;
        }
        catch (RadioListeningReviewException exception)
        {
            return new ListeningValidation([exception.Message], EarlyEvidence(exception.Message));
        }

        return ValidateRecord(review, record);
    }

    internal static void WriteEvidence(
        string repositoryRoot,
        string outputPath,
        JsonObject evidence,
        params string[] protectedPaths)
    {
        var resolvedProtected = new string[protectedPaths.Length];
        for (var index = 0; index < protectedPaths.Length; index++)
        {
            resolvedProtected[index] = ResolveFullPath(protectedPaths[index], "radio listening evidence output");
        }

        WriteAtomic(RequireOutput(repositoryRoot, outputPath, resolvedProtected), evidence);
    }

    private static ListeningValidation ValidateRecord(ReviewSet review, JsonNode record)
    {
        var errors = new List<string>();
        if (!StrictKeys(record, RecordFields, "listening record", errors))
        {
            return new ListeningValidation(errors, DecisionEvidence(review, false, 0, 0, 0, errors));
        }

        var recordObject = record.AsObject();
        if (!IsInteger(recordObject["schemaVersion"], 1))
        {
            errors.Add("listening record schemaVersion must be 1");
        }

        if (!IsString(recordObject["kind"], ReviewRecordKind))
        {
            errors.Add("listening record kind is invalid");
        }

        if (!IsString(recordObject["stationId"], review.StationId))
        {
            errors.Add("listening record stationId does not match the review copies");
        }

        if (!IsString(recordObject["reviewCopyManifestSha256"], review.ManifestSha256))
        {
            errors.Add("listening record manifest SHA-256 does not match the review copies");
        }

        if (StringOf(recordObject["reviewerId"]) is not string reviewer || !ReviewerIdPattern.IsMatch(reviewer))
        {
            errors.Add("listening record reviewerId must match radio-reviewer-[0-9]{3}");
        }

        if (!IsUtc(recordObject["executedUtc"]))
        {
            errors.Add("listening record executedUtc must use YYYY-MM-DDTHH:MM:SSZ");
        }

        var expected = review.Tracks.ToDictionary(track => track.AssetId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var approved = 0;
        var rejected = 0;
        var blocked = 0;
        var allComplete = true;
        if (recordObject["trackReviews"] is not JsonArray reviews || reviews.Count != review.Tracks.Count)
        {
            errors.Add(
                "listening record trackReviews must contain exactly "
                + review.Tracks.Count.ToString(CultureInfo.InvariantCulture)
                + " rows");
            allComplete = false;
        }
        else
        {
            for (var index = 0; index < reviews.Count; index++)
            {
                var label = "listening record trackReviews[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (!StrictKeys(reviews[index], TrackReviewFields, label, errors))
                {
                    allComplete = false;
                    continue;
                }

                var row = reviews[index]!.AsObject();
                if (StringOf(row["assetId"]) is not string assetId
                    || !expected.TryGetValue(assetId, out var track)
                    || !seen.Add(assetId))
                {
                    errors.Add(label + ".assetId must identify one unique exact review copy");
                    allComplete = false;
                    continue;
                }

                if (!IsString(row["outputFile"], track.OutputFile) || !IsString(row["outputSha256"], track.OutputSha256))
                {
                    errors.Add(label + " output identity does not match the exact review copy");
                }

                if (!DevicesMatch(row["reviewedDeviceIds"]))
                {
                    errors.Add(label + ".reviewedDeviceIds must be ['headphones', 'speakers'] in order");
                    allComplete = false;
                }

                var results = ReadCriteria(row["criteria"], label, errors, ref allComplete);
                if (StringOf(row["decision"]) is string decision && Decisions.Contains(decision, StringComparer.Ordinal))
                {
                    switch (decision)
                    {
                        case "approve-source-replacement":
                            approved++;
                            if (!AllPass(results))
                            {
                                errors.Add(label + " cannot approve unless every criterion passes");
                            }

                            break;
                        case "reject-source-replacement":
                            rejected++;
                            if (!results.Contains("fail", StringComparer.Ordinal))
                            {
                                errors.Add(label + " rejection requires at least one failed criterion");
                            }

                            break;
                        case "blocked":
                            blocked++;
                            if (!results.Contains("blocked", StringComparer.Ordinal))
                            {
                                errors.Add(label + " blocked decision requires at least one blocked criterion");
                            }

                            break;
                        default:
                            allComplete = false;
                            break;
                    }
                }
                else
                {
                    errors.Add(label + ".decision is unsupported: " + PythonRepr(row["decision"]));
                    allComplete = false;
                }

                if (!ValidFindings(row["findingIds"]))
                {
                    errors.Add(label + ".findingIds must contain unique radio-finding-[0-9]{3} IDs");
                }
                else if (StringOf(row["decision"]) is "reject-source-replacement" or "blocked"
                    && row["findingIds"] is JsonArray { Count: 0 })
                {
                    errors.Add(label + " rejected or blocked decision requires a finding ID");
                }
            }
        }

        if (!seen.SetEquals(expected.Keys))
        {
            errors.Add("listening record must cover every exact review copy once");
            allComplete = false;
        }

        if (!StrictKeys(recordObject["confirmations"], ConfirmationFields, "listening record confirmations", errors))
        {
            allComplete = false;
        }
        else
        {
            var confirmations = recordObject["confirmations"]!.AsObject();
            foreach (var field in ConfirmationFields)
            {
                if (confirmations[field] is not JsonValue value || !value.TryGetValue<bool>(out var confirmed))
                {
                    errors.Add("listening record confirmations." + field + " must be a boolean");
                    allComplete = false;
                }
                else if (!confirmed)
                {
                    allComplete = false;
                }
            }
        }

        return new ListeningValidation(
            errors,
            DecisionEvidence(review, allComplete && errors.Count == 0, approved, rejected, blocked, errors));
    }

    private static List<string> ReadCriteria(JsonNode? node, string label, List<string> errors, ref bool allComplete)
    {
        var results = new List<string>();
        if (node is not JsonArray rows || rows.Count != Criteria.Length)
        {
            errors.Add(
                label
                + ".criteria must contain the exact "
                + Criteria.Length.ToString(CultureInfo.InvariantCulture)
                + " criteria");
            allComplete = false;
            return results;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var criterionLabel = label + ".criteria[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (!StrictKeys(rows[index], CriterionFields, criterionLabel, errors))
            {
                allComplete = false;
                continue;
            }

            var criterion = rows[index]!.AsObject();
            if (!IsString(criterion["criterionId"], Criteria[index]))
            {
                errors.Add(criterionLabel + ".criterionId is out of contract order");
            }

            if (StringOf(criterion["result"]) is string result && CriterionResults.Contains(result, StringComparer.Ordinal))
            {
                results.Add(result);
                if (result == "pending")
                {
                    allComplete = false;
                }
            }
            else
            {
                errors.Add(criterionLabel + ".result is unsupported: " + PythonRepr(criterion["result"]));
            }
        }

        return results;
    }

    private static JsonObject BuildTemplate(ReviewSet review)
    {
        var tracks = new JsonArray();
        foreach (var track in review.Tracks)
        {
            var criteria = new JsonArray();
            foreach (var criterion in Criteria)
            {
                criteria.Add(new JsonObject
                {
                    ["criterionId"] = criterion,
                    ["result"] = "pending",
                });
            }

            tracks.Add(new JsonObject
            {
                ["assetId"] = track.AssetId,
                ["outputFile"] = track.OutputFile,
                ["outputSha256"] = track.OutputSha256,
                ["reviewedDeviceIds"] = new JsonArray(),
                ["criteria"] = criteria,
                ["decision"] = "pending",
                ["findingIds"] = new JsonArray(),
            });
        }

        var confirmations = new JsonObject();
        foreach (var field in ConfirmationFields)
        {
            confirmations[field] = false;
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = ReviewRecordKind,
            ["stationId"] = review.StationId,
            ["reviewCopyManifestSha256"] = review.ManifestSha256,
            ["reviewerId"] = "radio-reviewer-REPLACE",
            ["executedUtc"] = "REPLACE",
            ["trackReviews"] = tracks,
            ["confirmations"] = confirmations,
        };
    }

    private static JsonObject BuildHandoff(ReviewSet review)
    {
        var hashes = new JsonObject();
        foreach (var track in review.Tracks)
        {
            hashes[track.AssetId] = track.OutputSha256;
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = HandoffKind,
            ["passed"] = true,
            ["technicalInputsVerified"] = true,
            ["stationId"] = review.StationId,
            ["reviewCopyManifestSha256"] = review.ManifestSha256,
            ["trackCount"] = review.Tracks.Count,
            ["reviewCopySha256ByAssetId"] = hashes,
            ["humanListeningStatus"] = "pending",
            ["listeningComplete"] = false,
            ["sourceReplacementApproved"] = false,
            ["releaseApproved"] = false,
            ["exportEligibilityChanged"] = false,
            ["pendingGates"] = new JsonArray(PendingGate),
            ["errors"] = new JsonArray(),
        };
    }

    private static JsonObject DecisionEvidence(
        ReviewSet review,
        bool listeningComplete,
        int approved,
        int rejected,
        int blocked,
        List<string> errors)
    {
        var allApproved = listeningComplete && approved == review.Tracks.Count;
        var pending = new JsonArray();
        if (!allApproved)
        {
            pending.Add(PendingGate);
        }

        var reported = new JsonArray();
        foreach (var error in errors)
        {
            reported.Add(error);
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = DecisionKind,
            ["passed"] = errors.Count == 0,
            ["technicalInputsVerified"] = true,
            ["stationId"] = review.StationId,
            ["trackCount"] = review.Tracks.Count,
            ["approvedTrackCount"] = approved,
            ["rejectedTrackCount"] = rejected,
            ["blockedTrackCount"] = blocked,
            ["listeningComplete"] = listeningComplete,
            ["sourceReplacementApproved"] = allApproved,
            ["releaseApproved"] = false,
            ["exportEligibilityChanged"] = false,
            ["pendingGates"] = pending,
            ["errors"] = reported,
        };
    }

    private static JsonObject EarlyEvidence(string error) => new()
    {
        ["schemaVersion"] = 1,
        ["kind"] = DecisionKind,
        ["passed"] = false,
        ["technicalInputsVerified"] = false,
        ["listeningComplete"] = false,
        ["sourceReplacementApproved"] = false,
        ["releaseApproved"] = false,
        ["exportEligibilityChanged"] = false,
        ["errors"] = new JsonArray(error),
    };

    private static ReviewSet LoadReviewSet(string directory, int maximumJsonBytes)
    {
        var manifestPath = Path.Combine(directory, ManifestName);
        var manifestFile = ReadJson(manifestPath, "radio review-copy manifest", maximumJsonBytes);
        if (manifestFile.Node is not JsonObject manifest)
        {
            throw new RadioListeningReviewException("radio review-copy manifest must be an object");
        }

        RequireManifestFlag(manifest, "schemaVersion", static node => IsInteger(node, 1), "1");
        RequireManifestFlag(manifest, "kind", static node => IsString(node, ReviewSetKind), "'" + ReviewSetKind + "'");
        RequireManifestFlag(manifest, "technicalPass", IsTrue, "True");
        RequireManifestFlag(manifest, "releaseApproved", IsFalse, "False");
        RequireManifestFlag(manifest, "sourceReplacementApproved", IsFalse, "False");
        RequireManifestFlag(manifest, "exportEligibilityChanged", IsFalse, "False");
        RequireManifestFlag(manifest, "humanListeningRequired", IsTrue, "True");
        RequireManifestFlag(manifest, "humanListeningStatus", static node => IsString(node, "pending"), "'pending'");
        RequireManifestFlag(manifest, "sourceBytesModified", IsFalse, "False");
        RequireManifestFlag(manifest, "modifiedSourcePaths", static node => node is JsonArray { Count: 0 }, "[]");
        if (StringOf(manifest["stationId"]) is not string stationId || !StationIdPattern.IsMatch(stationId))
        {
            throw new RadioListeningReviewException("radio review-copy manifest stationId is invalid");
        }

        if (manifest["reviewCopies"] is not JsonArray rows || rows.Count is < 1 or > MaximumTracks)
        {
            throw new RadioListeningReviewException(
                "radio review-copy manifest must contain 1 to "
                + MaximumTracks.ToString(CultureInfo.InvariantCulture)
                + " tracks");
        }

        if (manifest["summary"] is not JsonObject summary || !IsInteger(summary["trackCount"], rows.Count))
        {
            throw new RadioListeningReviewException("radio review-copy manifest summary track count is invalid");
        }

        if (!IsInteger(summary["technicalPassCount"], rows.Count) || !IsInteger(summary["technicalFailureCount"], 0))
        {
            throw new RadioListeningReviewException(
                "radio review-copy manifest summary does not report a complete technical pass");
        }

        var tracks = new List<ReviewTrack>(rows.Count);
        var assetIds = new HashSet<string>(StringComparer.Ordinal);
        var outputNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            var label = "radio review-copy manifest reviewCopies[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (rows[index] is not JsonObject row)
            {
                throw new RadioListeningReviewException(label + " must be an object");
            }

            if (StringOf(row["assetId"]) is not string assetId
                || !assetId.StartsWith("asset:audio/radio/", StringComparison.Ordinal)
                || !assetIds.Add(assetId))
            {
                throw new RadioListeningReviewException(label + ".assetId must be a unique radio asset ID");
            }

            if (StringOf(row["outputFile"]) is not string outputName || !IsReviewFileName(outputName) || !outputNames.Add(outputName))
            {
                throw new RadioListeningReviewException(label + ".outputFile must be a unique review FLAC file name");
            }

            if (StringOf(row["outputSha256"]) is not string outputSha256 || !Sha256Pattern.IsMatch(outputSha256))
            {
                throw new RadioListeningReviewException(label + ".outputSha256 must be a SHA-256 digest");
            }

            if (!TryPositiveInteger(row["outputBytes"], out var outputBytes))
            {
                throw new RadioListeningReviewException(label + ".outputBytes must be a positive integer");
            }

            if (!IsString(row["stationId"], stationId) || !IsTrue(row["technicalPass"]) || row["failures"] is not JsonArray { Count: 0 })
            {
                throw new RadioListeningReviewException(
                    label + " must report the same station and a complete technical pass");
            }

            var copyPath = Path.GetFullPath(Path.Combine(directory, outputName));
            if (!IsInside(directory, copyPath) || !PathsEqual(Path.GetDirectoryName(copyPath), directory))
            {
                throw new RadioListeningReviewException(label + ".outputFile must be a unique review FLAC file name");
            }

            if (!IsRegularFile(copyPath))
            {
                throw new RadioListeningReviewException("missing regular review copy: " + copyPath);
            }

            if (new FileInfo(copyPath).Length != outputBytes)
            {
                throw new RadioListeningReviewException("review copy size mismatch: " + outputName);
            }

            if (!string.Equals(Sha256(copyPath), outputSha256, StringComparison.Ordinal))
            {
                throw new RadioListeningReviewException("review copy SHA-256 mismatch: " + outputName);
            }

            tracks.Add(new ReviewTrack(assetId, outputName, outputSha256));
        }

        tracks.Sort(static (left, right) => string.CompareOrdinal(left.AssetId, right.AssetId));
        return new ReviewSet(stationId, manifestFile.Sha256, tracks);
    }

    private static void RequireManifestFlag(JsonObject manifest, string name, Func<JsonNode?, bool> matches, string repr)
    {
        if (!matches(manifest[name]))
        {
            throw new RadioListeningReviewException("radio review-copy manifest " + name + " must be " + repr);
        }
    }

    private static bool StrictKeys(JsonNode? node, string[] expected, string label, List<string> errors)
    {
        if (node is not JsonObject obj)
        {
            errors.Add(label + " must be an object");
            return false;
        }

        if (!obj.Select(static pair => pair.Key).OrderBy(static key => key, StringComparer.Ordinal)
            .SequenceEqual(expected.OrderBy(static key => key, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            errors.Add(
                label
                + " fields must be "
                + StrictJsonFile.FormatFields(expected)
                + "; got "
                + StrictJsonFile.FormatFields(obj.Select(static pair => pair.Key)));
            return false;
        }

        return true;
    }

    private static bool DevicesMatch(JsonNode? node)
    {
        if (node is not JsonArray devices || devices.Count != DeviceIds.Length)
        {
            return false;
        }

        for (var index = 0; index < DeviceIds.Length; index++)
        {
            if (!IsString(devices[index], DeviceIds[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidFindings(JsonNode? node)
    {
        if (node is not JsonArray findings)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            if (StringOf(finding) is not string value || !FindingIdPattern.IsMatch(value) || !seen.Add(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllPass(List<string> results)
    {
        if (results.Count != Criteria.Length)
        {
            return false;
        }

        foreach (var result in results)
        {
            if (result != "pass")
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsReviewFileName(string name)
    {
        if (!name.EndsWith(".review.flac", StringComparison.Ordinal) || Path.GetFileName(name) != name)
        {
            return false;
        }

        foreach (var character in name)
        {
            if (character is '/' or '\\' or '\0' || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUtc(JsonNode? node)
    {
        if (StringOf(node) is not string text || !UtcPattern.IsMatch(text))
        {
            return false;
        }

        return DateTime.TryParseExact(
            text,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static string RequireReviewDirectory(string repositoryRoot, string reviewDirectory)
    {
        var root = ResolveFullPath(repositoryRoot, "repository root");
        var resolved = ResolveFullPath(reviewDirectory, "radio listening review directory");
        if (IsReparse(resolved))
        {
            throw new RadioListeningReviewException("radio listening review directory must be a regular directory path");
        }

        if (IsInside(Path.Combine(root, "assets"), resolved))
        {
            throw new RadioListeningReviewException("radio listening review cannot use the public assets tree");
        }

        if (IsInside(root, resolved) && !IsInside(Path.Combine(root, "TestResults", "radio-review"), resolved))
        {
            throw new RadioListeningReviewException(
                "radio listening review inside the repository must use ignored TestResults");
        }

        return resolved;
    }

    private static string RequireOutput(string repositoryRoot, string outputPath, params string[] protectedPaths)
    {
        var root = ResolveFullPath(repositoryRoot, "repository root");
        var resolved = ResolveFullPath(outputPath, "radio listening evidence output");
        if (!resolved.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new RadioListeningReviewException("radio listening evidence output must use a .json file name");
        }

        if (IsInside(root, resolved) && !IsInside(Path.Combine(root, "TestResults", "radio-review"), resolved))
        {
            throw new RadioListeningReviewException(
                "radio listening evidence inside the repository must use ignored TestResults");
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFileName(resolved), ManifestName, comparison))
        {
            throw new RadioListeningReviewException("radio listening evidence output cannot overwrite an input record");
        }

        foreach (var protectedPath in protectedPaths)
        {
            if (PathsEqual(resolved, Path.GetFullPath(protectedPath)))
            {
                throw new RadioListeningReviewException("radio listening evidence output cannot overwrite an input record");
            }
        }

        if (IsReparse(resolved))
        {
            throw new RadioListeningReviewException("radio listening evidence output must be a regular file path");
        }

        return resolved;
    }

    private static ParsedJson ReadJson(string path, string label, int maximumBytes)
    {
        if (!IsRegularFile(path))
        {
            throw new RadioListeningReviewException("missing regular " + label + ": " + path);
        }

        try
        {
            var length = new FileInfo(path).Length;
            if (length > maximumBytes)
            {
                throw new RadioListeningReviewException(
                    label
                    + " exceeds the "
                    + maximumBytes.ToString(CultureInfo.InvariantCulture)
                    + "-byte limit");
            }

            var bytes = File.ReadAllBytes(path);
            var text = StrictUtf8.GetString(bytes);
            var nonFinite = FindNonFiniteToken(text);
            if (nonFinite is not null)
            {
                throw new InvalidDataException("non-finite JSON number: " + nonFinite);
            }

            StrictJsonFile.RejectDuplicateProperties(bytes);
            var node = JsonNode.Parse(bytes, documentOptions: DocumentOptions)
                ?? throw new InvalidDataException("JSON root is missing");
            return new ParsedJson(node, Sha256(bytes));
        }
        catch (RadioListeningReviewException)
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
            throw new RadioListeningReviewException(
                "unreadable "
                + label
                + ": "
                + path
                + ": "
                + StrictJsonFile.SingleLine(exception.Message));
        }
    }

    private static void WriteAtomic(string path, JsonNode node)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new RadioListeningReviewException("could not write radio listening evidence: output directory is missing");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(
            parent,
            "." + Path.GetFileName(path) + ".staging." + Guid.NewGuid().ToString("N"));
        try
        {
            var bytes = Serialize(node);
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(staging, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RadioListeningReviewException(
                "could not write radio listening evidence: " + StrictJsonFile.SingleLine(exception.Message));
        }
        finally
        {
            try
            {
                if (File.Exists(staging))
                {
                    File.Delete(staging);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static byte[] Serialize(JsonNode node)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(node, RenderOptions);
        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            Array.Resize(ref bytes, bytes.Length + 1);
            bytes[^1] = (byte)'\n';
        }

        return bytes;
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

    private static bool TryPositiveInteger(JsonNode? node, out long value)
    {
        value = 0;
        if (node is not JsonValue json || !json.TryGetValue<JsonElement>(out var element) || element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = element.GetRawText();
        return PositiveIntegerPattern.IsMatch(raw)
            && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value > 0;
    }

    private static bool IsInteger(JsonNode? node, long expected)
    {
        if (node is not JsonValue json || !json.TryGetValue<JsonElement>(out var element) || element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = element.GetRawText();
        return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && string.Equals(raw, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            && parsed == expected;
    }

    private static bool IsTrue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var boolean) && boolean;

    private static bool IsFalse(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var boolean) && !boolean;

    private static bool IsString(JsonNode? node, string expected) =>
        StringOf(node) == expected;

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string PythonRepr(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<JsonElement>(out var element))
        {
            return StrictJsonFile.Format(element);
        }

        return "None";
    }

    private static string ResolveFullPath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new RadioListeningReviewException(label + " is invalid");
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RadioListeningReviewException(label + " is invalid");
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

    private static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            comparison);
    }

    private static bool IsRegularFile(string path)
    {
        if (!File.Exists(path) || Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static bool IsReparse(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool EntryExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string Sha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private readonly record struct ReviewTrack(string AssetId, string OutputFile, string OutputSha256);

    private readonly record struct ReviewSet(string StationId, string ManifestSha256, List<ReviewTrack> Tracks);

    private readonly record struct ParsedJson(JsonNode Node, string Sha256);
}

internal sealed class RadioListeningReviewException : InvalidOperationException
{
    internal RadioListeningReviewException(string message)
        : base(message)
    {
    }
}
