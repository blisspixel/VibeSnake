using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class RepositoryChecksTests
{
    private static readonly int[] EmojiPolicyCodePoints =
        [0x1F1E6, 0x1F300, 0x1FAFF, 0x200D, 0x20E3, 0xFE0F];

    public static TheoryData<string, string> CanonicalVersionMappings => new()
    {
        { "0.3.0-alpha.1", "0.3.0a1" },
        { "1.2.3-beta.4", "1.2.3b4" },
        { "2.0.0-rc.5", "2.0.0rc5" },
        { "1.0.0", "1.0.0" },
    };

    public static TheoryData<string> InvalidCanonicalVersions => new()
    {
        "01.0.0",
        "1.0",
        "1.0.0-alpha.0",
        "1.0.0-preview.1",
        "1.0.0+local",
        "../1.0.0",
    };

    public static TheoryData<string> InvalidVersionFiles => new()
    {
        "0.3.0a1\n",
        " 0.3.0-alpha.1\n",
        "0.3.0-alpha.1 \n",
        "0.3.0-alpha.1\n\n",
        "0.3.0-alpha.1\r\n",
        "0.3.0-alpha.1",
    };

    [Theory]
    [MemberData(nameof(CanonicalVersionMappings))]
    public void Canonical_product_versions_map_to_package_versions(
        string productVersion,
        string packageVersion)
    {
        Assert.Equal(packageVersion, ProductVersionCheck.MapPackageVersion(productVersion));
    }

    [Theory]
    [MemberData(nameof(InvalidCanonicalVersions))]
    public void Noncanonical_product_versions_are_rejected(string version)
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => ProductVersionCheck.MapPackageVersion(version));

        Assert.Contains("Unsupported canonical product version", exception.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidVersionFiles))]
    public void Version_file_requires_one_canonical_lf_terminated_line(string source)
    {
        WithTemporaryDirectory(root =>
        {
            File.WriteAllText(
                Path.Combine(root, "VERSION"),
                source,
                new UTF8Encoding(false));

            Assert.Throws<InvalidDataException>(() => ProductVersionCheck.ReadCanonicalVersion(root));
        });
    }

    [Fact]
    public void Version_file_rejects_invalid_utf8()
    {
        WithTemporaryDirectory(root =>
        {
            File.WriteAllBytes(Path.Combine(root, "VERSION"), [0xff, 0x0a]);

            var exception = Assert.Throws<InvalidDataException>(
                () => ProductVersionCheck.ReadCanonicalVersion(root));
            Assert.Contains("valid UTF-8", exception.Message);
        });
    }

    [Fact]
    public void Aligned_repository_versions_pass()
    {
        WithTemporaryDirectory(root =>
        {
            WriteVersionFixture(root);

            var result = ProductVersionCheck.Inspect(root);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            Assert.Equal(
                "Product versions aligned: product=0.3.0-alpha.1 package=0.3.0a1",
                result.SuccessMessage);
        });
    }

    [Fact]
    public void Duplicate_or_drifted_version_declarations_fail_closed()
    {
        WithTemporaryDirectory(root =>
        {
            WriteVersionFixture(root);
            File.AppendAllText(
                Path.Combine(root, "pyproject.toml"),
                "version = \"0.3.0a1\"\n",
                new UTF8Encoding(false));

            var duplicate = ProductVersionCheck.Inspect(root);

            Assert.False(duplicate.Passed);
            Assert.Contains(duplicate.Failures, failure => failure.Contains(
                "found 2",
                StringComparison.Ordinal));

            WriteVersionFixture(root);
            File.WriteAllText(
                Path.Combine(root, "game", "scripts", "ProductIdentity.cs"),
                "public const string AppVersion = \"0.3.1\";\n",
                new UTF8Encoding(false));

            var drift = ProductVersionCheck.Inspect(root);

            Assert.False(drift.Passed);
            Assert.Contains(drift.Failures, failure => failure.StartsWith(
                "Product version mismatch:",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Version_inspection_reports_missing_or_invalid_canonical_version()
    {
        WithTemporaryDirectory(root =>
        {
            var missing = ProductVersionCheck.Inspect(root);

            Assert.False(missing.Passed);
            Assert.Contains(
                "Could not read canonical product version from VERSION.",
                missing.Failures);

            File.WriteAllText(
                Path.Combine(root, "VERSION"),
                "not-semver\n",
                new UTF8Encoding(false));

            var malformed = ProductVersionCheck.Inspect(root);

            Assert.False(malformed.Passed);
            Assert.Contains(malformed.Failures, failure => failure.Contains(
                "canonical stable or prerelease SemVer",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Version_inspection_reports_missing_and_non_utf8_declarations()
    {
        WithTemporaryDirectory(root =>
        {
            WriteVersionFixture(root);
            File.Delete(Path.Combine(root, "src", "vibesnake", "__init__.py"));
            File.WriteAllBytes(Path.Combine(root, "pyproject.toml"), [0xff]);

            var result = ProductVersionCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Contains("Could not read pyproject.toml as UTF-8 text.", result.Failures);
            Assert.Contains(result.Failures, failure => failure.StartsWith(
                "Could not read src",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Documentation_check_accepts_local_external_encoded_and_fenced_links()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            File.WriteAllText(
                Path.Combine(root, "docs", "guide.md"),
                "[root](../README.md)\n"
                + "[encoded](<space%20name.md>)\n"
                + "[section](#local)\n"
                + "[web](https://example.test/missing)\n"
                + "[network](ftp://example.test/missing)\n"
                + "[mail](mailto:test@example.test)\n"
                + "```text\n[ignored](missing.md)\n```\n",
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(root, "docs", "space name.md"),
                "# Encoded\n",
                new UTF8Encoding(false));

            var result = DocumentationCheck.Inspect(root);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        });
    }

    [Fact]
    public void Documentation_check_reports_missing_targets_with_stable_locations()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            File.WriteAllText(
                Path.Combine(root, "docs", "guide.md"),
                "# Guide\n\n[missing](nowhere.md)\n",
                new UTF8Encoding(false));

            var result = DocumentationCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Contains(
                "docs/guide.md:3: missing target nowhere.md",
                result.Failures);
        });
    }

    [Fact]
    public void Documentation_check_handles_root_query_empty_and_protocol_relative_targets()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            WriteFile(
                root,
                "docs/guide.md",
                "[root](/README.md?view=1#top)\n"
                + "[empty](#section)\n"
                + "[query](?mode=local)\n"
                + "[protocol](//example.test/file.md)\n");

            var result = DocumentationCheck.Inspect(root);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        });
    }

    [Fact]
    public void Documentation_check_reports_invalid_local_targets_and_utf8()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            WriteFile(root, "docs/guide.md", "[invalid](bad%00path.md)\n");

            var invalidTarget = DocumentationCheck.Inspect(root);

            Assert.False(invalidTarget.Passed);
            Assert.Contains(invalidTarget.Failures, failure => failure.Contains(
                "invalid target bad%00path.md",
                StringComparison.Ordinal));

            File.WriteAllBytes(Path.Combine(root, "docs", "guide.md"), [0xff]);

            var invalidText = DocumentationCheck.Inspect(root);

            Assert.False(invalidText.Passed);
            Assert.Contains(invalidText.Failures, failure => failure.Contains(
                "could not read UTF-8 text",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Documentation_check_rejects_duplicate_contract_and_resource_claims()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            File.WriteAllText(
                Path.Combine(root, "CHANGELOG.md"),
                "contracts to `1.2.3` with rules resource v4\n"
                + "contracts to `1.2.3` with rules resource v4\n",
                new UTF8Encoding(false));

            var result = DocumentationCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains(
                "agent contract version 1.2.3 is already claimed on line 1",
                StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.Contains(
                "rules resource v4 is already claimed on line 1",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Documentation_check_requires_every_canonical_document()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            File.Delete(Path.Combine(root, "SUPPORT.md"));

            var result = DocumentationCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Contains("missing canonical document: SUPPORT.md", result.Failures);
        });
    }

    [Fact]
    public void Documentation_check_reports_missing_tree_and_unreadable_changelog()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            Directory.Delete(Path.Combine(root, "docs"), true);

            var missingTree = DocumentationCheck.Inspect(root);

            Assert.False(missingTree.Passed);
            Assert.Contains("missing canonical document tree: docs", missingTree.Failures);

            WriteDocumentationFixture(root);
            File.WriteAllBytes(Path.Combine(root, "CHANGELOG.md"), [0xff]);

            var invalidChangelog = DocumentationCheck.Inspect(root);

            Assert.False(invalidChangelog.Passed);
            Assert.Contains(
                "CHANGELOG.md: could not read UTF-8 text.",
                invalidChangelog.Failures);
        });
    }

    [Fact]
    public void Documentation_check_reports_a_missing_changelog_contract_source()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            File.Delete(Path.Combine(root, "CHANGELOG.md"));

            var result = DocumentationCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Contains("missing CHANGELOG.md", result.Failures);
        });
    }

    [Fact]
    public void Source_policy_files_match_the_active_authored_scope()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "README.md", "# Root\n");
            WriteFile(root, "assets/README.md", "# Assets\n");
            WriteFile(root, "src/active.PY", "value = 1\n");
            WriteFile(root, "docs/guide.md", "# Guide\n");
            WriteFile(root, "docs/archive/history.md", "# History\n");
            WriteFile(root, "docs/research/notes.md", "# Notes\n");
            WriteFile(root, "native/obj/generated.cs", "class Generated {}\n");
            WriteFile(root, "config/not-in-scan.yaml", "value: 1\n");

            var files = SourcePolicyCheck.PolicyFiles(root);

            Assert.Equal(
                ["README.md", "assets/README.md", "docs/guide.md", "src/active.PY"],
                files);
        });
    }

    [Fact]
    public void Source_policy_reports_text_unicode_marker_and_attribution_rules()
    {
        WithTemporaryDirectory(root =>
        {
            var marker = string.Concat("to", "do");
            var assistantName = string.Concat("co", "dex");
            var emDash = char.ConvertFromUtf32(0x2014);
            var emoji = char.ConvertFromUtf32(0x1F40D);
            WriteFile(
                root,
                "src/bad.cs",
                $"// {marker}: later\nvar separator = \"{emDash}\";\nvar icon = \"{emoji}\";\n// generated by {assistantName}\n");

            var result = SourcePolicyCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Equal(
                [
                    "src/bad.cs:1: unfinished-work marker is forbidden",
                    "src/bad.cs:2: em dash is forbidden",
                    "src/bad.cs:3: emoji is forbidden",
                    "src/bad.cs:4: assistant attribution is forbidden",
                ],
                result.Failures);
        });
    }

    [Fact]
    public void Source_policy_allows_markers_only_in_the_canonical_standard()
    {
        WithTemporaryDirectory(root =>
        {
            var marker = string.Concat("FIX", "ME");
            WriteFile(
                root,
                "docs/engineering/CODE_QUALITY_STANDARDS.md",
                $"The forbidden example is {marker}.\n");
            WriteFile(root, "src/active.cs", "class Active {}\n");

            var result = SourcePolicyCheck.Inspect(root);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        });
    }

    [Fact]
    public void Source_policy_covers_every_declared_emoji_range()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(
                root,
                "docs/emoji.md",
                string.Join('\n', EmojiPolicyCodePoints.Select(char.ConvertFromUtf32)) + "\n");

            var result = SourcePolicyCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Equal(6, result.Failures.Count);
            Assert.All(result.Failures, failure => Assert.EndsWith("emoji is forbidden", failure));
        });
    }

    [Fact]
    public void Source_policy_rejects_credentials_across_the_repository()
    {
        WithTemporaryDirectory(root =>
        {
            string[] forbiddenPaths =
            [
                ".env",
                "config/.env.production",
                "signing/id_rsa",
                "signing/id_rsa.pub",
                "signing/AuthKey_RELEASE.P8",
                "signing/release.keystore",
                "outside/windows.PFX",
                "outside/signing.key",
            ];
            foreach (var relativePath in forbiddenPaths)
            {
                WriteFile(root, relativePath, "private\n");
            }

            WriteFile(root, ".git/ignored.pem", "private\n");
            WriteFile(root, "build/ignored.p12", "private\n");

            var result = SourcePolicyCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Equal(forbiddenPaths.Length, result.Failures.Count);
            Assert.Equal(
                forbiddenPaths.Order(StringComparer.Ordinal),
                result.Failures.Select(failure => failure.Split(':', 2)[0]));
            Assert.All(
                result.Failures,
                failure => Assert.EndsWith("credential or signing material is forbidden", failure));
        });
    }

    [Fact]
    public void Source_policy_reports_unreadable_utf8_deterministically()
    {
        WithTemporaryDirectory(root =>
        {
            var path = Path.Combine(root, "src", "invalid.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [0xC3, 0x28]);

            var result = SourcePolicyCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Single(result.Failures);
            Assert.StartsWith("src/invalid.cs:1: unreadable UTF-8 text:", result.Failures[0]);
            Assert.DoesNotContain('\n', result.Failures[0]);
            Assert.DoesNotContain('\r', result.Failures[0]);
        });
    }

    [Fact]
    public void Source_policy_reports_python_placeholders_at_stable_lines()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(
                root,
                "src/bad.py",
                "def empty():\n"
                + "    pass\n"
                + "assert ((True)), 'message'\n"
                + "try:\n"
                + "    value = 1\n"
                + "except:\n"
                + "    value = 2\n"
                + "...\n"
                + "def inline(): ...\n"
                + "value = 1; ...\n");

            var result = SourcePolicyCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Equal(
                [
                    "src/bad.py:2: empty pass statement is forbidden",
                    "src/bad.py:3: constant-true assertion is forbidden",
                    "src/bad.py:6: bare except clause is forbidden",
                    "src/bad.py:8: ellipsis placeholder is forbidden",
                    "src/bad.py:9: ellipsis placeholder is forbidden",
                    "src/bad.py:10: ellipsis placeholder is forbidden",
                ],
                result.Failures);
        });
    }

    [Fact]
    public void Source_policy_ignores_python_tokens_in_strings_comments_and_values()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(
                root,
                "src/valid.py",
                "text = '''pass assert True except: ...'''\n"
                + "# pass assert True except: ...\n"
                + "sentinel = ...\n"
                + "items = [...]\n"
                + "assert value is True\n"
                + "try:\n"
                + "    value = 1\n"
                + "except ValueError:\n"
                + "    value = 2\n"
                + "def complete(): return Ellipsis\n");

            var result = SourcePolicyCheck.Inspect(root);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        });
    }

    [Fact]
    public void Source_policy_handles_multiline_and_deduplicated_python_statements()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(
                root,
                "src/placeholders.py",
                "def multiline():\n"
                + "    (\n"
                + "        ...\n"
                + "    )\n"
                + "pass; pass\n");

            var result = SourcePolicyCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Equal(
                [
                    "src/placeholders.py:3: ellipsis placeholder is forbidden",
                    "src/placeholders.py:5: empty pass statement is forbidden",
                ],
                result.Failures);
        });
    }

    [Theory]
    [InlineData("value = 'unterminated\n", 1, "unterminated string literal")]
    [InlineData("value = (1]\n", 1, "unbalanced delimiters")]
    [InlineData("value = [1\n", 1, "unbalanced delimiters")]
    public void Source_policy_rejects_invalid_python_lexical_structure(
        string source,
        int line,
        string detail)
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "src/invalid.py", source);

            var result = SourcePolicyCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Equal(
                $"src/invalid.py:{line}: invalid Python lexical structure: {detail}",
                Assert.Single(result.Failures));
        });
    }

    [Fact]
    public void Source_policy_rejects_a_missing_repository_root()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = SourcePolicyCheck.Inspect(missing);

        Assert.False(result.Passed);
        Assert.Equal("repository root does not exist", Assert.Single(result.Failures));
    }

    [Fact]
    public void Candidate_freeze_policy_resolves_direct_nested_overlapping_and_generated_surfaces()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCandidateFreezeFixture(root);

            var result = CandidateFreezeCheck.Inspect(root);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            Assert.Contains("7 frozen-surface files (pre-freeze)", result.SuccessMessage);
        });
    }

    [Fact]
    public void Candidate_freeze_policy_fails_closed_for_missing_invalid_utf8_and_malformed_json()
    {
        WithTemporaryDirectory(root =>
        {
            var missing = CandidateFreezeCheck.Inspect(root);
            Assert.False(missing.Passed);
            Assert.Contains(missing.Failures, failure => failure.Contains(
                "candidate freeze policy is unreadable",
                StringComparison.Ordinal));

            var policyPath = Path.Combine(root, "config", "candidate_freeze_policy_v1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(policyPath)!);
            File.WriteAllBytes(policyPath, [0xff]);
            var invalidUtf8 = CandidateFreezeCheck.Inspect(root);
            Assert.False(invalidUtf8.Passed);
            Assert.Contains(invalidUtf8.Failures, failure => failure.Contains(
                "candidate freeze policy is unreadable",
                StringComparison.Ordinal));

            File.WriteAllText(policyPath, "{", new UTF8Encoding(false));
            var malformed = CandidateFreezeCheck.Inspect(root);
            Assert.False(malformed.Passed);
            Assert.Contains(malformed.Failures, failure => failure.Contains(
                "candidate freeze policy is unreadable",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Candidate_freeze_policy_rejects_unknown_missing_duplicate_and_wrong_type_fields()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCandidateFreezeFixture(root);
            var policy = ReadCandidateFreezePolicy(root);
            policy["unexpected"] = true;
            WriteCandidateFreezePolicy(root, policy);
            Assert.False(CandidateFreezeCheck.Inspect(root).Passed);

            WriteCandidateFreezeFixture(root);
            policy = ReadCandidateFreezePolicy(root);
            policy.Remove("policyId");
            WriteCandidateFreezePolicy(root, policy);
            Assert.False(CandidateFreezeCheck.Inspect(root).Passed);

            WriteCandidateFreezeFixture(root);
            var policyPath = Path.Combine(root, "config", "candidate_freeze_policy_v1.json");
            var duplicate = File.ReadAllText(policyPath, Encoding.UTF8)
                .Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1,\n  \"schemaVersion\": 1,", StringComparison.Ordinal);
            File.WriteAllText(policyPath, duplicate, new UTF8Encoding(false));
            var duplicateResult = CandidateFreezeCheck.Inspect(root);
            Assert.False(duplicateResult.Passed);
            Assert.Contains(duplicateResult.Failures, failure => failure.Contains(
                "duplicate object field",
                StringComparison.Ordinal));

            WriteCandidateFreezeFixture(root);
            policy = ReadCandidateFreezePolicy(root);
            policy["schemaVersion"] = "1";
            WriteCandidateFreezePolicy(root, policy);
            Assert.False(CandidateFreezeCheck.Inspect(root).Passed);
        });
    }

    [Fact]
    public void Candidate_freeze_policy_rejects_unsafe_empty_and_broadened_contracts()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCandidateFreezeFixture(root);
            var policy = ReadCandidateFreezePolicy(root);
            var contracts = policy["frozenContracts"]!.AsArray();
            contracts[0]!["pathPatterns"] = JsonStrings("../outside/**/*.cs");
            WriteCandidateFreezePolicy(root, policy);

            var unsafeResult = CandidateFreezeCheck.Inspect(root);

            Assert.False(unsafeResult.Passed);
            Assert.Contains(unsafeResult.Failures, failure => failure.Contains(
                "unsafe path pattern",
                StringComparison.Ordinal));
            Assert.Contains(unsafeResult.Failures, failure => failure.Contains(
                "resolved to no files",
                StringComparison.Ordinal));

            WriteCandidateFreezeFixture(root);
            policy = ReadCandidateFreezePolicy(root);
            contracts = policy["frozenContracts"]!.AsArray();
            contracts[0]!["pathPatterns"] = JsonStrings("surface/missing/**/*.cs");
            WriteCandidateFreezePolicy(root, policy);
            var missingResult = CandidateFreezeCheck.Inspect(root);
            Assert.False(missingResult.Passed);
            Assert.Contains(missingResult.Failures, failure => failure.Contains(
                "matched no files",
                StringComparison.Ordinal));

            WriteCandidateFreezeFixture(root);
            policy = ReadCandidateFreezePolicy(root);
            contracts = policy["frozenContracts"]!.AsArray();
            var firstContract = contracts[0]!.DeepClone();
            var secondContract = contracts[1]!.DeepClone();
            contracts[0] = secondContract;
            contracts[1] = firstContract;
            WriteCandidateFreezePolicy(root, policy);
            Assert.Contains(
                CandidateFreezeCheck.Inspect(root).Failures,
                failure => failure.Contains("frozenContracts IDs", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Candidate_freeze_policy_rejects_prerequisite_change_and_severity_drift()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCandidateFreezeFixture(root);
            var policy = ReadCandidateFreezePolicy(root);
            policy["allowedChangeKinds"]!.AsArray().Add("feature");
            policy["requiredChangeEvidence"]!.AsArray().RemoveAt(0);
            policy["prerequisiteGates"]![0]!["state"] = "unknown";
            policy["severityPolicy"]![3]!["releaseEffect"] = "always-blocks";
            WriteCandidateFreezePolicy(root, policy);

            var result = CandidateFreezeCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.StartsWith(
                "allowedChangeKinds",
                StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.StartsWith(
                "requiredChangeEvidence",
                StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.Contains(
                "must be 'open' or 'passed'",
                StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.StartsWith(
                "severityPolicy",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Candidate_freeze_policy_rejects_activation_before_freeze()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCandidateFreezeFixture(
                root,
                activation: CandidateActivation("a".PadLeft(40, 'a'), "2026-08-21T12:00:00Z", "b".PadLeft(64, 'b')));

            var result = CandidateFreezeCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Contains("pre-freeze activation fields must all be null", result.Failures);
        });
    }

    [Fact]
    public void Candidate_freeze_baseline_requires_exact_identity_and_closed_prerequisites()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCandidateFreezeFixture(root);
            Assert.Throws<InvalidDataException>(() =>
                CandidateFreezeCheck.BuildBaselineJson(
                    root,
                    "a".PadLeft(40, 'a'),
                    "2026-08-21T12:00:00Z"));

            WriteCandidateFreezeFixture(root, prerequisitesPassed: true);
            Assert.Throws<InvalidDataException>(() =>
                CandidateFreezeCheck.BuildBaselineJson(root, "main", "2026-08-21T12:00:00Z"));
            Assert.Throws<InvalidDataException>(() =>
                CandidateFreezeCheck.BuildBaselineJson(root, "a".PadLeft(40, 'a'), "today"));
            Assert.Throws<InvalidDataException>(() =>
                CandidateFreezeCheck.BuildBaselineJson(
                    root,
                    "a".PadLeft(40, 'a'),
                    "2026-99-99T12:00:00Z"));
        });
    }

    [Fact]
    public void Candidate_freeze_baseline_is_deterministic_sorted_and_complete()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCandidateFreezeFixture(root, prerequisitesPassed: true);
            var revision = "a".PadLeft(40, 'a');
            const string generatedUtc = "2026-08-21T12:00:00Z";

            var first = CandidateFreezeCheck.BuildBaselineJson(root, revision, generatedUtc);
            var second = CandidateFreezeCheck.BuildBaselineJson(root, revision, generatedUtc);

            Assert.Equal(first, second);
            var baseline = JsonNode.Parse(first)!.AsObject();
            var files = baseline["files"]!.AsArray();
            Assert.Equal(7, files.Count);
            var paths = files.Select(file => file!["path"]!.GetValue<string>()).ToArray();
            Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
            Assert.DoesNotContain(paths, path => path.Contains("/obj/", StringComparison.Ordinal));
            var replay = files.Single(file =>
                file!["path"]!.GetValue<string>() == "surface/rules/ReplayContracts.cs")!;
            Assert.Equal(
                ["replay-schema", "rules"],
                replay["contractIds"]!.AsArray().Select(value => value!.GetValue<string>()));
            Assert.Matches("^[0-9a-f]{64}$", baseline["combinedSha256"]!.GetValue<string>());
        });
    }

    [Fact]
    public void Candidate_freeze_frozen_policy_accepts_exact_baseline_and_rejects_source_drift()
    {
        WithTemporaryDirectory(root =>
        {
            ActivateCandidateFreeze(root);

            var exact = CandidateFreezeCheck.Inspect(root);

            Assert.True(exact.Passed, string.Join(Environment.NewLine, exact.Failures));
            WriteFile(root, "surface/rules/Direct.cs", "changed\n");
            var drift = CandidateFreezeCheck.Inspect(root);
            Assert.False(drift.Passed);
            Assert.Contains(
                "frozen contract files differ from the baseline manifest",
                drift.Failures);
            Assert.Contains(
                "baseline combined SHA-256 does not match current frozen contracts",
                drift.Failures);
        });
    }

    [Fact]
    public void Candidate_freeze_frozen_policy_rejects_manifest_hash_and_shape_drift()
    {
        WithTemporaryDirectory(root =>
        {
            ActivateCandidateFreeze(root);
            var manifestPath = Path.Combine(root, "config", "candidate_freeze_baseline_v1.json");
            File.AppendAllText(manifestPath, "\n", new UTF8Encoding(false));

            var hashDrift = CandidateFreezeCheck.Inspect(root);

            Assert.False(hashDrift.Passed);
            Assert.Contains(
                "baseline manifest SHA-256 does not match the activation record",
                hashDrift.Failures);

            ActivateCandidateFreeze(root);
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath, Encoding.UTF8))!.AsObject();
            manifest["unexpected"] = true;
            WriteFile(root, "config/candidate_freeze_baseline_v1.json", RenderJson(manifest));
            UpdateCandidateBaselineHash(root);
            var shapeDrift = CandidateFreezeCheck.Inspect(root);
            Assert.False(shapeDrift.Passed);
            Assert.Contains(shapeDrift.Failures, failure => failure.Contains(
                "baseline manifest is unreadable",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Candidate_freeze_frozen_policy_rejects_open_or_ambiguous_activation()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCandidateFreezeFixture(
                root,
                state: "frozen",
                activation: CandidateActivation("MAIN", "today", "not-a-hash"));

            var result = CandidateFreezeCheck.Inspect(root);

            Assert.False(result.Passed);
            Assert.Contains(
                "every prerequisite gate must pass before the policy is frozen",
                result.Failures);
            Assert.Contains(result.Failures, failure => failure.StartsWith(
                "candidateRevision",
                StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.StartsWith(
                "activatedUtc",
                StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.StartsWith(
                "baselineSha256",
                StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Candidate_freeze_baseline_command_writes_atomically_and_reports_failures()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCandidateFreezeFixture(root, prerequisitesPassed: true);
            var output = new StringWriter();
            var error = new StringWriter();
            var revision = "a".PadLeft(40, 'a');

            var code = RepositoryCheckCommand.Run(
                [
                    "freeze-baseline",
                    revision,
                    "2026-08-21T12:00:00Z",
                    root,
                    "config/custom_freeze_baseline.json",
                ],
                output,
                error);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Contains("with 7 files", output.ToString());
            Assert.True(File.Exists(Path.Combine(root, "config", "custom_freeze_baseline.json")));
            Assert.Empty(Directory.EnumerateFiles(
                Path.Combine(root, "config"),
                ".custom_freeze_baseline.json.*.tmp"));

            WriteCandidateFreezeFixture(root);
            output = new StringWriter();
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["freeze-baseline", revision, "2026-08-21T12:00:00Z", root],
                output,
                error);
            Assert.Equal(1, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("prerequisite gate must pass", error.ToString());
        });
    }

    [Fact]
    public void Dependency_lock_digest_is_path_content_order_sensitive_and_contained()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFile(root, "requirements.txt", "runtime>=1\n");
            WriteFile(root, "requirements-dev.txt", "-r requirements.txt\ntest>=2\n");
            List<string> inputs = ["requirements.txt", "requirements-dev.txt"];

            var original = DependencyLockCheck.ComputeInputDigest(root, inputs);
            WriteFile(root, "requirements-dev.txt", "-r requirements.txt\ntest>=3\n");

            Assert.NotEqual(original, DependencyLockCheck.ComputeInputDigest(root, inputs));
            Assert.NotEqual(
                original,
                DependencyLockCheck.ComputeInputDigest(root, inputs.AsEnumerable().Reverse().ToArray()));
            Assert.Throws<InvalidDataException>(
                () => DependencyLockCheck.ComputeInputDigest(root, ["../outside.txt"]));
            Assert.Throws<InvalidDataException>(
                () => DependencyLockCheck.ComputeInputDigest(root, ["missing.txt"]));
            Assert.Throws<InvalidDataException>(
                () => DependencyLockCheck.ComputeInputDigest(root, [Path.GetFullPath("absolute.txt")]));
        });
    }

    [Fact]
    public void Dependency_lock_validation_requires_current_lf_pinned_hashed_content()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDependencyLockInputs(root);
            var valid = RenderDependencyLock(root, "ci");

            Assert.Equal(2, DependencyLockCheck.ValidateLockText(valid, root));

            var failures = new (string Source, string Message)[]
            {
                (valid.TrimEnd('\n'), "end with a newline"),
                (valid.Replace("\n", "\r\n", StringComparison.Ordinal), "LF line endings"),
                (valid.Replace("# Generator: RepositoryChecks", "# Generator: unknown", StringComparison.Ordinal), "generator header"),
                (Regex.Replace(valid, "# Inputs-SHA256: [a-f0-9]{64}", "# Inputs-SHA256: " + new string('0', 64)), "stale"),
                (valid.Replace("runtime==1.2.3", "runtime>=1.2.3", StringComparison.Ordinal), "exactly pinned"),
                (valid.Replace("    --hash=sha256:" + new string('a', 64) + "\n", string.Empty, StringComparison.Ordinal), "no SHA-256 hash"),
                (valid.Replace(new string('a', 64), new string('A', 64), StringComparison.Ordinal), "no SHA-256 hash"),
            };
            foreach (var (source, message) in failures)
            {
                var exception = Assert.Throws<InvalidDataException>(
                    () => DependencyLockCheck.ValidateLockText(source, root));
                Assert.Contains(message, exception.Message, StringComparison.Ordinal);
            }

            var headerOnly = valid[..valid.IndexOf("runtime==", StringComparison.Ordinal)];
            var empty = Assert.Throws<InvalidDataException>(
                () => DependencyLockCheck.ValidateLockText(headerOnly, root));
            Assert.Contains("no requirements", empty.Message, StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(
                () => DependencyLockCheck.ValidateLockText(valid, root, "unknown"));
        });
    }

    [Fact]
    public void Dependency_lock_rendering_removes_uv_header_and_normalizes_crlf()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDependencyLockInputs(root);
            var digest = DependencyLockCheck.ComputeInputDigest(
                root,
                ["pyproject.toml", "requirements.txt", "requirements-dev.txt"]);
            var raw = "# uv header\r\n\r\nruntime==1.2.3 \\\r\n"
                + $"    --hash=sha256:{new string('a', 64)}\r\n";

            var rendered = DependencyLockCheck.RenderGeneratedLock(raw, digest, "ci");

            Assert.DoesNotContain("uv header", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain('\r', rendered);
            Assert.StartsWith("# Generator: RepositoryChecks\n", rendered, StringComparison.Ordinal);
            Assert.Equal(1, DependencyLockCheck.ValidateLockText(rendered, root));

            Assert.Throws<InvalidDataException>(
                () => DependencyLockCheck.RenderGeneratedLock("# comments only\n", digest, "ci"));
            Assert.Throws<InvalidDataException>(
                () => DependencyLockCheck.RenderGeneratedLock("package==1\rbroken\n", digest, "ci"));
        });
    }

    [Fact]
    public void Dependency_lock_inspection_reports_profiles_independently_and_strict_utf8()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDependencyLockFixture(root);

            var valid = DependencyLockCheck.Inspect(root);

            Assert.True(valid.Passed, string.Join(Environment.NewLine, valid.Failures));
            Assert.Equal(
                "Python dependency locks verified: ci packages=2, runtime packages=2",
                valid.SuccessMessage);

            File.WriteAllBytes(Path.Combine(root, "requirements-ci.lock"), [0xff]);
            File.Delete(Path.Combine(root, "requirements-runtime.lock"));
            var invalid = DependencyLockCheck.Inspect(root);
            Assert.False(invalid.Passed);
            Assert.Equal(2, invalid.Failures.Count);
            Assert.StartsWith("ci: dependency lock is unreadable", invalid.Failures[0], StringComparison.Ordinal);
            Assert.StartsWith("runtime: dependency lock is unreadable", invalid.Failures[1], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Dependency_lock_generation_uses_exact_contract_and_atomic_replacement()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDependencyLockInputs(root);
            WriteFile(root, "requirements-ci.lock", "old\n");
            var resolver = new FakeDependencyResolverProcess();

            var count = DependencyLockCheck.WriteProfile(root, "ci", resolver);

            Assert.Equal(2, count);
            Assert.Equal(2, resolver.Calls.Count);
            Assert.Equal(["--version"], resolver.Calls[0].Arguments);
            Assert.Equal(TimeSpan.FromSeconds(10), resolver.Calls[0].Timeout);
            Assert.Equal(
                [
                    "pip",
                    "compile",
                    "requirements-dev.txt",
                    "--universal",
                    "--python-version",
                    "3.11",
                    "--generate-hashes",
                    "--output-file",
                ],
                resolver.Calls[1].Arguments[..^1]);
            Assert.Equal(TimeSpan.FromSeconds(180), resolver.Calls[1].Timeout);
            Assert.Equal(
                2,
                DependencyLockCheck.CheckProfile(root, "ci"));
            Assert.Empty(Directory.EnumerateFiles(root, ".requirements-ci.lock.*.tmp"));
        });
    }

    [Theory]
    [InlineData("wrong", 0, false, "uv 0.12.5\n", "is required")]
    [InlineData("missing", 0, false, "", "no version reported")]
    [InlineData("exit", 2, false, "uv 0.11.33\n", "unable to verify")]
    [InlineData("timeout", 0, true, "uv 0.11.33\n", "timed out after 10 seconds")]
    public void Dependency_lock_generation_rejects_unqualified_resolver_versions(
        string standardError,
        int exitCode,
        bool timedOut,
        string standardOutput,
        string expectedMessage)
    {
        WithTemporaryDirectory(root =>
        {
            WriteDependencyLockInputs(root);
            var resolver = new FakeDependencyResolverProcess
            {
                Results =
                {
                    new ResolverProcessResult(exitCode, standardOutput, standardError, timedOut),
                },
            };

            var exception = Assert.Throws<InvalidDataException>(
                () => DependencyLockCheck.WriteProfile(root, "ci", resolver));

            Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("warning: unstable", 0, false, "emitted a warning")]
    [InlineData("resolution failed", 2, false, "resolution failed")]
    [InlineData("", 0, true, "timed out after 180 seconds")]
    public void Dependency_lock_generation_rejects_resolution_failures(
        string standardError,
        int exitCode,
        bool timedOut,
        string expectedMessage)
    {
        WithTemporaryDirectory(root =>
        {
            WriteDependencyLockInputs(root);
            var resolver = new FakeDependencyResolverProcess
            {
                Results =
                {
                    new ResolverProcessResult(0, "uv 0.11.33\n", string.Empty),
                    new ResolverProcessResult(exitCode, string.Empty, standardError, timedOut),
                },
            };

            var exception = Assert.Throws<InvalidDataException>(
                () => DependencyLockCheck.WriteProfile(root, "runtime", resolver));

            Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Dependency_lock_generation_reports_resolver_and_output_failures()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDependencyLockInputs(root);
            var missing = new FakeDependencyResolverProcess
            {
                ResolveException = new FileNotFoundException("resolver absent"),
            };
            Assert.Contains(
                "resolver absent",
                Assert.Throws<InvalidDataException>(
                    () => DependencyLockCheck.WriteProfile(root, "ci", missing)).Message,
                StringComparison.Ordinal);

            var launchFailure = new FakeDependencyResolverProcess
            {
                RunException = new IOException("launch failed"),
            };
            Assert.Contains(
                "unable to verify uv version",
                Assert.Throws<InvalidDataException>(
                    () => DependencyLockCheck.WriteProfile(root, "ci", launchFailure)).Message,
                StringComparison.Ordinal);

            var empty = new FakeDependencyResolverProcess { RawLock = "# comments only\n" };
            Assert.Contains(
                "empty dependency lock",
                Assert.Throws<InvalidDataException>(
                    () => DependencyLockCheck.WriteProfile(root, "ci", empty)).Message,
                StringComparison.Ordinal);

            var invalidUtf8 = new FakeDependencyResolverProcess { WriteInvalidUtf8 = true };
            Assert.Contains(
                "generated dependency lock is unreadable",
                Assert.Throws<InvalidDataException>(
                    () => DependencyLockCheck.WriteProfile(root, "ci", invalidUtf8)).Message,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void System_dependency_resolver_prefers_checkout_then_path_and_fails_closed()
    {
        WithTemporaryDirectory(root =>
        {
            var localRelative = OperatingSystem.IsWindows()
                ? ".venv/Scripts/uv.exe"
                : ".venv/bin/uv";
            WriteFile(root, localRelative, string.Empty);
            var resolver = new SystemDependencyResolverProcess(string.Empty);

            Assert.Equal(
                Path.GetFullPath(Path.Combine(root, localRelative)),
                resolver.ResolveExecutable(root));

            File.Delete(Path.Combine(root, localRelative));
            var pathRoot = Path.Combine(root, "path");
            var executableName = OperatingSystem.IsWindows() ? "uv.exe" : "uv";
            WriteFile(root, "path/" + executableName, string.Empty);
            resolver = new SystemDependencyResolverProcess(pathRoot);
            Assert.Equal(
                Path.Combine(pathRoot, executableName),
                resolver.ResolveExecutable(root));
            resolver = new SystemDependencyResolverProcess(string.Empty);
            Assert.Throws<FileNotFoundException>(() => resolver.ResolveExecutable(root));
        });
    }

    [Fact]
    public void System_dependency_resolver_process_captures_output_exit_and_timeout()
    {
        var resolver = new SystemDependencyResolverProcess();
        string executable;
        string[] outputArguments;
        string[] timeoutArguments;
        if (OperatingSystem.IsWindows())
        {
            executable = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            outputArguments = ["-NoProfile", "-Command", "Write-Output output; [Console]::Error.WriteLine('error'); exit 7"];
            timeoutArguments = ["-NoProfile", "-Command", "Start-Sleep -Seconds 5"];
        }
        else
        {
            executable = "/bin/sh";
            outputArguments = ["-c", "printf output; printf error >&2; exit 7"];
            timeoutArguments = ["-c", "sleep 5"];
        }

        // Hosted Windows Coverlet runs can spend several seconds starting powershell.exe.
        var completed = resolver.Run(
            executable,
            outputArguments,
            Directory.GetCurrentDirectory(),
            TimeSpan.FromSeconds(30));
        var timedOut = resolver.Run(
            executable,
            timeoutArguments,
            Directory.GetCurrentDirectory(),
            TimeSpan.FromMilliseconds(100));

        Assert.False(completed.TimedOut);
        Assert.Equal(7, completed.ExitCode);
        Assert.Contains("output", completed.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("error", completed.StandardError, StringComparison.Ordinal);
        Assert.True(timedOut.TimedOut);
        Assert.ThrowsAny<Exception>(() => resolver.Run(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            [],
            Directory.GetCurrentDirectory(),
            TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Dependency_lock_write_command_has_success_failure_and_usage_paths()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDependencyLockInputs(root);
            var output = new StringWriter();
            var error = new StringWriter();

            var success = RepositoryCheckCommand.Run(
                ["lock-write", "ci", root],
                output,
                error,
                new FakeDependencyResolverProcess());

            Assert.Equal(0, success);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Contains("packages=2", output.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            var failure = RepositoryCheckCommand.Run(
                ["lock-write", "runtime", root],
                output,
                error,
                new FakeDependencyResolverProcess { ResolveException = new IOException("unavailable") });
            Assert.Equal(1, failure);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("generation failed", error.ToString(), StringComparison.OrdinalIgnoreCase);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["lock-write", "unknown", root],
                    output,
                    error,
                    new FakeDependencyResolverProcess()));
            Assert.Contains("lock-write <ci|runtime>", error.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Project_logo_check_covers_missing_malformed_dimension_and_hash_failures()
    {
        WithTemporaryDirectory(root =>
        {
            var missing = ProjectLogoCheck.Inspect(root);
            Assert.False(missing.Passed);
            Assert.Equal("logo is missing: assets/images/logo.png", missing.Failures.Single());

            Directory.CreateDirectory(Path.Combine(root, "assets", "images", "logo.png"));
            var directory = ProjectLogoCheck.Inspect(root);
            Assert.False(directory.Passed);
            Assert.Equal("logo is missing: assets/images/logo.png", directory.Failures.Single());
            Directory.Delete(Path.Combine(root, "assets", "images", "logo.png"));

            WritePngFixture(root, 1024, 1024, extra: [0x00], truncateTo: 8);
            var truncated = ProjectLogoCheck.Inspect(root);
            Assert.False(truncated.Passed);
            Assert.Equal("not a supported PNG logo: assets/images/logo.png", truncated.Failures.Single());

            WritePngFixture(root, 1024, 1024, extra: "not a png"u8);
            OverwriteLogoPrefix(root, "NOTPNG!!"u8);
            var signature = ProjectLogoCheck.Inspect(root);
            Assert.False(signature.Passed);
            Assert.Equal("not a supported PNG logo: assets/images/logo.png", signature.Failures.Single());

            WritePngFixture(root, 1024, 1024, extra: [], ihdrType: "IHDX"u8);
            var ihdr = ProjectLogoCheck.Inspect(root);
            Assert.False(ihdr.Passed);
            Assert.Equal("not a supported PNG logo: assets/images/logo.png", ihdr.Failures.Single());

            WritePngFixture(root, 1024, 1023, extra: [0x00]);
            var dimensions = ProjectLogoCheck.Inspect(root);
            Assert.False(dimensions.Passed);
            Assert.Equal("logo dimensions must be 1024x1024, got 1024x1023", dimensions.Failures.Single());

            WritePngFixture(root, 1024, 1024, extra: [0x00]);
            var hash = ProjectLogoCheck.Inspect(root);
            Assert.False(hash.Passed);
            Assert.Equal(
                "logo bytes do not match the preferred brand mark; "
                + "restore assets/images/logo.png from the approved Snakev2 mark",
                hash.Failures.Single());

            CopyApprovedLogo(root);
            var trailingPath = Path.Combine(root, "assets", "images", "logo.png");
            File.WriteAllBytes(trailingPath, [.. File.ReadAllBytes(trailingPath), 0x00]);
            var trailing = ProjectLogoCheck.Inspect(root);
            Assert.False(trailing.Passed);
            Assert.Equal(
                "logo bytes do not match the preferred brand mark; "
                + "restore assets/images/logo.png from the approved Snakev2 mark",
                trailing.Failures.Single());
        });
    }

    [Fact]
    public void Project_logo_command_is_isolated_from_other_checks()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            var docsOutput = new StringWriter();
            var docsError = new StringWriter();
            Assert.Equal(0, RepositoryCheckCommand.Run(["docs", root], docsOutput, docsError));
            Assert.DoesNotContain("Project logo", docsOutput.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, docsError.ToString());

            WriteVersionFixture(root);
            WriteCandidateFreezeFixture(root);
            WriteDependencyLockFixture(root);
            WriteAgentPluginFixture(root);
            var allOutput = new StringWriter();
            var allError = new StringWriter();
            Assert.Equal(1, RepositoryCheckCommand.Run(["all", root], allOutput, allError));
            Assert.Contains("Project logo check failed:", allError.ToString(), StringComparison.Ordinal);
            Assert.Contains("logo is missing: assets/images/logo.png", allError.ToString(), StringComparison.Ordinal);

            CopyApprovedLogo(root);
            var logoOutput = new StringWriter();
            var logoError = new StringWriter();
            Assert.Equal(0, RepositoryCheckCommand.Run(["logo", root], logoOutput, logoError));
            Assert.Equal("Project logo check passed." + Environment.NewLine, logoOutput.ToString());
            Assert.Equal(string.Empty, logoError.ToString());
            Assert.DoesNotContain("Product versions aligned", logoOutput.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Command_has_stable_usage_and_combined_success_paths()
    {
        var invalidOutput = new StringWriter();
        var invalidError = new StringWriter();

        var invalidCode = RepositoryCheckCommand.Run([], invalidOutput, invalidError);

        Assert.Equal(2, invalidCode);
        Assert.Equal(string.Empty, invalidOutput.ToString());
        Assert.Contains("RepositoryChecks <all|docs|freeze|locks|logo|source|version>", invalidError.ToString());

        WithTemporaryDirectory(root =>
        {
            WriteVersionFixture(root);
            WriteDocumentationFixture(root);
            WriteCandidateFreezeFixture(root);
            WriteDependencyLockFixture(root);
            WriteAgentPluginFixture(root);
            CopyApprovedLogo(root);
            var output = new StringWriter();
            var error = new StringWriter();

            var code = RepositoryCheckCommand.Run(["all", root], output, error);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Contains("Product versions aligned", output.ToString());
            Assert.Contains("Documentation link check passed", output.ToString());
            Assert.Contains("Candidate freeze policy check passed", output.ToString());
            Assert.Contains("Python dependency locks verified", output.ToString());
            Assert.Contains("Project logo check passed.", output.ToString());
            Assert.Contains("Source policy check passed", output.ToString());
            Assert.Contains("Agent Plugin source profile passed", output.ToString());
        });
    }

    [Theory]
    [InlineData("docs")]
    [InlineData("freeze")]
    [InlineData("locks")]
    [InlineData("logo")]
    [InlineData("source")]
    [InlineData("version")]
    public void Command_runs_each_individual_check(string command)
    {
        WithTemporaryDirectory(root =>
        {
            WriteVersionFixture(root);
            WriteDocumentationFixture(root);
            WriteCandidateFreezeFixture(root);
            WriteDependencyLockFixture(root);
            CopyApprovedLogo(root);
            var output = new StringWriter();
            var error = new StringWriter();

            var code = RepositoryCheckCommand.Run([command, root], output, error);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            Assert.NotEqual(string.Empty, output.ToString());
        });
    }

    [Fact]
    public void Command_rejects_null_extra_unknown_and_invalid_root_arguments()
    {
        foreach (IReadOnlyList<string>? arguments in new IReadOnlyList<string>?[]
        {
            null,
            ["all", ".", "extra"],
            ["unknown"],
            ["freeze-baseline", "revision"],
            ["lock-write"],
        })
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var code = RepositoryCheckCommand.Run(arguments, output, error);

            Assert.Equal(2, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("Usage:", error.ToString());
        }

        var invalidRootOutput = new StringWriter();
        var invalidRootError = new StringWriter();
        var invalidRootCode = RepositoryCheckCommand.Run(
            ["docs", "bad\0root"],
            invalidRootOutput,
            invalidRootError);

        Assert.Equal(2, invalidRootCode);
        Assert.Equal(string.Empty, invalidRootOutput.ToString());
        Assert.Contains("Repository root is invalid.", invalidRootError.ToString());
    }

    [Fact]
    public void Command_reports_check_failures_on_standard_error()
    {
        WithTemporaryDirectory(root =>
        {
            WriteDocumentationFixture(root);
            File.Delete(Path.Combine(root, "SUPPORT.md"));
            var output = new StringWriter();
            var error = new StringWriter();

            var code = RepositoryCheckCommand.Run(["docs", root], output, error);

            Assert.Equal(1, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("Documentation check failed:", error.ToString());
            Assert.Contains("missing canonical document: SUPPORT.md", error.ToString());
        });
    }

    [Fact]
    public void Current_repository_passes_native_repository_checks()
    {
        var root = ResolveRepositoryRoot();

        var version = ProductVersionCheck.Inspect(root);
        var docs = DocumentationCheck.Inspect(root);
        var freeze = CandidateFreezeCheck.Inspect(root);
        var locks = DependencyLockCheck.Inspect(root);
        var logo = ProjectLogoCheck.Inspect(root);
        var source = SourcePolicyCheck.Inspect(root);
        var plugin = AgentPluginCheck.Inspect(
            Path.Combine(root, "integrations", "vibesnake-agent-plugin"));

        Assert.True(version.Passed, string.Join(Environment.NewLine, version.Failures));
        Assert.True(docs.Passed, string.Join(Environment.NewLine, docs.Failures));
        Assert.True(freeze.Passed, string.Join(Environment.NewLine, freeze.Failures));
        Assert.True(locks.Passed, string.Join(Environment.NewLine, locks.Failures));
        Assert.Equal(52, DependencyLockCheck.CheckProfile(root, "ci"));
        Assert.Equal(4, DependencyLockCheck.CheckProfile(root, "runtime"));
        Assert.True(logo.Passed, string.Join(Environment.NewLine, logo.Failures));
        Assert.True(source.Passed, string.Join(Environment.NewLine, source.Failures));
        Assert.True(plugin.Passed, string.Join(Environment.NewLine, plugin.Failures));
    }

    private static void WriteCandidateFreezeFixture(
        string root,
        bool prerequisitesPassed = false,
        string state = "pre-freeze",
        JsonObject? activation = null)
    {
        WriteFile(root, "surface/rules/Direct.cs", "direct\n");
        WriteFile(root, "surface/rules/Nested/Nested.cs", "nested\n");
        WriteFile(root, "surface/rules/ReplayContracts.cs", "replay\n");
        WriteFile(root, "surface/rules/obj/Generated.cs", "generated\n");
        WriteFile(root, "surface/save/Persistence.cs", "save\n");
        WriteFile(root, "surface/content.json", "{}\n");
        WriteFile(root, "surface/input.cs", "input\n");
        WriteFile(root, "surface/accessibility.cs", "accessibility\n");

        var gateState = prerequisitesPassed ? "passed" : "open";
        var policy = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["policyId"] = "candidate-freeze-policy-v1",
            ["candidateVersion"] = "0.9.0",
            ["promotionVersion"] = "1.0.0",
            ["state"] = state,
            ["activation"] = activation ?? new JsonObject
            {
                ["candidateRevision"] = null,
                ["activatedUtc"] = null,
                ["baselineManifest"] = null,
                ["baselineSha256"] = null,
            },
            ["prerequisiteGates"] = new JsonArray(
                CandidateGate("0.8.0-acceptance", gateState),
                CandidateGate("clean-revision", gateState),
                CandidateGate("green-ci", gateState),
                CandidateGate("release-matrix-ready", gateState)),
            ["frozenContracts"] = new JsonArray(
                CandidateContract("rules", "surface/rules/**/*.cs"),
                CandidateContract("save-schemas", "surface/save/**/*.cs"),
                CandidateContract("replay-schema", "surface/rules/Replay*.cs"),
                CandidateContract("content-manifests", "surface/content.json"),
                CandidateContract("input-defaults", "surface/input.cs"),
                CandidateContract("accessibility-defaults", "surface/accessibility.cs")),
            ["allowedChangeKinds"] = JsonStrings(
                "defect",
                "compatibility",
                "performance",
                "documentation",
                "release-operation"),
            ["requiredChangeEvidence"] = JsonStrings(
                "changeKind",
                "failedGate",
                "severity",
                "reproduction",
                "verification",
                "affectedFrozenContracts",
                "risk",
                "rollback"),
            ["severityPolicy"] = new JsonArray(
                CandidateSeverity("P0", "always-blocks"),
                CandidateSeverity("P1", "always-blocks"),
                CandidateSeverity("P2", "decision-required"),
                CandidateSeverity("P3", "known-issue-eligible")),
        };
        WriteCandidateFreezePolicy(root, policy);
    }

    private static JsonObject CandidateGate(string id, string state) =>
        new() { ["id"] = id, ["state"] = state };

    private static JsonObject CandidateContract(string id, params string[] patterns) =>
        new() { ["id"] = id, ["pathPatterns"] = JsonStrings(patterns) };

    private static JsonObject CandidateSeverity(string id, string releaseEffect) =>
        new() { ["id"] = id, ["releaseEffect"] = releaseEffect };

    private static JsonObject CandidateActivation(
        string revision,
        string generatedUtc,
        string baselineSha256) =>
        new()
        {
            ["candidateRevision"] = revision,
            ["activatedUtc"] = generatedUtc,
            ["baselineManifest"] = "config/candidate_freeze_baseline_v1.json",
            ["baselineSha256"] = baselineSha256,
        };

    private static JsonArray JsonStrings(params string[] values) =>
        new(values.Select(value => JsonValue.Create(value)).ToArray());

    private static JsonObject ReadCandidateFreezePolicy(string root) =>
        JsonNode.Parse(File.ReadAllText(
            Path.Combine(root, "config", "candidate_freeze_policy_v1.json"),
            Encoding.UTF8))!.AsObject();

    private static void WriteCandidateFreezePolicy(string root, JsonObject policy) =>
        WriteFile(root, "config/candidate_freeze_policy_v1.json", RenderJson(policy));

    private static string RenderJson(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";

    private static void ActivateCandidateFreeze(string root)
    {
        const string generatedUtc = "2026-08-21T12:00:00Z";
        var revision = "a".PadLeft(40, 'a');
        WriteCandidateFreezeFixture(root, prerequisitesPassed: true);
        var baseline = CandidateFreezeCheck.BuildBaselineJson(root, revision, generatedUtc);
        WriteFile(root, "config/candidate_freeze_baseline_v1.json", baseline);
        var baselineHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(baseline)));
        WriteCandidateFreezeFixture(
            root,
            prerequisitesPassed: true,
            state: "frozen",
            activation: CandidateActivation(revision, generatedUtc, baselineHash));
    }

    private static void UpdateCandidateBaselineHash(string root)
    {
        var manifestPath = Path.Combine(root, "config", "candidate_freeze_baseline_v1.json");
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifestPath)));
        var policy = ReadCandidateFreezePolicy(root);
        policy["activation"]!["baselineSha256"] = hash;
        WriteCandidateFreezePolicy(root, policy);
    }

    private static void WriteVersionFixture(string root)
    {
        WriteFile(root, "VERSION", "0.3.0-alpha.1\n");
        WriteFile(root, "pyproject.toml", "version = \"0.3.0a1\"\n");
        WriteFile(
            root,
            "game/scripts/ProductIdentity.cs",
            "public const string AppVersion = \"0.3.0-alpha.1\";\n");
        WriteFile(root, "src/vibesnake/__init__.py", "__version__ = \"0.3.0a1\"\n");
    }

    private static void WriteDependencyLockInputs(string root)
    {
        if (!File.Exists(Path.Combine(root, "pyproject.toml")))
        {
            WriteFile(root, "pyproject.toml", "[project]\nname = \"fixture\"\n");
        }

        WriteFile(root, "requirements.txt", "runtime>=1\n");
        WriteFile(root, "requirements-dev.txt", "-r requirements.txt\ntest>=2\n");
        WriteFile(root, "requirements-runtime.txt", "-r requirements.txt\n");
    }

    private static string RenderDependencyLock(string root, string profile)
    {
        var inputs = profile == "ci"
            ? new[] { "pyproject.toml", "requirements.txt", "requirements-dev.txt" }
            : ["pyproject.toml", "requirements.txt", "requirements-runtime.txt"];
        var digest = DependencyLockCheck.ComputeInputDigest(root, inputs);
        var raw = "# uv generated header\n"
            + "runtime==1.2.3 \\\n"
            + $"    --hash=sha256:{new string('a', 64)}\n"
            + "test==2.4.0 ; python_version >= '3.11' \\\n"
            + $"    --hash=sha256:{new string('b', 64)}\n";
        return DependencyLockCheck.RenderGeneratedLock(raw, digest, profile);
    }

    private static void WriteDependencyLockFixture(string root)
    {
        WriteDependencyLockInputs(root);
        WriteFile(root, "requirements-ci.lock", RenderDependencyLock(root, "ci"));
        WriteFile(root, "requirements-runtime.lock", RenderDependencyLock(root, "runtime"));
    }

    private static void WriteDocumentationFixture(string root)
    {
        string[] rootDocuments =
        [
            "README.md",
            "ROADMAP.md",
            "CHANGELOG.md",
            "CODE_OF_CONDUCT.md",
            "CONTRIBUTING.md",
            "SECURITY.md",
            "SUPPORT.md",
        ];
        string[] supportingDocuments =
        [
            "assets/README.md",
            "assets/ai/README.md",
            "config/README.md",
            "data/README.md",
            "native/README.md",
            "scripts/README.md",
            "scripts/manual/README.md",
            "tests/README.md",
            "docs/research/README.md",
        ];
        foreach (var path in rootDocuments.Concat(supportingDocuments))
        {
            WriteFile(root, path, "# Document\n");
        }

        WriteFile(root, "docs/guide.md", "# Guide\n");
    }

    private static void WriteAgentPluginFixture(string root)
    {
        WriteFile(
            root,
            "integrations/vibesnake-agent-plugin/plugin.json",
            """
            {
              "$schema": "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json",
              "name": "vibesnake-agent",
              "version": "0.17.0",
              "description": "Play deterministic Vibe Snake matches through the local MCP host."
            }
            """ + "\n");
        WriteFile(
            root,
            "integrations/vibesnake-agent-plugin/skills/play-vibesnake/SKILL.md",
            """
            ---
            name: play-vibesnake
            description: Play deterministic Vibe Snake matches through the local MCP host.
            ---

            # Play Vibe Snake
            """ + "\n");
    }

    private static void WriteFile(string root, string relativePath, string source)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source, new UTF8Encoding(false));
    }

    private static void CopyApprovedLogo(string root)
    {
        var destination = Path.Combine(root, "assets", "images", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(
            Path.Combine(ResolveRepositoryRoot(), "assets", "images", "logo.png"),
            destination,
            overwrite: true);
    }

    private static void WritePngFixture(
        string root,
        uint width,
        uint height,
        ReadOnlySpan<byte> extra,
        ReadOnlySpan<byte> ihdrType = default,
        int? truncateTo = null)
    {
        var path = Path.Combine(root, "assets", "images", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[24 + extra.Length];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        if (ihdrType.IsEmpty)
        {
            "IHDR"u8.CopyTo(bytes.AsSpan(12));
        }
        else
        {
            ihdrType.CopyTo(bytes.AsSpan(12));
        }

        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        extra.CopyTo(bytes.AsSpan(24));
        File.WriteAllBytes(path, truncateTo is null ? bytes : bytes.AsSpan(0, truncateTo.Value).ToArray());
    }

    private static void OverwriteLogoPrefix(string root, ReadOnlySpan<byte> prefix)
    {
        var path = Path.Combine(root, "assets", "images", "logo.png");
        var bytes = File.ReadAllBytes(path);
        prefix.CopyTo(bytes);
        File.WriteAllBytes(path, bytes);
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-repository-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed record ResolverCall(
        string Executable,
        string[] Arguments,
        string WorkingDirectory,
        TimeSpan Timeout);

    private sealed class FakeDependencyResolverProcess : IDependencyResolverProcess
    {
        public List<ResolverCall> Calls { get; } = [];

        public List<ResolverProcessResult> Results { get; } = [];

        public Exception? ResolveException { get; init; }

        public Exception? RunException { get; init; }

        public bool WriteInvalidUtf8 { get; init; }

        public string RawLock { get; init; } =
            "# uv generated header\n"
            + "runtime==1.2.3 \\\n"
            + "    --hash=sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n"
            + "test==2.4.0 \\\n"
            + "    --hash=sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n";

        public string ResolveExecutable(string repositoryRoot)
        {
            if (ResolveException is not null)
            {
                throw ResolveException;
            }

            return Path.Combine(repositoryRoot, "fake-uv");
        }

        public ResolverProcessResult Run(
            string executable,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout)
        {
            if (RunException is not null)
            {
                throw RunException;
            }

            var copiedArguments = arguments.ToArray();
            Calls.Add(new ResolverCall(executable, copiedArguments, workingDirectory, timeout));
            if (copiedArguments.Length > 0 && copiedArguments[0] == "pip")
            {
                var outputIndex = Array.IndexOf(copiedArguments, "--output-file") + 1;
                if (WriteInvalidUtf8)
                {
                    File.WriteAllBytes(copiedArguments[outputIndex], [0xff]);
                }
                else
                {
                    File.WriteAllText(
                        copiedArguments[outputIndex],
                        RawLock,
                        new UTF8Encoding(false));
                }
            }

            if (Results.Count > 0)
            {
                var result = Results[0];
                Results.RemoveAt(0);
                return result;
            }

            return copiedArguments.Length == 1 && copiedArguments[0] == "--version"
                ? new ResolverProcessResult(0, "uv 0.11.33\n", string.Empty)
                : new ResolverProcessResult(0, string.Empty, string.Empty);
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
}
