using System.Text;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class RepositoryChecksTests
{
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
    public void Command_has_stable_usage_and_combined_success_paths()
    {
        var invalidOutput = new StringWriter();
        var invalidError = new StringWriter();

        var invalidCode = RepositoryCheckCommand.Run([], invalidOutput, invalidError);

        Assert.Equal(2, invalidCode);
        Assert.Equal(string.Empty, invalidOutput.ToString());
        Assert.Contains("RepositoryChecks <all|docs|version>", invalidError.ToString());

        WithTemporaryDirectory(root =>
        {
            WriteVersionFixture(root);
            WriteDocumentationFixture(root);
            var output = new StringWriter();
            var error = new StringWriter();

            var code = RepositoryCheckCommand.Run(["all", root], output, error);

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Contains("Product versions aligned", output.ToString());
            Assert.Contains("Documentation link check passed", output.ToString());
        });
    }

    [Theory]
    [InlineData("docs")]
    [InlineData("version")]
    public void Command_runs_each_individual_check(string command)
    {
        WithTemporaryDirectory(root =>
        {
            WriteVersionFixture(root);
            WriteDocumentationFixture(root);
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

        Assert.True(version.Passed, string.Join(Environment.NewLine, version.Failures));
        Assert.True(docs.Passed, string.Join(Environment.NewLine, docs.Failures));
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

    private static void WriteFile(string root, string relativePath, string source)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source, new UTF8Encoding(false));
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
