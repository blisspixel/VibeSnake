using System.Text.RegularExpressions;

namespace VibeSnake.Persistence;

public sealed record ReleaseOutputPlan(
    int SchemaVersion,
    string Kind,
    string Product,
    string ProductVersion,
    string Platform,
    string QualifiedInputShape,
    string DirectDownloadShape,
    string DirectDownloadFileName,
    string StoreDepotShape,
    string ArtifactManifestOutputName,
    string ChecksumOutputName,
    bool InstallerProvided,
    bool OptionalPackOutputSeparate,
    bool BaseGameIncludesOptionalPacks,
    bool PlayerDataExcluded,
    bool UninstallPreservesPlayerData,
    bool QualificationOnly,
    bool AssemblyEligible,
    bool PublicationEligible,
    IReadOnlyList<string> PublicationBlockers,
    long? PackageBytes,
    string? PackageSha256,
    bool DeterministicRepeatMatched,
    bool Passed)
{
    public const string PlanKind = "release-output-plan-v1";
    public const string ArtifactManifestOutput = "artifact-manifest.json";
    public const string ChecksumOutput = "SHA256SUMS";
    public const string OptionalPackExtension = ".vibesnake-pack.zip";

    private static readonly Regex ProductVersionPattern = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static ReleaseOutputPlan Create(
        ReleaseArtifactManifest artifact,
        ReleaseSigningReadiness signingReadiness,
        string productVersion,
        bool qualificationOnly)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(signingReadiness);
        if (string.IsNullOrWhiteSpace(productVersion)
            || productVersion.Length > 64
            || !ProductVersionPattern.IsMatch(productVersion))
        {
            throw new InvalidDataException(
                "Release output product version must be a bounded semantic version.");
        }
        if (!signingReadiness.Passed
            || signingReadiness.Kind != ReleaseSigningPolicy.ReadinessKind
            || signingReadiness.SigningState != "unsigned-input"
            || signingReadiness.Product != artifact.Product
            || signingReadiness.Platform != artifact.Platform
            || signingReadiness.SourceRevision != artifact.SourceRevision
            || signingReadiness.BuildMode != artifact.BuildMode)
        {
            throw new InvalidDataException(
                "Signing readiness does not match the qualified artifact.");
        }
        var qualifiedShape = ReleaseArtifactManifest.DeclaredInstallerArchiveShape(
            artifact.Platform);
        if (qualifiedShape == "unknown"
            || signingReadiness.ArtifactShape != qualifiedShape)
        {
            throw new InvalidDataException(
                "Qualified artifact shape is unsupported or inconsistent.");
        }

        var directShape = artifact.Platform switch
        {
            "windows-x64" => "zip-archive",
            "linux-x64" => "tar-gzip-archive",
            "macos-universal" => "app-bundle-zip",
            _ => throw new InvalidDataException("Release output platform is unsupported."),
        };
        var extension = artifact.Platform switch
        {
            "windows-x64" => ".zip",
            "linux-x64" => ".tar.gz",
            "macos-universal" => ".zip",
            _ => throw new InvalidDataException("Release output platform is unsupported."),
        };
        var depotShape = artifact.Platform == "macos-universal"
            ? "app-bundle"
            : "portable-folder";
        var qualifier = qualificationOnly ? "-qualification" : string.Empty;
        var packageName = $"VibeSnake-{productVersion}-{artifact.Platform}{qualifier}{extension}";
        var assemblyEligible = qualificationOnly || signingReadiness.PromotionEligible;
        if (!assemblyEligible)
        {
            throw new InvalidDataException(
                "Non-qualification output requires a promotable Release artifact.");
        }

        var blockers = new List<string>();
        if (qualificationOnly)
        {
            blockers.Add("qualification-only-input");
        }
        if (artifact.Platform is "windows-x64" or "macos-universal")
        {
            blockers.Add("protected-platform-signing");
        }
        blockers.Add("signed-output-verification");
        blockers.Add("post-signing-checksums");
        blockers.Add("final-provenance");
        blockers.Add("channel-approval");

        return new ReleaseOutputPlan(
            SchemaVersion: 1,
            Kind: PlanKind,
            Product: artifact.Product,
            ProductVersion: productVersion,
            Platform: artifact.Platform,
            QualifiedInputShape: qualifiedShape,
            DirectDownloadShape: directShape,
            DirectDownloadFileName: packageName,
            StoreDepotShape: depotShape,
            ArtifactManifestOutputName: ArtifactManifestOutput,
            ChecksumOutputName: ChecksumOutput,
            InstallerProvided: false,
            OptionalPackOutputSeparate: true,
            BaseGameIncludesOptionalPacks: false,
            PlayerDataExcluded: true,
            UninstallPreservesPlayerData: true,
            QualificationOnly: qualificationOnly,
            AssemblyEligible: assemblyEligible,
            PublicationEligible: false,
            PublicationBlockers: blockers,
            PackageBytes: null,
            PackageSha256: null,
            DeterministicRepeatMatched: false,
            Passed: true);
    }
}
