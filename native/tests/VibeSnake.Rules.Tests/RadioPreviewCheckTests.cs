using System.Diagnostics;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

// Real process timers must not compete with all-core simulation campaigns.
[Collection(AgentHostIntegrationGroup.Name)]
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
                ExpectedCanonicalDirectory(archive),
                RadioPreviewCheck.RequireDirectory(root.FullName, archive));
            Assert.Equal(
                ExpectedCanonicalDirectory(outside.FullName),
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
            Assert.Throws<RadioPreviewException>(() =>
                RadioPreviewCheck.RequireDirectory(root.FullName, Path.Combine(link, "nested")));
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
    public void Command_rejects_incomplete_playback_and_reports_an_empty_catalog()
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
                RadioPreviewCheck.MissingMessage(ExpectedCanonicalDirectory(archive))
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

    [Theory]
    [InlineData("q\n", 0)]
    [InlineData("", 0)]
    [InlineData("\nq\n", 1)]
    [InlineData("\n", 1)]
    [InlineData("\n\n\n\n", 2)]
    public void Interactive_preview_stops_and_disposes_each_started_session(string responses, int expected)
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-preview-flow-");
        try
        {
            var samples = RadioPreviewCheck.Catalog.Take(2).Select(sample =>
            {
                var path = Path.Combine(root.FullName, sample.FileName);
                File.WriteAllBytes(path, [1]);
                return new RadioPreviewCheck.AvailableSample(sample.Station, sample.FileName, path);
            }).ToArray();
            var sessions = new List<FakeSession>();
            RadioPreviewPlayback.Play(samples, "fake-player", new StringReader(responses), new StringWriter(),
                (player, path) =>
                {
                    Assert.Equal("fake-player", player);
                    Assert.Contains(samples, sample => sample.FullPath == path);
                    var session = new FakeSession();
                    sessions.Add(session);
                    return session;
                });
            Assert.Equal(expected, sessions.Count);
            Assert.All(sessions, session => Assert.True(session.Disposed));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Player_arguments_keep_paths_literal_and_do_not_use_a_shell()
    {
        var path = Path.Combine(Path.GetTempPath(), "candidate with spaces & punctuation.mp3");
        var info = RadioPreviewPlayback.CreateStartInfo("explicit ffplay path", path);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Equal("explicit ffplay path", info.FileName);
        Assert.Equal(new[] { "-nodisp", "-autoexit", "-nostats", "-loglevel", "error", "-i", path }, info.ArgumentList);
    }

    [Theory]
    [InlineData("/var/folders/review", "/var", "/private/var/folders/review")]
    [InlineData("/tmp/review", "/tmp", "/private/tmp/review")]
    [InlineData("/tmp", "/tmp", "/private/tmp")]
    [InlineData("/tmp-other/review", "/tmp", "/tmp-other/review")]
    [InlineData("/varied/review", "/var", "/varied/review")]
    [InlineData("/private/var/review", "/var", "/private/var/review")]
    public void Trusted_mac_system_prefix_normalization_preserves_directory_boundaries(
        string path, string prefix, string expected)
    {
        Assert.Equal(expected, RadioPreviewCheck.NormalizeTrustedMacPrefix(path, prefix));
        Assert.Throws<ArgumentException>(() => RadioPreviewCheck.NormalizeTrustedMacPrefix(path, "/custom"));
    }

    [Fact]
    public void Playback_failure_disposes_session_and_never_starts_the_next_track()
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-preview-failure-");
        try
        {
            var path = Path.Combine(root.FullName, "track.mp3");
            File.WriteAllBytes(path, [1]);
            var sample = new RadioPreviewCheck.AvailableSample("Station", "track.mp3", path);
            var session = new FakeSession { Fail = true };
            var starts = 0;
            Assert.Throws<RadioPreviewException>(() => RadioPreviewPlayback.Play(
                [sample, sample], "fake", new StringReader("\n\n\n\n"), new StringWriter(),
                (_, _) => { starts++; return session; }));
            Assert.Equal(1, starts);
            Assert.True(session.Disposed);
            File.Delete(path);
            Assert.Throws<RadioPreviewException>(() => RadioPreviewPlayback.Play(
                [sample], "fake", new StringReader("\n"), new StringWriter(),
                (_, _) => throw new Xunit.Sdk.XunitException("Missing sample must not launch a player")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Playback_requires_an_explicit_existing_player()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(2, RepositoryCheckCommand.Run(
            ["radio-preview", Path.GetTempPath(), "play", Path.GetTempPath(), "bad\0player"], output, error));
        Assert.Contains("existing ffplay executable path", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void Real_process_cleanup_and_timeout_do_not_require_audio()
    {
        var start = SleepingProcess();
        using (var session = new RadioPreviewPlayback.PlayerSession(start, TimeSpan.FromMinutes(30)))
        {
            session.CheckResult();
            session.Dispose();
            session.Dispose();
        }

        using var timed = new RadioPreviewPlayback.PlayerSession(SleepingProcess(), TimeSpan.FromMilliseconds(50));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            try
            {
                timed.CheckResult();
                return false;
            }
            catch (RadioPreviewException exception)
            {
                Assert.Contains("playback limit", exception.Message, StringComparison.Ordinal);
                return true;
            }
        }, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Missing_process_executable_is_reported_without_leaking_a_session()
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            UseShellExecute = false,
        };
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            new RadioPreviewPlayback.PlayerSession(start, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Nonzero_process_exit_is_reported_without_audio()
    {
        var start = SleepingProcess();
        start.ArgumentList[start.ArgumentList.Count - 1] = "exit 7";
        using var session = new RadioPreviewPlayback.PlayerSession(start, TimeSpan.FromSeconds(10));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            try
            {
                session.CheckResult();
                return false;
            }
            catch (RadioPreviewException exception)
            {
                Assert.Contains("exited with code 7", exception.Message, StringComparison.Ordinal);
                return true;
            }
        }, TimeSpan.FromSeconds(10)));
    }

    private static ProcessStartInfo SleepingProcess()
    {
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("Start-Sleep -Seconds 30");
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("sleep 30");
        }

        return start;
    }

    private sealed class FakeSession : RadioPreviewPlayback.ISession
    {
        internal bool Disposed { get; private set; }
        internal bool Fail { get; init; }
        public void CheckResult()
        {
            if (Fail)
            {
                throw new RadioPreviewException("player failed");
            }
        }
        public void Dispose() => Disposed = true;
    }

    private static string ExpectedCanonicalDirectory(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OperatingSystem.IsMacOS())
        {
            foreach (var prefix in new[] { "/var", "/tmp" })
            {
                if (full == prefix || full.StartsWith(prefix + "/", StringComparison.Ordinal))
                {
                    var target = new DirectoryInfo(prefix).ResolveLinkTarget(returnFinalTarget: true);
                    if (target?.FullName == "/private" + prefix)
                    {
                        return "/private" + full;
                    }
                }
            }
        }

        return full;
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
