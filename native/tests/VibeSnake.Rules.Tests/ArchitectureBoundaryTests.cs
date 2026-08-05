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
    public void Rules_and_persistence_form_a_one_way_dependency()
    {
        var rulesRefs = typeof(SnakeRun).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty);
        var persistenceRefs = typeof(ReplayStore).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty);

        Assert.DoesNotContain(
            rulesRefs,
            name => name.Equals("VibeSnake.Persistence", StringComparison.Ordinal));
        Assert.Contains(
            persistenceRefs,
            name => name.Equals("VibeSnake.Rules", StringComparison.Ordinal));
        Assert.DoesNotContain(
            rulesRefs,
            name => name.Equals("VibeSnake.Game", StringComparison.Ordinal));
        Assert.DoesNotContain(
            persistenceRefs,
            name => name.Equals("VibeSnake.Game", StringComparison.Ordinal));
    }

    private static readonly string[] ForbiddenRulesSourceFragments =
    [
        "using Godot",
        "System.Drawing",
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Random",
        "DateTime.Now",
        "DateTime.UtcNow",
        "Environment.GetEnvironmentVariable",
        "HttpClient",
        // Pure rules must not touch the filesystem; Persistence owns I/O.
        "using System.IO",
        "File.Open",
        "File.Read",
        "File.Write",
        "File.Create",
        "File.Delete",
        "File.Exists",
        "File.Copy",
        "File.Move",
        "Directory.Create",
        "Directory.Delete",
        "Directory.Exists",
        "Directory.Enumerate",
        "Directory.Get",
        "FileStream",
        "StreamReader",
        "StreamWriter",
        "FileInfo",
        "DirectoryInfo",
        "Path.Combine",
        "Path.GetFullPath",
        "Path.GetTemp",
        "Process.Start",
        "Process.GetCurrentProcess",
        "Thread.Sleep",
    ];

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
            foreach (var fragment in ForbiddenRulesSourceFragments)
            {
                if (text.Contains(fragment, StringComparison.Ordinal))
                {
                    offenders.Add(
                        Path.GetRelativePath(rulesDirectory, file) + " (" + fragment + ")");
                    break;
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Rules sources contain forbidden dependencies: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Persistence_source_tree_does_not_import_network_clients()
    {
        var persistenceDirectory = ResolveSourceDirectory("VibeSnake.Persistence");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
            persistenceDirectory,
            "*.cs",
            SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (text.Contains("using Godot", StringComparison.Ordinal)
                || text.Contains("System.Net.Http", StringComparison.Ordinal)
                || text.Contains("System.Net.Sockets", StringComparison.Ordinal)
                || text.Contains("HttpClient", StringComparison.Ordinal)
                || text.Contains("WebRequest", StringComparison.Ordinal)
                || text.Contains("System.Random", StringComparison.Ordinal)
                || text.Contains("DateTime.Now", StringComparison.Ordinal)
                || text.Contains("DateTime.UtcNow", StringComparison.Ordinal)
                || text.Contains("Environment.GetEnvironmentVariable", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(persistenceDirectory, file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Persistence sources contain network client surfaces: "
                + string.Join(", ", offenders));
    }

    private static string ResolveRulesSourceDirectory() =>
        ResolveSourceDirectory("VibeSnake.Rules");

    private static string ResolveSourceDirectory(string projectName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "native",
                "src",
                projectName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate native/src/" + projectName + ".");
    }
}
