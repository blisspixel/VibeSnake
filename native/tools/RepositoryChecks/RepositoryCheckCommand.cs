using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VibeSnake.Rules;

namespace RepositoryChecks;

public sealed record RepositoryCheckResult(
    string Name,
    bool Passed,
    string SuccessMessage,
    IReadOnlyList<string> Failures);

public static class ProductVersionCheck
{
    private static readonly Regex ProductVersionPattern = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(alpha|beta|rc)\.([1-9][0-9]*))?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex PackageVersionPattern = new(
        "^version\\s*=\\s*\"([^\"]+)\"\\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex NativeVersionPattern = new(
        "public const string AppVersion = \"([^\"]+)\";",
        RegexOptions.CultureInvariant);

    private static readonly Regex PythonVersionPattern = new(
        "__version__ = \"([^\"]+)\"",
        RegexOptions.CultureInvariant);

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var failures = new List<string>();

        string canonicalVersion;
        string packageVersion;
        try
        {
            canonicalVersion = ReadCanonicalVersion(root);
            packageVersion = MapPackageVersion(canonicalVersion);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or InvalidDataException)
        {
            failures.Add(SingleLine(exception.Message));
            return Failed(failures);
        }

        var packageValue = ReadSingleValue(
            root,
            "pyproject.toml",
            PackageVersionPattern,
            "package version",
            failures);
        var nativeValue = ReadSingleValue(
            root,
            Path.Combine("game", "scripts", "ProductIdentity.cs"),
            NativeVersionPattern,
            "ProductIdentity.AppVersion",
            failures);
        var pythonValue = ReadSingleValue(
            root,
            Path.Combine("src", "vibesnake", "__init__.py"),
            PythonVersionPattern,
            "Python fallback version",
            failures);

        if (failures.Count == 0
            && (nativeValue != canonicalVersion
                || packageValue != packageVersion
                || pythonValue != packageVersion))
        {
            failures.Add(
                "Product version mismatch: "
                + $"VERSION='{canonicalVersion}' "
                + $"pyproject.toml='{packageValue}' "
                + $"Python fallback='{pythonValue}' "
                + $"ProductIdentity.AppVersion='{nativeValue}'; "
                + $"expected package version='{packageVersion}'");
        }

        return failures.Count == 0
            ? new RepositoryCheckResult(
                "Product version alignment",
                true,
                $"Product versions aligned: product={canonicalVersion} package={packageVersion}",
                [])
            : Failed(failures);
    }

    public static string ReadCanonicalVersion(string repositoryRoot)
    {
        var relativePath = "VERSION";
        var path = Path.Combine(repositoryRoot, relativePath);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Could not read canonical product version from {relativePath}.",
                exception);
        }

        string source;
        try
        {
            source = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("VERSION must contain valid UTF-8.", exception);
        }

        if (!source.EndsWith('\n')
            || source.Count(character => character == '\n') != 1
            || source.Contains('\r'))
        {
            throw new InvalidDataException(
                "VERSION must contain exactly one UTF-8 line terminated by LF.");
        }

        var version = source[..^1];
        if (!ProductVersionPattern.IsMatch(version))
        {
            throw new InvalidDataException(
                $"VERSION must contain one canonical stable or prerelease SemVer; got '{version}'.");
        }

        return version;
    }

    public static string MapPackageVersion(string productVersion)
    {
        ArgumentNullException.ThrowIfNull(productVersion);
        var match = ProductVersionPattern.Match(productVersion);
        if (!match.Success)
        {
            throw new InvalidDataException(
                $"Unsupported canonical product version: '{productVersion}'.");
        }

        var stable = $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";
        if (!match.Groups[4].Success)
        {
            return stable;
        }

        var marker = match.Groups[4].Value switch
        {
            "alpha" => "a",
            "beta" => "b",
            "rc" => "rc",
            _ => throw new InvalidDataException("Unsupported product prerelease kind."),
        };
        return stable + marker + match.Groups[5].Value;
    }

    private static RepositoryCheckResult Failed(IReadOnlyList<string> failures) =>
        new("Product version alignment", false, string.Empty, failures);

    private static string? ReadSingleValue(
        string repositoryRoot,
        string relativePath,
        Regex pattern,
        string valueName,
        List<string> failures)
    {
        string source;
        try
        {
            source = File.ReadAllText(
                Path.Combine(repositoryRoot, relativePath),
                new UTF8Encoding(false, true));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException)
        {
            failures.Add($"Could not read {relativePath} as UTF-8 text.");
            return null;
        }

        var matches = pattern.Matches(source);
        if (matches.Count != 1)
        {
            failures.Add(
                $"Could not parse exactly one {valueName} from {relativePath}; found {matches.Count}.");
            return null;
        }

        return matches[0].Groups[1].Value;
    }

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

public static class DocumentationCheck
{
    private static readonly Regex LinkPattern = new(
        @"!?\[[^\]]*\]\(([^)]+)\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex ContractReleasePattern = new(
        @"contracts to `(?<version>\d+\.\d+\.\d+)` with rules resource (?<resource>v\d+)",
        RegexOptions.CultureInvariant);

    private static readonly string[] RootDocuments =
    [
        "README.md",
        "ROADMAP.md",
        "CHANGELOG.md",
        "CODE_OF_CONDUCT.md",
        "CONTRIBUTING.md",
        "SECURITY.md",
        "SUPPORT.md",
    ];

