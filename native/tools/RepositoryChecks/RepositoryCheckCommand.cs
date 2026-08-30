using System.Text;
using System.Text.RegularExpressions;

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
        RunCore(arguments, standardOutput, standardError, resolver: null);

    internal static int Run(
        IReadOnlyList<string>? arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        IDependencyResolverProcess resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return RunCore(arguments, standardOutput, standardError, resolver);
    }

    private static int RunCore(
        IReadOnlyList<string>? arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        IDependencyResolverProcess? resolver)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
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

        if (arguments is null
            || arguments.Count is < 1 or > 2
            || arguments[0] is not ("all" or "badges" or "docs" or "freeze" or "inventory" or "inventory-release" or "locks" or "logo" or "materials" or "rehearsal" or "screenshots" or "source" or "version"))
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
            "badges" => new[] { StationBadgeCheck.Inspect(repositoryRoot) },
            "docs" => new[] { DocumentationCheck.Inspect(repositoryRoot) },
            "freeze" => new[] { CandidateFreezeCheck.Inspect(repositoryRoot) },
            "inventory" => new[] { ContentInventoryCheck.Inspect(repositoryRoot) },
            "inventory-release" => new[] { ContentInventoryCheck.Inspect(repositoryRoot, requireReleaseReady: true) },
            "locks" => new[] { DependencyLockCheck.Inspect(repositoryRoot) },
            "logo" => new[] { ProjectLogoCheck.Inspect(repositoryRoot) },
            "materials" => new[] { ReleaseMaterialsCheck.Inspect(repositoryRoot) },
            "rehearsal" => new[] { ReleaseRehearsalCheck.Inspect(repositoryRoot) },
            "screenshots" => new[] { ReadmeScreenshotCheck.Inspect(repositoryRoot) },
            "source" => new[] { SourcePolicyCheck.Inspect(repositoryRoot) },
            "version" => new[] { ProductVersionCheck.Inspect(repositoryRoot) },
            _ => new[]
            {
                ProductVersionCheck.Inspect(repositoryRoot),
                DocumentationCheck.Inspect(repositoryRoot),
                CandidateFreezeCheck.Inspect(repositoryRoot),
                ContentInventoryCheck.Inspect(repositoryRoot),
                DependencyLockCheck.Inspect(repositoryRoot),
                ProjectLogoCheck.Inspect(repositoryRoot),
                ReleaseMaterialsCheck.Inspect(repositoryRoot),
                ReleaseRehearsalCheck.Inspect(repositoryRoot),
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

    private static int ReportSingleResult(
        RepositoryCheckResult result,
        TextWriter standardOutput,
        TextWriter standardError)
    {
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
            "Usage: RepositoryChecks <all|badges|docs|freeze|inventory|inventory-release|locks|logo|materials|rehearsal|screenshots|source|version> "
            + "[repository-root]");
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
            "       RepositoryChecks freeze-baseline <revision> <generated-utc> "
            + "[repository-root] [output]");
        writer.WriteLine(
            "       RepositoryChecks lock-write <ci|runtime> [repository-root]");
        writer.WriteLine(
            "       RepositoryChecks plugin <plugin-root> [--require-mcp]");
    }

}
