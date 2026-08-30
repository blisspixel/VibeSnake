using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public static class StablePromotionCheck
{
    private const string ContractRelativePath = "config/stable_promotion_v1.json";
    private const string AuthorityRelativePath = "config/stable_upstream_acceptance_v1.json";
    private const int MaximumJsonDepth = 64;
    private const int MaximumFailures = 128;
    private const int MaximumFailureCharacters = 256;
    private const int MaximumRelativePathCharacters = 512;
    private const int MaximumTextCharacters = 4096;
    private const int MaximumEvidencePaths = 16;
    private const int MaximumRetainedFiles = 512;
    private const int MaximumManifestEntries = 4096;
    private const int MaximumOutputBytes = 256 * 1024;
    private const long MaximumContractBytes = 1024 * 1024;
    private const long MaximumRecordBytes = 4 * 1024 * 1024;
    private const long MaximumDecisionBytes = 4 * 1024 * 1024;
    private const long MaximumManifestBytes = 8 * 1024 * 1024;
    private const long MaximumChecksumBytes = 1024 * 1024;
    private const long MaximumRetainedFileBytes = 8L * 1024 * 1024 * 1024;
    private const long MaximumRetainedTotalBytes = 64L * 1024 * 1024 * 1024;
    private const string StableVersion = "1.0.0";
    private const string StableTag = "1.0.0";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex RevisionPattern = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex WorkflowRunPattern = new(
        "^[1-9][0-9]{5,19}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex RolePattern = new(
        "^[a-z0-9][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(alpha|beta|rc)\.([1-9][0-9]*))?$",
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

    private static readonly string[] UpstreamDecisionIds =
    [
        "release-matrix",
        "manual-product-matrix",
        "external-validation",
        "release-materials",
        "release-rehearsal",
        "content-approval",
        "hardware-performance",
        "accessibility-human-review",
        "human-playtest",
        "platform-signing",
    ];

    private static readonly string[] PreservedEvidenceCategories =
    [
        "build-logs",
        "manifests",
        "sbom",
        "checksums",
        "migration-fixtures",
        "previous-artifacts",
        "support-record",
    ];

    private static readonly string[] StableContractAcknowledgements =
    [
        "patch-releases-preserve-scored-rules-unless-a-disclosed-correctness-or-exploit-fix-requires-change",
        "save-migrations-remain-nondestructive-and-tested",
        "existing-score-categories-retain-rules-identity",
        "removed-content-remains-visible-as-missing-or-incompatible",
        "accessibility-support-is-regression-tested",
        "offline-core-play-requires-no-account-or-network",
    ];

    private static readonly string[] RecordFields =
    [
        "schemaVersion", "kind", "sourceRevision", "appVersion", "tagName",
        "tagObjectRevision", "protectedWorkflowRunId", "artifactSha256ByPlatform",
        "artifactPathsByPlatform", "manifestSha256ByPlatform", "manifestPathsByPlatform",
        "provenanceSha256ByPlatform", "provenancePathsByPlatform", "checksumPathsByPlatform",
        "optionalPackSha256", "optionalPackPath", "optionalPackManifestSha256",
        "optionalPackManifestPath", "upstreamDecisionPathsById", "publicInstallResults",
        "preservedEvidencePathsByCategory", "stableContractAcknowledgements", "retainedFileSha256",
    ];

    private static readonly string[] PublicInstallFields =
        ["platformId", "result", "installedArtifactSha256", "smokeStateHash", "evidencePaths"];

    private static readonly string[] ReleaseRules =
    [
        "The protected workflow rebuilds from tag 1.0.0 at the exact reviewed source revision.",
        "All ten upstream decisions pass and explicitly accept release for the same source revision.",
        "All three public artifacts, manifests, provenance bundles, and checksum files are retained and hash-verified.",
        "The exact approved optional pack and manifest are retained and hash-verified separately from the core player.",
        "One public-file install and deterministic smoke passes on every platform using the published artifact bytes.",
        "Build logs, manifests, SBOM, checksums, migration fixtures, previous artifacts, and support records are preserved.",
        "The stable compatibility contract is acknowledged exactly and cannot be weakened during promotion.",
        "Renaming, copying, or manually uploading qualification artifacts cannot satisfy stable promotion.",
    ];

    private static readonly string[] PendingGates =
    [
        "all-upstream-release-decisions",
        "protected-1.0.0-tag-rebuild",
        "signed-attested-public-artifacts",
        "approved-optional-pack",
        "public-file-three-platform-install",
        "complete-preserved-release-record",
    ];

    private static readonly string[] GenericDecisionFields =
    [
        "schemaVersion", "kind", "passed", "releaseAcceptance", "sourceRevision", "appVersion",
        "candidateArtifactSha256ByPlatform", "candidateManifestSha256ByPlatform", "acceptedUtc",
        "gateRecords", "retainedFileSha256", "pendingGates", "errors",
    ];

    private static readonly string[] GenericGateFields =
        ["gateId", "result", "authorityRoleId", "evidencePaths"];

    private static readonly Dictionary<string, AcceptanceProfile> ExpectedProfiles =
        new Dictionary<string, AcceptanceProfile>(StringComparer.Ordinal)
        {
            ["release-matrix"] = new("release-matrix-acceptance-v1", "unsigned-candidate", []),
            ["manual-product-matrix"] = new("manual-product-matrix-acceptance-v1", "unsigned-candidate", []),
            ["external-validation"] = new("external-validation-acceptance-v1", "unsigned-candidate", []),
            ["release-materials"] = new("release-materials-acceptance-v1", "unsigned-candidate", []),
            ["release-rehearsal"] = new("release-rehearsal-handoff-v2", "unsigned-candidate", []),
            ["content-approval"] = new(
                "content-approval-acceptance-v1",
                "unsigned-candidate",
                ["optionalPackSha256", "optionalPackManifestSha256"]),
            ["hardware-performance"] = new("hardware-performance-acceptance-v1", "unsigned-candidate", []),
            ["accessibility-human-review"] = new(
                "accessibility-human-review-acceptance-v1",
                "unsigned-candidate",
                []),
            ["human-playtest"] = new("human-playtest-acceptance-v1", "unsigned-candidate", []),
            ["platform-signing"] = new(
                "platform-signing-acceptance-v1",
                "post-signing-public",
                ["inputArtifactSha256ByPlatform", "inputManifestSha256ByPlatform", "provenanceSha256ByPlatform"]),
        };

    private static readonly Dictionary<string, string[]> ExpectedGateIds =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["release-matrix"] =
            [
                "three-platform-release-matrix",
                "release-artifact-manifest-and-checksum-reconciliation",
                "deterministic-package-and-smoke-identity",
            ],
            ["manual-product-matrix"] =
            [
                "retained-windows-x64-full-flow",
                "retained-macos-universal-apple-silicon-full-flow",
                "retained-macos-universal-intel-full-flow",
                "retained-linux-x64-full-flow",
                "physical-input-audio-accessibility-profile-coverage",
            ],
            ["external-validation"] =
            [
                "controlled-real-artifact-distribution",
                "clean-install-fresh-participants",
                "structured-defect-comprehension-accessibility-crash-reports",
                "fresh-participant-comprehension-and-replay-intent",
                "clean-candidate-fix-and-gate-rerun-loop",
            ],
            ["release-materials"] =
            [
                "artifact-manifest-size-reconciliation",
                "marketing-claim-approval",
                "visible-image-review",
                "video-playback-review",
            ],
            ["release-rehearsal"] =
            [
                "staged-final-artifacts-and-checksums",
                "three-platform-install-update-rollback-removal",
                "optional-content-lifecycle",
                "withdrawal-and-previous-artifact-restoration",
                "user-data-preservation",
                "verified-release-authority-roles",
            ],
            ["content-approval"] =
            [
                "core-content-approval",
                "optional-pack-content-approval",
                "rights-credits-and-notices-reconciliation",
                "listening-review",
            ],
            ["hardware-performance"] =
            [
                "named-minimum-hardware-performance",
                "named-recommended-hardware-performance",
                "resolution-presentation-review",
                "long-session-resource-review",
            ],
            ["accessibility-human-review"] =
            [
                "physical-input-accessibility-review",
                "accessibility-user-review",
                "photosensitivity-review",
                "maximum-text-scale-review",
            ],
            ["human-playtest"] =
            [
                "formative-participant-review",
                "targeted-follow-up-review",
                "fresh-validation-review",
                "experience-target-range-acceptance",
            ],
            ["platform-signing"] =
            [
                "windows-signing-verification",
                "macos-signing-notarization-stapling-verification",
                "linux-runtime-baseline-and-desktop-integration",
                "provenance-verification",
            ],
        };

    private static readonly string[] AuthorityReleaseRules =
    [
        "The ten decision IDs, their accepted kinds, their required fields, and their ordered gate IDs are closed by this authority.",
        "Every generic accepted decision has exactly the generic fields plus its profile fields, and every special accepted decision has exactly its separately authorized schema.",
        "A structural release-materials-handoff-v2 is never an accepted release-material decision; only release-materials-acceptance-v1 can satisfy that decision ID.",
        "Release-rehearsal-handoff-v2 is accepted only when record integrity, external execution, rehearsal completion, and release acceptance are true and pending gates and errors are empty.",
        "Every accepted decision passes, accepts release, and binds the exact stable-promotion source revision and application version.",
        "The nine unsigned-candidate decisions bind one reviewed pre-signing artifact and manifest cohort; the special release-material decision binds that cohort's manifest map through the accepted rehearsal and structural-material identities.",
        "Content approval additionally matches the exact optional-pack and optional-pack-manifest SHA-256 values in the stable-promotion record.",
        "Platform signing binds its input artifact and manifest SHA-256 maps to the reviewed unsigned cohort, then binds its candidate artifact, candidate manifest, and provenance SHA-256 maps to the final public files in the stable-promotion record.",
        "Every generic gate record appears once in authority order, has result pass, uses a non-personal operational role ID, and retains at least one unique evidence file.",
        "Decision paths, gate evidence paths, and retained-file keys are unique portable NFC relative paths contained beneath one link-free trust root, with no aliases or portable case collisions.",
        "Each generic retained-file hash map is the exact closure of its gate evidence paths, and every retained regular file is bounded, read stably, and matched to its lowercase SHA-256 digest.",
        "Special release-material and release-rehearsal decisions retain and hash their complete nested evidence according to their own checked-in authorities, and stable promotion rechecks every referenced decision file digest.",
        "Pending gates and errors are empty, timestamps are canonical UTC, and automated validation never performs or invents approval, human review, hardware execution, signing, or publication.",
    ];

    private static readonly HashSet<string> ContractFields = Set(
        "schemaVersion", "kind", "status", "stableVersion", "stableTag", "artifactPlatforms",
        "upstreamDecisionIds", "preservedEvidenceCategories", "stableContractAcknowledgements",
        "requiredRecordFields", "requiredPublicInstallFields", "releaseRules");

    private static readonly HashSet<string> AuthorityFields = Set(
        "schemaVersion", "kind", "status", "artifactPlatforms", "decisionKindsById", "gateIdsById",
        "requiredGenericDecisionFields", "requiredGateFields", "releaseRules");

    private static readonly HashSet<string> GenericDecisionFieldSet = Set(GenericDecisionFields);
    private static readonly HashSet<string> ContentDecisionFieldSet = Set(
        [.. GenericDecisionFields, "optionalPackSha256", "optionalPackManifestSha256"]);
    private static readonly HashSet<string> SigningDecisionFieldSet = Set(
        [.. GenericDecisionFields, "inputArtifactSha256ByPlatform", "inputManifestSha256ByPlatform",
            "provenanceSha256ByPlatform"]);
    private static readonly HashSet<string> MaterialDecisionFields = Set(
        "schemaVersion", "kind", "passed", "foundationQualified", "candidateMaterialComplete",
        "releaseAcceptance", "sourceRevision", "appVersion", "candidateSha256",
        "structuralHandoffPath", "structuralHandoffSha256", "artifactManifestSha256ByPlatform",
        "acceptedUtc", "gateRecords", "retainedFileSha256", "pendingGates", "errors");
    private static readonly HashSet<string> MaterialStructuralFields = Set(
        "schemaVersion", "kind", "passed", "foundationQualified", "contractSha256",
        "documentSha256", "requiredDocumentCount", "artifactPlatformCount", "inputDeviceCount",
        "screenshotRoleCount", "videoRoleCount", "marketingClaimCount", "candidateSupplied",
        "candidateMaterialComplete", "releaseAcceptance", "sourceRevision", "appVersion",
        "candidateSha256", "pendingGates", "errors");
    private static readonly HashSet<string> RehearsalFields = Set(
        "schemaVersion", "kind", "passed", "protocolQualified", "contractSha256",
        "prerequisiteSha256", "artifactPlatformCount", "platformOperationCount",
        "requiredPlatformOperationCellCount", "authorityOperationCount", "recordSupplied",
        "recordSha256", "recordIntegrityQualified", "externalExecutionAttested",
        "rehearsalComplete", "releaseAcceptance", "sourceRevision", "appVersion",
        "previousVersion", "releaseMaterialsDecisionSha256", "candidateArtifactSha256ByPlatform",
        "candidateManifestSha256ByPlatform", "pendingGates", "errors");
    private static readonly HashSet<string> ManifestFields = Set(
        "schemaVersion", "product", "platform", "buildMode", "sourceRevision", "godotVersion",
        "godotCommit", "godotArchiveSha512", "godotExecutableSha256", "dotnetSdk",
        "smokeStateHash", "agentArenaPreviewExcluded", "fileCount", "totalBytes", "files",
        "containerEntries");
    private static readonly HashSet<string> ManifestEntryRequiredFields = Set("path", "bytes", "sha256");
    private static readonly HashSet<string> ManifestEntryAllowedFields = Set(
        "path", "bytes", "sha256", "compressedBytes");

    private static readonly string[] MaterialGateIds =
    [
        "artifact-manifest-size-reconciliation",
        "marketing-claim-approval",
        "visible-image-review",
        "video-playback-review",
    ];

    private static readonly string[] MaterialPendingGates = MaterialGateIds;

    private static readonly string[] RehearsalPrerequisites =
    [
        "config/release_materials_v1.json",
        "config/release_signing_policy.json",
        "docs/release/PACKAGING.md",
        "docs/release/SIGNING.md",
        "docs/guides/RECOVERY.md",
    ];

    private static readonly string[] MaterialDocuments =
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
                    "Stable promotion",
                    true,
                    qualification.RecordAccepted
                        ? "Stable 1.0 promotion accepted for the exact protected-workflow record."
                        : "Stable promotion guard qualified; protected 1.0 execution remains pending.",
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
        var failures = new List<string>();
        var trustedInputs = new List<string>();
        var contractPath = ResolveRegularFile(
            root,
            ContractRelativePath,
            MaximumContractBytes,
            "stable promotion contract");
        var authorityPath = ResolveRegularFile(
            root,
            AuthorityRelativePath,
            MaximumContractBytes,
            "stable upstream acceptance contract");
        trustedInputs.Add(contractPath);
        trustedInputs.Add(authorityPath);
        var contractBytes = ReadBoundedStableBytes(contractPath, MaximumContractBytes, "stable promotion contract");
        var authorityBytes = ReadBoundedStableBytes(
            authorityPath,
            MaximumContractBytes,
            "stable upstream acceptance contract");
        ValidateContract(contractBytes, failures);
        var authority = ValidateAuthority(authorityBytes, failures);
        var guardQualified = failures.Count == 0;

        RecordIdentity? identity = null;
        string? recordSha = null;
        var outputRoot = root;
        if (recordPath is not null)
        {
            var absoluteRecord = ResolveExplicitRegularFile(
                recordPath,
                MaximumRecordBytes,
                "stable promotion record");
            trustedInputs.Add(absoluteRecord);
            outputRoot = Path.GetDirectoryName(absoluteRecord)!;
            if (expectedRevision is null || !RevisionPattern.IsMatch(expectedRevision))
            {
                failures.Add("an exact lowercase 40-character expected revision is required with a promotion record");
            }
            else
            {
                var bytes = ReadBoundedStableBytes(
                    absoluteRecord,
                    MaximumRecordBytes,
                    "stable promotion record");
                recordSha = Sha256(bytes);
                using var document = ParseStrictJson(bytes, "stable promotion record");
                var files = new RetainedFiles(outputRoot, failures);
                identity = ValidateRecord(
                    root,
                    document.RootElement,
                    expectedRevision,
                    authority,
                    files,
                    failures);
                files.VerifyStableSnapshots();
                trustedInputs.AddRange(files.ResolvedPaths);
            }
        }

        var bounded = BoundFailures(failures);
        var accepted = recordPath is not null && identity is not null && bounded.Length == 0;
        var json = RenderEvidence(
            bounded,
            contractBytes,
            authorityBytes,
            guardQualified,
            recordPath is not null,
            recordSha,
            accepted ? identity : null);
        return new Qualification(outputRoot, bounded, accepted, json, trustedInputs.Distinct(PathComparer()).ToArray());
    }

    private static void ValidateContract(byte[] bytes, List<string> failures)
    {
        using var document = ParseStrictJson(bytes, "stable promotion contract");
        var value = document.RootElement;
        if (!RequireExactFields(value, ContractFields, "contract", failures))
        {
            return;
        }
        RequireInteger(value.GetProperty("schemaVersion"), 1, "contract.schemaVersion", failures);
        RequireExactText(value.GetProperty("kind"), "vibesnake-stable-promotion-v1", "contract.kind", failures);
        RequireExactText(value.GetProperty("status"), "guard-qualified-promotion-pending", "contract.status", failures);
        RequireExactText(value.GetProperty("stableVersion"), StableVersion, "contract.stableVersion", failures);
        RequireExactText(value.GetProperty("stableTag"), StableTag, "contract.stableTag", failures);
        RequireExactArray(value.GetProperty("artifactPlatforms"), ArtifactPlatforms, "contract.artifactPlatforms", failures);
        RequireExactArray(value.GetProperty("upstreamDecisionIds"), UpstreamDecisionIds, "contract.upstreamDecisionIds", failures);
        RequireExactArray(
            value.GetProperty("preservedEvidenceCategories"),
            PreservedEvidenceCategories,
            "contract.preservedEvidenceCategories",
            failures);
        RequireExactArray(
            value.GetProperty("stableContractAcknowledgements"),
            StableContractAcknowledgements,
            "contract.stableContractAcknowledgements",
            failures);
        RequireExactArray(value.GetProperty("requiredRecordFields"), RecordFields, "contract.requiredRecordFields", failures);
        RequireExactArray(
            value.GetProperty("requiredPublicInstallFields"),
            PublicInstallFields,
            "contract.requiredPublicInstallFields",
            failures);
        RequireExactArray(value.GetProperty("releaseRules"), ReleaseRules, "contract.releaseRules", failures);
    }

    private static AcceptanceAuthority ValidateAuthority(byte[] bytes, List<string> failures)
    {
        using var document = ParseStrictJson(bytes, "stable upstream acceptance contract");
        var value = document.RootElement;
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        var gates = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!RequireExactFields(value, AuthorityFields, "upstream acceptance contract", failures))
        {
            return new AcceptanceAuthority(kinds, gates);
        }
        RequireInteger(value.GetProperty("schemaVersion"), 1, "upstream acceptance contract.schemaVersion", failures);
        RequireExactText(
            value.GetProperty("kind"),
            "vibesnake-stable-upstream-acceptance-v1",
            "upstream acceptance contract.kind",
            failures);
        RequireExactText(
            value.GetProperty("status"),
            "closed-upstream-acceptance-authority",
            "upstream acceptance contract.status",
            failures);
        RequireExactArray(
            value.GetProperty("artifactPlatforms"),
            ArtifactPlatforms,
            "upstream acceptance contract.artifactPlatforms",
            failures);
        if (RequireExactFields(
            value.GetProperty("decisionKindsById"),
            Set(UpstreamDecisionIds),
            "upstream acceptance contract.decisionKindsById",
            failures))
        {
            foreach (var id in UpstreamDecisionIds)
            {
                var profileValue = value.GetProperty("decisionKindsById").GetProperty(id);
                var profileLabel = $"upstream acceptance contract.decisionKindsById.{id}";
                if (!RequireExactFields(
                    profileValue,
                    Set("acceptedKind", "artifactBinding", "additionalRequiredDecisionFields"),
                    profileLabel,
                    failures))
                {
                    continue;
                }
                var expected = ExpectedProfiles[id];
                RequireExactText(profileValue.GetProperty("acceptedKind"), expected.Kind, $"{profileLabel}.acceptedKind", failures);
                RequireExactText(
                    profileValue.GetProperty("artifactBinding"),
                    expected.ArtifactBinding,
                    $"{profileLabel}.artifactBinding",
                    failures);
                RequireExactArray(
                    profileValue.GetProperty("additionalRequiredDecisionFields"),
                    expected.AdditionalFields,
                    $"{profileLabel}.additionalRequiredDecisionFields",
                    failures);
                kinds[id] = expected.Kind;
            }
        }
        if (RequireExactFields(
            value.GetProperty("gateIdsById"),
            Set(UpstreamDecisionIds),
            "upstream acceptance contract.gateIdsById",
            failures))
        {
            foreach (var id in UpstreamDecisionIds)
            {
                var expected = ExpectedGateIds[id];
                RequireExactArray(
                    value.GetProperty("gateIdsById").GetProperty(id),
                    expected,
                    $"upstream acceptance contract.gateIdsById.{id}",
                    failures);
                gates[id] = expected;
            }
        }
        RequireExactArray(
            value.GetProperty("requiredGenericDecisionFields"),
            GenericDecisionFields,
            "upstream acceptance contract.requiredGenericDecisionFields",
            failures);
        RequireExactArray(
            value.GetProperty("requiredGateFields"),
            GenericGateFields,
            "upstream acceptance contract.requiredGateFields",
            failures);
        RequireExactArray(
            value.GetProperty("releaseRules"),
            AuthorityReleaseRules,
            "upstream acceptance contract.releaseRules",
            failures);
        if (kinds.Values.Distinct(StringComparer.Ordinal).Count() != kinds.Count)
        {
            failures.Add("upstream acceptance contract decision kinds must be unique");
        }
        return new AcceptanceAuthority(kinds, gates);
    }

    private static RecordIdentity? ValidateRecord(
        string repositoryRoot,
        JsonElement value,
        string expectedRevision,
        AcceptanceAuthority authority,
        RetainedFiles files,
        List<string> failures)
    {
        if (!RequireExactFields(value, Set(RecordFields), "promotion", failures))
        {
            return null;
        }
        RequireInteger(value.GetProperty("schemaVersion"), 1, "promotion.schemaVersion", failures);
        RequireExactText(value.GetProperty("kind"), "vibesnake-stable-promotion-record-v1", "promotion.kind", failures);
        RequireExactText(value.GetProperty("sourceRevision"), expectedRevision, "promotion.sourceRevision", failures);
        RequireExactText(value.GetProperty("appVersion"), StableVersion, "promotion.appVersion", failures);
        RequireExactText(value.GetProperty("tagName"), StableTag, "promotion.tagName", failures);
        RequireExactText(value.GetProperty("tagObjectRevision"), expectedRevision, "promotion.tagObjectRevision", failures);
        var workflowRun = RequireBoundedText(
            value.GetProperty("protectedWorkflowRunId"),
            "promotion.protectedWorkflowRunId",
            failures);
        if (workflowRun is not null && !WorkflowRunPattern.IsMatch(workflowRun))
        {
            failures.Add("promotion.protectedWorkflowRunId must be a retained numeric workflow run ID");
        }

        var artifacts = ReadDigestMap(value.GetProperty("artifactSha256ByPlatform"), "promotion artifact digests", failures);
        var artifactPaths = ReadPathMap(
            value.GetProperty("artifactPathsByPlatform"),
            "promotion artifact paths",
            files,
            failures);
        var manifests = ReadDigestMap(value.GetProperty("manifestSha256ByPlatform"), "promotion manifest digests", failures);
        var manifestPaths = ReadPathMap(
            value.GetProperty("manifestPathsByPlatform"),
            "promotion manifest paths",
            files,
            failures);
        var provenance = ReadDigestMap(
            value.GetProperty("provenanceSha256ByPlatform"),
            "promotion provenance digests",
            failures);
        var provenancePaths = ReadPathMap(
            value.GetProperty("provenancePathsByPlatform"),
            "promotion provenance paths",
            files,
            failures);
        var checksumPaths = ReadPathMap(
            value.GetProperty("checksumPathsByPlatform"),
            "promotion checksum paths",
            files,
            failures);
        ValidateDigestPairs(artifacts, artifactPaths, files, "promotion artifact", failures);
        ValidateDigestPairs(manifests, manifestPaths, files, "promotion manifest", failures);
        ValidateDigestPairs(provenance, provenancePaths, files, "promotion provenance", failures);
        if (artifacts.Values.Distinct(StringComparer.Ordinal).Count() != ArtifactPlatforms.Length)
        {
            failures.Add("promotion artifacts must have distinct platform SHA-256 identities");
        }
        var smokeHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var platform in ArtifactPlatforms)
        {
            if (manifestPaths.TryGetValue(platform, out var relativePath)
                && files.TryGet(relativePath, out var manifestFile))
            {
                var bytes = files.ReadBytes(manifestFile, MaximumManifestBytes, $"promotion manifest {platform}");
                using var manifest = ParseStrictJson(bytes, $"promotion manifest {platform}");
                var identity = ValidateManifest(manifest.RootElement, platform, expectedRevision, failures);
                if (identity is not null)
                {
                    smokeHashes[platform] = identity.SmokeStateHash;
                }
            }
            if (checksumPaths.TryGetValue(platform, out var checksumPath)
                && files.TryGet(checksumPath, out var checksumFile)
                && artifactPaths.TryGetValue(platform, out var artifactPath)
                && manifestPaths.TryGetValue(platform, out var manifestPath)
                && provenancePaths.TryGetValue(platform, out var provenancePath)
                && artifacts.TryGetValue(platform, out var artifactSha)
                && manifests.TryGetValue(platform, out var manifestSha)
                && provenance.TryGetValue(platform, out var provenanceSha))
            {
                ValidateChecksum(
                    files.ReadBytes(checksumFile, MaximumChecksumBytes, $"promotion checksum {platform}"),
                    platform,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [Path.GetFileName(artifactPath)] = artifactSha,
                        [Path.GetFileName(manifestPath)] = manifestSha,
                        [Path.GetFileName(provenancePath)] = provenanceSha,
                    },
                    failures);
            }
        }
        if (smokeHashes.Count == ArtifactPlatforms.Length
            && smokeHashes.Values.Distinct(StringComparer.Ordinal).Count() != 1)
        {
            failures.Add("promotion manifests must report one shared smoke state hash");
        }

        var optionalPackPath = ReadSinglePath(
            value.GetProperty("optionalPackPath"),
            "promotion.optionalPackPath",
            files,
            failures);
        var optionalManifestPath = ReadSinglePath(
            value.GetProperty("optionalPackManifestPath"),
            "promotion.optionalPackManifestPath",
            files,
            failures);
        var optionalPackSha = RequireDigest(value.GetProperty("optionalPackSha256"), "promotion.optionalPackSha256", failures);
        var optionalManifestSha = RequireDigest(
            value.GetProperty("optionalPackManifestSha256"),
            "promotion.optionalPackManifestSha256",
            failures);
        ValidateDigestPair(optionalPackSha, optionalPackPath, "promotion optional pack", files, failures);
        ValidateDigestPair(optionalManifestSha, optionalManifestPath, "promotion optional pack manifest", files, failures);

        var upstreamPaths = ReadNamedPathMap(
            value.GetProperty("upstreamDecisionPathsById"),
            UpstreamDecisionIds,
            "promotion upstream decisions",
            files,
            failures);
        var decisionDigests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, relativePath) in upstreamPaths)
        {
            if (files.TryGet(relativePath, out var file))
            {
                decisionDigests[id] = files.Hash(file).Sha256;
            }
        }
        var unsignedArtifacts = new Dictionary<string, string>(StringComparer.Ordinal);
        var unsignedManifests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in UpstreamDecisionIds)
        {
            if (!upstreamPaths.TryGetValue(id, out var relativePath)
                || !files.TryGet(relativePath, out var file))
            {
                continue;
            }
            var bytes = files.ReadBytes(file, MaximumDecisionBytes, $"upstream decision {id}");
            using var decision = ParseStrictJson(bytes, $"upstream decision {id}");
            if (id == "release-materials")
            {
                ValidateMaterialDecision(
                    repositoryRoot,
                    decision.RootElement,
                    relativePath,
                    expectedRevision,
                    unsignedManifests,
                    files,
                    failures);
            }
            else if (id == "release-rehearsal")
            {
                ValidateRehearsalDecision(
                    repositoryRoot,
                    decision.RootElement,
                    expectedRevision,
                    unsignedArtifacts,
                    unsignedManifests,
                    decisionDigests.GetValueOrDefault("release-materials"),
                    failures);
            }
            else
            {
                ValidateGenericDecision(
                    id,
                    decision.RootElement,
                    relativePath,
                    expectedRevision,
                    authority,
                    artifacts,
                    manifests,
                    provenance,
                    optionalPackSha,
                    optionalManifestSha,
                    unsignedArtifacts,
                    unsignedManifests,
                    files,
                    failures);
            }
        }

        ValidatePublicInstalls(
            value.GetProperty("publicInstallResults"),
            artifacts,
            smokeHashes,
            files,
            failures);
        ValidatePreservedEvidence(
            value.GetProperty("preservedEvidencePathsByCategory"),
            files,
            failures);
        RequireExactArray(
            value.GetProperty("stableContractAcknowledgements"),
            StableContractAcknowledgements,
            "promotion.stableContractAcknowledgements",
            failures);
        ValidateRetainedHashes(value.GetProperty("retainedFileSha256"), files, failures);

        return workflowRun is null || optionalPackSha is null || optionalManifestSha is null
            ? null
            : new RecordIdentity(
                expectedRevision,
                workflowRun,
                artifacts,
                manifests,
                optionalPackSha,
                optionalManifestSha);
    }

    private static void ValidateGenericDecision(
        string id,
        JsonElement value,
        string decisionPath,
        string expectedRevision,
        AcceptanceAuthority authority,
        IReadOnlyDictionary<string, string> finalArtifacts,
        IReadOnlyDictionary<string, string> finalManifests,
        IReadOnlyDictionary<string, string> finalProvenance,
        string? optionalPackSha,
        string? optionalManifestSha,
        Dictionary<string, string> unsignedArtifacts,
        Dictionary<string, string> unsignedManifests,
        RetainedFiles files,
        List<string> failures)
    {
        var expectedFields = id switch
        {
            "content-approval" => ContentDecisionFieldSet,
            "platform-signing" => SigningDecisionFieldSet,
            _ => GenericDecisionFieldSet,
        };
        var label = $"upstream decision {id}";
        if (!RequireExactFields(value, expectedFields, label, failures))
        {
            return;
        }
        RequireInteger(value.GetProperty("schemaVersion"), 1, $"{label}.schemaVersion", failures);
        RequireExactText(
            value.GetProperty("kind"),
            authority.Kinds.GetValueOrDefault(id),
            $"{label}.kind",
            failures);
        RequireBoolean(value.GetProperty("passed"), true, $"{label}.passed", failures);
        RequireBoolean(value.GetProperty("releaseAcceptance"), true, $"{label}.releaseAcceptance", failures);
        RequireExactText(value.GetProperty("sourceRevision"), expectedRevision, $"{label}.sourceRevision", failures);
        RequireExactText(value.GetProperty("appVersion"), StableVersion, $"{label}.appVersion", failures);
        ValidateUtc(value.GetProperty("acceptedUtc"), $"{label}.acceptedUtc", failures);
        RequireEmptyArray(value.GetProperty("pendingGates"), $"{label}.pendingGates", failures);
        RequireEmptyArray(value.GetProperty("errors"), $"{label}.errors", failures);
        var artifacts = ReadDigestMap(
            value.GetProperty("candidateArtifactSha256ByPlatform"),
            $"{label}.candidateArtifactSha256ByPlatform",
            failures);
        var manifests = ReadDigestMap(
            value.GetProperty("candidateManifestSha256ByPlatform"),
            $"{label}.candidateManifestSha256ByPlatform",
            failures);
        if (id == "release-matrix")
        {
            CopyMap(artifacts, unsignedArtifacts);
            CopyMap(manifests, unsignedManifests);
        }
        else if (id == "platform-signing")
        {
            RequireMapEqual(artifacts, finalArtifacts, $"{label} final artifact identity", failures);
            RequireMapEqual(manifests, finalManifests, $"{label} final manifest identity", failures);
        }
        else
        {
            RequireMapEqual(artifacts, unsignedArtifacts, $"{label} unsigned artifact identity", failures);
            RequireMapEqual(manifests, unsignedManifests, $"{label} unsigned manifest identity", failures);
        }
        if (id == "content-approval")
        {
            RequireExactText(value.GetProperty("optionalPackSha256"), optionalPackSha, $"{label}.optionalPackSha256", failures);
            RequireExactText(
                value.GetProperty("optionalPackManifestSha256"),
                optionalManifestSha,
                $"{label}.optionalPackManifestSha256",
                failures);
        }
        if (id == "platform-signing")
        {
            var inputArtifacts = ReadDigestMap(
                value.GetProperty("inputArtifactSha256ByPlatform"),
                $"{label}.inputArtifactSha256ByPlatform",
                failures);
            var inputManifests = ReadDigestMap(
                value.GetProperty("inputManifestSha256ByPlatform"),
                $"{label}.inputManifestSha256ByPlatform",
                failures);
            var provenance = ReadDigestMap(
                value.GetProperty("provenanceSha256ByPlatform"),
                $"{label}.provenanceSha256ByPlatform",
                failures);
            RequireMapEqual(inputArtifacts, unsignedArtifacts, $"{label} input artifact identity", failures);
            RequireMapEqual(inputManifests, unsignedManifests, $"{label} input manifest identity", failures);
            RequireMapEqual(provenance, finalProvenance, $"{label} provenance identity", failures);
        }
        var decisionPrefix = ParentPrefix(decisionPath);
        var decisionPaths = ValidateGateRecords(
            value.GetProperty("gateRecords"),
            authority.Gates.GetValueOrDefault(id) ?? [],
            label,
            decisionPrefix,
            files,
            failures);
        ValidateNestedRetainedHashes(
            value.GetProperty("retainedFileSha256"),
            decisionPaths,
            decisionPrefix,
            files,
            $"{label}.retainedFileSha256",
            failures);
    }

    private static void ValidateMaterialDecision(
        string repositoryRoot,
        JsonElement value,
        string decisionPath,
        string expectedRevision,
        IReadOnlyDictionary<string, string> unsignedManifests,
        RetainedFiles files,
        List<string> failures)
    {
        const string label = "upstream decision release-materials";
        if (!RequireExactFields(value, MaterialDecisionFields, label, failures))
        {
            return;
        }
        RequireInteger(value.GetProperty("schemaVersion"), 1, $"{label}.schemaVersion", failures);
        RequireExactText(value.GetProperty("kind"), "release-materials-acceptance-v1", $"{label}.kind", failures);
        RequireBoolean(value.GetProperty("passed"), true, $"{label}.passed", failures);
        RequireBoolean(value.GetProperty("foundationQualified"), true, $"{label}.foundationQualified", failures);
        RequireBoolean(value.GetProperty("candidateMaterialComplete"), true, $"{label}.candidateMaterialComplete", failures);
        RequireBoolean(value.GetProperty("releaseAcceptance"), true, $"{label}.releaseAcceptance", failures);
        RequireExactText(value.GetProperty("sourceRevision"), expectedRevision, $"{label}.sourceRevision", failures);
        RequireExactText(value.GetProperty("appVersion"), StableVersion, $"{label}.appVersion", failures);
        var candidateSha = RequireDigest(value.GetProperty("candidateSha256"), $"{label}.candidateSha256", failures);
        ValidateUtc(value.GetProperty("acceptedUtc"), $"{label}.acceptedUtc", failures);
        RequireEmptyArray(value.GetProperty("pendingGates"), $"{label}.pendingGates", failures);
        RequireEmptyArray(value.GetProperty("errors"), $"{label}.errors", failures);
        var manifestDigests = ReadDigestMap(
            value.GetProperty("artifactManifestSha256ByPlatform"),
            $"{label}.artifactManifestSha256ByPlatform",
            failures);
        RequireMapEqual(manifestDigests, unsignedManifests, $"{label} unsigned manifest identity", failures);
        var decisionPrefix = ParentPrefix(decisionPath);
        var nestedPaths = new List<string>();
        var structuralPath = ReadNestedPath(
            value.GetProperty("structuralHandoffPath"),
            $"{label}.structuralHandoffPath",
            decisionPrefix,
            files,
            failures);
        if (structuralPath is not null)
        {
            nestedPaths.Add(structuralPath.Relative);
            var structuralSha = RequireDigest(
                value.GetProperty("structuralHandoffSha256"),
                $"{label}.structuralHandoffSha256",
                failures);
            ValidateDigestPair(structuralSha, structuralPath.Global, "release materials structural handoff", files, failures);
            if (files.TryGet(structuralPath.Global, out var file))
            {
                var bytes = files.ReadBytes(file, MaximumDecisionBytes, "release materials structural handoff");
                using var structural = ParseStrictJson(bytes, "release materials structural handoff");
                ValidateMaterialStructural(
                    repositoryRoot,
                    structural.RootElement,
                    expectedRevision,
                    candidateSha,
                    failures);
            }
        }
        nestedPaths.AddRange(ValidateGateRecords(
            value.GetProperty("gateRecords"),
            MaterialGateIds,
            label,
            decisionPrefix,
            files,
            failures));
        ValidateNestedRetainedHashes(
            value.GetProperty("retainedFileSha256"),
            nestedPaths,
            decisionPrefix,
            files,
            $"{label}.retainedFileSha256",
            failures);
    }

    private static void ValidateMaterialStructural(
        string repositoryRoot,
        JsonElement value,
        string expectedRevision,
        string? candidateSha,
        List<string> failures)
    {
        const string label = "release materials structural handoff";
        if (!RequireExactFields(value, MaterialStructuralFields, label, failures))
        {
            return;
        }
        RequireInteger(value.GetProperty("schemaVersion"), 2, $"{label}.schemaVersion", failures);
        RequireExactText(value.GetProperty("kind"), "release-materials-handoff-v2", $"{label}.kind", failures);
        RequireBoolean(value.GetProperty("passed"), true, $"{label}.passed", failures);
        RequireBoolean(value.GetProperty("foundationQualified"), true, $"{label}.foundationQualified", failures);
        RequireBoolean(value.GetProperty("candidateSupplied"), true, $"{label}.candidateSupplied", failures);
        RequireBoolean(value.GetProperty("candidateMaterialComplete"), true, $"{label}.candidateMaterialComplete", failures);
        RequireBoolean(value.GetProperty("releaseAcceptance"), false, $"{label}.releaseAcceptance", failures);
        RequireExactText(value.GetProperty("sourceRevision"), expectedRevision, $"{label}.sourceRevision", failures);
        RequireExactText(value.GetProperty("appVersion"), StableVersion, $"{label}.appVersion", failures);
        RequireExactText(value.GetProperty("candidateSha256"), candidateSha, $"{label}.candidateSha256", failures);
        var contractPath = ResolveRegularFile(
            repositoryRoot,
            "config/release_materials_v1.json",
            MaximumContractBytes,
            "release materials contract");
        RequireExactText(
            value.GetProperty("contractSha256"),
            HashStableFile(contractPath, MaximumContractBytes, "release materials contract").Sha256,
            $"{label}.contractSha256",
            failures);
        var documents = value.GetProperty("documentSha256");
        if (RequireExactFields(documents, Set(MaterialDocuments), $"{label}.documentSha256", failures))
        {
            foreach (var path in MaterialDocuments)
            {
                var file = ResolveRegularFile(repositoryRoot, path, MaximumRetainedFileBytes, path);
                RequireExactText(
                    documents.GetProperty(path),
                    HashStableFile(file, MaximumRetainedFileBytes, path).Sha256,
                    $"{label}.documentSha256.{path}",
                    failures);
            }
        }
        RequireInteger(value.GetProperty("requiredDocumentCount"), 10, $"{label}.requiredDocumentCount", failures);
        RequireInteger(value.GetProperty("artifactPlatformCount"), 3, $"{label}.artifactPlatformCount", failures);
        RequireInteger(value.GetProperty("inputDeviceCount"), 4, $"{label}.inputDeviceCount", failures);
        RequireInteger(value.GetProperty("screenshotRoleCount"), 6, $"{label}.screenshotRoleCount", failures);
        RequireInteger(value.GetProperty("videoRoleCount"), 2, $"{label}.videoRoleCount", failures);
        RequireInteger(value.GetProperty("marketingClaimCount"), 8, $"{label}.marketingClaimCount", failures);
        RequireExactArray(value.GetProperty("pendingGates"), MaterialPendingGates, $"{label}.pendingGates", failures);
        RequireEmptyArray(value.GetProperty("errors"), $"{label}.errors", failures);
    }

    private static void ValidateRehearsalDecision(
        string repositoryRoot,
        JsonElement value,
        string expectedRevision,
        IReadOnlyDictionary<string, string> unsignedArtifacts,
        IReadOnlyDictionary<string, string> unsignedManifests,
        string? materialDecisionSha,
        List<string> failures)
    {
        const string label = "upstream decision release-rehearsal";
        if (!RequireExactFields(value, RehearsalFields, label, failures))
        {
            return;
        }
        RequireInteger(value.GetProperty("schemaVersion"), 2, $"{label}.schemaVersion", failures);
        RequireExactText(value.GetProperty("kind"), "release-rehearsal-handoff-v2", $"{label}.kind", failures);
        foreach (var field in new[]
        {
            "passed", "protocolQualified", "recordSupplied", "recordIntegrityQualified",
            "externalExecutionAttested", "rehearsalComplete", "releaseAcceptance",
        })
        {
            RequireBoolean(value.GetProperty(field), true, $"{label}.{field}", failures);
        }
        RequireExactText(value.GetProperty("sourceRevision"), expectedRevision, $"{label}.sourceRevision", failures);
        RequireExactText(value.GetProperty("appVersion"), StableVersion, $"{label}.appVersion", failures);
        var previous = RequireBoundedText(value.GetProperty("previousVersion"), $"{label}.previousVersion", failures);
        if (previous is not null
            && (!TryParseVersion(previous, out var parsed) || parsed.CompareTo(new ComparableVersion(1, 0, 0, 3, 0)) >= 0))
        {
            failures.Add($"{label}.previousVersion must be a canonical supported version earlier than 1.0.0");
        }
        RequireDigest(value.GetProperty("recordSha256"), $"{label}.recordSha256", failures);
        RequireExactText(
            value.GetProperty("releaseMaterialsDecisionSha256"),
            materialDecisionSha,
            $"{label}.releaseMaterialsDecisionSha256",
            failures);
        RequireMapEqual(
            ReadDigestMap(value.GetProperty("candidateArtifactSha256ByPlatform"), $"{label}.candidateArtifactSha256ByPlatform", failures),
            unsignedArtifacts,
            $"{label} unsigned artifact identity",
            failures);
        RequireMapEqual(
            ReadDigestMap(value.GetProperty("candidateManifestSha256ByPlatform"), $"{label}.candidateManifestSha256ByPlatform", failures),
            unsignedManifests,
            $"{label} unsigned manifest identity",
            failures);
        var rehearsalContract = ResolveRegularFile(
            repositoryRoot,
            "config/release_rehearsal_v1.json",
            MaximumContractBytes,
            "release rehearsal contract");
        RequireExactText(
            value.GetProperty("contractSha256"),
            HashStableFile(rehearsalContract, MaximumContractBytes, "release rehearsal contract").Sha256,
            $"{label}.contractSha256",
            failures);
        var prerequisites = value.GetProperty("prerequisiteSha256");
        if (RequireExactFields(prerequisites, Set(RehearsalPrerequisites), $"{label}.prerequisiteSha256", failures))
        {
            foreach (var path in RehearsalPrerequisites)
            {
                var file = ResolveRegularFile(repositoryRoot, path, MaximumRetainedFileBytes, path);
                RequireExactText(
                    prerequisites.GetProperty(path),
                    HashStableFile(file, MaximumRetainedFileBytes, path).Sha256,
                    $"{label}.prerequisiteSha256.{path}",
                    failures);
            }
        }
        RequireInteger(value.GetProperty("artifactPlatformCount"), 3, $"{label}.artifactPlatformCount", failures);
        RequireInteger(value.GetProperty("platformOperationCount"), 11, $"{label}.platformOperationCount", failures);
        RequireInteger(value.GetProperty("requiredPlatformOperationCellCount"), 33, $"{label}.requiredPlatformOperationCellCount", failures);
        RequireInteger(value.GetProperty("authorityOperationCount"), 4, $"{label}.authorityOperationCount", failures);
        RequireEmptyArray(value.GetProperty("pendingGates"), $"{label}.pendingGates", failures);
        RequireEmptyArray(value.GetProperty("errors"), $"{label}.errors", failures);
    }

    private static void ValidatePublicInstalls(
        JsonElement value,
        IReadOnlyDictionary<string, string> artifacts,
        IReadOnlyDictionary<string, string> smokeHashes,
        RetainedFiles files,
        List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != ArtifactPlatforms.Length)
        {
            failures.Add("promotion.publicInstallResults must contain exactly three ordered rows");
            return;
        }
        var index = 0;
        foreach (var row in value.EnumerateArray())
        {
            var label = $"promotion.publicInstallResults[{index}]";
            if (RequireExactFields(row, Set(PublicInstallFields), label, failures))
            {
                var platform = ArtifactPlatforms[index];
                RequireExactText(row.GetProperty("platformId"), platform, $"{label}.platformId", failures);
                RequireExactText(row.GetProperty("result"), "pass", $"{label}.result", failures);
                RequireExactText(
                    row.GetProperty("installedArtifactSha256"),
                    artifacts.GetValueOrDefault(platform),
                    $"{label}.installedArtifactSha256",
                    failures);
                RequireExactText(
                    row.GetProperty("smokeStateHash"),
                    smokeHashes.GetValueOrDefault(platform),
                    $"{label}.smokeStateHash",
                    failures);
                _ = ReadPathArray(row.GetProperty("evidencePaths"), $"{label}.evidencePaths", files, failures);
            }
            index++;
        }
    }

    private static void ValidatePreservedEvidence(JsonElement value, RetainedFiles files, List<string> failures)
    {
        if (!RequireExactFields(value, Set(PreservedEvidenceCategories), "promotion preserved evidence", failures))
        {
            return;
        }
        foreach (var category in PreservedEvidenceCategories)
        {
            _ = ReadPathArray(
                value.GetProperty(category),
                $"promotion preserved evidence.{category}",
                files,
                failures);
        }
    }

    private static Dictionary<string, string> ReadDigestMap(
        JsonElement value,
        string label,
        List<string> failures)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!RequireExactFields(value, Set(ArtifactPlatforms), label, failures))
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

    private static Dictionary<string, string> ReadPathMap(
        JsonElement value,
        string label,
        RetainedFiles files,
        List<string> failures) =>
        ReadNamedPathMap(value, ArtifactPlatforms, label, files, failures);

    private static Dictionary<string, string> ReadNamedPathMap(
        JsonElement value,
        string[] keys,
        string label,
        RetainedFiles files,
        List<string> failures)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!RequireExactFields(value, Set(keys), label, failures))
        {
            return result;
        }
        foreach (var key in keys)
        {
            var path = ReadSinglePath(value.GetProperty(key), $"{label}.{key}", files, failures);
            if (path is not null)
            {
                result[key] = path;
            }
        }
        return result;
    }

    private static string? ReadSinglePath(
        JsonElement value,
        string label,
        RetainedFiles files,
        List<string> failures)
    {
        var path = RequireBoundedText(value, label, failures, MaximumRelativePathCharacters);
        if (path is null || !IsSafeRelativePath(path))
        {
            if (path is not null)
            {
                failures.Add($"{label} must be a safe portable relative path");
            }
            return null;
        }
        files.Add(path, label);
        return path;
    }

    private static NestedPath? ReadNestedPath(
        JsonElement value,
        string label,
        string prefix,
        RetainedFiles files,
        List<string> failures)
    {
        var relative = RequireBoundedText(value, label, failures, MaximumRelativePathCharacters);
        if (relative is null || !IsSafeRelativePath(relative))
        {
            if (relative is not null)
            {
                failures.Add($"{label} must be a safe portable relative path");
            }
            return null;
        }
        var global = CombineRelative(prefix, relative);
        files.Add(global, label);
        return new NestedPath(relative, global);
    }

    private static List<string> ReadPathArray(
        JsonElement value,
        string label,
        RetainedFiles files,
        List<string> failures)
    {
        var result = new List<string>();
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is < 1 or > MaximumEvidencePaths)
        {
            failures.Add($"{label} must contain 1 through {MaximumEvidencePaths} paths");
            return result;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var path = RequireBoundedText(item, $"{label}[{index}]", failures, MaximumRelativePathCharacters);
            if (path is null || !IsSafeRelativePath(path) || !seen.Add(path))
            {
                failures.Add($"{label} must contain unique safe portable relative paths");
            }
            else
            {
                files.Add(path, $"{label}[{index}]");
                result.Add(path);
            }
            index++;
        }
        return result;
    }

    private static List<string> ValidateGateRecords(
        JsonElement value,
        string[] expectedGates,
        string parentLabel,
        string prefix,
        RetainedFiles files,
        List<string> failures)
    {
        var retained = new List<string>();
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != expectedGates.Length)
        {
            failures.Add($"{parentLabel}.gateRecords must contain the exact ordered acceptance gates");
            return retained;
        }
        var index = 0;
        foreach (var row in value.EnumerateArray())
        {
            var label = $"{parentLabel}.gateRecords[{index}]";
            if (RequireExactFields(row, Set(GenericGateFields), label, failures))
            {
                RequireExactText(row.GetProperty("gateId"), expectedGates[index], $"{label}.gateId", failures);
                RequireExactText(row.GetProperty("result"), "pass", $"{label}.result", failures);
                var role = RequireBoundedText(row.GetProperty("authorityRoleId"), $"{label}.authorityRoleId", failures);
                if (role is not null && !RolePattern.IsMatch(role))
                {
                    failures.Add($"{label}.authorityRoleId must be a non-personal operational role ID");
                }
                if (row.GetProperty("evidencePaths").ValueKind != JsonValueKind.Array
                    || row.GetProperty("evidencePaths").GetArrayLength() is < 1 or > MaximumEvidencePaths)
                {
                    failures.Add($"{label}.evidencePaths must contain 1 through {MaximumEvidencePaths} paths");
                }
                else
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pathValue in row.GetProperty("evidencePaths").EnumerateArray())
                    {
                        var path = RequireBoundedText(pathValue, $"{label}.evidencePaths", failures, MaximumRelativePathCharacters);
                        if (path is null || !IsSafeRelativePath(path) || !seen.Add(path))
                        {
                            failures.Add($"{label}.evidencePaths must contain unique safe portable relative paths");
                            continue;
                        }
                        var global = CombineRelative(prefix, path);
                        files.Add(global, $"{label}.evidencePaths");
                        retained.Add(path);
                    }
                }
            }
            index++;
        }
        return retained;
    }

    private static void ValidateNestedRetainedHashes(
        JsonElement value,
        IEnumerable<string> expectedPaths,
        string prefix,
        RetainedFiles files,
        string label,
        List<string> failures)
    {
        var expected = expectedPaths.ToHashSet(StringComparer.Ordinal);
        if (!RequireExactFields(value, expected, label, failures))
        {
            return;
        }
        foreach (var relative in expected.Order(StringComparer.Ordinal))
        {
            var expectedSha = RequireDigest(value.GetProperty(relative), $"{label}.{relative}", failures);
            var global = CombineRelative(prefix, relative);
            if (expectedSha is not null
                && files.TryGet(global, out var file)
                && !string.Equals(files.Hash(file).Sha256, expectedSha, StringComparison.Ordinal))
            {
                failures.Add($"{label} hash mismatch: {relative}");
            }
        }
    }

    private static void ValidateRetainedHashes(JsonElement value, RetainedFiles files, List<string> failures)
    {
        var expected = files.RelativePaths.ToHashSet(StringComparer.Ordinal);
        if (!RequireExactFields(value, expected, "promotion.retainedFileSha256", failures))
        {
            return;
        }
        foreach (var path in expected.Order(StringComparer.Ordinal))
        {
            var expectedSha = RequireDigest(value.GetProperty(path), $"promotion.retainedFileSha256.{path}", failures);
            if (expectedSha is not null
                && files.TryGet(path, out var file)
                && !string.Equals(files.Hash(file).Sha256, expectedSha, StringComparison.Ordinal))
            {
                failures.Add($"stable promotion retained file hash mismatch: {path}");
            }
        }
    }

    private static void ValidateDigestPairs(
        Dictionary<string, string> digests,
        Dictionary<string, string> paths,
        RetainedFiles files,
        string label,
        List<string> failures)
    {
        foreach (var platform in ArtifactPlatforms)
        {
            if (digests.TryGetValue(platform, out var digest)
                && paths.TryGetValue(platform, out var path))
            {
                ValidateDigestPair(digest, path, $"{label} {platform}", files, failures);
            }
        }
    }

    private static void ValidateDigestPair(
        string? digest,
        string? path,
        string label,
        RetainedFiles files,
        List<string> failures)
    {
        if (digest is not null
            && path is not null
            && files.TryGet(path, out var file)
            && !string.Equals(files.Hash(file).Sha256, digest, StringComparison.Ordinal))
        {
            failures.Add($"{label} hash mismatch");
        }
    }

    private static ManifestIdentity? ValidateManifest(
        JsonElement value,
        string platform,
        string expectedRevision,
        List<string> failures)
    {
        var label = $"promotion manifest {platform}";
        if (!RequireExactFields(value, ManifestFields, label, failures))
        {
            return null;
        }
        RequireInteger(value.GetProperty("schemaVersion"), 3, $"{label}.schemaVersion", failures);
        RequireExactText(value.GetProperty("product"), "Vibe Snake", $"{label}.product", failures);
        RequireExactText(value.GetProperty("platform"), platform, $"{label}.platform", failures);
        RequireExactText(value.GetProperty("buildMode"), "Release", $"{label}.buildMode", failures);
        RequireExactText(value.GetProperty("sourceRevision"), expectedRevision, $"{label}.sourceRevision", failures);
        _ = RequireBoundedText(value.GetProperty("godotVersion"), $"{label}.godotVersion", failures);
        _ = RequireBoundedText(value.GetProperty("godotCommit"), $"{label}.godotCommit", failures);
        RequireLowerHex(value.GetProperty("godotArchiveSha512"), 128, $"{label}.godotArchiveSha512", failures);
        RequireDigest(value.GetProperty("godotExecutableSha256"), $"{label}.godotExecutableSha256", failures);
        _ = RequireBoundedText(value.GetProperty("dotnetSdk"), $"{label}.dotnetSdk", failures);
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
        return smoke is null ? null : new ManifestIdentity(smoke);
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
            if (!ManifestEntryRequiredFields.IsSubsetOf(fields) || !fields.IsSubsetOf(ManifestEntryAllowedFields))
            {
                failures.Add($"{rowLabel} fields are invalid");
                index++;
                continue;
            }
            var path = RequireBoundedText(row.GetProperty("path"), $"{rowLabel}.path", failures, MaximumRelativePathCharacters);
            if (path is not null && (!IsSafeRelativePath(path) || !paths.Add(path)))
            {
                failures.Add($"{rowLabel}.path must be a unique safe portable relative path");
            }
            var bytes = RequireNonnegativeInteger(row.GetProperty("bytes"), $"{rowLabel}.bytes", failures);
            RequireDigest(row.GetProperty("sha256"), $"{rowLabel}.sha256", failures);
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

    private static void ValidateChecksum(
        byte[] bytes,
        string platform,
        Dictionary<string, string> expected,
        List<string> failures)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            failures.Add($"promotion checksum {platform} must contain valid UTF-8");
            return;
        }
        if (!text.EndsWith('\n') || text.Contains('\r'))
        {
            failures.Add($"promotion checksum {platform} must be canonical LF text");
            return;
        }
        var rows = text[..^1].Split('\n');
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Length < 67 || row[64..66] != "  " || !IsLowerHex(row[..64], 64))
            {
                failures.Add($"promotion checksum {platform} contains a malformed row");
                return;
            }
            var name = row[66..];
            if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || !actual.TryAdd(name, row[..64]))
            {
                failures.Add($"promotion checksum {platform} contains an invalid or duplicate filename");
                return;
            }
        }
        if (actual.Count != expected.Count
            || expected.Any(item => !actual.TryGetValue(item.Key, out var digest)
                || !string.Equals(digest, item.Value, StringComparison.Ordinal)))
        {
            failures.Add($"promotion checksum {platform} must bind the exact artifact, manifest, and provenance files");
        }
    }

    private static void RequireMapEqual(
        Dictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected,
        string label,
        List<string> failures)
    {
        foreach (var platform in ArtifactPlatforms)
        {
            if (!actual.TryGetValue(platform, out var actualValue)
                || !expected.TryGetValue(platform, out var expectedValue)
                || !string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
            {
                failures.Add($"{label} does not match for {platform}");
            }
        }
    }

    private static void CopyMap(IReadOnlyDictionary<string, string> source, Dictionary<string, string> target)
    {
        target.Clear();
        foreach (var (key, value) in source)
        {
            target[key] = value;
        }
    }

    private static string RenderEvidence(
        string[] failures,
        byte[] contractBytes,
        byte[] authorityBytes,
        bool guardQualified,
        bool recordSupplied,
        string? recordSha,
        RecordIdentity? identity)
    {
        var accepted = identity is not null && failures.Length == 0;
        var root = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["kind"] = "stable-promotion-handoff-v2",
            ["passed"] = failures.Length == 0,
            ["guardQualified"] = guardQualified,
            ["contractSha256"] = Sha256(contractBytes),
            ["upstreamAcceptanceContractSha256"] = Sha256(authorityBytes),
            ["stableVersion"] = StableVersion,
            ["stableTag"] = StableTag,
            ["artifactPlatformCount"] = ArtifactPlatforms.Length,
            ["upstreamDecisionCount"] = UpstreamDecisionIds.Length,
            ["preservedEvidenceCategoryCount"] = PreservedEvidenceCategories.Length,
            ["stableContractAcknowledgementCount"] = StableContractAcknowledgements.Length,
            ["recordSupplied"] = recordSupplied,
            ["recordSha256"] = recordSha,
            ["recordIntegrityQualified"] = accepted,
            ["protectedWorkflowAttested"] = accepted,
            ["promotionComplete"] = accepted,
            ["releaseAcceptance"] = accepted,
            ["sourceRevision"] = accepted ? identity!.SourceRevision : null,
            ["protectedWorkflowRunId"] = accepted ? identity!.ProtectedWorkflowRunId : null,
            ["artifactSha256ByPlatform"] = PlatformJson(accepted ? identity!.Artifacts : EmptyMap()),
            ["manifestSha256ByPlatform"] = PlatformJson(accepted ? identity!.Manifests : EmptyMap()),
            ["optionalPackSha256"] = accepted ? identity!.OptionalPackSha256 : null,
            ["optionalPackManifestSha256"] = accepted ? identity!.OptionalPackManifestSha256 : null,
            ["pendingGates"] = new JsonArray(
                (accepted ? [] : PendingGates).Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()),
            ["errors"] = new JsonArray(failures.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()),
        };
        var json = root.ToJsonString(RenderOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        if (StrictUtf8.GetByteCount(json) > MaximumOutputBytes)
        {
            throw new InvalidDataException($"stable promotion evidence exceeds the {MaximumOutputBytes}-byte output limit");
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

    private static Dictionary<string, string> EmptyMap() => new(StringComparer.Ordinal);

    private static void WriteAtomicEvidence(
        string root,
        IReadOnlyList<string> trustedInputs,
        string outputPath,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var path = Path.GetFullPath(Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(root, outputPath));
        EnsureContained(root, path, "stable promotion evidence output");
        var relativeOutput = GetContainedRelativePath(root, path, "stable promotion evidence output")
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!IsSafeRelativePath(relativeOutput))
        {
            throw new InvalidDataException("stable promotion evidence output must use a safe portable relative path");
        }
        if (trustedInputs.Any(input => PortablePathsAlias(input, path)))
        {
            throw new InvalidDataException("stable promotion evidence output cannot alias a qualification input");
        }
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("stable promotion evidence output has no parent directory");
        CreateLinkFreeDirectory(root, parent, "stable promotion evidence output parent");
        if (Path.Exists(path))
        {
            EnsureNoLinks(root, path, "stable promotion evidence output");
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            {
                throw new InvalidDataException("stable promotion evidence output must be a regular file");
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
                throw new InvalidDataException("stable promotion evidence write verification failed");
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
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("repository root must be an existing non-link directory");
        }
        return root;
    }

    private static string ResolveRegularFile(string root, string relativePath, long maximumBytes, string label)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
        if (!Directory.Exists(parent) || (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
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
        if (length == 0 || length > maximumBytes)
        {
            throw new InvalidDataException($"{label} must be nonempty and at most {maximumBytes} bytes");
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
        source.ReadExactly(bytes);
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
        return new FileSnapshot(total, after.LastWriteTimeUtc, Convert.ToHexStringLower(hash.GetHashAndReset()));
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

    private static void RequireExactArray(JsonElement value, string[] expected, string label, List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            failures.Add($"{label} must equal [{string.Join(", ", expected)}]");
            return;
        }
        if (!value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .SequenceEqual(expected, StringComparer.Ordinal))
        {
            failures.Add($"{label} must equal [{string.Join(", ", expected)}]");
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
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var actual) || actual != expected)
        {
            failures.Add($"{label} must be integer {expected}");
        }
    }

    private static long? RequireNonnegativeInteger(JsonElement value, string label, List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var actual) || actual < 0)
        {
            failures.Add($"{label} must be a nonnegative integer");
            return null;
        }
        return actual;
    }

    private static void RequireBoolean(JsonElement value, bool expected, string label, List<string> failures)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || value.GetBoolean() != expected)
        {
            failures.Add($"{label} must be {expected.ToString().ToLowerInvariant()}");
        }
    }

    private static void RequireExactText(JsonElement value, string? expected, string label, List<string> failures)
    {
        if (expected is null || value.ValueKind != JsonValueKind.String
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
            failures.Add($"{label} must be a nonempty NFC string up to {maximumCharacters} characters");
            return null;
        }
        return result;
    }

    private static string? RequireDigest(JsonElement value, string label, List<string> failures) =>
        RequireLowerHex(value, 64, label, failures);

    private static string? RequireLowerHex(JsonElement value, int length, string label, List<string> failures)
    {
        if (value.ValueKind != JsonValueKind.String || !IsLowerHex(value.GetString(), length))
        {
            failures.Add($"{label} must be {length} lowercase hexadecimal characters");
            return null;
        }
        return value.GetString();
    }

    private static void ValidateUtc(JsonElement value, string label, List<string> failures)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (text is null
            || !DateTimeOffset.TryParseExact(
                text,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
            || parsed.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) != text)
        {
            failures.Add($"{label} must use a valid YYYY-MM-DDTHH:MM:SSZ UTC timestamp");
        }
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
            ? match.Groups[4].Value switch { "alpha" => 0, "beta" => 1, "rc" => 2, _ => -1 }
            : 3;
        var sequence = 0;
        if (channel < 3
            && !int.TryParse(match.Groups[5].Value, NumberStyles.None, CultureInfo.InvariantCulture, out sequence))
        {
            version = default;
            return false;
        }
        version = new ComparableVersion(major, minor, patch, channel, sequence);
        return true;
    }

    private static bool IsSafeRelativePath(string value)
    {
        if (value.Length is < 1 or > MaximumRelativePathCharacters
            || !value.IsNormalized(NormalizationForm.FormC)
            || value[0] == '/'
            || value[^1] == '/'
            || value.Contains('\\')
            || value.Contains(':')
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

    private static string ParentPrefix(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        return index < 0 ? string.Empty : relativePath[..index];
    }

    private static string CombineRelative(string prefix, string relative) =>
        prefix.Length == 0 ? relative : $"{prefix}/{relative}";

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
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
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
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
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

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

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

    private static bool PortablePathsAlias(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string value) => value.Normalize(NormalizationForm.FormC);

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();

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
        new("Stable promotion", false, string.Empty, BoundFailures(failures));

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    private sealed record Qualification(
        string OutputRoot,
        string[] Failures,
        bool RecordAccepted,
        string Json,
        IReadOnlyList<string> TrustedInputs);

    private sealed record AcceptanceAuthority(
        IReadOnlyDictionary<string, string> Kinds,
        IReadOnlyDictionary<string, string[]> Gates);

    private sealed record AcceptanceProfile(
        string Kind,
        string ArtifactBinding,
        string[] AdditionalFields);

    private sealed record RecordIdentity(
        string SourceRevision,
        string ProtectedWorkflowRunId,
        IReadOnlyDictionary<string, string> Artifacts,
        IReadOnlyDictionary<string, string> Manifests,
        string OptionalPackSha256,
        string OptionalPackManifestSha256);

    private sealed record ManifestIdentity(string SmokeStateHash);
    private sealed record ManifestEntry(string Path, long Bytes);
    private sealed record NestedPath(string Relative, string Global);
    private sealed record RetainedFile(string Path, string RelativePath, long Length);
    private readonly record struct FileSnapshot(long Length, DateTime LastWriteTimeUtc, string Sha256);

    private readonly record struct ComparableVersion(
        int Major,
        int Minor,
        int Patch,
        int Channel,
        int Sequence) : IComparable<ComparableVersion>
    {
        public int CompareTo(ComparableVersion other)
        {
            var left = (Major, Minor, Patch, Channel, Sequence);
            var right = (other.Major, other.Minor, other.Patch, other.Channel, other.Sequence);
            return left.CompareTo(right);
        }
    }

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
            if (!Directory.Exists(this.root) || (File.GetAttributes(this.root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("retained-file root must be a regular non-link directory");
            }
        }

        public IEnumerable<string> RelativePaths => files.Keys;
        public IEnumerable<string> ResolvedPaths => files.Values.Select(file => file.Path);

        public void Add(string relativePath, string exclusiveLabel)
        {
            if (files.TryGetValue(relativePath, out var existingFile))
            {
                ReserveExclusive(existingFile.Path, exclusiveLabel);
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
                var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
                ReserveExclusive(path, exclusiveLabel);
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                failures.Add($"{relativePath}: {SingleLine(exception.Message)}");
            }
        }

        public bool TryGet(string relativePath, out RetainedFile file) => files.TryGetValue(relativePath, out file!);

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
            var snapshot = new FileSnapshot(bytes.LongLength, new FileInfo(file.Path).LastWriteTimeUtc, Sha256(bytes));
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
                if (current != snapshot)
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
            }
            else
            {
                exclusivePaths[path] = label;
            }
        }
    }
}
