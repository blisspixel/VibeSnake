using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VibeSnake.Rules.Tests;

internal sealed record ParityDivergenceRequest(
    string Contract,
    string Fixture,
    string TestFilter,
    string CaseId,
    long? Seed,
    int FirstDivergentStep,
    object InitialState,
    object CommandPrefix,
    object ExpectedState,
    object ExpectedEvents,
    object ActualState,
    object ActualEvents,
    object ActualCanonicalState,
    string ActualStateHash,
    object? MinimizedCommandPrefix = null,
    int? MinimizedStepCount = null);

internal static class ParityDivergence
{
    public const int SchemaVersion = 1;

    private const string DefaultRelativeOutputDirectory = "TestResults/native/divergence";
    private const string TestProject =
        "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
        },
    };

    public static bool AreEquivalent(object expected, object actual)
    {
        var expectedNode = JsonSerializer.SerializeToNode(expected, SerializerOptions);
        var actualNode = JsonSerializer.SerializeToNode(actual, SerializerOptions);
        return JsonNode.DeepEquals(expectedNode, actualNode);
    }

    public static string WriteBundle(
        ParityDivergenceRequest request,
        string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Contract))
        {
            throw new ArgumentException("Divergence contract is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.CaseId))
        {
            throw new ArgumentException("Divergence case ID is required.", nameof(request));
        }

        if (request.FirstDivergentStep < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        outputDirectory ??= Environment.GetEnvironmentVariable("VIBESNAKE_DIVERGENCE_DIR");
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetFullPath(DefaultRelativeOutputDirectory)
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var bundle = new
        {
            SchemaVersion,
            Kind = "python-csharp-first-divergence",
            request.Contract,
            request.Fixture,
            request.CaseId,
            request.Seed,
            request.FirstDivergentStep,
            ReproductionCommand =
                $"dotnet test {TestProject} --configuration Release "
                + $"--filter \"FullyQualifiedName~{request.TestFilter}\"",
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
            request.InitialState,
            request.CommandPrefix,
            MinimizedCommandPrefix = request.MinimizedCommandPrefix,
            MinimizedStepCount = request.MinimizedStepCount,
            Expected = new
            {
                State = request.ExpectedState,
                Events = request.ExpectedEvents,
            },
            Actual = new
            {
                State = request.ActualState,
                Events = request.ActualEvents,
                CanonicalState = request.ActualCanonicalState,
                StateHash = request.ActualStateHash,
            },
        };

        var safeContract = SafeFileSegment(request.Contract);
        var safeCaseId = SafeFileSegment(request.CaseId);
        var path = Path.Combine(
            outputDirectory,
            $"{safeContract}-{safeCaseId}-step-{request.FirstDivergentStep:D6}.json");
        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(bundle, SerializerOptions) + "\n";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
        return path;
    }

    public static void ThrowWithBundle(ParityDivergenceRequest request)
    {
        var path = WriteBundle(request);
        throw new Xunit.Sdk.XunitException(
            $"Parity diverged in {request.CaseId} at step {request.FirstDivergentStep}. "
            + $"Reproduction bundle: {path}");
    }

    private static string SafeFileSegment(string value) => string.Concat(
        value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '_'));
}
