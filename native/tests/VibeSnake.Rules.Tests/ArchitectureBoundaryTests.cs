using System.Reflection;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] ForbiddenRulesAssemblyNameFragments =
    [
        "Godot",
        "GodotSharp",
        "Pygame",
        "System.Drawing",
        "System.Net.Http",
        "System.Net.Sockets",
    ];

    [Fact]
    public void Rules_assembly_has_no_godot_or_presentation_dependencies()
    {
        var assembly = typeof(SnakeRun).Assembly;
        var referencedNames = assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        foreach (var forbidden in ForbiddenRulesAssemblyNameFragments)
        {
            Assert.DoesNotContain(
                referencedNames,
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }

        Assert.DoesNotContain(
            referencedNames,
            name => name.Equals("VibeSnake.Game", StringComparison.Ordinal));
        Assert.DoesNotContain(
            referencedNames,
            name => name.Equals("VibeSnake.Persistence", StringComparison.Ordinal));
    }

    [Fact]
    public void Persistence_assembly_depends_on_rules_but_not_godot()
    {
        var assembly = typeof(ReplayStore).Assembly;
        var referencedNames = assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        Assert.Contains(
            referencedNames,
            name => name.Equals("VibeSnake.Rules", StringComparison.Ordinal));
        Assert.DoesNotContain(
            referencedNames,
            name => name.Contains("Godot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            referencedNames,
            name => name.Equals("VibeSnake.Game", StringComparison.Ordinal));
        Assert.DoesNotContain(
            referencedNames,
            name => name.Equals("System.Net.Http", StringComparison.Ordinal));
    }

    [Fact]
    public void Rules_source_tree_does_not_import_forbidden_namespaces()
    {
        var rulesDirectory = ResolveRulesSourceDirectory();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(rulesDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (text.Contains("using Godot", StringComparison.Ordinal)
                || text.Contains("System.Drawing", StringComparison.Ordinal)
                || text.Contains("System.Net.Http", StringComparison.Ordinal)
                || text.Contains("System.Net.Sockets", StringComparison.Ordinal)
                || text.Contains("System.Random", StringComparison.Ordinal)
                || text.Contains("DateTime.Now", StringComparison.Ordinal)
                || text.Contains("DateTime.UtcNow", StringComparison.Ordinal)
                || text.Contains("Environment.GetEnvironmentVariable", StringComparison.Ordinal)
                || text.Contains("HttpClient", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(rulesDirectory, file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Rules sources contain forbidden dependencies: " + string.Join(", ", offenders));
    }

    private static string ResolveRulesSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "native",
                "src",
                "VibeSnake.Rules");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate native/src/VibeSnake.Rules.");
    }
}
