using VibeSnake.Persistence;
using System.Text.Json.Nodes;

namespace VibeSnake.Rules.Tests;

public sealed class ReleaseSigningPolicyTests
{
    private const string Revision = "abcdef0123456789abcdef0123456789abcdef01";
    private const string Digest =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Repository_policy_locks_every_platform_and_credential_boundary()
    {
        var result = ReleaseSigningPolicy.LoadFromFile(ResolvePolicyPath());

        Assert.True(result.IsSuccess, result.Message);
        var policy = Assert.IsType<ReleaseSigningPolicy>(result.Policy);
        Assert.Equal(3, policy.Platforms.Count);
        Assert.False(policy.OrdinaryCiCredentialAccess);
        Assert.True(policy.ReleaseEnvironmentRequired);
        Assert.False(policy.SigningMaterialAllowedInRepository);
        Assert.False(policy.SigningMaterialAllowedInArtifacts);
        Assert.True(policy.ChecksumsAfterPlatformSigning);
        Assert.True(policy.AttestAfterPlatformSigning);
        Assert.True(policy.PublisherIdentityRequiredAtPromotion);
        Assert.Equal(
            ReleaseArtifactManifest.SupportedPlatforms.Order(StringComparer.Ordinal),
            policy.Platforms.Select(item => item.Platform));
    }

    [Fact]
    public void Release_artifacts_route_to_platform_signing_and_provenance()
    {
        var policy = LoadPolicy();
        var windows = policy.Evaluate(
            Artifact("windows-x64", "Release", [Entry("VibeSnake.exe")], []),
            Digest);
        var mac = policy.Evaluate(
            Artifact(
                "macos-universal",
                "Release",
                [Entry("VibeSnake.zip")],
                [Entry("Vibe Snake.app/Contents/MacOS/Vibe Snake")]),
            Digest);
        var linux = policy.Evaluate(
            Artifact("linux-x64", "Release", [Entry("VibeSnake.x86_64")], []),
            Digest);

        Assert.All([windows, mac, linux], readiness =>
        {
            Assert.True(readiness.Passed);
            Assert.True(readiness.PromotionEligible);
            Assert.Equal("unsigned-input", readiness.SigningState);
            Assert.Equal("ready-for-protected-signing", readiness.PromotionStatus);
            Assert.Contains("sha256-checksums", readiness.RequiredVerifications);
            Assert.Contains("github-oidc-provenance", readiness.RequiredVerifications);
        });
        Assert.Equal("authenticode-sha256", windows.PlatformSigning);
        Assert.Contains("signtool-policy-verify", windows.RequiredVerifications);
        Assert.Equal("developer-id-hardened-runtime", mac.PlatformSigning);
        Assert.Equal("apple-notary-service", mac.Notarization);
        Assert.Contains("stapler-validate", mac.RequiredVerifications);
        Assert.Equal("not-applicable", linux.PlatformSigning);
        Assert.Empty(linux.SignableTargets);
    }

    [Fact]
    public void Debug_or_unversioned_artifacts_are_explicitly_not_promotable()
    {
        var policy = LoadPolicy();
        var debug = policy.Evaluate(
            Artifact("windows-x64", "Debug", [Entry("VibeSnake.exe")], []),
            Digest);
        var unavailable = policy.Evaluate(
            Artifact(
                "windows-x64",
                "Release",
                [Entry("VibeSnake.exe")],
                [],
                sourceRevision: "unavailable"),
            Digest);

        Assert.True(debug.Passed);
        Assert.False(debug.PromotionEligible);
        Assert.Equal("debug-artifact-not-promotable", debug.PromotionStatus);
        Assert.True(unavailable.Passed);
        Assert.False(unavailable.PromotionEligible);
        Assert.Equal("source-revision-not-promotable", unavailable.PromotionStatus);
    }

    [Fact]
    public void Parser_rejects_duplicate_unknown_or_weakened_policy_fields()
    {
        var valid = File.ReadAllText(ResolvePolicyPath());
        var duplicate = valid.Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1, \"schemaVersion\": 1,",
            StringComparison.Ordinal);
        var unknown = valid.Replace(
            "\"kind\": \"release-signing-policy-v1\",",
            "\"kind\": \"release-signing-policy-v1\", \"surprise\": true,",
            StringComparison.Ordinal);
        var weakened = valid.Replace(
            "\"ordinaryCiCredentialAccess\": false",
            "\"ordinaryCiCredentialAccess\": true",
            StringComparison.Ordinal);

