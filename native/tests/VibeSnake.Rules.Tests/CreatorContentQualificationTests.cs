using System.Text;
using System.Text.Json;
using ValidateCreatorContent;

namespace VibeSnake.Rules.Tests;

public sealed class CreatorContentQualificationTests
{
    private static readonly string[] PersonalityCodes =
    [
        "personality-success",
        "personality-empty",
        "personality-invalid-json",
        "personality-unsupported-schema",
        "personality-missing-field",
        "personality-invalid-type",
        "personality-out-of-range",
        "personality-invalid-color",
        "personality-path-unsafe",
        "personality-io-error",
        "personality-unknown-field",
        "personality-duplicate-field",
        "personality-too-large",
        "personality-reserved-id",
        "personality-capacity-exceeded",
        "personality-duplicate-id",
    ];

    private static readonly string[] PackCodes =
    [
        "pack-set-valid",
        "pack-set-incompatible",
        "pack-set-invalid",
        "core-kind-required",
        "optional-kind-invalid",
        "pack-id-collision",
        "compatible",
        "game-version-too-old",
        "game-version-too-new",
        "ruleset-mismatch",
        "rules-version-too-old",
        "rules-version-too-new",
        "missing-dependency",
        "dependency-version-too-old",
        "dependency-version-too-new",
    ];

    [Fact]
    public void Creator_commands_schemas_examples_and_no_code_boundary_are_published()
    {
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var contentRoot = Path.Combine(repositoryRoot, "docs", "content");
        var personalitySchemaPath = Path.Combine(contentRoot, "schemas", "personality.schema.json");
        var radioSchemaPath = Path.Combine(contentRoot, "schemas", "radio-pack.schema.json");
        var personalityExamplePath = Path.Combine(contentRoot, "examples", "personality.schema1.json");
        var radioExamplePath = Path.Combine(contentRoot, "examples", "radio-pack.schema1.json");
        var guidePath = Path.Combine(contentRoot, "CREATOR_CONTENT.md");

        using var personalitySchema = JsonDocument.Parse(File.ReadAllText(personalitySchemaPath));
        using var radioSchema = JsonDocument.Parse(File.ReadAllText(radioSchemaPath));
        using var radioExample = JsonDocument.Parse(File.ReadAllText(radioExamplePath));
        Assert.False(personalitySchema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.False(radioSchema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("radio", radioExample.RootElement.GetProperty("kind").GetString());
        Assert.Equal(
            "audio/mpeg",
            radioExample.RootElement.GetProperty("files")[0].GetProperty("mediaType").GetString());
        Assert.Equal(
            "radio-track",
            radioExample.RootElement.GetProperty("files")[0].GetProperty("role").GetString());

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CreatorContentCommand.Run(
            ["personality", personalityExamplePath, "--id", "route_planner"],
            output,
            error);
        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        using var exampleReport = JsonDocument.Parse(output.ToString());
        Assert.Equal(CreatorContentCommand.Contract, exampleReport.RootElement
            .GetProperty("contract").GetString());
        Assert.True(exampleReport.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(exampleReport.RootElement.GetProperty("executesContent").GetBoolean());
        Assert.False(exampleReport.RootElement.GetProperty("arbitraryCodeSupported").GetBoolean());

        var guide = File.ReadAllText(guidePath);
        Assert.All(PersonalityCodes, code => Assert.Contains(code, guide, StringComparison.Ordinal));
        Assert.All(PackCodes, code => Assert.Contains(code, guide, StringComparison.Ordinal));
        Assert.Contains("duplicate optional ID is a hard collision", guide, StringComparison.Ordinal);
        Assert.Contains("Arbitrary code plugins remain outside 1.0", guide, StringComparison.Ordinal);

        var toolRoot = Path.Combine(repositoryRoot, "native", "tools", "ValidateCreatorContent");
        var toolSource = string.Join(
            '\n',
            Directory.GetFiles(toolRoot, "*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        foreach (var prohibited in new[]
        {
            "Process.Start",
            "Assembly.Load",
            "NativeLibrary.Load",
            "DllImport",
            "CSharpScript",
            "System.Reflection.Emit",
            "Activator.CreateInstance",
            "HttpClient",
            "WebRequest",
            "System.Net.Sockets",
        })
        {
            Assert.DoesNotContain(prohibited, toolSource, StringComparison.Ordinal);
        }

        var referencedAssemblies = typeof(CreatorContentCommand).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("Godot", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("Pygame", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("System.Net", StringComparison.Ordinal));

        var evidence = new
        {
            SchemaVersion = 1,
            Kind = "creator-content-qualification-v1",
            Passed = true,
            Contract = CreatorContentCommand.Contract,
            Commands = new[] { "personality", "pack-set" },
            SchemaCount = 2,
            ExampleCount = 2,
            PersonalityCodeCount = PersonalityCodes.Length,
            PackAndCompatibilityCodeCount = PackCodes.Length,
            CanonicalManifestRequired = true,
            CollisionPolicy = "reject-all-duplicate-pack-ids",
            ResolutionOrder = "core-then-ordinal-unique-optional-ids",
            ExecutesContent = false,
            ArbitraryCodeSupported = false,
            SchemasPublished = true,
            ExamplesPublished = true,
            StableErrorCodesPublished = true,
            CollisionRulesPublished = true,
            NoEngineOrNetworkReferences = true,
            RuntimePayloadKinds = new[] { "typed-personality-values", "inventory-approved-audio-mpeg" },
            ReferencedAssemblies = referencedAssemblies,
        };
        var evidenceDirectory = Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        evidenceDirectory = string.IsNullOrWhiteSpace(evidenceDirectory)
            ? Path.Combine(repositoryRoot, "TestResults", "native")
            : Path.GetFullPath(evidenceDirectory);
        Directory.CreateDirectory(evidenceDirectory);
        var evidencePath = Path.Combine(evidenceDirectory, "creator_content.json");
        File.WriteAllText(
            evidencePath,
            JsonSerializer.Serialize(
                evidence,
                TestJsonSerializerOptions.CamelCaseIndented) + "\n",
            new UTF8Encoding(false));

        Assert.True(File.Exists(evidencePath));
    }
}
