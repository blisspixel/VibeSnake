using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public static class ReleaseRehearsalCheck
{
    private const string ContractRelativePath = "config/release_rehearsal_v1.json";
    private const string MaterialAcceptanceContractRelativePath =
        "config/release_materials_acceptance_v1.json";
    private const int MaximumJsonDepth = 64;
    private const int MaximumFailures = 128;
    private const int MaximumFailureCharacters = 256;
    private const int MaximumRelativePathCharacters = 512;
    private const int MaximumTextCharacters = 4096;
    private const int MaximumEvidencePaths = 16;
    private const int MaximumMigrationFixtures = 256;
    private const int MaximumManifestEntries = 4096;
    private const int MaximumRetainedFiles = 4096;
    private const int MaximumOutputBytes = 256 * 1024;
    private const long MaximumContractBytes = 1024 * 1024;
    private const long MaximumRecordBytes = 4 * 1024 * 1024;
    private const long MaximumDecisionBytes = 4 * 1024 * 1024;
    private const long MaximumManifestBytes = 8 * 1024 * 1024;
    private const long MaximumPrerequisiteBytes = 16 * 1024 * 1024;
    private const long MaximumPrerequisiteTotalBytes = 64 * 1024 * 1024;
    private const long MaximumRetainedFileBytes = 8L * 1024 * 1024 * 1024;
    private const long MaximumRetainedTotalBytes = 32L * 1024 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex VersionPattern = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(alpha|beta|rc)\.([1-9][0-9]*))?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex RolePattern = new(
        "^[a-z0-9][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions RenderOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };

    private static readonly string[] ArtifactPlatforms =
        ["windows-x64", "macos-universal", "linux-x64"];

    private static readonly string[] PlatformOperationIds =
    [
        "download",
        "checksum",
        "signature-verification",
        "install",
        "launch",
        "save-creation",
        "optional-content-install",
        "optional-content-removal",
        "update",
        "rollback",
        "application-removal",
    ];

    private static readonly string[] AuthorityOperationIds =
        ["publish", "halt", "replace", "communicate"];

    private static readonly string[] ResultValues = ["pass", "fail", "blocked"];

    private static readonly string[] RecordFields =
    [
        "schemaVersion",
        "kind",
        "rehearsalId",
        "sourceRevision",
        "appVersion",
        "previousVersion",
        "stagedLocationId",
        "executedUtc",
        "candidateArtifactSha256ByPlatform",
        "candidateArtifactPathsByPlatform",
        "previousArtifactSha256ByPlatform",
        "previousArtifactPathsByPlatform",
        "candidateManifestSha256ByPlatform",
        "candidateManifestPathsByPlatform",
        "releaseMaterialsDecisionSha256",
        "releaseMaterialsDecisionPath",
        "migrationFixtureSetSha256",
        "migrationFixturePaths",
        "platformResults",
        "withdrawalResult",
        "authorityRecords",
        "retainedFileSha256",
    ];

    private static readonly string[] PlatformResultFields =
    [
        "platformId",
        "operationResults",
        "evidencePathsByOperation",
        "protectedUserDataSha256Before",
        "protectedUserDataSha256After",
    ];

    private static readonly string[] WithdrawalFields =
    [
        "candidateUnavailable",
        "previousArtifactRestored",
        "userDataPreserved",
        "communicationPrepared",
        "evidencePaths",
    ];

    private static readonly string[] AuthorityFields =
        ["operationId", "roleId", "authorizationVerified", "evidencePaths"];

    private static readonly string[] PrerequisitePaths =
    [
        "config/release_materials_v1.json",
        "config/release_signing_policy.json",
        "docs/release/PACKAGING.md",
        "docs/release/SIGNING.md",
        "docs/guides/RECOVERY.md",
    ];

    private static readonly string[] ReleaseRules =
    [
        "The staged candidate artifacts, manifests, release-material decision, previous artifacts, and migration fixtures are retained and hash-verified.",
        "Download, checksum, signature, install, launch, save creation, optional content, update, rollback, and removal pass on every platform.",
        "Rollback and application removal preserve the protected preexisting user-data fixture exactly.",
        "Withdrawal makes the candidate unavailable, restores the previous artifact, preserves user data, and prepares communication.",
        "Publish, halt, replace, and communicate authority is assigned to verified operational roles without storing personal data.",
        "Any failed or blocked operation prevents rehearsal acceptance.",
    ];

    private static readonly string[] PendingGates =
    [
        "staged-final-artifacts-and-checksums",
        "three-platform-install-update-rollback-removal",
        "optional-content-lifecycle",
        "withdrawal-and-previous-artifact-restoration",
        "user-data-preservation",
        "verified-release-authority-roles",
    ];

    private static readonly string[] MaterialGateIds =
    [
        "artifact-manifest-size-reconciliation",
        "marketing-claim-approval",
        "visible-image-review",
        "video-playback-review",
    ];

    private static readonly string[] MaterialDocumentPaths =
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

    private static readonly string[] MaterialAcceptanceRules =
    [
        "The accepted decision binds one structurally complete release-material handoff whose release acceptance remains false.",
        "The source revision, application version, candidate hash, and three artifact-manifest hashes match the structural handoff and rehearsal record exactly.",
        "Artifact-manifest size reconciliation, marketing-claim approval, visible-image review, and video-playback review each have one passing gate record.",
        "Every gate uses a verified non-personal authority role and at least one retained evidence file.",
        "The retained-file hash map is the exact closure of the structural handoff and all gate evidence paths.",
        "Pending gates and errors are empty before release acceptance can be true.",
        "Automated validation checks contract shape, retained bytes, and cross-file identity but never performs or invents external or human approval.",
    ];

    private static readonly HashSet<string> ContractFields = Set(
        "schemaVersion", "kind", "status", "artifactPlatforms", "platformOperationIds",
        "authorityOperationIds", "resultValues", "requiredRecordFields",
        "requiredPlatformResultFields", "requiredWithdrawalFields", "requiredAuthorityFields",
        "prerequisitePaths", "releaseRules");

    private static readonly string[] MaterialDecisionFieldOrder =
    [
        "schemaVersion",
        "kind",
        "passed",
        "foundationQualified",
        "candidateMaterialComplete",
        "releaseAcceptance",
        "sourceRevision",
        "appVersion",
        "candidateSha256",
        "structuralHandoffPath",
        "structuralHandoffSha256",
        "artifactManifestSha256ByPlatform",
        "acceptedUtc",
        "gateRecords",
        "retainedFileSha256",
        "pendingGates",
        "errors",
    ];

    private static readonly HashSet<string> MaterialDecisionFields =
        MaterialDecisionFieldOrder.ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> MaterialAcceptanceContractFields = Set(
        "schemaVersion", "kind", "status", "artifactPlatforms", "sourceStructuralHandoffKind",
        "acceptedDecisionKind", "gateIds", "resultValues", "requiredDecisionFields",
        "requiredGateFields", "releaseRules");

    private static readonly HashSet<string> MaterialGateFields = Set(
        "gateId", "result", "authorityRoleId", "evidencePaths");

    private static readonly HashSet<string> StructuralHandoffFields = Set(
        "schemaVersion", "kind", "passed", "foundationQualified", "contractSha256",
        "documentSha256", "requiredDocumentCount", "artifactPlatformCount", "inputDeviceCount",
        "screenshotRoleCount", "videoRoleCount", "marketingClaimCount", "candidateSupplied",
        "candidateMaterialComplete", "releaseAcceptance", "sourceRevision", "appVersion",
        "candidateSha256", "pendingGates", "errors");

    private static readonly HashSet<string> ManifestFields = Set(
        "schemaVersion", "product", "platform", "buildMode", "sourceRevision", "godotVersion",
        "godotCommit", "godotArchiveSha512", "godotExecutableSha256", "dotnetSdk",
        "smokeStateHash", "agentArenaPreviewExcluded", "fileCount", "totalBytes", "files",
        "containerEntries");

    private static readonly HashSet<string> ManifestEntryRequiredFields = Set(
        "path", "bytes", "sha256");

    private static readonly HashSet<string> ManifestEntryAllowedFields = Set(
        "path", "bytes", "sha256", "compressedBytes");

    public static RepositoryCheckResult Inspect(string repositoryRoot) =>
        Execute(repositoryRoot, recordPath: null, expectedRevision: null, outputPath: null);

    public static RepositoryCheckResult WriteFoundationHandoff(
        string repositoryRoot,
        string outputPath) =>
        Execute(repositoryRoot, recordPath: null, expectedRevision: null, outputPath);

    public static RepositoryCheckResult WriteRecordHandoff(
        string repositoryRoot,
        string recordPath,
        string expectedRevision,
        string outputPath) =>
        Execute(repositoryRoot, recordPath, expectedRevision, outputPath);

    private static RepositoryCheckResult Execute(
        string repositoryRoot,
        string? recordPath,
        string? expectedRevision,
        string? outputPath)
    {
        try
        {
            var qualification = Qualify(repositoryRoot, recordPath, expectedRevision);
            if (outputPath is not null)
            {
                WriteAtomicEvidence(
                    qualification.OutputRoot,
                    qualification.TrustedInputs,
                    outputPath,
                    qualification.Json);
            }

            return qualification.Failures.Length == 0
                ? new RepositoryCheckResult(
                    "Release rehearsal",
                    true,
                    qualification.RecordAccepted
                        ? "Release and rollback rehearsal accepted for the exact candidate."
                        : "Release rehearsal handoff qualified; staged execution remains pending.",
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
        string? recordPath,
        string? expectedRevision)
    {
        var root = ResolveRepositoryRoot(repositoryRoot);
        var outputRoot = root;
        var failures = new List<string>();
        var trustedInputs = new List<string>();
        string? contractSha = null;
        var prerequisiteHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        string? appVersion = null;
        string? materialAcceptanceContractSha = null;

        try
        {
            var contractPath = ResolveRegularFile(
                root,
                ContractRelativePath,
                MaximumContractBytes,
                "release rehearsal contract");
            trustedInputs.Add(contractPath);
            var contractBytes = ReadBoundedStableBytes(
                contractPath,
                MaximumContractBytes,
                "release rehearsal contract");
            contractSha = Sha256(contractBytes);
            using var contract = ParseStrictJson(contractBytes, "release rehearsal contract");
            ValidateContract(contract.RootElement, failures);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add(SingleLine(exception.Message));
            trustedInputs.Add(Path.GetFullPath(Path.Combine(root, ContractRelativePath)));
        }

        try
        {
            var path = ResolveRegularFile(
                root,
                MaterialAcceptanceContractRelativePath,
                MaximumContractBytes,
                "release materials acceptance contract");
            trustedInputs.Add(path);
            var bytes = ReadBoundedStableBytes(
                path,
                MaximumContractBytes,
                "release materials acceptance contract");
            materialAcceptanceContractSha = Sha256(bytes);
            prerequisiteHashes[MaterialAcceptanceContractRelativePath] =
                materialAcceptanceContractSha;
            using var document = ParseStrictJson(bytes, "release materials acceptance contract");
            ValidateMaterialAcceptanceContract(document.RootElement, failures);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add(SingleLine(exception.Message));
            trustedInputs.Add(Path.GetFullPath(
                Path.Combine(root, MaterialAcceptanceContractRelativePath)));
        }

        long prerequisiteTotal = 0;
        foreach (var relativePath in PrerequisitePaths)
        {
            try
            {
                var path = ResolveRegularFile(
                    root,
                    relativePath,
                    MaximumPrerequisiteBytes,
                    "release rehearsal prerequisite");
                trustedInputs.Add(path);
                var snapshot = HashStableFile(path, MaximumPrerequisiteBytes, "release rehearsal prerequisite");
                prerequisiteTotal = checked(prerequisiteTotal + snapshot.Length);
                if (prerequisiteTotal > MaximumPrerequisiteTotalBytes)
                {
                    throw new InvalidDataException(
                        $"release rehearsal prerequisites exceed the {MaximumPrerequisiteTotalBytes}-byte aggregate limit");
                }

                prerequisiteHashes[relativePath] = snapshot.Sha256;
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                failures.Add($"{relativePath}: {SingleLine(exception.Message)}");
                trustedInputs.Add(Path.GetFullPath(
                    Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))));
            }
        }

        try
        {
            var versionPath = ResolveRegularFile(root, "VERSION", 1024, "canonical product version");
            trustedInputs.Add(versionPath);
            appVersion = ProductVersionCheck.ReadCanonicalVersion(root);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add(SingleLine(exception.Message));
            trustedInputs.Add(Path.Combine(root, "VERSION"));
        }

        var foundationFailureCount = failures.Count;
        RecordQualification? record = null;
        string? recordSha = null;
        if (recordPath is not null)
        {
            if (!string.IsNullOrWhiteSpace(recordPath))
            {
                var proposedRecordPath = Path.GetFullPath(recordPath);
                var proposedRecordParent = Path.GetDirectoryName(proposedRecordPath);
                if (proposedRecordParent is not null
                    && Directory.Exists(proposedRecordParent)
                    && (File.GetAttributes(proposedRecordParent) & FileAttributes.ReparsePoint) == 0)
                {
                    outputRoot = proposedRecordParent;
                }
            }
            if (!IsLowerHex(expectedRevision, 40))
            {
                failures.Add(
                    "an exact lowercase 40-character expected revision is required with a rehearsal record");
            }

            try
            {
                var path = ResolveExplicitRegularFile(
                    recordPath,
                    MaximumRecordBytes,
                    "release rehearsal record");
                trustedInputs.Add(path);
                var bytes = ReadBoundedStableBytes(path, MaximumRecordBytes, "release rehearsal record");
                recordSha = Sha256(bytes);
                using var document = ParseStrictJson(bytes, "release rehearsal record");
                record = ValidateRecord(
                    root,
                    path,
                    document.RootElement,
                    expectedRevision,
                    appVersion,
                    failures);
                if (record is not null)
                {
                    trustedInputs.AddRange(record.TrustedInputs);
                }
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                failures.Add(SingleLine(exception.Message));
                if (!string.IsNullOrWhiteSpace(recordPath))
                {
                    trustedInputs.Add(Path.GetFullPath(recordPath));
                }
            }
        }

        var boundedFailures = BoundFailures(failures);
        var protocolQualified = foundationFailureCount == 0;
        var recordAccepted = recordPath is not null
            && record is not null
            && protocolQualified
            && boundedFailures.Length == 0;
        var sourceRevision = recordAccepted ? record!.SourceRevision : null;
        var previousVersion = recordAccepted ? record!.PreviousVersion : null;
        var materialDecisionSha = recordAccepted ? record!.ReleaseMaterialsDecisionSha256 : null;
        var candidateArtifactHashes = recordAccepted
            ? record!.CandidateArtifactSha256ByPlatform
            : EmptyPlatformMap();
        var candidateManifestHashes = recordAccepted
            ? record!.CandidateManifestSha256ByPlatform
            : EmptyPlatformMap();
        var json = RenderEvidence(
            boundedFailures,
            protocolQualified,
            contractSha,
            materialAcceptanceContractSha,
            prerequisiteHashes,
            recordPath is not null,
            recordSha,
            recordAccepted,
            sourceRevision,
            appVersion,
            previousVersion,
            materialDecisionSha,
            candidateArtifactHashes,
            candidateManifestHashes);
        return new Qualification(
            outputRoot,
            boundedFailures,
            recordAccepted,
            json,
            trustedInputs.Select(Path.GetFullPath).Distinct(PathComparer()).ToArray());
    }

    private static void ValidateContract(JsonElement value, List<string> failures)
    {
        if (!RequireExactFields(value, ContractFields, "contract", failures))
        {
            return;
        }

        RequireInteger(value.GetProperty("schemaVersion"), 1, "contract.schemaVersion", failures);
        RequireExactText(value.GetProperty("kind"), "vibesnake-release-rehearsal-v1", "contract.kind", failures);
        RequireExactText(
            value.GetProperty("status"),
            "qualified-handoff-execution-pending",
            "contract.status",
            failures);
        RequireExactArray(value.GetProperty("artifactPlatforms"), ArtifactPlatforms, "contract.artifactPlatforms", failures);
        RequireExactArray(value.GetProperty("platformOperationIds"), PlatformOperationIds, "contract.platformOperationIds", failures);
        RequireExactArray(value.GetProperty("authorityOperationIds"), AuthorityOperationIds, "contract.authorityOperationIds", failures);
        RequireExactArray(value.GetProperty("resultValues"), ResultValues, "contract.resultValues", failures);
        RequireExactArray(value.GetProperty("requiredRecordFields"), RecordFields, "contract.requiredRecordFields", failures);
        RequireExactArray(
            value.GetProperty("requiredPlatformResultFields"),
            PlatformResultFields,
            "contract.requiredPlatformResultFields",
            failures);
        RequireExactArray(value.GetProperty("requiredWithdrawalFields"), WithdrawalFields, "contract.requiredWithdrawalFields", failures);
        RequireExactArray(value.GetProperty("requiredAuthorityFields"), AuthorityFields, "contract.requiredAuthorityFields", failures);
        RequireExactArray(value.GetProperty("prerequisitePaths"), PrerequisitePaths, "contract.prerequisitePaths", failures);
        RequireExactArray(value.GetProperty("releaseRules"), ReleaseRules, "contract.releaseRules", failures);
    }

    private static void ValidateMaterialAcceptanceContract(
        JsonElement value,
        List<string> failures)
    {
        const string label = "release materials acceptance contract";
        if (!RequireExactFields(value, MaterialAcceptanceContractFields, label, failures))
        {
            return;
        }
        RequireInteger(value.GetProperty("schemaVersion"), 1, $"{label}.schemaVersion", failures);
        RequireExactText(
            value.GetProperty("kind"),
            "vibesnake-release-materials-acceptance-v1",
            $"{label}.kind",
            failures);
        RequireExactText(
            value.GetProperty("status"),
            "external-approval-required",
            $"{label}.status",
            failures);
        RequireExactArray(
            value.GetProperty("artifactPlatforms"),
            ArtifactPlatforms,
            $"{label}.artifactPlatforms",
            failures);
        RequireExactText(
            value.GetProperty("sourceStructuralHandoffKind"),
            "release-materials-handoff-v2",
            $"{label}.sourceStructuralHandoffKind",
            failures);
        RequireExactText(
            value.GetProperty("acceptedDecisionKind"),
            "release-materials-acceptance-v1",
            $"{label}.acceptedDecisionKind",
            failures);
        RequireExactArray(value.GetProperty("gateIds"), MaterialGateIds, $"{label}.gateIds", failures);
        RequireExactArray(value.GetProperty("resultValues"), ["pass"], $"{label}.resultValues", failures);
        RequireExactArray(
            value.GetProperty("requiredDecisionFields"),
            MaterialDecisionFieldOrder,
            $"{label}.requiredDecisionFields",
            failures);
        RequireExactArray(
            value.GetProperty("requiredGateFields"),
            ["gateId", "result", "authorityRoleId", "evidencePaths"],
            $"{label}.requiredGateFields",
            failures);
        RequireExactArray(
            value.GetProperty("releaseRules"),
            MaterialAcceptanceRules,
            $"{label}.releaseRules",
            failures);
    }

    private static RecordQualification? ValidateRecord(
        string repositoryRoot,
        string recordPath,
        JsonElement value,
        string? expectedRevision,
        string? canonicalAppVersion,
        List<string> failures)
    {
        if (!RequireExactFields(value, RecordFields.ToHashSet(StringComparer.Ordinal), "rehearsal", failures))
        {
            return null;
        }

        RequireInteger(value.GetProperty("schemaVersion"), 1, "rehearsal.schemaVersion", failures);
        RequireExactText(value.GetProperty("kind"), "vibesnake-release-rehearsal-record-v1", "rehearsal.kind", failures);
        _ = RequireBoundedText(value.GetProperty("rehearsalId"), "rehearsal.rehearsalId", failures);
        var sourceRevision = RequireBoundedText(
            value.GetProperty("sourceRevision"),
            "rehearsal.sourceRevision",
            failures);
        if (!IsLowerHex(sourceRevision, 40))
        {
            failures.Add("rehearsal.sourceRevision must be a lowercase 40-character revision");
        }
        if (!string.Equals(sourceRevision, expectedRevision, StringComparison.Ordinal))
        {
            failures.Add("rehearsal.sourceRevision must match the exact expected revision");
        }

        var appVersion = RequireBoundedText(value.GetProperty("appVersion"), "rehearsal.appVersion", failures);
        if (canonicalAppVersion is not null
            && !string.Equals(appVersion, canonicalAppVersion, StringComparison.Ordinal))
        {
            failures.Add($"rehearsal.appVersion must be '{canonicalAppVersion}'");
        }

        var previousVersion = RequireBoundedText(
            value.GetProperty("previousVersion"),
            "rehearsal.previousVersion",
            failures);
        if (previousVersion is null || !TryParseVersion(previousVersion, out var parsedPrevious))
        {
            failures.Add("rehearsal.previousVersion must be a canonical semantic version");
        }
        else if (appVersion is not null
            && TryParseVersion(appVersion, out var parsedApp)
            && parsedPrevious.CompareTo(parsedApp) >= 0)
        {
            failures.Add("rehearsal.previousVersion must be strictly earlier than appVersion");
        }

        _ = RequireBoundedText(value.GetProperty("stagedLocationId"), "rehearsal.stagedLocationId", failures);
        var executedUtc = ValidateUtc(value.GetProperty("executedUtc"), "rehearsal.executedUtc", failures);

        var baseDirectory = Path.GetDirectoryName(recordPath)
            ?? throw new InvalidDataException("release rehearsal record has no parent directory");
        var retained = new RetainedFiles(baseDirectory, failures);
        var nestedMaterialInputs = new List<string>();
        var candidateDigests = ReadDigestMap(
            value.GetProperty("candidateArtifactSha256ByPlatform"),
            "rehearsal.candidateArtifactSha256ByPlatform",
            failures);
        var candidatePaths = ReadPathMap(
            value.GetProperty("candidateArtifactPathsByPlatform"),
            "rehearsal.candidateArtifactPathsByPlatform",
            retained,
            failures,
            exclusive: true);
        var previousDigests = ReadDigestMap(
            value.GetProperty("previousArtifactSha256ByPlatform"),
            "rehearsal.previousArtifactSha256ByPlatform",
            failures);
        var previousPaths = ReadPathMap(
            value.GetProperty("previousArtifactPathsByPlatform"),
            "rehearsal.previousArtifactPathsByPlatform",
            retained,
            failures,
            exclusive: true);
        var manifestDigests = ReadDigestMap(
            value.GetProperty("candidateManifestSha256ByPlatform"),
            "rehearsal.candidateManifestSha256ByPlatform",
            failures);
        var manifestPaths = ReadPathMap(
            value.GetProperty("candidateManifestPathsByPlatform"),
            "rehearsal.candidateManifestPathsByPlatform",
            retained,
            failures,
            exclusive: true);

        var manifests = new Dictionary<string, ManifestIdentity>(StringComparer.Ordinal);
        foreach (var platform in ArtifactPlatforms)
        {
            ValidateDigestPair(candidateDigests, candidatePaths, platform, "candidate artifact", retained, failures);
            ValidateDigestPair(previousDigests, previousPaths, platform, "previous artifact", retained, failures);
            ValidateDigestPair(manifestDigests, manifestPaths, platform, "candidate manifest", retained, failures);
            if (candidateDigests.TryGetValue(platform, out var candidateDigest)
                && previousDigests.TryGetValue(platform, out var previousDigest)
                && string.Equals(candidateDigest, previousDigest, StringComparison.Ordinal))
            {
                failures.Add($"candidate and previous artifact hashes must differ for {platform}");
            }

            if (manifestPaths.TryGetValue(platform, out var relativeManifest)
                && retained.TryGet(relativeManifest, out var manifestFile))
            {
                try
                {
                    var bytes = retained.ReadBytes(manifestFile, MaximumManifestBytes, "candidate artifact manifest");
                    using var manifest = ParseStrictJson(bytes, "candidate artifact manifest");
                    var identity = ValidateManifest(manifest.RootElement, platform, sourceRevision, failures);
                    if (identity is not null)
                    {
                        manifests[platform] = identity;
                    }
                }
                catch (Exception exception) when (IsExpectedFailure(exception))
                {
                    failures.Add($"candidate manifest {platform}: {SingleLine(exception.Message)}");
                }
            }
        }
        ValidateManifestSet(manifests, failures);

        var decisionPath = ReadSinglePath(
            value.GetProperty("releaseMaterialsDecisionPath"),
            "rehearsal.releaseMaterialsDecisionPath",
            retained,
            failures,
            exclusive: true);
        var decisionDigest = RequireDigest(
            value.GetProperty("releaseMaterialsDecisionSha256"),
            "rehearsal.releaseMaterialsDecisionSha256",
            failures);
        if (decisionPath is not null)
        {
            ValidateDigestPair(
                decisionDigest,
                decisionPath,
                "release materials decision",
                retained,
                failures);
            if (retained.TryGet(decisionPath, out var decisionFile))
            {
                ValidateMaterialDecision(
                    decisionFile,
                    sourceRevision,
                    appVersion,
                    executedUtc,
                    manifestDigests,
                    retained,
                    nestedMaterialInputs,
                    failures);
            }
        }

        var fixturePaths = ReadPathArray(
            value.GetProperty("migrationFixturePaths"),
            "rehearsal.migrationFixturePaths",
            retained,
            failures,
            MaximumMigrationFixtures);
        var fixtureSetDigest = RequireDigest(
            value.GetProperty("migrationFixtureSetSha256"),
            "rehearsal.migrationFixtureSetSha256",
            failures);
        if (fixturePaths.Count > 0 && fixtureSetDigest is not null)
        {
            var actual = ComputeFixtureSetSha(fixturePaths, retained);
            if (!string.Equals(actual, fixtureSetDigest, StringComparison.Ordinal))
            {
                failures.Add("rehearsal migration fixture set hash mismatch");
            }
        }

        ValidatePlatformResults(value.GetProperty("platformResults"), retained, failures);
        ValidateWithdrawal(value.GetProperty("withdrawalResult"), retained, failures);
        ValidateAuthorities(value.GetProperty("authorityRecords"), retained, failures);
        ValidateRetainedHashes(value.GetProperty("retainedFileSha256"), retained, failures);
        retained.VerifyStableSnapshots();

        return sourceRevision is null || appVersion is null || previousVersion is null || decisionDigest is null
            ? null
            : new RecordQualification(
                sourceRevision,
                previousVersion,
                decisionDigest,
                candidateDigests,
                manifestDigests,
                retained.ResolvedPaths.Concat(nestedMaterialInputs).ToArray());
    }

    private static void ValidateMaterialDecision(
        RetainedFile decisionFile,
        string? expectedRevision,
        string? expectedAppVersion,
        DateTimeOffset? executedUtc,
        Dictionary<string, string> expectedManifestDigests,
        RetainedFiles rehearsalFiles,
        List<string> trustedInputs,
        List<string> failures)
    {
        byte[] bytes;
        try
        {
            bytes = rehearsalFiles.ReadBytes(
                decisionFile,
                MaximumDecisionBytes,
                "accepted release materials decision");
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add(SingleLine(exception.Message));
            return;
        }

        using var document = ParseStrictJson(bytes, "accepted release materials decision");
        var value = document.RootElement;
        if (!RequireExactFields(value, MaterialDecisionFields, "release materials decision", failures))
        {
            return;
        }

        RequireInteger(value.GetProperty("schemaVersion"), 1, "release materials decision.schemaVersion", failures);
        RequireExactText(value.GetProperty("kind"), "release-materials-acceptance-v1", "release materials decision.kind", failures);
        RequireBoolean(value.GetProperty("passed"), true, "release materials decision.passed", failures);
        RequireBoolean(value.GetProperty("foundationQualified"), true, "release materials decision.foundationQualified", failures);
        RequireBoolean(value.GetProperty("candidateMaterialComplete"), true, "release materials decision.candidateMaterialComplete", failures);
        RequireBoolean(value.GetProperty("releaseAcceptance"), true, "release materials decision.releaseAcceptance", failures);
        RequireExactText(value.GetProperty("sourceRevision"), expectedRevision, "release materials decision.sourceRevision", failures);
        RequireExactText(value.GetProperty("appVersion"), expectedAppVersion, "release materials decision.appVersion", failures);
        var candidateSha = RequireDigest(value.GetProperty("candidateSha256"), "release materials decision.candidateSha256", failures);
        var acceptedUtc = ValidateUtc(
            value.GetProperty("acceptedUtc"),
            "release materials decision.acceptedUtc",
            failures);
        if (acceptedUtc is not null && executedUtc is not null && acceptedUtc > executedUtc)
        {
            failures.Add("release materials decision.acceptedUtc must not be later than rehearsal.executedUtc");
        }
        RequireEmptyArray(value.GetProperty("pendingGates"), "release materials decision.pendingGates", failures);
        RequireEmptyArray(value.GetProperty("errors"), "release materials decision.errors", failures);

        var decisionBase = Path.GetDirectoryName(decisionFile.Path)!;
        var decisionFiles = new RetainedFiles(decisionBase, failures);
        var structuralPath = ReadSinglePath(
            value.GetProperty("structuralHandoffPath"),
            "release materials decision.structuralHandoffPath",
            decisionFiles,
            failures,
            exclusive: true);
        var structuralSha = RequireDigest(
            value.GetProperty("structuralHandoffSha256"),
            "release materials decision.structuralHandoffSha256",
            failures);
        if (structuralPath is not null)
        {
            ValidateDigestPair(structuralSha, structuralPath, "release materials structural handoff", decisionFiles, failures);
            if (decisionFiles.TryGet(structuralPath, out var structuralFile))
            {
                ValidateStructuralMaterialsHandoff(
                    structuralFile,
                    expectedRevision,
                    expectedAppVersion,
                    candidateSha,
                    decisionFiles,
                    failures);
            }
        }

        var decisionManifestDigests = ReadDigestMap(
            value.GetProperty("artifactManifestSha256ByPlatform"),
            "release materials decision.artifactManifestSha256ByPlatform",
            failures);
        foreach (var platform in ArtifactPlatforms)
        {
            if (!decisionManifestDigests.TryGetValue(platform, out var actual)
                || !expectedManifestDigests.TryGetValue(platform, out var expected)
                || !string.Equals(actual, expected, StringComparison.Ordinal))
            {
                failures.Add($"release materials decision manifest hash does not match rehearsal for {platform}");
            }
        }

        ValidateMaterialGates(value.GetProperty("gateRecords"), decisionFiles, failures);
        ValidateRetainedHashes(value.GetProperty("retainedFileSha256"), decisionFiles, failures, "release materials decision.retainedFileSha256");
        decisionFiles.VerifyStableSnapshots();
        trustedInputs.AddRange(decisionFiles.ResolvedPaths);
    }

    private static void ValidateStructuralMaterialsHandoff(
        RetainedFile file,
        string? expectedRevision,
        string? expectedAppVersion,
        string? expectedCandidateSha,
        RetainedFiles files,
        List<string> failures)
    {
        var bytes = files.ReadBytes(file, MaximumDecisionBytes, "release materials structural handoff");
        using var document = ParseStrictJson(bytes, "release materials structural handoff");
        var value = document.RootElement;
        if (!RequireExactFields(value, StructuralHandoffFields, "release materials structural handoff", failures))
        {
            return;
        }

        RequireInteger(value.GetProperty("schemaVersion"), 2, "release materials structural handoff.schemaVersion", failures);
        RequireExactText(value.GetProperty("kind"), "release-materials-handoff-v2", "release materials structural handoff.kind", failures);
        RequireBoolean(value.GetProperty("passed"), true, "release materials structural handoff.passed", failures);
        RequireBoolean(value.GetProperty("foundationQualified"), true, "release materials structural handoff.foundationQualified", failures);
        RequireBoolean(value.GetProperty("candidateSupplied"), true, "release materials structural handoff.candidateSupplied", failures);
        RequireBoolean(value.GetProperty("candidateMaterialComplete"), true, "release materials structural handoff.candidateMaterialComplete", failures);
        RequireBoolean(value.GetProperty("releaseAcceptance"), false, "release materials structural handoff.releaseAcceptance", failures);
        RequireLowerHex(
            value.GetProperty("contractSha256"),
            64,
            "release materials structural handoff.contractSha256",
            failures);
        ValidateClosedDigestMap(
            value.GetProperty("documentSha256"),
            MaterialDocumentPaths,
            "release materials structural handoff.documentSha256",
            failures);
        RequireInteger(value.GetProperty("requiredDocumentCount"), 10, "release materials structural handoff.requiredDocumentCount", failures);
        RequireInteger(value.GetProperty("artifactPlatformCount"), 3, "release materials structural handoff.artifactPlatformCount", failures);
        RequireInteger(value.GetProperty("inputDeviceCount"), 4, "release materials structural handoff.inputDeviceCount", failures);
        RequireInteger(value.GetProperty("screenshotRoleCount"), 6, "release materials structural handoff.screenshotRoleCount", failures);
        RequireInteger(value.GetProperty("videoRoleCount"), 2, "release materials structural handoff.videoRoleCount", failures);
        RequireInteger(value.GetProperty("marketingClaimCount"), 8, "release materials structural handoff.marketingClaimCount", failures);
        RequireExactText(value.GetProperty("sourceRevision"), expectedRevision, "release materials structural handoff.sourceRevision", failures);
        RequireExactText(value.GetProperty("appVersion"), expectedAppVersion, "release materials structural handoff.appVersion", failures);
        RequireExactText(value.GetProperty("candidateSha256"), expectedCandidateSha, "release materials structural handoff.candidateSha256", failures);
        RequireExactArray(value.GetProperty("pendingGates"), MaterialGateIds, "release materials structural handoff.pendingGates", failures);
        RequireEmptyArray(value.GetProperty("errors"), "release materials structural handoff.errors", failures);
    }

    private static void ValidateMaterialGates(
        JsonElement value,
        RetainedFiles files,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != MaterialGateIds.Length)
        {
            failures.Add("release materials decision.gateRecords must contain the four ordered acceptance gates");
            return;
        }

        var index = 0;
        foreach (var row in value.EnumerateArray())
        {
            var label = $"release materials decision.gateRecords[{index}]";
            if (RequireExactFields(row, MaterialGateFields, label, failures))
            {
                RequireExactText(row.GetProperty("gateId"), MaterialGateIds[index], $"{label}.gateId", failures);
                RequireExactText(row.GetProperty("result"), "pass", $"{label}.result", failures);
                var role = RequireBoundedText(row.GetProperty("authorityRoleId"), $"{label}.authorityRoleId", failures);
                if (role is not null && !RolePattern.IsMatch(role))
                {
                    failures.Add($"{label}.authorityRoleId must be a non-personal operational role ID");
                }
                _ = ReadPathArray(
                    row.GetProperty("evidencePaths"),
                    $"{label}.evidencePaths",
                    files,
                    failures,
                    MaximumEvidencePaths,
                    exclusive: true);
            }
            index++;
        }
    }

    private static ManifestIdentity? ValidateManifest(
        JsonElement value,
        string platform,
        string? expectedRevision,
        List<string> failures)
    {
        var label = $"candidate manifest {platform}";
        if (!RequireExactFields(value, ManifestFields, label, failures))
        {
            return null;
        }

        RequireInteger(value.GetProperty("schemaVersion"), 3, $"{label}.schemaVersion", failures);
        RequireExactText(value.GetProperty("product"), "Vibe Snake", $"{label}.product", failures);
        RequireExactText(value.GetProperty("platform"), platform, $"{label}.platform", failures);
        RequireExactText(value.GetProperty("buildMode"), "Release", $"{label}.buildMode", failures);
        RequireExactText(value.GetProperty("sourceRevision"), expectedRevision, $"{label}.sourceRevision", failures);
        var godotVersion = RequireBoundedText(value.GetProperty("godotVersion"), $"{label}.godotVersion", failures);
        var godotCommit = RequireBoundedText(value.GetProperty("godotCommit"), $"{label}.godotCommit", failures);
        var dotnetSdk = RequireBoundedText(value.GetProperty("dotnetSdk"), $"{label}.dotnetSdk", failures);
        RequireLowerHex(value.GetProperty("godotArchiveSha512"), 128, $"{label}.godotArchiveSha512", failures);
        RequireLowerHex(value.GetProperty("godotExecutableSha256"), 64, $"{label}.godotExecutableSha256", failures);
        var smoke = RequireLowerHex(value.GetProperty("smokeStateHash"), 16, $"{label}.smokeStateHash", failures);
        RequireBoolean(value.GetProperty("agentArenaPreviewExcluded"), true, $"{label}.agentArenaPreviewExcluded", failures);
        var fileCount = RequireNonnegativeInteger(value.GetProperty("fileCount"), $"{label}.fileCount", failures);
        var totalBytes = RequireNonnegativeInteger(value.GetProperty("totalBytes"), $"{label}.totalBytes", failures);
        var files = ValidateManifestEntries(value.GetProperty("files"), $"{label}.files", required: true, failures);
        _ = ValidateManifestEntries(value.GetProperty("containerEntries"), $"{label}.containerEntries", required: false, failures);
        if (fileCount is not null && fileCount != files.Count)
        {
            failures.Add($"{label}.fileCount must match files array length");
        }
        if (totalBytes is not null && totalBytes != files.Sum(entry => entry.Bytes))
        {
            failures.Add($"{label}.totalBytes must equal the sum of file bytes");
        }

        return godotVersion is null || godotCommit is null || dotnetSdk is null || smoke is null
            ? null
            : new ManifestIdentity(godotVersion, godotCommit, dotnetSdk, smoke);
    }

    private static List<ManifestEntry> ValidateManifestEntries(
        JsonElement value,
        string label,
        bool required,
        List<string> failures)
    {
        var result = new List<ManifestEntry>();
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > MaximumManifestEntries
            || (required && value.GetArrayLength() == 0))
        {
            failures.Add($"{label} must be a bounded{(required ? " nonempty" : string.Empty)} array");
            return result;
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var row in value.EnumerateArray())
        {
            var rowLabel = $"{label}[{index}]";
            if (row.ValueKind != JsonValueKind.Object)
            {
                failures.Add($"{rowLabel} must be an object");
                index++;
                continue;
            }
            var fields = row.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            if (!ManifestEntryRequiredFields.IsSubsetOf(fields)
                || !fields.IsSubsetOf(ManifestEntryAllowedFields))
            {
                failures.Add($"{rowLabel} fields are invalid");
                index++;
                continue;
            }
            var path = RequireBoundedText(row.GetProperty("path"), $"{rowLabel}.path", failures, MaximumRelativePathCharacters);
            if (path is not null && (!IsSafeRelativePath(path, out _) || !paths.Add(path)))
            {
                failures.Add($"{rowLabel}.path must be a unique safe portable relative path");
            }
            var bytes = RequireNonnegativeInteger(row.GetProperty("bytes"), $"{rowLabel}.bytes", failures);
            RequireLowerHex(row.GetProperty("sha256"), 64, $"{rowLabel}.sha256", failures);
            if (row.TryGetProperty("compressedBytes", out var compressed))
            {
                _ = RequireNonnegativeInteger(compressed, $"{rowLabel}.compressedBytes", failures);
            }
            if (path is not null && bytes is not null)
            {
                result.Add(new ManifestEntry(path, bytes.Value));
            }
            index++;
        }
        return result;
    }

    private static void ValidateManifestSet(
        IReadOnlyDictionary<string, ManifestIdentity> manifests,
        List<string> failures)
    {
        if (manifests.Count != ArtifactPlatforms.Length)
        {
            return;
        }
        if (manifests.Values.Select(value => value.GodotVersion).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            failures.Add("candidate manifests must report one Godot version");
        }
        if (manifests.Values.Select(value => value.GodotCommit).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            failures.Add("candidate manifests must report one Godot commit");
        }
        if (manifests.Values.Select(value => value.DotnetSdk).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            failures.Add("candidate manifests must report one .NET SDK version");
        }
        if (manifests.Values.Select(value => value.SmokeStateHash).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            failures.Add("candidate manifests must report one smoke state hash");
        }
    }

    private static void ValidatePlatformResults(
        JsonElement value,
        RetainedFiles files,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != ArtifactPlatforms.Length)
        {
            failures.Add("rehearsal.platformResults must contain exactly three ordered rows");
            return;
        }
        var index = 0;
        foreach (var row in value.EnumerateArray())
        {
            var label = $"rehearsal.platformResults[{index}]";
            if (RequireExactFields(row, PlatformResultFields.ToHashSet(StringComparer.Ordinal), label, failures))
            {
                RequireExactText(row.GetProperty("platformId"), ArtifactPlatforms[index], $"{label}.platformId", failures);
                ValidateOperationResults(row.GetProperty("operationResults"), label, failures);
                ValidateOperationEvidence(row.GetProperty("evidencePathsByOperation"), label, files, failures);
                var before = RequireDigest(row.GetProperty("protectedUserDataSha256Before"), $"{label}.protectedUserDataSha256Before", failures);
                var after = RequireDigest(row.GetProperty("protectedUserDataSha256After"), $"{label}.protectedUserDataSha256After", failures);
                if (before is not null && after is not null && !string.Equals(before, after, StringComparison.Ordinal))
                {
                    failures.Add($"{label} rollback or removal changed protected user data");
                }
            }
            index++;
        }
    }

    private static void ValidateOperationResults(JsonElement value, string parent, List<string> failures)
    {
        var label = $"{parent}.operationResults";
        if (!RequireExactFields(value, PlatformOperationIds.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return;
        }
        foreach (var operation in PlatformOperationIds)
        {
            RequireExactText(value.GetProperty(operation), "pass", $"{label}.{operation}", failures);
        }
    }

    private static void ValidateOperationEvidence(
        JsonElement value,
        string parent,
        RetainedFiles files,
        List<string> failures)
    {
        var label = $"{parent}.evidencePathsByOperation";
        if (!RequireExactFields(value, PlatformOperationIds.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return;
        }
        foreach (var operation in PlatformOperationIds)
        {
            _ = ReadPathArray(value.GetProperty(operation), $"{label}.{operation}", files, failures, MaximumEvidencePaths);
        }
    }

    private static void ValidateWithdrawal(JsonElement value, RetainedFiles files, List<string> failures)
    {
        if (!RequireExactFields(value, WithdrawalFields.ToHashSet(StringComparer.Ordinal), "rehearsal.withdrawalResult", failures))
        {
            return;
        }
        foreach (var field in WithdrawalFields.Take(4))
        {
            RequireBoolean(value.GetProperty(field), true, $"rehearsal.withdrawalResult.{field}", failures);
        }
        _ = ReadPathArray(
            value.GetProperty("evidencePaths"),
            "rehearsal.withdrawalResult.evidencePaths",
            files,
            failures,
            MaximumEvidencePaths);
    }

    private static void ValidateAuthorities(JsonElement value, RetainedFiles files, List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != AuthorityOperationIds.Length)
        {
            failures.Add("rehearsal.authorityRecords must contain exactly four ordered rows");
            return;
        }
        var index = 0;
        foreach (var row in value.EnumerateArray())
        {
            var label = $"rehearsal.authorityRecords[{index}]";
            if (RequireExactFields(row, AuthorityFields.ToHashSet(StringComparer.Ordinal), label, failures))
            {
                RequireExactText(row.GetProperty("operationId"), AuthorityOperationIds[index], $"{label}.operationId", failures);
                var role = RequireBoundedText(row.GetProperty("roleId"), $"{label}.roleId", failures);
                if (role is not null && !RolePattern.IsMatch(role))
                {
                    failures.Add($"{label}.roleId must be a non-personal operational role ID");
                }
                RequireBoolean(row.GetProperty("authorizationVerified"), true, $"{label}.authorizationVerified", failures);
                _ = ReadPathArray(row.GetProperty("evidencePaths"), $"{label}.evidencePaths", files, failures, MaximumEvidencePaths);
            }
            index++;
        }
    }

    private static Dictionary<string, string> ReadDigestMap(
        JsonElement value,
        string label,
        List<string> failures)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!RequireExactFields(value, ArtifactPlatforms.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return result;
        }
        foreach (var platform in ArtifactPlatforms)
        {
            var digest = RequireDigest(value.GetProperty(platform), $"{label}.{platform}", failures);
            if (digest is not null)
            {
                result[platform] = digest;
            }
        }
        return result;
    }

    private static void ValidateClosedDigestMap(
        JsonElement value,
        string[] keys,
        string label,
        List<string> failures)
    {
        if (!RequireExactFields(value, keys.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return;
        }
        foreach (var key in keys)
        {
            RequireDigest(value.GetProperty(key), $"{label}.{key}", failures);
        }
    }

    private static Dictionary<string, string> ReadPathMap(
        JsonElement value,
        string label,
        RetainedFiles files,
        List<string> failures,
        bool exclusive)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!RequireExactFields(value, ArtifactPlatforms.ToHashSet(StringComparer.Ordinal), label, failures))
        {
            return result;
        }
        foreach (var platform in ArtifactPlatforms)
        {
            var path = ReadSinglePath(value.GetProperty(platform), $"{label}.{platform}", files, failures, exclusive);
            if (path is not null)
            {
                result[platform] = path;
            }
        }
        return result;
    }

    private static string? ReadSinglePath(
        JsonElement value,
        string label,
        RetainedFiles files,
        List<string> failures,
        bool exclusive)
    {
        var path = RequireBoundedText(value, label, failures, MaximumRelativePathCharacters);
        if (path is null)
        {
            return null;
        }
        if (!IsSafeRelativePath(path, out var reason))
        {
            failures.Add($"{label} {reason}");
            return null;
        }
        files.Add(path, exclusive ? label : null);
        return path;
    }

    private static List<string> ReadPathArray(
        JsonElement value,
        string label,
        RetainedFiles files,
        List<string> failures,
        int maximum,
        bool exclusive = false)
    {
        var result = new List<string>();
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is < 1
            || value.GetArrayLength() > maximum)
        {
            failures.Add($"{label} must contain 1 to {maximum} unique safe relative paths");
            return result;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in value.EnumerateArray())
        {
            var path = RequireBoundedText(element, label, failures, MaximumRelativePathCharacters);
            if (path is null || !IsSafeRelativePath(path, out _) || !seen.Add(path))
            {
                failures.Add($"{label} must contain unique safe relative paths");
                continue;
            }
            files.Add(path, exclusive ? label : null);
            result.Add(path);
        }
        return result;
    }

    private static void ValidateDigestPair(
        Dictionary<string, string> digests,
        Dictionary<string, string> paths,
        string key,
        string label,
        RetainedFiles files,
        List<string> failures)
    {
        if (digests.TryGetValue(key, out var digest) && paths.TryGetValue(key, out var path))
        {
            ValidateDigestPair(digest, path, $"{label} {key}", files, failures);
        }
    }

    private static void ValidateDigestPair(
        string? digest,
        string path,
        string label,
        RetainedFiles files,
        List<string> failures)
    {
        if (digest is null || !files.TryGet(path, out var file))
        {
            return;
        }
        try
        {
            var actual = files.Hash(file).Sha256;
            if (!string.Equals(actual, digest, StringComparison.Ordinal))
            {
                failures.Add($"{label} hash mismatch");
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            failures.Add($"{label}: {SingleLine(exception.Message)}");
        }
    }

    private static string ComputeFixtureSetSha(IEnumerable<string> paths, RetainedFiles files)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relativePath in paths.Order(StringComparer.Ordinal))
        {
            digest.AppendData(StrictUtf8.GetBytes(relativePath));
            digest.AppendData([0]);
            var file = files.Get(relativePath);
            digest.AppendData(Convert.FromHexString(files.Hash(file).Sha256));
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static void ValidateRetainedHashes(
        JsonElement value,
        RetainedFiles files,
        List<string> failures,
        string label = "rehearsal.retainedFileSha256")
    {
        var expected = files.RelativePaths.ToHashSet(StringComparer.Ordinal);
        if (!RequireExactFields(value, expected, label, failures))
        {
            return;
        }
        foreach (var relativePath in expected.Order(StringComparer.Ordinal))
        {
            var expectedSha = RequireDigest(value.GetProperty(relativePath), $"{label}.{relativePath}", failures);
            if (expectedSha is null || !files.TryGet(relativePath, out var file))
            {
                continue;
            }
            try
            {
                if (!string.Equals(files.Hash(file).Sha256, expectedSha, StringComparison.Ordinal))
                {
                    failures.Add($"retained rehearsal file hash mismatch: {relativePath}");
                }
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                failures.Add($"{relativePath}: {SingleLine(exception.Message)}");
            }
        }
    }

    private static string RenderEvidence(
        string[] failures,
        bool protocolQualified,
        string? contractSha,
        string? materialAcceptanceContractSha,
        Dictionary<string, string> prerequisites,
        bool recordSupplied,
        string? recordSha,
        bool recordAccepted,
        string? sourceRevision,
        string? appVersion,
        string? previousVersion,
        string? materialsDecisionSha,
        IReadOnlyDictionary<string, string> candidateArtifactHashes,
        IReadOnlyDictionary<string, string> candidateManifestHashes)
    {
        var prerequisiteJson = new JsonObject();
        foreach (var path in PrerequisitePaths.Append(MaterialAcceptanceContractRelativePath))
        {
            if (prerequisites.TryGetValue(path, out var digest))
            {
                prerequisiteJson[path] = digest;
            }
        }
        var root = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["kind"] = "release-rehearsal-handoff-v2",
            ["passed"] = failures.Length == 0,
            ["protocolQualified"] = protocolQualified,
            ["contractSha256"] = contractSha,
            ["materialAcceptanceContractSha256"] = materialAcceptanceContractSha,
            ["prerequisiteSha256"] = prerequisiteJson,
            ["artifactPlatformCount"] = ArtifactPlatforms.Length,
            ["platformOperationCount"] = PlatformOperationIds.Length,
            ["requiredPlatformOperationCellCount"] = ArtifactPlatforms.Length * PlatformOperationIds.Length,
            ["authorityOperationCount"] = AuthorityOperationIds.Length,
            ["recordSupplied"] = recordSupplied,
            ["recordSha256"] = recordSha,
            ["recordIntegrityQualified"] = recordAccepted,
            ["externalExecutionAttested"] = recordAccepted,
            ["rehearsalComplete"] = recordAccepted,
            ["releaseAcceptance"] = recordAccepted,
            ["sourceRevision"] = sourceRevision,
            ["appVersion"] = appVersion,
            ["previousVersion"] = previousVersion,
            ["releaseMaterialsDecisionSha256"] = materialsDecisionSha,
            ["candidateArtifactSha256ByPlatform"] = PlatformJson(candidateArtifactHashes),
            ["candidateManifestSha256ByPlatform"] = PlatformJson(candidateManifestHashes),
            ["pendingGates"] = new JsonArray(
                (recordAccepted ? [] : PendingGates)
                    .Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()),
            ["errors"] = new JsonArray(failures.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()),
        };
        var json = root.ToJsonString(RenderOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        if (StrictUtf8.GetByteCount(json) > MaximumOutputBytes)
        {
            throw new InvalidDataException(
                $"release rehearsal evidence exceeds the {MaximumOutputBytes}-byte output limit");
        }
        return json;
    }

    private static JsonObject PlatformJson(IReadOnlyDictionary<string, string> values)
    {
        var result = new JsonObject();
        foreach (var platform in ArtifactPlatforms)
        {
            if (values.TryGetValue(platform, out var value))
            {
                result[platform] = value;
            }
        }
        return result;
    }

    private static Dictionary<string, string> EmptyPlatformMap() => new(StringComparer.Ordinal);

    private static void WriteAtomicEvidence(
        string root,
        IReadOnlyList<string> trustedInputs,
        string outputPath,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var path = Path.GetFullPath(
            Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(root, outputPath));
        EnsureContained(root, path, "release rehearsal evidence output");
        if (trustedInputs.Any(input => PathsAlias(input, path)))
        {
            throw new InvalidDataException(
                "release rehearsal evidence output cannot alias a qualification input");
        }
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("release rehearsal evidence output has no parent directory");
        CreateLinkFreeDirectory(root, parent, "release rehearsal evidence output parent");
        if (Path.Exists(path))
        {
            EnsureNoLinks(root, path, "release rehearsal evidence output");
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            {
                throw new InvalidDataException("release rehearsal evidence output must be a regular file");
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
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException("release rehearsal evidence write verification failed");
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
        return RequireRegularFile(path, maximumBytes, label);
    }

    private static string ResolveExplicitRegularFile(string value, long maximumBytes, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var path = Path.GetFullPath(value);
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException($"{label} has no parent directory");
        if (!Directory.Exists(parent)
            || (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"{label} parent must be a regular non-link directory");
        }
        EnsureNoLinks(parent, path, label);
        return RequireRegularFile(path, maximumBytes, label);
    }

    private static string RequireRegularFile(string path, long maximumBytes, string label)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"missing {label}: {path.Replace('\\', '/')}");
        }
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException($"{label} must be a regular non-link file");
        }
        var length = new FileInfo(path).Length;
        if (length == 0)
        {
            throw new InvalidDataException($"{label} must be nonempty");
        }
        if (length > maximumBytes)
        {
            throw new InvalidDataException($"{label} exceeds the {maximumBytes}-byte validation limit");
        }
        return path;
    }

    private static byte[] ReadBoundedStableBytes(string path, long maximumBytes, string label)
    {
        var before = new FileInfo(path);
        using var source = OpenRead(path);
        if (source.Length > maximumBytes || source.Length > int.MaxValue)
        {
            throw new InvalidDataException($"{label} exceeds the {maximumBytes}-byte validation limit");
        }
        var bytes = new byte[checked((int)source.Length)];
        ReadExact(source, bytes);
        if (source.ReadByte() != -1)
        {
            throw new InvalidDataException($"{label} grew while it was read");
        }
        var after = new FileInfo(path);
        if (!after.Exists
            || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || before.Length != after.Length
            || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
        {
            throw new InvalidDataException($"{label} changed while it was read");
        }
        return bytes;
    }

    private static FileSnapshot HashStableFile(string path, long maximumBytes, string label)
    {
        var before = new FileInfo(path);
        using var source = OpenRead(path);
        if (source.Length > maximumBytes)
        {
            throw new InvalidDataException($"{label} exceeds the {maximumBytes}-byte validation limit");
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        int count;
        while ((count = source.Read(buffer)) > 0)
        {
            total = checked(total + count);
            if (total > maximumBytes)
            {
                throw new InvalidDataException($"{label} exceeds the {maximumBytes}-byte validation limit");
            }
            hash.AppendData(buffer.AsSpan(0, count));
        }
        var after = new FileInfo(path);
        if (!after.Exists
            || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || before.Length != total
            || after.Length != total
            || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
        {
            throw new InvalidDataException($"{label} changed while it was hashed");
        }
        return new FileSnapshot(
            total,
            after.LastWriteTimeUtc,
            Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        1024 * 1024,
        FileOptions.SequentialScan);

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
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumJsonDepth,
        });
        RejectDuplicateProperties(document.RootElement, label);
        return document;
    }

    private static void RejectDuplicateProperties(JsonElement value, string label)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    throw new InvalidDataException($"{label} repeats JSON field: {property.Name}");
                }
                RejectDuplicateProperties(property.Value, $"{label}.{property.Name}");
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateProperties(item, $"{label}[{index++}]");
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
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != expected.Length)
        {
            failures.Add($"{label} must equal [{string.Join(", ", expected)}]");
            return;
        }
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || !string.Equals(item.GetString(), expected[index++], StringComparison.Ordinal))
            {
                failures.Add($"{label} must equal [{string.Join(", ", expected)}]");
                return;
            }
        }
    }

    private static void RequireEmptyArray(JsonElement value, string label, List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 0)
        {
            failures.Add($"{label} must be an empty array");
        }
    }

    private static void RequireInteger(JsonElement value, int expected, string label, List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var actual)
            || actual != expected)
        {
            failures.Add($"{label} must be integer {expected}");
        }
    }

    private static long? RequireNonnegativeInteger(JsonElement value, string label, List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var actual)
            || actual < 0)
        {
            failures.Add($"{label} must be a nonnegative integer");
            return null;
        }
        return actual;
    }

    private static void RequireBoolean(JsonElement value, bool expected, string label, List<string> failures)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || value.GetBoolean() != expected)
        {
            failures.Add($"{label} must be {expected.ToString().ToLowerInvariant()}");
        }
    }

    private static void RequireExactText(JsonElement value, string? expected, string label, List<string> failures)
    {
        if (expected is null
            || value.ValueKind != JsonValueKind.String
            || !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            failures.Add($"{label} must be '{expected ?? "<unavailable>"}'");
        }
    }

    private static string? RequireBoundedText(
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
        var result = value.GetString()!;
        if (string.IsNullOrWhiteSpace(result)
            || !result.IsNormalized(NormalizationForm.FormC)
            || result.EnumerateRunes().Take(maximumCharacters + 1).Count() > maximumCharacters)
        {
            failures.Add(
                $"{label} must be a nonempty NFC string up to {maximumCharacters} characters");
            return null;
        }
        return result;
    }

    private static string? RequireDigest(JsonElement value, string label, List<string> failures) =>
        RequireLowerHex(value, 64, label, failures);

    private static string? RequireLowerHex(
        JsonElement value,
        int length,
        string label,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.String || !IsLowerHex(value.GetString(), length))
        {
            failures.Add($"{label} must be {length} lowercase hexadecimal characters");
            return null;
        }
        return value.GetString();
    }

    private static DateTimeOffset? ValidateUtc(
        JsonElement value,
        string label,
        List<string> failures)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (text is null
            || !DateTimeOffset.TryParseExact(
                text,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
            || !string.Equals(
                parsed.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                text,
                StringComparison.Ordinal))
        {
            failures.Add($"{label} must use a valid YYYY-MM-DDTHH:MM:SSZ UTC timestamp");
            return null;
        }
        return parsed;
    }

    private static bool TryParseVersion(string value, out ComparableVersion version)
    {
        var match = VersionPattern.Match(value);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            version = default;
            return false;
        }
        var channel = match.Groups[4].Success
            ? match.Groups[4].Value switch
            {
                "alpha" => 0,
                "beta" => 1,
                "rc" => 2,
                _ => -1,
            }
            : 3;
        var sequence = 0;
        if (channel < 3
            && !int.TryParse(
                match.Groups[5].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sequence))
        {
            version = default;
            return false;
        }
        version = new ComparableVersion(major, minor, patch, channel, sequence);
        return true;
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
            || value.Any(char.IsControl))
        {
            return false;
        }
        foreach (var segment in value.Split('/'))
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
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!NormalizePath(path).StartsWith(NormalizePath(prefix), PathComparison()))
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
        var current = root;
        foreach (var segment in GetContainedRelativePath(root, path, label).Split(
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

    private static string GetContainedRelativePath(string root, string path, string label)
    {
        var relative = Path.GetRelativePath(root, path);
        var first = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (Path.IsPathRooted(relative) || first == "..")
        {
            throw new InvalidDataException($"{label} must be inside its trusted root");
        }
        return relative == "." ? string.Empty : relative;
    }

    private static bool IsLowerHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void ReadExact(Stream source, Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var count = source.Read(destination[offset..]);
            if (count == 0)
            {
                throw new InvalidDataException("file ended before the declared content was complete");
            }
            offset += count;
        }
    }

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

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static bool PathsAlias(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), PathComparison());

    private static string NormalizePath(string value) => value.Normalize(NormalizationForm.FormC);

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
        var runes = SingleLine(value).EnumerateRunes().Take(MaximumFailureCharacters + 1).ToArray();
        return runes.Length <= MaximumFailureCharacters
            ? string.Concat(runes.Select(rune => rune.ToString()))
            : string.Concat(runes.Take(MaximumFailureCharacters).Select(rune => rune.ToString())) + "...";
    }

    private static RepositoryCheckResult Failed(string[] failures) =>
        new("Release rehearsal", false, string.Empty, BoundFailures(failures));

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    private sealed record Qualification(
        string OutputRoot,
        string[] Failures,
        bool RecordAccepted,
        string Json,
        IReadOnlyList<string> TrustedInputs);

    private sealed record RecordQualification(
        string SourceRevision,
        string PreviousVersion,
        string ReleaseMaterialsDecisionSha256,
        IReadOnlyDictionary<string, string> CandidateArtifactSha256ByPlatform,
        IReadOnlyDictionary<string, string> CandidateManifestSha256ByPlatform,
        IReadOnlyList<string> TrustedInputs);

    private sealed record ManifestIdentity(
        string GodotVersion,
        string GodotCommit,
        string DotnetSdk,
        string SmokeStateHash);

    private sealed record ManifestEntry(string Path, long Bytes);

    private readonly record struct ComparableVersion(
        int Major,
        int Minor,
        int Patch,
        int Channel,
        int Sequence) : IComparable<ComparableVersion>
    {
        public int CompareTo(ComparableVersion other)
        {
            var result = Major.CompareTo(other.Major);
            if (result != 0)
            {
                return result;
            }
            result = Minor.CompareTo(other.Minor);
            if (result != 0)
            {
                return result;
            }
            result = Patch.CompareTo(other.Patch);
            if (result != 0)
            {
                return result;
            }
            result = Channel.CompareTo(other.Channel);
            return result != 0 ? result : Sequence.CompareTo(other.Sequence);
        }
    }

    private sealed record RetainedFile(string Path, string RelativePath, long Length);

    private readonly record struct FileSnapshot(
        long Length,
        DateTime LastWriteTimeUtc,
        string Sha256);

    private sealed class RetainedFiles
    {
        private readonly string root;
        private readonly List<string> failures;
        private readonly Dictionary<string, RetainedFile> files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> foldedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> exclusivePaths = new(PathComparer());
        private readonly Dictionary<string, FileSnapshot> snapshots = new(PathComparer());
        private long totalBytes;

        public RetainedFiles(string root, List<string> failures)
        {
            this.root = Path.GetFullPath(root);
            this.failures = failures;
            if (!Directory.Exists(this.root)
                || (File.GetAttributes(this.root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("retained-file root must be a regular non-link directory");
            }
        }

        public IEnumerable<string> RelativePaths => files.Keys;
        public IEnumerable<string> ResolvedPaths => files.Values.Select(file => file.Path);

        public void Add(string relativePath, string? exclusiveLabel)
        {
            if (files.ContainsKey(relativePath))
            {
                if (exclusiveLabel is not null)
                {
                    ReserveExclusive(files[relativePath].Path, exclusiveLabel);
                }
                return;
            }
            if (files.Count == MaximumRetainedFiles)
            {
                failures.Add($"retained files exceed the {MaximumRetainedFiles}-file limit");
                return;
            }
            if (foldedPaths.TryGetValue(relativePath, out var existing)
                && !string.Equals(existing, relativePath, StringComparison.Ordinal))
            {
                failures.Add($"retained paths collide by portable case: {existing}, {relativePath}");
                return;
            }
            foldedPaths[relativePath] = relativePath;
            try
            {
                var path = Path.GetFullPath(
                    Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                EnsureContained(root, path, "retained file");
                EnsureNoLinks(root, path, "retained file");
                path = RequireRegularFile(path, MaximumRetainedFileBytes, "retained file");
                var length = new FileInfo(path).Length;
                totalBytes = checked(totalBytes + length);
                if (totalBytes > MaximumRetainedTotalBytes)
                {
                    throw new InvalidDataException(
                        $"retained files exceed the {MaximumRetainedTotalBytes}-byte aggregate limit");
                }
                files[relativePath] = new RetainedFile(path, relativePath, length);
                if (exclusiveLabel is not null)
                {
                    ReserveExclusive(path, exclusiveLabel);
                }
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                failures.Add($"{relativePath}: {SingleLine(exception.Message)}");
            }
        }

        public bool TryGet(string relativePath, out RetainedFile file) =>
            files.TryGetValue(relativePath, out file!);

        public RetainedFile Get(string relativePath) => files[relativePath];

        public FileSnapshot Hash(RetainedFile file)
        {
            if (!snapshots.TryGetValue(file.Path, out var snapshot))
            {
                snapshot = HashStableFile(file.Path, MaximumRetainedFileBytes, "retained file");
                if (snapshot.Length != file.Length)
                {
                    throw new InvalidDataException("retained file changed before hashing");
                }
                snapshots[file.Path] = snapshot;
            }
            return snapshot;
        }

        public byte[] ReadBytes(RetainedFile file, long maximumBytes, string label)
        {
            var bytes = ReadBoundedStableBytes(file.Path, maximumBytes, label);
            var snapshot = new FileSnapshot(
                bytes.LongLength,
                new FileInfo(file.Path).LastWriteTimeUtc,
                Sha256(bytes));
            if (snapshot.Length != file.Length)
            {
                throw new InvalidDataException($"{label} changed before reading");
            }
            if (snapshots.TryGetValue(file.Path, out var existing)
                && !string.Equals(existing.Sha256, snapshot.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{label} changed between validation reads");
            }
            snapshots[file.Path] = snapshot;
            return bytes;
        }

        public void VerifyStableSnapshots()
        {
            foreach (var (path, snapshot) in snapshots)
            {
                var current = HashStableFile(path, MaximumRetainedFileBytes, "retained file");
                if (current.Length != snapshot.Length
                    || current.LastWriteTimeUtc != snapshot.LastWriteTimeUtc
                    || !string.Equals(current.Sha256, snapshot.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("retained file changed after validation");
                }
            }
        }

        private void ReserveExclusive(string path, string label)
        {
            if (exclusivePaths.TryGetValue(path, out var existing)
                && !string.Equals(existing, label, StringComparison.Ordinal))
            {
                failures.Add($"qualification inputs cannot alias: {existing}, {label}");
                return;
            }
            exclusivePaths[path] = label;
        }
    }
}