        Assert.Equal(
            ReleaseSigningPolicyLoadCode.InvalidField,
            ReleaseSigningPolicy.Parse(duplicate).Code);
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.InvalidField,
            ReleaseSigningPolicy.Parse(unknown).Code);
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.InvalidField,
            ReleaseSigningPolicy.Parse(weakened).Code);
    }

    [Fact]
    public void Readiness_rejects_bad_manifest_digests_or_missing_signing_targets()
    {
        var policy = LoadPolicy();
        var artifact = Artifact("windows-x64", "Release", [Entry("other.exe")], []);

        Assert.Throws<InvalidDataException>(() => policy.Evaluate(artifact, "bad"));
        Assert.Throws<InvalidDataException>(() => policy.Evaluate(artifact, Digest));
    }

    [Fact]
    public void Load_and_parse_fail_closed_for_empty_missing_oversized_and_malformed_inputs()
    {
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.Empty,
            ReleaseSigningPolicy.LoadFromFile(" ").Code);
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.InvalidField,
            ReleaseSigningPolicy.LoadFromFile(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).Code);
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.Empty,
            ReleaseSigningPolicy.Parse(" ").Code);
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.InvalidJson,
            ReleaseSigningPolicy.Parse("{").Code);
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.InvalidField,
            ReleaseSigningPolicy.Parse("[]").Code);
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.InvalidField,
            ReleaseSigningPolicy.Parse(new string('x', ReleaseSigningPolicy.MaximumPolicyBytes + 1)).Code);
        Assert.False(
            new ReleaseSigningPolicyLoadResult(
                ReleaseSigningPolicyLoadCode.Success,
                "missing").IsSuccess);

        var oversizedPath = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-signing-policy-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(
                oversizedPath,
                new string('x', ReleaseSigningPolicy.MaximumPolicyBytes + 1));
            Assert.Equal(
                ReleaseSigningPolicyLoadCode.InvalidField,
                ReleaseSigningPolicy.LoadFromFile(oversizedPath).Code);
        }
        finally
        {
            File.Delete(oversizedPath);
        }
    }

    [Fact]
    public void Parser_rejects_every_missing_or_mistyped_root_field()
    {
        string[] fields =
        [
            "schemaVersion", "kind", "credentialBoundary", "ordinaryCiCredentialAccess",
            "releaseEnvironmentRequired", "signingMaterialAllowedInRepository",
            "signingMaterialAllowedInArtifacts", "checksumsAfterPlatformSigning",
            "attestAfterPlatformSigning", "publisherIdentityRequiredAtPromotion", "platforms",
        ];
        foreach (var field in fields)
        {
            var missing = ValidPolicyDocument();
            Assert.True(missing.Remove(field));
            AssertInvalid(missing);

            var mistyped = ValidPolicyDocument();
            mistyped[field] = field == "schemaVersion"
                ? JsonValue.Create("1")
                : JsonValue.Create(1);
            AssertInvalid(mistyped);
        }

        var unsupported = ValidPolicyDocument();
        unsupported["schemaVersion"] = 2;
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.UnsupportedSchema,
            ReleaseSigningPolicy.Parse(unsupported.ToJsonString()).Code);

        foreach (var (field, value) in new Dictionary<string, JsonNode?>
        {
            ["kind"] = "unexpected",
            ["credentialBoundary"] = "ordinary-ci",
            ["ordinaryCiCredentialAccess"] = true,
            ["releaseEnvironmentRequired"] = false,
            ["signingMaterialAllowedInRepository"] = true,
            ["signingMaterialAllowedInArtifacts"] = true,
            ["checksumsAfterPlatformSigning"] = false,
            ["attestAfterPlatformSigning"] = false,
            ["publisherIdentityRequiredAtPromotion"] = false,
            ["platforms"] = new JsonObject(),
        })
        {
            var document = ValidPolicyDocument();
            document[field] = value?.DeepClone();
            AssertInvalid(document);
        }
    }

    [Fact]
    public void Parser_rejects_malformed_platform_routes_and_arrays()
    {
        var nonObject = ValidPolicyDocument();
        nonObject["platforms"]!.AsArray()[0] = "linux-x64";
        AssertInvalid(nonObject);

        var duplicate = ValidPolicyDocument();
        duplicate["platforms"]!.AsArray()[1]!["platform"] =
            duplicate["platforms"]!.AsArray()[0]!["platform"]!.GetValue<string>();
        AssertInvalid(duplicate);

        string[] routeFields =
        [
            "platform", "artifactShape", "platformSigning", "notarization",
            "signableTargets", "requiredVerifications",
        ];
        foreach (var field in routeFields)
        {
            var missing = ValidPolicyDocument();
            Assert.True(missing["platforms"]!.AsArray()[0]!.AsObject().Remove(field));
            AssertInvalid(missing);

            var unknown = ValidPolicyDocument();
            unknown["platforms"]!.AsArray()[0]!["unknown"] = true;
            AssertInvalid(unknown);
        }

        foreach (var field in new[] { "artifactShape", "platformSigning", "notarization" })
        {
            var mistyped = ValidPolicyDocument();
            mistyped["platforms"]!.AsArray()[0]![field] = true;
            AssertInvalid(mistyped);
        }

        var wrongArray = ValidPolicyDocument();
        wrongArray["platforms"]!.AsArray()[0]!["signableTargets"] = true;
        AssertInvalid(wrongArray);

        foreach (var invalidEntry in new JsonNode?[]
        {
            JsonValue.Create(1),
            JsonValue.Create(" "),
            JsonValue.Create(new string('x', 129)),
        })
        {
            var invalidArrayEntry = ValidPolicyDocument();
            invalidArrayEntry["platforms"]!.AsArray()[1]!["requiredVerifications"] =
                new JsonArray(invalidEntry?.DeepClone());
            AssertInvalid(invalidArrayEntry);
        }

        var duplicateArrayEntry = ValidPolicyDocument();
        duplicateArrayEntry["platforms"]!.AsArray()[0]!["requiredVerifications"] =
            new JsonArray("same", "same");
        AssertInvalid(duplicateArrayEntry);
    }

    [Fact]
    public void Parser_rejects_each_weakened_platform_contract()
    {
        foreach (var routeIndex in Enumerable.Range(0, 3))
        {
            foreach (var field in new[]
            {
                "artifactShape", "platformSigning", "notarization",
            })
            {
                var document = ValidPolicyDocument();
                document["platforms"]!.AsArray()[routeIndex]![field] = "weakened";
                AssertInvalid(document);
            }

            foreach (var field in new[] { "signableTargets", "requiredVerifications" })
            {
                var document = ValidPolicyDocument();
                document["platforms"]!.AsArray()[routeIndex]![field] =
                    new JsonArray("weakened");
                AssertInvalid(document);
            }
        }

        var missingRoute = ValidPolicyDocument();
        missingRoute["platforms"]!.AsArray().RemoveAt(0);
        AssertInvalid(missingRoute);
    }

    [Fact]
    public void Readiness_rejects_unknown_routes_shape_drift_and_missing_macos_bundle()
    {
        var policy = LoadPolicy();
        Assert.Throws<InvalidDataException>(() => policy.Evaluate(
            Artifact("unknown", "Release", [], []),
            Digest));

        var driftedPolicy = policy with
        {
            Platforms = policy.Platforms
                .Select(route => route.Platform == "windows-x64"
                    ? route with { ArtifactShape = "installer" }
                    : route)
                .ToArray(),
        };
        Assert.Throws<InvalidDataException>(() => driftedPolicy.Evaluate(
            Artifact("windows-x64", "Release", [Entry("VibeSnake.exe")], []),
            Digest));

        Assert.Throws<InvalidDataException>(() => policy.Evaluate(
            Artifact("macos-universal", "Release", [Entry("VibeSnake.zip")], []),
            Digest));
    }

    private static ReleaseSigningPolicy LoadPolicy()
    {
        var result = ReleaseSigningPolicy.LoadFromFile(ResolvePolicyPath());
        return Assert.IsType<ReleaseSigningPolicy>(result.Policy);
    }

    private static JsonObject ValidPolicyDocument() =>
        JsonNode.Parse(File.ReadAllText(ResolvePolicyPath()))!.AsObject();

    private static void AssertInvalid(JsonObject document) =>
        Assert.Equal(
            ReleaseSigningPolicyLoadCode.InvalidField,
            ReleaseSigningPolicy.Parse(document.ToJsonString()).Code);

    private static ReleaseArtifactManifest Artifact(
        string platform,
        string buildMode,
        IReadOnlyList<ReleaseArtifactFileEntry> files,
        IReadOnlyList<ReleaseArtifactFileEntry> containerEntries,
        string sourceRevision = Revision) => new(
            SchemaVersion: ReleaseArtifactManifest.CurrentSchemaVersion,
            Product: ReleaseArtifactManifest.ProductName,
            Platform: platform,
            BuildMode: buildMode,
            SourceRevision: sourceRevision,
            GodotVersion: "4.7.1",
            GodotCommit: "a13da4feb",
            GodotArchiveSha512: new string('a', 128),
            GodotExecutableSha256: new string('b', 64),
            DotnetSdk: "10.0.302",
            SmokeStateHash: "0123456789abcdef",
            FileCount: files.Count,
            TotalBytes: files.Sum(file => file.Bytes),
            Files: files,
            ContainerEntries: containerEntries);

    private static ReleaseArtifactFileEntry Entry(string path) =>
        new(path, 1, new string('c', 64));

    private static string ResolvePolicyPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "config",
                "release_signing_policy.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate config/release_signing_policy.json.");
    }
}
