using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ReleaseOutputPlanTests
{
    [Theory]
    [InlineData("windows-x64", "portable-folder", "zip-archive", "portable-folder", ".zip")]
    [InlineData("linux-x64", "portable-folder", "tar-gzip-archive", "portable-folder", ".tar.gz")]
    [InlineData("macos-universal", "app-bundle-zip", "app-bundle-zip", "app-bundle", ".zip")]
    public void Qualification_outputs_define_exact_direct_and_depot_shapes(
        string platform,
        string inputShape,
        string downloadShape,
        string depotShape,
        string extension)
    {
        var artifact = Artifact(platform, "Debug");
        var plan = ReleaseOutputPlan.Create(
            artifact,
            Readiness(artifact, inputShape, promotionEligible: false),
            "0.2.1",
            qualificationOnly: true);

        Assert.True(plan.Passed);
        Assert.True(plan.AssemblyEligible);
        Assert.False(plan.PublicationEligible);
        Assert.Equal(inputShape, plan.QualifiedInputShape);
        Assert.Equal(downloadShape, plan.DirectDownloadShape);
        Assert.Equal(depotShape, plan.StoreDepotShape);
        Assert.EndsWith("-qualification" + extension, plan.DirectDownloadFileName);
        Assert.True(plan.OptionalPackOutputSeparate);
        Assert.False(plan.BaseGameIncludesOptionalPacks);
        Assert.True(plan.PlayerDataExcluded);
        Assert.True(plan.UninstallPreservesPlayerData);
    }

    [Fact]
    public void Release_assembly_still_blocks_publication_until_final_verification()
    {
        var artifact = Artifact("windows-x64", "Release");
        var plan = ReleaseOutputPlan.Create(
            artifact,
            Readiness(artifact, "portable-folder", promotionEligible: true),
            "1.0.0-rc.1",
            qualificationOnly: false);

        Assert.True(plan.AssemblyEligible);
        Assert.False(plan.QualificationOnly);
        Assert.False(plan.PublicationEligible);
        Assert.Equal("VibeSnake-1.0.0-rc.1-windows-x64.zip", plan.DirectDownloadFileName);
        Assert.Contains("protected-platform-signing", plan.PublicationBlockers);
        Assert.Contains("post-signing-checksums", plan.PublicationBlockers);
        Assert.Contains("final-provenance", plan.PublicationBlockers);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0+local")]
    [InlineData("../1.0.0")]
    public void Rejects_unsafe_or_noncanonical_product_versions(string version)
    {
        var artifact = Artifact("linux-x64", "Debug");
        Assert.Throws<InvalidDataException>(() => ReleaseOutputPlan.Create(
            artifact,
            Readiness(artifact, "portable-folder", promotionEligible: false),
            version,
            qualificationOnly: true));
    }

    [Fact]
    public void Nonqualification_output_requires_release_promotion_readiness()
    {
        var artifact = Artifact("windows-x64", "Debug");
        Assert.Throws<InvalidDataException>(() => ReleaseOutputPlan.Create(
            artifact,
            Readiness(artifact, "portable-folder", promotionEligible: false),
            "0.2.1",
            qualificationOnly: false));
    }

    [Fact]
    public void Rejects_readiness_for_another_artifact_or_shape()
    {
        var artifact = Artifact("windows-x64", "Debug");
        var wrongPlatform = Readiness(
            artifact with { Platform = "linux-x64" },
            "portable-folder",
            promotionEligible: false);
        var wrongShape = Readiness(artifact, "app-bundle-zip", promotionEligible: false);

        Assert.Throws<InvalidDataException>(() => ReleaseOutputPlan.Create(
            artifact,
            wrongPlatform,
            "0.2.1",
            qualificationOnly: true));
        Assert.Throws<InvalidDataException>(() => ReleaseOutputPlan.Create(
            artifact,
            wrongShape,
            "0.2.1",
            qualificationOnly: true));
    }

    private static ReleaseArtifactManifest Artifact(string platform, string buildMode) => new(
        SchemaVersion: 2,
        Product: "Vibe Snake",
        Platform: platform,
        BuildMode: buildMode,
        SourceRevision: "abcdef0123456789abcdef0123456789abcdef01",
        GodotVersion: "4.7.1",
        GodotCommit: "a13da4feb",
        GodotArchiveSha512: new string('a', 128),
        GodotExecutableSha256: new string('b', 64),
        DotnetSdk: "10.0.303",
        SmokeStateHash: "0123456789abcdef",
        AgentArenaPreviewExcluded: string.Equals(buildMode, "Release", StringComparison.Ordinal),
        FileCount: 0,
        TotalBytes: 0,
        Files: [],
        ContainerEntries: []);

    private static ReleaseSigningReadiness Readiness(
        ReleaseArtifactManifest artifact,
        string shape,
        bool promotionEligible) => new(
            SchemaVersion: 1,
            Kind: ReleaseSigningPolicy.ReadinessKind,
            Product: artifact.Product,
            Platform: artifact.Platform,
            ArtifactShape: shape,
            ArtifactManifestSha256: new string('c', 64),
            SourceRevision: artifact.SourceRevision,
            BuildMode: artifact.BuildMode,
            SigningState: "unsigned-input",
            PlatformSigning: "not-applicable",
            Notarization: "not-applicable",
            CredentialBoundary: "protected-release-environment",
            OrdinaryCiCredentialAccess: false,
            ReleaseEnvironmentRequired: true,
            SigningMaterialAllowedInRepository: false,
            SigningMaterialAllowedInArtifacts: false,
            ChecksumsAfterPlatformSigning: true,
            AttestAfterPlatformSigning: true,
            PublisherIdentityRequiredAtPromotion: true,
            SignableTargets: [],
            RequiredVerifications: [],
            PromotionEligible: promotionEligible,
            PromotionStatus: promotionEligible
                ? "ready-for-protected-signing"
                : "debug-artifact-not-promotable",
            Passed: true);
}
