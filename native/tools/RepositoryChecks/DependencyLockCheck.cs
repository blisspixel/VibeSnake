using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

internal sealed record DependencyLockProfile(
    string Name,
    IReadOnlyList<string> Inputs,
    string LockName,
    string SourceRequirement,
    string PythonVersion)
{
    public string RegenerateCommand =>
        "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- "
        + $"lock-write {Name} .";

    public string GeneratedHeader =>
        "# Generator: RepositoryChecks\n"
        + $"# Regenerate: {RegenerateCommand}\n"
        + $"# Resolver: uv {DependencyLockCheck.ResolverVersion}\n";
}

internal sealed record ResolverProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false);

internal interface IDependencyResolverProcess
{
    string ResolveExecutable(string repositoryRoot);

    ResolverProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout);
}

internal sealed class SystemDependencyResolverProcess : IDependencyResolverProcess
{
    private readonly string executablePath;

    public SystemDependencyResolverProcess(string? executablePath = null)
    {
        this.executablePath = executablePath
            ?? Environment.GetEnvironmentVariable("PATH")
            ?? string.Empty;
    }

    public string ResolveExecutable(string repositoryRoot)
    {
        var localPath = OperatingSystem.IsWindows()
            ? Path.Combine(repositoryRoot, ".venv", "Scripts", "uv.exe")
            : Path.Combine(repositoryRoot, ".venv", "bin", "uv");
        if (File.Exists(localPath))
        {
            return Path.GetFullPath(localPath);
        }

        var executableName = OperatingSystem.IsWindows() ? "uv.exe" : "uv";
        foreach (var pathEntry in executablePath
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(pathEntry.Trim('"'), executableName));
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"uv {DependencyLockCheck.ResolverVersion} is required to regenerate dependency locks.");
    }

    public ResolverProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        var result = BoundedProcessRunner.Run(
            executable,
            arguments,
            workingDirectory,
            timeout);
        return new ResolverProcessResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut);
    }
}

public static class DependencyLockCheck
{
    public const string ResolverVersion = "0.11.33";

    private static readonly Regex PinPattern = new(
        @"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)==(?<version>[^\s;\\]+)"
        + @"(?:\s*;\s*[^\\]+)?\s*\\?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex HashPattern = new(
        @"--hash=sha256:[a-f0-9]{64}(?:\s|$)",
        RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, DependencyLockProfile> Profiles =
        new Dictionary<string, DependencyLockProfile>(StringComparer.Ordinal)
        {
            ["ci"] = new(
                "ci",
                ["pyproject.toml", "requirements.txt", "requirements-dev.txt"],
                "requirements-ci.lock",
                "requirements-dev.txt",
                "3.11"),
            ["runtime"] = new(
                "runtime",
                ["pyproject.toml", "requirements.txt", "requirements-runtime.txt"],
                "requirements-runtime.lock",
                "requirements-runtime.txt",
                "3.11"),
        };

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var counts = new List<string>();
        var failures = new List<string>();
        foreach (var profile in Profiles.Values)
        {
            try
            {
                counts.Add($"{profile.Name} packages={CheckProfile(root, profile.Name)}");
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or DecoderFallbackException
                    or InvalidDataException)
            {
                failures.Add($"{profile.Name}: {SingleLine(exception.Message)}");
            }
        }

        return failures.Count == 0
            ? new RepositoryCheckResult(
                "Python dependency locks",
                true,
                "Python dependency locks verified: " + string.Join(", ", counts),
                [])
            : new RepositoryCheckResult("Python dependency locks", false, string.Empty, failures);
    }

    public static int CheckProfile(string repositoryRoot, string profileName)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var profile = GetProfile(profileName);
        var path = Path.Combine(root, profile.LockName);
        string lockText;
        try
        {
            lockText = ReadUtf8(path);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException)
        {
            throw new InvalidDataException(
                $"dependency lock is unreadable: {profile.LockName}: {SingleLine(exception.Message)}",
                exception);
        }

