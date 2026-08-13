using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VibeSnake.Persistence;

/// <summary>
/// Offline structured log levels for support diagnostics. Never submitted over
/// the network. Messages are sanitized and truncated before they reach disk.
/// </summary>
public enum DiagnosticLogLevel : byte
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
}

/// <summary>
/// Append-only JSONL writer under the player user-data <c>logs/</c> directory.
/// Intended for sparse session and fault events, not per-tick gameplay spam.
/// </summary>
public sealed class StructuredLocalLog
{
    public const string LogsDirectoryName = "logs";
    public const string ActiveLogFileName = "vibesnake.jsonl";
    public const string RotatedLogFilePrefix = "vibesnake.";
    public const string RotatedLogFileSuffix = ".jsonl";
    public const int MaximumMessageCharacters = 2_000;
    public const int MaximumCategoryCharacters = 64;
    public const int MaximumEventCodeCharacters = 64;
    public const long MaximumActiveLogBytes = 1_048_576;
    public const int MaximumRotatedFiles = 4;

    private static readonly JsonSerializerOptions LogSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;

    public StructuredLocalLog(
        string userDataRoot,
        DiagnosticLogLevel minimumLevel = DiagnosticLogLevel.Information,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        if (!Enum.IsDefined(minimumLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLevel));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        LogsDirectory = Path.Combine(UserDataRoot, LogsDirectoryName);
        ActiveLogPath = Path.Combine(LogsDirectory, ActiveLogFileName);
        MinimumLevel = minimumLevel;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string UserDataRoot { get; }

    public string LogsDirectory { get; }

    public string ActiveLogPath { get; }

    public DiagnosticLogLevel MinimumLevel { get; }

    public string EnsureLogsDirectory()
    {
        Directory.CreateDirectory(LogsDirectory);
        return LogsDirectory;
    }

    public void Trace(string category, string message, string? eventCode = null) =>
        Write(DiagnosticLogLevel.Trace, category, message, eventCode);

    public void Debug(string category, string message, string? eventCode = null) =>
        Write(DiagnosticLogLevel.Debug, category, message, eventCode);

    public void Information(string category, string message, string? eventCode = null) =>
        Write(DiagnosticLogLevel.Information, category, message, eventCode);

    public void Warning(string category, string message, string? eventCode = null) =>
        Write(DiagnosticLogLevel.Warning, category, message, eventCode);

    public void Error(string category, string message, string? eventCode = null) =>
        Write(DiagnosticLogLevel.Error, category, message, eventCode);

    public void Write(
        DiagnosticLogLevel level,
        string category,
        string message,
        string? eventCode = null)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        if (level < MinimumLevel)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var sanitizedCategory = Truncate(
            SanitizeToken(category),
            MaximumCategoryCharacters);
        var sanitizedMessage = Truncate(
            SanitizeMessage(message),
            MaximumMessageCharacters);
        var sanitizedEvent = eventCode is null
            ? null
            : Truncate(SanitizeToken(eventCode), MaximumEventCodeCharacters);
        if (string.IsNullOrWhiteSpace(sanitizedCategory)
            || string.IsNullOrWhiteSpace(sanitizedMessage))
        {
            throw new ArgumentException(
                "Category and message must retain content after sanitization.");
        }

        var timestamp = _timeProvider.GetUtcNow().UtcDateTime.ToString(
            "O",
            CultureInfo.InvariantCulture);
        var payload = new
        {
            schemaVersion = 1,
            kind = "structured-log",
            capturedAtUtc = timestamp,
            level = level.ToString(),
            category = sanitizedCategory,
            eventCode = sanitizedEvent,
            message = sanitizedMessage,
        };
        var line = JsonSerializer.Serialize(
            payload,
            LogSerializerOptions) + "\n";

        lock (_gate)
        {
            Directory.CreateDirectory(LogsDirectory);
            RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
            File.AppendAllText(
                ActiveLogPath,
                line,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private void RotateIfNeeded(int nextLineBytes)
    {
        if (!File.Exists(ActiveLogPath))
        {
            return;
        }

        var info = new FileInfo(ActiveLogPath);
        if (info.Length + nextLineBytes <= MaximumActiveLogBytes)
        {
            return;
        }

        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString(
            "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture);
        var rotatedPath = Path.Combine(
            LogsDirectory,
            RotatedLogFilePrefix + stamp + RotatedLogFileSuffix);
        if (File.Exists(rotatedPath))
        {
            rotatedPath = Path.Combine(
                LogsDirectory,
                RotatedLogFilePrefix
                    + stamp
                    + "-"
                    + Guid.NewGuid().ToString("N")[..8]
                    + RotatedLogFileSuffix);
        }

        File.Move(ActiveLogPath, rotatedPath);
        PruneRotatedFiles();
    }

    private void PruneRotatedFiles()
    {
        var rotated = Directory
            .GetFiles(LogsDirectory, RotatedLogFilePrefix + "*" + RotatedLogFileSuffix)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.CreationTimeUtc)
            .ToArray();
        for (var index = MaximumRotatedFiles; index < rotated.Length; index++)
        {
            rotated[index].Delete();
        }
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
        sanitized = sanitized.Replace('\r', ' ').Replace('\n', ' ');
        return sanitized;
    }
}
