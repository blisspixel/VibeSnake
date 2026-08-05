using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class PersonalityDocumentTests
{
    [Fact]
    public void Accepts_legacy_custom_personality_shape()
    {
        var result = PersonalityDocument.Read(
            """
            {
              "name": "Military Tactician",
              "description": "Calculates ahead and prioritizes survival.",
              "aggression": 0.4,
              "risk_tolerance": 0.2,
              "patience": 0.95,
              "greed": 0.3,
              "chaos": 0.0,
              "power_up_priority": 0.7,
              "color": [50, 100, 150]
            }
            """,
            "military_tactician.json");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Document);
        Assert.Equal(1, result.Document.SchemaVersion);
        Assert.Equal("Military Tactician", result.Document.Name);
        Assert.Equal(0.4, result.Document.Aggression);
        Assert.Equal([50, 100, 150], result.Document.Color);
    }

    [Fact]
    public void Rejects_boolean_traits_and_invalid_color()
    {
        var result = PersonalityDocument.Read(
            """
            {
              "name": "Broken",
              "description": "Bad data",
              "aggression": true,
              "risk_tolerance": 0.5,
              "patience": 0.5,
              "greed": 0.5,
              "chaos": 0.5,
              "power_up_priority": 0.5,
              "color": [0, 1]
            }
            """,
            "broken.json");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues!, issue => issue.Field == "aggression");
        Assert.Contains(result.Issues!, issue => issue.Field == "color");
        Assert.Contains("broken.json", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_out_of_range_traits()
    {
        var result = PersonalityDocument.Read(
            """
            {
              "name": "Over",
              "description": "Too greedy",
              "aggression": 0.1,
              "risk_tolerance": 0.1,
              "patience": 0.1,
              "greed": 1.5,
              "chaos": 0.1,
              "power_up_priority": 0.1,
              "color": [1, 2, 3]
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues!, issue => issue.Field == "greed");
    }

    [Fact]
    public void Rejects_future_schema()
    {
        var result = PersonalityDocument.Read(
            """
            {
              "schema_version": 99,
              "name": "Future",
              "description": "Not yet",
              "aggression": 0.1,
              "risk_tolerance": 0.1,
              "patience": 0.1,
              "greed": 0.1,
              "chaos": 0.1,
              "power_up_priority": 0.1,
              "color": [1, 2, 3]
            }
            """);

        Assert.Equal(PersonalityLoadCode.UnsupportedSchema, result.Code);
    }

    [Fact]
    public void Loads_example_file_from_checkout_when_present()
    {
        var path = ResolveExamplePath();
        if (path is null)
        {
            return;
        }

        var result = PersonalityDocument.ReadFile(path);
        Assert.True(result.IsSuccess, result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Document!.Name));
    }

    [Fact]
    public void Rejects_empty_payload_and_unsafe_file_names()
    {
        Assert.Equal(PersonalityLoadCode.Empty, PersonalityDocument.Read("").Code);
        Assert.Equal(PersonalityLoadCode.InvalidJson, PersonalityDocument.Read("{").Code);
        Assert.Equal(
            PersonalityLoadCode.PathUnsafe,
            PersonalityDocument.ReadFile("not-json.txt").Code);
    }

    [Fact]
    public void Rejects_missing_name_and_non_object_root()
    {
        Assert.Equal(PersonalityLoadCode.InvalidType, PersonalityDocument.Read("[]").Code);
        var missing = PersonalityDocument.Read(
            """
            {
              "description": "No name",
              "aggression": 0.1,
              "risk_tolerance": 0.1,
              "patience": 0.1,
              "greed": 0.1,
              "chaos": 0.1,
              "power_up_priority": 0.1,
              "color": [1, 2, 3]
            }
            """);
        Assert.False(missing.IsSuccess);
        Assert.Contains(missing.Issues!, issue => issue.Field == "name");
    }

    [Fact]
    public void Rejects_blank_strings_and_non_finite_traits()
    {
        var blank = PersonalityDocument.Read(
            """
            {
              "name": "   ",
              "description": "ok",
              "aggression": 0.1,
              "risk_tolerance": 0.1,
              "patience": 0.1,
              "greed": 0.1,
              "chaos": 0.1,
              "power_up_priority": 0.1,
              "color": [1, 2, 3]
            }
            """);
        Assert.False(blank.IsSuccess);
        Assert.Contains(blank.Issues!, issue => issue.Field == "name");

        var nan = PersonalityDocument.Read(
            """
            {
              "name": "NaN",
              "description": "bad trait",
              "aggression": "NaN",
              "risk_tolerance": 0.1,
              "patience": 0.1,
              "greed": 0.1,
              "chaos": 0.1,
              "power_up_priority": 0.1,
              "color": [1, 2, 3]
            }
            """);
        Assert.False(nan.IsSuccess);
        Assert.Contains(nan.Issues!, issue => issue.Field == "aggression");
    }

    [Fact]
    public void Rejects_path_traversal_and_missing_files()
    {
        // File name containing ".." is rejected before open.
        Assert.Equal(
            PersonalityLoadCode.PathUnsafe,
            PersonalityDocument.ReadFile("evil..name.json").Code);
        Assert.Equal(
            PersonalityLoadCode.PathUnsafe,
            PersonalityDocument.ReadFile("not-a-json-file").Code);

        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-missing-" + Guid.NewGuid().ToString("N") + ".json");
        Assert.Equal(
            PersonalityLoadCode.IoError,
            PersonalityDocument.ReadFile(missingPath).Code);
    }

    [Fact]
    public void Accepts_explicit_schema_version_one()
    {
        var result = PersonalityDocument.Read(
            """
            {
              "schemaVersion": 1,
              "name": "Schema One",
              "description": "Explicit schema",
              "aggression": 0.2,
              "risk_tolerance": 0.3,
              "patience": 0.4,
              "greed": 0.5,
              "chaos": 0.6,
              "power_up_priority": 0.7,
              "color": [10, 20, 30]
            }
            """);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Document!.SchemaVersion);
        Assert.Equal(0.6, result.Document.Chaos);
    }

    private static string? ResolveExamplePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "assets",
                "ai",
                "examples",
                "military_tactician.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
