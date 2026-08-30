using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepositoryChecks;

internal sealed record ScreenshotProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false);

internal interface IScreenshotCaptureProcess
{
    ScreenshotProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout);
}

[ExcludeFromCodeCoverage]
internal sealed class SystemScreenshotCaptureProcess : IScreenshotCaptureProcess
{
    private const int MaximumOutputCharacters = 256 * 1024;
    private static readonly TimeSpan TerminationBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OutputDrainBudget = TimeSpan.FromSeconds(2);

    public ScreenshotProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var outputCancellation = new CancellationTokenSource();
        var standardOutput = ReadBoundedAsync(
            process.StandardOutput,
            outputCancellation.Token);
        var standardError = ReadBoundedAsync(
            process.StandardError,
            outputCancellation.Token);
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or NotSupportedException
                    or Win32Exception)
            {
                // A bounded wait below still prevents cleanup from hanging.
            }

            using var terminationSource = new CancellationTokenSource(TerminationBudget);
            try
            {
                process.WaitForExitAsync(terminationSource.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // The process did not terminate inside the fixed cleanup budget.
            }

            outputCancellation.Cancel();
            var timedOutOutput = CompleteOutput(
                standardOutput,
                standardError,
                outputCancellation);
            return new ScreenshotProcessResult(
                -1,
                timedOutOutput.StandardOutput,
                timedOutOutput.StandardError,
                TimedOut: true);
        }

        var completedOutput = CompleteOutput(
            standardOutput,
            standardError,
            outputCancellation);
        return new ScreenshotProcessResult(
            process.ExitCode,
            completedOutput.StandardOutput,
            completedOutput.StandardError);
    }

    private static (string StandardOutput, string StandardError) CompleteOutput(
        Task<string> standardOutput,
        Task<string> standardError,
        CancellationTokenSource cancellation)
    {
        var combined = Task.WhenAll(standardOutput, standardError);
        try
        {
            combined.WaitAsync(OutputDrainBudget).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (IsOutputCompletionFailure(exception))
        {
            cancellation.Cancel();
            try
            {
                combined.WaitAsync(OutputDrainBudget).GetAwaiter().GetResult();
            }
            catch (Exception retryException) when (IsOutputCompletionFailure(retryException))
            {
                ObserveLater(standardOutput);
                ObserveLater(standardError);
            }
        }

        return (
            CompletedTranscript(standardOutput),
            CompletedTranscript(standardError));
    }

    private static bool IsOutputCompletionFailure(Exception exception) =>
        exception is TimeoutException
            or OperationCanceledException
            or IOException
            or ObjectDisposedException;

    private static string CompletedTranscript(Task<string> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            return task.Result;
        }

        if (task.IsFaulted)
        {
            _ = task.Exception;
            return "[process output read failed]";
        }

        return "[process output drain timed out]";
    }

    private static void ObserveLater(Task<string> task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            while (await reader
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false) is var count && count > 0)
            {
                var remaining = MaximumOutputCharacters - output.Length;
                if (remaining > 0)
                {
                    output.Append(buffer, 0, Math.Min(remaining, count));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Return the bounded partial transcript after timeout cleanup.
        }

        if (output.Length == MaximumOutputCharacters)
        {
            output.Append("\n[process output truncated]");
        }

        return output.ToString();
    }
}

public static class ReadmeScreenshotCheck
{
    public const string ScreenshotRelativeDirectory = "docs/images/screenshots";
    public const string ManifestRelativePath = "docs/images/screenshots/manifest.json";
    public const string Generator = "native/tools/RepositoryChecks/ReadmeScreenshotCheck.cs";

