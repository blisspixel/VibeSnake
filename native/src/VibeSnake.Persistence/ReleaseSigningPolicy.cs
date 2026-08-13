using System.Text.Json;

namespace VibeSnake.Persistence;

public enum ReleaseSigningPolicyLoadCode : byte
{
    Success = 0,
    Empty = 1,
    InvalidJson = 2,
    UnsupportedSchema = 3,
    InvalidField = 4,
}

public sealed record ReleaseSigningPolicyLoadResult(
    ReleaseSigningPolicyLoadCode Code,
    string Message,
    ReleaseSigningPolicy? Policy = null)
{
    public bool IsSuccess => Code == ReleaseSigningPolicyLoadCode.Success && Policy is not null;
}

public sealed record ReleasePlatformSigningPolicy(
    string Platform,
    string ArtifactShape,
    string PlatformSigning,
    string Notarization,
    IReadOnlyList<string> SignableTargets,
    IReadOnlyList<string> RequiredVerifications);

public sealed record ReleaseSigningReadiness(
    int SchemaVersion,
    string Kind,
    string Product,
    string Platform,
    string ArtifactShape,
    string ArtifactManifestSha256,
    string SourceRevision,
    string BuildMode,
    string SigningState,
    string PlatformSigning,
    string Notarization,
    string CredentialBoundary,
    bool OrdinaryCiCredentialAccess,
    bool ReleaseEnvironmentRequired,
    bool SigningMaterialAllowedInRepository,
    bool SigningMaterialAllowedInArtifacts,
    bool ChecksumsAfterPlatformSigning,
    bool AttestAfterPlatformSigning,
    bool PublisherIdentityRequiredAtPromotion,
    IReadOnlyList<string> SignableTargets,
    IReadOnlyList<string> RequiredVerifications,
    bool PromotionEligible,
    string PromotionStatus,
    bool Passed);