        return ValidateLockText(lockText, root, profileName);
    }

    public static string ComputeInputDigest(
        string repositoryRoot,
        IReadOnlyList<string> inputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(inputs);
        var root = Path.GetFullPath(repositoryRoot);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relativePath in inputs)
        {
            var canonicalRelative = CanonicalInputPath(root, relativePath);
            var path = Path.Combine(root, canonicalRelative.Replace('/', Path.DirectorySeparatorChar));
            byte[] contents;
            try
            {
                contents = File.ReadAllBytes(path);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException(
                    $"dependency input is unreadable: {canonicalRelative}: {SingleLine(exception.Message)}",
                    exception);
            }

            digest.AppendData(Encoding.UTF8.GetBytes(canonicalRelative));
            digest.AppendData([0]);
            digest.AppendData(contents);
            digest.AppendData([0]);
        }

        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    public static int ValidateLockText(
        string lockText,
        string repositoryRoot,
        string profileName = "ci")
    {
        ArgumentNullException.ThrowIfNull(lockText);
        var profile = GetProfile(profileName);
        if (!lockText.EndsWith('\n'))
        {
            throw new InvalidDataException("dependency lock must end with a newline");
        }

        if (lockText.Contains('\r'))
        {
            throw new InvalidDataException("dependency lock must use LF line endings");
        }

        if (!lockText.StartsWith(profile.GeneratedHeader, StringComparison.Ordinal))
        {
            throw new InvalidDataException("dependency lock has an unknown generator header");
        }

        var expectedDigest = ComputeInputDigest(repositoryRoot, profile.Inputs);
        var digestLine = $"# Inputs-SHA256: {expectedDigest}\n";
        if (!lockText.AsSpan(profile.GeneratedHeader.Length).StartsWith(digestLine, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "dependency lock is stale; regenerate it after changing requirement inputs");
        }

        var lines = lockText.Split('\n');
        var requirementIndexes = lines
            .Select((line, index) => (line, index))
            .Where(item => item.line.Length > 0
                && !char.IsWhiteSpace(item.line[0])
                && !item.line.StartsWith('#'))
            .Select(item => item.index)
            .ToArray();
        if (requirementIndexes.Length == 0)
        {
            throw new InvalidDataException("dependency lock contains no requirements");
        }

        for (var position = 0; position < requirementIndexes.Length; position++)
        {
            var startIndex = requirementIndexes[position];
            var requirementLine = lines[startIndex];
            if (!PinPattern.IsMatch(requirementLine))
            {
                throw new InvalidDataException(
                    $"dependency must be exactly pinned: {requirementLine}");
            }

            var endIndex = position + 1 < requirementIndexes.Length
                ? requirementIndexes[position + 1]
                : lines.Length;
            var block = string.Join('\n', lines[startIndex..endIndex]) + "\n";
            if (!HashPattern.IsMatch(block))
            {
                throw new InvalidDataException(
                    $"dependency pin has no SHA-256 hash: {requirementLine}");
            }
        }

        return requirementIndexes.Length;
    }

    public static int WriteProfile(string repositoryRoot, string profileName) =>
        WriteProfile(repositoryRoot, profileName, new SystemDependencyResolverProcess());

    internal static int WriteProfile(
        string repositoryRoot,
        string profileName,
        IDependencyResolverProcess resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var root = Path.GetFullPath(repositoryRoot);
        var profile = GetProfile(profileName);
        string executable;
        try
        {
            executable = resolver.ResolveExecutable(root);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            throw new InvalidDataException(SingleLine(exception.Message), exception);
        }

        ResolverProcessResult versionResult;
        try
        {
            versionResult = resolver.Run(executable, ["--version"], root, TimeSpan.FromSeconds(10));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            throw new InvalidDataException(
                $"unable to verify uv version: {SingleLine(exception.Message)}",
                exception);
        }

        var versionLine = versionResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();
        if (versionResult.TimedOut)
        {
            throw new InvalidDataException("unable to verify uv version: timed out after 10 seconds");
        }

        if (versionResult.ExitCode != 0)
        {
            throw new InvalidDataException(
                "unable to verify uv version: " + ProcessFailureDetails(versionResult));
        }

        if (versionLine is null
            || !Regex.IsMatch(
                versionLine,
                $@"^uv {Regex.Escape(ResolverVersion)}(?:\s|$)",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException(
                $"uv {ResolverVersion} is required to regenerate locks; found "
                + (versionLine ?? "no version reported"));
        }

        var generatedPath = Path.Combine(
            Path.GetTempPath(),
            $"vibesnake-requirements-{Guid.NewGuid():N}.lock");
        File.WriteAllBytes(generatedPath, []);
        try
        {
            ResolverProcessResult resolutionResult;
            try
            {
                resolutionResult = resolver.Run(
                    executable,
                    [
                        "pip",
                        "compile",
                        profile.SourceRequirement,
                        "--universal",
                        "--python-version",
                        profile.PythonVersion,
                        "--generate-hashes",
                        "--output-file",
                        generatedPath,
                    ],
                    root,
                    TimeSpan.FromSeconds(180));
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                throw new InvalidDataException(
                    $"uv dependency resolution failed: {SingleLine(exception.Message)}",
                    exception);
            }

            if (resolutionResult.TimedOut)
            {
                throw new InvalidDataException(
                    "uv dependency resolution failed: timed out after 180 seconds");
            }

            if (resolutionResult.ExitCode != 0)
            {
                throw new InvalidDataException(
                    "uv dependency resolution failed: " + ProcessFailureDetails(resolutionResult));
            }

            if (resolutionResult.StandardError.Contains("warning", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "uv dependency resolution emitted a warning: "
                    + SingleLine(resolutionResult.StandardError));
            }

            string rawLock;
            try
            {
                rawLock = ReadUtf8(generatedPath);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or DecoderFallbackException)
            {
                throw new InvalidDataException(
                    $"generated dependency lock is unreadable: {SingleLine(exception.Message)}",
                    exception);
            }

            var rendered = RenderGeneratedLock(
                rawLock,
                ComputeInputDigest(root, profile.Inputs),
                profileName);
            var count = ValidateLockText(rendered, root, profileName);
            WriteAtomically(Path.Combine(root, profile.LockName), rendered);
            return count;
        }
        finally
        {
            File.Delete(generatedPath);
        }
    }

    internal static string RenderGeneratedLock(
        string rawLock,
        string inputDigest,
        string profileName)
    {
        ArgumentNullException.ThrowIfNull(rawLock);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputDigest);
        var profile = GetProfile(profileName);
        var normalized = rawLock.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.Contains('\r'))
        {
            throw new InvalidDataException("uv produced a dependency lock with invalid line endings");
        }

        var lines = normalized.Split('\n');
        var firstRequirement = Array.FindIndex(
            lines,
            line => line.Length > 0 && !line.StartsWith('#'));
        if (firstRequirement < 0)
        {
            throw new InvalidDataException("uv produced an empty dependency lock");
        }

        var payload = string.Join('\n', lines[firstRequirement..]).TrimEnd() + "\n";
        return profile.GeneratedHeader + $"# Inputs-SHA256: {inputDigest}\n" + payload;
    }

    private static DependencyLockProfile GetProfile(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        if (!Profiles.TryGetValue(profileName, out var profile))
        {
            throw new InvalidDataException($"unknown dependency lock profile: {profileName}");
        }

        return profile;
    }

    private static string CanonicalInputPath(string repositoryRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"dependency input escapes the repository: {relativePath}");
        }

        string path;
        try
        {
            path = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException(
                $"dependency input is invalid: {relativePath}",
                exception);
        }

        var canonicalRelative = Path.GetRelativePath(repositoryRoot, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (canonicalRelative == ".."
            || canonicalRelative.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(canonicalRelative))
        {
            throw new InvalidDataException($"dependency input escapes the repository: {relativePath}");
        }

        return canonicalRelative;
    }

    private static string ReadUtf8(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static void WriteAtomically(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("dependency lock output has no parent directory");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(contents);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string ProcessFailureDetails(ResolverProcessResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        return string.IsNullOrWhiteSpace(details)
            ? $"resolver exited with code {result.ExitCode}"
            : SingleLine(details);
    }

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
