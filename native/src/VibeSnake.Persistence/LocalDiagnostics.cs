using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VibeSnake.Persistence;

/// <summary>
/// Offline crash and support report writer. Never embeds absolute user paths
/// or full save contents. Network submission is intentionally absent.
/// </summary>
public sealed class LocalDiagnostics
{
    public const string DiagnosticsDirectoryName = "diagnostics";
    public const string ReportFileExtension = ".vibesnake-diagnostic.json";
    public const string DivergenceReportFileExtension = ".vibesnake-divergence.json";
    public const int MaximumMessageCharacters = 2_000;
    public const int MaximumStackCharacters = 8_000;
    public const int MaximumRecentCommands = 64;
    public const int MaximumCommandCharacters = 64;
    public const int MaximumReportsRetained = 32;

    private static readonly JsonSerializerOptions ReportSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public LocalDiagnostics(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        DiagnosticsDirectory = Path.Combine(UserDataRoot, DiagnosticsDirectoryName);
    }

    public string UserDataRoot { get; }

    public string DiagnosticsDirectory { get; }

    public string WriteCrashReport(
        string appVersion,
        string platform,
        string rulesetId,
        int rulesVersion,
        string screenState,
        Exception exception,
        TimeProvider? timeProvider = null,
        string? configHash = null,
        string? configHashAlgorithm = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenState);
        ArgumentNullException.ThrowIfNull(exception);
        timeProvider ??= TimeProvider.System;
        if (configHash is not null && !IsLowerHex(configHash, 64))
        {
            throw new ArgumentException(
                "configHash must be a 64-character lowercase hex digest when provided.",
                nameof(configHash));
        }

        if (configHashAlgorithm is not null && string.IsNullOrWhiteSpace(configHashAlgorithm))
        {
            throw new ArgumentException(
                "configHashAlgorithm must be non-empty when provided.",
                nameof(configHashAlgorithm));
        }

        Directory.CreateDirectory(DiagnosticsDirectory);
        var timestamp = timeProvider.GetUtcNow().UtcDateTime;
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{timestamp:yyyyMMdd'T'HHmmss'Z'}_{SanitizeToken(exception.GetType().Name)}{ReportFileExtension}");
        var path = Path.Combine(DiagnosticsDirectory, fileName);

        var payload = new
        {
            schemaVersion = 1,
            kind = "crash-report",
            capturedAtUtc = timestamp.ToString("O", CultureInfo.InvariantCulture),
            appVersion,
            platform = SanitizeToken(platform),
            rulesetId = SanitizeToken(rulesetId),
            rulesVersion,
            configHash,
            configHashAlgorithm = string.IsNullOrWhiteSpace(configHashAlgorithm)
                ? null
                : SanitizeToken(configHashAlgorithm),
            screenState = SanitizeToken(screenState),
            exceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            message = Truncate(SanitizeMessage(exception.Message), MaximumMessageCharacters),
            stackTrace = Truncate(
                SanitizeMessage(exception.StackTrace ?? string.Empty),
                MaximumStackCharacters),
        };

