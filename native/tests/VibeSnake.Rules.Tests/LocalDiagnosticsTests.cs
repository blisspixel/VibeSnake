using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class LocalDiagnosticsTests
{
    [Fact]
    public void Writes_sanitized_crash_report_under_user_data()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var diagnostics = new LocalDiagnostics(root);
            var path = diagnostics.WriteCrashReport(
                appVersion: "0.2.1",
                platform: "Windows",
                rulesetId: "vibesnake-core",
                rulesVersion: 4,
                screenState: "Running",
                exception: new InvalidOperationException(
                    "Failed under C:\\Users\\example\\secret\\file.txt"));

            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("\"schemaVersion\": 1", text, StringComparison.Ordinal);
            Assert.Contains("vibesnake-core", text, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\Users\\example", text, StringComparison.Ordinal);
            Assert.Contains("<path>", text, StringComparison.Ordinal);
            Assert.Single(diagnostics.ListReportFileNames());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_non_absolute_user_data_root()
    {
        Assert.Throws<ArgumentException>(() => new LocalDiagnostics("relative/path"));
    }

    [Fact]
    public void Prunes_reports_beyond_retention_limit()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-diagnostics-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var diagnostics = new LocalDiagnostics(root);
            for (var index = 0; index < LocalDiagnostics.MaximumReportsRetained + 5; index++)
            {
                diagnostics.WriteCrashReport(
                    appVersion: "0.2.1",
                    platform: "Linux",
                    rulesetId: "vibesnake-core",
                    rulesVersion: 4,
                    screenState: "Menu",
                    exception: new InvalidOperationException($"probe-{index}"));
                Thread.Sleep(5);
            }

            Assert.True(diagnostics.ListReportFileNames().Count <= LocalDiagnostics.MaximumReportsRetained);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Truncates_long_messages_and_lists_empty_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-diagnostics-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var diagnostics = new LocalDiagnostics(root);
            Assert.Empty(diagnostics.ListReportFileNames());
            var path = diagnostics.WriteCrashReport(
                appVersion: "0.2.1",
                platform: "macOS",
                rulesetId: "vibesnake-core",
                rulesVersion: 4,
                screenState: "Ended",
                exception: new InvalidOperationException(new string('x', LocalDiagnostics.MaximumMessageCharacters + 50)));
            var text = File.ReadAllText(path);
            Assert.True(text.Length < LocalDiagnostics.MaximumMessageCharacters + 500);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
