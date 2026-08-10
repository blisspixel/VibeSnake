using VibeSnake.Persistence;
using System.Text.Json;

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
                    "Failed under C:\\Users\\example\\secret\\file.txt and "
                    + "/var/lib/vibesnake/private/save.json"));

            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("\"schemaVersion\": 1", text, StringComparison.Ordinal);
            Assert.Contains("vibesnake-core", text, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\Users\\example", text, StringComparison.Ordinal);
            Assert.DoesNotContain("/var/lib/vibesnake", text, StringComparison.Ordinal);
            Assert.Contains("<path>", text, StringComparison.Ordinal);
            Assert.Single(diagnostics.ListReportFileNames());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Writes_optional_config_hash_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-diagnostics-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var diagnostics = new LocalDiagnostics(root);
            var configHash = new string('a', 64);
            var path = diagnostics.WriteCrashReport(
                appVersion: "0.2.1",
                platform: "Windows",
                rulesetId: "vibesnake-core",
                rulesVersion: 4,
                screenState: "Running",
                exception: new InvalidOperationException("probe"),
                configHash: configHash,
                configHashAlgorithm: "sha256-canonical-runconfig-v1");

            var text = File.ReadAllText(path);
            Assert.Contains(configHash, text, StringComparison.Ordinal);
            Assert.Contains("sha256-canonical-runconfig-v1", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Writes_bounded_reproducible_first_divergence_report()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-divergence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var diagnostics = new LocalDiagnostics(root);
            var commands = Enumerable.Range(0, 70)
                .Select(index => index == 69
                    ? "Up C:\\Users\\example\\private\\trace.json"
                    : $"Left-{index}")
                .ToArray();
            var path = diagnostics.WriteDivergenceReport(
                appVersion: "0.2.1",
                platform: "Windows",
                rulesetId: "vibesnake-core",
                rulesVersion: 4,
                campaignId: "candidate-reliability",
                modeId: "vibe",
                gameplaySeed: 0x1234UL,
                controllerSeed: 0x5678UL,
                runIndex: 2,
                firstDivergentStep: 17,
                expectedStateHash: "1111111111111111",
                actualStateHash: "2222222222222222",
                recentCommands: commands,
                timeProvider: new FixedDiagnosticsTimeProvider(
                    new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero)));

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var rootElement = document.RootElement;
            Assert.Equal(1, rootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                "deterministic-divergence-report-v1",
                rootElement.GetProperty("kind").GetString());
            Assert.Equal("0000000000001234", rootElement.GetProperty("gameplaySeed").GetString());
            Assert.Equal("0000000000005678", rootElement.GetProperty("controllerSeed").GetString());
            Assert.Equal(17, rootElement.GetProperty("firstDivergentStep").GetInt32());
            Assert.Equal(
                LocalDiagnostics.MaximumRecentCommands,
                rootElement.GetProperty("recentCommandCount").GetInt32());
            Assert.DoesNotContain("C:\\Users\\example", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Single(diagnostics.ListDivergenceReportFileNames());
            Assert.Empty(diagnostics.ListReportFileNames());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Divergence_report_rejects_invalid_reproduction_fields()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-divergence-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var diagnostics = new LocalDiagnostics(root);
            Assert.Throws<ArgumentOutOfRangeException>(() => diagnostics.WriteDivergenceReport(
                "0.2.1", "Linux", "vibesnake-core", 4, "campaign", "classic",
                1, 2, -1, 0, "1111111111111111", "2222222222222222", []));
            Assert.Throws<ArgumentOutOfRangeException>(() => diagnostics.WriteDivergenceReport(
                "0.2.1", "Linux", "vibesnake-core", 4, "campaign", "classic",
                1, 2, 0, -1, "1111111111111111", "2222222222222222", []));
            Assert.Throws<ArgumentException>(() => diagnostics.WriteDivergenceReport(
                "0.2.1", "Linux", "vibesnake-core", 4, "campaign", "classic",
                1, 2, 0, 0, "bad", "2222222222222222", []));
            Assert.Throws<ArgumentException>(() => diagnostics.WriteDivergenceReport(
                "0.2.1", "Linux", "vibesnake-core", 4, "campaign", "classic",
                1, 2, 0, 0, "1111111111111111", "BAD", []));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_invalid_config_hash_shape()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-diagnostics-badhash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var diagnostics = new LocalDiagnostics(root);
            Assert.Throws<ArgumentException>(() => diagnostics.WriteCrashReport(
                appVersion: "0.2.1",
                platform: "Windows",
                rulesetId: "vibesnake-core",
                rulesVersion: 4,
                screenState: "Menu",
                exception: new InvalidOperationException("probe"),
                configHash: "not-hex"));
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
    public void EnsureDiagnosticsDirectory_creates_absolute_diagnostics_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-diagnostics-ensure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var diagnostics = new LocalDiagnostics(root);
            var path = diagnostics.EnsureDiagnosticsDirectory();
            Assert.True(Directory.Exists(path));
            Assert.True(Path.IsPathFullyQualified(path));
            Assert.Equal(diagnostics.DiagnosticsDirectory, path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    private sealed class FixedDiagnosticsTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
