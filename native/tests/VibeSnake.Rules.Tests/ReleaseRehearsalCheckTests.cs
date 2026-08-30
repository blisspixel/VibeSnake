using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class ReleaseRehearsalCheckTests
{
    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly string[] Platforms =
        ["windows-x64", "macos-universal", "linux-x64"];

    private static readonly string[] Operations =
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

    private static readonly string[] AuthorityIds =
        ["publish", "halt", "replace", "communicate"];

    private static readonly string[] MaterialGateIds =
    [
        "artifact-manifest-size-reconciliation",
        "marketing-claim-approval",
        "visible-image-review",
        "video-playback-review",
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

    [Fact]
    public void Foundation_is_read_only_and_writes_canonical_pending_evidence()
    {
        WithFixture(fixture =>
        {
            var before = Directory.GetFiles(fixture.RepositoryRoot, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var inspection = ReleaseRehearsalCheck.Inspect(fixture.RepositoryRoot);
            var after = Directory.GetFiles(fixture.RepositoryRoot, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.True(inspection.Passed, string.Join("; ", inspection.Failures));
            Assert.Equal(before, after);
            Assert.Equal("Release rehearsal", inspection.Name);
            Assert.Contains("staged execution remains pending", inspection.SuccessMessage, StringComparison.Ordinal);

            var output = Path.Combine(fixture.RepositoryRoot, "TestResults", "rehearsal.json");
            var first = ReleaseRehearsalCheck.WriteFoundationHandoff(fixture.RepositoryRoot, output);
            var firstBytes = File.ReadAllBytes(output);
            var second = ReleaseRehearsalCheck.WriteFoundationHandoff(fixture.RepositoryRoot, output);

            Assert.True(first.Passed, string.Join("; ", first.Failures));
            Assert.True(second.Passed, string.Join("; ", second.Failures));
            Assert.Equal(firstBytes, File.ReadAllBytes(output));
            Assert.DoesNotContain((byte)'\r', firstBytes);
            using var document = JsonDocument.Parse(firstBytes);
            var value = document.RootElement;
            Assert.Equal(2, value.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("release-rehearsal-handoff-v2", value.GetProperty("kind").GetString());
            Assert.True(value.GetProperty("passed").GetBoolean());
            Assert.True(value.GetProperty("protocolQualified").GetBoolean());
            Assert.False(value.GetProperty("recordSupplied").GetBoolean());
            Assert.False(value.GetProperty("releaseAcceptance").GetBoolean());
            Assert.Equal(6, value.GetProperty("pendingGates").GetArrayLength());
            Assert.Equal(6, value.GetProperty("prerequisiteSha256").EnumerateObject().Count());
            Assert.Equal(
                value.GetProperty("materialAcceptanceContractSha256").GetString(),
                value.GetProperty("prerequisiteSha256")
                    .GetProperty("config/release_materials_acceptance_v1.json")
                    .GetString());
        });
    }

    [Fact]
    public void Exact_external_record_and_material_acceptance_close_the_gate()
    {
        WithFixture(fixture =>
        {
            var output = Path.Combine(fixture.EvidenceRoot, "decisions", "rehearsal.json");
            var result = ReleaseRehearsalCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                output);

            Assert.True(result.Passed, string.Join("; ", result.Failures));
            Assert.Contains("accepted for the exact candidate", result.SuccessMessage, StringComparison.Ordinal);
            Assert.True(File.Exists(output));
            using var document = JsonDocument.Parse(File.ReadAllBytes(output));
            var value = document.RootElement;
            Assert.True(value.GetProperty("recordIntegrityQualified").GetBoolean());
            Assert.True(value.GetProperty("externalExecutionAttested").GetBoolean());
            Assert.True(value.GetProperty("rehearsalComplete").GetBoolean());
            Assert.True(value.GetProperty("releaseAcceptance").GetBoolean());
            Assert.Equal(Revision, value.GetProperty("sourceRevision").GetString());
            Assert.Equal("0.3.0-alpha.1", value.GetProperty("appVersion").GetString());
            Assert.Equal("0.2.1", value.GetProperty("previousVersion").GetString());
            Assert.Equal(3, value.GetProperty("candidateArtifactSha256ByPlatform").EnumerateObject().Count());
            Assert.Equal(3, value.GetProperty("candidateManifestSha256ByPlatform").EnumerateObject().Count());
            Assert.Equal(0, value.GetProperty("pendingGates").GetArrayLength());
            Assert.Equal(0, value.GetProperty("errors").GetArrayLength());
        });
    }

    [Fact]
    public void Record_shape_results_identity_time_and_types_fail_closed()
    {
        var mutations = new Action<Fixture, JsonObject>[]
        {
            (_, record) => record["schemaVersion"] = true,
            (_, record) => record.Remove("rehearsalId"),
            (_, record) => record["sourceRevision"] = new string('b', 40),
            (_, record) => record["appVersion"] = "0.2.1",
            (_, record) => record["previousVersion"] = "9.0.0",
            (_, record) => record["executedUtc"] = "2026-99-99T99:99:99Z",
            (_, record) => record["migrationFixtureSetSha256"] = new string('f', 64),
            (_, record) => record["platformResults"]![0]!["operationResults"]!["rollback"] = "fail",
            (_, record) => record["platformResults"]![1]!["protectedUserDataSha256After"] = new string('f', 64),
            (_, record) => record["withdrawalResult"]!["candidateUnavailable"] = 1,
            (_, record) => record["authorityRecords"]![0]!["authorizationVerified"] = 1,
            (_, record) => record["authorityRecords"]![1]!["roleId"] = "Person Name",
            (_, record) => record["platformResults"]![0]!["platformId"] = "linux-x64",
        };

        foreach (var mutation in mutations)
        {
            WithFixture(fixture =>
            {
                var record = LoadObject(fixture.RecordPath);
                mutation(fixture, record);
                WriteJson(fixture.RecordPath, record);

                var result = Validate(fixture);

                Assert.False(result.Passed);
                Assert.NotEmpty(result.Failures);
            });
        }
    }

    [Fact]
    public void Accepted_material_decision_must_be_authorized_coherent_and_prior()
    {
        var mutations = new Action<Fixture, JsonObject>[]
        {
            (_, decision) => decision["releaseAcceptance"] = false,
            (_, decision) => decision["candidateMaterialComplete"] = false,
            (_, decision) => decision["sourceRevision"] = new string('b', 40),
            (_, decision) => decision["acceptedUtc"] = "2026-08-09T19:00:00Z",
            (_, decision) => decision["pendingGates"] = StringArray("still-pending"),
            (_, decision) => decision["gateRecords"]![0]!["result"] = "blocked",
            (_, decision) => decision["gateRecords"]![1]!["authorityRoleId"] = "Named Person",
            (_, decision) => decision["artifactManifestSha256ByPlatform"]!["windows-x64"] = new string('f', 64),
        };

        foreach (var mutation in mutations)
        {
            WithFixture(fixture =>
            {
                var decision = LoadObject(fixture.MaterialDecisionPath);
                mutation(fixture, decision);
                SaveDecisionAndRefreshRecord(fixture, decision);

                var result = Validate(fixture);

                Assert.False(result.Passed);
                Assert.NotEmpty(result.Failures);
            });
        }

        WithFixture(fixture =>
        {
            var decision = LoadObject(fixture.MaterialDecisionPath);
            decision["gateRecords"]![0]!["evidencePaths"] = StringArray("structural.json");
            RefreshDecisionRetained(fixture, decision);
            SaveDecisionAndRefreshRecord(fixture, decision);
            var result = Validate(fixture);
            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains("cannot alias", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            var decision = LoadObject(fixture.MaterialDecisionPath);
            var shared = decision["gateRecords"]![0]!["evidencePaths"]![0]!.GetValue<string>();
            decision["gateRecords"]![1]!["evidencePaths"] = StringArray(shared);
            RefreshDecisionRetained(fixture, decision);
            SaveDecisionAndRefreshRecord(fixture, decision);
            var result = Validate(fixture);
            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains("cannot alias", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Manifests_are_strict_release_candidate_records_and_cross_platform_coherent()
    {
        var mutations = new Action<JsonObject>[]
        {
            manifest => manifest["platform"] = "linux-x64",
            manifest => manifest["buildMode"] = "Debug",
            manifest => manifest["sourceRevision"] = new string('b', 40),
            manifest => manifest["agentArenaPreviewExcluded"] = false,
            manifest => manifest["smokeStateHash"] = new string('f', 16),
            manifest => manifest["fileCount"] = 2,
            manifest => manifest["totalBytes"] = 2,
            manifest => manifest["files"]![0]!["path"] = "../escape",
        };

        foreach (var mutation in mutations)
        {
            WithFixture(fixture =>
            {
                var manifestPath = Path.Combine(
                    fixture.EvidenceRoot,
                    "staged",
                    "windows-x64",
                    "artifact-manifest.json");
                var manifest = LoadObject(manifestPath);
                mutation(manifest);
                WriteJson(manifestPath, manifest);
                RefreshManifestBindings(fixture, "windows-x64", manifestPath);

                var result = Validate(fixture);

                Assert.False(result.Passed);
                Assert.NotEmpty(result.Failures);
            });
        }
    }

    [Fact]
    public void Duplicate_oversized_unsafe_tampered_and_linked_inputs_fail_closed()
    {
        WithFixture(fixture =>
        {
            var text = File.ReadAllText(fixture.RecordPath, Encoding.UTF8);
            File.WriteAllText(
                fixture.RecordPath,
                text.Replace(
                    "\"schemaVersion\": 1,",
                    "\"schemaVersion\": 1,\n  \"schemaVersion\": 1,",
                    StringComparison.Ordinal),
                new UTF8Encoding(false));
            Assert.Contains(
                Validate(fixture).Failures,
                failure => failure.Contains("repeats JSON field", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            File.WriteAllText(
                fixture.RecordPath,
                new string('x', 4 * 1024 * 1024 + 1),
                new UTF8Encoding(false));
            Assert.Contains(
                Validate(fixture).Failures,
                failure => failure.Contains("byte validation limit", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            var record = LoadObject(fixture.RecordPath);
            record["migrationFixturePaths"] = StringArray("../escape.json");
            WriteJson(fixture.RecordPath, record);
            Assert.False(Validate(fixture).Passed);
        });

        WithFixture(fixture =>
        {
            var retained = Path.Combine(fixture.EvidenceRoot, "evidence", "windows-x64.txt");
            File.AppendAllText(retained, "tampered", Encoding.UTF8);
            Assert.Contains(
                Validate(fixture).Failures,
                failure => failure.Contains("hash mismatch", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            var retained = Path.Combine(fixture.EvidenceRoot, "evidence", "windows-x64.txt");
            var target = Path.Combine(fixture.RepositoryRoot, "outside.txt");
            File.WriteAllText(target, "outside", new UTF8Encoding(false));
            File.Delete(retained);
            try
            {
                File.CreateSymbolicLink(retained, target);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or PlatformNotSupportedException)
            {
                return;
            }
            Assert.Contains(
                Validate(fixture).Failures,
                failure => failure.Contains("link", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Output_must_stay_in_its_route_root_and_cannot_alias_inputs()
    {
        WithFixture(fixture =>
        {
            var outsideFoundation = Path.Combine(fixture.EvidenceRoot, "foundation.json");
            var foundation = ReleaseRehearsalCheck.WriteFoundationHandoff(
                fixture.RepositoryRoot,
                outsideFoundation);
            Assert.False(foundation.Passed);
            Assert.False(File.Exists(outsideFoundation));

            var original = File.ReadAllBytes(fixture.RecordPath);
            var alias = ReleaseRehearsalCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                fixture.RecordPath);
            Assert.False(alias.Passed);
            Assert.Contains(alias.Failures, failure => failure.Contains("cannot alias", StringComparison.Ordinal));
            Assert.Equal(original, File.ReadAllBytes(fixture.RecordPath));

            var nestedEvidence = Path.Combine(
                fixture.EvidenceRoot,
                "materials",
                "gates",
                "visible-image-review.json");
            var nestedOriginal = File.ReadAllBytes(nestedEvidence);
            var nestedAlias = ReleaseRehearsalCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                nestedEvidence);
            Assert.False(nestedAlias.Passed);
            Assert.Contains(
                nestedAlias.Failures,
                failure => failure.Contains("cannot alias", StringComparison.Ordinal));
            Assert.Equal(nestedOriginal, File.ReadAllBytes(nestedEvidence));

            var repositoryOutput = Path.Combine(fixture.RepositoryRoot, "record-decision.json");
            var escaped = ReleaseRehearsalCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                Revision,
                repositoryOutput);
            Assert.False(escaped.Passed);
            Assert.False(File.Exists(repositoryOutput));
        });
    }

    [Fact]
    public void Missing_or_drifted_foundation_authorities_fail_closed()
    {
        WithFixture(fixture =>
        {
            File.Delete(Path.Combine(fixture.RepositoryRoot, "docs", "release", "SIGNING.md"));
            var result = ReleaseRehearsalCheck.Inspect(fixture.RepositoryRoot);
            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains("SIGNING.md", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            var path = Path.Combine(
                fixture.RepositoryRoot,
                "config",
                "release_materials_acceptance_v1.json");
            var contract = LoadObject(path);
            contract["gateIds"]![0] = "invented-gate";
            WriteJson(path, contract);
            var result = ReleaseRehearsalCheck.Inspect(fixture.RepositoryRoot);
            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains("gateIds", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Foundation_authority_documents_reject_malformed_shapes_types_and_encoding()
    {
        var mutations = new Action<Fixture>[]
        {
            fixture => WriteBytes(
                fixture.RepositoryRoot,
                "config/release_rehearsal_v1.json",
                StrictBytes("[]\n")),
            fixture => MutateRepositoryJson(
                fixture,
                "config/release_rehearsal_v1.json",
                value => value.Remove("status")),
            fixture => MutateRepositoryJson(
                fixture,
                "config/release_rehearsal_v1.json",
                value => value["schemaVersion"] = 2),
            fixture => MutateRepositoryJson(
                fixture,
                "config/release_rehearsal_v1.json",
                value => value["schemaVersion"] = JsonNode.Parse("1e100")),
            fixture => MutateRepositoryJson(
                fixture,
                "config/release_rehearsal_v1.json",
                value => value["artifactPlatforms"] = "windows-x64"),
            fixture => MutateRepositoryJson(
                fixture,
                "config/release_rehearsal_v1.json",
                value => value["artifactPlatforms"] = StringArray("windows-x64")),
            fixture => MutateRepositoryJson(
                fixture,
                "config/release_rehearsal_v1.json",
                value => value["artifactPlatforms"] = new JsonArray(1, "macos-universal", "linux-x64")),
            fixture => WriteBytes(
                fixture.RepositoryRoot,
                "config/release_materials_acceptance_v1.json",
                StrictBytes("false\n")),
            fixture => MutateRepositoryJson(
                fixture,
                "config/release_materials_acceptance_v1.json",
                value => value.Remove("acceptedDecisionKind")),
            fixture => MutateRepositoryJson(
                fixture,
                "config/release_materials_acceptance_v1.json",
                value => value["sourceStructuralHandoffKind"] = 7),
            fixture => MutateRepositoryJson(
                fixture,
                "config/release_materials_acceptance_v1.json",
                value => value["resultValues"] = StringArray("fail")),
            fixture => WriteBytes(
                fixture.RepositoryRoot,
                "config/release_rehearsal_v1.json",
                [0xc3, 0x28]),
            fixture => WriteBytes(
                fixture.RepositoryRoot,
                "config/release_rehearsal_v1.json",
                StrictBytes(new string('[', 65) + new string(']', 65))),
            fixture => File.WriteAllBytes(
                Path.Combine(fixture.RepositoryRoot, "docs", "release", "PACKAGING.md"),
                []),
            fixture => ReplaceFileWithDirectory(
                fixture.RepositoryRoot,
                "config/release_signing_policy.json"),
        };

        foreach (var mutation in mutations)
        {
            WithFixture(fixture =>
            {
                mutation(fixture);
                var result = ReleaseRehearsalCheck.Inspect(fixture.RepositoryRoot);
                Assert.False(result.Passed);
                Assert.NotEmpty(result.Failures);
            });
        }
    }

    [Fact]
    public void Record_parsing_maps_scalars_versions_and_diagnostics_fail_closed()
    {
        var rawRecords = new byte[][]
        {
            StrictBytes("[]\n"),
            [0xc3, 0x28],
            StrictBytes(new string('[', 65) + new string(']', 65)),
        };
        foreach (var bytes in rawRecords)
        {
            WithFixture(fixture =>
            {
                File.WriteAllBytes(fixture.RecordPath, bytes);
                Assert.False(Validate(fixture).Passed);
            });
        }

        WithFixture(fixture =>
        {
            var text = File.ReadAllText(fixture.RecordPath, Encoding.UTF8);
            File.WriteAllText(
                fixture.RecordPath,
                text.Replace(
                    "\"download\": \"pass\",",
                    "\"download\": \"pass\",\n          \"download\": \"pass\",",
                    StringComparison.Ordinal),
                new UTF8Encoding(false));
            Assert.Contains(
                Validate(fixture).Failures,
                failure => failure.Contains("repeats JSON field", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            var record = LoadObject(fixture.RecordPath);
            record["candidateArtifactSha256ByPlatform"] = new JsonArray();
            record["candidateArtifactPathsByPlatform"] = true;
            record["previousArtifactSha256ByPlatform"]!["windows-x64"] = 7;
            record["previousArtifactPathsByPlatform"]!["windows-x64"] = 7;
            record["candidateManifestSha256ByPlatform"]!["windows-x64"] = new string('A', 64);
            record["candidateManifestPathsByPlatform"]!.AsObject().Remove("linux-x64");
            record["releaseMaterialsDecisionSha256"] = false;
            record["releaseMaterialsDecisionPath"] = false;
            record["migrationFixtureSetSha256"] = false;
            record["migrationFixturePaths"] = new JsonArray();
            record["platformResults"] = new JsonObject();
            record["withdrawalResult"] = new JsonArray();
            record["authorityRecords"] = new JsonObject();
            record["retainedFileSha256"] = new JsonArray();
            WriteJson(fixture.RecordPath, record);

            var result = Validate(fixture);

            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains("must be an object", StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.Contains("lowercase hexadecimal", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            var record = LoadObject(fixture.RecordPath);
            record["rehearsalId"] = 1;
            record["sourceRevision"] = "";
            record["appVersion"] = "e\u0301";
            record["previousVersion"] = "not-semver";
            record["stagedLocationId"] = new string('x', 4097);
            record["executedUtc"] = 1;
            WriteJson(fixture.RecordPath, record);

            var result = Validate(fixture);

            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains("nonempty NFC", StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.Contains("UTC timestamp", StringComparison.Ordinal));
        });

        foreach (var previousVersion in new[]
        {
            "2147483648.0.0",
            "0.2147483648.0",
            "0.0.2147483648",
            "0.3.0-alpha.2147483648",
            "0.3.0-alpha.1",
            "0.3.0-beta.1",
            "0.3.0-rc.1",
            "0.3.0",
        })
        {
            WithFixture(fixture =>
            {
                var record = LoadObject(fixture.RecordPath);
                record["previousVersion"] = previousVersion;
                WriteJson(fixture.RecordPath, record);
                Assert.False(Validate(fixture).Passed);
            });
        }

        WithFixture(fixture =>
        {
            var result = ReleaseRehearsalCheck.WriteRecordHandoff(
                fixture.RepositoryRoot,
                fixture.RecordPath,
                null!,
                Path.Combine(fixture.EvidenceRoot, "null-revision.json"));
            Assert.False(result.Passed);
        });

        WithFixture(fixture =>
        {
            var record = LoadObject(fixture.RecordPath);
            record[new string('x', 400)] = true;
            WriteJson(fixture.RecordPath, record);
            var result = Validate(fixture);
            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.EndsWith("...", StringComparison.Ordinal));
            Assert.All(result.Failures, failure => Assert.True(failure.EnumerateRunes().Count() <= 259));
        });

        WithFixture(fixture =>
        {
            var record = LoadObject(fixture.RecordPath);
            record["migrationFixtureSetSha256"] = 1;
            record["migrationFixturePaths"] = StringArray(
                Enumerable.Range(0, 140).Select(index => $"missing/{index:D3}.json").ToArray());
            WriteJson(fixture.RecordPath, record);
            var result = Validate(fixture);
            Assert.False(result.Passed);
            Assert.Equal(128, result.Failures.Count);
            Assert.Equal(
                "Additional validation failures were omitted at the diagnostic limit.",
                result.Failures[^1]);
        });
    }

    [Fact]
    public void Portable_paths_and_retained_files_reject_every_unsafe_path_family()
    {
        WithFixture(fixture =>
        {
            var record = LoadObject(fixture.RecordPath);
            record["migrationFixtureSetSha256"] = 1;
            record["migrationFixturePaths"] = new JsonArray(
                JsonValue.Create(7),
                JsonValue.Create(""),
                JsonValue.Create("   "),
                JsonValue.Create("e\u0301/file"),
                JsonValue.Create(new string('x', 513)),
                JsonValue.Create("/absolute"),
                JsonValue.Create("trailing/"),
                JsonValue.Create("back\\slash"),
                JsonValue.Create("drive:c"),
                JsonValue.Create("wild*card"),
                JsonValue.Create("control\u0001char"),
                JsonValue.Create("a//b"),
                JsonValue.Create("a/./b"),
                JsonValue.Create("a/../b"),
                JsonValue.Create("trailing-space "),
                JsonValue.Create("trailing-dot."),
                JsonValue.Create("CON"),
                JsonValue.Create("prn.txt"),
                JsonValue.Create("AUX"),
                JsonValue.Create("nul.bin"),
                JsonValue.Create("CLOCK$"),
                JsonValue.Create("COM1.txt"),
                JsonValue.Create("lpt9"));
            WriteJson(fixture.RecordPath, record);

            var result = Validate(fixture);

            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains("safe relative", StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.Contains("unique safe", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            var record = LoadObject(fixture.RecordPath);
            record["migrationFixtureSetSha256"] = 1;
            record["migrationFixturePaths"] = StringArray(
                Enumerable.Range(0, 257).Select(index => $"fixtures/{index:D3}.json").ToArray());
            WriteJson(fixture.RecordPath, record);
            Assert.Contains(
                Validate(fixture).Failures,
                failure => failure.Contains("1 to 256", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            WriteFile(fixture.EvidenceRoot, "fixtures/Case.json", "first");
            WriteFile(fixture.EvidenceRoot, "fixtures/case.json", "second");
            var record = LoadObject(fixture.RecordPath);
            record["migrationFixtureSetSha256"] = 1;
            record["migrationFixturePaths"] = StringArray("fixtures/Case.json", "fixtures/case.json");
            WriteJson(fixture.RecordPath, record);
            Assert.Contains(
                Validate(fixture).Failures,
                failure => failure.Contains("collide by portable case", StringComparison.Ordinal));
        });

        WithFixture(fixture =>
        {
            WriteBytes(fixture.EvidenceRoot, "fixtures/empty.json", []);
            Directory.CreateDirectory(Path.Combine(fixture.EvidenceRoot, "fixtures", "directory.json"));
            var record = LoadObject(fixture.RecordPath);
            record["migrationFixtureSetSha256"] = 1;
            record["migrationFixturePaths"] = StringArray(
                "fixtures/empty.json",
                "fixtures/directory.json",
                "fixtures/missing.json");
            WriteJson(fixture.RecordPath, record);
            var failures = Validate(fixture).Failures;
            Assert.Contains(failures, failure => failure.Contains("nonempty", StringComparison.Ordinal));
            Assert.Contains(failures, failure => failure.Contains("missing retained file", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Manifest_arrays_entries_optional_fields_and_identity_are_strict()
    {
        WithFixture(fixture =>
        {
            WriteManifestAndRefresh(fixture, new JsonArray());
            Assert.False(Validate(fixture).Passed);
        });

        var mutations = new Action<JsonObject>[]
        {
            manifest => manifest.Remove("product"),
            manifest =>
            {
                manifest["schemaVersion"] = JsonNode.Parse("1e100");
                manifest["product"] = 1;
                manifest["godotVersion"] = 1;
                manifest["godotCommit"] = "";
                manifest["dotnetSdk"] = "e\u0301";
                manifest["godotArchiveSha512"] = new string('A', 128);
                manifest["godotExecutableSha256"] = 1;
                manifest["smokeStateHash"] = "short";
                manifest["fileCount"] = -1;
                manifest["totalBytes"] = JsonNode.Parse("1e100");
            },
            manifest =>
            {
                manifest["files"] = 1;
                manifest["containerEntries"] = 1;
            },
            manifest => manifest["files"] = new JsonArray(),
            manifest => manifest["containerEntries"] = new JsonArray(
                Enumerable.Range(0, 4097).Select(index => (JsonNode?)JsonValue.Create(index)).ToArray()),
            manifest => manifest["files"] = new JsonArray(
                JsonValue.Create(1),
                new JsonObject { ["path"] = "missing.bin", ["bytes"] = 1 },
                new JsonObject
                {
                    ["path"] = "extra.bin",
                    ["bytes"] = 1,
                    ["sha256"] = new string('a', 64),
                    ["unexpected"] = true,
                },
                new JsonObject
                {
                    ["path"] = "dup.bin",
                    ["bytes"] = 1,
                    ["sha256"] = new string('a', 64),
                },
                new JsonObject
                {
                    ["path"] = "DUP.bin",
                    ["bytes"] = "one",
                    ["sha256"] = 1,
                    ["compressedBytes"] = -1,
                }),
            manifest =>
            {
                manifest["godotVersion"] = "4.8.0";
                manifest["godotCommit"] = "different";
                manifest["dotnetSdk"] = "11.0.100";
                manifest["smokeStateHash"] = "1111111111111111";
            },
        };

        foreach (var mutation in mutations)
        {
            WithFixture(fixture =>
            {
                var path = WindowsManifestPath(fixture);
                var manifest = LoadObject(path);
                mutation(manifest);
                WriteManifestAndRefresh(fixture, manifest);
                Assert.False(Validate(fixture).Passed);
            });
        }
    }

    [Fact]
    public void Material_decision_gate_and_structural_shapes_fail_closed()
    {
        var rawDecisions = new byte[][]
        {
            StrictBytes("[]\n"),
            [0xc3, 0x28],
            StrictBytes(new string('[', 65) + new string(']', 65)),
        };
        foreach (var bytes in rawDecisions)
        {
            WithFixture(fixture =>
            {
                File.WriteAllBytes(fixture.MaterialDecisionPath, bytes);
                RefreshRecordDecisionBinding(fixture);
                Assert.False(Validate(fixture).Passed);
            });
        }

        WithFixture(fixture =>
        {
            var decision = LoadObject(fixture.MaterialDecisionPath);
            decision.Remove("passed");
            SaveDecisionAndRefreshRecord(fixture, decision);
            Assert.False(Validate(fixture).Passed);
        });

        WithFixture(fixture =>
        {
            var decision = LoadObject(fixture.MaterialDecisionPath);
            decision["schemaVersion"] = JsonNode.Parse("1e100");
            decision["kind"] = 1;
            decision["passed"] = 1;
            decision["foundationQualified"] = false;
            decision["candidateSha256"] = 1;
            decision["structuralHandoffPath"] = 1;
            decision["structuralHandoffSha256"] = new string('A', 64);
            decision["artifactManifestSha256ByPlatform"] = new JsonArray();
            decision["acceptedUtc"] = 1;
            decision["gateRecords"] = new JsonObject();
            decision["retainedFileSha256"] = new JsonArray();
            decision["errors"] = StringArray("error");
            SaveDecisionAndRefreshRecord(fixture, decision);
            Assert.False(Validate(fixture).Passed);
        });

        WithFixture(fixture =>
        {
            var decision = LoadObject(fixture.MaterialDecisionPath);
            decision["gateRecords"] = new JsonArray(
                JsonValue.Create(1),
                new JsonObject
                {
                    ["gateId"] = MaterialGateIds[1],
                    ["result"] = "pass",
                    ["authorityRoleId"] = 1,
                    ["evidencePaths"] = new JsonArray(),
                },
                new JsonObject
                {
                    ["gateId"] = MaterialGateIds[2],
                    ["result"] = "pass",
                    ["authorityRoleId"] = "x",
                    ["evidencePaths"] = StringArray("gates/visible-image-review.json", "gates/visible-image-review.json"),
                },
                new JsonObject
                {
                    ["gateId"] = MaterialGateIds[3],
                    ["result"] = "pass",
                    ["authorityRoleId"] = "release-video-role",
                    ["evidencePaths"] = StringArray(
                        Enumerable.Range(0, 17).Select(index => $"gates/video-{index}.json").ToArray()),
                });
            SaveDecisionAndRefreshRecord(fixture, decision);
            Assert.False(Validate(fixture).Passed);
        });

        WithFixture(fixture =>
        {
            WriteStructuralAndRefresh(fixture, new JsonArray());
            Assert.False(Validate(fixture).Passed);
        });

        WithFixture(fixture =>
        {
            var structural = LoadObject(Path.Combine(fixture.EvidenceRoot, "materials", "structural.json"));
            structural["schemaVersion"] = JsonNode.Parse("1e100");
            structural["kind"] = 1;
            structural["passed"] = 1;
            structural["documentSha256"] = new JsonArray();
            structural["requiredDocumentCount"] = JsonNode.Parse("1e100");
            structural["sourceRevision"] = 1;
            structural["candidateSha256"] = new string('A', 64);
            structural["pendingGates"] = StringArray("wrong");
            structural["errors"] = StringArray("error");
            WriteStructuralAndRefresh(fixture, structural);
            Assert.False(Validate(fixture).Passed);
        });
    }

    [Fact]
    public void Missing_empty_and_non_file_roots_records_and_outputs_fail_closed()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.False(ReleaseRehearsalCheck.Inspect(missingRoot).Passed);

        WithFixture(fixture =>
        {
            Assert.False(ReleaseRehearsalCheck.Inspect(null!).Passed);
            Assert.False(ReleaseRehearsalCheck.WriteFoundationHandoff(
                fixture.RepositoryRoot,
                " ").Passed);

            var outputDirectory = Path.Combine(fixture.RepositoryRoot, "output-directory");
            Directory.CreateDirectory(outputDirectory);
            Assert.False(ReleaseRehearsalCheck.WriteFoundationHandoff(
                fixture.RepositoryRoot,
                outputDirectory).Passed);

            var parentFile = Path.Combine(fixture.RepositoryRoot, "output-parent");
            File.WriteAllText(parentFile, "file", new UTF8Encoding(false));
            Assert.False(ReleaseRehearsalCheck.WriteFoundationHandoff(
                fixture.RepositoryRoot,
                Path.Combine(parentFile, "result.json")).Passed);
        });

        WithFixture(fixture =>
        {
            File.Delete(fixture.RecordPath);
            Assert.False(Validate(fixture).Passed);
        });

        WithFixture(fixture =>
        {
            File.WriteAllBytes(fixture.RecordPath, []);
            Assert.False(Validate(fixture).Passed);
        });
    }

    private static void MutateRepositoryJson(
        Fixture fixture,
        string relativePath,
        Action<JsonObject> mutation)
    {
        var path = Path.Combine(fixture.RepositoryRoot, Portable(relativePath));
        var value = LoadObject(path);
        mutation(value);
        WriteJson(path, value);
    }

    private static void ReplaceFileWithDirectory(string root, string relativePath)
    {
        var path = Path.Combine(root, Portable(relativePath));
        File.Delete(path);
        Directory.CreateDirectory(path);
    }

    private static string WindowsManifestPath(Fixture fixture) =>
        Path.Combine(
            fixture.EvidenceRoot,
            "staged",
            "windows-x64",
            "artifact-manifest.json");

    private static void WriteManifestAndRefresh(Fixture fixture, JsonNode manifest)
    {
        var path = WindowsManifestPath(fixture);
        WriteJson(path, manifest);
        RefreshManifestBindings(fixture, "windows-x64", path);
    }

    private static void RefreshRecordDecisionBinding(Fixture fixture)
    {
        var record = LoadObject(fixture.RecordPath);
        var digest = Hash(fixture.MaterialDecisionPath);
        record["releaseMaterialsDecisionSha256"] = digest;
        record["retainedFileSha256"]!["materials/acceptance.json"] = digest;
        WriteJson(fixture.RecordPath, record);
    }

    private static void WriteStructuralAndRefresh(Fixture fixture, JsonNode structural)
    {
        var path = Path.Combine(fixture.EvidenceRoot, "materials", "structural.json");
        WriteJson(path, structural);
        var decision = LoadObject(fixture.MaterialDecisionPath);
        var digest = Hash(path);
        decision["structuralHandoffSha256"] = digest;
        decision["retainedFileSha256"]!["structural.json"] = digest;
        SaveDecisionAndRefreshRecord(fixture, decision);
    }

    private static RepositoryCheckResult Validate(Fixture fixture) =>
        ReleaseRehearsalCheck.WriteRecordHandoff(
            fixture.RepositoryRoot,
            fixture.RecordPath,
            Revision,
            Path.Combine(fixture.EvidenceRoot, "result.json"));

    private static void WithFixture(Action<Fixture> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-rehearsal-tests", Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repository");
        var evidence = Path.Combine(root, "retained");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(evidence);
        try
        {
            WriteRepositoryFixture(repository);
            var fixture = WriteRecordFixture(repository, evidence);
            action(fixture);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteRepositoryFixture(string root)
    {
        var sourceRoot = ResolveRepositoryRoot();
        CopyFile(sourceRoot, root, "config/release_rehearsal_v1.json");
        CopyFile(sourceRoot, root, "config/release_materials_acceptance_v1.json");
        WriteFile(root, "VERSION", "0.3.0-alpha.1\n");
        WriteFile(root, "config/release_materials_v1.json", "{}\n");
        WriteFile(root, "config/release_signing_policy.json", "{}\n");
        WriteFile(root, "docs/release/PACKAGING.md", "packaging authority\n");
        WriteFile(root, "docs/release/SIGNING.md", "signing authority\n");
        WriteFile(root, "docs/guides/RECOVERY.md", "recovery authority\n");
    }

    private static Fixture WriteRecordFixture(string repositoryRoot, string evidenceRoot)
    {
        var recordPath = Path.Combine(evidenceRoot, "record.json");
        var candidatePaths = new JsonObject();
        var candidateHashes = new JsonObject();
        var previousPaths = new JsonObject();
        var previousHashes = new JsonObject();
        var manifestPaths = new JsonObject();
        var manifestHashes = new JsonObject();
        var retained = new HashSet<string>(StringComparer.Ordinal);

        foreach (var platform in Platforms)
        {
            var candidate = $"staged/{platform}/candidate.bin";
            var previous = $"previous/{platform}/previous.bin";
            var manifest = $"staged/{platform}/artifact-manifest.json";
            WriteBytes(evidenceRoot, candidate, StrictBytes($"candidate-{platform}"));
            WriteBytes(evidenceRoot, previous, StrictBytes($"previous-{platform}"));
            WriteJson(Path.Combine(evidenceRoot, Portable(manifest)), Manifest(platform));
            candidatePaths[platform] = candidate;
            candidateHashes[platform] = Hash(Path.Combine(evidenceRoot, Portable(candidate)));
            previousPaths[platform] = previous;
            previousHashes[platform] = Hash(Path.Combine(evidenceRoot, Portable(previous)));
            manifestPaths[platform] = manifest;
            manifestHashes[platform] = Hash(Path.Combine(evidenceRoot, Portable(manifest)));
            retained.UnionWith([candidate, previous, manifest]);
        }

        var materialDirectory = Path.Combine(evidenceRoot, "materials");
        Directory.CreateDirectory(materialDirectory);
        var structuralPath = Path.Combine(materialDirectory, "structural.json");
        WriteJson(structuralPath, StructuralMaterialsHandoff());
        var gates = new JsonArray();
        foreach (var gate in MaterialGateIds)
        {
            var relative = $"gates/{gate}.json";
            WriteBytes(materialDirectory, relative, StrictBytes($"approved-{gate}"));
            gates.Add(new JsonObject
            {
                ["gateId"] = gate,
                ["result"] = "pass",
                ["authorityRoleId"] = $"release-{gate}-role",
                ["evidencePaths"] = StringArray(relative),
            });
        }
        var decision = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "release-materials-acceptance-v1",
            ["passed"] = true,
            ["foundationQualified"] = true,
            ["candidateMaterialComplete"] = true,
            ["releaseAcceptance"] = true,
            ["sourceRevision"] = Revision,
            ["appVersion"] = "0.3.0-alpha.1",
            ["candidateSha256"] = new string('c', 64),
            ["structuralHandoffPath"] = "structural.json",
            ["structuralHandoffSha256"] = Hash(structuralPath),
            ["artifactManifestSha256ByPlatform"] = manifestHashes.DeepClone(),
            ["acceptedUtc"] = "2026-08-09T17:00:00Z",
            ["gateRecords"] = gates,
            ["retainedFileSha256"] = new JsonObject(),
            ["pendingGates"] = new JsonArray(),
            ["errors"] = new JsonArray(),
        };
        var decisionPath = Path.Combine(materialDirectory, "acceptance.json");
        var fixture = new Fixture(repositoryRoot, evidenceRoot, recordPath, decisionPath);
        RefreshDecisionRetained(fixture, decision);
        WriteJson(decisionPath, decision);
        const string decisionRelative = "materials/acceptance.json";
        retained.Add(decisionRelative);

        var fixturePaths = new[] { "fixtures/preferences-v5.json", "fixtures/personal-best-v1.json" };
        foreach (var path in fixturePaths)
        {
            WriteBytes(evidenceRoot, path, StrictBytes(path));
            retained.Add(path);
        }

        var platformResults = new JsonArray();
        foreach (var platform in Platforms)
        {
            var evidence = $"evidence/{platform}.txt";
            WriteBytes(evidenceRoot, evidence, StrictBytes($"operations-{platform}"));
            retained.Add(evidence);
            var operationResults = new JsonObject();
            var evidenceByOperation = new JsonObject();
            foreach (var operation in Operations)
            {
                operationResults[operation] = "pass";
                evidenceByOperation[operation] = StringArray(evidence);
            }
            var protectedHash = HashBytes(StrictBytes($"protected-{platform}"));
            platformResults.Add(new JsonObject
            {
                ["platformId"] = platform,
                ["operationResults"] = operationResults,
                ["evidencePathsByOperation"] = evidenceByOperation,
                ["protectedUserDataSha256Before"] = protectedHash,
                ["protectedUserDataSha256After"] = protectedHash,
            });
        }

        const string withdrawalEvidence = "evidence/withdrawal.txt";
        WriteBytes(evidenceRoot, withdrawalEvidence, StrictBytes("withdrawal"));
        retained.Add(withdrawalEvidence);
        var authorities = new JsonArray();
        foreach (var authority in AuthorityIds)
        {
            var evidence = $"evidence/authority-{authority}.txt";
            WriteBytes(evidenceRoot, evidence, StrictBytes(authority));
            retained.Add(evidence);
            authorities.Add(new JsonObject
            {
                ["operationId"] = authority,
                ["roleId"] = $"release-{authority}-role",
                ["authorizationVerified"] = true,
                ["evidencePaths"] = StringArray(evidence),
            });
        }

        var record = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-release-rehearsal-record-v1",
            ["rehearsalId"] = "candidate-rehearsal-001",
            ["sourceRevision"] = Revision,
            ["appVersion"] = "0.3.0-alpha.1",
            ["previousVersion"] = "0.2.1",
            ["stagedLocationId"] = "controlled-stage-001",
            ["executedUtc"] = "2026-08-09T18:00:00Z",
            ["candidateArtifactSha256ByPlatform"] = candidateHashes,
            ["candidateArtifactPathsByPlatform"] = candidatePaths,
            ["previousArtifactSha256ByPlatform"] = previousHashes,
            ["previousArtifactPathsByPlatform"] = previousPaths,
            ["candidateManifestSha256ByPlatform"] = manifestHashes,
            ["candidateManifestPathsByPlatform"] = manifestPaths,
            ["releaseMaterialsDecisionSha256"] = Hash(decisionPath),
            ["releaseMaterialsDecisionPath"] = decisionRelative,
            ["migrationFixtureSetSha256"] = FixtureSetHash(evidenceRoot, fixturePaths),
            ["migrationFixturePaths"] = StringArray(fixturePaths),
            ["platformResults"] = platformResults,
            ["withdrawalResult"] = new JsonObject
            {
                ["candidateUnavailable"] = true,
                ["previousArtifactRestored"] = true,
                ["userDataPreserved"] = true,
                ["communicationPrepared"] = true,
                ["evidencePaths"] = StringArray(withdrawalEvidence),
            },
            ["authorityRecords"] = authorities,
            ["retainedFileSha256"] = HashMap(evidenceRoot, retained),
        };
        WriteJson(recordPath, record);
        return fixture;
    }

    private static JsonObject Manifest(string platform)
    {
        var payloadHash = HashBytes([1]);
        return new JsonObject
        {
            ["schemaVersion"] = 3,
            ["product"] = "Vibe Snake",
            ["platform"] = platform,
            ["buildMode"] = "Release",
            ["sourceRevision"] = Revision,
            ["godotVersion"] = "4.7.1",
            ["godotCommit"] = "a13da4feb",
            ["godotArchiveSha512"] = new string('a', 128),
            ["godotExecutableSha256"] = new string('b', 64),
            ["dotnetSdk"] = "10.0.100",
            ["smokeStateHash"] = "600f29e8919a9400",
            ["agentArenaPreviewExcluded"] = true,
            ["fileCount"] = 1,
            ["totalBytes"] = 1,
            ["files"] = new JsonArray(new JsonObject
            {
                ["path"] = "VibeSnake.bin",
                ["bytes"] = 1,
                ["sha256"] = payloadHash,
                ["compressedBytes"] = 1,
            }),
            ["containerEntries"] = new JsonArray(),
        };
    }

    private static JsonObject StructuralMaterialsHandoff()
    {
        var documents = new JsonObject();
        foreach (var document in MaterialDocuments)
        {
            documents[document] = new string('d', 64);
        }
        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["kind"] = "release-materials-handoff-v2",
            ["passed"] = true,
            ["foundationQualified"] = true,
            ["contractSha256"] = new string('a', 64),
            ["documentSha256"] = documents,
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
            ["appVersion"] = "0.3.0-alpha.1",
            ["candidateSha256"] = new string('c', 64),
            ["pendingGates"] = StringArray(MaterialGateIds),
            ["errors"] = new JsonArray(),
        };
    }

    private static void RefreshManifestBindings(Fixture fixture, string platform, string manifestPath)
    {
        var record = LoadObject(fixture.RecordPath);
        var manifestSha = Hash(manifestPath);
        record["candidateManifestSha256ByPlatform"]![platform] = manifestSha;
        var decision = LoadObject(fixture.MaterialDecisionPath);
        decision["artifactManifestSha256ByPlatform"]![platform] = manifestSha;
        SaveDecisionAndRefreshRecord(fixture, decision, record);
    }

    private static void SaveDecisionAndRefreshRecord(
        Fixture fixture,
        JsonObject decision,
        JsonObject? record = null)
    {
        WriteJson(fixture.MaterialDecisionPath, decision);
        record ??= LoadObject(fixture.RecordPath);
        record["releaseMaterialsDecisionSha256"] = Hash(fixture.MaterialDecisionPath);
        RefreshRecordRetained(fixture, record);
        WriteJson(fixture.RecordPath, record);
    }

    private static void RefreshDecisionRetained(Fixture fixture, JsonObject decision)
    {
        var relativePaths = new HashSet<string>(StringComparer.Ordinal)
        {
            decision["structuralHandoffPath"]!.GetValue<string>(),
        };
        foreach (var gate in decision["gateRecords"]!.AsArray())
        {
            foreach (var path in gate!["evidencePaths"]!.AsArray())
            {
                relativePaths.Add(path!.GetValue<string>());
            }
        }
        decision["retainedFileSha256"] = HashMap(
            Path.GetDirectoryName(fixture.MaterialDecisionPath)!,
            relativePaths);
    }

    private static void RefreshRecordRetained(Fixture fixture, JsonObject record)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in new[]
        {
            "candidateArtifactPathsByPlatform",
            "previousArtifactPathsByPlatform",
            "candidateManifestPathsByPlatform",
        })
        {
            foreach (var property in record[field]!.AsObject())
            {
                paths.Add(property.Value!.GetValue<string>());
            }
        }
        paths.Add(record["releaseMaterialsDecisionPath"]!.GetValue<string>());
        foreach (var path in record["migrationFixturePaths"]!.AsArray())
        {
            paths.Add(path!.GetValue<string>());
        }
        foreach (var row in record["platformResults"]!.AsArray())
        {
            foreach (var operation in row!["evidencePathsByOperation"]!.AsObject())
            {
                foreach (var path in operation.Value!.AsArray())
                {
                    paths.Add(path!.GetValue<string>());
                }
            }
        }
        foreach (var path in record["withdrawalResult"]!["evidencePaths"]!.AsArray())
        {
            paths.Add(path!.GetValue<string>());
        }
        foreach (var row in record["authorityRecords"]!.AsArray())
        {
            foreach (var path in row!["evidencePaths"]!.AsArray())
            {
                paths.Add(path!.GetValue<string>());
            }
        }
        record["retainedFileSha256"] = HashMap(fixture.EvidenceRoot, paths);
    }

    private static JsonObject HashMap(string root, IEnumerable<string> paths)
    {
        var result = new JsonObject();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            result[path] = Hash(Path.Combine(root, Portable(path)));
        }
        return result;
    }

    private static string FixtureSetHash(string root, IEnumerable<string> paths)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            hash.AppendData(StrictBytes(path));
            hash.AppendData([0]);
            hash.AppendData(Convert.FromHexString(Hash(Path.Combine(root, Portable(path)))));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static JsonArray StringArray(params string[] values) =>
        new(values
            .Select(value => (JsonNode?)JsonValue.Create(value))
            .ToArray());

    private static JsonObject LoadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))!.AsObject();

    private static void WriteJson(string path, JsonNode value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    private static void WriteFile(string root, string relativePath, string value) =>
        WriteBytes(root, relativePath, StrictBytes(value));

    private static void WriteBytes(string root, string relativePath, byte[] value)
    {
        var path = Path.Combine(root, Portable(relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, value);
    }

    private static void CopyFile(string sourceRoot, string targetRoot, string relativePath)
    {
        var target = Path.Combine(targetRoot, Portable(relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Path.Combine(sourceRoot, Portable(relativePath)), target);
    }

    private static string Hash(string path) => HashBytes(File.ReadAllBytes(path));

    private static string HashBytes(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    private static byte[] StrictBytes(string value) => new UTF8Encoding(false).GetBytes(value);

    private static string Portable(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

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
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record Fixture(
        string RepositoryRoot,
        string EvidenceRoot,
        string RecordPath,
        string MaterialDecisionPath);
}
