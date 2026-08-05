using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class StructuredLocalLogTests
{
    [Fact]
    public void Writes_sanitized_jsonl_under_user_data_logs()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new StructuredLocalLog(root);
            log.Information(
                "shell",
                "Session started under C:\\Users\\example\\AppData\\VibeSnake",
                eventCode: "session_start");

            Assert.True(File.Exists(log.ActiveLogPath));
            var text = File.ReadAllText(log.ActiveLogPath);
            Assert.Contains("\"kind\":\"structured-log\"", text, StringComparison.Ordinal);
            Assert.Contains("\"level\":\"Information\"", text, StringComparison.Ordinal);
            Assert.Contains("\"category\":\"shell\"", text, StringComparison.Ordinal);
            Assert.Contains("\"eventCode\":\"session_start\"", text, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\Users\\example", text, StringComparison.Ordinal);
            Assert.Contains("<path>", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Respects_minimum_level_filter()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-logs-level-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new StructuredLocalLog(
                root,
                minimumLevel: DiagnosticLogLevel.Warning);
            log.Information("shell", "should not appear");
            log.Warning("shell", "should appear", eventCode: "warn");

            var text = File.ReadAllText(log.ActiveLogPath);
            Assert.DoesNotContain("should not appear", text, StringComparison.Ordinal);
            Assert.Contains("should appear", text, StringComparison.Ordinal);
            Assert.Contains("\"level\":\"Warning\"", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rotates_when_active_file_would_exceed_byte_budget()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-logs-rotate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new StructuredLocalLog(root);
            Directory.CreateDirectory(log.LogsDirectory);
            // Pre-seed an oversized active file so the next write rotates.
            File.WriteAllBytes(
                log.ActiveLogPath,
                new byte[StructuredLocalLog.MaximumActiveLogBytes]);

            log.Error("shell", "rotation required", eventCode: "rotate");

            Assert.True(File.Exists(log.ActiveLogPath));
            var active = File.ReadAllText(log.ActiveLogPath);
            Assert.Contains("rotation required", active, StringComparison.Ordinal);
            var rotated = Directory.GetFiles(
                log.LogsDirectory,
                StructuredLocalLog.RotatedLogFilePrefix
                    + "*"
                    + StructuredLocalLog.RotatedLogFileSuffix);
            Assert.NotEmpty(rotated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_relative_user_data_root()
    {
        Assert.Throws<ArgumentException>(() => new StructuredLocalLog("relative/path"));
    }

    [Fact]
    public void Prunes_rotated_files_beyond_retention_limit()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-logs-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new StructuredLocalLog(root);
            Directory.CreateDirectory(log.LogsDirectory);
            for (var index = 0; index < StructuredLocalLog.MaximumRotatedFiles + 3; index++)
            {
                var path = Path.Combine(
                    log.LogsDirectory,
                    StructuredLocalLog.RotatedLogFilePrefix
                        + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
                        + StructuredLocalLog.RotatedLogFileSuffix);
                File.WriteAllText(path, "{}\n");
                File.SetCreationTimeUtc(path, DateTime.UtcNow.AddMinutes(-index));
            }

            File.WriteAllBytes(
                log.ActiveLogPath,
                new byte[StructuredLocalLog.MaximumActiveLogBytes]);
            log.Warning("shell", "trigger prune", eventCode: "prune");

            var rotated = Directory.GetFiles(
                log.LogsDirectory,
                StructuredLocalLog.RotatedLogFilePrefix
                    + "*"
                    + StructuredLocalLog.RotatedLogFileSuffix);
            Assert.True(rotated.Length <= StructuredLocalLog.MaximumRotatedFiles);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Truncates_oversized_messages()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-logs-trunc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = new StructuredLocalLog(root);
            var longMessage = new string('x', StructuredLocalLog.MaximumMessageCharacters + 80);
            log.Error("shell", longMessage);

            var text = File.ReadAllText(log.ActiveLogPath);
            Assert.DoesNotContain(longMessage, text, StringComparison.Ordinal);
            using var document = System.Text.Json.JsonDocument.Parse(text);
            var message = document.RootElement.GetProperty("message").GetString();
            Assert.NotNull(message);
            Assert.Equal(StructuredLocalLog.MaximumMessageCharacters, message.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