        var json = JsonSerializer.Serialize(
            payload,
            ReportSerializerOptions) + "\n";
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
        PruneOldReports();
        return path;
    }

    /// <summary>
    /// Writes the first deterministic mismatch with enough bounded state to
    /// reproduce the exact run. Commands retain only the most recent bounded
    /// prefix and never include save contents or absolute paths.
    /// </summary>
    public string WriteDivergenceReport(
        string appVersion,
        string platform,
        string rulesetId,
        int rulesVersion,
        string campaignId,
        string modeId,
        ulong gameplaySeed,
        ulong controllerSeed,
        int runIndex,
        int firstDivergentStep,
        string expectedStateHash,
        string actualStateHash,
        IReadOnlyList<string> recentCommands,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modeId);
        ArgumentNullException.ThrowIfNull(recentCommands);
        ArgumentOutOfRangeException.ThrowIfNegative(runIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(firstDivergentStep);

        if (!IsLowerHex(expectedStateHash, 16))
        {
            throw new ArgumentException(
                "expectedStateHash must be a 16-character lowercase hex digest.",
                nameof(expectedStateHash));
        }

        if (!IsLowerHex(actualStateHash, 16))
        {
            throw new ArgumentException(
                "actualStateHash must be a 16-character lowercase hex digest.",
                nameof(actualStateHash));
        }

        timeProvider ??= TimeProvider.System;
        Directory.CreateDirectory(DiagnosticsDirectory);
        var timestamp = timeProvider.GetUtcNow().UtcDateTime;
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{timestamp:yyyyMMdd'T'HHmmss'Z'}_{SanitizeToken(campaignId)}_{SanitizeToken(modeId)}_step-{firstDivergentStep:D6}{DivergenceReportFileExtension}");
        var path = Path.Combine(DiagnosticsDirectory, fileName);
        var boundedCommands = recentCommands
            .TakeLast(MaximumRecentCommands)
            .Select(command => Truncate(SanitizeMessage(command), MaximumCommandCharacters))
            .ToArray();
        var payload = new
        {
            schemaVersion = 1,
            kind = "deterministic-divergence-report-v1",
            capturedAtUtc = timestamp.ToString("O", CultureInfo.InvariantCulture),
            appVersion,
            platform = SanitizeToken(platform),
            rulesetId = SanitizeToken(rulesetId),
            rulesVersion,
            campaignId = SanitizeToken(campaignId),
            modeId = SanitizeToken(modeId),
            gameplaySeed = gameplaySeed.ToString("x16", CultureInfo.InvariantCulture),
            controllerSeed = controllerSeed.ToString("x16", CultureInfo.InvariantCulture),
            runIndex,
            firstDivergentStep,
            expectedStateHash,
            actualStateHash,
            recentCommandCount = boundedCommands.Length,
            recentCommands = boundedCommands,
        };
        var json = JsonSerializer.Serialize(
            payload,
            ReportSerializerOptions) + "\n";
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
        PruneOldReports();
        return path;
    }

    public IReadOnlyList<string> ListReportFileNames()
    {
        return ListReportFileNames(ReportFileExtension);
    }

    public IReadOnlyList<string> ListDivergenceReportFileNames() =>
        ListReportFileNames(DivergenceReportFileExtension);

    /// <summary>
    /// Ensures the diagnostics directory exists and returns its absolute path for
    /// UI "open folder" actions. Never creates network paths.
    /// </summary>
    public string EnsureDiagnosticsDirectory()
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        return DiagnosticsDirectory;
    }

    private void PruneOldReports()
    {
        var files = Directory.GetFiles(DiagnosticsDirectory)
            .Where(path =>
                path.EndsWith(ReportFileExtension, StringComparison.Ordinal)
                || path.EndsWith(DivergenceReportFileExtension, StringComparison.Ordinal))
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.CreationTimeUtc)
            .ToArray();
        for (var index = MaximumReportsRetained; index < files.Length; index++)
        {
            files[index].Delete();
        }
    }

    private string[] ListReportFileNames(string extension)
    {
        if (!Directory.Exists(DiagnosticsDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(DiagnosticsDirectory, "*" + extension)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Truncate(string value, int maximum)
    {
        if (value.Length <= maximum)
        {
            return value;
        }

        return value[..maximum];
    }

    private static string SanitizeToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ' ')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.ToString();
    }

    private static string SanitizeMessage(string value)
    {
        // Strip absolute filesystem paths so crash reports stay support-safe offline.
        var sanitized = value.Replace('\\', '/');
        sanitized = Regex.Replace(
            sanitized,
            "[A-Za-z]:/[^\\s\"']+",
            "<path>",
            RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(
            sanitized,
            "/(?:home|Users)/[^\\s\"']+",
            "<path>",
            RegexOptions.CultureInvariant);
        sanitized = Regex.Replace(
            sanitized,
            "(?<![:A-Za-z0-9])/(?:[^/\\s\"']+/)*[^/\\s\"']+",
            "<path>",
            RegexOptions.CultureInvariant);
        return sanitized;
    }

    private static bool IsLowerHex(string value, int length)
    {
        if (value.Length != length)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (
                (character < '0' || character > '9')
                && (character < 'a' || character > 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
