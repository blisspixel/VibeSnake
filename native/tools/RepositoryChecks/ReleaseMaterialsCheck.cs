using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepositoryChecks;

public static class ReleaseMaterialsCheck
{
    private const string ContractRelativePath = "config/release_materials_v1.json";
    private const int MaximumJsonDepth = 64;
    private const int MaximumPathsPerEvidenceSet = 16;
    private const int MaximumRetainedFiles = 320;
    private const int MaximumRelativePathCharacters = 512;
    private const int MaximumTextCharacters = 4096;
    private const int MaximumMediaElements = 4_096;
    private const int MaximumFailures = 128;
    private const int MaximumFailureCharacters = 256;
    private const int MaximumEvidenceBytes = 256 * 1024;
    private const long MaximumContractBytes = 1024 * 1024;
    private const long MaximumCandidateBytes = 4 * 1024 * 1024;
    private const long MaximumDocumentBytes = 16 * 1024 * 1024;
    private const long MaximumDocumentTotalBytes = 64 * 1024 * 1024;
    private const long MaximumImageBytes = 256 * 1024 * 1024;
    private const long MaximumVideoBytes = 512 * 1024 * 1024;
    private const long MaximumRetainedFileBytes = 8L * 1024 * 1024 * 1024;
    private const long MaximumRetainedTotalBytes = 32L * 1024 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly JsonSerializerOptions RenderOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };

    private static readonly string[] RequiredDocumentPaths =
    [
        "README.md",
        "docs/guides/PLAYER_GUIDE.md",
        "docs/guides/ACCESSIBILITY.md",
        "PRIVACY.md",
        "SUPPORT.md",
        "docs/guides/RECOVERY.md",
        "docs/release/KNOWN_ISSUES.md",
        "THIRD_PARTY_NOTICES.md",
        "CREDITS.md",
        "CHANGELOG.md",
    ];

    private static readonly string[] ArtifactPlatforms =
        ["windows-x64", "macos-universal", "linux-x64"];

    private static readonly string[] InputDeviceIds =
        ["keyboard", "mouse", "xbox-layout-controller", "playstation-layout-controller"];

    private static readonly string[] ScreenshotRoles =
    [
        "main-menu",
        "classic-gameplay",
        "vibe-gameplay",
        "controls-remapping",
        "accessibility-settings",
        "spectator-and-replay",
    ];

    private static readonly string[] VideoRoles =
        ["gameplay-overview", "accessibility-and-input"];

    private static readonly string[] MarketingClaimIds =
    [
        "native-three-platform-player",
        "offline-core-play",
        "keyboard-mouse-controller",
        "nine-integrated-powers",
        "accessibility-features",
        "local-save-recovery",
        "optional-pack-boundary",
        "no-account-required",
    ];

    private static readonly string[] CandidateFields =
    [
        "schemaVersion",
        "kind",
        "sourceRevision",
        "appVersion",
        "artifactManifestSha256ByPlatform",
        "downloadBytesByPlatform",
        "installedBytesByPlatform",
        "supportedOperatingSystemsByPlatform",
        "inputDeviceIds",
        "inputEvidencePathsByDevice",
        "offlineBehavior",
        "saveLocationsByPlatform",
        "coreContentBytes",
        "optionalContentBytes",
        "documentationSha256",
        "screenshotPathsByRole",
        "videoPathsByRole",
        "retainedFileSha256",
        "marketingClaims",
    ];

    private static readonly string[] MarketingClaimFields =
        ["claimId", "statement", "evidencePaths"];

    private static readonly HashSet<string> RecognizedMp4Brands = new(
        ["isom", "iso2", "mp41", "mp42", "avc1", "M4V ", "qt  "],
        StringComparer.Ordinal);

    private static readonly HashSet<string> RecognizedMp4VideoSampleEntries = new(
        ["avc1"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> RecognizedWebmVideoCodecs = new(
        ["V_VP8", "V_VP9", "V_AV1"],
        StringComparer.Ordinal);

    private static readonly string[] ReleaseRules =
    [
        "Every required document is nonempty and hash-bound to the exact candidate record.",
        "Operating-system support and download and installed sizes are stated separately for every artifact platform.",
        "Keyboard, mouse, Xbox-layout controller, and PlayStation-layout controller claims link to retained physical evidence.",
        "Core and optional content sizes are stated separately and match the candidate manifests.",
        "Offline behavior and platform save locations are published exactly.",
        "Every screenshot and video role is captured from the exact candidate and retained as a nonempty file.",
        "Every permitted marketing claim is nonempty, evidence-linked, and bound to the candidate revision.",
        "Pending, reference-player, qualification-only, or unapproved-content evidence cannot be presented as a final candidate claim.",
    ];

    private static readonly string[] PendingGates =
    [
        "exact-candidate-document-hashes",
        "platform-os-and-size-publication",
        "physical-input-evidence",
        "candidate-screenshots-and-video",
        "evidence-bound-marketing-claims",
        "final-third-party-notice-generation",
        "tested-public-support-route",
    ];

    private static readonly string[] SeparateReleaseGates =
    [
        "artifact-manifest-size-reconciliation",
        "marketing-claim-approval",
        "visible-image-review",
        "video-playback-review",
    ];

    private static readonly KeyValuePair<string, string>[] PendingDocumentMarkers =
    [
        new("README.md", "Store-ready 1.0 is not ready"),
        new("docs/guides/PLAYER_GUIDE.md", "currently runs from a source checkout"),
        new("docs/guides/ACCESSIBILITY.md", "Accessibility validation is still in progress"),
        new("PRIVACY.md", "Final candidate review is pending"),
        new("SUPPORT.md", "Public support, issue, play-feedback, and enhancement intake is currently closed"),
        new("docs/guides/RECOVERY.md", "final candidate wording and physical review are pending"),
        new("docs/release/KNOWN_ISSUES.md", "pre-candidate alpha issues"),
        new("THIRD_PARTY_NOTICES.md", "final notice bundle must be regenerated"),
        new("CREDITS.md", "Final candidate content and platform credits are pending"),
    ];

    private static readonly HashSet<string> ContractFields = new(
        [
            "schemaVersion",
            "kind",
            "status",
            "requiredDocumentPaths",
            "artifactPlatforms",
            "inputDeviceIds",
            "screenshotRoles",
            "videoRoles",
            "marketingClaimIds",
            "offlineBehaviorValue",
            "requiredCandidateFields",
            "requiredMarketingClaimFields",
            "releaseRules",
        ],
        StringComparer.Ordinal);

    public static RepositoryCheckResult Inspect(string repositoryRoot) =>
        Execute(repositoryRoot, candidatePath: null, expectedRevision: null, outputPath: null);

    public static RepositoryCheckResult WriteFoundationHandoff(
        string repositoryRoot,
        string outputPath) =>
        Execute(repositoryRoot, candidatePath: null, expectedRevision: null, outputPath);

    public static RepositoryCheckResult WriteCandidateHandoff(
        string repositoryRoot,
        string candidatePath,
        string expectedRevision,
        string outputPath) =>
        Execute(repositoryRoot, candidatePath, expectedRevision, outputPath);

    internal static string? ValidateMediaForRepositoryCheck(string path, string mediaKind)
    {
        try
        {
            var resolved = ResolveExplicitRegularFile(
                path,
                MaximumRetainedFileBytes,
                "retained media");
            var file = new RetainedFile(resolved, Path.GetFileName(resolved), new FileInfo(resolved).Length);
            var failures = new List<string>();
            file.MediaExpectations.Add(mediaKind, "retained media");
            _ = HashAndValidateStableFile(file, failures);
            return failures.FirstOrDefault();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return SingleLine(exception.Message);
        }
    }

    private static RepositoryCheckResult Execute(
        string repositoryRoot,
        string? candidatePath,
        string? expectedRevision,
        string? outputPath)
    {
        try
        {
            var qualification = Qualify(repositoryRoot, candidatePath, expectedRevision);
            if (outputPath is not null)
            {
                WriteAtomicEvidence(
                    repositoryRoot,
                    candidatePath,
                    qualification.TrustedRetainedPaths,
                    outputPath,
                    qualification.Json);
            }

            return qualification.Failures.Length == 0
                ? new RepositoryCheckResult(
                    "Release materials",
                    true,
                    qualification.CandidateAccepted
                        ? "Exact-candidate release-material structure qualified; separate release gates remain pending."
                        : "Release-material foundation qualified; exact candidate materials remain pending.",
                    [])
                : Failed(qualification.Failures);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed([SingleLine(exception.Message)]);
        }
    }

    private static Qualification Qualify(
        string repositoryRoot,
        string? candidatePath,
        string? expectedRevision)
    {
        var root = ResolveRepositoryRoot(repositoryRoot);
        var foundationFailures = new List<string>();
        var contractSha256 = ReadAndValidateContract(root, foundationFailures);
        var documents = ReadDocuments(root, foundationFailures);
        string? appVersion = null;
        try
        {
            _ = ResolveRegularFile(root, "VERSION", 1024, "canonical product version");
            appVersion = ProductVersionCheck.ReadCanonicalVersion(root);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            foundationFailures.Add(SingleLine(exception.Message));
        }

        var foundationQualified = foundationFailures.Count == 0;
        var candidateFailures = new List<string>();
        string? candidateSha256 = null;
        string? acceptedRevision = null;
        var candidateSupplied = candidatePath is not null;
        var candidateParsed = false;
        IReadOnlyList<string> trustedRetainedPaths = [];

        if (candidateSupplied)
        {
            if (!IsLowerHex(expectedRevision, 40))
            {
                candidateFailures.Add(
                    "an exact lowercase 40-character expected revision is required with a candidate");
            }

            foreach (var marker in PendingDocumentMarkers)
            {
                if (documents.Text.TryGetValue(marker.Key, out var text)
                    && text.Contains(marker.Value, StringComparison.OrdinalIgnoreCase))
                {
                    candidateFailures.Add(
                        $"candidate document retains pending marker in {marker.Key}: {marker.Value}");
                }
            }

            ValidateCandidate(
                root,
                candidatePath!,
                expectedRevision,
                appVersion,
                documents.Hashes,
                candidateFailures,
                out candidateSha256,
                out var parsedRevision,
                out candidateParsed,
                out trustedRetainedPaths);
            if (candidateParsed
                && foundationQualified
                && candidateFailures.Count == 0)
            {
                acceptedRevision = parsedRevision;
            }
        }

        var failures = BoundFailures(foundationFailures.Concat(candidateFailures));
        var candidateAccepted = candidateSupplied
            && candidateParsed
            && foundationQualified
            && failures.Length == 0;
        var json = RenderEvidence(
            failures,
            foundationQualified,
            contractSha256,
            documents.Hashes,
            candidateSupplied,
            candidateAccepted,
            acceptedRevision,
            appVersion,
            candidateSha256);
        return new Qualification(failures, candidateAccepted, json, trustedRetainedPaths);
    }

    private static string? ReadAndValidateContract(string root, List<string> failures)
    {
        byte[] bytes;
        try
        {
            var path = ResolveRegularFile(
                root,
                ContractRelativePath,
                MaximumContractBytes,
                "release materials contract");
            bytes = ReadBoundedBytes(path, MaximumContractBytes, "release materials contract");
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add(SingleLine(exception.Message));
            return null;
        }

        var sha256 = Sha256(bytes);
        JsonDocument? document = null;
        try
        {
            document = ParseStrictJson(bytes, "release materials contract");
            var value = document.RootElement;
            if (!RequireExactFields(value, ContractFields, "contract", failures))
            {
                return sha256;
            }

            RequireInteger(value.GetProperty("schemaVersion"), 1, "contract.schemaVersion", failures);
            RequireExactText(value.GetProperty("kind"), "vibesnake-release-materials-v1", "contract.kind", failures);
            RequireExactText(
                value.GetProperty("status"),
                "foundation-qualified-candidate-pending",
                "contract.status",
                failures);
            RequireExactArray(value.GetProperty("requiredDocumentPaths"), RequiredDocumentPaths, "contract.requiredDocumentPaths", failures);
            RequireExactArray(value.GetProperty("artifactPlatforms"), ArtifactPlatforms, "contract.artifactPlatforms", failures);
            RequireExactArray(value.GetProperty("inputDeviceIds"), InputDeviceIds, "contract.inputDeviceIds", failures);
            RequireExactArray(value.GetProperty("screenshotRoles"), ScreenshotRoles, "contract.screenshotRoles", failures);
            RequireExactArray(value.GetProperty("videoRoles"), VideoRoles, "contract.videoRoles", failures);
            RequireExactArray(value.GetProperty("marketingClaimIds"), MarketingClaimIds, "contract.marketingClaimIds", failures);
            RequireExactText(
                value.GetProperty("offlineBehaviorValue"),
                "core-play-requires-no-account-or-network",
                "contract.offlineBehaviorValue",
                failures);
            RequireExactArray(value.GetProperty("requiredCandidateFields"), CandidateFields, "contract.requiredCandidateFields", failures);
            RequireExactArray(value.GetProperty("requiredMarketingClaimFields"), MarketingClaimFields, "contract.requiredMarketingClaimFields", failures);
            RequireExactArray(value.GetProperty("releaseRules"), ReleaseRules, "contract.releaseRules", failures);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add(SingleLine(exception.Message));
        }
        finally
        {
            document?.Dispose();
        }

        return sha256;
    }

    private static DocumentSet ReadDocuments(string root, List<string> failures)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var relativePath in RequiredDocumentPaths)
        {
            try
            {
                var path = ResolveRegularFile(root, relativePath, MaximumDocumentBytes, "required release document");
                var bytes = ReadBoundedBytes(path, MaximumDocumentBytes, $"required release document {relativePath}");
                totalBytes = checked(totalBytes + bytes.Length);
                if (totalBytes > MaximumDocumentTotalBytes)
                {
                    throw new InvalidDataException(
                        $"release documents exceed the {MaximumDocumentTotalBytes}-byte aggregate limit");
                }

                if (bytes.Length < 200)
                {
                    failures.Add($"required release document is unexpectedly small: {relativePath}");
                    continue;
                }

                text[relativePath] = StrictUtf8.GetString(bytes);
                hashes[relativePath] = Sha256(bytes);
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                failures.Add($"{relativePath}: {SingleLine(exception.Message)}");
            }
        }

        return new DocumentSet(hashes, text);
    }

    private static void ValidateCandidate(
        string repositoryRoot,
        string candidatePath,
        string? expectedRevision,
        string? canonicalAppVersion,
        IReadOnlyDictionary<string, string> documentHashes,
        List<string> failures,
        out string? candidateSha256,
        out string? sourceRevision,
        out bool parsed,
        out IReadOnlyList<string> trustedRetainedPaths)
    {
        candidateSha256 = null;
        sourceRevision = null;
        parsed = false;
        trustedRetainedPaths = [];
        string path;
        byte[] bytes;
        try
        {
            path = ResolveExplicitRegularFile(candidatePath, MaximumCandidateBytes, "release materials candidate");
            bytes = ReadBoundedBytes(path, MaximumCandidateBytes, "release materials candidate");
            candidateSha256 = Sha256(bytes);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add(SingleLine(exception.Message));
            return;
        }

        JsonDocument? document = null;
        RetainedFiles? retained = null;
        try
        {
            document = ParseStrictJson(bytes, "release materials candidate");
            var candidate = document.RootElement;
            if (!RequireExactFields(
                candidate,
                CandidateFields.ToHashSet(StringComparer.Ordinal),
                "candidate",
                failures))
            {
                return;
            }

            parsed = true;
            RequireInteger(candidate.GetProperty("schemaVersion"), 1, "candidate.schemaVersion", failures);
            RequireExactText(
                candidate.GetProperty("kind"),
                "vibesnake-release-materials-candidate-v1",
                "candidate.kind",
                failures);

            var revision = RequireText(candidate.GetProperty("sourceRevision"), "candidate.sourceRevision", failures);
            if (revision is not null)
            {
                sourceRevision = revision;
                if (!IsLowerHex(revision, 40))
                {
                    failures.Add("candidate.sourceRevision must be a lowercase 40-character revision");
                }

                if (!string.Equals(revision, expectedRevision, StringComparison.Ordinal))
                {
                    failures.Add("candidate.sourceRevision must match the exact expected revision");
                }
            }

            var appVersion = RequireText(candidate.GetProperty("appVersion"), "candidate.appVersion", failures);
            if (appVersion is not null
                && canonicalAppVersion is not null
                && !string.Equals(appVersion, canonicalAppVersion, StringComparison.Ordinal))
            {
                failures.Add(
                    $"candidate.appVersion must be '{canonicalAppVersion}'; got '{appVersion}'");
            }

            ValidateDigestMap(
                candidate.GetProperty("artifactManifestSha256ByPlatform"),
                ArtifactPlatforms,
                "candidate.artifactManifestSha256ByPlatform",
                failures);
            ValidateIntegerMap(candidate.GetProperty("downloadBytesByPlatform"), "candidate.downloadBytesByPlatform", positive: true, failures);
            ValidateIntegerMap(candidate.GetProperty("installedBytesByPlatform"), "candidate.installedBytesByPlatform", positive: true, failures);
            ValidateOperatingSystems(candidate.GetProperty("supportedOperatingSystemsByPlatform"), failures);
            RequireExactArray(candidate.GetProperty("inputDeviceIds"), InputDeviceIds, "candidate.inputDeviceIds", failures);

            var baseDirectory = Path.GetDirectoryName(path)!;
            retained = new RetainedFiles(baseDirectory, failures);
            ValidatePathMap(
                candidate.GetProperty("inputEvidencePathsByDevice"),
                InputDeviceIds,
                "candidate.inputEvidencePathsByDevice",
                retained,
                mediaKind: null,
                failures);
            RequireExactText(
                candidate.GetProperty("offlineBehavior"),
                "core-play-requires-no-account-or-network",
                "candidate.offlineBehavior",
                failures);
            ValidateTextMap(candidate.GetProperty("saveLocationsByPlatform"), "candidate.saveLocationsByPlatform", 1024, failures);
            RequireNonnegativeInteger(candidate.GetProperty("coreContentBytes"), "candidate.coreContentBytes", failures);
            RequireNonnegativeInteger(candidate.GetProperty("optionalContentBytes"), "candidate.optionalContentBytes", failures);
            ValidateDocumentationHashes(candidate.GetProperty("documentationSha256"), documentHashes, failures);
            ValidatePathMap(
                candidate.GetProperty("screenshotPathsByRole"),
                ScreenshotRoles,
                "candidate.screenshotPathsByRole",
                retained,
                "image",
                failures);
            ValidatePathMap(
                candidate.GetProperty("videoPathsByRole"),
                VideoRoles,
                "candidate.videoPathsByRole",
                retained,
                "video",
                failures);
            ValidateClaims(candidate.GetProperty("marketingClaims"), retained, failures);
            ValidateRetainedHashes(candidate.GetProperty("retainedFileSha256"), retained, failures);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add(SingleLine(exception.Message));
        }
        finally
        {
            trustedRetainedPaths = retained?.ResolvedPaths.ToArray() ?? [];
            document?.Dispose();
        }
    }

    private static void ValidateDigestMap(
        JsonElement value,
        IReadOnlyList<string> keys,
        string label,
        List<string> failures)
    {
        if (!RequireExactFields(value, keys.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return;
        }

        foreach (var key in keys)
        {
            var digest = RequireText(value.GetProperty(key), $"{label}.{key}", failures);
            if (digest is not null && !IsLowerHex(digest, 64))
            {
                failures.Add($"{label}.{key} must be a lowercase SHA-256 digest");
            }
        }
    }

    private static void ValidateIntegerMap(
        JsonElement value,
        string label,
        bool positive,
        List<string> failures)
    {
        if (!RequireExactFields(
            value,
            ArtifactPlatforms.ToHashSet(StringComparer.Ordinal),
            label,
            failures))
        {
            return;
        }

        foreach (var platform in ArtifactPlatforms)
        {
            var item = value.GetProperty(platform);
            if (item.ValueKind != JsonValueKind.Number
                || !item.TryGetInt64(out var number)
                || (positive ? number <= 0 : number < 0))
            {
                failures.Add($"{label}.{platform} must be a {(positive ? "positive" : "nonnegative")} integer byte count");
            }
        }
    }

    private static void ValidateOperatingSystems(JsonElement value, List<string> failures)
    {
        const string label = "candidate.supportedOperatingSystemsByPlatform";
        if (!RequireExactFields(value, ArtifactPlatforms.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return;
        }

        foreach (var platform in ArtifactPlatforms)
        {
            var versions = value.GetProperty(platform);
            if (versions.ValueKind != JsonValueKind.Array
                || versions.GetArrayLength() is < 1 or > 16)
            {
                failures.Add($"{label}.{platform} must contain 1 through 16 operating-system values");
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var item in versions.EnumerateArray())
            {
                var text = RequireText(item, $"{label}.{platform}[{index}]", failures, 256);
                if (text is not null && !seen.Add(text))
                {
                    failures.Add($"{label}.{platform} repeats value: {text}");
                }

                index++;
            }
        }
    }

    private static void ValidateTextMap(
        JsonElement value,
        string label,
        int maximumCharacters,
        List<string> failures)
    {
        if (!RequireExactFields(value, ArtifactPlatforms.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return;
        }

        foreach (var platform in ArtifactPlatforms)
        {
            _ = RequireText(value.GetProperty(platform), $"{label}.{platform}", failures, maximumCharacters);
        }
    }

    private static void ValidateDocumentationHashes(
        JsonElement value,
        IReadOnlyDictionary<string, string> expected,
        List<string> failures)
    {
        const string label = "candidate.documentationSha256";
        if (!RequireExactFields(value, RequiredDocumentPaths.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return;
        }

        foreach (var path in RequiredDocumentPaths)
        {
            var digest = RequireText(value.GetProperty(path), $"{label}.{path}", failures);
            if (digest is null)
            {
                continue;
            }

            if (!IsLowerHex(digest, 64))
            {
                failures.Add($"{label}.{path} must be a lowercase SHA-256 digest");
            }
            else if (!expected.TryGetValue(path, out var expectedDigest)
                || !string.Equals(digest, expectedDigest, StringComparison.Ordinal))
            {
                failures.Add($"candidate documentation hash mismatch: {path}");
            }
        }
    }

    private static void ValidatePathMap(
        JsonElement value,
        IReadOnlyList<string> keys,
        string label,
        RetainedFiles retained,
        string? mediaKind,
        List<string> failures)
    {
        if (!RequireExactFields(value, keys.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return;
        }

        foreach (var key in keys)
        {
            var paths = ValidatePathArray(value.GetProperty(key), $"{label}.{key}", retained, failures);
            foreach (var relativePath in paths)
            {
                if (mediaKind is not null)
                {
                    retained.ExpectMedia(relativePath, mediaKind, $"{label}.{key}");
                }
            }
        }
    }

    private static List<string> ValidatePathArray(
        JsonElement value,
        string label,
        RetainedFiles retained,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is < 1 or > MaximumPathsPerEvidenceSet)
        {
            failures.Add(
                $"{label} must contain 1 through {MaximumPathsPerEvidenceSet} unique safe relative paths");
            return [];
        }

        var result = new List<string>();
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                failures.Add($"{label}[{index}] must be a safe relative path string");
                index++;
                continue;
            }

            var relativePath = item.GetString()!;
            if (!IsSafeRelativePath(relativePath, out var pathFailure))
            {
                failures.Add($"{label}[{index}] {pathFailure}");
                index++;
                continue;
            }

            if (!exact.Add(relativePath) || !folded.Add(relativePath))
            {
                failures.Add($"{label} repeats a path or portable case variant: {relativePath}");
                index++;
                continue;
            }

            result.Add(relativePath);
            retained.Add(relativePath);
            index++;
        }

        return result;
    }

    private static void ValidateClaims(
        JsonElement value,
        RetainedFiles retained,
        List<string> failures)
    {
        const string label = "candidate.marketingClaims";
        if (value.ValueKind != JsonValueKind.Array)
        {
            failures.Add($"{label} must be an array");
            return;
        }

        if (value.GetArrayLength() > MarketingClaimIds.Length)
        {
            failures.Add($"{label} cannot contain more than {MarketingClaimIds.Length} claims");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var claim in value.EnumerateArray())
        {
            var itemLabel = $"{label}[{index}]";
            if (!RequireExactFields(
                claim,
                MarketingClaimFields.ToHashSet(StringComparer.Ordinal),
                itemLabel,
                failures))
            {
                index++;
                continue;
            }

            var claimId = RequireText(claim.GetProperty("claimId"), $"{itemLabel}.claimId", failures);
            if (claimId is not null
                && (!MarketingClaimIds.Contains(claimId, StringComparer.Ordinal)
                    || !seen.Add(claimId)))
            {
                failures.Add($"{itemLabel}.claimId must be unique and supported");
            }

            _ = RequireText(claim.GetProperty("statement"), $"{itemLabel}.statement", failures);
            _ = ValidatePathArray(claim.GetProperty("evidencePaths"), $"{itemLabel}.evidencePaths", retained, failures);
            index++;
        }

        if (!seen.SetEquals(MarketingClaimIds))
        {
            failures.Add("candidate.marketingClaims must cover every permitted claim");
        }
    }

    private static void ValidateRetainedHashes(
        JsonElement value,
        RetainedFiles retained,
        List<string> failures)
    {
        const string label = "candidate.retainedFileSha256";
        var expectedPaths = retained.RelativePaths.ToHashSet(StringComparer.Ordinal);
        if (!RequireExactFields(value, expectedPaths, label, failures))
        {
            return;
        }

        foreach (var relativePath in retained.RelativePaths.Order(StringComparer.Ordinal))
        {
            if (!retained.TryGet(relativePath, out var file))
            {
                continue;
            }

            var expected = RequireText(value.GetProperty(relativePath), $"{label}.{relativePath}", failures);
            if (expected is null)
            {
                continue;
            }

            if (!IsLowerHex(expected, 64))
            {
                failures.Add($"{label}.{relativePath} must be a lowercase SHA-256 digest");
                continue;
            }

            try
            {
                var actual = HashAndValidateStableFile(file, failures);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    failures.Add($"candidate retained file hash mismatch: {relativePath}");
                }
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                failures.Add($"{relativePath}: {SingleLine(exception.Message)}");
            }
        }
    }

    private static void ValidateMedia(
        RetainedFile file,
        string snapshotPath,
        string mediaKind,
        string label,
        List<string> failures)
    {
        var extension = Path.GetExtension(file.RelativePath).ToLowerInvariant();
        try
        {
            if (mediaKind == "image")
            {
                if (file.Length > MaximumImageBytes)
                {
                    throw new InvalidDataException(
                        $"image exceeds the {MaximumImageBytes}-byte validation limit");
                }

                switch (extension)
                {
                    case ".png":
                        var pngFailure = ContentInventoryCheck.ValidatePngForRepositoryCheck(snapshotPath);
                        if (pngFailure is not null)
                        {
                            throw new InvalidDataException(pngFailure);
                        }

                        break;
                    case ".jpg":
                    case ".jpeg":
                        ValidateJpeg(snapshotPath, file.Length);
                        break;
                    default:
                        throw new InvalidDataException("screenshots must use PNG or JPEG files");
                }
            }
            else
            {
                switch (extension)
                {
                    case ".mp4":
                        ValidateMp4(snapshotPath, file.Length);
                        break;
                    case ".webm":
                        ValidateWebm(snapshotPath, file.Length);
                        break;
                    default:
                        throw new InvalidDataException("videos must use MP4 or WebM files");
                }
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add($"{label} is not a recognized retained {mediaKind} file: {file.RelativePath}: {SingleLine(exception.Message)}");
        }
    }

    private static void ValidateJpeg(string path, long length)
    {
        using var source = OpenRead(path);
        if (length < 8 || ReadByte(source) != 0xff || ReadByte(source) != 0xd8)
        {
            throw new InvalidDataException("JPEG SOI marker is missing");
        }

        var frameComponents = new HashSet<int>();
        var frameQuantizationTables = new HashSet<int>();
        var quantizationTables = new HashSet<int>();
        var dcHuffmanTables = new HashSet<int>();
        var acHuffmanTables = new HashSet<int>();
        var sawFrame = false;
        var sawScan = false;
        ushort restartInterval = 0;
        var segments = 0;
        int? pendingMarker = null;
        while (source.Position < length || pendingMarker is not null)
        {
            if (++segments > 4096)
            {
                throw new InvalidDataException("JPEG exceeds the 4096-segment limit");
            }

            var marker = pendingMarker ?? ReadJpegMarker(source);
            pendingMarker = null;
            if (marker == 0xd9)
            {
                if (!sawFrame
                    || !sawScan
                    || !frameQuantizationTables.IsSubsetOf(quantizationTables)
                    || source.Position != length)
                {
                    throw new InvalidDataException("JPEG has an incomplete frame or trailing bytes");
                }

                return;
            }

            if (marker is >= 0xd0 and <= 0xd7 or 0x01 or 0xd8)
            {
                throw new InvalidDataException("JPEG contains a standalone marker outside scan data");
            }

            var segmentLength = ReadUInt16BigEndian(source);
            if (segmentLength < 2 || source.Position + segmentLength - 2 > length)
            {
                throw new InvalidDataException("JPEG segment length exceeds the file");
            }

            var payloadLength = segmentLength - 2;
            if (sawScan)
            {
                throw new InvalidDataException("JPEG baseline scan must terminate at EOI");
            }

            if (marker == 0xdb)
            {
                ReadJpegQuantizationTables(source, payloadLength, quantizationTables);
                continue;
            }

            if (marker == 0xc4)
            {
                ReadJpegHuffmanTables(
                    source,
                    payloadLength,
                    dcHuffmanTables,
                    acHuffmanTables);
                continue;
            }

            if (marker == 0xdd)
            {
                if (payloadLength != 2)
                {
                    throw new InvalidDataException("JPEG DRI segment is malformed");
                }

                restartInterval = ReadUInt16BigEndian(source);
                continue;
            }

            if (IsStartOfFrame(marker))
            {
                if (marker != 0xc0 || sawFrame || payloadLength < 9)
                {
                    throw new InvalidDataException(
                        "JPEG must contain one baseline sequential frame header");
                }

                if (ReadByte(source) != 8)
                {
                    throw new InvalidDataException("JPEG baseline sample precision must be 8 bits");
                }

                var height = ReadUInt16BigEndian(source);
                var width = ReadUInt16BigEndian(source);
                if (width == 0 || height == 0 || width > 16_384 || height > 16_384
                    || (ulong)width * height > 67_108_864)
                {
                    throw new InvalidDataException("JPEG dimensions exceed the supported bounds");
                }

                var componentCount = ReadByte(source);
                if (componentCount is < 1 or > 4
                    || payloadLength != 6 + (3 * componentCount))
                {
                    throw new InvalidDataException("JPEG frame components are malformed");
                }

                for (var index = 0; index < componentCount; index++)
                {
                    var componentId = ReadByte(source);
                    var sampling = ReadByte(source);
                    var horizontalSampling = sampling >> 4;
                    var verticalSampling = sampling & 0x0f;
                    var quantizationTable = ReadByte(source);
                    if (!frameComponents.Add(componentId)
                        || horizontalSampling is < 1 or > 4
                        || verticalSampling is < 1 or > 4
                        || quantizationTable > 3)
                    {
                        throw new InvalidDataException("JPEG frame components are malformed");
                    }

                    frameQuantizationTables.Add(quantizationTable);
                }

                sawFrame = true;
                continue;
            }

            if (marker != 0xda)
            {
                Skip(source, payloadLength);
                continue;
            }

            if (!sawFrame)
            {
                throw new InvalidDataException("JPEG scan appears before a frame header");
            }

            ReadJpegScanHeader(
                source,
                payloadLength,
                frameComponents,
                dcHuffmanTables,
                acHuffmanTables);
            if (!frameQuantizationTables.IsSubsetOf(quantizationTables))
            {
                throw new InvalidDataException("JPEG frame references a missing quantization table");
            }

            sawScan = true;
            var entropyBytes = 0L;
            while (source.Position < length)
            {
                var value = ReadByte(source);
                if (value != 0xff)
                {
                    entropyBytes++;
                    continue;
                }

                var next = ReadByte(source);
                while (next == 0xff)
                {
                    next = ReadByte(source);
                }

                if (next == 0x00)
                {
                    entropyBytes++;
                    continue;
                }

                if (next is >= 0xd0 and <= 0xd7)
                {
                    if (restartInterval == 0)
                    {
                        throw new InvalidDataException(
                            "JPEG restart marker requires a nonzero DRI interval");
                    }

                    continue;
                }

                if (entropyBytes == 0)
                {
                    throw new InvalidDataException("JPEG scan contains no entropy-coded data");
                }

                pendingMarker = next;
                break;
            }
        }

        throw new InvalidDataException("JPEG EOI marker is missing");
    }

    private static void ReadJpegQuantizationTables(
        Stream source,
        int payloadLength,
        HashSet<int> tables)
    {
        var remaining = payloadLength;
        while (remaining > 0)
        {
            var descriptor = ReadByte(source);
            remaining--;
            var precision = descriptor >> 4;
            var tableId = descriptor & 0x0f;
            var tableBytes = precision switch
            {
                0 => 64,
                1 => 128,
                _ => throw new InvalidDataException("JPEG quantization table precision is invalid"),
            };
            if (tableId > 3 || remaining < tableBytes)
            {
                throw new InvalidDataException("JPEG quantization table is malformed");
            }

            for (var index = 0; index < 64; index++)
            {
                var value = precision == 0
                    ? ReadByte(source)
                    : ReadUInt16BigEndian(source);
                if (value == 0)
                {
                    throw new InvalidDataException(
                        "JPEG quantization table values must be nonzero");
                }
            }

            remaining -= tableBytes;
            tables.Add(tableId);
        }
    }

    private static void ReadJpegHuffmanTables(
        Stream source,
        int payloadLength,
        HashSet<int> dcTables,
        HashSet<int> acTables)
    {
        var remaining = payloadLength;
        while (remaining > 0)
        {
            if (remaining < 17)
            {
                throw new InvalidDataException("JPEG Huffman table is truncated");
            }

            var descriptor = ReadByte(source);
            remaining--;
            var tableClass = descriptor >> 4;
            var tableId = descriptor & 0x0f;
            if (tableClass > 1 || tableId > 3)
            {
                throw new InvalidDataException("JPEG Huffman table identity is invalid");
            }

            var symbolCount = 0;
            var availableCodes = 1;
            for (var length = 1; length <= 16; length++)
            {
                var count = ReadByte(source);
                symbolCount += count;
                availableCodes = (availableCodes * 2) - count;
                if (availableCodes < 0)
                {
                    throw new InvalidDataException("JPEG Huffman table is oversubscribed");
                }
            }

            remaining -= 16;
            if (symbolCount is < 1 or > 256 || remaining < symbolCount)
            {
                throw new InvalidDataException("JPEG Huffman table symbols are malformed");
            }

            Skip(source, symbolCount);
            remaining -= symbolCount;
            (tableClass == 0 ? dcTables : acTables).Add(tableId);
        }
    }

    private static void ReadJpegScanHeader(
        Stream source,
        int payloadLength,
        HashSet<int> frameComponents,
        HashSet<int> dcTables,
        HashSet<int> acTables)
    {
        if (payloadLength < 6)
        {
            throw new InvalidDataException("JPEG scan header is truncated");
        }

        var componentCount = ReadByte(source);
        if (componentCount is < 1 or > 4
            || componentCount != frameComponents.Count
            || payloadLength != 1 + (2 * componentCount) + 3)
        {
            throw new InvalidDataException("JPEG scan components are malformed");
        }

        var seen = new HashSet<int>();
        for (var index = 0; index < componentCount; index++)
        {
            var componentId = ReadByte(source);
            var selectors = ReadByte(source);
            var dcTable = selectors >> 4;
            var acTable = selectors & 0x0f;
            if (!frameComponents.Contains(componentId)
                || !seen.Add(componentId)
                || !dcTables.Contains(dcTable)
                || !acTables.Contains(acTable))
            {
                throw new InvalidDataException(
                    "JPEG scan references an unknown component or Huffman table");
            }
        }

        if (ReadByte(source) != 0 || ReadByte(source) != 63 || ReadByte(source) != 0)
        {
            throw new InvalidDataException("JPEG baseline scan parameters are invalid");
        }
    }

    private static void ValidateMp4(string path, long length)
    {
        using var source = OpenRead(path);
        var sawFtyp = false;
        var sawMoov = false;
        var sawMdat = false;
        var boxes = 0;
        Mp4SampleTable? videoTable = null;
        var mediaData = new List<(long Start, long End)>();
        while (source.Position < length)
        {
            var box = ReadMp4Box(source, length, ref boxes);
            switch (box.Type)
            {
                case "ftyp":
                    if (sawFtyp || box.Start != 0)
                    {
                        throw new InvalidDataException("MP4 ftyp box is missing or malformed");
                    }

                    ValidateMp4FileType(source, box);
                    sawFtyp = true;
                    break;
                case "moov":
                    if (sawMoov)
                    {
                        throw new InvalidDataException("MP4 contains multiple moov boxes");
                    }

                    videoTable = ValidateMp4Movie(source, box, ref boxes);
                    sawMoov = true;
                    break;
                case "mdat":
                    if (box.PayloadLength == 0)
                    {
                        throw new InvalidDataException("MP4 mdat box must be nonempty");
                    }

                    sawMdat = true;
                    mediaData.Add((box.PayloadStart, box.End));
                    break;
            }

            source.Position = box.End;
        }

        if (!sawFtyp
            || !sawMoov
            || !sawMdat
            || videoTable is null
            || source.Position != length)
        {
            throw new InvalidDataException(
                "MP4 must contain bounded ftyp, moov, video-track, sample-table, and mdat structure");
        }

        if (videoTable.Chunks.Any(chunk =>
                !mediaData.Any(range =>
                    chunk.Offset >= (ulong)range.Start
                    && chunk.Offset < (ulong)range.End
                    && chunk.Length <= (ulong)range.End - chunk.Offset)))
        {
            throw new InvalidDataException(
                "MP4 video chunk byte extents must each fit inside one mdat payload");
        }
    }

    private static Mp4Box ReadMp4Box(Stream source, long containerEnd, ref int boxes)
    {
        if (++boxes > MaximumMediaElements)
        {
            throw new InvalidDataException("MP4 exceeds the box validation limit");
        }

        var start = source.Position;
        if (containerEnd - start < 8)
        {
            throw new InvalidDataException("MP4 box header is truncated");
        }

        ulong size = ReadUInt32BigEndian(source);
        Span<byte> typeBytes = stackalloc byte[4];
        ReadExact(source, typeBytes);
        if (!IsPrintableMp4Type(typeBytes))
        {
            throw new InvalidDataException("MP4 box type is invalid");
        }

        var type = Encoding.ASCII.GetString(typeBytes);
        var headerSize = 8UL;
        if (size == 1)
        {
            size = ReadUInt64BigEndian(source);
            headerSize = 16;
        }
        else if (size == 0)
        {
            size = checked((ulong)(containerEnd - start));
        }

        if (size < headerSize || size > checked((ulong)(containerEnd - start)))
        {
            throw new InvalidDataException("MP4 box size exceeds its container");
        }

        var end = checked(start + (long)size);
        return new Mp4Box(type, start, source.Position, end, checked((long)(size - headerSize)));
    }

    private static bool IsPrintableMp4Type(ReadOnlySpan<byte> value)
    {
        foreach (var item in value)
        {
            if (item is < 0x20 or > 0x7e)
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateMp4FileType(Stream source, Mp4Box box)
    {
        if (box.PayloadLength < 8 || (box.PayloadLength - 8) % 4 != 0)
        {
            throw new InvalidDataException("MP4 ftyp box is missing or malformed");
        }

        var brands = new HashSet<string>(StringComparer.Ordinal);
        Span<byte> brand = stackalloc byte[4];
        ReadExact(source, brand);
        brands.Add(Encoding.ASCII.GetString(brand));
        _ = ReadUInt32BigEndian(source);
        while (source.Position < box.End)
        {
            ReadExact(source, brand);
            brands.Add(Encoding.ASCII.GetString(brand));
        }

        if (!brands.Overlaps(RecognizedMp4Brands))
        {
            throw new InvalidDataException("MP4 ftyp does not declare a recognized media brand");
        }
    }

    private static Mp4SampleTable ValidateMp4Movie(Stream source, Mp4Box movie, ref int boxes)
    {
        var sawHeader = false;
        Mp4SampleTable? videoTable = null;
        while (source.Position < movie.End)
        {
            var child = ReadMp4Box(source, movie.End, ref boxes);
            switch (child.Type)
            {
                case "mvhd":
                    ValidateMp4TimedHeader(source, child, "movie");
                    sawHeader = true;
                    break;
                case "trak":
                    var trackTable = ValidateMp4Track(source, child, ref boxes);
                    videoTable ??= trackTable;
                    break;
            }

            source.Position = child.End;
        }

        if (!sawHeader || videoTable is null || source.Position != movie.End)
        {
            throw new InvalidDataException("MP4 moov must contain mvhd and a video trak");
        }

        return videoTable;
    }

    private static Mp4SampleTable? ValidateMp4Track(Stream source, Mp4Box track, ref int boxes)
    {
        var sawHeader = false;
        Mp4SampleTable? videoTable = null;
        while (source.Position < track.End)
        {
            var child = ReadMp4Box(source, track.End, ref boxes);
            switch (child.Type)
            {
                case "tkhd":
                    ValidateMp4TrackHeader(source, child);
                    sawHeader = true;
                    break;
                case "mdia":
                    videoTable = ValidateMp4Media(source, child, ref boxes);
                    break;
            }

            source.Position = child.End;
        }

        if (!sawHeader || source.Position != track.End)
        {
            throw new InvalidDataException("MP4 trak must contain a valid tkhd");
        }

        return videoTable;
    }

    private static Mp4SampleTable? ValidateMp4Media(Stream source, Mp4Box media, ref int boxes)
    {
        var sawHeader = false;
        var videoHandler = false;
        Mp4SampleTable? sampleTable = null;
        while (source.Position < media.End)
        {
            var child = ReadMp4Box(source, media.End, ref boxes);
            switch (child.Type)
            {
                case "mdhd":
                    ValidateMp4TimedHeader(source, child, "media");
                    sawHeader = true;
                    break;
                case "hdlr":
                    videoHandler = ValidateMp4Handler(source, child);
                    break;
                case "minf":
                    sampleTable = ValidateMp4MediaInformation(source, child, ref boxes);
                    break;
            }

            source.Position = child.End;
        }

        if (!sawHeader || source.Position != media.End)
        {
            throw new InvalidDataException("MP4 mdia must contain valid mdhd and hdlr structure");
        }

        return videoHandler ? sampleTable : null;
    }

    private static Mp4SampleTable? ValidateMp4MediaInformation(
        Stream source,
        Mp4Box mediaInformation,
        ref int boxes)
    {
        Mp4SampleTable? table = null;
        while (source.Position < mediaInformation.End)
        {
            var child = ReadMp4Box(source, mediaInformation.End, ref boxes);
            if (child.Type == "stbl")
            {
                table = ValidateMp4SampleTable(source, child, ref boxes);
            }

            source.Position = child.End;
        }

        return table;
    }

    private static Mp4SampleTable ValidateMp4SampleTable(Stream source, Mp4Box table, ref int boxes)
    {
        HashSet<uint>? recognizedVisualDescriptions = null;
        uint timingSampleCount = 0;
        uint[]? sampleSizes = null;
        List<Mp4SampleToChunk>? sampleToChunk = null;
        List<ulong>? chunkOffsets = null;
        while (source.Position < table.End)
        {
            var child = ReadMp4Box(source, table.End, ref boxes);
            switch (child.Type)
            {
                case "stsd":
                    recognizedVisualDescriptions = ValidateMp4SampleDescriptions(
                        source,
                        child,
                        ref boxes);
                    break;
                case "stts":
                    timingSampleCount = ValidateMp4TimeToSample(source, child);
                    break;
                case "stsc":
                    sampleToChunk = ValidateMp4SampleToChunk(source, child);
                    break;
                case "stsz":
                    sampleSizes = ValidateMp4SampleSizes(source, child);
                    break;
                case "stco":
                    chunkOffsets = ValidateMp4ChunkOffsets(source, child, large: false);
                    break;
                case "co64":
                    chunkOffsets = ValidateMp4ChunkOffsets(source, child, large: true);
                    break;
            }

            source.Position = child.End;
        }

        if (recognizedVisualDescriptions is null
            || timingSampleCount == 0
            || sampleSizes is null
            || timingSampleCount != sampleSizes.Length
            || sampleToChunk is null
            || chunkOffsets is null)
        {
            throw new InvalidDataException("MP4 video sample tables are incomplete or inconsistent");
        }

        if (sampleToChunk.Any(entry =>
                !recognizedVisualDescriptions.Contains(entry.DescriptionIndex)))
        {
            throw new InvalidDataException(
                "MP4 sample chunks must reference recognized visual sample descriptions");
        }

        var chunkLengths = BuildMp4ChunkByteLengths(
            sampleToChunk,
            checked((uint)chunkOffsets.Count),
            sampleSizes);
        if (chunkLengths is null)
        {
            throw new InvalidDataException("MP4 video sample tables are incomplete or inconsistent");
        }

        return new Mp4SampleTable(
            chunkOffsets.Zip(
                chunkLengths,
                static (offset, length) => new Mp4ChunkExtent(offset, length)).ToArray());
    }

    private static void ValidateMp4TimedHeader(Stream source, Mp4Box box, string label)
    {
        if (box.PayloadLength < 20 || ReadByte(source) != 0)
        {
            throw new InvalidDataException($"MP4 {label} header is unsupported or truncated");
        }

        Skip(source, 11);
        var timescale = ReadUInt32BigEndian(source);
        var duration = ReadUInt32BigEndian(source);
        if (timescale == 0 || duration == 0)
        {
            throw new InvalidDataException($"MP4 {label} timing must be positive");
        }
    }

    private static void ValidateMp4TrackHeader(Stream source, Mp4Box box)
    {
        if (box.PayloadLength < 24 || ReadByte(source) != 0)
        {
            throw new InvalidDataException("MP4 track header is unsupported or truncated");
        }

        Skip(source, 11);
        var trackId = ReadUInt32BigEndian(source);
        Skip(source, 4);
        var duration = ReadUInt32BigEndian(source);
        if (trackId == 0 || duration == 0)
        {
            throw new InvalidDataException("MP4 track identity and duration must be positive");
        }
    }

    private static bool ValidateMp4Handler(Stream source, Mp4Box box)
    {
        if (box.PayloadLength < 12)
        {
            throw new InvalidDataException("MP4 handler is truncated");
        }

        Skip(source, 8);
        Span<byte> handler = stackalloc byte[4];
        ReadExact(source, handler);
        return handler.SequenceEqual("vide"u8);
    }

    private static HashSet<uint> ValidateMp4SampleDescriptions(
        Stream source,
        Mp4Box box,
        ref int boxes)
    {
        if (box.PayloadLength < 16)
        {
            throw new InvalidDataException("MP4 stsd is truncated");
        }

        Skip(source, 4);
        var count = ReadUInt32BigEndian(source);
        if (count is < 1 or > 32)
        {
            throw new InvalidDataException("MP4 stsd entry count is invalid");
        }

        var recognizedVideo = new HashSet<uint>();
        for (var index = 0U; index < count; index++)
        {
            var entry = ReadMp4Box(source, box.End, ref boxes);
            var recognized = RecognizedMp4VideoSampleEntries.Contains(entry.Type);
            if (recognized)
            {
                ValidateMp4VisualSampleEntry(source, entry, ref boxes);
                recognizedVideo.Add(index + 1);
            }

            source.Position = entry.End;
        }

        if (recognizedVideo.Count == 0 || source.Position != box.End)
        {
            throw new InvalidDataException("MP4 stsd lacks a bounded video sample entry");
        }

        return recognizedVideo;
    }

    private static void ValidateMp4VisualSampleEntry(
        Stream source,
        Mp4Box entry,
        ref int boxes)
    {
        if (entry.PayloadLength < 78)
        {
            throw new InvalidDataException("MP4 video sample entry is truncated");
        }

        Skip(source, 6);
        var dataReference = ReadUInt16BigEndian(source);
        Skip(source, 16);
        var width = ReadUInt16BigEndian(source);
        var height = ReadUInt16BigEndian(source);
        Skip(source, 12);
        var frameCount = ReadUInt16BigEndian(source);
        Skip(source, 32);
        var depth = ReadUInt16BigEndian(source);
        var reserved = ReadUInt16BigEndian(source);
        if (dataReference == 0
            || width == 0
            || height == 0
            || (ulong)width * height > 67_108_864
            || frameCount == 0
            || depth == 0
            || reserved != ushort.MaxValue)
        {
            throw new InvalidDataException("MP4 video sample entry fields are invalid");
        }

        var configuration = false;
        while (source.Position < entry.End)
        {
            var child = ReadMp4Box(source, entry.End, ref boxes);
            if (child.Type == "avcC")
            {
                ValidateAvcConfiguration(source, child);
                configuration = true;
            }

            source.Position = child.End;
        }

        if (!configuration || source.Position != entry.End)
        {
            throw new InvalidDataException("MP4 avc1 sample entry lacks a bounded avcC configuration");
        }
    }

    private static void ValidateAvcConfiguration(Stream source, Mp4Box configuration)
    {
        if (configuration.PayloadLength < 11
            || ReadByte(source) != 1
            || ReadByte(source) == 0)
        {
            throw new InvalidDataException("MP4 avcC header is unsupported or truncated");
        }

        _ = ReadByte(source);
        if (ReadByte(source) == 0)
        {
            throw new InvalidDataException("MP4 avcC level must be positive");
        }

        var lengthDescriptor = ReadByte(source);
        var sequenceDescriptor = ReadByte(source);
        var sequenceCount = sequenceDescriptor & 0x1f;
        if ((lengthDescriptor & 0xfc) != 0xfc
            || (lengthDescriptor & 0x03) == 2
            || (sequenceDescriptor & 0xe0) != 0xe0
            || sequenceCount == 0)
        {
            throw new InvalidDataException("MP4 avcC length or sequence descriptor is invalid");
        }

        for (var index = 0; index < sequenceCount; index++)
        {
            ValidateAvcParameterSet(source, configuration.End, expectedType: 7, "sequence");
        }

        var pictureCount = ReadByte(source);
        if (pictureCount == 0)
        {
            throw new InvalidDataException("MP4 avcC requires a picture parameter set");
        }

        for (var index = 0; index < pictureCount; index++)
        {
            ValidateAvcParameterSet(source, configuration.End, expectedType: 8, "picture");
        }

        if (source.Position != configuration.End)
        {
            throw new InvalidDataException("MP4 avcC contains trailing configuration bytes");
        }
    }

    private static void ValidateAvcParameterSet(
        Stream source,
        long end,
        int expectedType,
        string label)
    {
        var length = ReadUInt16BigEndian(source);
        if (length == 0 || source.Position + length > end)
        {
            throw new InvalidDataException($"MP4 avcC {label} parameter set is truncated");
        }

        if ((ReadByte(source) & 0x1f) != expectedType)
        {
            throw new InvalidDataException($"MP4 avcC {label} parameter set identity is invalid");
        }

        Skip(source, length - 1);
    }

    private static uint ValidateMp4TimeToSample(Stream source, Mp4Box box)
    {
        var count = ReadMp4TableCount(source, box, 8, "stts");
        ulong samples = 0;
        for (var index = 0U; index < count; index++)
        {
            var sampleCount = ReadUInt32BigEndian(source);
            var delta = ReadUInt32BigEndian(source);
            if (sampleCount == 0 || delta == 0)
            {
                throw new InvalidDataException("MP4 stts entries must be positive");
            }

            samples = checked(samples + sampleCount);
        }

        return samples is < 1 or > MaximumMediaElements
            ? throw new InvalidDataException("MP4 sample count exceeds the validation limit")
            : checked((uint)samples);
    }

    private static List<Mp4SampleToChunk> ValidateMp4SampleToChunk(Stream source, Mp4Box box)
    {
        var count = ReadMp4TableCount(source, box, 12, "stsc");
        var result = new List<Mp4SampleToChunk>(checked((int)count));
        for (var index = 0U; index < count; index++)
        {
            var entry = new Mp4SampleToChunk(
                ReadUInt32BigEndian(source),
                ReadUInt32BigEndian(source),
                ReadUInt32BigEndian(source));
            if (entry.FirstChunk == 0
                || entry.SamplesPerChunk == 0
                || entry.DescriptionIndex == 0
                || (result.Count > 0 && entry.FirstChunk <= result[^1].FirstChunk))
            {
                throw new InvalidDataException("MP4 stsc entries are invalid");
            }

            result.Add(entry);
        }

        return result;
    }

    private static uint[] ValidateMp4SampleSizes(Stream source, Mp4Box box)
    {
        if (box.PayloadLength < 12)
        {
            throw new InvalidDataException("MP4 stsz is truncated");
        }

        Skip(source, 4);
        var fixedSize = ReadUInt32BigEndian(source);
        var count = ReadUInt32BigEndian(source);
        if (count is < 1 or > MaximumMediaElements)
        {
            throw new InvalidDataException("MP4 stsz sample count exceeds the validation limit");
        }

        var result = new uint[checked((int)count)];
        if (fixedSize > 0)
        {
            if (source.Position != box.End)
            {
                throw new InvalidDataException("MP4 fixed stsz contains trailing entries");
            }

            Array.Fill(result, fixedSize);
        }
        else
        {
            if (box.End - source.Position != checked(4L * count))
            {
                throw new InvalidDataException("MP4 variable stsz length is inconsistent");
            }

            for (var index = 0U; index < count; index++)
            {
                var size = ReadUInt32BigEndian(source);
                if (size == 0)
                {
                    throw new InvalidDataException("MP4 sample size must be positive");
                }

                result[index] = size;
            }
        }

        return result;
    }

    private static List<ulong> ValidateMp4ChunkOffsets(Stream source, Mp4Box box, bool large)
    {
        var width = large ? 8 : 4;
        var count = ReadMp4TableCount(source, box, width, large ? "co64" : "stco");
        var result = new List<ulong>(checked((int)count));
        for (var index = 0U; index < count; index++)
        {
            var offset = large ? ReadUInt64BigEndian(source) : ReadUInt32BigEndian(source);
            if (offset == 0)
            {
                throw new InvalidDataException("MP4 chunk offset must be positive");
            }

            result.Add(offset);
        }

        return result;
    }

    private static uint ReadMp4TableCount(Stream source, Mp4Box box, int entryBytes, string label)
    {
        if (box.PayloadLength < 8)
        {
            throw new InvalidDataException($"MP4 {label} is truncated");
        }

        Skip(source, 4);
        var count = ReadUInt32BigEndian(source);
        if (count is < 1 or > MaximumMediaElements
            || box.End - source.Position != checked((long)entryBytes * count))
        {
            throw new InvalidDataException($"MP4 {label} entry count is inconsistent");
        }

        return count;
    }

    private static List<ulong>? BuildMp4ChunkByteLengths(
        IReadOnlyList<Mp4SampleToChunk> entries,
        uint chunkCount,
        uint[] sampleSizes)
    {
        if (chunkCount == 0
            || entries.Count == 0
            || entries[0].FirstChunk != 1
            || entries[^1].FirstChunk > chunkCount)
        {
            return null;
        }

        var result = new List<ulong>(checked((int)chunkCount));
        var entryIndex = 0;
        var sampleIndex = 0;
        for (var chunk = 1U; chunk <= chunkCount; chunk++)
        {
            while (entryIndex + 1 < entries.Count
                && entries[entryIndex + 1].FirstChunk <= chunk)
            {
                entryIndex++;
            }

            var entry = entries[entryIndex];
            ulong chunkBytes = 0;
            for (var sample = 0U; sample < entry.SamplesPerChunk; sample++)
            {
                if (sampleIndex >= sampleSizes.Length)
                {
                    return null;
                }

                chunkBytes = checked(chunkBytes + sampleSizes[sampleIndex]);
                sampleIndex++;
            }

            result.Add(chunkBytes);
        }

        return sampleIndex == sampleSizes.Length ? result : null;
    }

    private static void ValidateWebm(string path, long length)
    {
        using var source = OpenRead(path);
        var elements = 0;
        var header = ReadWebmElement(source, length, ref elements);
        if (header.Id != 0x1a45dfa3 || header.Size == 0)
        {
            throw new InvalidDataException("WebM EBML header is missing");
        }

        var headerEnd = checked(source.Position + (long)header.Size);
        var ebmlVersion = false;
        var ebmlReadVersion = false;
        var maximumIdLength = false;
        var maximumSizeLength = false;
        var webmDocType = false;
        var docTypeVersion = false;
        var docTypeReadVersion = false;
        while (source.Position < headerEnd)
        {
            var child = ReadWebmElement(source, headerEnd, ref elements);
            switch (child.Id)
            {
                case 0x4286:
                    ebmlVersion = ReadEbmlUnsigned(source, child, 4) == 1;
                    break;
                case 0x42f7:
                    ebmlReadVersion = ReadEbmlUnsigned(source, child, 4) == 1;
                    break;
                case 0x42f2:
                    maximumIdLength = ReadEbmlUnsigned(source, child, 4) is >= 1 and <= 4;
                    break;
                case 0x42f3:
                    maximumSizeLength = ReadEbmlUnsigned(source, child, 8) is >= 1 and <= 8;
                    break;
                case 0x4282:
                    webmDocType = ReadEbmlText(source, child, 16) == "webm";
                    break;
                case 0x4287:
                    docTypeVersion = ReadEbmlUnsigned(source, child, 4) is >= 1 and <= 4;
                    break;
                case 0x4285:
                    docTypeReadVersion = ReadEbmlUnsigned(source, child, 4) is >= 1 and <= 2;
                    break;
                default:
                    source.Position = checked(source.Position + (long)child.Size);
                    break;
            }
        }

        if (!ebmlVersion
            || !ebmlReadVersion
            || !maximumIdLength
            || !maximumSizeLength
            || !webmDocType
            || !docTypeVersion
            || !docTypeReadVersion
            || source.Position != headerEnd)
        {
            throw new InvalidDataException("WebM EBML header fields are incomplete or unsupported");
        }

        var segment = ReadWebmElement(source, length, ref elements);
        if (segment.Id != 0x18538067 || segment.Size == 0)
        {
            throw new InvalidDataException("WebM Segment is missing");
        }

        var segmentEnd = checked(source.Position + (long)segment.Size);
        var info = false;
        HashSet<ulong>? videoTracks = null;
        var cluster = false;
        while (source.Position < segmentEnd)
        {
            var child = ReadWebmElement(source, segmentEnd, ref elements);
            switch (child.Id)
            {
                case 0x1549a966:
                    ValidateWebmInfo(source, child, ref elements);
                    info = true;
                    break;
                case 0x1654ae6b:
                    videoTracks = ValidateWebmTracks(source, child, ref elements);
                    break;
                case 0x1f43b675:
                    if (!info || videoTracks is null)
                    {
                        throw new InvalidDataException(
                            "WebM Info and Tracks must precede Cluster media");
                    }

                    ValidateWebmCluster(source, child, videoTracks, ref elements);
                    cluster = true;
                    break;
                default:
                    source.Position = checked(source.Position + (long)child.Size);
                    break;
            }
        }

        if (!info
            || videoTracks is null
            || videoTracks.Count == 0
            || !cluster
            || source.Position != segmentEnd
            || segmentEnd != length)
        {
            throw new InvalidDataException(
                "WebM must contain bounded Info, video TrackEntry, and keyframe Cluster structure");
        }
    }

    private static EbmlElement ReadWebmElement(FileStream source, long limit, ref int elements)
    {
        if (++elements > MaximumMediaElements)
        {
            throw new InvalidDataException("WebM exceeds the element validation limit");
        }

        return ReadEbmlElement(source, limit);
    }

    private static void ValidateWebmInfo(
        FileStream source,
        EbmlElement info,
        ref int elements)
    {
        var end = checked(source.Position + (long)info.Size);
        var timecodeScale = false;
        var duration = false;
        while (source.Position < end)
        {
            var child = ReadWebmElement(source, end, ref elements);
            switch (child.Id)
            {
                case 0x2ad7b1:
                    timecodeScale = ReadEbmlUnsigned(source, child, 8) is >= 1 and <= 1_000_000_000;
                    break;
                case 0x4489:
                    duration = ReadEbmlFloat(source, child) is > 0 and < double.PositiveInfinity;
                    break;
                default:
                    source.Position = checked(source.Position + (long)child.Size);
                    break;
            }
        }

        if (!timecodeScale || !duration || source.Position != end)
        {
            throw new InvalidDataException("WebM Info requires bounded timecode scale and duration");
        }
    }

    private static HashSet<ulong> ValidateWebmTracks(
        FileStream source,
        EbmlElement tracks,
        ref int elements)
    {
        var end = checked(source.Position + (long)tracks.Size);
        var videoTracks = new HashSet<ulong>();
        var trackNumbers = new HashSet<ulong>();
        var trackUids = new HashSet<ulong>();
        while (source.Position < end)
        {
            var child = ReadWebmElement(source, end, ref elements);
            if (child.Id == 0xae)
            {
                var track = ValidateWebmTrackEntry(source, child, ref elements);
                if (!trackNumbers.Add(track.Number) || !trackUids.Add(track.Uid))
                {
                    throw new InvalidDataException(
                        "WebM TrackNumber and TrackUID must be globally unique and positive");
                }

                if (track.SupportedVideo)
                {
                    videoTracks.Add(track.Number);
                }
            }
            else
            {
                source.Position = checked(source.Position + (long)child.Size);
            }
        }

        if (videoTracks.Count == 0 || source.Position != end)
        {
            throw new InvalidDataException("WebM Tracks lacks a supported video TrackEntry");
        }

        return videoTracks;
    }

    private static WebmTrack ValidateWebmTrackEntry(
        FileStream source,
        EbmlElement entry,
        ref int elements)
    {
        var end = checked(source.Position + (long)entry.Size);
        ulong? trackNumber = null;
        ulong? trackUid = null;
        ulong? trackType = null;
        string? codec = null;
        var dimensions = false;
        while (source.Position < end)
        {
            var child = ReadWebmElement(source, end, ref elements);
            switch (child.Id)
            {
                case 0xd7:
                    trackNumber = ReadEbmlUnsigned(source, child, 8);
                    break;
                case 0x73c5:
                    trackUid = ReadEbmlUnsigned(source, child, 8);
                    break;
                case 0x83:
                    trackType = ReadEbmlUnsigned(source, child, 8);
                    break;
                case 0x86:
                    codec = ReadEbmlText(source, child, 32);
                    break;
                case 0xe0:
                    dimensions = ValidateWebmVideo(source, child, ref elements);
                    break;
                default:
                    source.Position = checked(source.Position + (long)child.Size);
                    break;
            }
        }

        if (source.Position != end)
        {
            throw new InvalidDataException("WebM TrackEntry exceeds its container");
        }

        if (trackNumber is null or 0 || trackUid is null or 0 || trackType is null or 0)
        {
            throw new InvalidDataException(
                "WebM TrackEntry requires positive TrackNumber, TrackUID, and TrackType values");
        }

        if (trackType != 1)
        {
            return new WebmTrack(trackNumber.Value, trackUid.Value, SupportedVideo: false);
        }

        if (codec is null
            || !RecognizedWebmVideoCodecs.Contains(codec)
            || !dimensions)
        {
            throw new InvalidDataException(
                "WebM video TrackEntry identity, codec, or dimensions are incomplete");
        }

        return new WebmTrack(trackNumber.Value, trackUid.Value, SupportedVideo: true);
    }

    private static bool ValidateWebmVideo(
        FileStream source,
        EbmlElement video,
        ref int elements)
    {
        var end = checked(source.Position + (long)video.Size);
        ulong? width = null;
        ulong? height = null;
        while (source.Position < end)
        {
            var child = ReadWebmElement(source, end, ref elements);
            switch (child.Id)
            {
                case 0xb0:
                    width = ReadEbmlUnsigned(source, child, 8);
                    break;
                case 0xba:
                    height = ReadEbmlUnsigned(source, child, 8);
                    break;
                default:
                    source.Position = checked(source.Position + (long)child.Size);
                    break;
            }
        }

        return width is >= 1 and <= 16_384
            && height is >= 1 and <= 16_384
            && checked(width.Value * height.Value) <= 67_108_864;
    }

    private static void ValidateWebmCluster(
        FileStream source,
        EbmlElement cluster,
        HashSet<ulong> videoTracks,
        ref int elements)
    {
        var end = checked(source.Position + (long)cluster.Size);
        var timecode = false;
        var keyframe = false;
        while (source.Position < end)
        {
            var child = ReadWebmElement(source, end, ref elements);
            switch (child.Id)
            {
                case 0xe7:
                    _ = ReadEbmlUnsigned(source, child, 8);
                    timecode = true;
                    break;
                case 0xa3:
                    keyframe |= ValidateWebmSimpleBlock(source, child, videoTracks);
                    break;
                default:
                    source.Position = checked(source.Position + (long)child.Size);
                    break;
            }
        }

        if (!timecode || !keyframe || source.Position != end)
        {
            throw new InvalidDataException("WebM Cluster requires a video keyframe SimpleBlock");
        }
    }

    private static bool ValidateWebmSimpleBlock(
        FileStream source,
        EbmlElement block,
        HashSet<ulong> videoTracks)
    {
        if (block.Size < 5)
        {
            throw new InvalidDataException("WebM SimpleBlock is truncated");
        }

        var start = source.Position;
        var track = ReadEbmlInteger(source, clearMarker: true, out _);
        if (!videoTracks.Contains(track) || source.Position + 3 >= start + (long)block.Size)
        {
            throw new InvalidDataException("WebM SimpleBlock track or payload is invalid");
        }

        _ = ReadUInt16BigEndian(source);
        var flags = ReadByte(source);
        source.Position = checked(start + (long)block.Size);
        return (flags & 0x80) != 0 && (flags & 0x06) == 0;
    }

    private static ulong ReadEbmlUnsigned(FileStream source, EbmlElement element, int maximumBytes)
    {
        if (element.Size is < 1 || element.Size > (ulong)maximumBytes)
        {
            throw new InvalidDataException("WebM unsigned element size is invalid");
        }

        ulong value = 0;
        for (var index = 0UL; index < element.Size; index++)
        {
            value = (value << 8) | (uint)ReadByte(source);
        }

        return value;
    }

    private static string ReadEbmlText(FileStream source, EbmlElement element, int maximumBytes)
    {
        if (element.Size is < 1 || element.Size > (ulong)maximumBytes)
        {
            throw new InvalidDataException("WebM text element size is invalid");
        }

        var value = new byte[checked((int)element.Size)];
        ReadExact(source, value);
        if (value.Any(item => item is < 0x20 or > 0x7e))
        {
            throw new InvalidDataException("WebM text element contains invalid bytes");
        }

        return Encoding.ASCII.GetString(value);
    }

    private static double ReadEbmlFloat(FileStream source, EbmlElement element)
    {
        return element.Size switch
        {
            4 => BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32BigEndian(source))),
            8 => BitConverter.Int64BitsToDouble(unchecked((long)ReadUInt64BigEndian(source))),
            _ => throw new InvalidDataException("WebM floating-point element size is invalid"),
        };
    }

    private static EbmlElement ReadEbmlElement(FileStream source, long limit)
    {
        var id = ReadEbmlInteger(source, clearMarker: false, out var idLength);
        if (idLength > 4)
        {
            throw new InvalidDataException("EBML element ID exceeds four bytes");
        }
        var size = ReadEbmlInteger(source, clearMarker: true, out var sizeLength);
        var unknown = size == ((1UL << (7 * sizeLength)) - 1);
        if (unknown || size > checked((ulong)(limit - source.Position)))
        {
            throw new InvalidDataException("EBML element size is unknown or exceeds its container");
        }

        return new EbmlElement(id, size);
    }

    private static ulong ReadEbmlInteger(FileStream source, bool clearMarker, out int length)
    {
        var first = ReadByte(source);
        var marker = 0x80;
        length = 1;
        while (length <= 8 && (first & marker) == 0)
        {
            marker >>= 1;
            length++;
        }

        if (length > 8)
        {
            throw new InvalidDataException("EBML variable-length integer is invalid");
        }

        ulong value = clearMarker ? (uint)(first & (marker - 1)) : (uint)first;
        for (var index = 1; index < length; index++)
        {
            value = (value << 8) | (uint)ReadByte(source);
        }

        return value;
    }

    private static string RenderEvidence(
        string[] failures,
        bool foundationQualified,
        string? contractSha256,
        IReadOnlyDictionary<string, string> documentHashes,
        bool candidateSupplied,
        bool candidateAccepted,
        string? sourceRevision,
        string? appVersion,
        string? candidateSha256)
    {
        var documents = new JsonObject();
        foreach (var path in RequiredDocumentPaths)
        {
            if (documentHashes.TryGetValue(path, out var digest))
            {
                documents[path] = digest;
            }
        }

        var root = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["kind"] = "release-materials-handoff-v2",
            ["passed"] = failures.Length == 0,
            ["foundationQualified"] = foundationQualified,
            ["contractSha256"] = contractSha256,
            ["documentSha256"] = documents,
            ["requiredDocumentCount"] = RequiredDocumentPaths.Length,
            ["artifactPlatformCount"] = ArtifactPlatforms.Length,
            ["inputDeviceCount"] = InputDeviceIds.Length,
            ["screenshotRoleCount"] = ScreenshotRoles.Length,
            ["videoRoleCount"] = VideoRoles.Length,
            ["marketingClaimCount"] = MarketingClaimIds.Length,
            ["candidateSupplied"] = candidateSupplied,
            ["candidateMaterialComplete"] = candidateAccepted,
            ["releaseAcceptance"] = false,
            ["sourceRevision"] = sourceRevision,
            ["appVersion"] = appVersion,
            ["candidateSha256"] = candidateSha256,
            ["pendingGates"] = new JsonArray(
                (candidateAccepted ? SeparateReleaseGates : PendingGates.Concat(SeparateReleaseGates))
                    .Select(value => (JsonNode?)JsonValue.Create(value))
                    .ToArray()),
            ["errors"] = new JsonArray(
                failures.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
        };
        var rendered = root.ToJsonString(RenderOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        if (StrictUtf8.GetByteCount(rendered) > MaximumEvidenceBytes)
        {
            throw new InvalidDataException(
                $"release materials evidence exceeds the {MaximumEvidenceBytes}-byte output limit");
        }

        return rendered;
    }

    private static void WriteAtomicEvidence(
        string repositoryRoot,
        string? candidatePath,
        IReadOnlyList<string> trustedRetainedPaths,
        string outputPath,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var root = ResolveRepositoryRoot(repositoryRoot);
        var path = Path.GetFullPath(
            Path.IsPathRooted(outputPath)
                ? outputPath
                : Path.Combine(root, outputPath));
        EnsureContained(root, path, "release materials evidence output");
        RejectInputAlias(root, candidatePath, trustedRetainedPaths, path);
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("release materials evidence output has no parent directory");
        CreateLinkFreeDirectory(root, parent, "release materials evidence output parent");
        if (Path.Exists(path))
        {
            EnsureNoLinks(root, path, "release materials evidence output");
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new InvalidDataException("release materials evidence output must be a regular file");
            }
        }

        var bytes = StrictUtf8.GetBytes(value);
        var temporary = Path.Combine(parent, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
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
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            var actual = File.ReadAllBytes(path);
            if (!actual.AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException("release materials evidence write verification failed");
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string ResolveRepositoryRoot(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new InvalidDataException("repository root must be an existing directory");
        }

        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("repository root cannot be a link");
        }

        return root;
    }

    private static string ResolveRegularFile(
        string root,
        string relativePath,
        long maximumBytes,
        string label)
    {
        var path = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureContained(root, path, label);
        EnsureNoLinks(root, path, label);
        return RequireRegularFile(path, maximumBytes, label, relativePath);
    }

    private static string ResolveExplicitRegularFile(
        string pathValue,
        long maximumBytes,
        string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathValue);
        var path = Path.GetFullPath(pathValue);
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException($"{label} has no parent directory");
        if (!Directory.Exists(parent)
            || (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"{label} parent must be a regular non-link directory");
        }

        EnsureNoLinks(parent, path, label);
        return RequireRegularFile(path, maximumBytes, label, pathValue);
    }

    private static string RequireRegularFile(
        string path,
        long maximumBytes,
        string label,
        string displayPath)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"missing {label}: {displayPath.Replace('\\', '/')}");
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException($"{label} must be a regular non-link file");
        }

        var length = new FileInfo(path).Length;
        if (length > maximumBytes)
        {
            throw new InvalidDataException($"{label} exceeds the {maximumBytes}-byte validation limit");
        }

        return path;
    }

    private static byte[] ReadBoundedBytes(string path, long maximumBytes, string label)
    {
        using var source = OpenRead(path);
        if (source.Length > maximumBytes || source.Length > int.MaxValue)
        {
            throw new InvalidDataException($"{label} exceeds the {maximumBytes}-byte validation limit");
        }

        var bytes = new byte[checked((int)source.Length)];
        ReadExact(source, bytes);
        if (source.ReadByte() != -1)
        {
            throw new InvalidDataException($"{label} changed while it was read");
        }

        return bytes;
    }

    private static JsonDocument ParseStrictJson(byte[] bytes, string label)
    {
        try
        {
            _ = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"{label} must contain valid UTF-8", exception);
        }

        var document = JsonDocument.Parse(
            bytes,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumJsonDepth,
            });
        RejectDuplicateProperties(document.RootElement, label);
        return document;
    }

    private static void RejectDuplicateProperties(JsonElement value, string location)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    throw new InvalidDataException($"{location} repeats JSON field: {property.Name}");
                }

                RejectDuplicateProperties(property.Value, $"{location}.{property.Name}");
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateProperties(item, $"{location}[{index}]");
                index++;
            }
        }
    }

    private static bool RequireExactFields(
        JsonElement value,
        IReadOnlySet<string> expected,
        string label,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            failures.Add($"{label} must be an object");
            return false;
        }

        var actual = value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
        {
            failures.Add(
                $"{label} fields must be [{string.Join(", ", expected.Order(StringComparer.Ordinal))}]; "
                + $"got [{string.Join(", ", actual.Order(StringComparer.Ordinal))}]");
            return false;
        }

        return true;
    }

    private static void RequireExactArray(
        JsonElement value,
        string[] expected,
        string label,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() != expected.Length)
        {
            failures.Add($"{label} must equal [{string.Join(", ", expected)}]");
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || !string.Equals(item.GetString(), expected[index], StringComparison.Ordinal))
            {
                failures.Add($"{label} must equal [{string.Join(", ", expected)}]");
                return;
            }

            index++;
        }
    }

    private static void RequireExactText(
        JsonElement value,
        string expected,
        string label,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.String
            || !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            failures.Add($"{label} must be '{expected}'");
        }
    }

    private static string? RequireText(
        JsonElement value,
        string label,
        List<string> failures,
        int maximumCharacters = MaximumTextCharacters)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            failures.Add($"{label} must be a nonempty string");
            return null;
        }

        var text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text)
            || text.EnumerateRunes().Take(maximumCharacters + 1).Count() > maximumCharacters)
        {
            failures.Add($"{label} must be a nonempty string up to {maximumCharacters} characters");
            return null;
        }

        return text;
    }

    private static void RequireInteger(
        JsonElement value,
        int expected,
        string label,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var actual)
            || actual != expected)
        {
            failures.Add($"{label} must be integer {expected}");
        }
    }

    private static void RequireNonnegativeInteger(
        JsonElement value,
        string label,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var actual)
            || actual < 0)
        {
            failures.Add($"{label} must be a nonnegative integer byte count");
        }
    }

    private static bool IsSafeRelativePath(string value, out string failure)
    {
        failure = "must be a safe relative POSIX path";
        if (value.Length is < 1 or > MaximumRelativePathCharacters
            || !value.IsNormalized(NormalizationForm.FormC)
            || value[0] == '/'
            || value[^1] == '/'
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains(':', StringComparison.Ordinal)
            || value.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0
            || value.Any(character => char.IsControl(character)))
        {
            return false;
        }

        var segments = value.Split('/');
        foreach (var segment in segments)
        {
            if (segment is "" or "." or ".."
                || segment.EndsWith(' ')
                || segment.EndsWith('.')
                || IsReservedWindowsName(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var stem = segment.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] is >= '1' and <= '9');
    }

    private static void EnsureContained(string root, string path, string label)
    {
        if (PathsAlias(root, path))
        {
            return;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!NormalizePathForComparison(path).StartsWith(
            NormalizePathForComparison(prefix),
            PathComparison()))
        {
            throw new InvalidDataException($"{label} must be inside its trusted root");
        }
    }

    private static void EnsureNoLinks(string root, string path, string label)
    {
        var relative = GetContainedRelativePath(root, path, label);
        var current = root;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Path.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"{label} path cannot contain a link");
            }
        }
    }

    private static void CreateLinkFreeDirectory(string root, string path, string label)
    {
        EnsureContained(root, path, label);
        var relative = GetContainedRelativePath(root, path, label);
        var current = root;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Path.Exists(current))
            {
                Directory.CreateDirectory(current);
            }

            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.Directory) == 0)
            {
                throw new InvalidDataException($"{label} path cannot contain a link or non-directory");
            }
        }
    }

    private static void RejectInputAlias(
        string root,
        string? candidatePath,
        IReadOnlyList<string> trustedRetainedPaths,
        string outputPath)
    {
        var inputs = RequiredDocumentPaths
            .Append(ContractRelativePath)
            .Append("VERSION")
            .Select(relativePath => Path.GetFullPath(
                Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();
        if (candidatePath is not null)
        {
            inputs.Add(Path.GetFullPath(candidatePath));
        }

        inputs.AddRange(trustedRetainedPaths.Select(Path.GetFullPath));

        if (inputs.Any(input => PathsAlias(input, outputPath)))
        {
            throw new InvalidDataException(
                "release materials evidence output cannot alias a qualification input");
        }
    }

    private static string GetContainedRelativePath(string root, string path, string label)
    {
        var relative = Path.GetRelativePath(root, path);
        var firstSegment = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (Path.IsPathRooted(relative) || firstSegment == "..")
        {
            throw new InvalidDataException($"{label} must be inside its trusted root");
        }

        return relative == "." ? string.Empty : relative;
    }

    private static string HashAndValidateStableFile(
        RetainedFile file,
        List<string> failures)
    {
        var before = new FileInfo(file.Path);
        if (before.Length != file.Length)
        {
            throw new InvalidDataException("retained file changed before hashing");
        }

        if (file.MediaExpectations.ContainsKey("image") && file.Length > MaximumImageBytes)
        {
            throw new InvalidDataException(
                $"image exceeds the {MaximumImageBytes}-byte validation limit");
        }

        if (file.MediaExpectations.ContainsKey("video") && file.Length > MaximumVideoBytes)
        {
            throw new InvalidDataException(
                $"video exceeds the {MaximumVideoBytes}-byte validation limit");
        }

        string? snapshotPath = null;
        try
        {
            FileStream? snapshot = null;
            if (file.MediaExpectations.Count > 0)
            {
                snapshotPath = Path.Combine(
                    Path.GetTempPath(),
                    $"vibesnake-release-material-{Guid.NewGuid():N}.snapshot");
                snapshot = new FileStream(
                    snapshotPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.SequentialScan);
            }

            string digest;
            try
            {
                using var source = OpenRead(file.Path);
                if (source.Length != file.Length)
                {
                    throw new InvalidDataException("retained file changed before hashing");
                }

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1024 * 1024];
                long total = 0;
                while (total < file.Length)
                {
                    var count = source.Read(
                        buffer,
                        0,
                        checked((int)Math.Min(buffer.Length, file.Length - total)));
                    if (count == 0)
                    {
                        throw new InvalidDataException("retained file changed while hashing");
                    }

                    hash.AppendData(buffer.AsSpan(0, count));
                    snapshot?.Write(buffer, 0, count);
                    total += count;
                }

                if (source.ReadByte() != -1)
                {
                    throw new InvalidDataException("retained file grew while hashing");
                }

                digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            finally
            {
                snapshot?.Dispose();
            }

            var after = new FileInfo(file.Path);
            var attributes = File.GetAttributes(file.Path);
            if (!after.Exists
                || (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                || after.Length != before.Length
                || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
            {
                throw new InvalidDataException("retained file changed while inspected");
            }

            if (snapshotPath is not null)
            {
                foreach (var expectation in file.MediaExpectations)
                {
                    ValidateMedia(
                        file,
                        snapshotPath,
                        expectation.Key,
                        expectation.Value,
                        failures);
                }
            }

            return digest;
        }
        finally
        {
            if (snapshotPath is not null)
            {
                File.Delete(snapshotPath);
            }
        }
    }

    private static FileStream OpenRead(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool IsLowerHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ReadExact(Stream source, Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var count = source.Read(destination[offset..]);
            if (count == 0)
            {
                throw new InvalidDataException("file ended before the declared structure was complete");
            }

            offset += count;
        }
    }

    private static int ReadByte(Stream source)
    {
        var value = source.ReadByte();
        return value < 0
            ? throw new InvalidDataException("file ended before the declared structure was complete")
            : value;
    }

    private static ushort ReadUInt16BigEndian(Stream source)
    {
        Span<byte> value = stackalloc byte[2];
        ReadExact(source, value);
        return BinaryPrimitives.ReadUInt16BigEndian(value);
    }

    private static uint ReadUInt32BigEndian(Stream source)
    {
        Span<byte> value = stackalloc byte[4];
        ReadExact(source, value);
        return BinaryPrimitives.ReadUInt32BigEndian(value);
    }

    private static ulong ReadUInt64BigEndian(Stream source)
    {
        Span<byte> value = stackalloc byte[8];
        ReadExact(source, value);
        return BinaryPrimitives.ReadUInt64BigEndian(value);
    }

    private static void Skip(Stream source, long count)
    {
        if (count < 0 || source.Position + count > source.Length)
        {
            throw new InvalidDataException("declared structure exceeds the file");
        }

        source.Position += count;
    }

    private static int ReadJpegMarker(Stream source)
    {
        if (ReadByte(source) != 0xff)
        {
            throw new InvalidDataException("JPEG marker prefix is missing");
        }

        var marker = ReadByte(source);
        while (marker == 0xff)
        {
            marker = ReadByte(source);
        }

        if (marker == 0x00)
        {
            throw new InvalidDataException("JPEG stuffed byte appears outside scan data");
        }

        return marker;
    }

    private static bool IsStartOfFrame(int marker) =>
        marker is >= 0xc0 and <= 0xcf
        && marker is not (0xc4 or 0xc8 or 0xcc);

    private static bool IsExpectedFailure(Exception exception) =>
        exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or DecoderFallbackException
            or JsonException
            or NotSupportedException
            or OverflowException;

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool PathsAlias(string left, string right) =>
        string.Equals(
            NormalizePathForComparison(left),
            NormalizePathForComparison(right),
            PathComparison());

    private static string NormalizePathForComparison(string value) =>
        value.Normalize(NormalizationForm.FormC);

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string[] BoundFailures(IEnumerable<string> failures)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var overflow = false;
        foreach (var failure in failures)
        {
            var bounded = BoundFailure(failure);
            if (!seen.Add(bounded))
            {
                continue;
            }

            if (result.Count == MaximumFailures)
            {
                overflow = true;
                continue;
            }

            result.Add(bounded);
        }

        if (overflow)
        {
            result[^1] = "Additional validation failures were omitted at the diagnostic limit.";
        }

        return result.ToArray();
    }

    private static string BoundFailure(string value)
    {
        var singleLine = SingleLine(value);
        var runes = singleLine.EnumerateRunes().Take(MaximumFailureCharacters + 1).ToArray();
        return runes.Length <= MaximumFailureCharacters
            ? singleLine
            : string.Concat(runes.Take(MaximumFailureCharacters).Select(rune => rune.ToString())) + "...";
    }

    private static RepositoryCheckResult Failed(string[] failures) =>
        new("Release materials", false, string.Empty, BoundFailures(failures));

    private sealed record Qualification(
        string[] Failures,
        bool CandidateAccepted,
        string Json,
        IReadOnlyList<string> TrustedRetainedPaths);

    private sealed record DocumentSet(
        IReadOnlyDictionary<string, string> Hashes,
        IReadOnlyDictionary<string, string> Text);

    private sealed record RetainedFile(
        string Path,
        string RelativePath,
        long Length)
    {
        public Dictionary<string, string> MediaExpectations { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct EbmlElement(ulong Id, ulong Size);

    private readonly record struct WebmTrack(ulong Number, ulong Uid, bool SupportedVideo);

    private readonly record struct Mp4Box(
        string Type,
        long Start,
        long PayloadStart,
        long End,
        long PayloadLength);

    private readonly record struct Mp4SampleToChunk(
        uint FirstChunk,
        uint SamplesPerChunk,
        uint DescriptionIndex);

    private readonly record struct Mp4ChunkExtent(ulong Offset, ulong Length);

    private sealed record Mp4SampleTable(
        IReadOnlyList<Mp4ChunkExtent> Chunks);

    private sealed class RetainedFiles
    {
        private readonly string root;
        private readonly List<string> failures;
        private readonly Dictionary<string, RetainedFile> files = new(StringComparer.Ordinal);
        private readonly HashSet<string> paths = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> foldedPaths = new(StringComparer.OrdinalIgnoreCase);
        private long totalBytes;

        public RetainedFiles(string root, List<string> failures)
        {
            this.root = Path.GetFullPath(root);
            this.failures = failures;
            if (!Directory.Exists(this.root)
                || (File.GetAttributes(this.root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "candidate retained-file root must be a regular non-link directory");
            }
        }

        public IEnumerable<string> RelativePaths => paths;

        public IEnumerable<string> ResolvedPaths => files.Values.Select(file => file.Path);

        public void Add(string relativePath)
        {
            if (!paths.Add(relativePath))
            {
                return;
            }

            if (paths.Count > MaximumRetainedFiles)
            {
                failures.Add($"candidate retained files exceed the {MaximumRetainedFiles}-file limit");
                return;
            }

            if (foldedPaths.TryGetValue(relativePath, out var existing)
                && !string.Equals(existing, relativePath, StringComparison.Ordinal))
            {
                failures.Add($"candidate retained paths collide by portable case: {existing}, {relativePath}");
                return;
            }

            foldedPaths[relativePath] = relativePath;
            try
            {
                var path = Path.GetFullPath(
                    Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                EnsureContained(root, path, "candidate retained file");
                EnsureNoLinks(root, path, "candidate retained file");
                path = RequireRegularFile(path, MaximumRetainedFileBytes, "candidate retained file", relativePath);
                var length = new FileInfo(path).Length;
                if (length == 0)
                {
                    throw new InvalidDataException("candidate retained file must be nonempty");
                }

                totalBytes = checked(totalBytes + length);
                if (totalBytes > MaximumRetainedTotalBytes)
                {
                    throw new InvalidDataException(
                        $"candidate retained files exceed the {MaximumRetainedTotalBytes}-byte aggregate limit");
                }

                files[relativePath] = new RetainedFile(path, relativePath, length);
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                failures.Add($"{relativePath}: {SingleLine(exception.Message)}");
            }
        }

        public bool TryGet(string relativePath, out RetainedFile file) =>
            files.TryGetValue(relativePath, out file!);

        public void ExpectMedia(string relativePath, string mediaKind, string label)
        {
            if (TryGet(relativePath, out var file))
            {
                file.MediaExpectations.TryAdd(mediaKind, label);
            }
        }
    }
}
