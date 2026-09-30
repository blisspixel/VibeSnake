using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class ProductReviewPreparationCheckTests
{
    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherRevision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Workspace_projects_three_packages_into_four_pending_rows()
    {
        var root = TempDirectory();
        try
        {
            var evidence = Evidence(root, MatrixJson());
            var output = Path.Combine(root, "outside");
            var preparation = Prepare(RepositoryRoot(), evidence, output, MatrixJson());
            var workspace = preparation.OutputDirectory;

            Assert.Equal(Path.Combine(output, Revision), workspace);
            Assert.Equal(Revision, preparation.Revision);
            Assert.Equal(6, preparation.FileCount);
            AssertNoStaging(output);

            var candidateBytes = File.ReadAllBytes(Path.Combine(workspace, "candidate.json"));
            AssertCanonical(candidateBytes);
            var candidate = JsonDocument.Parse(candidateBytes);
            Assert.Equal(Revision, candidate.RootElement.GetProperty("candidateRevision").GetString());
            Assert.Equal(123456, candidate.RootElement.GetProperty("releaseRunId").GetInt64());
            Assert.Equal(
                "https://github.com/blisspixel/VibeSnake/actions/runs/123456",
                candidate.RootElement.GetProperty("releaseRunUrl").GetString());
            Assert.Equal("pending", candidate.RootElement.GetProperty("humanReviewStatus").GetString());
            Assert.False(candidate.RootElement.GetProperty("releaseAcceptance").GetBoolean());
            Assert.False(candidate.RootElement.GetProperty("publicationEligible").GetBoolean());
            Assert.Equal("0.3.0-alpha.1", candidate.RootElement.GetProperty("appVersion").GetString());
            var rows = candidate.RootElement.GetProperty("artifactRows").EnumerateArray().ToArray();
            Assert.Equal(
                "windows-x64,macos-universal-apple-silicon,macos-universal-intel,linux-x64",
                string.Join(',', rows.Select(row => row.GetProperty("platformRowId").GetString())));
            Assert.Equal("arm64", rows[1].GetProperty("architecture").GetString());
            Assert.Equal("x86_64", rows[2].GetProperty("architecture").GetString());
            Assert.Equal(rows[1].GetProperty("sha256").GetString(), rows[2].GetProperty("sha256").GetString());
            Assert.Equal(new string('4', 64), rows[1].GetProperty("sha256").GetString());
            Assert.Equal(200, rows[1].GetProperty("bytes").GetInt64());

            var templates = Directory.GetFiles(Path.Combine(workspace, "templates"), "*.json.template");
            Assert.Equal(4, templates.Length);
            var session = JsonDocument.Parse(File.ReadAllBytes(templates.Single(path => path.EndsWith("windows-x64.session.json.template", StringComparison.Ordinal))));
            Assert.Equal(2, session.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("vibesnake-manual-product-matrix-session-v2", session.RootElement.GetProperty("kind").GetString());
            Assert.Equal(Revision, session.RootElement.GetProperty("candidateRevision").GetString());
            Assert.Equal(new string('2', 64), session.RootElement.GetProperty("artifactSha256").GetString());
            Assert.Equal("product-matrix-REPLACE", session.RootElement.GetProperty("sessionId").GetString());
            var results = session.RootElement.GetProperty("results").EnumerateArray().ToArray();
            Assert.Equal(36, results.Length);
            Assert.All(results, result =>
            {
                Assert.Equal("pending", result.GetProperty("result").GetString());
                Assert.Equal("REPLACE", result.GetProperty("inputDeviceId").GetString());
                Assert.Empty(result.GetProperty("inputCapabilityIds").EnumerateArray());
                Assert.Empty(result.GetProperty("settingsProfileIds").EnumerateArray());
            });
            Assert.Equal("first-launch", results[0].GetProperty("flowId").GetString());
            Assert.Equal("quit", results[^1].GetProperty("flowId").GetString());
            Assert.Empty(Directory.GetFiles(Path.Combine(workspace, "sessions"), "*.json"));
            Assert.Empty(Directory.GetFiles(Path.Combine(workspace, "sessions", "evidence", "linux-x64")));

            var guide = File.ReadAllText(Path.Combine(workspace, "REVIEW.md"));
            Assert.StartsWith(
                "# Exact Candidate Manual Review Workspace\n\nStatus: prepared, physical execution pending.\n",
                guide,
                StringComparison.Ordinal);
            Assert.Contains("physical execution pending.", guide, StringComparison.Ordinal);
            Assert.Contains(
                "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- manual-matrix-record `",
                guide,
                StringComparison.Ordinal);
            Assert.Contains("all 144 platform-flow cells, all 432 complete-device", guide, StringComparison.Ordinal);
            Assert.EndsWith("sign, publish, approve, or modify candidate bytes.\n", guide, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", guide, StringComparison.Ordinal);

            var manifestBytes = File.ReadAllBytes(Path.Combine(workspace, "workspace-manifest.json"));
            AssertCanonical(manifestBytes);
            var manifest = JsonDocument.Parse(manifestBytes);
            Assert.Equal("pending", manifest.RootElement.GetProperty("humanReviewStatus").GetString());
            Assert.False(manifest.RootElement.GetProperty("releaseAcceptance").GetBoolean());
            Assert.False(manifest.RootElement.TryGetProperty("publicationEligible", out _));
            var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
            Assert.Equal(6, files.Length);
            var paths = files.Select(file => file.GetProperty("path").GetString()).ToArray();
            Assert.Equal(string.Join('\n', paths.OrderBy(path => path, StringComparer.Ordinal)), string.Join('\n', paths));
            foreach (var file in files)
            {
                var relative = file.GetProperty("path").GetString()!;
                var full = Path.Combine(workspace, relative.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(new FileInfo(full).Length, file.GetProperty("bytes").GetInt64());
                Assert.Equal(Sha256(full), file.GetProperty("sha256").GetString());
            }

            var repeat = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(RepositoryRoot(), evidence, output, MatrixJson()));
            Assert.Contains("already exists", repeat.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Retained_matrix_matches_parsed_content_and_rejects_drift()
    {
        var root = TempDirectory();
        try
        {
            var original = MatrixJson();
            var evidence = Evidence(root, Reordered(original));
            var output = Path.Combine(root, "outside");
            var preparation = Prepare(RepositoryRoot(), evidence, output, original);
            Assert.Equal(6, preparation.FileCount);

            var drifted = Evidence(Path.Combine(root, "drift"), MatrixJson(windowsBytes: 101));
            var mismatch = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(RepositoryRoot(), drifted, Path.Combine(root, "drift-out"), MatrixJson()));
            Assert.Contains("does not match", mismatch.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "drift-out")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validation_failure_writes_nothing()
    {
        var root = TempDirectory();
        try
        {
            var evidence = Evidence(root, MatrixJson());
            var output = Path.Combine(root, "outside");
            var error = Assert.Throws<ProductReviewPreparationException>(() =>
                ProductReviewPreparationCheck.Prepare(
                    RepositoryRoot(),
                    evidence,
                    Revision,
                    "123456",
                    "blisspixel/VibeSnake",
                    output,
                    (_, _, _) => new ReleaseMatrixCheck.Qualification(false, ["failed gate"], "{}"),
                    ProductReviewPreparationCheck.DefaultMaximumMatrixBytes));
            Assert.Contains("validation failed", error.Message, StringComparison.Ordinal);
            Assert.Contains("failed gate", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Retained_matrix_rejects_duplicates_links_limits_and_non_finite_numbers()
    {
        var root = TempDirectory();
        try
        {
            var duplicate = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(RepositoryRoot(), Evidence(root, DuplicateSchema(MatrixJson())), Path.Combine(root, "out-dup"), MatrixJson()));
            Assert.Contains("duplicate JSON field: schemaVersion", duplicate.Message, StringComparison.Ordinal);

            var missing = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(RepositoryRoot(), Path.Combine(root, "absent"), Path.Combine(root, "out-missing"), MatrixJson()));
            Assert.Contains("missing retained Release matrix", missing.Message, StringComparison.Ordinal);

            var directory = Path.Combine(root, "directory-matrix");
            Directory.CreateDirectory(Path.Combine(directory, "vibesnake-release-matrix", "release_matrix.json"));
            var directoryError = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(RepositoryRoot(), directory, Path.Combine(root, "out-dir"), MatrixJson()));
            Assert.Contains("missing retained Release matrix", directoryError.Message, StringComparison.Ordinal);

            var linkedRoot = Path.Combine(root, "linked");
            var target = Path.Combine(root, "target.json");
            Directory.CreateDirectory(Path.Combine(linkedRoot, "vibesnake-release-matrix"));
            File.WriteAllText(target, MatrixJson());
            var linkPath = Path.Combine(linkedRoot, "vibesnake-release-matrix", "release_matrix.json");
            if (TryCreateFileLink(linkPath, target))
            {
                var linkError = Assert.Throws<ProductReviewPreparationException>(() =>
                    Prepare(RepositoryRoot(), linkedRoot, Path.Combine(root, "out-link"), MatrixJson()));
                Assert.Contains("missing retained Release matrix", linkError.Message, StringComparison.Ordinal);
            }

            var limited = Assert.Throws<ProductReviewPreparationException>(() =>
                ProductReviewPreparationCheck.Prepare(
                    RepositoryRoot(),
                    Evidence(Path.Combine(root, "limited"), MatrixJson()),
                    Revision,
                    "123456",
                    "blisspixel/VibeSnake",
                    Path.Combine(root, "out-limit"),
                    (_, _, _) => new ReleaseMatrixCheck.Qualification(true, [], MatrixJson()),
                    32));
            Assert.Contains("exceeds the 32-byte limit", limited.Message, StringComparison.Ordinal);

            var bomRoot = Path.Combine(root, "bom");
            var bomPath = Path.Combine(bomRoot, "vibesnake-release-matrix", "release_matrix.json");
            Directory.CreateDirectory(Path.GetDirectoryName(bomPath)!);
            var payload = Encoding.UTF8.GetBytes(MatrixJson());
            File.WriteAllBytes(bomPath, [0xEF, 0xBB, 0xBF, .. payload]);
            var bom = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(RepositoryRoot(), bomRoot, Path.Combine(root, "out-bom"), MatrixJson()));
            Assert.Contains("UTF-8 BOM", bom.Message, StringComparison.Ordinal);

            var nonFiniteRoot = Path.Combine(root, "nan");
            var nonFinitePath = Path.Combine(nonFiniteRoot, "vibesnake-release-matrix", "release_matrix.json");
            Directory.CreateDirectory(Path.GetDirectoryName(nonFinitePath)!);
            File.WriteAllText(nonFinitePath, "{\"schemaVersion\": NaN}");
            var nonFinite = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(RepositoryRoot(), nonFiniteRoot, Path.Combine(root, "out-nan"), MatrixJson()));
            Assert.Contains("non-finite JSON number: NaN", nonFinite.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Arguments_output_location_and_contract_fail_closed()
    {
        var root = TempDirectory();
        try
        {
            var evidence = Evidence(root, MatrixJson());
            AssertMessage(RepositoryRoot(), evidence, "gg", "123456", "blisspixel/VibeSnake", Path.Combine(root, "out"), "lowercase 40-character");
            AssertMessage(RepositoryRoot(), evidence, Revision, "0", "blisspixel/VibeSnake", Path.Combine(root, "out"), "positive integer");
            AssertMessage(RepositoryRoot(), evidence, Revision, "01", "blisspixel/VibeSnake", Path.Combine(root, "out"), "positive integer");
            AssertMessage(RepositoryRoot(), evidence, Revision, "123456", "not a repo", Path.Combine(root, "out"), "owner/name");
            AssertMessage(RepositoryRoot(), evidence, Revision, "123456", "a/b/c", Path.Combine(root, "out"), "owner/name");

            WriteContract(root, OneRowContract());
            var inside = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(root, evidence, Path.Combine(root, "notes"), OnePlatformMatrix()));
            Assert.Contains("under ignored TestResults", inside.Message, StringComparison.Ordinal);
            var escaped = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(root, evidence, Path.Combine(root, "TestResults", "..", "notes"), OnePlatformMatrix()));
            Assert.Contains("under ignored TestResults", escaped.Message, StringComparison.Ordinal);

            var allowed = Prepare(
                root,
                Evidence(Path.Combine(root, "one"), OnePlatformMatrix()),
                Path.Combine(root, "TestResults", "manual"),
                OnePlatformMatrix());
            Assert.Equal(3, allowed.FileCount);
            Assert.True(allowed.OutputDirectory.Contains(Path.Combine("TestResults", "manual"), StringComparison.Ordinal));

            File.Delete(Path.Combine(root, "config", "qa_manual_product_matrix_v2.json"));
            var missingContract = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(root, evidence, Path.Combine(root, "TestResults", "elsewhere"), OnePlatformMatrix()));
            Assert.Contains("missing manual product matrix contract", missingContract.Message, StringComparison.Ordinal);

            WriteContract(root, """{"platformRows":[],"requiredFlows":["first-launch"]}""");
            AssertMessage(root, evidence, Revision, "123456", "blisspixel/VibeSnake", Result(root, "empty-rows"), "platform rows are empty", OnePlatformMatrix());
            WriteContract(root, """{"platformRows":[{"id":"windows-x64","artifactPlatform":"windows-x64","architecture":"x86_64"}],"requiredFlows":[]}""");
            AssertMessage(root, evidence, Revision, "123456", "blisspixel/VibeSnake", Result(root, "empty-flows"), "required flows are empty", OnePlatformMatrix());
            WriteContract(
                root,
                """{"platformRows":[{"id":"windows-x64","artifactPlatform":"windows-x64","architecture":"x86_64"},{"id":"windows-x64","artifactPlatform":"linux-x64","architecture":"x86_64"}],"requiredFlows":["first-launch"]}""");
            AssertMessage(root, evidence, Revision, "123456", "blisspixel/VibeSnake", Result(root, "dup-row"), "duplicate platform row: windows-x64", OnePlatformMatrix());
            WriteContract(
                root,
                """{"platformRows":[{"id":"../evil","artifactPlatform":"windows-x64","architecture":"x86_64"}],"requiredFlows":["first-launch"]}""");
            AssertMessage(root, evidence, Revision, "123456", "blisspixel/VibeSnake", Result(root, "traversal"), "platform row is invalid", OnePlatformMatrix());
            WriteContract(root, """{"schemaVersion":1,"schemaVersion":1,"platformRows":[],"requiredFlows":["first-launch"]}""");
            var duplicateContract = Assert.Throws<ProductReviewPreparationException>(() =>
                Prepare(root, evidence, Result(root, "dup-contract"), OnePlatformMatrix()));
            Assert.Contains("duplicate JSON field: schemaVersion", duplicateContract.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Candidate_projection_rejects_incomplete_release_rows()
    {
        var root = TempDirectory();
        try
        {
            WriteContract(root, OneRowContract());
            Reject(root, "pass", "passing qualification record", OnePlatformMatrix(passed: false));
            Reject(root, "mode", "requires a Release matrix", OnePlatformMatrix(buildMode: "Debug"));
            Reject(root, "rev", "does not match the expected revision", OnePlatformMatrix(revision: OtherRevision));
            Reject(root, "sha", "package SHA-256 is invalid", OnePlatformMatrix(packageSha: "ABCD"));
            Reject(root, "name", "download file name is invalid", OnePlatformMatrix(fileName: "nested/package.zip"));
            Reject(root, "size", "package size is invalid", OnePlatformMatrix(packageBytes: 0));
            Reject(root, "fraction", "package size is invalid", OnePlatformMatrix(rawBytes: "1.5"));
            Reject(root, "platforms", "exact manual artifact platforms", MatrixJson());

            var last = OnePlatformMatrix();
            var node = JsonNode.Parse(last)!.AsObject();
            node["platforms"]!.AsArray().Add(JsonNode.Parse(Platform("windows-x64", "7", "8", 50, "later.zip").ToJsonString()));
            var later = node.ToJsonString();
            var workspace = Prepare(root, Evidence(Path.Combine(root, "later"), later), Result(root, "later-out"), later);
            var candidate = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace.OutputDirectory, "candidate.json")));
            Assert.Equal(new string('8', 64), candidate.RootElement.GetProperty("artifactRows")[0].GetProperty("sha256").GetString());
            Assert.Equal(50, candidate.RootElement.GetProperty("artifactRows")[0].GetProperty("bytes").GetInt64());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reparse_output_and_narrow_contract_failures_stop_before_writing()
    {
        var root = TempDirectory();
        try
        {
            var target = Path.Combine(root, "junction-target");
            var link = Path.Combine(root, "junction-output");
            Directory.CreateDirectory(target);
            if (TryCreateDirectoryLink(link, target))
            {
                var linked = Assert.Throws<ProductReviewPreparationException>(() =>
                    ProductReviewPreparationCheck.Prepare(
                        RepositoryRoot(),
                        Evidence(Path.Combine(root, "junction-evidence"), MatrixJson()),
                        Revision,
                        "123456",
                        "blisspixel/VibeSnake",
                        link,
                        (_, _, _) => new ReleaseMatrixCheck.Qualification(true, [], MatrixJson()),
                        ProductReviewPreparationCheck.DefaultMaximumMatrixBytes));
                Assert.Contains("regular directory path", linked.Message, StringComparison.Ordinal);
            }

            var output = Path.Combine(root, "outside");
            var didNotPass = Assert.Throws<ProductReviewPreparationException>(() =>
                ProductReviewPreparationCheck.Prepare(
                    RepositoryRoot(),
                    Path.Combine(root, "unused"),
                    Revision,
                    "123456",
                    "a/b",
                    output,
                    (_, _, _) => new ReleaseMatrixCheck.Qualification(false, [], "{}"),
                    ProductReviewPreparationCheck.DefaultMaximumMatrixBytes));
            Assert.Contains("qualification did not pass", didNotPass.Message, StringComparison.Ordinal);

            var disk = Assert.Throws<ProductReviewPreparationException>(() =>
                ProductReviewPreparationCheck.Prepare(
                    RepositoryRoot(),
                    Path.Combine(root, "unused-io"),
                    Revision,
                    "123456",
                    "a/b",
                    Path.Combine(root, "outside-io"),
                    (_, _, _) => throw new IOException("disk full"),
                    ProductReviewPreparationCheck.DefaultMaximumMatrixBytes));
            Assert.Contains("disk full", disk.Message, StringComparison.Ordinal);

            WriteContract(root, """{"kind":"wrong"}""");
            AssertMessage(root, Evidence(Path.Combine(root, "bad-shape"), OnePlatformMatrix()), Revision, "123456", "a/b", Result(root, "bad-shape"), "are invalid", OnePlatformMatrix());
            WriteContract(root, """{"platformRows":[{"id":"windows-x64","artifactPlatform":"windows-x64","architecture":"x86_64"}],"requiredFlows":["first-launch","first-launch"]}""");
            AssertMessage(root, Evidence(Path.Combine(root, "dup-flow"), OnePlatformMatrix()), Revision, "123456", "a/b", Result(root, "dup-flow"), "duplicate required flow: first-launch", OnePlatformMatrix());
            WriteContract(root, """{"platformRows":[{"id":"windows-x64","artifactPlatform":"windows-x64","architecture":"x86_64"}],"requiredFlows":["bad flow"]}""");
            AssertMessage(root, Evidence(Path.Combine(root, "bad-flow"), OnePlatformMatrix()), Revision, "123456", "a/b", Result(root, "bad-flow"), "required flow is invalid", OnePlatformMatrix());

            WriteContract(root, OneRowContract());
            var manifest = JsonNode.Parse(OnePlatformMatrix())!.AsObject();
            manifest["platforms"]![0]!["artifactManifestSha256"] = "zz";
            Reject(root, "manifest-sha", "manifest SHA-256 is invalid", manifest.ToJsonString());
            manifest = JsonNode.Parse(OnePlatformMatrix())!.AsObject();
            manifest["platforms"] = "nope";
            Reject(root, "platforms-shape", "platforms must be an array", manifest.ToJsonString());
            manifest = JsonNode.Parse(OnePlatformMatrix())!.AsObject();
            manifest["productVersion"] = "";
            Reject(root, "version", "product version is invalid", manifest.ToJsonString());
            manifest = JsonNode.Parse(OnePlatformMatrix())!.AsObject();
            manifest["sourceRevision"] = "abc";
            Reject(root, "bad-revision", "source revision is invalid", manifest.ToJsonString());
            manifest = JsonNode.Parse(OnePlatformMatrix())!.AsObject();
            manifest["platforms"]![0]!["directDownloadFileName"] = "..";
            Reject(root, "dotdot", "download file name is invalid", manifest.ToJsonString());
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            var link = Path.Combine(root, "junction-output");
            if (Directory.Exists(link))
            {
                Directory.Delete(link, recursive: false);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Empty_evidence_uses_real_release_qualification_and_writes_nothing()
    {
        var root = TempDirectory();
        try
        {
            var evidence = Path.Combine(root, "empty-evidence");
            Directory.CreateDirectory(evidence);
            var output = Path.Combine(root, "outside");
            var error = Assert.Throws<ProductReviewPreparationException>(() =>
                ProductReviewPreparationCheck.Prepare(
                    RepositoryRoot(),
                    evidence,
                    Revision,
                    "123456",
                    "blisspixel/VibeSnake",
                    output));
            Assert.Contains("Release matrix validation failed", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
            Assert.DoesNotContain("https://", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Command_prepares_pending_workspace_and_rejects_bad_invocation()
    {
        var root = TempDirectory();
        try
        {
            var evidence = Evidence(root, MatrixJson());
            var output = Path.Combine(root, "outside");
            var standardOutput = new StringWriter();
            var standardError = new StringWriter();
            var code = RepositoryCheckCommand.Run(
                ["product-review-prepare", RepositoryRoot(), evidence, Revision, "123456", "blisspixel/VibeSnake", output],
                standardOutput,
                standardError,
                (_, _, _) => new ReleaseMatrixCheck.Qualification(true, [], MatrixJson()));

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, standardError.ToString());
            Assert.Equal(
                "Manual product review workspace prepared: revision="
                + Revision
                + " files=6 output="
                + Path.Combine(output, Revision)
                + " physical_execution=pending"
                + Environment.NewLine,
                standardOutput.ToString());

            var failedOutput = new StringWriter();
            var failedError = new StringWriter();
            var failed = RepositoryCheckCommand.Run(
                ["product-review-prepare", RepositoryRoot(), evidence, Revision, "123456", "blisspixel/VibeSnake", Path.Combine(root, "second")],
                failedOutput,
                failedError,
                (_, _, _) => new ReleaseMatrixCheck.Qualification(false, ["failed gate"], "{}"));
            Assert.Equal(1, failed);
            Assert.Equal(string.Empty, failedOutput.ToString());
            Assert.Contains(
                "Manual product review preparation failed: Release matrix validation failed: failed gate",
                failedError.ToString(),
                StringComparison.Ordinal);

            var usageError = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(["product-review-prepare"], new StringWriter(), usageError));
            Assert.Contains(
                "RepositoryChecks product-review-prepare <repository-root> <release-evidence-root> <expected-revision> <release-run-id> <owner/name> <output-root>",
                usageError.ToString(),
                StringComparison.Ordinal);

            var invalidError = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["product-review-prepare", "bad\0root", evidence, Revision, "123456", "blisspixel/VibeSnake", output],
                    new StringWriter(),
                    invalidError));
            Assert.Contains("Repository root is invalid.", invalidError.ToString(), StringComparison.Ordinal);

            var blankError = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["product-review-prepare", RepositoryRoot(), " ", Revision, "123456", "blisspixel/VibeSnake", output],
                    new StringWriter(),
                    blankError));
            Assert.Contains("evidence root or output is invalid.", blankError.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProductReviewPreparationCheck.Preparation Prepare(
        string repository,
        string evidence,
        string output,
        string matrix,
        bool passed = true) =>
        ProductReviewPreparationCheck.Prepare(
            repository,
            evidence,
            Revision,
            "123456",
            "blisspixel/VibeSnake",
            output,
            (_, revision, mode) =>
            {
                Assert.Equal(Revision, revision);
                Assert.Equal("Release", mode);
                return new ReleaseMatrixCheck.Qualification(passed, [], matrix);
            },
            ProductReviewPreparationCheck.DefaultMaximumMatrixBytes);

    private static void AssertMessage(
        string repository,
        string evidence,
        string revision,
        string runId,
        string owner,
        string output,
        string fragment,
        string? matrix = null,
        bool passed = true)
    {
        var error = Assert.Throws<ProductReviewPreparationException>(() =>
            ProductReviewPreparationCheck.Prepare(
                repository,
                evidence,
                revision,
                runId,
                owner,
                output,
                (_, _, _) => new ReleaseMatrixCheck.Qualification(passed, [], matrix ?? MatrixJson()),
                ProductReviewPreparationCheck.DefaultMaximumMatrixBytes));
        Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(output, revision)));
    }

    private static string Evidence(string root, string json)
    {
        var path = Path.Combine(root, "vibesnake-release-matrix", "release_matrix.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return root;
    }

    private static void WriteContract(string root, string json)
    {
        var path = Path.Combine(root, "config", "qa_manual_product_matrix_v2.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static string OneRowContract() =>
        """
        {"platformRows":[{"id":"windows-x64","artifactPlatform":"windows-x64","architecture":"x86_64"}],"requiredFlows":["first-launch"]}
        """;

    private static string MatrixJson(string? revision = null, int windowsBytes = 100) =>
        new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "release-matrix-qualification-v1",
            ["passed"] = true,
            ["sourceRevision"] = revision ?? Revision,
            ["buildMode"] = "Release",
            ["productVersion"] = "0.3.0-alpha.1",
            ["platforms"] = new JsonArray
            {
                Platform("windows-x64", "1", "2", windowsBytes, "VibeSnake-windows.zip"),
                Platform("macos-universal", "3", "4", 200, "VibeSnake-macos.zip"),
                Platform("linux-x64", "5", "6", 300, "VibeSnake-linux.tar.gz"),
            },
        }.ToJsonString();

    private static string OnePlatformMatrix(
        bool passed = true,
        string? revision = null,
        string buildMode = "Release",
        string? packageSha = null,
        string? fileName = null,
        int? packageBytes = null,
        string? rawBytes = null)
    {
        var row = Platform("windows-x64", "1", packageSha ?? new string('2', 64), packageBytes ?? 100, fileName ?? "VibeSnake-windows.zip");
        if (packageSha == "ABCD")
        {
            row["packageSha256"] = "ABCD";
        }

        if (rawBytes is not null)
        {
            row["packageBytes"] = JsonNode.Parse(rawBytes);
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "release-matrix-qualification-v1",
            ["passed"] = passed,
            ["sourceRevision"] = revision ?? Revision,
            ["buildMode"] = buildMode,
            ["productVersion"] = "0.3.0-alpha.1",
            ["platforms"] = new JsonArray(row),
        }.ToJsonString();
    }

    private static JsonObject Platform(string platform, string manifestNibble, string packageNibble, int bytes, string fileName) =>
        new()
        {
            ["platform"] = platform,
            ["artifactManifestSha256"] = new string(manifestNibble[0], 64),
            ["packageSha256"] = packageNibble.Length == 64 ? packageNibble : new string(packageNibble[0], 64),
            ["packageBytes"] = bytes,
            ["directDownloadFileName"] = fileName,
        };

    private static string DuplicateSchema(string json)
    {
        var token = "\"schemaVersion\":1";
        var index = json.IndexOf(token, StringComparison.Ordinal);
        Assert.True(index >= 0);
        return json.Insert(index + token.Length, "," + token);
    }

    private static string Reordered(string json)
    {
        var source = JsonNode.Parse(json)!.AsObject();
        var reversed = new JsonObject();
        foreach (var property in source.Reverse())
        {
            reversed[property.Key] = property.Value?.DeepClone();
        }

        return reversed.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AssertCanonical(byte[] bytes)
    {
        Assert.NotEmpty(bytes);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    private static void AssertNoStaging(string output)
    {
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(output),
            path => Path.GetFileName(path).Contains(".staging.", StringComparison.Ordinal));
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Reject(string root, string name, string fragment, string matrix) =>
        AssertMessage(
            root,
            Evidence(Path.Combine(root, name + "-evidence"), matrix),
            Revision,
            "123456",
            "a/b",
            Result(root, name),
            fragment,
            matrix);

    private static string Result(string root, string name) =>
        Path.Combine(root, "TestResults", name);

    private static bool TryCreateDirectoryLink(string linkPath, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c mklink /J \"" + linkPath + "\" \"" + target + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process!.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }

        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryCreateFileLink(string linkPath, string target)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "vibesnake-product-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "config", "qa_manual_product_matrix_v2.json"))
                && File.Exists(Path.Combine(directory.FullName, "native", "VibeSnake.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not resolve repository root.");
    }
}
