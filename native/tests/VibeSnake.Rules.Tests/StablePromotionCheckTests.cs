using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class StablePromotionCheckTests
{
    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly string[] Platforms =
        ["windows-x64", "macos-universal", "linux-x64"];

    private static readonly string[] DecisionIds =
    [
        "release-matrix", "manual-product-matrix", "external-validation", "release-materials",
        "release-rehearsal", "content-approval", "hardware-performance",
        "accessibility-human-review", "human-playtest", "platform-signing",
    ];

    private static readonly string[] PreservedCategories =
    [
        "build-logs", "manifests", "sbom", "checksums", "migration-fixtures",
        "previous-artifacts", "support-record",
    ];

    private static readonly string[] Acknowledgements =
    [
        "patch-releases-preserve-scored-rules-unless-a-disclosed-correctness-or-exploit-fix-requires-change",
        "save-migrations-remain-nondestructive-and-tested",
        "existing-score-categories-retain-rules-identity",
        "removed-content-remains-visible-as-missing-or-incompatible",
        "accessibility-support-is-regression-tested",
        "offline-core-play-requires-no-account-or-network",
    ];

    private static readonly Dictionary<string, string[]> Gates =
        new(StringComparer.Ordinal)
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
                "artifact-manifest-size-reconciliation", "marketing-claim-approval",
                "visible-image-review", "video-playback-review",
            ],
            ["content-approval"] =
            [
                "core-content-approval", "optional-pack-content-approval",
                "rights-credits-and-notices-reconciliation", "listening-review",
            ],
            ["hardware-performance"] =
            [
                "named-minimum-hardware-performance", "named-recommended-hardware-performance",
                "resolution-presentation-review", "long-session-resource-review",
            ],
            ["accessibility-human-review"] =
            [
                "physical-input-accessibility-review", "accessibility-user-review",
                "photosensitivity-review", "maximum-text-scale-review",
            ],
            ["human-playtest"] =
            [
                "formative-participant-review", "targeted-follow-up-review",
                "fresh-validation-review", "experience-target-range-acceptance",
            ],
            ["platform-signing"] =
            [
                "windows-signing-verification", "macos-signing-notarization-stapling-verification",
                "linux-runtime-baseline-and-desktop-integration", "provenance-verification",
            ],
        };

    private static readonly Dictionary<string, string> Kinds =
        new(StringComparer.Ordinal)
        {
            ["release-matrix"] = "release-matrix-acceptance-v1",
            ["manual-product-matrix"] = "manual-product-matrix-acceptance-v1",
            ["external-validation"] = "external-validation-acceptance-v1",
            ["release-materials"] = "release-materials-acceptance-v1",
            ["release-rehearsal"] = "release-rehearsal-handoff-v2",
            ["content-approval"] = "content-approval-acceptance-v1",
            ["hardware-performance"] = "hardware-performance-acceptance-v1",
            ["accessibility-human-review"] = "accessibility-human-review-acceptance-v1",
            ["human-playtest"] = "human-playtest-acceptance-v1",
            ["platform-signing"] = "platform-signing-acceptance-v1",
        };

    [Fact]
    public void Exact_foundation_is_canonical_repeatable_and_pending()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);

            var inspect = StablePromotionCheck.Inspect(root);
            var output = Path.Combine(root, "TestResults", "stable.json");
            var first = StablePromotionCheck.WriteFoundationHandoff(root, output);
            var bytes = File.ReadAllBytes(output);
            var second = StablePromotionCheck.WriteFoundationHandoff(root, output);

            Assert.True(inspect.Passed, string.Join(Environment.NewLine, inspect.Failures));
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.Equal(bytes, File.ReadAllBytes(output));
            var text = File.ReadAllText(output, new UTF8Encoding(false, true));
            Assert.EndsWith("\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
            using var handoff = JsonDocument.Parse(text);
            var value = handoff.RootElement;
            Assert.Equal(2, value.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("stable-promotion-handoff-v2", value.GetProperty("kind").GetString());
            Assert.True(value.GetProperty("passed").GetBoolean());
            Assert.True(value.GetProperty("guardQualified").GetBoolean());
            Assert.False(value.GetProperty("recordSupplied").GetBoolean());
            Assert.False(value.GetProperty("promotionComplete").GetBoolean());
            Assert.False(value.GetProperty("releaseAcceptance").GetBoolean());
            Assert.Equal(6, value.GetProperty("pendingGates").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, value.GetProperty("sourceRevision").ValueKind);
            Assert.Empty(value.GetProperty("artifactSha256ByPlatform").EnumerateObject());
        });
    }

    [Fact]
    public void Exact_protected_record_cross_binds_unsigned_and_signed_cohorts()
    {
        WithFixture(fixture =>
        {
            var output = Path.Combine(fixture.RecordRoot, "decision.json");
            var result = StablePromotionCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                output);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            Assert.Equal(
                "Stable 1.0 promotion accepted for the exact protected-workflow record.",
                result.SuccessMessage);
            using var handoff = JsonDocument.Parse(File.ReadAllBytes(output));
            var value = handoff.RootElement;
            Assert.True(value.GetProperty("recordIntegrityQualified").GetBoolean());
            Assert.True(value.GetProperty("protectedWorkflowAttested").GetBoolean());
            Assert.True(value.GetProperty("promotionComplete").GetBoolean());
            Assert.True(value.GetProperty("releaseAcceptance").GetBoolean());
            Assert.Equal(Revision, value.GetProperty("sourceRevision").GetString());
            Assert.Equal("1234567890", value.GetProperty("protectedWorkflowRunId").GetString());
            Assert.Equal(
                fixture.FinalArtifacts["windows-x64"],
                value.GetProperty("artifactSha256ByPlatform").GetProperty("windows-x64").GetString());
            Assert.Empty(value.GetProperty("pendingGates").EnumerateArray());
            Assert.Empty(value.GetProperty("errors").EnumerateArray());
        });
    }

    [Fact]
    public void Generic_favorable_json_and_reused_decision_paths_are_rejected()
    {
        WithFixture(fixture =>
        {
            fixture.WriteDecision(
                "manual-product-matrix",
                new JsonObject
                {
                    ["passed"] = true,
                    ["releaseAcceptance"] = true,
                    ["sourceRevision"] = Revision,
                });
            fixture.RefreshRecordHashes();
            AssertFailure(fixture, "fields must be");
        });
        WithFixture(fixture =>
        {
            var upstream = fixture.Record["upstreamDecisionPathsById"]!.AsObject();
            upstream["manual-product-matrix"] = upstream["release-matrix"]!.GetValue<string>();
            fixture.WriteRecord();
            AssertFailure(fixture, "cannot alias");
        });
    }

    [Fact]
    public void Generic_kind_gate_revision_and_completion_are_exact()
    {
        WithFixture(fixture =>
        {
            fixture.MutateDecision("human-playtest", decision => decision["kind"] = "generic-decision-v1");
            AssertFailure(fixture, "upstream decision human-playtest.kind");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision(
                "hardware-performance",
                decision => decision["sourceRevision"] = new string('b', 40));
            AssertFailure(fixture, "sourceRevision");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision("external-validation", decision => decision["releaseAcceptance"] = false);
            AssertFailure(fixture, "releaseAcceptance must be true");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision("manual-product-matrix", decision =>
            {
                decision["gateRecords"]![0]!["gateId"] = "wrong-gate";
            });
            AssertFailure(fixture, "gateId");
        });
    }

    [Fact]
    public void Nine_decisions_share_unsigned_identity_and_signing_bridges_to_public_bytes()
    {
        WithFixture(fixture =>
        {
            fixture.MutateDecision("accessibility-human-review", decision =>
            {
                decision["candidateArtifactSha256ByPlatform"]!["windows-x64"] = new string('b', 64);
            });
            AssertFailure(fixture, "unsigned artifact identity");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision("platform-signing", decision =>
            {
                decision["inputManifestSha256ByPlatform"]!["linux-x64"] = new string('c', 64);
            });
            AssertFailure(fixture, "input manifest identity");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision("platform-signing", decision =>
            {
                decision["candidateArtifactSha256ByPlatform"]!["macos-universal"] = new string('d', 64);
            });
            AssertFailure(fixture, "final artifact identity");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision("platform-signing", decision =>
            {
                decision["provenanceSha256ByPlatform"]!["windows-x64"] = new string('e', 64);
            });
            AssertFailure(fixture, "provenance identity");
        });
    }

    [Fact]
    public void Material_acceptance_is_not_structural_and_is_bound_to_unsigned_manifests()
    {
        WithFixture(fixture =>
        {
            fixture.MutateDecision("release-materials", decision =>
            {
                decision["kind"] = "release-materials-handoff-v2";
            });
            AssertFailure(fixture, "release-materials.kind");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision("release-materials", decision =>
            {
                decision["artifactManifestSha256ByPlatform"]!["windows-x64"] = new string('f', 64);
            });
            AssertFailure(fixture, "manifest identity");
        });
        WithFixture(fixture =>
        {
            var structural = fixture.ReadJson("decisions/release-materials/structural.json");
            structural["releaseAcceptance"] = true;
            fixture.WriteJson("decisions/release-materials/structural.json", structural);
            fixture.RefreshDecisionHashes("release-materials");
            AssertFailure(fixture, "structural handoff.releaseAcceptance");
        });
    }

    [Fact]
    public void Rehearsal_is_complete_and_binds_material_decision_and_unsigned_candidate()
    {
        WithFixture(fixture =>
        {
            fixture.MutateDecision("release-rehearsal", decision => decision["rehearsalComplete"] = false);
            AssertFailure(fixture, "rehearsalComplete must be true");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision(
                "release-rehearsal",
                decision => decision["releaseMaterialsDecisionSha256"] = new string('1', 64));
            AssertFailure(fixture, "releaseMaterialsDecisionSha256");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision("release-rehearsal", decision =>
            {
                decision["candidateArtifactSha256ByPlatform"]!["linux-x64"] = new string('2', 64);
            });
            AssertFailure(fixture, "artifact identity");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision("release-rehearsal", decision => decision["previousVersion"] = "1.0.0");
            AssertFailure(fixture, "earlier than 1.0.0");
        });
    }

    [Fact]
    public void Content_approval_binds_exact_optional_pack_and_manifest()
    {
        WithFixture(fixture =>
        {
            fixture.MutateDecision("content-approval", decision => decision["optionalPackSha256"] = new string('3', 64));
            AssertFailure(fixture, "optionalPackSha256");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision(
                "content-approval",
                decision => decision["optionalPackManifestSha256"] = new string('4', 64));
            AssertFailure(fixture, "optionalPackManifestSha256");
        });
    }

    [Fact]
    public void Artifacts_manifests_provenance_and_checksums_are_exact()
    {
        WithFixture(fixture =>
        {
            File.AppendAllText(
                Path.Combine(fixture.RecordRoot, "public/windows-x64/VibeSnake-1.0.0-windows-x64.bin"),
                "tamper",
                Encoding.UTF8);
            AssertFailure(fixture, "artifact windows-x64 hash mismatch");
        });
        WithFixture(fixture =>
        {
            var manifest = fixture.ReadJson("public/linux-x64/artifact-manifest.json");
            manifest["agentArenaPreviewExcluded"] = false;
            fixture.WriteJson("public/linux-x64/artifact-manifest.json", manifest);
            fixture.RefreshRecordHashes();
            AssertFailure(fixture, "agentArenaPreviewExcluded must be true");
        });
        WithFixture(fixture =>
        {
            File.WriteAllText(
                Path.Combine(fixture.RecordRoot, "public/macos-universal/SHA256SUMS"),
                $"{fixture.FinalArtifacts["macos-universal"]} copied-name\n",
                new UTF8Encoding(false));
            fixture.RefreshRecordHashes();
            AssertFailure(fixture, "malformed row");
        });
    }

    [Fact]
    public void Public_install_rows_and_preserved_contract_are_exact()
    {
        WithFixture(fixture =>
        {
            fixture.Record["publicInstallResults"]![0]!["platformId"] = "linux-x64";
            fixture.WriteRecord();
            AssertFailure(fixture, "publicInstallResults[0].platformId");
        });
        WithFixture(fixture =>
        {
            fixture.Record["publicInstallResults"]![1]!["smokeStateHash"] = "1111111111111111";
            fixture.WriteRecord();
            AssertFailure(fixture, "smokeStateHash");
        });
        WithFixture(fixture =>
        {
            fixture.Record["stableContractAcknowledgements"]!.AsArray().RemoveAt(0);
            fixture.WriteRecord();
            AssertFailure(fixture, "stableContractAcknowledgements");
        });
        WithFixture(fixture =>
        {
            fixture.Record["preservedEvidencePathsByCategory"]!.AsObject().Remove("sbom");
            fixture.WriteRecord();
            AssertFailure(fixture, "preserved evidence fields");
        });
    }

    [Fact]
    public void Retained_hash_closure_rejects_missing_extra_and_tampered_files()
    {
        WithFixture(fixture =>
        {
            fixture.Record["retainedFileSha256"]!.AsObject().Remove("preserved/build-logs.txt");
            fixture.WriteRecord();
            AssertFailure(fixture, "retainedFileSha256 fields");
        });
        WithFixture(fixture =>
        {
            fixture.Record["retainedFileSha256"]!["not-referenced.txt"] = new string('a', 64);
            fixture.WriteRecord();
            AssertFailure(fixture, "retainedFileSha256 fields");
        });
        WithFixture(fixture =>
        {
            File.AppendAllText(
                Path.Combine(fixture.RecordRoot, "preserved/support-record.txt"),
                "tampered",
                Encoding.UTF8);
            AssertFailure(fixture, "retained file hash mismatch");
        });
    }

    [Fact]
    public void Unsafe_case_colliding_and_nested_reused_paths_fail_closed()
    {
        WithFixture(fixture =>
        {
            fixture.Record["optionalPackPath"] = "../outside.pack";
            fixture.WriteRecord();
            AssertFailure(fixture, "safe portable relative path");
        });
        WithFixture(fixture =>
        {
            var artifacts = fixture.Record["artifactPathsByPlatform"]!.AsObject();
            artifacts["linux-x64"] = artifacts["windows-x64"]!.GetValue<string>();
            fixture.WriteRecord();
            AssertFailure(fixture, "cannot alias");
        });
        WithFixture(fixture =>
        {
            var decision = fixture.ReadDecision("human-playtest");
            var rows = decision["gateRecords"]!.AsArray();
            rows[1]!["evidencePaths"]![0] = rows[0]!["evidencePaths"]![0]!.GetValue<string>();
            fixture.WriteDecision("human-playtest", decision);
            fixture.RefreshRecordHashes();
            AssertFailure(fixture, "cannot alias");
        });
    }

    [Fact]
    public void Strict_json_rejects_duplicate_invalid_utf8_and_authority_drift()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var contract = Path.Combine(root, "config", "stable_promotion_v1.json");
            var text = File.ReadAllText(contract, Encoding.UTF8);
            File.WriteAllText(contract, text.Replace("{", "{\n  \"schemaVersion\": 1,", StringComparison.Ordinal), new UTF8Encoding(false));
            AssertFailure(root, "repeats JSON field");

            File.WriteAllBytes(contract, [0xff, 0xfe]);
            AssertFailure(root, "valid UTF-8");
        });
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            var path = Path.Combine(root, "config", "stable_upstream_acceptance_v1.json");
            var authority = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
            authority["decisionKindsById"]!["content-approval"]!["artifactBinding"] = "post-signing-public";
            WriteJson(path, authority);
            AssertFailure(root, "artifactBinding");
        });
    }

    [Fact]
    public void Record_requires_exact_revision_and_output_is_contained_and_non_aliasing()
    {
        WithFixture(fixture =>
        {
            var missing = StablePromotionCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                "ABC",
                Path.Combine(fixture.RecordRoot, "failure.json"));
            Assert.False(missing.Passed);
            Assert.Contains(missing.Failures, item => item.Contains("expected revision", StringComparison.Ordinal));

            var outside = StablePromotionCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                Path.Combine(fixture.RepositoryRoot, "outside.json"));
            Assert.False(outside.Passed);
            Assert.Contains(outside.Failures, item => item.Contains("trusted root", StringComparison.Ordinal));

            var alias = StablePromotionCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                fixture.RecordPath);
            Assert.False(alias.Passed);
            Assert.Contains(alias.Failures, item => item.Contains("cannot alias", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Invalid_record_writes_bounded_failure_evidence_without_trusting_identity()
    {
        WithFixture(fixture =>
        {
            fixture.Record["tagName"] = "v1.0.0";
            fixture.WriteRecord();
            var output = Path.Combine(fixture.RecordRoot, "failure.json");

            var result = StablePromotionCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                output);

            Assert.False(result.Passed);
            using var handoff = JsonDocument.Parse(File.ReadAllBytes(output));
            var value = handoff.RootElement;
            Assert.False(value.GetProperty("passed").GetBoolean());
            Assert.False(value.GetProperty("releaseAcceptance").GetBoolean());
            Assert.Equal(JsonValueKind.Null, value.GetProperty("sourceRevision").ValueKind);
            Assert.Empty(value.GetProperty("artifactSha256ByPlatform").EnumerateObject());
            Assert.NotEmpty(value.GetProperty("errors").EnumerateArray());
        });
    }

    [Fact]
    public void Supplied_record_cannot_mask_a_failed_foundation_authority()
    {
        WithFixture(fixture =>
        {
            var authorityPath = Path.Combine(
                fixture.RepositoryRoot,
                "config",
                "stable_upstream_acceptance_v1.json");
            var authority = JsonNode.Parse(File.ReadAllBytes(authorityPath))!.AsObject();
            authority["gateIdsById"]!["human-playtest"]!.AsArray().RemoveAt(0);
            WriteJson(authorityPath, authority);
            var output = Path.Combine(fixture.RecordRoot, "failed-foundation.json");

            var result = StablePromotionCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                output);

            Assert.False(result.Passed);
            using var handoff = JsonDocument.Parse(File.ReadAllBytes(output));
            Assert.False(handoff.RootElement.GetProperty("guardQualified").GetBoolean());
            Assert.False(handoff.RootElement.GetProperty("releaseAcceptance").GetBoolean());
        });
    }

    [Fact]
    public void Portable_path_policy_rejects_every_cross_platform_alias_family()
    {
        var invalid = new[]
        {
            "", "/absolute", "trailing/", "back\\slash", "C:drive", "wild?card",
            "control\u0001name", ".", "..", "folder/.", "folder/..", "trail ", "trail.",
            "CON", "prn.txt", "AUX", "NUL.bin", "CLOCK$", "COM1.txt", "LPT9",
        };
        foreach (var path in invalid)
        {
            WithFixture(fixture =>
            {
                fixture.Record["optionalPackPath"] = path;
                fixture.WriteRecord();
                var result = StablePromotionCheck.WriteRecordHandoff(
                    fixture.RepositoryRoot,
                    fixture.RecordPath,
                    Revision,
                    Path.Combine(fixture.RecordRoot, "invalid-path.json"));
                Assert.False(result.Passed);
            });
        }
    }

    [Fact]
    public void Previous_version_parser_accepts_channels_and_rejects_overflow_or_future_values()
    {
        foreach (var version in new[] { "0.9.0-alpha.1", "0.9.0-beta.2", "0.9.0-rc.3" })
        {
            WithFixture(fixture =>
            {
                fixture.MutateDecision("release-rehearsal", decision => decision["previousVersion"] = version);
                var result = StablePromotionCheck.WriteRecordHandoff(
                    fixture.RepositoryRoot,
                    fixture.RecordPath,
                    Revision,
                    Path.Combine(fixture.RecordRoot, "accepted-version.json"));
                Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            });
        }
        foreach (var version in new[]
        {
            "not-semver", "999999999999999999999.0.0", "0.999999999999999999999.0",
            "0.0.999999999999999999999", "0.9.0-alpha.999999999999999999999", "2.0.0",
        })
        {
            WithFixture(fixture =>
            {
                fixture.MutateDecision("release-rehearsal", decision => decision["previousVersion"] = version);
                AssertFailure(fixture, "previousVersion");
            });
        }
    }

    [Fact]
    public void Manifest_entries_fail_closed_on_shape_bounds_paths_and_numeric_types()
    {
        var mutations = new Action<JsonObject>[]
        {
            manifest => manifest["files"] = "not-an-array",
            manifest => manifest["files"] = new JsonArray(),
            manifest => manifest["files"] = new JsonArray("not-an-object"),
            manifest => manifest["files"] = new JsonArray(new JsonObject { ["path"] = "x" }),
            manifest => manifest["files"]![0]!["path"] = "../escape",
            manifest => manifest["files"]![0]!["bytes"] = -1,
            manifest => manifest["files"]![0]!["sha256"] = "BAD",
            manifest => manifest["files"]![0]!["compressedBytes"] = -1,
            manifest => manifest["fileCount"] = 2,
            manifest => manifest["totalBytes"] = 4,
            manifest => manifest["containerEntries"] = "not-an-array",
        };
        foreach (var mutation in mutations)
        {
            WithFixture(fixture =>
            {
                var manifest = fixture.ReadJson("public/windows-x64/artifact-manifest.json");
                mutation(manifest);
                fixture.WriteJson("public/windows-x64/artifact-manifest.json", manifest);
                fixture.RefreshRecordHashes();
                var result = StablePromotionCheck.WriteRecordHandoff(
                    fixture.RepositoryRoot,
                    fixture.RecordPath,
                    Revision,
                    Path.Combine(fixture.RecordRoot, "invalid-manifest.json"));
                Assert.False(result.Passed);
            });
        }
    }

    [Fact]
    public void Checksum_parser_rejects_encoding_line_endings_names_duplicates_and_wrong_sets()
    {
        var invalidRows = new byte[][]
        {
            [0xff, 0xfe],
            Encoding.UTF8.GetBytes($"{new string('a', 64)}  file\r\n"),
            Encoding.UTF8.GetBytes($"{new string('a', 64)}  ../file\n"),
            Encoding.UTF8.GetBytes($"{new string('a', 64)}  same\n{new string('b', 64)}  same\n"),
            Encoding.UTF8.GetBytes($"{new string('a', 64)}  unrelated\n"),
        };
        foreach (var bytes in invalidRows)
        {
            WithFixture(fixture =>
            {
                File.WriteAllBytes(Path.Combine(fixture.RecordRoot, "public/windows-x64/SHA256SUMS"), bytes);
                fixture.RefreshRecordHashes();
                var result = StablePromotionCheck.WriteRecordHandoff(
                    fixture.RepositoryRoot,
                    fixture.RecordPath,
                    Revision,
                    Path.Combine(fixture.RecordRoot, "invalid-checksum.json"));
                Assert.False(result.Passed);
            });
        }
    }

    [Fact]
    public void Gate_and_evidence_arrays_reject_wrong_shapes_roles_and_duplicates()
    {
        var mutations = new Action<JsonObject>[]
        {
            decision => decision["gateRecords"] = "not-an-array",
            decision => decision["gateRecords"] = new JsonArray(),
            decision => decision["gateRecords"]![0] = "not-an-object",
            decision => decision["gateRecords"]![0]!["authorityRoleId"] = "Personal Name",
            decision => decision["gateRecords"]![0]!["evidencePaths"] = new JsonArray(),
            decision => decision["gateRecords"]![0]!["evidencePaths"] = new JsonArray("../escape"),
        };
        foreach (var mutation in mutations)
        {
            WithFixture(fixture =>
            {
                fixture.MutateDecision("release-matrix", mutation);
                var result = StablePromotionCheck.WriteRecordHandoff(
                    fixture.RepositoryRoot,
                    fixture.RecordPath,
                    Revision,
                    Path.Combine(fixture.RecordRoot, "invalid-gate.json"));
                Assert.False(result.Passed);
            });
        }
        WithFixture(fixture =>
        {
            fixture.Record["publicInstallResults"]![0]!["evidencePaths"] = "not-an-array";
            fixture.WriteRecord();
            AssertFailure(fixture, "evidencePaths");
        });
        WithFixture(fixture =>
        {
            fixture.Record["publicInstallResults"]![0]!["evidencePaths"] =
                new JsonArray("install/windows-x64.txt", "install/windows-x64.txt");
            fixture.WriteRecord();
            AssertFailure(fixture, "unique safe");
        });
    }

    [Fact]
    public void Output_validation_rejects_portable_names_directories_and_non_directory_parents()
    {
        WithFixture(fixture =>
        {
            var reserved = StablePromotionCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                Path.Combine(fixture.RecordRoot, "CON"));
            Assert.False(reserved.Passed);

            var directory = Path.Combine(fixture.RecordRoot, "existing");
            Directory.CreateDirectory(directory);
            var directoryResult = StablePromotionCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                directory);
            Assert.False(directoryResult.Passed);

            var parentFile = Path.Combine(fixture.RecordRoot, "parent-file");
            File.WriteAllText(parentFile, "not a directory", Encoding.UTF8);
            var parentResult = StablePromotionCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                Path.Combine(parentFile, "output.json"));
            Assert.False(parentResult.Passed);
        });
    }

    [Fact]
    public void Scalar_types_timestamps_pending_state_and_digest_maps_fail_closed()
    {
        var mutations = new Action<JsonObject>[]
        {
            decision => decision["schemaVersion"] = "1",
            decision => decision["passed"] = "true",
            decision => decision["releaseAcceptance"] = 1,
            decision => decision["sourceRevision"] = 123,
            decision => decision["appVersion"] = false,
            decision => decision["acceptedUtc"] = null,
            decision => decision["acceptedUtc"] = "2026-08-30T12:00:00.000Z",
            decision => decision["acceptedUtc"] = "2026-99-99T12:00:00Z",
            decision => decision["pendingGates"] = new JsonArray("still-pending"),
            decision => decision["errors"] = new JsonArray("failure"),
            decision => decision["candidateArtifactSha256ByPlatform"] = "not-an-object",
            decision => decision["candidateManifestSha256ByPlatform"]!.AsObject().Remove("linux-x64"),
            decision => decision["candidateArtifactSha256ByPlatform"]!["windows-x64"] = 123,
            decision => decision["candidateManifestSha256ByPlatform"]!["windows-x64"] = "ABC",
        };
        foreach (var mutation in mutations)
        {
            WithFixture(fixture =>
            {
                fixture.MutateDecision("external-validation", mutation);
                var result = StablePromotionCheck.WriteRecordHandoff(
                    fixture.RepositoryRoot,
                    fixture.RecordPath,
                    Revision,
                    Path.Combine(fixture.RecordRoot, "invalid-scalar.json"));
                Assert.False(result.Passed);
            });
        }
    }

    [Fact]
    public void Record_map_shape_workflow_identity_and_special_scalar_failures_are_closed()
    {
        var recordMutations = new Action<Fixture>[]
        {
            fixture => fixture.Record.Remove("kind"),
            fixture => fixture.Record["protectedWorkflowRunId"] = "000001",
            fixture => fixture.Record["protectedWorkflowRunId"] = 123456,
            fixture => fixture.Record["artifactSha256ByPlatform"] = "not-an-object",
            fixture => fixture.Record["artifactSha256ByPlatform"]!.AsObject().Remove("linux-x64"),
            fixture => fixture.Record["manifestPathsByPlatform"]!.AsObject().Remove("linux-x64"),
            fixture => fixture.Record["provenanceSha256ByPlatform"]!.AsObject().Remove("linux-x64"),
            fixture => fixture.Record["artifactSha256ByPlatform"]!["linux-x64"] = "BAD",
            fixture => fixture.Record["optionalPackSha256"] = 123,
            fixture => fixture.Record["optionalPackManifestSha256"] = "BAD",
            fixture => fixture.Record["publicInstallResults"] = new JsonArray(),
            fixture => fixture.Record["preservedEvidencePathsByCategory"] = "not-an-object",
            fixture => fixture.Record["stableContractAcknowledgements"] = "not-an-array",
        };
        foreach (var mutation in recordMutations)
        {
            WithFixture(fixture =>
            {
                mutation(fixture);
                fixture.WriteRecord();
                var result = StablePromotionCheck.WriteRecordHandoff(
                    fixture.RepositoryRoot,
                    fixture.RecordPath,
                    Revision,
                    Path.Combine(fixture.RecordRoot, "invalid-record-shape.json"));
                Assert.False(result.Passed);
            });
        }
        WithFixture(fixture =>
        {
            fixture.MutateDecision("release-rehearsal", decision => decision["recordSha256"] = 123);
            AssertFailure(fixture, "recordSha256");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision("release-materials", decision => decision["structuralHandoffPath"] = 123);
            AssertFailure(fixture, "structuralHandoffPath");
        });
        WithFixture(fixture =>
        {
            fixture.MutateDecision(
                "release-materials",
                decision => decision["structuralHandoffPath"] = "../structural.json");
            AssertFailure(fixture, "structuralHandoffPath");
        });
    }

    [Fact]
    public void Missing_empty_case_colliding_and_directory_inputs_are_rejected()
    {
        WithFixture(fixture =>
        {
            fixture.Record["optionalPackPath"] = "optional/missing.pack";
            fixture.WriteRecord();
            AssertFailure(fixture, "missing retained file");
        });
        WithFixture(fixture =>
        {
            var empty = Path.Combine(fixture.RecordRoot, "optional", "empty.pack");
            File.WriteAllBytes(empty, []);
            fixture.Record["optionalPackPath"] = "optional/empty.pack";
            fixture.WriteRecord();
            AssertFailure(fixture, "nonempty");
        });
        WithFixture(fixture =>
        {
            var directory = Path.Combine(fixture.RecordRoot, "optional", "directory.pack");
            Directory.CreateDirectory(directory);
            fixture.Record["optionalPackPath"] = "optional/directory.pack";
            fixture.WriteRecord();
            AssertFailure(fixture, "missing retained file");
        });
        WithFixture(fixture =>
        {
            fixture.Record["preservedEvidencePathsByCategory"]!["sbom"]!.AsArray()
                .Add("OPTIONAL/APPROVED.VIBESNAKE-PACK.ZIP");
            fixture.WriteRecord();
            AssertFailure(fixture, "collide by portable case");
        });
    }

    [Fact]
    public void Manifest_top_level_types_smoke_identity_and_duplicate_entries_are_rejected()
    {
        var mutations = new Action<JsonObject>[]
        {
            manifest => manifest.Remove("product"),
            manifest => manifest["schemaVersion"] = "3",
            manifest => manifest["smokeStateHash"] = 123,
            manifest => manifest["fileCount"] = -1,
            manifest => manifest["totalBytes"] = "3",
            manifest => manifest["files"]!.AsArray().Add(manifest["files"]![0]!.DeepClone()),
        };
        foreach (var mutation in mutations)
        {
            WithFixture(fixture =>
            {
                var manifest = fixture.ReadJson("public/macos-universal/artifact-manifest.json");
                mutation(manifest);
                fixture.WriteJson("public/macos-universal/artifact-manifest.json", manifest);
                fixture.RefreshRecordHashes();
                var result = StablePromotionCheck.WriteRecordHandoff(
                    fixture.RepositoryRoot,
                    fixture.RecordPath,
                    Revision,
                    Path.Combine(fixture.RecordRoot, "invalid-manifest-top.json"));
                Assert.False(result.Passed);
            });
        }
        WithFixture(fixture =>
        {
            var manifest = fixture.ReadJson("public/linux-x64/artifact-manifest.json");
            manifest["smokeStateHash"] = "1111111111111111";
            fixture.WriteJson("public/linux-x64/artifact-manifest.json", manifest);
            fixture.RefreshRecordHashes();
            AssertFailure(fixture, "shared smoke state hash");
        });
    }

    private static void AssertFailure(Fixture fixture, string expected) =>
        AssertFailure(fixture.RepositoryRoot, fixture.RecordPath, expected);

    private static void AssertFailure(string repositoryRoot, string expected)
    {
        var result = StablePromotionCheck.Inspect(repositoryRoot);
        Assert.False(result.Passed);
        Assert.Contains(result.Failures, item => item.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertFailure(string repositoryRoot, string recordPath, string expected)
    {
        var result = StablePromotionCheck.WriteRecordHandoff(
            repositoryRoot,
            recordPath,
            Revision,
            Path.Combine(Path.GetDirectoryName(recordPath)!, "failure.json"));
        Assert.False(result.Passed);
        Assert.True(
            result.Failures.Any(item => item.Contains(expected, StringComparison.OrdinalIgnoreCase)),
            string.Join(Environment.NewLine, result.Failures));
    }

    private static void WithFixture(Action<Fixture> action)
    {
        WithTemporaryDirectory(root =>
        {
            WriteFoundation(root);
            action(new Fixture(root));
        });
    }

    private static void WriteFoundation(string root)
    {
        var source = FindRepositoryRoot();
        Copy(source, root, "config/stable_promotion_v1.json");
        Copy(source, root, "config/stable_upstream_acceptance_v1.json");
    }

    private static void Copy(string sourceRoot, string destinationRoot, string relativePath)
    {
        var destination = Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(
            Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            destination);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "config", "stable_promotion_v1.json")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"vibesnake-stable-{Guid.NewGuid():N}");
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

    private static void WriteJson(string path, JsonNode value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    private static string Sha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class Fixture
    {
        private readonly HashSet<string> referenced = new(StringComparer.Ordinal);

        public Fixture(string repositoryRoot)
        {
            RepositoryRoot = repositoryRoot;
            RecordRoot = Path.Combine(repositoryRoot, "retained");
            Directory.CreateDirectory(RecordRoot);
            WritePrerequisites();
            UnsignedArtifacts = Platforms.ToDictionary(
                platform => platform,
                platform => HashText($"unsigned-artifact-{platform}"),
                StringComparer.Ordinal);
            UnsignedManifests = Platforms.ToDictionary(
                platform => platform,
                platform => HashText($"unsigned-manifest-{platform}"),
                StringComparer.Ordinal);
            FinalArtifacts = new Dictionary<string, string>(StringComparer.Ordinal);
            FinalManifests = new Dictionary<string, string>(StringComparer.Ordinal);
            FinalProvenance = new Dictionary<string, string>(StringComparer.Ordinal);
            var artifactPaths = new JsonObject();
            var manifestPaths = new JsonObject();
            var provenancePaths = new JsonObject();
            var checksumPaths = new JsonObject();
            foreach (var platform in Platforms)
            {
                var artifact = $"public/{platform}/VibeSnake-1.0.0-{platform}.bin";
                WriteBytes(artifact, Encoding.UTF8.GetBytes($"signed public artifact {platform}"));
                FinalArtifacts[platform] = Hash(artifact);
                var manifest = $"public/{platform}/artifact-manifest.json";
                WriteJson(manifest, Manifest(platform));
                FinalManifests[platform] = Hash(manifest);
                var provenance = $"public/{platform}/provenance.jsonl";
                WriteBytes(provenance, Encoding.UTF8.GetBytes($"retained provenance {platform}\n"));
                FinalProvenance[platform] = Hash(provenance);
                var checksum = $"public/{platform}/SHA256SUMS";
                var rows = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Path.GetFileName(artifact)] = FinalArtifacts[platform],
                    [Path.GetFileName(manifest)] = FinalManifests[platform],
                    [Path.GetFileName(provenance)] = FinalProvenance[platform],
                };
                WriteBytes(
                    checksum,
                    Encoding.UTF8.GetBytes(string.Concat(rows.OrderBy(item => item.Key, StringComparer.Ordinal)
                        .Select(item => $"{item.Value}  {item.Key}\n"))));
                artifactPaths[platform] = artifact;
                manifestPaths[platform] = manifest;
                provenancePaths[platform] = provenance;
                checksumPaths[platform] = checksum;
            }
            WriteBytes("optional/approved.vibesnake-pack.zip", "approved optional pack"u8.ToArray());
            WriteBytes("optional/pack.json", "approved optional manifest"u8.ToArray());
            OptionalPackSha = Hash("optional/approved.vibesnake-pack.zip");
            OptionalManifestSha = Hash("optional/pack.json");

            var upstreamPaths = new JsonObject();
            foreach (var id in DecisionIds.Where(id => id is not "release-materials" and not "release-rehearsal"))
            {
                upstreamPaths[id] = $"decisions/{id}.json";
                WriteDecision(id, GenericDecision(id));
            }
            upstreamPaths["release-materials"] = "decisions/release-materials.json";
            WriteMaterialDecision();
            upstreamPaths["release-rehearsal"] = "decisions/release-rehearsal.json";
            WriteRehearsalDecision();

            var installs = new JsonArray();
            foreach (var platform in Platforms)
            {
                var evidence = $"install/{platform}.txt";
                WriteBytes(evidence, Encoding.UTF8.GetBytes($"public install {platform}"));
                installs.Add(new JsonObject
                {
                    ["platformId"] = platform,
                    ["result"] = "pass",
                    ["installedArtifactSha256"] = FinalArtifacts[platform],
                    ["smokeStateHash"] = "600f29e8919a9400",
                    ["evidencePaths"] = new JsonArray(evidence),
                });
            }
            var preserved = new JsonObject();
            foreach (var category in PreservedCategories)
            {
                var path = $"preserved/{category}.txt";
                WriteBytes(path, Encoding.UTF8.GetBytes($"preserved {category}"));
                preserved[category] = new JsonArray(path);
            }
            Record = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "vibesnake-stable-promotion-record-v1",
                ["sourceRevision"] = Revision,
                ["appVersion"] = "1.0.0",
                ["tagName"] = "1.0.0",
                ["tagObjectRevision"] = Revision,
                ["protectedWorkflowRunId"] = "1234567890",
                ["artifactSha256ByPlatform"] = Map(FinalArtifacts),
                ["artifactPathsByPlatform"] = artifactPaths,
                ["manifestSha256ByPlatform"] = Map(FinalManifests),
                ["manifestPathsByPlatform"] = manifestPaths,
                ["provenanceSha256ByPlatform"] = Map(FinalProvenance),
                ["provenancePathsByPlatform"] = provenancePaths,
                ["checksumPathsByPlatform"] = checksumPaths,
                ["optionalPackSha256"] = OptionalPackSha,
                ["optionalPackPath"] = "optional/approved.vibesnake-pack.zip",
                ["optionalPackManifestSha256"] = OptionalManifestSha,
                ["optionalPackManifestPath"] = "optional/pack.json",
                ["upstreamDecisionPathsById"] = upstreamPaths,
                ["publicInstallResults"] = installs,
                ["preservedEvidencePathsByCategory"] = preserved,
                ["stableContractAcknowledgements"] = new JsonArray(
                    Acknowledgements.Select(item => (JsonNode?)item).ToArray()),
                ["retainedFileSha256"] = new JsonObject(),
            };
            RecordPath = Path.Combine(RecordRoot, "record.json");
            RefreshRecordHashes();
        }

        public string RepositoryRoot { get; }
        public string RecordRoot { get; }
        public string RecordPath { get; }
        public JsonObject Record { get; }
        public Dictionary<string, string> UnsignedArtifacts { get; }
        public Dictionary<string, string> UnsignedManifests { get; }
        public Dictionary<string, string> FinalArtifacts { get; }
        public Dictionary<string, string> FinalManifests { get; }
        public Dictionary<string, string> FinalProvenance { get; }
        public string OptionalPackSha { get; }
        public string OptionalManifestSha { get; }

        public void MutateDecision(string id, Action<JsonObject> mutation)
        {
            var decision = ReadDecision(id);
            mutation(decision);
            WriteDecision(id, decision);
            RefreshRecordHashes();
        }

        public JsonObject ReadDecision(string id) => ReadJson($"decisions/{id}.json");

        public void WriteDecision(string id, JsonObject value) => WriteJson($"decisions/{id}.json", value);

        public JsonObject ReadJson(string relative) =>
            JsonNode.Parse(File.ReadAllBytes(Path.Combine(RecordRoot, Native(relative))))!.AsObject();

        public void WriteJson(string relative, JsonNode value)
        {
            var path = Path.Combine(RecordRoot, Native(relative));
            StablePromotionCheckTests.WriteJson(path, value);
            referenced.Add(relative);
        }

        public void WriteRecord() => StablePromotionCheckTests.WriteJson(RecordPath, Record);

        public void RefreshDecisionHashes(string id)
        {
            var decision = ReadDecision(id);
            var retained = decision["retainedFileSha256"]!.AsObject();
            foreach (var property in retained.ToArray())
            {
                retained[property.Key] = Hash($"decisions/{property.Key}");
            }
            WriteDecision(id, decision);
            RefreshRecordHashes();
        }

        public void RefreshRecordHashes()
        {
            var hashes = new JsonObject();
            foreach (var relative in referenced.Order(StringComparer.Ordinal))
            {
                hashes[relative] = Hash(relative);
            }
            Record["retainedFileSha256"] = hashes;
            WriteRecord();
        }

        private void WritePrerequisites()
        {
            WriteRepositoryFile("config/release_materials_v1.json", "release materials contract");
            WriteRepositoryFile("config/release_rehearsal_v1.json", "release rehearsal contract");
            WriteRepositoryFile("config/release_signing_policy.json", "release signing policy");
            WriteRepositoryFile("README.md", "readme");
            WriteRepositoryFile("docs/guides/PLAYER_GUIDE.md", "player guide");
            WriteRepositoryFile("docs/guides/ACCESSIBILITY.md", "accessibility");
            WriteRepositoryFile("PRIVACY.md", "privacy");
            WriteRepositoryFile("SUPPORT.md", "support");
            WriteRepositoryFile("docs/release/PACKAGING.md", "packaging");
            WriteRepositoryFile("docs/release/SIGNING.md", "signing");
            WriteRepositoryFile("docs/guides/RECOVERY.md", "recovery");
            WriteRepositoryFile("docs/release/KNOWN_ISSUES.md", "known issues");
            WriteRepositoryFile("THIRD_PARTY_NOTICES.md", "third party notices");
            WriteRepositoryFile("CREDITS.md", "credits");
            WriteRepositoryFile("CHANGELOG.md", "changelog");
        }

        private void WriteRepositoryFile(string relative, string value)
        {
            var path = Path.Combine(RepositoryRoot, Native(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value + "\n", new UTF8Encoding(false));
        }

        private JsonObject GenericDecision(string id)
        {
            var retained = new JsonObject();
            var gateRows = new JsonArray();
            foreach (var (gate, index) in Gates[id].Select((gate, index) => (gate, index)))
            {
                var evidence = $"{id}/gate-{index}.txt";
                WriteBytes($"decisions/{evidence}", Encoding.UTF8.GetBytes($"{id} {gate}"));
                retained[evidence] = Hash($"decisions/{evidence}");
                gateRows.Add(new JsonObject
                {
                    ["gateId"] = gate,
                    ["result"] = "pass",
                    ["authorityRoleId"] = $"{id}-reviewer",
                    ["evidencePaths"] = new JsonArray(evidence),
                });
            }
            var candidateArtifacts = id == "platform-signing" ? FinalArtifacts : UnsignedArtifacts;
            var candidateManifests = id == "platform-signing" ? FinalManifests : UnsignedManifests;
            var decision = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = Kinds[id],
                ["passed"] = true,
                ["releaseAcceptance"] = true,
                ["sourceRevision"] = Revision,
                ["appVersion"] = "1.0.0",
                ["candidateArtifactSha256ByPlatform"] = Map(candidateArtifacts),
                ["candidateManifestSha256ByPlatform"] = Map(candidateManifests),
                ["acceptedUtc"] = "2026-08-30T12:00:00Z",
                ["gateRecords"] = gateRows,
                ["retainedFileSha256"] = retained,
                ["pendingGates"] = new JsonArray(),
                ["errors"] = new JsonArray(),
            };
            if (id == "content-approval")
            {
                decision["optionalPackSha256"] = OptionalPackSha;
                decision["optionalPackManifestSha256"] = OptionalManifestSha;
            }
            if (id == "platform-signing")
            {
                decision["inputArtifactSha256ByPlatform"] = Map(UnsignedArtifacts);
                decision["inputManifestSha256ByPlatform"] = Map(UnsignedManifests);
                decision["provenanceSha256ByPlatform"] = Map(FinalProvenance);
            }
            return decision;
        }

        private void WriteMaterialDecision()
        {
            var structuralRelative = "release-materials/structural.json";
            var documentSha = new JsonObject();
            foreach (var path in new[]
            {
                "README.md", "docs/guides/PLAYER_GUIDE.md", "docs/guides/ACCESSIBILITY.md",
                "PRIVACY.md", "SUPPORT.md", "docs/guides/RECOVERY.md",
                "docs/release/KNOWN_ISSUES.md", "THIRD_PARTY_NOTICES.md", "CREDITS.md", "CHANGELOG.md",
            })
            {
                documentSha[path] = Sha256(Path.Combine(RepositoryRoot, Native(path)));
            }
            var structural = new JsonObject
            {
                ["schemaVersion"] = 2,
                ["kind"] = "release-materials-handoff-v2",
                ["passed"] = true,
                ["foundationQualified"] = true,
                ["contractSha256"] = Sha256(Path.Combine(RepositoryRoot, Native("config/release_materials_v1.json"))),
                ["documentSha256"] = documentSha,
                ["requiredDocumentCount"] = 10,
                ["artifactPlatformCount"] = 3,
                ["inputDeviceCount"] = 4,
                ["screenshotRoleCount"] = 6,
                ["videoRoleCount"] = 2,
                ["marketingClaimCount"] = 8,
                ["candidateSupplied"] = true,
                ["candidateMaterialComplete"] = true,
                ["releaseAcceptance"] = false,
                ["sourceRevision"] = Revision,
                ["appVersion"] = "1.0.0",
                ["candidateSha256"] = new string('9', 64),
                ["pendingGates"] = new JsonArray(Gates["release-materials"].Select(item => (JsonNode?)item).ToArray()),
                ["errors"] = new JsonArray(),
            };
            WriteJson($"decisions/{structuralRelative}", structural);
            var retained = new JsonObject { [structuralRelative] = Hash($"decisions/{structuralRelative}") };
            var rows = new JsonArray();
            foreach (var (gate, index) in Gates["release-materials"].Select((gate, index) => (gate, index)))
            {
                var evidence = $"release-materials/gate-{index}.txt";
                WriteBytes($"decisions/{evidence}", Encoding.UTF8.GetBytes(gate));
                retained[evidence] = Hash($"decisions/{evidence}");
                rows.Add(new JsonObject
                {
                    ["gateId"] = gate,
                    ["result"] = "pass",
                    ["authorityRoleId"] = "release-material-reviewer",
                    ["evidencePaths"] = new JsonArray(evidence),
                });
            }
            WriteDecision(
                "release-materials",
                new JsonObject
                {
                    ["schemaVersion"] = 1,
                    ["kind"] = "release-materials-acceptance-v1",
                    ["passed"] = true,
                    ["foundationQualified"] = true,
                    ["candidateMaterialComplete"] = true,
                    ["releaseAcceptance"] = true,
                    ["sourceRevision"] = Revision,
                    ["appVersion"] = "1.0.0",
                    ["candidateSha256"] = new string('9', 64),
                    ["structuralHandoffPath"] = structuralRelative,
                    ["structuralHandoffSha256"] = Hash($"decisions/{structuralRelative}"),
                    ["artifactManifestSha256ByPlatform"] = Map(UnsignedManifests),
                    ["acceptedUtc"] = "2026-08-30T12:00:00Z",
                    ["gateRecords"] = rows,
                    ["retainedFileSha256"] = retained,
                    ["pendingGates"] = new JsonArray(),
                    ["errors"] = new JsonArray(),
                });
        }

        private void WriteRehearsalDecision()
        {
            var prerequisites = new JsonObject();
            foreach (var path in new[]
            {
                "config/release_materials_v1.json", "config/release_signing_policy.json",
                "docs/release/PACKAGING.md", "docs/release/SIGNING.md", "docs/guides/RECOVERY.md",
            })
            {
                prerequisites[path] = Sha256(Path.Combine(RepositoryRoot, Native(path)));
            }
            WriteDecision(
                "release-rehearsal",
                new JsonObject
                {
                    ["schemaVersion"] = 2,
                    ["kind"] = "release-rehearsal-handoff-v2",
                    ["passed"] = true,
                    ["protocolQualified"] = true,
                    ["contractSha256"] = Sha256(Path.Combine(RepositoryRoot, Native("config/release_rehearsal_v1.json"))),
                    ["prerequisiteSha256"] = prerequisites,
                    ["artifactPlatformCount"] = 3,
                    ["platformOperationCount"] = 11,
                    ["requiredPlatformOperationCellCount"] = 33,
                    ["authorityOperationCount"] = 4,
                    ["recordSupplied"] = true,
                    ["recordSha256"] = new string('8', 64),
                    ["recordIntegrityQualified"] = true,
                    ["externalExecutionAttested"] = true,
                    ["rehearsalComplete"] = true,
                    ["releaseAcceptance"] = true,
                    ["sourceRevision"] = Revision,
                    ["appVersion"] = "1.0.0",
                    ["previousVersion"] = "0.9.0",
                    ["releaseMaterialsDecisionSha256"] = Hash("decisions/release-materials.json"),
                    ["candidateArtifactSha256ByPlatform"] = Map(UnsignedArtifacts),
                    ["candidateManifestSha256ByPlatform"] = Map(UnsignedManifests),
                    ["pendingGates"] = new JsonArray(),
                    ["errors"] = new JsonArray(),
                });
        }

        private static JsonObject Manifest(string platform) =>
            new()
            {
                ["schemaVersion"] = 3,
                ["product"] = "Vibe Snake",
                ["platform"] = platform,
                ["buildMode"] = "Release",
                ["sourceRevision"] = Revision,
                ["godotVersion"] = "4.7.1.stable.mono",
                ["godotCommit"] = "5216e747a",
                ["godotArchiveSha512"] = new string('a', 128),
                ["godotExecutableSha256"] = new string('b', 64),
                ["dotnetSdk"] = "10.0.303",
                ["smokeStateHash"] = "600f29e8919a9400",
                ["agentArenaPreviewExcluded"] = true,
                ["fileCount"] = 1,
                ["totalBytes"] = 3,
                ["files"] = new JsonArray(
                    new JsonObject
                    {
                        ["path"] = "VibeSnake.pck",
                        ["bytes"] = 3,
                        ["sha256"] = HashText($"manifest payload {platform}"),
                    }),
                ["containerEntries"] = new JsonArray(),
            };

        private void WriteBytes(string relative, byte[] bytes)
        {
            var path = Path.Combine(RecordRoot, Native(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            referenced.Add(relative);
        }

        private string Hash(string relative) => Sha256(Path.Combine(RecordRoot, Native(relative)));

        private static JsonObject Map(Dictionary<string, string> values)
        {
            var result = new JsonObject();
            foreach (var platform in Platforms)
            {
                result[platform] = values[platform];
            }
            return result;
        }

        private static string HashText(string value) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private static string Native(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);
    }
}