/// <summary>
/// Strict, non-secret signing policy. It describes the boundary between an
/// unsigned qualified build and protected platform signing without containing
/// certificates, private keys, passwords, or notarization credentials.
/// </summary>
public sealed record ReleaseSigningPolicy(
    int SchemaVersion,
    string Kind,
    string CredentialBoundary,
    bool OrdinaryCiCredentialAccess,
    bool ReleaseEnvironmentRequired,
    bool SigningMaterialAllowedInRepository,
    bool SigningMaterialAllowedInArtifacts,
    bool ChecksumsAfterPlatformSigning,
    bool AttestAfterPlatformSigning,
    bool PublisherIdentityRequiredAtPromotion,
    IReadOnlyList<ReleasePlatformSigningPolicy> Platforms)
{
    public const int CurrentSchemaVersion = 1;
    public const string PolicyKind = "release-signing-policy-v1";
    public const string ReadinessKind = "release-signing-readiness-v1";
    public const int MaximumPolicyBytes = 64 * 1024;

    private static readonly string[] RootFields =
    [
        "schemaVersion",
        "kind",
        "credentialBoundary",
        "ordinaryCiCredentialAccess",
        "releaseEnvironmentRequired",
        "signingMaterialAllowedInRepository",
        "signingMaterialAllowedInArtifacts",
        "checksumsAfterPlatformSigning",
        "attestAfterPlatformSigning",
        "publisherIdentityRequiredAtPromotion",
        "platforms",
    ];

    private static readonly string[] PlatformFields =
    [
        "platform",
        "artifactShape",
        "platformSigning",
        "notarization",
        "signableTargets",
        "requiredVerifications",
    ];

    public static ReleaseSigningPolicyLoadResult LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Failure(ReleaseSigningPolicyLoadCode.Empty, "Signing policy path is empty.");
        }
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return Failure(
                    ReleaseSigningPolicyLoadCode.InvalidField,
                    "Signing policy file does not exist.");
            }
            if (info.Length > MaximumPolicyBytes)
            {
                return Failure(
                    ReleaseSigningPolicyLoadCode.InvalidField,
                    $"Signing policy exceeds {MaximumPolicyBytes} bytes.");
            }
            return Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                ReleaseSigningPolicyLoadCode.InvalidField,
                "Could not read signing policy: " + exception.Message);
        }
    }

    public static ReleaseSigningPolicyLoadResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failure(ReleaseSigningPolicyLoadCode.Empty, "Signing policy is empty.");
        }
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumPolicyBytes)
        {
            return Failure(
                ReleaseSigningPolicyLoadCode.InvalidField,
                $"Signing policy exceeds {MaximumPolicyBytes} bytes.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });
        }
        catch (JsonException exception)
        {
            return Failure(
                ReleaseSigningPolicyLoadCode.InvalidJson,
                "Signing policy JSON is invalid: " + exception.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("Signing policy root must be an object.");
            }
            var fieldError = ValidateExactFields(root, RootFields, "Signing policy");
            if (fieldError is not null)
            {
                return Invalid(fieldError);
            }
            if (!TryGetInt(root, "schemaVersion", out var schemaVersion))
            {
                return Invalid("schemaVersion must be an integer.");
            }
            if (schemaVersion != CurrentSchemaVersion)
            {
                return Failure(
                    ReleaseSigningPolicyLoadCode.UnsupportedSchema,
                    "Unsupported signing policy schemaVersion: " + schemaVersion);
            }
            if (!TryGetString(root, "kind", out var kind) || kind != PolicyKind)
            {
                return Invalid("kind must be " + PolicyKind + ".");
            }
            if (!TryGetString(root, "credentialBoundary", out var credentialBoundary)
                || credentialBoundary != "protected-release-environment")
            {
                return Invalid(
                    "credentialBoundary must be protected-release-environment.");
            }

            if (!TryGetBool(root, "ordinaryCiCredentialAccess", out var ordinaryCiAccess)
                || ordinaryCiAccess
                || !TryGetBool(root, "releaseEnvironmentRequired", out var environmentRequired)
                || !environmentRequired
                || !TryGetBool(
                    root,
                    "signingMaterialAllowedInRepository",
                    out var repositoryMaterial)
                || repositoryMaterial
                || !TryGetBool(
                    root,
                    "signingMaterialAllowedInArtifacts",
                    out var artifactMaterial)
                || artifactMaterial
                || !TryGetBool(
                    root,
                    "checksumsAfterPlatformSigning",
                    out var checksumsAfterSigning)
                || !checksumsAfterSigning
                || !TryGetBool(
                    root,
                    "attestAfterPlatformSigning",
                    out var attestAfterSigning)
                || !attestAfterSigning
                || !TryGetBool(
                    root,
                    "publisherIdentityRequiredAtPromotion",
                    out var publisherRequired)
                || !publisherRequired)
            {
                return Invalid("Signing separation safety flags cannot be weakened.");
            }

            if (!root.TryGetProperty("platforms", out var platformsElement)
                || platformsElement.ValueKind != JsonValueKind.Array)
            {
                return Invalid("platforms must be an array.");
            }
            var platforms = new List<ReleasePlatformSigningPolicy>();
            var seenPlatforms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in platformsElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return Invalid("platforms entries must be objects.");
                }
                fieldError = ValidateExactFields(element, PlatformFields, "Signing platform");
                if (fieldError is not null)
                {
                    return Invalid(fieldError);
                }
                if (!TryGetString(element, "platform", out var platform)
                    || !seenPlatforms.Add(platform))
                {
                    return Invalid("Signing platform ids must be present and unique.");
                }
                string[] targets = [];
                string[] verifications = [];
                string? arrayError = null;
                if (!TryGetString(element, "artifactShape", out var shape)
                    || !TryGetString(element, "platformSigning", out var platformSigning)
                    || !TryGetString(element, "notarization", out var notarization)
                    || !TryReadStringArray(
                        element,
                        "signableTargets",
                        out targets,
                        out arrayError)
                    || !TryReadStringArray(
                        element,
                        "requiredVerifications",
                        out verifications,
                        out arrayError))
                {
                    return Invalid(arrayError ?? "Signing platform fields are invalid.");
                }
                platforms.Add(new ReleasePlatformSigningPolicy(
                    platform,
                    shape,
                    platformSigning,
                    notarization,
                    targets,
                    verifications));
            }

            var platformError = ValidatePlatformContracts(platforms);
            if (platformError is not null)
            {
                return Invalid(platformError);
            }
            return new ReleaseSigningPolicyLoadResult(
                ReleaseSigningPolicyLoadCode.Success,
                "ok",
                new ReleaseSigningPolicy(
                    schemaVersion,
                    kind,
                    credentialBoundary,
                    ordinaryCiAccess,
                    environmentRequired,
                    repositoryMaterial,
                    artifactMaterial,
                    checksumsAfterSigning,
                    attestAfterSigning,
                    publisherRequired,
                    platforms.OrderBy(item => item.Platform, StringComparer.Ordinal).ToArray()));
        }
    }

    public ReleaseSigningReadiness Evaluate(
        ReleaseArtifactManifest artifact,
        string artifactManifestSha256)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (!IsLowerHex(artifactManifestSha256, 64))
        {
            throw new InvalidDataException(
                "Artifact manifest SHA-256 must be 64 lowercase hex characters.");
        }
        var route = Platforms.SingleOrDefault(item => item.Platform == artifact.Platform)
            ?? throw new InvalidDataException(
                "Signing policy has no route for artifact platform " + artifact.Platform + ".");
        var declaredShape = ReleaseArtifactManifest.DeclaredInstallerArchiveShape(
            artifact.Platform);
        if (route.ArtifactShape != declaredShape)
        {
            throw new InvalidDataException("Signing policy artifact shape does not match manifest.");
        }
        if (!HasSignableTargets(artifact, route))
        {
            throw new InvalidDataException(
                "Artifact does not contain every platform signing target.");
        }

        var releaseRevision = IsLowerHex(artifact.SourceRevision, 40);
        var promotionEligible = artifact.BuildMode == "Release" && releaseRevision;
        var promotionStatus = promotionEligible
            ? "ready-for-protected-signing"
            : artifact.BuildMode != "Release"
                ? "debug-artifact-not-promotable"
                : "source-revision-not-promotable";
        return new ReleaseSigningReadiness(
            SchemaVersion: 1,
            Kind: ReadinessKind,
            Product: artifact.Product,
            Platform: artifact.Platform,
            ArtifactShape: route.ArtifactShape,
            ArtifactManifestSha256: artifactManifestSha256,
            SourceRevision: artifact.SourceRevision,
            BuildMode: artifact.BuildMode,
            SigningState: "unsigned-input",
            PlatformSigning: route.PlatformSigning,
            Notarization: route.Notarization,
            CredentialBoundary: CredentialBoundary,
            OrdinaryCiCredentialAccess: OrdinaryCiCredentialAccess,
            ReleaseEnvironmentRequired: ReleaseEnvironmentRequired,
            SigningMaterialAllowedInRepository: SigningMaterialAllowedInRepository,
            SigningMaterialAllowedInArtifacts: SigningMaterialAllowedInArtifacts,
            ChecksumsAfterPlatformSigning: ChecksumsAfterPlatformSigning,
            AttestAfterPlatformSigning: AttestAfterPlatformSigning,
            PublisherIdentityRequiredAtPromotion: PublisherIdentityRequiredAtPromotion,
            SignableTargets: route.SignableTargets,
            RequiredVerifications: route.RequiredVerifications,
            PromotionEligible: promotionEligible,
            PromotionStatus: promotionStatus,
            Passed: true);
    }

    private static bool HasSignableTargets(
        ReleaseArtifactManifest artifact,
        ReleasePlatformSigningPolicy route)
    {
        var files = artifact.Files.Select(file => file.Path.Replace('\\', '/')).ToArray();
        var containerFiles = artifact.ContainerEntries
            .Select(file => file.Path.Replace('\\', '/'))
            .ToArray();
        return route.Platform switch
        {
            "windows-x64" => route.SignableTargets.All(target => files.Contains(
                target,
                StringComparer.Ordinal)),
            "macos-universal" => route.SignableTargets.All(target => containerFiles.Any(path =>
                path.StartsWith(target + "/", StringComparison.Ordinal))),
            "linux-x64" => route.SignableTargets.Count == 0,
            _ => false,
        };
    }

    private static string? ValidatePlatformContracts(
        List<ReleasePlatformSigningPolicy> platforms)
    {
        if (platforms.Count != ReleaseArtifactManifest.SupportedPlatforms.Length)
        {
            return "Signing policy must define exactly every supported platform.";
        }
        string? Check(
            string platform,
            string shape,
            string signing,
            string notarization,
            string[] targets,
            string[] verifications)
        {
            var route = platforms.SingleOrDefault(item => item.Platform == platform);
            if (route is null
                || route.ArtifactShape != shape
                || route.PlatformSigning != signing
                || route.Notarization != notarization
                || !route.SignableTargets.SequenceEqual(targets, StringComparer.Ordinal)
                || !route.RequiredVerifications.SequenceEqual(
                    verifications,
                    StringComparer.Ordinal))
            {
                return "Signing policy route is incomplete or weakened for " + platform + ".";
            }
            return null;
        }

        return Check(
                "linux-x64",
                "portable-folder",
                "not-applicable",
                "not-applicable",
                [],
                ["executable-permission", "sha256-checksums", "github-oidc-provenance"])
            ?? Check(
                "macos-universal",
                "app-bundle-zip",
                "developer-id-hardened-runtime",
                "apple-notary-service",
                ["Vibe Snake.app"],
                [
                    "codesign-strict-verify",
                    "hardened-runtime-verify",
                    "notarization-accepted",
                    "stapler-validate",
                    "gatekeeper-assess",
                    "sha256-checksums",
                    "github-oidc-provenance",
                ])
            ?? Check(
                "windows-x64",
                "portable-folder",
                "authenticode-sha256",
                "not-applicable",
                ["VibeSnake.exe"],
                ["signtool-policy-verify", "sha256-checksums", "github-oidc-provenance"]);
    }

    private static string? ValidateExactFields(
        JsonElement element,
        IReadOnlyCollection<string> expected,
        string location)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                return location + " contains duplicate field " + property.Name + ".";
            }
            if (!expected.Contains(property.Name, StringComparer.Ordinal))
            {
                return location + " contains unknown field " + property.Name + ".";
            }
        }
        var missing = expected.Where(field => !seen.Contains(field)).ToArray();
        return missing.Length == 0
            ? null
            : location + " is missing field " + missing[0] + ".";
    }

    private static bool TryReadStringArray(
        JsonElement root,
        string name,
        out string[] values,
        out string? error)
    {
        values = [];
        error = null;
        if (!root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            error = name + " must be an array.";
            return false;
        }
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = name + " entries must be strings.";
                return false;
            }
            var value = item.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > 128
                || !seen.Add(value))
            {
                error = name + " entries must be bounded, non-empty, and unique.";
                return false;
            }
            result.Add(value);
        }
        values = result.ToArray();
        return true;
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool TryGetBool(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var element)
            || (element.ValueKind != JsonValueKind.True
                && element.ValueKind != JsonValueKind.False))
        {
            return false;
        }
        value = element.GetBoolean();
        return true;
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool IsLowerHex(string value, int expectedLength) =>
        value.Length == expectedLength
        && value.All(character =>
            char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    private static ReleaseSigningPolicyLoadResult Invalid(string message) =>
        Failure(ReleaseSigningPolicyLoadCode.InvalidField, message);

    private static ReleaseSigningPolicyLoadResult Failure(
        ReleaseSigningPolicyLoadCode code,
        string message) => new(code, message);
}