    private static readonly string[] SupportingDocuments =
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

    private static readonly HashSet<string> ExternalSchemes = new(
        ["http", "https", "mailto", "tel", "data"],
        StringComparer.OrdinalIgnoreCase);

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var failures = new List<string>();
        var documents = CanonicalDocuments(root, failures);

        foreach (var document in documents)
        {
            var relativeDocument = RelativePath(root, document);
            if (!File.Exists(document))
            {
                failures.Add($"missing canonical document: {relativeDocument}");
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(document, new UTF8Encoding(false, true));
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or DecoderFallbackException)
            {
                failures.Add($"{relativeDocument}: could not read UTF-8 text.");
                continue;
            }

            foreach (var (lineNumber, target) in LinkTargets(lines))
            {
                string? localPath;
                try
                {
                    localPath = ResolveLocalPath(root, document, target);
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                        or NotSupportedException
                        or UriFormatException)
                {
                    failures.Add($"{relativeDocument}:{lineNumber}: invalid target {target}");
                    continue;
                }

                if (localPath is not null
                    && !File.Exists(localPath)
                    && !Directory.Exists(localPath))
                {
                    failures.Add($"{relativeDocument}:{lineNumber}: missing target {target}");
                }
            }
        }

        failures.AddRange(ChangelogContractFailures(root));
        return failures.Count == 0
            ? new RepositoryCheckResult(
                "Documentation",
                true,
                $"Documentation link check passed for {documents.Length} canonical files.",
                [])
            : new RepositoryCheckResult("Documentation", false, string.Empty, failures);
    }

    public static IReadOnlyList<(int LineNumber, string Target)> LinkTargets(
        IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var targets = new List<(int LineNumber, string Target)>();
        var inFence = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                continue;
            }

            targets.AddRange(LinkPattern.Matches(line)
                .Select(match => (index + 1, match.Groups[1].Value.Trim())));
        }

        return targets;
    }

    private static string[] CanonicalDocuments(
        string repositoryRoot,
        List<string> failures)
    {
        var documents = RootDocuments
            .Select(path => Path.Combine(repositoryRoot, path))
            .ToList();
        var docsRoot = Path.Combine(repositoryRoot, "docs");
        if (!Directory.Exists(docsRoot))
        {
            failures.Add("missing canonical document tree: docs");
        }
        else
        {
            documents.AddRange(Directory
                .EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(docsRoot, path)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains("research", StringComparer.Ordinal))
                .OrderBy(path => RelativePath(repositoryRoot, path), StringComparer.Ordinal));
        }

        documents.AddRange(SupportingDocuments.Select(path => Path.Combine(repositoryRoot, path)));
        return documents
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static List<string> ChangelogContractFailures(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "CHANGELOG.md");
        if (!File.Exists(path))
        {
            return ["missing CHANGELOG.md"];
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, new UTF8Encoding(false, true));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException)
        {
            return ["CHANGELOG.md: could not read UTF-8 text."];
        }

        var failures = new List<string>();
        var versions = new Dictionary<string, int>(StringComparer.Ordinal);
        var resources = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Length; index++)
        {
            var match = ContractReleasePattern.Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var lineNumber = index + 1;
            var version = match.Groups["version"].Value;
            var resource = match.Groups["resource"].Value;
            if (versions.TryGetValue(version, out var versionLine))
            {
                failures.Add(
                    $"CHANGELOG.md:{lineNumber}: agent contract version {version} is already "
                    + $"claimed on line {versionLine}; each entry names its own release");
            }
            else
            {
                versions.Add(version, lineNumber);
            }

            if (resources.TryGetValue(resource, out var resourceLine))
            {
                failures.Add(
                    $"CHANGELOG.md:{lineNumber}: rules resource {resource} is already "
                    + $"claimed on line {resourceLine}; each entry names its own resource");
            }
            else
            {
                resources.Add(resource, lineNumber);
            }
        }

        return failures;
    }

    private static string? ResolveLocalPath(
        string repositoryRoot,
        string document,
        string target)
    {
        if (target.StartsWith('#'))
        {
            return null;
        }

        if (target.StartsWith('<') && target.EndsWith('>'))
        {
            target = target[1..^1];
        }

        var schemeSeparator = target.IndexOf(':');
        if (schemeSeparator > 0
            && (ExternalSchemes.Contains(target[..schemeSeparator])
                || target[(schemeSeparator + 1)..].StartsWith("//", StringComparison.Ordinal)))
        {
            return null;
        }

        if (target.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        var delimiter = target.IndexOfAny(['?', '#']);
        var pathText = delimiter < 0 ? target : target[..delimiter];
        pathText = Uri.UnescapeDataString(pathText);
        if (pathText.Length == 0)
        {
            return null;
        }

        return pathText.StartsWith('/')
            ? Path.GetFullPath(Path.Combine(repositoryRoot, pathText.TrimStart('/')))
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document)!, pathText));
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

}

public static class RepositoryCheckCommand
{
    public static int Run(
        IReadOnlyList<string>? arguments,
        TextWriter standardOutput,
        TextWriter standardError) =>
        RunCore(arguments, standardOutput, standardError, resolver: null, upstreamFetch: null);

