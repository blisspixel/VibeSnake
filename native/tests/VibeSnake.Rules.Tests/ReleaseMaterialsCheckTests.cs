using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class ReleaseMaterialsCheckTests
{
    private static readonly string[] RequiredDocuments =
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

    private static readonly string[] Platforms =
        ["windows-x64", "macos-universal", "linux-x64"];

    private static readonly string[] Inputs =
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

    private static readonly string[] ClaimIds =
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

    private static readonly string[] SeparateReleaseGates =
    [
        "artifact-manifest-size-reconciliation",
        "marketing-claim-approval",
        "visible-image-review",
        "video-playback-review",
    ];

    [Fact]
    public void Exact_foundation_passes_and_schema_two_handoff_is_canonical_repeatable_and_pending()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var inspection = ReleaseMaterialsCheck.Inspect(root);

            Assert.True(inspection.Passed, string.Join(Environment.NewLine, inspection.Failures));
            Assert.Equal(
                "Release-material foundation qualified; exact candidate materials remain pending.",
                inspection.SuccessMessage);

            var output = Path.Combine(root, "TestResults", "release-materials.json");
            var first = ReleaseMaterialsCheck.WriteFoundationHandoff(root, output);
            var firstBytes = File.ReadAllBytes(output);
            var second = ReleaseMaterialsCheck.WriteFoundationHandoff(root, output);

            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.Equal(firstBytes, File.ReadAllBytes(output));
            var text = File.ReadAllText(output, new UTF8Encoding(false, true));
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
            using var handoff = JsonDocument.Parse(text);
            var value = handoff.RootElement;
            Assert.Equal(2, value.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("release-materials-handoff-v2", value.GetProperty("kind").GetString());
            Assert.True(value.GetProperty("passed").GetBoolean());
            Assert.True(value.GetProperty("foundationQualified").GetBoolean());
            Assert.Equal(10, value.GetProperty("requiredDocumentCount").GetInt32());
            Assert.Equal("1.0.0", value.GetProperty("appVersion").GetString());
            Assert.Equal(JsonValueKind.Null, value.GetProperty("sourceRevision").ValueKind);
            Assert.False(value.GetProperty("candidateMaterialComplete").GetBoolean());
            Assert.Equal(11, value.GetProperty("pendingGates").GetArrayLength());

            var rootOutput = Path.Combine(root, "release-materials.json");
            var rootResult = ReleaseMaterialsCheck.WriteFoundationHandoff(root, rootOutput);
            Assert.True(rootResult.Passed, string.Join(Environment.NewLine, rootResult.Failures));
            Assert.True(File.Exists(rootOutput));
        });
    }

    [Fact]
    public void Exact_candidate_binds_revision_version_candidate_documents_media_claims_and_retained_hashes()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var candidatePath = WriteCandidate(root);
            var output = Path.Combine(root, "decision", "release-materials.json");

            var result = ReleaseMaterialsCheck.WriteCandidateHandoff(
                root,
                candidatePath,
                new string('a', 40),
                output);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            Assert.Equal(
                "Exact-candidate release-material structure qualified; separate release gates remain pending.",
                result.SuccessMessage);
            using var handoff = JsonDocument.Parse(File.ReadAllBytes(output));
            var value = handoff.RootElement;
            Assert.True(value.GetProperty("candidateSupplied").GetBoolean());
            Assert.True(value.GetProperty("candidateMaterialComplete").GetBoolean());
            Assert.False(value.GetProperty("releaseAcceptance").GetBoolean());
            Assert.Equal(new string('a', 40), value.GetProperty("sourceRevision").GetString());
            Assert.Equal(Sha256(File.ReadAllBytes(candidatePath)), value.GetProperty("candidateSha256").GetString());
            Assert.Equal(
                SeparateReleaseGates,
                value.GetProperty("pendingGates")
                    .EnumerateArray()
                    .Select(item => item.GetString()!)
                    .ToArray());
        });
    }

    [Fact]
    public void Contract_rejects_duplicate_invalid_utf8_wrong_shape_and_oversize_inputs()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var contract = Path.Combine(root, "config", "release_materials_v1.json");
            var original = File.ReadAllText(contract, Encoding.UTF8);

            File.WriteAllText(
                contract,
                original.Replace("{", "{\n  \"schemaVersion\": 1,", StringComparison.Ordinal),
                new UTF8Encoding(false));
            AssertFailure(root, "repeats JSON field");

            File.WriteAllBytes(contract, [0xff, 0xfe, 0xfd]);
            AssertFailure(root, "UTF-8");

            File.WriteAllText(contract, "{}\n", new UTF8Encoding(false));
            AssertFailure(root, "contract fields must be");

            File.WriteAllBytes(contract, new byte[(1024 * 1024) + 1]);
            AssertFailure(root, "exceeds the 1048576-byte");
        });
    }

    [Fact]
    public void Foundation_rejects_missing_small_invalid_utf8_linked_documents_and_bad_version()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            File.Delete(Path.Combine(root, "SUPPORT.md"));
            AssertFailure(root, "SUPPORT.md");

            WriteFoundation(root);
            File.WriteAllText(Path.Combine(root, "README.md"), "small", new UTF8Encoding(false));
            AssertFailure(root, "unexpectedly small");

            WriteFoundation(root);
            File.WriteAllBytes(Path.Combine(root, "PRIVACY.md"), [0xff, 0xfe, 0xfd, .. new byte[200]]);
            AssertFailure(root, "PRIVACY.md");

            WriteFoundation(root);
            File.WriteAllText(Path.Combine(root, "VERSION"), "01.0.0\n", new UTF8Encoding(false));
            AssertFailure(root, "canonical");

            WriteFoundation(root);
            var actual = Path.Combine(root, "outside.md");
            File.WriteAllText(actual, DocumentText("outside"), new UTF8Encoding(false));
            var linked = Path.Combine(root, "SUPPORT.md");
            File.Delete(linked);
            if (TryCreateFileLink(linked, actual))
            {
                AssertFailure(root, "link");
            }
        });
    }

    [Fact]
    public void Candidate_rejects_pending_markers_revision_version_types_maps_and_claim_coverage()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var candidatePath = WriteCandidate(root);
            File.AppendAllText(Path.Combine(root, "README.md"), "Store-ready 1.0 is not ready\n");
            var pending = WriteCandidateResult(root, candidatePath, new string('a', 40));
            Assert.Contains(pending.Failures, value => value.Contains("pending marker", StringComparison.Ordinal));

            WriteFoundation(root);
            var candidate = ReadObject(candidatePath);
            candidate["sourceRevision"] = new string('b', 40);
            candidate["appVersion"] = "1.0.1";
            candidate["coreContentBytes"] = true;
            candidate["artifactManifestSha256ByPlatform"]!["windows-x64"] = 1;
            candidate["marketingClaims"]!.AsArray().RemoveAt(0);
            WriteObject(candidatePath, candidate);
            var invalid = WriteCandidateResult(root, candidatePath, new string('a', 40));

            Assert.False(invalid.Passed);
            Assert.Contains(invalid.Failures, value => value.Contains("sourceRevision", StringComparison.Ordinal));
            Assert.Contains(invalid.Failures, value => value.Contains("appVersion", StringComparison.Ordinal));
            Assert.Contains(invalid.Failures, value => value.Contains("coreContentBytes", StringComparison.Ordinal));
            Assert.Contains(
                invalid.Failures,
                value => value.Contains("artifactManifestSha256ByPlatform.windows-x64", StringComparison.Ordinal));
            Assert.Contains(invalid.Failures, value => value.Contains("cover every permitted claim", StringComparison.Ordinal));

            var malformedExpected = WriteCandidateResult(root, candidatePath, "BAD");
            Assert.Contains(malformedExpected.Failures, value => value.Contains("expected revision", StringComparison.Ordinal));
            Assert.Contains(malformedExpected.Failures, value => value.Contains("coreContentBytes", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Candidate_rejects_unsafe_missing_empty_case_colliding_linked_and_tampered_retained_files()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var candidatePath = WriteCandidate(root);
            var candidate = ReadObject(candidatePath);
            candidate["inputEvidencePathsByDevice"]!["keyboard"] = new JsonArray("../escape.json");
            WriteObject(candidatePath, candidate);
            AssertCandidateFailure(root, candidatePath, "safe relative POSIX path");

            candidatePath = WriteCandidate(root);
            candidate = ReadObject(candidatePath);
            candidate["inputEvidencePathsByDevice"]!["keyboard"] = new JsonArray("evidence/MISSING.json");
            WriteObject(candidatePath, candidate);
            AssertCandidateFailure(root, candidatePath, "missing candidate retained file");

            candidatePath = WriteCandidate(root);
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(candidatePath)!, "evidence", "input.json"), []);
            AssertCandidateFailure(root, candidatePath, "must be nonempty");

            candidatePath = WriteCandidate(root);
            var retainedRoot = Path.GetDirectoryName(candidatePath)!;
            var target = Path.Combine(retainedRoot, "outside.json");
            File.WriteAllText(target, "linked evidence");
            var input = Path.Combine(retainedRoot, "evidence", "input.json");
            File.Delete(input);
            if (TryCreateFileLink(input, target))
            {
                AssertCandidateFailure(root, candidatePath, "link");
            }

            candidatePath = WriteCandidate(root);
            File.AppendAllText(
                Path.Combine(Path.GetDirectoryName(candidatePath)!, "evidence", "claim.json"),
                "tampered");
            AssertCandidateFailure(root, candidatePath, "hash mismatch");
        });
    }

    [Fact]
    public void Candidate_rejects_truncated_extension_mismatched_and_incomplete_media()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var candidatePath = WriteCandidate(root);
            var retainedRoot = Path.GetDirectoryName(candidatePath)!;
            File.WriteAllBytes(Path.Combine(retainedRoot, "media", "screenshot.png"), [0x89, 0x50, 0x4e, 0x47]);
            AssertCandidateFailure(root, candidatePath, "recognized retained image");

            candidatePath = WriteCandidate(root);
            retainedRoot = Path.GetDirectoryName(candidatePath)!;
            File.WriteAllBytes(Path.Combine(retainedRoot, "media", "video.mp4"), Encoding.ASCII.GetBytes("not an mp4"));
            AssertCandidateFailure(root, candidatePath, "recognized retained video");

            candidatePath = WriteCandidate(root);
            var candidate = ReadObject(candidatePath);
            candidate["screenshotPathsByRole"]!["main-menu"] = new JsonArray("media/video.mp4");
            WriteObject(candidatePath, candidate);
            AssertCandidateFailure(root, candidatePath, "screenshots must use PNG or JPEG");
        });
    }

    [Fact]
    public void Structurally_complete_jpeg_and_webm_are_accepted_and_truncation_is_rejected()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var candidatePath = WriteCandidate(root, "media/screenshot.jpg", "media/video.webm");
            var retainedRoot = Path.GetDirectoryName(candidatePath)!;
            File.WriteAllBytes(Path.Combine(retainedRoot, "media", "screenshot.jpg"), ValidJpeg());
            File.WriteAllBytes(Path.Combine(retainedRoot, "media", "video.webm"), ValidWebm());
            RefreshRetainedHashes(candidatePath);

            var accepted = WriteCandidateResult(root, candidatePath, new string('a', 40));
            Assert.True(accepted.Passed, string.Join(Environment.NewLine, accepted.Failures));

            var webm = Path.Combine(retainedRoot, "media", "video.webm");
            File.WriteAllBytes(webm, File.ReadAllBytes(webm)[..^1]);
            AssertCandidateFailure(root, candidatePath, "recognized retained video");
        });
    }

    [Fact]
    public void Failed_qualification_still_writes_bounded_failure_evidence_with_candidate_digest()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var candidatePath = WriteCandidate(root);
            var duplicate = File.ReadAllText(candidatePath, Encoding.UTF8).Replace(
                "{",
                "{\n  \"schemaVersion\": 1,",
                StringComparison.Ordinal);
            File.WriteAllText(candidatePath, duplicate, new UTF8Encoding(false));
            var output = Path.Combine(root, "decision", "failure.json");

            var result = ReleaseMaterialsCheck.WriteCandidateHandoff(
                root,
                candidatePath,
                new string('a', 40),
                output);

            Assert.False(result.Passed);
            Assert.True(File.Exists(output));
            using var handoff = JsonDocument.Parse(File.ReadAllBytes(output));
            Assert.False(handoff.RootElement.GetProperty("passed").GetBoolean());
            Assert.Equal(
                Sha256(File.ReadAllBytes(candidatePath)),
                handoff.RootElement.GetProperty("candidateSha256").GetString());
            Assert.Contains(
                handoff.RootElement.GetProperty("errors").EnumerateArray(),
                item => item.GetString()!.Contains("repeats JSON field", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Writer_contains_output_rejects_input_aliases_and_retains_missing_contract_failure_evidence()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var contract = Path.Combine(root, "config", "release_materials_v1.json");
            File.Delete(contract);
            var failureOutput = Path.Combine(root, "decision", "missing-contract.json");

            var missing = ReleaseMaterialsCheck.WriteFoundationHandoff(root, failureOutput);

            Assert.False(missing.Passed);
            Assert.True(File.Exists(failureOutput));
            using (var handoff = JsonDocument.Parse(File.ReadAllBytes(failureOutput)))
            {
                Assert.False(handoff.RootElement.GetProperty("foundationQualified").GetBoolean());
                Assert.Equal(JsonValueKind.Null, handoff.RootElement.GetProperty("contractSha256").ValueKind);
            }

            WriteFoundation(root);
            var escaped = Path.Combine(Path.GetDirectoryName(root)!, $"escape-{Guid.NewGuid():N}.json");
            var escapeResult = ReleaseMaterialsCheck.WriteFoundationHandoff(root, escaped);
            Assert.False(escapeResult.Passed);
            Assert.Contains(escapeResult.Failures, value => value.Contains("inside", StringComparison.Ordinal));
            Assert.False(File.Exists(escaped));

            var readme = Path.Combine(root, "README.md");
            var originalReadme = File.ReadAllBytes(readme);
            var alias = ReleaseMaterialsCheck.WriteFoundationHandoff(root, readme);
            Assert.False(alias.Passed);
            Assert.Contains(alias.Failures, value => value.Contains("alias", StringComparison.Ordinal));
            Assert.Equal(originalReadme, File.ReadAllBytes(readme));

            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                var caseAlias = ReleaseMaterialsCheck.WriteFoundationHandoff(
                    root,
                    Path.Combine(root, "readme.md"));
                Assert.False(caseAlias.Passed);
                Assert.Contains(
                    caseAlias.Failures,
                    value => value.Contains("alias", StringComparison.Ordinal));
                Assert.Equal(originalReadme, File.ReadAllBytes(readme));
            }

            var candidatePath = WriteCandidate(root);
            var retainedInput = Path.Combine(
                Path.GetDirectoryName(candidatePath)!,
                "evidence",
                "input.json");
            var retainedInputBytes = File.ReadAllBytes(retainedInput);
            var retainedAlias = ReleaseMaterialsCheck.WriteCandidateHandoff(
                root,
                candidatePath,
                new string('a', 40),
                retainedInput);
            Assert.False(retainedAlias.Passed);
            Assert.Contains(
                retainedAlias.Failures,
                value => value.Contains("alias", StringComparison.Ordinal));
            Assert.Equal(retainedInputBytes, File.ReadAllBytes(retainedInput));

            candidatePath = WriteCandidate(root);
            var composedCandidate = Path.Combine(
                Path.GetDirectoryName(candidatePath)!,
                "candidate-\u00e9.json");
            File.Move(candidatePath, composedCandidate);
            var normalizedAlias = ReleaseMaterialsCheck.WriteCandidateHandoff(
                root,
                composedCandidate,
                new string('a', 40),
                Path.Combine(Path.GetDirectoryName(candidatePath)!, "candidate-e\u0301.json"));
            Assert.False(normalizedAlias.Passed);
            Assert.Contains(
                normalizedAlias.Failures,
                value => value.Contains("alias", StringComparison.Ordinal));
            Assert.True(File.Exists(composedCandidate));

            var directoryOutput = Path.Combine(root, "existing-output");
            Directory.CreateDirectory(directoryOutput);
            var directoryResult = ReleaseMaterialsCheck.WriteFoundationHandoff(root, directoryOutput);
            Assert.False(directoryResult.Passed);
            Assert.Contains(
                directoryResult.Failures,
                value => value.Contains("regular file", StringComparison.Ordinal));

            var target = Path.Combine(root, "actual-output");
            Directory.CreateDirectory(target);
            var linked = Path.Combine(root, "linked-output");
            if (TryCreateDirectoryLink(linked, target))
            {
                var linkedResult = ReleaseMaterialsCheck.WriteFoundationHandoff(
                    root,
                    Path.Combine(linked, "handoff.json"));
                Assert.False(linkedResult.Passed);
                Assert.Contains(linkedResult.Failures, value => value.Contains("link", StringComparison.Ordinal));
                Assert.False(File.Exists(Path.Combine(target, "handoff.json")));
            }
        });
    }

    [Fact]
    public void Oversized_claim_collection_produces_bounded_diagnostics_and_evidence()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var candidatePath = WriteCandidate(root);
            var candidate = ReadObject(candidatePath);
            candidate["marketingClaims"] = new JsonArray(
                Enumerable.Range(0, 10_000).Select(_ => (JsonNode?)false).ToArray());
            WriteObject(candidatePath, candidate);
            var output = Path.Combine(root, "decision", "bounded-failure.json");

            var result = ReleaseMaterialsCheck.WriteCandidateHandoff(
                root,
                candidatePath,
                new string('a', 40),
                output);

            Assert.False(result.Passed);
            Assert.InRange(result.Failures.Count, 1, 128);
            Assert.Contains(
                result.Failures,
                value => value.Contains("cannot contain more than", StringComparison.Ordinal));
            Assert.InRange(new FileInfo(output).Length, 1, 256 * 1024);
            using var evidence = JsonDocument.Parse(File.ReadAllBytes(output));
            Assert.InRange(evidence.RootElement.GetProperty("errors").GetArrayLength(), 1, 128);
        });
    }

    [Fact]
    public void Candidate_closed_schema_rejects_collection_text_path_claim_and_hash_variants()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            AssertCandidateMutation(root, value => value.Remove("kind"), "candidate fields must be");
            AssertCandidateMutation(root, value => value["schemaVersion"] = "1", "schemaVersion");
            AssertCandidateMutation(root, value => value["kind"] = "other", "candidate.kind");
            AssertCandidateMutation(
                root,
                value => value["sourceRevision"] = "BAD",
                "lowercase 40-character revision");
            AssertCandidateMutation(
                root,
                value => value["artifactManifestSha256ByPlatform"]!.AsObject().Remove("linux-x64"),
                "artifactManifestSha256ByPlatform fields");
            AssertCandidateMutation(
                root,
                value => value["artifactManifestSha256ByPlatform"]!["windows-x64"] = new string('A', 64),
                "lowercase SHA-256 digest");
            AssertCandidateMutation(
                root,
                value => value["downloadBytesByPlatform"]!["windows-x64"] = 0,
                "positive integer");
            AssertCandidateMutation(
                root,
                value => value["installedBytesByPlatform"]!["windows-x64"] = 1.5,
                "positive integer");
            AssertCandidateMutation(
                root,
                value => value["supportedOperatingSystemsByPlatform"]!["windows-x64"] = new JsonArray(),
                "1 through 16");
            AssertCandidateMutation(
                root,
                value => value["supportedOperatingSystemsByPlatform"]!["windows-x64"] =
                    new JsonArray("same", "same"),
                "repeats value");
            AssertCandidateMutation(
                root,
                value => value["supportedOperatingSystemsByPlatform"]!["windows-x64"] =
                    new JsonArray(1),
                "nonempty string");
            AssertCandidateMutation(
                root,
                value => value["inputDeviceIds"] = new JsonArray(Inputs.Reverse().Select(item => (JsonNode?)item).ToArray()),
                "inputDeviceIds");
            AssertCandidateMutation(root, value => value["offlineBehavior"] = "online", "offlineBehavior");
            AssertCandidateMutation(
                root,
                value => value["saveLocationsByPlatform"]!["windows-x64"] = " ",
                "nonempty string");
            AssertCandidateMutation(
                root,
                value => value["saveLocationsByPlatform"]!["windows-x64"] = new string('x', 1025),
                "up to 1024 characters");
            AssertCandidateMutation(root, value => value["optionalContentBytes"] = -1, "nonnegative integer");
            AssertCandidateMutation(
                root,
                value => value["documentationSha256"]!["README.md"] = new string('f', 64),
                "documentation hash mismatch");
            AssertCandidateMutation(
                root,
                value => value["documentationSha256"]!["README.md"] = new string('F', 64),
                "lowercase SHA-256 digest");
            AssertCandidateMutation(
                root,
                value => value["screenshotPathsByRole"]!.AsObject().Remove("main-menu"),
                "screenshotPathsByRole fields");
            AssertCandidateMutation(root, value => value["videoPathsByRole"] = new JsonArray(), "must be an object");
            AssertCandidateMutation(
                root,
                value => value["inputEvidencePathsByDevice"]!["keyboard"] = new JsonArray(),
                "1 through 16");
            AssertCandidateMutation(
                root,
                value => value["inputEvidencePathsByDevice"]!["keyboard"] = new JsonArray(1),
                "safe relative path string");
            AssertCandidateMutation(
                root,
                value => value["inputEvidencePathsByDevice"]!["keyboard"] =
                    new JsonArray("evidence/input.json", "evidence/input.json"),
                "repeats a path");
            AssertCandidateMutation(
                root,
                value => value["inputEvidencePathsByDevice"]!["keyboard"] =
                    new JsonArray("evidence/input.json", "EVIDENCE/INPUT.JSON"),
                "portable case variant");
            AssertCandidateMutation(
                root,
                value => value["inputEvidencePathsByDevice"]!["keyboard"] = new JsonArray("evidence/NUL.json"),
                "safe relative POSIX path");
            foreach (var unsafePath in new[]
            {
                string.Empty,
                "/evidence/input.json",
                "evidence/input.json/",
                "evidence\\input.json",
                "C:/evidence/input.json",
                "evidence/control\u0001.json",
                "evidence/less<than.json",
                "evidence/greater>than.json",
                "evidence/quote\"name.json",
                "evidence/pipe|name.json",
                "evidence/question?.json",
                "evidence/star*.json",
                new string('a', 513),
                "evidence/./input.json",
                "evidence/../input.json",
                "evidence/trailing ",
                "evidence/trailing.",
                "evidence/CON.txt",
                "evidence/PRN.txt",
                "evidence/AUX.txt",
                "evidence/CLOCK$.txt",
                "evidence/COM1.txt",
                "evidence/LPT9.txt",
            })
            {
                AssertCandidateMutation(
                    root,
                    value => value["inputEvidencePathsByDevice"]!["keyboard"] = new JsonArray(unsafePath),
                    "safe relative POSIX path");
            }

            AssertCandidateMutation(
                root,
                value => value["inputEvidencePathsByDevice"]!["keyboard"] =
                    new JsonArray("evidence/cafe\u0301.json"),
                "safe relative POSIX path");

            AssertCandidateMutation(
                root,
                value => value["marketingClaims"]![0]!["evidencePaths"] =
                    new JsonArray("EVIDENCE/CLAIM.JSON"),
                "collide by portable case");

            AssertCandidateMutation(
                root,
                value => value["inputEvidencePathsByDevice"]!["keyboard"] =
                    new JsonArray(Enumerable.Range(0, 17).Select(index => (JsonNode?)$"evidence/{index}.json").ToArray()),
                "1 through 16");
            AssertCandidateMutation(root, value => value["marketingClaims"] = "claims", "must be an array");
            AssertCandidateMutation(
                root,
                value => value["marketingClaims"]!.AsArray().Add(value["marketingClaims"]![0]!.DeepClone()),
                "cannot contain more than");
            AssertCandidateMutation(
                root,
                value => value["marketingClaims"]![0]!["extra"] = true,
                "marketingClaims[0] fields");
            AssertCandidateMutation(
                root,
                value => value["marketingClaims"]![0]!["claimId"] = "unsupported",
                "unique and supported");
            AssertCandidateMutation(
                root,
                value => value["marketingClaims"]![0]!["statement"] = " ",
                "statement must be");
            AssertCandidateMutation(
                root,
                value => value["marketingClaims"]![0]!["evidencePaths"] = new JsonArray(),
                "evidencePaths must contain");
            AssertCandidateMutation(
                root,
                value => value["retainedFileSha256"]!.AsObject().Remove("evidence/input.json"),
                "retainedFileSha256 fields");
            AssertCandidateMutation(
                root,
                value => value["retainedFileSha256"]!["extra.json"] = new string('a', 64),
                "retainedFileSha256 fields");
            AssertCandidateMutation(
                root,
                value => value["retainedFileSha256"]!["evidence/input.json"] = 1,
                "evidence/input.json must be a nonempty string");
            AssertCandidateMutation(
                root,
                value => value["retainedFileSha256"]!["evidence/input.json"] = new string('a', 63),
                "lowercase SHA-256 digest");
            AssertCandidateMutation(
                root,
                value => value["retainedFileSha256"]!["evidence/input.json"] = new string('A', 64),
                "lowercase SHA-256 digest");
        });
    }

    [Fact]
    public void Contract_closed_schema_rejects_scalar_and_ordered_array_drift()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            AssertContractMutation(root, value => value["schemaVersion"] = true, "schemaVersion");
            AssertContractMutation(root, value => value["kind"] = "other", "contract.kind");
            AssertContractMutation(root, value => value["status"] = "complete", "contract.status");
            AssertContractMutation(
                root,
                value => value["requiredDocumentPaths"]!.AsArray().RemoveAt(0),
                "requiredDocumentPaths");
            AssertContractMutation(
                root,
                value => value["artifactPlatforms"] = new JsonArray(Platforms.Reverse().Select(item => (JsonNode?)item).ToArray()),
                "artifactPlatforms");
            AssertContractMutation(root, value => value["inputDeviceIds"] = "inputs", "inputDeviceIds");
            AssertContractMutation(
                root,
                value => value["offlineBehaviorValue"] = "online",
                "offlineBehaviorValue");
            AssertContractMutation(
                root,
                value => value["releaseRules"]![0] = "changed",
                "releaseRules");
        });
    }

    [Fact]
    public void Jpeg_parser_rejects_marker_length_dimension_scan_and_termination_corruption()
    {
        WithTemporaryDirectory(root =>
        {
            AssertMediaPasses(root, "valid.jpg", ValidJpeg(), "image");
            AssertMediaPasses(
                root,
                "three-component.jpg",
                ValidJpeg(frameComponentCount: 3),
                "image");
            AssertMediaPasses(
                root,
                "restart.jpg",
                ValidJpeg(includeDri: true, includeRestart: true),
                "image");
            AssertMediaFails(root, "wrong-soi.jpg", [0, 1, 2, 3, 4, 5, 6, 7], "image", "SOI");
            AssertMediaFails(root, "missing-prefix.jpg", [0xff, 0xd8, 0, 1, 2, 3, 4, 5], "image", "marker prefix");
            AssertMediaFails(root, "stuffed.jpg", [0xff, 0xd8, 0xff, 0x00, 0, 0, 0, 0], "image", "stuffed byte");
            AssertMediaFails(root, "standalone.jpg", [0xff, 0xd8, 0xff, 0xd0, 0, 0, 0, 0], "image", "standalone marker");
            AssertMediaFails(root, "bad-length.jpg", [0xff, 0xd8, 0xff, 0xe0, 0, 1, 0, 0], "image", "segment length");
            AssertMediaFails(
                root,
                "short-frame.jpg",
                [0xff, 0xd8, 0xff, 0xc0, 0, 7, 0, 0, 0, 0, 0],
                "image",
                "frame header");

            var dimensions = ValidJpeg();
            var frameIndex = IndexOf(dimensions, [0xff, 0xc0]);
            dimensions[frameIndex + 5] = 0;
            dimensions[frameIndex + 6] = 0;
            AssertMediaFails(root, "dimensions.jpg", dimensions, "image", "dimensions");

            var noQuantization = ValidJpeg();
            var quantizationIndex = IndexOf(noQuantization, [0xff, 0xdb]);
            noQuantization[quantizationIndex + 1] = 0xe0;
            AssertMediaFails(root, "no-dqt.jpg", noQuantization, "image", "quantization");

            var badQuantizationPrecision = ValidJpeg();
            quantizationIndex = IndexOf(badQuantizationPrecision, [0xff, 0xdb]);
            badQuantizationPrecision[quantizationIndex + 4] = 0x20;
            AssertMediaFails(
                root,
                "dqt-precision.jpg",
                badQuantizationPrecision,
                "image",
                "precision");

            var badQuantizationIdentity = ValidJpeg();
            quantizationIndex = IndexOf(badQuantizationIdentity, [0xff, 0xdb]);
            badQuantizationIdentity[quantizationIndex + 4] = 4;
            AssertMediaFails(root, "dqt-id.jpg", badQuantizationIdentity, "image", "quantization table");

            var zeroQuantizationValue = ValidJpeg();
            quantizationIndex = IndexOf(zeroQuantizationValue, [0xff, 0xdb]);
            zeroQuantizationValue[quantizationIndex + 5] = 0;
            AssertMediaFails(
                root,
                "dqt-zero.jpg",
                zeroQuantizationValue,
                "image",
                "must be nonzero");

            var noHuffman = ValidJpeg();
            noHuffman[IndexOf(noHuffman, [0xff, 0xc4]) + 1] = 0xe0;
            AssertMediaFails(root, "no-dht.jpg", noHuffman, "image", "Huffman");

            var badHuffmanIdentity = ValidJpeg();
            var huffmanIndex = IndexOf(badHuffmanIdentity, [0xff, 0xc4]);
            badHuffmanIdentity[huffmanIndex + 4] = 0x20;
            AssertMediaFails(root, "dht-id.jpg", badHuffmanIdentity, "image", "identity");

            var oversubscribedHuffman = ValidJpeg();
            huffmanIndex = IndexOf(oversubscribedHuffman, [0xff, 0xc4]);
            oversubscribedHuffman[huffmanIndex + 5] = 3;
            AssertMediaFails(
                root,
                "dht-oversubscribed.jpg",
                oversubscribedHuffman,
                "image",
                "oversubscribed");

            var zeroSymbolHuffman = ValidJpeg();
            huffmanIndex = IndexOf(zeroSymbolHuffman, [0xff, 0xc4]);
            zeroSymbolHuffman[huffmanIndex + 5] = 0;
            AssertMediaFails(root, "dht-empty.jpg", zeroSymbolHuffman, "image", "symbols");

            var badSelector = ValidJpeg();
            var scanIndex = IndexOf(badSelector, [0xff, 0xda]);
            badSelector[scanIndex + 6] = 0x11;
            AssertMediaFails(root, "selector.jpg", badSelector, "image", "Huffman table");

            var badSampling = ValidJpeg();
            frameIndex = IndexOf(badSampling, [0xff, 0xc0]);
            badSampling[frameIndex + 11] = 0;
            AssertMediaFails(root, "sampling.jpg", badSampling, "image", "components");

            var missingFrameTable = ValidJpeg();
            frameIndex = IndexOf(missingFrameTable, [0xff, 0xc0]);
            missingFrameTable[frameIndex + 12] = 1;
            AssertMediaFails(root, "frame-table.jpg", missingFrameTable, "image", "quantization table");

            var unknownScanComponent = ValidJpeg();
            scanIndex = IndexOf(unknownScanComponent, [0xff, 0xda]);
            unknownScanComponent[scanIndex + 5] = 2;
            AssertMediaFails(root, "scan-component.jpg", unknownScanComponent, "image", "component");

            AssertMediaFails(
                root,
                "partial-scan.jpg",
                ValidJpeg(frameComponentCount: 3, scanComponentCount: 1),
                "image",
                "scan components");

            AssertMediaFails(
                root,
                "restart-without-dri.jpg",
                ValidJpeg(includeRestart: true),
                "image",
                "nonzero DRI");

            var badScanParameters = ValidJpeg();
            scanIndex = IndexOf(badScanParameters, [0xff, 0xda]);
            badScanParameters[scanIndex + 9] = 62;
            AssertMediaFails(root, "scan-parameters.jpg", badScanParameters, "image", "parameters");

            var emptyEntropy = ValidJpeg();
            var eoiIndex = IndexOf(emptyEntropy, [0x3f, 0xff, 0xd9]);
            emptyEntropy[eoiIndex] = 0xff;
            AssertMediaFails(root, "empty-entropy.jpg", emptyEntropy, "image", "entropy-coded");

            AssertMediaFails(
                root,
                "scan-first.jpg",
                [0xff, 0xd8, 0xff, 0xda, 0, 8, 1, 1, 0, 0, 0x3f, 0, 1, 0xff, 0xd9],
                "image",
                "before a frame");
            AssertMediaFails(root, "missing-eoi.jpg", ValidJpeg()[..^2], "image", "EOI");
            AssertMediaFails(root, "trailing.jpg", [.. ValidJpeg(), 0], "image", "trailing bytes");
        });
    }

    [Fact]
    public void Mp4_and_webm_parsers_reject_box_element_identity_size_and_required_structure_corruption()
    {
        WithTemporaryDirectory(root =>
        {
            var oversizedVideo = Path.Combine(root, "oversized.mp4");
            using (var output = new FileStream(oversizedVideo, FileMode.CreateNew, FileAccess.Write))
            {
                output.SetLength((512L * 1024 * 1024) + 1);
            }

            Assert.Contains(
                "video exceeds the 536870912-byte validation limit",
                ReleaseMaterialsCheck.ValidateMediaForRepositoryCheck(oversizedVideo, "video"),
                StringComparison.Ordinal);

            AssertMediaPasses(root, "valid.mp4", ValidMp4(), "video");
            AssertMediaPasses(
                root,
                "variable-sizes.mp4",
                ValidMp4(variableSampleSizes: true),
                "video");
            AssertMediaPasses(
                root,
                "large-offsets.mp4",
                ValidMp4(largeChunkOffsets: true),
                "video");
            AssertMediaPasses(
                root,
                "unused-unknown-description.mp4",
                ValidMp4(includeUnknownSampleDescription: true),
                "video");
            AssertMediaFails(root, "short.mp4", [0, 0, 0, 8], "video", "truncated");

            var undersized = ValidMp4();
            BinaryPrimitives.WriteUInt32BigEndian(undersized, 7);
            AssertMediaFails(root, "undersized.mp4", undersized, "video", "box size");

            var invalidType = ValidMp4();
            invalidType[4] = 0;
            AssertMediaFails(root, "box-type.mp4", invalidType, "video", "box type");

            var unknownBrand = ValidMp4();
            Encoding.ASCII.GetBytes("zzzz").CopyTo(unknownBrand, 8);
            Encoding.ASCII.GetBytes("zzzz").CopyTo(unknownBrand, 16);
            AssertMediaFails(root, "brand.mp4", unknownBrand, "video", "recognized media brand");

            using var misplacedOutput = new MemoryStream();
            WriteBox(misplacedOutput, "free", [0]);
            misplacedOutput.Write(ValidMp4());
            AssertMediaFails(root, "misplaced.mp4", misplacedOutput.ToArray(), "video", "ftyp");

            using var noMediaOutput = new MemoryStream();
            WriteBox(noMediaOutput, "ftyp", [.. Encoding.ASCII.GetBytes("isom"), 0, 0, 0, 0]);
            AssertMediaFails(root, "missing-boxes.mp4", noMediaOutput.ToArray(), "video", "moov");

            var extended = new byte[16];
            BinaryPrimitives.WriteUInt32BigEndian(extended, 1);
            Encoding.ASCII.GetBytes("ftyp").CopyTo(extended, 4);
            BinaryPrimitives.WriteUInt64BigEndian(extended.AsSpan(8), 1000);
            AssertMediaFails(root, "extended.mp4", extended, "video", "box size");

            var zeroFinalBox = ValidMp4();
            var mediaDataIndex = IndexOf(zeroFinalBox, Encoding.ASCII.GetBytes("mdat"));
            BinaryPrimitives.WriteUInt32BigEndian(zeroFinalBox.AsSpan(mediaDataIndex - 4), 0);
            AssertMediaPasses(root, "zero-final.mp4", zeroFinalBox, "video");

            var emptyMediaData = ValidMp4();
            mediaDataIndex = IndexOf(emptyMediaData, Encoding.ASCII.GetBytes("mdat"));
            BinaryPrimitives.WriteUInt32BigEndian(emptyMediaData.AsSpan(mediaDataIndex - 4), 8);
            AssertMediaFails(root, "empty-mdat.mp4", emptyMediaData, "video", "nonempty");

            AssertMediaFails(
                root,
                "duplicate-moov.mp4",
                [.. ValidMp4(), .. Box("moov", [0])],
                "video",
                "multiple moov");

            AssertMediaFails(
                root,
                "duplicate-ftyp.mp4",
                [.. ValidMp4(), .. Box("ftyp", [.. Encoding.ASCII.GetBytes("isom"), .. new byte[4]])],
                "video",
                "ftyp");

            var unsupportedMovieHeader = ValidMp4();
            unsupportedMovieHeader[IndexOf(unsupportedMovieHeader, Encoding.ASCII.GetBytes("mvhd")) + 4] = 1;
            AssertMediaFails(root, "mvhd-version.mp4", unsupportedMovieHeader, "video", "movie header");

            var zeroMovieTiming = ValidMp4();
            var movieHeaderIndex = IndexOf(zeroMovieTiming, Encoding.ASCII.GetBytes("mvhd"));
            BinaryPrimitives.WriteUInt32BigEndian(zeroMovieTiming.AsSpan(movieHeaderIndex + 16), 0);
            AssertMediaFails(root, "mvhd-time.mp4", zeroMovieTiming, "video", "timing");

            var zeroTrackIdentity = ValidMp4();
            var trackHeaderIndex = IndexOf(zeroTrackIdentity, Encoding.ASCII.GetBytes("tkhd"));
            BinaryPrimitives.WriteUInt32BigEndian(zeroTrackIdentity.AsSpan(trackHeaderIndex + 16), 0);
            AssertMediaFails(root, "tkhd-id.mp4", zeroTrackIdentity, "video", "identity");

            var audioHandler = ValidMp4();
            Encoding.ASCII.GetBytes("soun").CopyTo(
                audioHandler,
                IndexOf(audioHandler, Encoding.ASCII.GetBytes("vide")));
            AssertMediaFails(root, "audio.mp4", audioHandler, "video", "video trak");

            var outsideMedia = ValidMp4();
            var chunkOffset = IndexOf(outsideMedia, Encoding.ASCII.GetBytes("stco")) + 12;
            BinaryPrimitives.WriteUInt32BigEndian(outsideMedia.AsSpan(chunkOffset), 1);
            AssertMediaFails(root, "outside-mdat.mp4", outsideMedia, "video", "mdat");

            var zeroWidth = ValidMp4();
            var sampleEntry = IndexOf(zeroWidth, Encoding.ASCII.GetBytes("avc1"));
            zeroWidth[sampleEntry + 28] = 0;
            zeroWidth[sampleEntry + 29] = 0;
            AssertMediaFails(root, "zero-width.mp4", zeroWidth, "video", "sample entry fields");

            var unknownCodec = ValidMp4();
            Encoding.ASCII.GetBytes("zzzz").CopyTo(
                unknownCodec,
                IndexOf(unknownCodec, Encoding.ASCII.GetBytes("avc1")));
            AssertMediaFails(root, "codec.mp4", unknownCodec, "video", "lacks a bounded video");

            var missingConfiguration = ValidMp4();
            var configurationIndex = IndexOf(missingConfiguration, Encoding.ASCII.GetBytes("avcC"));
            missingConfiguration[configurationIndex] = (byte)'x';
            AssertMediaFails(root, "configuration.mp4", missingConfiguration, "video", "lacks a bounded avcC");

            var badSequenceSet = ValidMp4();
            var sequenceIndex = IndexOf(badSequenceSet, [0, 4, 0x67, 0x42, 0, 0x1e]);
            badSequenceSet[sequenceIndex + 2] = 0x68;
            AssertMediaFails(root, "sequence-set.mp4", badSequenceSet, "video", "sequence parameter set identity");

            var invalidConfigurationVersion = ValidMp4();
            configurationIndex = IndexOf(invalidConfigurationVersion, Encoding.ASCII.GetBytes("avcC"));
            invalidConfigurationVersion[configurationIndex + 4] = 0;
            AssertMediaFails(root, "avcc-version.mp4", invalidConfigurationVersion, "video", "header");

            var missingProfile = ValidMp4();
            configurationIndex = IndexOf(missingProfile, Encoding.ASCII.GetBytes("avcC"));
            missingProfile[configurationIndex + 5] = 0;
            AssertMediaFails(root, "avcc-profile.mp4", missingProfile, "video", "header");

            var missingLevel = ValidMp4();
            configurationIndex = IndexOf(missingLevel, Encoding.ASCII.GetBytes("avcC"));
            missingLevel[configurationIndex + 7] = 0;
            AssertMediaFails(root, "avcc-level.mp4", missingLevel, "video", "level");

            var invalidLengthDescriptor = ValidMp4();
            configurationIndex = IndexOf(invalidLengthDescriptor, Encoding.ASCII.GetBytes("avcC"));
            invalidLengthDescriptor[configurationIndex + 8] = 0;
            AssertMediaFails(root, "avcc-length.mp4", invalidLengthDescriptor, "video", "descriptor");

            var invalidLengthSize = ValidMp4();
            configurationIndex = IndexOf(invalidLengthSize, Encoding.ASCII.GetBytes("avcC"));
            invalidLengthSize[configurationIndex + 8] = 0xfe;
            AssertMediaFails(root, "avcc-length-size.mp4", invalidLengthSize, "video", "descriptor");

            var missingSequenceSet = ValidMp4();
            configurationIndex = IndexOf(missingSequenceSet, Encoding.ASCII.GetBytes("avcC"));
            missingSequenceSet[configurationIndex + 9] = 0xe0;
            AssertMediaFails(root, "avcc-sequence.mp4", missingSequenceSet, "video", "descriptor");

            var missingPictureSet = ValidMp4();
            configurationIndex = IndexOf(missingPictureSet, Encoding.ASCII.GetBytes("avcC"));
            missingPictureSet[configurationIndex + 16] = 0;
            AssertMediaFails(root, "avcc-picture.mp4", missingPictureSet, "video", "picture parameter set");

            var zeroTimingDelta = ValidMp4();
            var timeToSampleIndex = IndexOf(zeroTimingDelta, Encoding.ASCII.GetBytes("stts"));
            BinaryPrimitives.WriteUInt32BigEndian(zeroTimingDelta.AsSpan(timeToSampleIndex + 16), 0);
            AssertMediaFails(root, "stts.mp4", zeroTimingDelta, "video", "positive");

            var zeroTimingEntries = ValidMp4();
            timeToSampleIndex = IndexOf(zeroTimingEntries, Encoding.ASCII.GetBytes("stts"));
            BinaryPrimitives.WriteUInt32BigEndian(zeroTimingEntries.AsSpan(timeToSampleIndex + 8), 0);
            AssertMediaFails(root, "stts-count.mp4", zeroTimingEntries, "video", "entry count");

            var zeroFirstChunk = ValidMp4();
            var sampleToChunkIndex = IndexOf(zeroFirstChunk, Encoding.ASCII.GetBytes("stsc"));
            BinaryPrimitives.WriteUInt32BigEndian(zeroFirstChunk.AsSpan(sampleToChunkIndex + 12), 0);
            AssertMediaFails(root, "stsc.mp4", zeroFirstChunk, "video", "stsc entries");

            var zeroSampleSize = ValidMp4(variableSampleSizes: true);
            var sampleSizeIndex = IndexOf(zeroSampleSize, Encoding.ASCII.GetBytes("stsz"));
            BinaryPrimitives.WriteUInt32BigEndian(zeroSampleSize.AsSpan(sampleSizeIndex + 16), 0);
            AssertMediaFails(root, "stsz.mp4", zeroSampleSize, "video", "sample size");

            var oversizedSample = ValidMp4();
            sampleSizeIndex = IndexOf(oversizedSample, Encoding.ASCII.GetBytes("stsz"));
            BinaryPrimitives.WriteUInt32BigEndian(oversizedSample.AsSpan(sampleSizeIndex + 8), 2);
            AssertMediaFails(root, "sample-bytes.mp4", oversizedSample, "video", "mdat");

            var inconsistentVariableSizes = ValidMp4();
            sampleSizeIndex = IndexOf(inconsistentVariableSizes, Encoding.ASCII.GetBytes("stsz"));
            BinaryPrimitives.WriteUInt32BigEndian(inconsistentVariableSizes.AsSpan(sampleSizeIndex + 8), 0);
            AssertMediaFails(
                root,
                "stsz-length.mp4",
                inconsistentVariableSizes,
                "video",
                "length is inconsistent");

            var zeroChunkCount = ValidMp4();
            var chunkTableIndex = IndexOf(zeroChunkCount, Encoding.ASCII.GetBytes("stco"));
            BinaryPrimitives.WriteUInt32BigEndian(zeroChunkCount.AsSpan(chunkTableIndex + 8), 0);
            AssertMediaFails(root, "stco-count.mp4", zeroChunkCount, "video", "entry count");

            var mismatchedChunkLayout = ValidMp4();
            sampleToChunkIndex = IndexOf(mismatchedChunkLayout, Encoding.ASCII.GetBytes("stsc"));
            BinaryPrimitives.WriteUInt32BigEndian(mismatchedChunkLayout.AsSpan(sampleToChunkIndex + 16), 2);
            AssertMediaFails(root, "chunk-layout.mp4", mismatchedChunkLayout, "video", "inconsistent");

            var unknownDescription = ValidMp4();
            sampleToChunkIndex = IndexOf(unknownDescription, Encoding.ASCII.GetBytes("stsc"));
            BinaryPrimitives.WriteUInt32BigEndian(unknownDescription.AsSpan(sampleToChunkIndex + 20), 2);
            AssertMediaFails(
                root,
                "description-index.mp4",
                unknownDescription,
                "video",
                "recognized visual sample descriptions");

            AssertMediaFails(
                root,
                "used-unknown-description.mp4",
                ValidMp4(
                    includeUnknownSampleDescription: true,
                    useUnknownSampleDescription: true),
                "video",
                "recognized visual sample descriptions");

            var splitChunk = ValidMp4();
            sampleSizeIndex = IndexOf(splitChunk, Encoding.ASCII.GetBytes("stsz"));
            BinaryPrimitives.WriteUInt32BigEndian(splitChunk.AsSpan(sampleSizeIndex + 8), 2);
            AssertMediaFails(
                root,
                "split-chunk.mp4",
                [.. splitChunk, .. Box("mdat", [0])],
                "video",
                "one mdat");

            AssertMediaPasses(root, "valid.webm", ValidWebm(), "video");
            AssertMediaPasses(
                root,
                "single-duration.webm",
                ValidWebm(singlePrecisionDuration: true),
                "video");
            AssertMediaPasses(
                root,
                "unique-audio-video.webm",
                ValidWebmWithTracks(
                    WebmTrackEntry(2, 2, 2, supportedVideo: false),
                    WebmTrackEntry(1, 1, 1, supportedVideo: true)),
                "video");
            AssertMediaFails(
                root,
                "duplicate-track-number.webm",
                ValidWebmWithTracks(
                    WebmTrackEntry(1, 2, 2, supportedVideo: false),
                    WebmTrackEntry(1, 1, 1, supportedVideo: true)),
                "video",
                "globally unique");
            AssertMediaFails(
                root,
                "duplicate-track-uid.webm",
                ValidWebmWithTracks(
                    WebmTrackEntry(2, 1, 2, supportedVideo: false),
                    WebmTrackEntry(1, 1, 1, supportedVideo: true)),
                "video",
                "globally unique");
            AssertMediaFails(
                root,
                "zero-audio-track-uid.webm",
                ValidWebmWithTracks(
                    WebmTrackEntry(2, 0, 2, supportedVideo: false),
                    WebmTrackEntry(1, 1, 1, supportedVideo: true)),
                "video",
                "positive TrackNumber");
            var wrongHeader = ValidWebm();
            wrongHeader[0] = 0x19;
            AssertMediaFails(root, "wrong-header.webm", wrongHeader, "video", "header");

            var zeroHeader = ValidWebm();
            zeroHeader[4] = 0x80;
            AssertMediaFails(root, "zero-header.webm", zeroHeader, "video", "header");

            var wrongDocType = ValidWebm();
            var docTypeIndex = IndexOf(wrongDocType, Encoding.ASCII.GetBytes("webm"));
            wrongDocType[docTypeIndex] = (byte)'x';
            AssertMediaFails(root, "doctype.webm", wrongDocType, "video", "header fields");

            var unsupportedIdLength = ValidWebm();
            var idLengthIndex = IndexOf(unsupportedIdLength, [0x42, 0xf2, 0x81, 4]);
            unsupportedIdLength[idLengthIndex + 3] = 5;
            AssertMediaFails(root, "id-length.webm", unsupportedIdLength, "video", "header fields");

            var wrongSegment = ValidWebm();
            var segmentIndex = IndexOf(wrongSegment, [0x18, 0x53, 0x80, 0x67]);
            wrongSegment[segmentIndex] = 0x19;
            AssertMediaFails(root, "segment.webm", wrongSegment, "video", "Segment");

            AssertMediaFails(root, "truncated.webm", ValidWebm()[..^1], "video", "exceeds its container");
            AssertMediaFails(root, "unknown-size.webm", [0x1a, 0x45, 0xdf, 0xa3, 0xff], "video", "unknown");

            var missingCodec = ValidWebm();
            missingCodec[IndexOf(missingCodec, Encoding.ASCII.GetBytes("V_VP8"))] = (byte)'X';
            AssertMediaFails(root, "codec.webm", missingCodec, "video", "codec");

            var invalidCodecText = ValidWebm();
            invalidCodecText[IndexOf(invalidCodecText, Encoding.ASCII.GetBytes("V_VP8"))] = 1;
            AssertMediaFails(root, "codec-text.webm", invalidCodecText, "video", "text element");

            var zeroTimecodeScale = ValidWebm();
            var scaleIndex = IndexOf(zeroTimecodeScale, [0x2a, 0xd7, 0xb1, 0x83]);
            zeroTimecodeScale.AsSpan(scaleIndex + 4, 3).Clear();
            AssertMediaFails(root, "timecode-scale.webm", zeroTimecodeScale, "video", "Info");

            var zeroDuration = ValidWebm();
            var durationIndex = IndexOf(zeroDuration, [0x44, 0x89, 0x88]);
            zeroDuration.AsSpan(durationIndex + 3, 8).Clear();
            AssertMediaFails(root, "duration.webm", zeroDuration, "video", "Info");

            var audioOnly = ValidWebm();
            var trackTypeIndex = IndexOf(audioOnly, [0x83, 0x81, 1]);
            audioOnly[trackTypeIndex + 2] = 2;
            AssertMediaFails(root, "audio-only.webm", audioOnly, "video", "video TrackEntry");

            var zeroTrackNumber = ValidWebm();
            var trackNumberIndex = IndexOf(zeroTrackNumber, [0xd7, 0x81, 1]);
            zeroTrackNumber[trackNumberIndex + 2] = 0;
            AssertMediaFails(
                root,
                "track-number.webm",
                zeroTrackNumber,
                "video",
                "positive TrackNumber");

            var emptyTrackNumber = ValidWebm();
            trackNumberIndex = IndexOf(emptyTrackNumber, [0xd7, 0x81, 1]);
            emptyTrackNumber[trackNumberIndex + 1] = 0x80;
            AssertMediaFails(root, "empty-track-number.webm", emptyTrackNumber, "video", "unsigned element");

            var zeroPixelWidth = ValidWebm();
            var widthIndex = IndexOf(zeroPixelWidth, [0xb0, 0x81, 1]);
            zeroPixelWidth[widthIndex + 2] = 0;
            AssertMediaFails(root, "width.webm", zeroPixelWidth, "video", "dimensions");

            var nonKeyframe = ValidWebm();
            var blockIndex = IndexOf(nonKeyframe, [0xa3, 0x85, 0x81, 0, 0, 0x80, 0]);
            nonKeyframe[blockIndex + 5] = 0;
            AssertMediaFails(root, "non-keyframe.webm", nonKeyframe, "video", "keyframe");

            var lacedKeyframe = ValidWebm();
            blockIndex = IndexOf(lacedKeyframe, [0xa3, 0x85, 0x81, 0, 0, 0x80, 0]);
            lacedKeyframe[blockIndex + 5] = 0x82;
            AssertMediaFails(root, "laced-keyframe.webm", lacedKeyframe, "video", "keyframe");

            var wrongBlockTrack = ValidWebm();
            blockIndex = IndexOf(wrongBlockTrack, [0xa3, 0x85, 0x81, 0, 0, 0x80, 0]);
            wrongBlockTrack[blockIndex + 2] = 0x82;
            AssertMediaFails(root, "block-track.webm", wrongBlockTrack, "video", "track");

            var shortBlock = ValidWebm();
            blockIndex = IndexOf(shortBlock, [0xa3, 0x85, 0x81, 0, 0, 0x80, 0]);
            shortBlock[blockIndex + 1] = 0x84;
            AssertMediaFails(root, "short-block.webm", shortBlock, "video", "truncated");

            var missingClusterTimecode = ValidWebm();
            var clusterTimecodeIndex = IndexOf(missingClusterTimecode, [0xe7, 0x81, 0]);
            missingClusterTimecode[clusterTimecodeIndex] = 0xe6;
            AssertMediaFails(root, "cluster-timecode.webm", missingClusterTimecode, "video", "Cluster");

            var noCluster = EbmlElement(
                [0x18, 0x53, 0x80, 0x67],
                [
                    .. ValidWebmInfo(),
                    .. ValidWebmTracks(),
                ]);
            var webmHeader = ValidWebmHeader();
            AssertMediaFails(root, "no-cluster.webm", [.. webmHeader, .. noCluster], "video", "Cluster");
        });
    }

    private static void WriteFoundation(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.Copy(
            Path.Combine(ResolveRepositoryRoot(), "config", "release_materials_v1.json"),
            Path.Combine(root, "config", "release_materials_v1.json"),
            overwrite: true);
        File.WriteAllText(Path.Combine(root, "VERSION"), "1.0.0\n", new UTF8Encoding(false));
        foreach (var relativePath in RequiredDocuments)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DocumentText(relativePath), new UTF8Encoding(false));
        }
    }

    private static string WriteCandidate(
        string root,
        string screenshotPath = "media/screenshot.png",
        string videoPath = "media/video.mp4")
    {
        var candidatePath = Path.Combine(root, "retained", "candidate.json");
        var retainedRoot = Path.GetDirectoryName(candidatePath)!;
        Directory.CreateDirectory(Path.Combine(retainedRoot, "evidence"));
        Directory.CreateDirectory(Path.Combine(retainedRoot, "media"));
        File.WriteAllText(Path.Combine(retainedRoot, "evidence", "input.json"), "retained input evidence");
        File.WriteAllText(Path.Combine(retainedRoot, "evidence", "claim.json"), "retained claim evidence");
        if (screenshotPath.EndsWith(".png", StringComparison.Ordinal))
        {
            File.Copy(
                Path.Combine(ResolveRepositoryRoot(), "assets", "images", "logo.png"),
                Path.Combine(retainedRoot, screenshotPath.Replace('/', Path.DirectorySeparatorChar)),
                overwrite: true);
        }
        else if (screenshotPath.EndsWith(".jpg", StringComparison.Ordinal))
        {
            File.WriteAllBytes(
                Path.Combine(retainedRoot, screenshotPath.Replace('/', Path.DirectorySeparatorChar)),
                ValidJpeg());
        }

        if (videoPath.EndsWith(".mp4", StringComparison.Ordinal))
        {
            File.WriteAllBytes(
                Path.Combine(retainedRoot, videoPath.Replace('/', Path.DirectorySeparatorChar)),
                ValidMp4());
        }
        else if (videoPath.EndsWith(".webm", StringComparison.Ordinal))
        {
            File.WriteAllBytes(
                Path.Combine(retainedRoot, videoPath.Replace('/', Path.DirectorySeparatorChar)),
                ValidWebm());
        }

        var documentation = new JsonObject();
        foreach (var relativePath in RequiredDocuments)
        {
            documentation[relativePath] = Sha256(
                File.ReadAllBytes(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

        var inputPaths = new JsonObject();
        foreach (var input in Inputs)
        {
            inputPaths[input] = new JsonArray("evidence/input.json");
        }

        var screenshots = new JsonObject();
        foreach (var role in ScreenshotRoles)
        {
            screenshots[role] = new JsonArray(screenshotPath);
        }

        var videos = new JsonObject();
        foreach (var role in VideoRoles)
        {
            videos[role] = new JsonArray(videoPath);
        }

        var claims = new JsonArray();
        foreach (var claimId in ClaimIds)
        {
            claims.Add(
                new JsonObject
                {
                    ["claimId"] = claimId,
                    ["statement"] = $"Verified candidate claim for {claimId}.",
                    ["evidencePaths"] = new JsonArray("evidence/claim.json"),
                });
        }

        var candidate = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-release-materials-candidate-v1",
            ["sourceRevision"] = new string('a', 40),
            ["appVersion"] = "1.0.0",
            ["artifactManifestSha256ByPlatform"] = PlatformObject(
                platform => new string((char)('1' + Array.IndexOf(Platforms, platform)), 64)),
            ["downloadBytesByPlatform"] = PlatformObject(platform => 100_000_000 + Array.IndexOf(Platforms, platform)),
            ["installedBytesByPlatform"] = PlatformObject(platform => 200_000_000 + Array.IndexOf(Platforms, platform)),
            ["supportedOperatingSystemsByPlatform"] = PlatformObject(
                platform => new JsonArray($"Qualified {platform} operating system")),
            ["inputDeviceIds"] = new JsonArray(Inputs.Select(value => (JsonNode?)value).ToArray()),
            ["inputEvidencePathsByDevice"] = inputPaths,
            ["offlineBehavior"] = "core-play-requires-no-account-or-network",
            ["saveLocationsByPlatform"] = PlatformObject(platform => $"Qualified save location for {platform}"),
            ["coreContentBytes"] = 50_000_000,
            ["optionalContentBytes"] = 300_000_000,
            ["documentationSha256"] = documentation,
            ["screenshotPathsByRole"] = screenshots,
            ["videoPathsByRole"] = videos,
            ["retainedFileSha256"] = new JsonObject(),
            ["marketingClaims"] = claims,
        };
        WriteObject(candidatePath, candidate);
        RefreshRetainedHashes(candidatePath);
        return candidatePath;
    }

    private static void RefreshRetainedHashes(string candidatePath)
    {
        var candidate = ReadObject(candidatePath);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapName in new[]
        {
            "inputEvidencePathsByDevice",
            "screenshotPathsByRole",
            "videoPathsByRole",
        })
        {
            foreach (var property in candidate[mapName]!.AsObject())
            {
                foreach (var item in property.Value!.AsArray())
                {
                    paths.Add(item!.GetValue<string>());
                }
            }
        }

        foreach (var claim in candidate["marketingClaims"]!.AsArray())
        {
            foreach (var item in claim!["evidencePaths"]!.AsArray())
            {
                paths.Add(item!.GetValue<string>());
            }
        }

        var hashes = new JsonObject();
        foreach (var relativePath in paths.Order(StringComparer.Ordinal))
        {
            hashes[relativePath] = Sha256(
                File.ReadAllBytes(
                    Path.Combine(
                        Path.GetDirectoryName(candidatePath)!,
                        relativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

        candidate["retainedFileSha256"] = hashes;
        WriteObject(candidatePath, candidate);
    }

    private static JsonObject PlatformObject(Func<string, JsonNode?> value)
    {
        var result = new JsonObject();
        foreach (var platform in Platforms)
        {
            result[platform] = value(platform);
        }

        return result;
    }

    private static RepositoryCheckResult WriteCandidateResult(
        string root,
        string candidatePath,
        string revision) =>
        ReleaseMaterialsCheck.WriteCandidateHandoff(
            root,
            candidatePath,
            revision,
            Path.Combine(root, "decision", $"result-{Guid.NewGuid():N}.json"));

    private static void AssertCandidateFailure(string root, string candidatePath, string expected)
    {
        var result = WriteCandidateResult(root, candidatePath, new string('a', 40));
        Assert.False(result.Passed);
        Assert.Contains(result.Failures, value => value.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertCandidateMutation(
        string root,
        Action<JsonObject> mutation,
        string expected)
    {
        var candidatePath = WriteCandidate(root);
        var candidate = ReadObject(candidatePath);
        mutation(candidate);
        WriteObject(candidatePath, candidate);
        AssertCandidateFailure(root, candidatePath, expected);
    }

    private static void AssertContractMutation(
        string root,
        Action<JsonObject> mutation,
        string expected)
    {
        WriteFoundation(root);
        var path = Path.Combine(root, "config", "release_materials_v1.json");
        var contract = ReadObject(path);
        mutation(contract);
        WriteObject(path, contract);
        AssertFailure(root, expected);
    }

    private static void AssertFailure(string root, string expected)
    {
        var result = ReleaseMaterialsCheck.Inspect(root);
        Assert.False(result.Passed);
        Assert.Contains(result.Failures, value => value.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertMediaPasses(
        string root,
        string fileName,
        byte[] bytes,
        string mediaKind)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllBytes(path, bytes);
        Assert.Null(ReleaseMaterialsCheck.ValidateMediaForRepositoryCheck(path, mediaKind));
    }

    private static void AssertMediaFails(
        string root,
        string fileName,
        byte[] bytes,
        string mediaKind,
        string expected)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllBytes(path, bytes);
        var failure = ReleaseMaterialsCheck.ValidateMediaForRepositoryCheck(path, mediaKind);
        Assert.NotNull(failure);
        Assert.Contains(expected, failure, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))!.AsObject();

    private static void WriteObject(string path, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    private static string DocumentText(string identity) =>
        $"# Final release document for {identity}\n\n"
        + string.Concat(
            Enumerable.Repeat(
                "Verified final candidate information, supported behavior, evidence, and player guidance. ",
                5));

    private static byte[] ValidMp4(
        bool variableSampleSizes = false,
        bool largeChunkOffsets = false,
        bool includeUnknownSampleDescription = false,
        bool useUnknownSampleDescription = false)
    {
        var provisional = BuildMp4(
            1,
            variableSampleSizes,
            largeChunkOffsets,
            includeUnknownSampleDescription,
            useUnknownSampleDescription);
        return BuildMp4(
            checked((uint)(provisional.Length - 1)),
            variableSampleSizes,
            largeChunkOffsets,
            includeUnknownSampleDescription,
            useUnknownSampleDescription);
    }

    private static byte[] BuildMp4(
        uint chunkOffset,
        bool variableSampleSizes,
        bool largeChunkOffsets,
        bool includeUnknownSampleDescription,
        bool useUnknownSampleDescription)
    {
        using var output = new MemoryStream();
        WriteBox(
            output,
            "ftyp",
            [.. Encoding.ASCII.GetBytes("isom"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("isom")]);

        var movieHeader = Box(
            "mvhd",
            [.. new byte[12], .. UInt32(1000), .. UInt32(1000)]);
        var trackHeader = Box(
            "tkhd",
            [0, 0, 0, 1, .. new byte[8], .. UInt32(1), .. new byte[4], .. UInt32(1000)]);
        var mediaHeader = Box(
            "mdhd",
            [.. new byte[12], .. UInt32(1000), .. UInt32(1000)]);
        var handler = Box(
            "hdlr",
            [.. new byte[8], .. Encoding.ASCII.GetBytes("vide")]);
        var sampleEntry = Box("avc1", Mp4VisualSampleEntry());
        var unknownSampleEntry = Box("mp4a", new byte[8]);
        var sampleDescriptions = Box(
            "stsd",
            includeUnknownSampleDescription
                ? [.. new byte[4], .. UInt32(2), .. unknownSampleEntry, .. sampleEntry]
                : [.. new byte[4], .. UInt32(1), .. sampleEntry]);
        var timeToSample = Box(
            "stts",
            [.. new byte[4], .. UInt32(1), .. UInt32(1), .. UInt32(1000)]);
        var sampleToChunk = Box(
            "stsc",
            [
                .. new byte[4],
                .. UInt32(1),
                .. UInt32(1),
                .. UInt32(1),
                .. UInt32(useUnknownSampleDescription ? 1U : includeUnknownSampleDescription ? 2U : 1U),
            ]);
        var sampleSizes = Box(
            "stsz",
            variableSampleSizes
                ? [.. new byte[4], .. UInt32(0), .. UInt32(1), .. UInt32(1)]
                : [.. new byte[4], .. UInt32(1), .. UInt32(1)]);
        var chunkOffsets = Box(
            largeChunkOffsets ? "co64" : "stco",
            largeChunkOffsets
                ? [.. new byte[4], .. UInt32(1), .. UInt64(chunkOffset)]
                : [.. new byte[4], .. UInt32(1), .. UInt32(chunkOffset)]);
        var sampleTable = Box(
            "stbl",
            [.. sampleDescriptions, .. timeToSample, .. sampleToChunk, .. sampleSizes, .. chunkOffsets]);
        var mediaInformation = Box("minf", sampleTable);
        var media = Box("mdia", [.. mediaHeader, .. handler, .. mediaInformation]);
        var track = Box("trak", [.. trackHeader, .. media]);
        WriteBox(output, "moov", [.. movieHeader, .. track]);
        WriteBox(output, "mdat", [0]);
        return output.ToArray();
    }

    private static byte[] Box(string type, byte[] payload)
    {
        using var output = new MemoryStream();
        WriteBox(output, type, payload);
        return output.ToArray();
    }

    private static byte[] UInt32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] UInt64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Mp4VisualSampleEntry()
    {
        var value = new byte[78];
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(28), 0x0048_0000);
        BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(32), 0x0048_0000);
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(74), 0x18);
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(76), ushort.MaxValue);
        return
        [
            .. value,
            .. Box(
                "avcC",
                [
                    1, 0x42, 0, 0x1e, 0xff, 0xe1,
                    0, 4, 0x67, 0x42, 0, 0x1e,
                    1, 0, 2, 0x68, 0xce,
                ]),
        ];
    }

    private static void WriteBox(Stream output, string type, byte[] payload)
    {
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)(payload.Length + 8)));
        output.Write(size);
        output.Write(Encoding.ASCII.GetBytes(type));
        output.Write(payload);
    }

    private static byte[] ValidJpeg(
        int frameComponentCount = 1,
        int? scanComponentCount = null,
        bool includeDri = false,
        bool includeRestart = false)
    {
        var scanComponents = scanComponentCount ?? frameComponentCount;
        using var output = new MemoryStream();
        output.Write([0xff, 0xd8]);
        WriteJpegSegment(output, 0xdb, [0, .. Enumerable.Repeat((byte)1, 64)]);
        using (var frame = new MemoryStream())
        {
            frame.Write([8, 0, 1, 0, 1, checked((byte)frameComponentCount)]);
            for (var component = 1; component <= frameComponentCount; component++)
            {
                frame.Write([checked((byte)component), 0x11, 0]);
            }

            WriteJpegSegment(output, 0xc0, frame.ToArray());
        }

        WriteJpegSegment(
            output,
            0xc4,
            [
                0,
                1, .. new byte[15],
                0,
                0x10,
                1, .. new byte[15],
                0,
            ]);
        if (includeDri)
        {
            WriteJpegSegment(output, 0xdd, [0, 1]);
        }

        using (var scan = new MemoryStream())
        {
            scan.WriteByte(checked((byte)scanComponents));
            for (var component = 1; component <= scanComponents; component++)
            {
                scan.Write([checked((byte)component), 0]);
            }

            scan.Write([0, 63, 0]);
            WriteJpegSegment(output, 0xda, scan.ToArray());
        }

        output.WriteByte(0x3f);
        if (includeRestart)
        {
            output.Write([0xff, 0xd0, 0x3f]);
        }

        output.Write([0xff, 0xd9]);
        return output.ToArray();
    }

    private static void WriteJpegSegment(Stream output, byte marker, byte[] payload)
    {
        output.WriteByte(0xff);
        output.WriteByte(marker);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)(payload.Length + 2)));
        output.Write(length);
        output.Write(payload);
    }

    private static byte[] ValidWebm(bool singlePrecisionDuration = false)
        => ValidWebmWithTracks(
            singlePrecisionDuration,
            WebmTrackEntry(1, 1, 1, supportedVideo: true));

    private static byte[] ValidWebmWithTracks(params byte[][] trackEntries) =>
        ValidWebmWithTracks(singlePrecisionDuration: false, trackEntries);

    private static byte[] ValidWebmWithTracks(
        bool singlePrecisionDuration,
        params byte[][] trackEntries)
    {
        var header = ValidWebmHeader();
        var info = ValidWebmInfo(singlePrecisionDuration);
        var tracks = ValidWebmTracks(trackEntries);
        var cluster = ValidWebmCluster();
        var segment = EbmlElement(
            [0x18, 0x53, 0x80, 0x67],
            [.. info, .. tracks, .. cluster]);
        return [.. header, .. segment];
    }

    private static byte[] ValidWebmHeader() =>
        EbmlElement(
            [0x1a, 0x45, 0xdf, 0xa3],
            [
                .. EbmlElement([0x42, 0x86], [1]),
                .. EbmlElement([0x42, 0xf7], [1]),
                .. EbmlElement([0x42, 0xf2], [4]),
                .. EbmlElement([0x42, 0xf3], [8]),
                .. EbmlElement([0x42, 0x82], Encoding.ASCII.GetBytes("webm")),
                .. EbmlElement([0x42, 0x87], [4]),
                .. EbmlElement([0x42, 0x85], [2]),
            ]);

    private static byte[] ValidWebmInfo(bool singlePrecisionDuration = false)
    {
        byte[] duration;
        if (singlePrecisionDuration)
        {
            duration = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(
                duration,
                BitConverter.SingleToInt32Bits(1000));
        }
        else
        {
            duration = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(
                duration,
                BitConverter.DoubleToInt64Bits(1000));
        }
        return EbmlElement(
            [0x15, 0x49, 0xa9, 0x66],
            [
                .. EbmlElement([0x2a, 0xd7, 0xb1], [0x0f, 0x42, 0x40]),
                .. EbmlElement([0x44, 0x89], duration),
            ]);
    }

    private static byte[] ValidWebmTracks() =>
        ValidWebmTracks(WebmTrackEntry(1, 1, 1, supportedVideo: true));

    private static byte[] ValidWebmTracks(params byte[][] trackEntries) =>
        EbmlElement(
            [0x16, 0x54, 0xae, 0x6b],
            trackEntries.SelectMany(entry => entry).ToArray());

    private static byte[] WebmTrackEntry(
        byte trackNumber,
        byte trackUid,
        byte trackType,
        bool supportedVideo)
    {
        var fields = new List<byte>(
            [
                .. EbmlElement([0xd7], [trackNumber]),
                .. EbmlElement([0x73, 0xc5], [trackUid]),
                .. EbmlElement([0x83], [trackType]),
                .. EbmlElement(
                    [0x86],
                    Encoding.ASCII.GetBytes(supportedVideo ? "V_VP8" : "A_OPUS")),
            ]);
        if (supportedVideo)
        {
            fields.AddRange(
                EbmlElement(
                    [0xe0],
                    [
                        .. EbmlElement([0xb0], [1]),
                        .. EbmlElement([0xba], [1]),
                    ]));
        }

        return EbmlElement(
            [0xae],
            fields.ToArray());
    }

    private static byte[] ValidWebmCluster() =>
        EbmlElement(
            [0x1f, 0x43, 0xb6, 0x75],
            [
                .. EbmlElement([0xe7], [0]),
                .. EbmlElement([0xa3], [0x81, 0, 0, 0x80, 0]),
            ]);

    private static byte[] EbmlElement(byte[] id, byte[] payload)
    {
        Assert.InRange(payload.Length, 0, 126);
        return [.. id, (byte)(0x80 | payload.Length), .. payload];
    }

    private static int IndexOf(byte[] source, byte[] value)
    {
        for (var index = 0; index <= source.Length - value.Length; index++)
        {
            if (source.AsSpan(index, value.Length).SequenceEqual(value))
            {
                return index;
            }
        }

        throw new InvalidDataException("Test fixture pattern is missing.");
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-release-material-checks",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VERSION"))
                && Directory.Exists(Path.Combine(directory.FullName, "native")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