    private const int ExpectedWidth = 1280;
    private const int ExpectedHeight = 720;
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumToolchainBytes = 1024 * 1024;
    private const int MaximumFingerprintFiles = 20_000;
    private const int MaximumFingerprintEntries = 50_000;
    private const int MaximumRelativePathCharacters = 512;
    private const long MaximumFingerprintFileBytes = 256L * 1024 * 1024;
    private const long MaximumFingerprintBytes = 4L * 1024 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly JsonSerializerOptions RenderOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };

    private static readonly ScreenshotSpec[] Specifications =
    [
        new("main-menu.png", "Main menu", "MENU"),
        new("powers-run.png", "Vibe mode gameplay", "RUNNING"),
        new("customization.png", "Customization", "COSMETICS"),
        new("ai-channel.png", "AI channel", "SPECTATOR"),
    ];

    private static readonly HashSet<string> TextFingerprintExtensions = new(
        [".cs", ".godot", ".json", ".md", ".py", ".svg", ".tscn", ".txt"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> GameFingerprintExtensions = new(
        [.. TextFingerprintExtensions, ".png"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] FixedFingerprintPaths =
    [
        "config/content_inventory.json",
        "native/toolchain.json",
        "native/tools/RepositoryChecks/ContentInventoryCheck.cs",
        Generator,
        "native/tools/RepositoryChecks/PngHeaderReader.cs",
    ];

    public static RepositoryCheckResult Inspect(string repositoryRoot) =>
        Inspect(repositoryRoot, expectedSourceFingerprint: null);

    internal static RepositoryCheckResult Inspect(
        string repositoryRoot,
        string? expectedSourceFingerprint)
    {
        try
        {
            var root = ResolveRoot(repositoryRoot);
            var screenshotDirectory = ResolveDirectory(root, ScreenshotRelativeDirectory);
            var expectedFiles = Specifications
                .Select(specification => specification.File)
                .Append("manifest.json")
                .ToHashSet(StringComparer.Ordinal);
            var actualFiles = ClosedDirectoryFiles(screenshotDirectory);
            if (!actualFiles.SetEquals(expectedFiles))
            {
                throw new InvalidDataException(
                    "screenshot directory must contain exactly four canonical PNG files and manifest.json");
            }

            var manifestPath = ResolveRegularFile(root, ManifestRelativePath, MaximumManifestBytes);
            var manifestBytes = File.ReadAllBytes(manifestPath);
            var manifestText = StrictUtf8.GetString(manifestBytes);
            RejectDuplicateJsonProperties(manifestBytes);
            using var document = JsonDocument.Parse(manifestBytes);
            var records = ValidateManifest(document.RootElement);

            var sourceFingerprint = expectedSourceFingerprint ?? ComputeSourceFingerprint(root);
            if (!string.Equals(records.SourceSha256, sourceFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "README screenshots are stale relative to current native presentation source");
            }

            var readmePath = ResolveRegularFile(root, "README.md", MaximumManifestBytes);
            var readme = StrictUtf8.GetString(File.ReadAllBytes(readmePath));
            foreach (var (specification, record) in Specifications.Zip(records.Screenshots))
            {
                ValidateScreenshot(root, specification, record, readme);
            }

            var canonical = RenderManifest(root, sourceFingerprint);
            if (!string.Equals(manifestText, canonical, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "screenshot manifest is not canonical or does not match committed evidence");
            }

            return new RepositoryCheckResult(
                "README screenshots",
                true,
                $"README screenshots verified: {Specifications.Length} native captures",
                []);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed(SingleLine(exception.Message));
        }
    }

    public static RepositoryCheckResult Capture(
        string repositoryRoot,
        string godotExecutable) =>
        Capture(repositoryRoot, godotExecutable, new SystemScreenshotCaptureProcess());

    internal static RepositoryCheckResult Capture(
        string repositoryRoot,
        string godotExecutable,
        IScreenshotCaptureProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        string? temporaryRoot = null;
        try
        {
            var root = ResolveRoot(repositoryRoot);
            var godot = ResolveExplicitExecutable(godotExecutable);
            VerifyGodotIdentity(root, godot, process);
            var build = process.Run(
                "dotnet",
                ["build", Path.Combine(root, "game", "VibeSnake.Game.sln"), "--nologo"],
                root,
                TimeSpan.FromSeconds(180));
            EnsureProcessPassed("native screenshot build", build, successMarker: null);

            temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "vibesnake-readme-screenshots",
                Guid.NewGuid().ToString("N"));
            var staging = Path.Combine(temporaryRoot, "capture");
            var playerData = Path.Combine(temporaryRoot, "player-data");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(playerData);

            var capture = process.Run(
                godot,
                [
                    "--path",
                    Path.Combine(root, "game"),
                    "--rendering-method",
                    "gl_compatibility",
                    "--",
                    $"--readme-capture-dir={staging}",
                    $"--smoke-user-data-root={playerData}",
                ],
                root,
                TimeSpan.FromSeconds(120));
            EnsureProcessPassed(
                "native Godot screenshot capture",
                capture,
                "VIBESNAKE_README_CAPTURE_OK count=4");

            var stagedFiles = ClosedDirectoryFiles(staging);
            var expectedStagedFiles = Specifications
                .Select(specification => specification.File)
                .ToHashSet(StringComparer.Ordinal);
            if (!stagedFiles.SetEquals(expectedStagedFiles))
            {
                throw new InvalidDataException(
                    "native capture must write exactly the four canonical PNG files");
            }

            foreach (var specification in Specifications)
            {
                ValidatePng(Path.Combine(staging, specification.File), specification.File);
            }

            var screenshotDirectory = ResolveInsideRoot(root, ScreenshotRelativeDirectory);
            Directory.CreateDirectory(screenshotDirectory);
            RejectExistingPathComponents(root, screenshotDirectory, "screenshot directory");
            foreach (var specification in Specifications)
            {
                ReplaceFileAtomically(
                    Path.Combine(staging, specification.File),
                    Path.Combine(screenshotDirectory, specification.File));
            }

            var sourceFingerprint = ComputeSourceFingerprint(root);
            var manifest = StrictUtf8.GetBytes(RenderManifest(root, sourceFingerprint));
            ReplaceBytesAtomically(manifest, Path.Combine(screenshotDirectory, "manifest.json"));

            var verification = Inspect(root);
            if (!verification.Passed)
            {
                throw new InvalidDataException(
                    "capture verification failed: " + string.Join("; ", verification.Failures));
            }

            return new RepositoryCheckResult(
                "README screenshots",
                true,
                $"README screenshots captured and verified: {Specifications.Length} native captures; visual review required",
                []);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed(SingleLine(exception.Message));
        }
        finally
        {
            if (temporaryRoot is not null && Directory.Exists(temporaryRoot))
            {
                try
                {
                    Directory.Delete(temporaryRoot, recursive: true);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // Generated temporary evidence never weakens committed verification.
                }
            }
        }
    }

    internal static string ComputeSourceFingerprint(string repositoryRoot) =>
        ComputeSourceFingerprint(repositoryRoot, MaximumFingerprintEntries);

    internal static string ComputeSourceFingerprint(
        string repositoryRoot,
        int maximumEntries)
    {
        if (maximumEntries is < 1 or > MaximumFingerprintEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        var root = ResolveRoot(repositoryRoot);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var encounteredEntries = 0;
        AddTree(
            root,
            "game",
            path => GameFingerprintExtensions.Contains(Path.GetExtension(path)),
            paths,
            ref encounteredEntries,
            maximumEntries);
        AddTree(
            root,
            "native/src/VibeSnake.Rules",
            path => string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase),
            paths,
            ref encounteredEntries,
            maximumEntries);
        AddTree(
            root,
            "native/src/VibeSnake.Persistence",
            path => string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase),
            paths,
            ref encounteredEntries,
            maximumEntries);
        foreach (var relativePath in FixedFingerprintPaths)
        {
            AddFingerprintFile(root, relativePath, paths);
        }

        if (paths.Count > MaximumFingerprintFiles)
        {
            throw new InvalidDataException(
                $"screenshot source fingerprint exceeds {MaximumFingerprintFiles} files");
        }

        var foldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in paths.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!foldedPaths.Add(entry.Key))
            {
                throw new InvalidDataException(
                    $"screenshot source paths collide by case: {entry.Key}");
            }

            AppendUtf8(digest, entry.Key);
            digest.AppendData([0]);
            var before = new FileInfo(entry.Value);
            if (before.Length > MaximumFingerprintFileBytes)
            {
                throw new InvalidDataException(
                    $"screenshot source file is too large: {entry.Key}");
            }

            totalBytes = checked(totalBytes + before.Length);
            if (totalBytes > MaximumFingerprintBytes)
            {
                throw new InvalidDataException("screenshot source fingerprint exceeds its byte budget");
            }

            using (var stream = new FileStream(
                entry.Value,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan))
            {
                if (TextFingerprintExtensions.Contains(Path.GetExtension(entry.Value)))
                {
                    AppendNormalizedText(digest, stream);
                }
                else
                {
                    AppendStream(digest, stream);
                }
            }

            var after = new FileInfo(entry.Value);
            if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
            {
                throw new InvalidDataException(
                    $"screenshot source changed while it was fingerprinted: {entry.Key}");
            }

            digest.AppendData([0]);
        }

        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    internal static string RenderManifest(string repositoryRoot, string sourceFingerprint)
    {
        var root = ResolveRoot(repositoryRoot);
        if (!IsLowerSha256(sourceFingerprint))
        {
            throw new InvalidDataException("screenshot source fingerprint is invalid");
        }

        var screenshots = new JsonArray();
        foreach (var specification in Specifications)
        {
            var relativePath = ScreenshotRelativeDirectory + "/" + specification.File;
            var path = ResolveRegularFile(root, relativePath, MaximumFingerprintFileBytes);
            var headerError = PngHeaderReader.TryRead(path, out var width, out var height);
            if (headerError is not null)
            {
                throw new InvalidDataException($"not a supported PNG screenshot: {specification.File}");
            }

            screenshots.Add(new JsonObject
            {
                ["file"] = specification.File,
                ["height"] = height,
                ["label"] = specification.Label,
                ["sha256"] = Sha256(path),
                ["state"] = specification.State,
                ["width"] = width,
            });
        }

        var manifest = new JsonObject
        {
            ["generator"] = Generator,
            ["schemaVersion"] = 1,
            ["screenshots"] = screenshots,
            ["sourceSha256"] = sourceFingerprint,
        };
        var rendered = manifest.ToJsonString(RenderOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return rendered + "\n";
    }

    private static ScreenshotManifest ValidateManifest(JsonElement root)
    {
        RequireObjectFields(
            root,
            "screenshot manifest",
            ["generator", "schemaVersion", "screenshots", "sourceSha256"]);
        if (root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number
            || !root.GetProperty("schemaVersion").TryGetInt32(out var schemaVersion)
            || schemaVersion != 1)
        {
            throw new InvalidDataException("unsupported screenshot manifest schema");
        }

        if (ReadRequiredString(root, "generator") != Generator)
        {
            throw new InvalidDataException("screenshot manifest generator is invalid");
        }

        var sourceSha256 = ReadRequiredString(root, "sourceSha256");
        if (!IsLowerSha256(sourceSha256))
        {
            throw new InvalidDataException("screenshot manifest sourceSha256 is invalid");
        }

        var screenshots = root.GetProperty("screenshots");
        if (screenshots.ValueKind != JsonValueKind.Array
            || screenshots.GetArrayLength() != Specifications.Length)
        {
            throw new InvalidDataException(
                "screenshot manifest must contain four canonical records");
        }

        var records = new List<ScreenshotRecord>();
        foreach (var (element, specification) in screenshots.EnumerateArray().Zip(Specifications))
        {
            RequireObjectFields(
                element,
                $"screenshot record {records.Count}",
                ["file", "height", "label", "sha256", "state", "width"]);
            var file = ReadRequiredString(element, "file");
            if (file != specification.File
                || Path.GetFileName(file) != file
                || file is "." or "..")
            {
                throw new InvalidDataException(
                    $"screenshot record {records.Count} has an unsafe or unexpected file name");
            }

            var label = ReadRequiredString(element, "label");
            var state = ReadRequiredString(element, "state");
            var sha256 = ReadRequiredString(element, "sha256");
            if (label != specification.Label || state != specification.State)
            {
                throw new InvalidDataException($"screenshot metadata mismatch: {file}");
            }

            if (!IsLowerSha256(sha256))
            {
                throw new InvalidDataException($"screenshot hash is invalid: {file}");
            }

            records.Add(new ScreenshotRecord(
                file,
                label,
                state,
                ReadRequiredPositiveInteger(element, "width"),
                ReadRequiredPositiveInteger(element, "height"),
                sha256));
        }

        return new ScreenshotManifest(sourceSha256, records);
    }

    private static void ValidateScreenshot(
        string root,
        ScreenshotSpec specification,
        ScreenshotRecord record,
        string readme)
    {
        var relativePath = ScreenshotRelativeDirectory + "/" + specification.File;
        var path = ResolveRegularFile(root, relativePath, MaximumFingerprintFileBytes);
        ValidatePng(path, specification.File);
        var headerError = PngHeaderReader.TryRead(path, out var width, out var height);
        if (headerError is not null)
        {
            throw new InvalidDataException($"not a supported PNG screenshot: {specification.File}");
        }

        if (width != ExpectedWidth || height != ExpectedHeight)
        {
            throw new InvalidDataException(
                $"screenshot is not {ExpectedWidth}x{ExpectedHeight}: {specification.File}");
        }

        if (record.Width != width || record.Height != height)
        {
            throw new InvalidDataException($"screenshot dimensions changed: {specification.File}");
        }

        if (record.Sha256 != Sha256(path))
        {
            throw new InvalidDataException($"screenshot hash changed: {specification.File}");
        }

        if (!readme.Contains(relativePath, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"README does not reference {relativePath}");
        }
    }

    private static void ValidatePng(string path, string fileName)
    {
        var validationError = ContentInventoryCheck.ValidatePngForRepositoryCheck(path);
        if (validationError is not null)
        {
            throw new InvalidDataException(
                $"invalid README screenshot PNG {fileName}: {validationError}");
        }

        var headerError = PngHeaderReader.TryRead(path, out var width, out var height);
        if (headerError is not null)
        {
            throw new InvalidDataException($"not a supported PNG screenshot: {fileName}");
        }

        if (width != ExpectedWidth || height != ExpectedHeight)
        {
            throw new InvalidDataException(
                $"screenshot is not {ExpectedWidth}x{ExpectedHeight}: {fileName}");
        }
    }

    private static void AddTree(
        string root,
        string relativeRoot,
        Func<string, bool> include,
        Dictionary<string, string> paths,
        ref int encounteredEntries,
        int maximumEntries)
    {
        var treeRoot = ResolveDirectory(root, relativeRoot);
        var pending = new Stack<string>();
        pending.Push(treeRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            RejectReparsePoint(directory, "screenshot source directory");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                encounteredEntries++;
                if (encounteredEntries > maximumEntries)
                {
                    throw new InvalidDataException(
                        $"screenshot source traversal exceeds {maximumEntries} entries");
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"screenshot source may not contain links: {RelativePath(root, entry)}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var name = Path.GetFileName(entry);
                    if (name is not (".godot" or "bin" or "obj"))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                if (include(entry))
                {
                    AddFingerprintFile(root, RelativePath(root, entry), paths);
                }
            }
        }
    }

    private static void AddFingerprintFile(
        string root,
        string relativePath,
        Dictionary<string, string> paths)
    {
        var canonicalRelativePath = relativePath.Replace('\\', '/');
        if (canonicalRelativePath.Length > MaximumRelativePathCharacters)
        {
            throw new InvalidDataException(
                $"screenshot source path is too long: {canonicalRelativePath}");
        }

        var fullPath = ResolveRegularFile(root, canonicalRelativePath, MaximumFingerprintFileBytes);
        if (!paths.TryAdd(canonicalRelativePath, fullPath))
        {
            throw new InvalidDataException(
                $"duplicate screenshot source path: {canonicalRelativePath}");
        }
    }

    private static void AppendNormalizedText(IncrementalHash digest, Stream stream)
    {
        var buffer = new byte[1024 * 1024];
        var normalized = new byte[buffer.Length + 1];
        var pendingCarriageReturn = false;
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var output = 0;
            for (var index = 0; index < count; index++)
            {
                var value = buffer[index];
                if (pendingCarriageReturn)
                {
                    normalized[output++] = (byte)'\n';
                    pendingCarriageReturn = false;
                    if (value == (byte)'\n')
                    {
                        continue;
                    }
                }

                if (value == (byte)'\r')
                {
                    pendingCarriageReturn = true;
                }
                else
                {
                    normalized[output++] = value;
                }
            }

            if (output > 0)
            {
                digest.AppendData(normalized.AsSpan(0, output));
            }
        }

        if (pendingCarriageReturn)
        {
            digest.AppendData([(byte)'\n']);
        }
    }

    private static void AppendStream(IncrementalHash digest, Stream stream)
    {
        var buffer = new byte[1024 * 1024];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            digest.AppendData(buffer.AsSpan(0, count));
        }
    }

    private static void AppendUtf8(IncrementalHash digest, string value) =>
        digest.AppendData(Encoding.UTF8.GetBytes(value));

    private static HashSet<string> ClosedDirectoryFiles(string directory)
    {
        RejectReparsePoint(directory, "screenshot directory");
        var files = new HashSet<string>(StringComparer.Ordinal);
        var encounteredEntries = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            encounteredEntries++;
            if (encounteredEntries > 16)
            {
                throw new InvalidDataException("screenshot directory exceeds 16 entries");
            }

            var attributes = File.GetAttributes(entry);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidDataException(
                    $"screenshot directory contains a directory or link: {Path.GetFileName(entry)}");
            }

            if (!files.Add(Path.GetFileName(entry)))
            {
                throw new InvalidDataException("screenshot directory contains duplicate file names");
            }
        }

        return files;
    }

    private static void RejectDuplicateJsonProperties(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        var propertySets = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                propertySets.Push(new HashSet<string>(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                propertySets.Pop();
            }
            else if (reader.TokenType == JsonTokenType.PropertyName
                && !propertySets.Peek().Add(reader.GetString()!))
            {
                throw new InvalidDataException(
                    $"screenshot manifest contains duplicate JSON property: {reader.GetString()}");
            }
        }
    }

    private static void RequireObjectFields(
        JsonElement element,
        string location,
        string[] expectedFields)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{location} must be a JSON object");
        }

        var actualFields = element.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!actualFields.SetEquals(expectedFields))
        {
            throw new InvalidDataException($"{location} fields do not match schema 1");
        }
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"screenshot manifest {propertyName} must be a string");
        }

        return property.GetString()!;
    }

    private static int ReadRequiredPositiveInteger(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value)
            || value <= 0)
        {
            throw new InvalidDataException(
                $"screenshot manifest {propertyName} must be a positive integer");
        }

        return value;
    }

    private static string ResolveRoot(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("repository root does not exist");
        }

        RejectReparsePoint(root, "repository root");
        return root;
    }

    private static string ResolveDirectory(string root, string relativePath)
    {
        var path = ResolveInsideRoot(root, relativePath);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"missing directory: {relativePath}");
        }

        RejectReparsePoint(path, relativePath);
        return path;
    }

    private static string ResolveRegularFile(string root, string relativePath, long maximumBytes)
    {
        var path = ResolveInsideRoot(root, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"missing file: {relativePath}");
        }

        RejectReparsePoint(path, relativePath);
        var information = new FileInfo(path);
        if (information.Length > maximumBytes)
        {
            throw new InvalidDataException($"file exceeds its size budget: {relativePath}");
        }

        return path;
    }

    private static string ResolveInsideRoot(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"repository path must be relative: {relativePath}");
        }

        var path = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(prefix, comparison))
        {
            throw new InvalidDataException($"repository path escapes its root: {relativePath}");
        }

        RejectExistingPathComponents(root, path, relativePath);
        return path;
    }

    private static string ResolveExplicitExecutable(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var path = Path.GetFullPath(executable);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("explicit Godot executable does not exist");
        }

        // Explicit executable paths can legitimately pass through platform aliases such as
        // macOS /var. The final executable must still be a regular, non-linked file, and its
        // exact pinned build identity is verified before it can render any evidence.
        RejectReparsePoint(path, "Godot executable");
        return path;
    }

    private static void VerifyGodotIdentity(
        string root,
        string godotExecutable,
        IScreenshotCaptureProcess process)
    {
        var expectedIdentity = ReadPinnedGodotIdentity(root);
        var result = process.Run(
            godotExecutable,
            ["--version"],
            root,
            TimeSpan.FromSeconds(30));
        EnsureProcessPassed("Godot identity query", result, successMarker: null);
        var lines = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!string.IsNullOrWhiteSpace(result.StandardError)
            || lines.Length != 1
            || lines[0] != expectedIdentity)
        {
            var actualIdentity = lines.Length == 1 ? lines[0] : "invalid output";
            throw new InvalidDataException(
                $"Godot toolchain mismatch: expected {expectedIdentity}, received {actualIdentity}");
        }
    }

    private static string ReadPinnedGodotIdentity(string root)
    {
        var path = ResolveRegularFile(root, "native/toolchain.json", MaximumToolchainBytes);
        var bytes = File.ReadAllBytes(path);
        _ = StrictUtf8.GetString(bytes);
        RejectDuplicateJsonProperties(bytes);
        using var document = JsonDocument.Parse(bytes);
        var documentRoot = document.RootElement;
        if (documentRoot.ValueKind != JsonValueKind.Object
            || !documentRoot.TryGetProperty("godot", out var godot)
            || godot.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("native/toolchain.json has no Godot object");
        }

        var version = ReadToolchainString(godot, "version");
        var flavor = ReadToolchainString(godot, "flavor");
        var commit = ReadToolchainString(godot, "commit");
        var versionParts = version.Split('.');
        if (versionParts.Length != 3
            || versionParts.Any(part => !uint.TryParse(part, out _))
            || versionParts.Any(part => part.Length > 1 && part[0] == '0')
            || flavor != "dotnet"
            || commit.Length != 9
            || !commit.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new InvalidDataException("native/toolchain.json has an invalid Godot identity");
        }

        return $"{version}.stable.mono.official.{commit}";
    }

    private static string ReadToolchainString(JsonElement godot, string propertyName)
    {
        if (!godot.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException(
                $"native/toolchain.json Godot {propertyName} must be a nonempty string");
        }

        return property.GetString()!;
    }

    private static void RejectExistingPathComponents(
        string root,
        string path,
        string description)
    {
        var relativePath = Path.GetRelativePath(root, path);
        if (relativePath == ".")
        {
            return;
        }

        var current = root;
        foreach (var segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                break;
            }

            RejectReparsePoint(current, description);
        }
    }

    private static void RejectReparsePoint(string path, string description)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"{description} may not be a link or reparse point");
        }
    }

    private static void EnsureProcessPassed(
        string name,
        ScreenshotProcessResult result,
        string? successMarker)
    {
        if (result.TimedOut)
        {
            throw new InvalidDataException($"{name} timed out");
        }

        var output = result.StandardOutput + result.StandardError;
        if (result.ExitCode != 0
            || (successMarker is not null
                && !output.Contains(successMarker, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"{name} failed: {SingleLine(output.Length == 0 ? "no process output" : output)}");
        }
    }

    private static void ReplaceFileAtomically(string source, string destination)
    {
        if (File.Exists(destination))
        {
            RejectReparsePoint(destination, Path.GetFileName(destination));
        }

        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void ReplaceBytesAtomically(byte[] bytes, string destination)
    {
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string Sha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static bool IsLowerSha256(string value) =>
        value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or DecoderFallbackException
            or JsonException
            or OverflowException
            or NotSupportedException
            or Win32Exception
            or ArgumentException;

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static RepositoryCheckResult Failed(string failure) =>
        new("README screenshots", false, string.Empty, [failure]);

    private sealed record ScreenshotSpec(string File, string Label, string State);

    private sealed record ScreenshotRecord(
        string File,
        string Label,
        string State,
        int Width,
        int Height,
        string Sha256);

    private sealed record ScreenshotManifest(
        string SourceSha256,
        IReadOnlyList<ScreenshotRecord> Screenshots);
}