    internal static int Run(
        IReadOnlyList<string>? arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        IDependencyResolverProcess resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return RunCore(arguments, standardOutput, standardError, resolver, upstreamFetch: null);
    }

    internal static int Run(
        IReadOnlyList<string>? arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        Func<string, byte[]> upstreamFetch)
    {
        ArgumentNullException.ThrowIfNull(upstreamFetch);
        return RunCore(arguments, standardOutput, standardError, resolver: null, upstreamFetch);
    }

    private static int RunCore(
        IReadOnlyList<string>? arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        IDependencyResolverProcess? resolver,
        Func<string, byte[]>? upstreamFetch)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "achievement-candidates-write")
        {
            return RunAchievementCandidatesWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "last-stand-write")
        {
            return RunLastStandWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "phase-shift-write")
        {
            return RunPhaseShiftWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "shield-write")
        {
            return RunShieldWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "remaining-powers-write")
        {
            return RunRemainingPowersWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "core-rules-write")
        {
            return RunCoreRulesWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "movement-write")
        {
            return RunMovementWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "knowledge-write")
        {
            return RunKnowledgeWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "interop-write")
        {
            return RunInteropWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "freeze-baseline")
        {
            return RunFreezeBaseline(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "lock-write")
        {
            return RunLockWrite(arguments, standardOutput, standardError, resolver);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "plugin")
        {
            return RunPlugin(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "host-package")
        {
            return RunHostPackage(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "badge-write")
        {
            return RunBadgeWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "inventory-write")
        {
            return RunInventoryWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "screenshots-write")
        {
            return RunScreenshotWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "materials-write")
        {
            return RunMaterialsWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "materials-candidate")
        {
            return RunMaterialsCandidate(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "rehearsal-write")
        {
            return RunRehearsalWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "rehearsal-record")
        {
            return RunRehearsalRecord(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "stable-write")
        {
            return RunStableWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "stable-record")
        {
            return RunStableRecord(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "manual-matrix-write")
        {
            return RunManualMatrixWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "manual-matrix-record")
        {
            return RunManualMatrixRecord(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "external-validation-write")
        {
            return RunExternalValidationWrite(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "external-validation-record")
        {
            return RunExternalValidationRecord(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "release-matrix")
        {
            return RunReleaseMatrix(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "unsigned-preview")
        {
            return RunUnsignedPreview(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "content-packs")
        {
            return RunContentPacks(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "radio-pack")
        {
            return RunRadioPack(arguments, standardOutput, standardError);
        }

        if (arguments is not null
            && arguments.Count > 0
            && arguments[0] == "interop-upstream")
        {
            return RunInteropUpstream(arguments, standardOutput, standardError, upstreamFetch);
        }

        if (arguments is null
            || arguments.Count is < 1 or > 2
            || arguments[0] is not ("achievement-candidates" or "all" or "badges" or "core-rules" or "docs" or "external-validation" or "freeze" or "interop" or "inventory" or "inventory-release" or "knowledge" or "last-stand" or "locks" or "logo" or "manual-matrix" or "materials" or "movement" or "phase-shift" or "rehearsal" or "remaining-powers" or "screenshots" or "shield" or "source" or "stable" or "version"))
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 2 ? arguments[1] : ".");
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            standardError.WriteLine("Repository root is invalid.");
            return 2;
        }

        var results = arguments[0] switch
        {
            "achievement-candidates" => new[]
            {
                AchievementCandidateFixtureCheck.Inspect(repositoryRoot),
            },
            "badges" => new[] { StationBadgeCheck.Inspect(repositoryRoot) },
            "core-rules" => new[] { CoreRulesFixtureCheck.Inspect(repositoryRoot) },
            "docs" => new[] { DocumentationCheck.Inspect(repositoryRoot) },
            "freeze" => new[] { CandidateFreezeCheck.Inspect(repositoryRoot) },
            "interop" => new[] { AgentInteropCheck.Inspect(repositoryRoot) },
            "inventory" => new[] { ContentInventoryCheck.Inspect(repositoryRoot) },
            "inventory-release" => new[] { ContentInventoryCheck.Inspect(repositoryRoot, requireReleaseReady: true) },
            "knowledge" => new[] { AgentKnowledgeCheck.Inspect(repositoryRoot) },
            "last-stand" => new[] { LastStandFixtureCheck.Inspect(repositoryRoot) },
            "locks" => new[] { DependencyLockCheck.Inspect(repositoryRoot) },
            "external-validation" => new[] { ExternalValidationCheck.Inspect(repositoryRoot) },
            "logo" => new[] { ProjectLogoCheck.Inspect(repositoryRoot) },
            "manual-matrix" => new[] { ManualProductMatrixCheck.Inspect(repositoryRoot) },
            "materials" => new[] { ReleaseMaterialsCheck.Inspect(repositoryRoot) },
            "movement" => new[] { MovementFixtureCheck.Inspect(repositoryRoot) },
            "phase-shift" => new[] { PhaseShiftFixtureCheck.Inspect(repositoryRoot) },
            "rehearsal" => new[] { ReleaseRehearsalCheck.Inspect(repositoryRoot) },
            "remaining-powers" => new[] { RemainingPowersFixtureCheck.Inspect(repositoryRoot) },
            "screenshots" => new[] { ReadmeScreenshotCheck.Inspect(repositoryRoot) },
            "shield" => new[] { ShieldFixtureCheck.Inspect(repositoryRoot) },
            "source" => new[] { SourcePolicyCheck.Inspect(repositoryRoot) },
            "stable" => new[] { StablePromotionCheck.Inspect(repositoryRoot) },
            "version" => new[] { ProductVersionCheck.Inspect(repositoryRoot) },
            _ => new[]
            {
                AchievementCandidateFixtureCheck.Inspect(repositoryRoot),
                LastStandFixtureCheck.Inspect(repositoryRoot),
                PhaseShiftFixtureCheck.Inspect(repositoryRoot),
                ShieldFixtureCheck.Inspect(repositoryRoot),
                RemainingPowersFixtureCheck.Inspect(repositoryRoot),
                CoreRulesFixtureCheck.Inspect(repositoryRoot),
                MovementFixtureCheck.Inspect(repositoryRoot),
                AgentInteropCheck.Inspect(repositoryRoot),
                AgentKnowledgeCheck.Inspect(repositoryRoot),
                ProductVersionCheck.Inspect(repositoryRoot),
                DocumentationCheck.Inspect(repositoryRoot),
                CandidateFreezeCheck.Inspect(repositoryRoot),
                ContentInventoryCheck.Inspect(repositoryRoot),
                DependencyLockCheck.Inspect(repositoryRoot),
                ProjectLogoCheck.Inspect(repositoryRoot),
                ReleaseMaterialsCheck.Inspect(repositoryRoot),
                ReleaseRehearsalCheck.Inspect(repositoryRoot),
                StablePromotionCheck.Inspect(repositoryRoot),
                ManualProductMatrixCheck.Inspect(repositoryRoot),
                ExternalValidationCheck.Inspect(repositoryRoot),
                ReadmeScreenshotCheck.Inspect(repositoryRoot),
                StationBadgeCheck.Inspect(repositoryRoot),
                SourcePolicyCheck.Inspect(repositoryRoot),
                AgentPluginCheck.Inspect(
                    Path.Combine(repositoryRoot, "integrations", "vibesnake-agent-plugin")),
            },
        };

        var passed = true;
        foreach (var result in results)
        {
            if (result.Passed)
            {
                standardOutput.WriteLine(result.SuccessMessage);
                continue;
            }

            passed = false;
            standardError.WriteLine(result.Name + " check failed:");
            foreach (var failure in result.Failures)
            {
                standardError.WriteLine("  " + failure);
            }
        }

        return passed ? 0 : 1;
    }

    private static int RunAchievementCandidatesWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        return ReportSingleResult(
            AchievementCandidateFixtureCheck.Write(
                arguments.Count == 2 ? arguments[1] : "."),
            standardOutput,
            standardError);
    }

    private static int RunLastStandWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        return ReportSingleResult(
            LastStandFixtureCheck.Write(
                arguments.Count == 2 ? arguments[1] : "."),
            standardOutput,
            standardError);
    }

    private static int RunPhaseShiftWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        return ReportSingleResult(
            PhaseShiftFixtureCheck.Write(
                arguments.Count == 2 ? arguments[1] : "."),
            standardOutput,
            standardError);
    }

    private static int RunShieldWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        return ReportSingleResult(
            ShieldFixtureCheck.Write(
                arguments.Count == 2 ? arguments[1] : "."),
            standardOutput,
            standardError);
    }

    private static int RunRemainingPowersWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        return ReportSingleResult(
            RemainingPowersFixtureCheck.Write(
                arguments.Count == 2 ? arguments[1] : "."),
            standardOutput,
            standardError);
    }

    private static int RunCoreRulesWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        return ReportSingleResult(
            CoreRulesFixtureCheck.Write(
                arguments.Count == 2 ? arguments[1] : "."),
            standardOutput,
            standardError);
    }

    private static int RunMovementWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        return ReportSingleResult(
            MovementFixtureCheck.Write(
                arguments.Count == 2 ? arguments[1] : "."),
            standardOutput,
            standardError);
    }

    private static int RunKnowledgeWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        return ReportSingleResult(
            AgentKnowledgeCheck.Write(
                arguments.Count == 2 ? arguments[1] : "."),
            standardOutput,
            standardError);
    }

    private static int RunInteropWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        return ReportSingleResult(
            AgentInteropCheck.WriteDigests(
                arguments.Count == 2 ? arguments[1] : "."),
            standardOutput,
            standardError);
    }

    private static int RunMaterialsWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 2 or > 3)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 3 ? arguments[2] : ".");
            outputPath = Path.GetFullPath(arguments[1], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Repository root or release-material output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            ReleaseMaterialsCheck.WriteFoundationHandoff(repositoryRoot, outputPath),
            standardOutput,
            standardError);
    }

    private static int RunMaterialsCandidate(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 4 or > 5)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string candidatePath;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 5 ? arguments[4] : ".");
            candidatePath = Path.GetFullPath(arguments[1], repositoryRoot);
            outputPath = Path.GetFullPath(arguments[3], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine(
                "Repository root, release-material candidate, or output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            ReleaseMaterialsCheck.WriteCandidateHandoff(
                repositoryRoot,
                candidatePath,
                arguments[2],
                outputPath),
            standardOutput,
            standardError);
    }

    private static int RunRehearsalWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 2 or > 3)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 3 ? arguments[2] : ".");
            outputPath = Path.GetFullPath(arguments[1], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Repository root or release-rehearsal output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            ReleaseRehearsalCheck.WriteFoundationHandoff(repositoryRoot, outputPath),
            standardOutput,
            standardError);
    }

    private static int RunRehearsalRecord(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 4 or > 5)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string recordPath;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 5 ? arguments[4] : ".");
            recordPath = Path.GetFullPath(arguments[1], repositoryRoot);
            outputPath = Path.GetFullPath(arguments[3], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine(
                "Repository root, release-rehearsal record, or output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            ReleaseRehearsalCheck.WriteRecordHandoff(
                repositoryRoot,
                recordPath,
                arguments[2],
                outputPath),
            standardOutput,
            standardError);
    }

    private static int RunStableWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 2 or > 3)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 3 ? arguments[2] : ".");
            outputPath = Path.GetFullPath(arguments[1], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Repository root or stable-promotion output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            StablePromotionCheck.WriteFoundationHandoff(repositoryRoot, outputPath),
            standardOutput,
            standardError);
    }

    private static int RunStableRecord(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 4 or > 5)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string recordPath;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 5 ? arguments[4] : ".");
            recordPath = Path.GetFullPath(arguments[1], repositoryRoot);
            outputPath = Path.GetFullPath(arguments[3], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine(
                "Repository root, stable-promotion record, or output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            StablePromotionCheck.WriteRecordHandoff(
                repositoryRoot,
                recordPath,
                arguments[2],
                outputPath),
            standardOutput,
            standardError);
    }

    private static int RunManualMatrixWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 2 or > 3)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 3 ? arguments[2] : ".");
            outputPath = Path.GetFullPath(arguments[1], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Repository root or manual-product output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            ManualProductMatrixCheck.WriteFoundationHandoff(repositoryRoot, outputPath),
            standardOutput,
            standardError,
            "Manual product matrix qualification failed:");
    }

    private static int RunManualMatrixRecord(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 4 or > 5)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string sessionsDirectory;
        string candidatePath;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 5 ? arguments[4] : ".");
            sessionsDirectory = Path.GetFullPath(arguments[1], repositoryRoot);
            candidatePath = Path.GetFullPath(arguments[2], repositoryRoot);
            outputPath = Path.GetFullPath(arguments[3], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine(
                "Repository root, manual-product sessions, candidate, or output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            ManualProductMatrixCheck.RecordSessions(
                repositoryRoot,
                sessionsDirectory,
                candidatePath,
                outputPath),
            standardOutput,
            standardError,
            "Manual product matrix qualification failed:");
    }

    private static int RunExternalValidationWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 2 or > 3)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 3 ? arguments[2] : ".");
            outputPath = Path.GetFullPath(arguments[1], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Repository root or external-validation output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            ExternalValidationCheck.WriteFoundationHandoff(repositoryRoot, outputPath),
            standardOutput,
            standardError,
            "External validation qualification failed:");
    }

    private static int RunExternalValidationRecord(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 5 or > 6)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string sessionsDirectory;
        string candidateLedgerPath;
        string findingsPath;
        string outputPath;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 6 ? arguments[5] : ".");
            sessionsDirectory = Path.GetFullPath(arguments[1], repositoryRoot);
            candidateLedgerPath = Path.GetFullPath(arguments[2], repositoryRoot);
            findingsPath = Path.GetFullPath(arguments[3], repositoryRoot);
            outputPath = Path.GetFullPath(arguments[4], repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine(
                "Repository root, external-validation sessions, ledger, findings, or output is invalid.");
            return 2;
        }

        return ReportSingleResult(
            ExternalValidationCheck.Record(
                repositoryRoot,
                sessionsDirectory,
                candidateLedgerPath,
                findingsPath,
                outputPath),
            standardOutput,
            standardError,
            "External validation qualification failed:");
    }

    private static int ReportSingleResult(
        RepositoryCheckResult result,
        TextWriter standardOutput,
        TextWriter standardError,
        string? failureHeading = null)
    {
        if (result.Passed)
        {
            standardOutput.WriteLine(result.SuccessMessage);
            return 0;
        }

        standardError.WriteLine(failureHeading ?? result.Name + " check failed:");
        foreach (var failure in result.Failures)
        {
            standardError.WriteLine("  " + failure);
        }

        return 1;
    }

    private static int RunScreenshotWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 2 or > 3)
        {
            WriteUsage(standardError);
            return 2;
        }

        var result = ReadmeScreenshotCheck.Capture(
            arguments.Count == 3 ? arguments[2] : ".",
            arguments[1]);
        if (result.Passed)
        {
            standardOutput.WriteLine(result.SuccessMessage);
            return 0;
        }

        standardError.WriteLine(result.Name + " generation failed:");
        foreach (var failure in result.Failures)
        {
            standardError.WriteLine("  " + failure);
        }

        return 1;
    }

    private static int RunInventoryWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        var result = ContentInventoryCheck.Write(
            arguments.Count == 2 ? arguments[1] : ".");
        if (result.Passed)
        {
            standardOutput.WriteLine(result.SuccessMessage);
            return 0;
        }

        standardError.WriteLine(result.Name + " generation failed:");
        foreach (var failure in result.Failures)
        {
            standardError.WriteLine("  " + failure);
        }

        return 1;
    }

    private static int RunBadgeWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count > 2)
        {
            WriteUsage(standardError);
            return 2;
        }

        var result = StationBadgeCheck.Write(arguments.Count == 2 ? arguments[1] : ".");
        if (result.Passed)
        {
            standardOutput.WriteLine(result.SuccessMessage);
            return 0;
        }

        standardError.WriteLine(result.Name + " generation failed:");
        foreach (var failure in result.Failures)
        {
            standardError.WriteLine("  " + failure);
        }

        return 1;
    }

    private static int RunPlugin(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 2 or > 3
            || (arguments.Count == 3 && arguments[2] != "--require-mcp"))
        {
            WriteUsage(standardError);
            return 2;
        }

        RepositoryCheckResult result;
        try
        {
            result = AgentPluginCheck.Inspect(
                Path.GetFullPath(arguments[1]),
                requireMcp: arguments.Count == 3);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Agent Plugin root is invalid.");
            return 2;
        }

        if (result.Passed)
        {
            standardOutput.WriteLine(result.SuccessMessage);
            return 0;
        }

        standardError.WriteLine(result.Name + " check failed:");
        foreach (var failure in result.Failures)
        {
            standardError.WriteLine("  " + failure);
        }

        return 1;
    }

    private static int RunHostPackage(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 2 or > 3)
        {
            WriteUsage(standardError);
            return 2;
        }

        string packageRoot;
        try
        {
            packageRoot = Path.GetFullPath(arguments[1]);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Agent Host package root is invalid.");
            return 2;
        }

        string repositoryRoot;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 3 ? arguments[2] : ".");
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Repository root is invalid.");
            return 2;
        }

        RepositoryCheckResult result;
        try
        {
            result = AgentHostPackageCheck.Inspect(packageRoot, repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Agent Host package root is invalid.");
            return 2;
        }

        if (result.Passed)
        {
            standardOutput.WriteLine(result.SuccessMessage);
            return 0;
        }

        standardError.WriteLine(result.Name + " check failed:");
        foreach (var failure in result.Failures)
        {
            standardError.WriteLine("  " + failure);
        }

        return 1;
    }

    private static int RunReleaseMatrix(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count != 5)
        {
            WriteUsage(standardError);
            return 2;
        }

        string downloadRoot;
        string outputPath;
        try
        {
            downloadRoot = Path.GetFullPath(arguments[1]);
            outputPath = Path.GetFullPath(arguments[4]);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Release matrix download root or output is invalid.");
            return 2;
        }

        ReleaseMatrixCheck.Qualification qualification;
        try
        {
            qualification = ReleaseMatrixCheck.Qualify(downloadRoot, arguments[2], arguments[3]);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Release matrix download root or output is invalid.");
            return 2;
        }

        try
        {
            var parent = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(parent))
            {
                standardError.WriteLine("Release matrix output is invalid.");
                return 2;
            }

            Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, qualification.Json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            standardError.WriteLine(
                "Release matrix qualification failed: "
                + exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim());
            return 1;
        }

        if (!qualification.Passed)
        {
            standardError.WriteLine("Release matrix qualification failed:");
            foreach (var error in qualification.Errors)
            {
                standardError.WriteLine("  " + error);
            }

            return 1;
        }

        var platforms = 0;
        using (var evidence = JsonDocument.Parse(qualification.Json))
        {
            platforms = evidence.RootElement.GetProperty("platforms").GetArrayLength();
        }

        standardOutput.WriteLine(
            "Release matrix qualification passed for "
            + platforms.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " platforms at "
            + arguments[2]
            + ".");
        return 0;
    }

    private static int RunUnsignedPreview(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count != 9)
        {
            WriteUsage(standardError);
            return 2;
        }

        string channelRoot;
        string provenanceRoot;
        string radioPackRoot;
        string matrixPath;
        string versionRoot;
        string outputRoot;
        try
        {
            channelRoot = Path.GetFullPath(arguments[1]);
            provenanceRoot = Path.GetFullPath(arguments[2]);
            radioPackRoot = Path.GetFullPath(arguments[3]);
            matrixPath = Path.GetFullPath(arguments[4]);
            versionRoot = Path.GetFullPath(arguments[5]);
            outputRoot = Path.GetFullPath(arguments[8]);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Unsigned preview input or output path is invalid.");
            return 2;
        }

        UnsignedPreviewCheck.Assembly assembly;
        try
        {
            assembly = UnsignedPreviewCheck.Assemble(
                channelRoot,
                provenanceRoot,
                radioPackRoot,
                matrixPath,
                versionRoot,
                arguments[6],
                arguments[7],
                outputRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Unsigned preview input or output path is invalid.");
            return 2;
        }

        if (!assembly.Passed)
        {
            standardError.WriteLine("Unsigned native alpha preview assembly failed:");
            foreach (var error in assembly.Errors)
            {
                standardError.WriteLine("  " + error);
            }

            return 1;
        }

        var productVersion = string.Empty;
        var platforms = 0;
        using (var evidence = JsonDocument.Parse(assembly.Json))
        {
            productVersion = evidence.RootElement.GetProperty("productVersion").GetString() ?? string.Empty;
            platforms = evidence.RootElement.GetProperty("packages").GetArrayLength();
        }

        standardOutput.WriteLine(
            "Unsigned native alpha preview assembled: version="
            + productVersion
            + " platforms="
            + platforms.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return 0;
    }

    private static int RunLockWrite(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        IDependencyResolverProcess? resolver)
    {
        if (arguments.Count is < 2 or > 3
            || arguments[1] is not ("ci" or "runtime"))
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 3 ? arguments[2] : ".");
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            standardError.WriteLine("Repository root is invalid.");
            return 2;
        }

        try
        {
            var count = resolver is null
                ? DependencyLockCheck.WriteProfile(repositoryRoot, arguments[1])
                : DependencyLockCheck.WriteProfile(repositoryRoot, arguments[1], resolver);
            standardOutput.WriteLine(
                $"Python {arguments[1]} dependency lock written: packages={count}");
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            standardError.WriteLine(
                "Dependency lock generation failed: "
                + exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim());
            return 1;
        }
    }

    private static int RunFreezeBaseline(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (arguments.Count is < 3 or > 5)
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string? outputPath = null;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count >= 4 ? arguments[3] : ".");
            if (arguments.Count == 5)
            {
                outputPath = Path.GetFullPath(
                    Path.IsPathRooted(arguments[4])
                        ? arguments[4]
                        : Path.Combine(repositoryRoot, arguments[4]));
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            standardError.WriteLine("Repository root or baseline output is invalid.");
            return 2;
        }

        try
        {
            var count = CandidateFreezeCheck.WriteBaseline(
                repositoryRoot,
                arguments[1],
                arguments[2],
                outputPath);
            standardOutput.WriteLine(
                $"Prepared candidate freeze baseline with {count} files.");
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            standardError.WriteLine(
                "Candidate freeze baseline preparation failed: "
                + exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim());
            return 1;
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(
            "Usage: RepositoryChecks <achievement-candidates|all|badges|core-rules|docs|external-validation|freeze|interop|inventory|inventory-release|knowledge|last-stand|locks|logo|manual-matrix|materials|movement|phase-shift|rehearsal|remaining-powers|screenshots|shield|source|stable|version> "
            + "[repository-root]");
        writer.WriteLine(
            "       RepositoryChecks achievement-candidates-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks last-stand-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks phase-shift-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks shield-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks remaining-powers-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks core-rules-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks movement-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks knowledge-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks interop-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks badge-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks inventory-write [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks screenshots-write <godot-executable> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks materials-write <output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks materials-candidate <candidate> <expected-revision> "
            + "<output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks rehearsal-write <output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks rehearsal-record <record> <expected-revision> "
            + "<output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks stable-write <output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks stable-record <record> <expected-revision> "
            + "<output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks manual-matrix-write <output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks manual-matrix-record <sessions-directory> <candidate> "
            + "<output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks external-validation-write <output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks external-validation-record <sessions-directory> "
            + "<candidate-ledger> <findings> <output> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks freeze-baseline <revision> <generated-utc> "
            + "[repository-root] [output]");
        writer.WriteLine(
            "       RepositoryChecks lock-write <ci|runtime> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks plugin <plugin-root> [--require-mcp]");
        writer.WriteLine(
            "       RepositoryChecks host-package <package-root> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks release-matrix <download-root> <expected-revision> <Debug|Release> <output>");
        writer.WriteLine(
            "       RepositoryChecks unsigned-preview <channel-root> <provenance-root> <radio-pack-root> "
            + "<matrix> <version-root> <tag> <expected-revision> <output>");
        writer.WriteLine(
            "       RepositoryChecks content-packs <repository-root> <manifest> [manifest ...] "
            + "[--inventory <path>] [--game-version <version>] [--ruleset-id <id>] [--ruleset-version <version>]");
        writer.WriteLine(
            "       RepositoryChecks radio-pack <repository-root> <manifest> <output> "
            + "[--curation <path>] [--inventory <path>]");
        writer.WriteLine(
            "       RepositoryChecks interop-upstream [repository-root]");
    }

    private const string DefaultGameVersion = "0.3.0";

    private readonly record struct ContentPackInvocation(
        string RepositoryRoot,
        string[] Manifests,
        string? InventoryPath,
        string GameVersion,
        string RulesetId,
        int RulesetVersion);

    private readonly record struct RadioPackInvocation(
        string RepositoryRoot,
        string ManifestPath,
        string OutputPath,
        string? CurationPath,
        string? InventoryPath);

    private static int RunContentPacks(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (!TryReadContentPackInvocation(arguments, out var invocation))
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string[] manifests;
        string? inventoryPath;
        try
        {
            repositoryRoot = Path.GetFullPath(invocation.RepositoryRoot);
            manifests = invocation.Manifests.Select(Path.GetFullPath).ToArray();
            inventoryPath = invocation.InventoryPath is null
                ? null
                : Path.GetFullPath(invocation.InventoryPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Content pack input path is invalid.");
            return 2;
        }

        try
        {
            var result = ContentPackQualificationCheck.Qualify(
                repositoryRoot,
                manifests,
                inventoryPath,
                invocation.GameVersion,
                invocation.RulesetId,
                invocation.RulesetVersion);
            foreach (var line in result.Lines)
            {
                standardOutput.WriteLine(line);
            }

            return result.Passed ? 0 : 1;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or DecoderFallbackException
                or InvalidDataException
                or IOException
                or JsonException
                or UnauthorizedAccessException)
        {
            standardError.WriteLine(
                "Content pack qualification failed: "
                + exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim());
            return 1;
        }
    }

    private static int RunRadioPack(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        if (!TryReadRadioPackInvocation(arguments, out var invocation))
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        string manifestPath;
        string outputPath;
        string curationPath;
        string inventoryPath;
        try
        {
            repositoryRoot = Path.GetFullPath(invocation.RepositoryRoot);
            manifestPath = Path.GetFullPath(invocation.ManifestPath);
            outputPath = Path.GetFullPath(invocation.OutputPath);
            curationPath = Path.GetFullPath(
                invocation.CurationPath
                ?? Path.Combine(repositoryRoot, "config", "content_curation_v1.json"));
            inventoryPath = Path.GetFullPath(
                invocation.InventoryPath
                ?? Path.Combine(repositoryRoot, ContentInventoryCheck.InventoryRelativePath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Radio pack input or output path is invalid.");
            return 2;
        }

        try
        {
            var evidence = RadioPackAssemblyCheck.Assemble(
                repositoryRoot,
                manifestPath,
                curationPath,
                inventoryPath,
                outputPath);
            standardOutput.WriteLine(
                "Approved radio pack assembled: "
                + evidence.PackFileName
                + " tracks="
                + evidence.TrackCount.ToString(CultureInfo.InvariantCulture)
                + " sha256="
                + evidence.PackSha256);
            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or DecoderFallbackException
                or InvalidDataException
                or IOException
                or JsonException
                or UnauthorizedAccessException)
        {
            standardError.WriteLine(
                "Radio pack assembly failed: "
                + exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim());
            return 1;
        }
    }

    private static bool TryReadContentPackInvocation(
        IReadOnlyList<string> arguments,
        out ContentPackInvocation invocation)
    {
        invocation = default;
        var positionals = new List<string>();
        string? inventoryPath = null;
        var gameVersion = DefaultGameVersion;
        var rulesetId = RulesetIdentity.CurrentId;
        var rulesetVersion = RulesetIdentity.CurrentVersion;
        for (var index = 1; index < arguments.Count; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(token);
                continue;
            }

            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return false;
            }

            var value = arguments[++index];
            switch (token)
            {
                case "--inventory":
                    inventoryPath = value;
                    break;
                case "--game-version":
                    gameVersion = value;
                    break;
                case "--ruleset-id":
                    rulesetId = value;
                    break;
                case "--ruleset-version":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                        || parsed <= 0)
                    {
                        return false;
                    }

                    rulesetVersion = parsed;
                    break;
                default:
                    return false;
            }
        }

        if (positionals.Count < 2)
        {
            return false;
        }

        invocation = new ContentPackInvocation(
            positionals[0],
            positionals.Skip(1).ToArray(),
            inventoryPath,
            gameVersion,
            rulesetId,
            rulesetVersion);
        return true;
    }

    private static bool TryReadRadioPackInvocation(
        IReadOnlyList<string> arguments,
        out RadioPackInvocation invocation)
    {
        invocation = default;
        var positionals = new List<string>();
        string? curationPath = null;
        string? inventoryPath = null;
        for (var index = 1; index < arguments.Count; index++)
        {
            var token = arguments[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(token);
                continue;
            }

            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return false;
            }

            var value = arguments[++index];
            switch (token)
            {
                case "--curation":
                    curationPath = value;
                    break;
                case "--inventory":
                    inventoryPath = value;
                    break;
                default:
                    return false;
            }
        }

        if (positionals.Count != 3)
        {
            return false;
        }

        invocation = new RadioPackInvocation(
            positionals[0],
            positionals[1],
            positionals[2],
            curationPath,
            inventoryPath);
        return true;
    }

    private static int RunInteropUpstream(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        Func<string, byte[]>? upstreamFetch)
    {
        if (arguments.Count > 2 || (arguments.Count == 2 && string.IsNullOrWhiteSpace(arguments[1])))
        {
            WriteUsage(standardError);
            return 2;
        }

        string repositoryRoot;
        try
        {
            repositoryRoot = Path.GetFullPath(arguments.Count == 2 ? arguments[1] : ".");
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            standardError.WriteLine("Repository root is invalid.");
            return 2;
        }

        UpstreamIntegrityClient? ownedClient = null;
        var fetch = upstreamFetch;
        if (fetch is null)
        {
            ownedClient = UpstreamIntegrityClient.Create();
            fetch = ownedClient.Get;
        }

        try
        {
            var inspection = AgentInteropUpstreamCheck.Inspect(repositoryRoot, fetch);
            if (inspection.LoadError is not null)
            {
                standardError.WriteLine(
                    "Agent interoperability upstream check failed: " + inspection.LoadError);
                return 1;
            }

            if (inspection.Errors.Length > 0)
            {
                standardError.WriteLine("Agent interoperability upstream check failed:");
                foreach (var error in inspection.Errors)
                {
                    standardError.WriteLine("  " + error);
                }

                return 1;
            }

            standardOutput.WriteLine(
                "Agent interoperability upstream specification and schema pins passed: " + repositoryRoot);
            return 0;
        }
        finally
        {
            ownedClient?.Dispose();
        }
    }

}
