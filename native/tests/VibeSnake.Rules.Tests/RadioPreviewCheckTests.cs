using System.Diagnostics;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class RadioPreviewCheckTests
{
    [Fact]
    public void List_prints_the_fixed_catalog_and_skips_empty_files()
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-radio-preview-");
        try
        {
            var directory = Path.Combine(root.FullName, "archive", "radio");
            Directory.CreateDirectory(directory);
            foreach (var sample in RadioPreviewCheck.Catalog)
            {
                File.WriteAllBytes(Path.Combine(directory, sample.FileName), [1]);
            }

            File.WriteAllBytes(Path.Combine(directory, RadioPreviewCheck.Catalog[1].FileName), []);
            var resolved = RadioPreviewCheck.RequireDirectory(root.FullName, directory);
            var samples = RadioPreviewCheck.FindSamples(resolved);
            Assert.Equal(7, samples.Count);
            Assert.DoesNotContain(samples, sample => sample.FileName == RadioPreviewCheck.Catalog[1].FileName);
            Assert.Equal(RadioPreviewCheck.Catalog[0].Station, samples[0].Station);
            Assert.Equal("flow_signal_crystalline_frequency.mp3", samples[0].FileName);

            var output = new StringWriter();
            var error = new StringWriter();
            var code = RepositoryCheckCommand.Run(
                ["radio-preview", root.FullName, "list", directory],
                output,
                error);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            var lines = output.ToString().TrimEnd().Split(Environment.NewLine);
            Assert.Equal(7, lines.Length);
            Assert.Equal("The Flow Signal: flow_signal_crystalline_frequency.mp3", lines[0]);
            Assert.Equal("The Global Coil: global_coil_dancehall_fang.mp3", lines[1]);
            Assert.DoesNotContain("play", output.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Directory_policy_matches_the_python_preview_guard()
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-radio-preview-policy-");
        var outside = Directory.CreateTempSubdirectory("vibesnake-radio-preview-outside-");
        try
        {
            var assets = Path.Combine(root.FullName, "assets", "audio");
            var docs = Path.Combine(root.FullName, "docs");
            var archive = Path.Combine(root.FullName, "archive", "radio");
            Directory.CreateDirectory(assets);
            Directory.CreateDirectory(docs);
            Directory.CreateDirectory(archive);
            Directory.CreateDirectory(outside.FullName);

            var publicError = Assert.Throws<RadioPreviewException>(() =>
                RadioPreviewCheck.RequireDirectory(root.FullName, assets));
            Assert.Equal(RadioPreviewCheck.PublicAssetsMessage, publicError.Message);
            var repoError = Assert.Throws<RadioPreviewException>(() =>
                RadioPreviewCheck.RequireDirectory(root.FullName, docs));
            Assert.Equal(RadioPreviewCheck.ArchiveMessage, repoError.Message);
            Assert.Equal(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(archive)),
                RadioPreviewCheck.RequireDirectory(root.FullName, archive));
            Assert.Equal(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(outside.FullName)),
                RadioPreviewCheck.RequireDirectory(root.FullName, outside.FullName));

            var filePath = Path.Combine(outside.FullName, "not-a-directory");
            File.WriteAllBytes(filePath, [1]);
            var fileError = Assert.Throws<RadioPreviewException>(() =>
                RadioPreviewCheck.RequireDirectory(root.FullName, filePath));
            Assert.Equal(RadioPreviewCheck.DirectoryMessage, fileError.Message);

            var missing = Path.Combine(outside.FullName, "missing");
            var resolvedMissing = RadioPreviewCheck.RequireDirectory(root.FullName, missing);
            Assert.Empty(RadioPreviewCheck.FindSamples(resolvedMissing));

            var link = Path.Combine(root.FullName, "linked-preview");
            LinkDirectory(link, outside.FullName);
            var linkError = Assert.Throws<RadioPreviewException>(() =>
                RadioPreviewCheck.RequireDirectory(root.FullName, link));
            Assert.Equal(RadioPreviewCheck.DirectoryMessage, linkError.Message);
        }
        finally
        {
            var link = Path.Combine(root.FullName, "linked-preview");
            if (Directory.Exists(link))
            {
                Directory.Delete(link, recursive: false);
            }

            root.Delete(recursive: true);
            outside.Delete(recursive: true);
        }
    }

    [Fact]
    public void Command_rejects_playback_and_reports_an_empty_catalog()
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-radio-preview-command-");
        try
        {
            var archive = Path.Combine(root.FullName, "archive");
            Directory.CreateDirectory(archive);
            var before = Directory.GetFileSystemEntries(archive).Length;

            var usage = new StringWriter();
            var usageError = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["radio-preview", root.FullName, "play", archive],
                    usage,
                    usageError));
            Assert.Equal(string.Empty, usage.ToString());
            Assert.Contains(
                "RepositoryChecks radio-preview <repository-root> list <directory>",
                usageError.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain("pygame", usageError.ToString(), StringComparison.OrdinalIgnoreCase);

            var invalidRoot = new StringWriter();
            var invalidRootError = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["radio-preview", "bad\0root", "list", archive],
                    invalidRoot,
                    invalidRootError));
            Assert.Equal("Repository root is invalid." + Environment.NewLine, invalidRootError.ToString());

            var invalidPath = new StringWriter();
            var invalidPathError = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["radio-preview", root.FullName, "list", " "],
                    invalidPath,
                    invalidPathError));
            Assert.Equal("Radio preview path is invalid." + Environment.NewLine, invalidPathError.ToString());

            var empty = new StringWriter();
            var emptyError = new StringWriter();
            Assert.Equal(
                1,
                RepositoryCheckCommand.Run(
                    ["radio-preview", root.FullName, "list", archive],
                    empty,
                    emptyError));
            Assert.Equal(string.Empty, emptyError.ToString());
            Assert.Equal(
                RadioPreviewCheck.MissingMessage(Path.TrimEndingDirectorySeparator(Path.GetFullPath(archive)))
                + Environment.NewLine,
                empty.ToString());
            Assert.Equal(before, Directory.GetFileSystemEntries(archive).Length);

            var assets = Path.Combine(root.FullName, "assets");
            Directory.CreateDirectory(assets);
            var rejected = new StringWriter();
            var rejectedError = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["radio-preview", root.FullName, "list", assets],
                    rejected,
                    rejectedError));
            Assert.Equal(
                "Radio preview failed: " + RadioPreviewCheck.PublicAssetsMessage + Environment.NewLine,
                rejectedError.ToString());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static void LinkDirectory(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Assert.NotNull(process);
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
    }
}
