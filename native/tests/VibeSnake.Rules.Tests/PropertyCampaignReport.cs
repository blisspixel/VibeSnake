using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// Versioned report producer for deterministic property campaigns over the pure
/// rules kernel. Host-dependent wall time is optional metadata only; pass/fail
/// is driven exclusively by invariant outcomes.
/// </summary>
internal static class PropertyCampaignReport
{
    public const int SchemaVersion = 1;
    public const string Kind = "rules-property-campaign-v1";

    private const string DefaultRelativeOutputDirectory = "TestResults/native";
    private const string TestProject =
        "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Write(
        PropertyCampaignResult result,
        string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        outputDirectory ??= Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? ResolveDefaultEvidenceDirectory()
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var path = Path.Combine(outputDirectory, "property_campaign.json");
        var document = new
        {
            SchemaVersion,
            Kind,
            result.CampaignId,
            Engine = new
            {
                RulesetId = SnakeRun.RulesetId,
                RulesVersion = SnakeRun.RulesVersion,
                CanonicalStateSchemaVersion = SnakeRun.CanonicalStateSchemaVersion,
                StateHashAlgorithm = SnakeRun.StateHashAlgorithmId,
                RandomAlgorithm = Pcg32.AlgorithmId,
            },
            Environment = new
            {
                SourceRevision = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "unavailable",
                Framework = RuntimeInformation.FrameworkDescription,
                OperatingSystem = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            },
            result.SeedCount,
            result.Seeds,
            result.OperationsPerSeed,
            result.StepsExecuted,
            result.RestoresExecuted,
            result.RestartsExecuted,
            result.InvariantsChecked,
            result.Passed,
            FirstFailure = result.FirstFailure,
            ReproductionCommand =
                $"dotnet test {TestProject} --filter \"FullyQualifiedName~{result.TestFilter}\"",
            Notes = new[]
            {
                "Deterministic property campaign over pure rules only.",
                "Does not claim presentation frame times or human feel.",
            },
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(document, SerializerOptions) + "\n");
        return path;
    }

    private static string ResolveDefaultEvidenceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var roadmap = Path.Combine(directory.FullName, "ROADMAP.md");
            var solution = Path.Combine(directory.FullName, "native", "VibeSnake.slnx");
            if (File.Exists(roadmap) && File.Exists(solution))
            {
                return Path.Combine(directory.FullName, "TestResults", "native");
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, DefaultRelativeOutputDirectory));
    }
}

internal sealed record PropertyCampaignResult(
    string CampaignId,
    string TestFilter,
    int SeedCount,
    IReadOnlyList<ulong> Seeds,
    int OperationsPerSeed,
    int StepsExecuted,
    int RestoresExecuted,
    int RestartsExecuted,
    IReadOnlyList<string> InvariantsChecked,
    bool Passed,
    PropertyCampaignFailure? FirstFailure);

internal sealed record PropertyCampaignFailure(
    ulong Seed,
    int Operation,
    int Tick,
    string Invariant,
    string Detail,
    string StateHash);
