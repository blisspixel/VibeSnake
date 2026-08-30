using System.Diagnostics;
using System.Globalization;
using System.Text;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class ReadmeScreenshotCheckTests
{
    private const string FixedFingerprint =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly string[] InvalidToolchains =
    [
        "[]\n",
        "{}\n",
        "{\"godot\":[]}\n",
        "{\"godot\":{\"version\":1,\"flavor\":\"dotnet\",\"commit\":\"a13da4feb\"}}\n",
        "{\"godot\":{\"version\":\"4.7\",\"flavor\":\"dotnet\",\"commit\":\"a13da4feb\"}}\n",
        "{\"godot\":{\"version\":\"04.7.1\",\"flavor\":\"dotnet\",\"commit\":\"a13da4feb\"}}\n",
        "{\"godot\":{\"version\":\"4.7.1\",\"flavor\":\"standard\",\"commit\":\"a13da4feb\"}}\n",
        "{\"godot\":{\"version\":\"4.7.1\",\"flavor\":\"dotnet\",\"commit\":\"A13DA4FEB\"}}\n",
    ];

    [Fact]
    public void Exact_screenshot_fixture_passes_strict_native_verification()
    {
        WithTemporaryDirectory(root =>
        {
            WriteEvidenceFixture(root, FixedFingerprint);

            var result = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            Assert.Equal(
                "README screenshots verified: 4 native captures",
                result.SuccessMessage);
        });
    }

    [Fact]
    public void Screenshot_verification_rejects_stale_duplicate_and_noncanonical_manifests()
    {
        WithTemporaryDirectory(root =>
        {
            WriteEvidenceFixture(root, FixedFingerprint);
            var manifestPath = Path.Combine(root, "docs", "images", "screenshots", "manifest.json");
            var manifest = File.ReadAllText(manifestPath, Encoding.UTF8);

            File.WriteAllText(
                manifestPath,
                manifest.Replace(FixedFingerprint, new string('b', 64), StringComparison.Ordinal),
                new UTF8Encoding(false));
            var stale = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(stale.Passed);
            Assert.Contains("stale", stale.Failures.Single(), StringComparison.Ordinal);

            File.WriteAllText(
                manifestPath,
                manifest.Replace(
                    "\"generator\":",
                    "\"generator\": \"duplicate\",\n  \"generator\":",
                    StringComparison.Ordinal),
                new UTF8Encoding(false));
            var duplicate = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(duplicate.Passed);
            Assert.Contains("duplicate JSON property", duplicate.Failures.Single(), StringComparison.Ordinal);

            File.WriteAllText(manifestPath, manifest.Replace("  ", "    ", StringComparison.Ordinal));
            var noncanonical = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(noncanonical.Passed);
            Assert.Contains("not canonical", noncanonical.Failures.Single(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Screenshot_verification_rejects_closed_set_hash_readme_and_png_failures()
    {
        WithTemporaryDirectory(root =>
        {
            WriteEvidenceFixture(root, FixedFingerprint);
            var screenshotDirectory = Path.Combine(root, "docs", "images", "screenshots");
            File.WriteAllText(Path.Combine(screenshotDirectory, "extra.txt"), "extra");
            var extra = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(extra.Passed);
            Assert.Contains("exactly four", extra.Failures.Single(), StringComparison.Ordinal);
            File.Delete(Path.Combine(screenshotDirectory, "extra.txt"));

            var excessiveFiles = Enumerable.Range(0, 17)
                .Select(index => Path.Combine(screenshotDirectory, $"extra-{index:D2}.txt"))
                .ToArray();
            foreach (var path in excessiveFiles)
            {
                File.WriteAllText(path, "extra");
            }

            var excessive = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(excessive.Passed);
            Assert.Contains("exceeds 16 entries", excessive.Failures.Single(), StringComparison.Ordinal);
            foreach (var path in excessiveFiles)
            {
                File.Delete(path);
            }

            File.WriteAllText(Path.Combine(root, "README.md"), "# Missing evidence\n");
            var readme = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(readme.Passed);
            Assert.Contains("README does not reference", readme.Failures.Single(), StringComparison.Ordinal);

            WriteReadme(root);
            var screenshot = Path.Combine(screenshotDirectory, "main-menu.png");
            File.WriteAllBytes(screenshot, [.. File.ReadAllBytes(screenshot), 0]);
            File.WriteAllText(
                Path.Combine(screenshotDirectory, "manifest.json"),
                ReadmeScreenshotCheck.RenderManifest(root, FixedFingerprint),
                new UTF8Encoding(false));
            var png = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(png.Passed);
            Assert.Contains("invalid README screenshot PNG", png.Failures.Single(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Screenshot_verification_rejects_metadata_and_record_schema_drift()
    {
        WithTemporaryDirectory(root =>
        {
            WriteEvidenceFixture(root, FixedFingerprint);
            var manifestPath = Path.Combine(root, "docs", "images", "screenshots", "manifest.json");
            var manifest = File.ReadAllText(manifestPath, Encoding.UTF8);

            File.WriteAllText(
                manifestPath,
                manifest.Replace("\"Main menu\"", "\"Wrong menu\"", StringComparison.Ordinal),
                new UTF8Encoding(false));
            var metadata = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(metadata.Passed);
            Assert.Contains("metadata mismatch", metadata.Failures.Single(), StringComparison.Ordinal);

            File.WriteAllText(
                manifestPath,
                manifest.Replace("\"height\": 720,", "\"height\": \"720\",", StringComparison.Ordinal),
                new UTF8Encoding(false));
            var type = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(type.Passed);
            Assert.Contains("positive integer", type.Failures.Single(), StringComparison.Ordinal);

            File.WriteAllText(
                manifestPath,
                manifest.Replace("\"state\": \"MENU\",", "", StringComparison.Ordinal),
                new UTF8Encoding(false));
            var field = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(field.Passed);
            Assert.Contains("fields do not match", field.Failures.Single(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Screenshot_manifest_rejects_closed_identity_shape_and_hash_errors()
    {
        WithTemporaryDirectory(root =>
        {
            WriteEvidenceFixture(root, FixedFingerprint);
            var manifestPath = Path.Combine(root, "docs", "images", "screenshots", "manifest.json");
            var manifest = File.ReadAllText(manifestPath, Encoding.UTF8);

            File.WriteAllText(
                manifestPath,
                manifest.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal),
                new UTF8Encoding(false));
            Assert.Contains(
                "unsupported screenshot manifest schema",
                ReadmeScreenshotCheck.Inspect(root, FixedFingerprint).Failures.Single(),
                StringComparison.Ordinal);

            File.WriteAllText(
                manifestPath,
                manifest.Replace(ReadmeScreenshotCheck.Generator, "wrong-generator", StringComparison.Ordinal),
                new UTF8Encoding(false));
            Assert.Contains(
                "generator is invalid",
                ReadmeScreenshotCheck.Inspect(root, FixedFingerprint).Failures.Single(),
                StringComparison.Ordinal);

            File.WriteAllText(
                manifestPath,
                manifest.Replace(FixedFingerprint, FixedFingerprint.ToUpperInvariant(), StringComparison.Ordinal),
                new UTF8Encoding(false));
            Assert.Contains(
                "sourceSha256 is invalid",
                ReadmeScreenshotCheck.Inspect(root, FixedFingerprint).Failures.Single(),
                StringComparison.Ordinal);

            File.WriteAllText(
                manifestPath,
                manifest.Replace("\"main-menu.png\"", "\"../main-menu.png\"", StringComparison.Ordinal),
                new UTF8Encoding(false));
            Assert.Contains(
                "unsafe or unexpected file name",
                ReadmeScreenshotCheck.Inspect(root, FixedFingerprint).Failures.Single(),
                StringComparison.Ordinal);

            File.WriteAllText(
                manifestPath,
                manifest.Replace(
                    "c81289bbd8b957ae504a756a591d36274febee81dc58b074d3c9324af46eb4a8",
                    new string('G', 64),
                    StringComparison.Ordinal),
                new UTF8Encoding(false));
            Assert.Contains(
                "screenshot hash is invalid",
                ReadmeScreenshotCheck.Inspect(root, FixedFingerprint).Failures.Single(),
                StringComparison.Ordinal);

            File.WriteAllText(manifestPath, "[]\n", new UTF8Encoding(false));
            Assert.Contains(
                "must be a JSON object",
                ReadmeScreenshotCheck.Inspect(root, FixedFingerprint).Failures.Single(),
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Source_fingerprint_normalizes_text_line_endings_and_hashes_binary_exactly()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFingerprintFixture(root);
            var textPath = Path.Combine(root, "game", "fixture.cs");
            var pngPath = Path.Combine(root, "game", "fixture.png");
            File.WriteAllText(textPath, "alpha\nbeta\n", new UTF8Encoding(false));
            File.WriteAllBytes(pngPath, [1, 13, 10, 2]);
            var lf = ReadmeScreenshotCheck.ComputeSourceFingerprint(root);

            File.WriteAllText(textPath, "alpha\r\nbeta\r", new UTF8Encoding(false));
            var normalized = ReadmeScreenshotCheck.ComputeSourceFingerprint(root);
            Assert.Equal(lf, normalized);

            File.WriteAllBytes(pngPath, [1, 10, 2]);
            var binary = ReadmeScreenshotCheck.ComputeSourceFingerprint(root);
            Assert.NotEqual(lf, binary);

            var bounded = Assert.Throws<InvalidDataException>(
                () => ReadmeScreenshotCheck.ComputeSourceFingerprint(root, maximumEntries: 1));
            Assert.Contains("exceeds 1 entries", bounded.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ReadmeScreenshotCheck.ComputeSourceFingerprint(root, maximumEntries: 0));
        });
    }

    [Fact]
    public void Native_capture_stages_validates_replaces_and_verifies_evidence()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFingerprintFixture(root);
            WriteReadme(root);
            var executable = Path.Combine(root, "godot.exe");
            File.WriteAllText(executable, "fixture");
            var process = new FakeScreenshotProcess(ResolveRepositoryRoot());

            var result = ReadmeScreenshotCheck.Capture(root, executable, process);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            Assert.Contains("visual review required", result.SuccessMessage, StringComparison.Ordinal);
            Assert.Equal(3, process.Calls.Count);
            Assert.Equal(Path.GetFullPath(executable), process.Calls[0].Executable);
            Assert.Equal(["--version"], process.Calls[0].Arguments);
            Assert.Equal(TimeSpan.FromSeconds(30), process.Calls[0].Timeout);
            Assert.Equal("dotnet", process.Calls[1].Executable);
            Assert.Equal(TimeSpan.FromSeconds(180), process.Calls[1].Timeout);
            Assert.Equal(Path.GetFullPath(executable), process.Calls[2].Executable);
            Assert.Equal(TimeSpan.FromSeconds(120), process.Calls[2].Timeout);
            Assert.Contains(
                process.Calls[2].Arguments,
                argument => argument.StartsWith("--readme-capture-dir=", StringComparison.Ordinal));
            Assert.True(ReadmeScreenshotCheck.Inspect(root).Passed);
        });
    }

    [Fact]
    public void Native_capture_fails_closed_on_build_capture_and_staged_output_errors()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFingerprintFixture(root);
            WriteReadme(root);
            var executable = Path.Combine(root, "godot.exe");
            File.WriteAllText(executable, "fixture");

            var buildFailure = new FakeScreenshotProcess(ResolveRepositoryRoot())
            {
                BuildResult = new ScreenshotProcessResult(1, "", "build failed"),
            };
            var build = ReadmeScreenshotCheck.Capture(root, executable, buildFailure);
            Assert.False(build.Passed);
            Assert.Contains("build failed", build.Failures.Single(), StringComparison.Ordinal);

            var identityFailure = new FakeScreenshotProcess(ResolveRepositoryRoot())
            {
                VersionResult = new ScreenshotProcessResult(0, "4.7.0.stable.mono.official.wrong\n", ""),
            };
            var identity = ReadmeScreenshotCheck.Capture(root, executable, identityFailure);
            Assert.False(identity.Passed);
            Assert.Contains("Godot toolchain mismatch", identity.Failures.Single(), StringComparison.Ordinal);

            var launchFailure = new FakeScreenshotProcess(ResolveRepositoryRoot())
            {
                RunException = new System.ComponentModel.Win32Exception("launch failed"),
            };
            var launch = ReadmeScreenshotCheck.Capture(root, executable, launchFailure);
            Assert.False(launch.Passed);
            Assert.Contains("launch failed", launch.Failures.Single(), StringComparison.Ordinal);

            var timeout = new FakeScreenshotProcess(ResolveRepositoryRoot())
            {
                CaptureResult = new ScreenshotProcessResult(-1, "", "", TimedOut: true),
            };
            var timedOut = ReadmeScreenshotCheck.Capture(root, executable, timeout);
            Assert.False(timedOut.Passed);
            Assert.Contains("timed out", timedOut.Failures.Single(), StringComparison.Ordinal);

            var extra = new FakeScreenshotProcess(ResolveRepositoryRoot()) { WriteExtraFile = true };
            var extraResult = ReadmeScreenshotCheck.Capture(root, executable, extra);
            Assert.False(extraResult.Passed);
            Assert.Contains("exactly the four", extraResult.Failures.Single(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Intermediate_directory_links_are_rejected_for_reads_fingerprints_and_capture_writes()
    {
        WithTemporaryDirectory(root =>
        {
            WriteEvidenceFixture(root, FixedFingerprint);
            var images = Path.Combine(root, "docs", "images");
            var actualImages = Path.Combine(root, "actual-images");
            Directory.Move(images, actualImages);
            if (!TryCreateDirectoryLink(images, actualImages))
            {
                return;
            }

            var inspection = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(inspection.Passed);
            Assert.Contains("link or reparse point", inspection.Failures.Single(), StringComparison.Ordinal);
        });

        WithTemporaryDirectory(root =>
        {
            WriteFingerprintFixture(root);
            var rules = Path.Combine(root, "native", "src", "VibeSnake.Rules");
            var actualRules = Path.Combine(root, "actual-rules");
            Directory.Move(rules, actualRules);
            if (!TryCreateDirectoryLink(rules, actualRules))
            {
                return;
            }

            var exception = Assert.Throws<InvalidDataException>(
                () => ReadmeScreenshotCheck.ComputeSourceFingerprint(root));
            Assert.Contains("link or reparse point", exception.Message, StringComparison.Ordinal);
        });

        WithTemporaryDirectory(root =>
        {
            WriteFingerprintFixture(root);
            WriteReadme(root);
            var actualImages = Path.Combine(root, "actual-images");
            Directory.CreateDirectory(actualImages);
            var images = Path.Combine(root, "docs", "images");
            Directory.CreateDirectory(Path.GetDirectoryName(images)!);
            if (!TryCreateDirectoryLink(images, actualImages))
            {
                return;
            }

            var executable = Path.Combine(root, "godot.exe");
            File.WriteAllText(executable, "fixture");
            var capture = ReadmeScreenshotCheck.Capture(
                root,
                executable,
                new FakeScreenshotProcess(ResolveRepositoryRoot()));
            Assert.False(capture.Passed);
            Assert.Contains("link or reparse point", capture.Failures.Single(), StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(actualImages));
        });
    }

    [Fact]
    public void System_process_timeout_is_bounded_and_terminates_the_process_tree()
    {
        var executable = OperatingSystem.IsWindows() ? "ping.exe" : "/bin/sh";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "127.0.0.1", "-n", "30", "-w", "1000" }
            : ["-c", "sleep 30"];
        var stopwatch = Stopwatch.StartNew();

        var result = new SystemScreenshotCaptureProcess().Run(
            executable,
            arguments,
            ResolveRepositoryRoot(),
            TimeSpan.FromMilliseconds(100));

        stopwatch.Stop();
        Assert.True(result.TimedOut);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), stopwatch.Elapsed.ToString());

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var pidPath = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-screenshot-child-" + Guid.NewGuid().ToString("N"));
        int? childPid = null;
        try
        {
            var escapedPidPath = pidPath.Replace("'", "'\\''", StringComparison.Ordinal);
            stopwatch.Restart();
            var inheritedPipe = new SystemScreenshotCaptureProcess().Run(
                "/bin/sh",
                ["-c", $"sleep 30 & echo $! > '{escapedPidPath}'"],
                ResolveRepositoryRoot(),
                TimeSpan.FromSeconds(5));
            stopwatch.Stop();

            Assert.Equal(0, inheritedPipe.ExitCode);
            Assert.False(inheritedPipe.TimedOut);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), stopwatch.Elapsed.ToString());
            childPid = int.Parse(
                File.ReadAllText(pidPath).Trim(),
                CultureInfo.InvariantCulture);
        }
        finally
        {
            if (childPid is { } processId)
            {
                try
                {
                    using var child = Process.GetProcessById(processId);
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit(5000);
                }
                catch (ArgumentException)
                {
                    // The child exited before test cleanup.
                }
            }

            if (File.Exists(pidPath))
            {
                File.Delete(pidPath);
            }
        }
    }

    [Fact]
    public void Native_capture_and_evidence_reject_executable_marker_dimension_and_hash_drift()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFingerprintFixture(root);
            WriteReadme(root);
            var missingExecutable = ReadmeScreenshotCheck.Capture(
                root,
                Path.Combine(root, "missing-godot"),
                new FakeScreenshotProcess(ResolveRepositoryRoot()));
            Assert.False(missingExecutable.Passed);
            Assert.Contains("does not exist", missingExecutable.Failures.Single(), StringComparison.Ordinal);

            var executable = Path.Combine(root, "godot.exe");
            File.WriteAllText(executable, "fixture");
            var markerFailure = ReadmeScreenshotCheck.Capture(
                root,
                executable,
                new FakeScreenshotProcess(ResolveRepositoryRoot())
                {
                    CaptureResult = new ScreenshotProcessResult(0, "capture complete", ""),
                });
            Assert.False(markerFailure.Passed);
            Assert.Contains("capture complete", markerFailure.Failures.Single(), StringComparison.Ordinal);
        });

        WithTemporaryDirectory(root =>
        {
            WriteEvidenceFixture(root, FixedFingerprint);
            var screenshotDirectory = Path.Combine(root, "docs", "images", "screenshots");
            File.Copy(
                Path.Combine(ResolveRepositoryRoot(), "assets", "images", "logo.png"),
                Path.Combine(screenshotDirectory, "main-menu.png"),
                overwrite: true);
            File.WriteAllText(
                Path.Combine(screenshotDirectory, "manifest.json"),
                ReadmeScreenshotCheck.RenderManifest(root, FixedFingerprint),
                new UTF8Encoding(false));
            var dimensions = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(dimensions.Passed);
            Assert.Contains("not 1280x720", dimensions.Failures.Single(), StringComparison.Ordinal);

            File.Copy(
                Path.Combine(
                    ResolveRepositoryRoot(),
                    "docs",
                    "images",
                    "screenshots",
                    "main-menu.png"),
                Path.Combine(screenshotDirectory, "main-menu.png"),
                overwrite: true);
            var manifestPath = Path.Combine(screenshotDirectory, "manifest.json");
            var manifest = ReadmeScreenshotCheck.RenderManifest(root, FixedFingerprint);
            File.WriteAllText(
                manifestPath,
                manifest.Replace(
                    "c81289bbd8b957ae504a756a591d36274febee81dc58b074d3c9324af46eb4a8",
                    new string('b', 64),
                    StringComparison.Ordinal),
                new UTF8Encoding(false));
            var hash = ReadmeScreenshotCheck.Inspect(root, FixedFingerprint);
            Assert.False(hash.Passed);
            Assert.Contains("hash changed", hash.Failures.Single(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Native_capture_rejects_malformed_toolchain_and_version_output()
    {
        WithTemporaryDirectory(root =>
        {
            WriteFingerprintFixture(root);
            WriteReadme(root);
            var executable = Path.Combine(root, "godot.exe");
            File.WriteAllText(executable, "fixture");
            foreach (var toolchain in InvalidToolchains)
            {
                WriteFile(root, "native/toolchain.json", toolchain);
                var result = ReadmeScreenshotCheck.Capture(
                    root,
                    executable,
                    new FakeScreenshotProcess(ResolveRepositoryRoot()));
                Assert.False(result.Passed);
            }

            WriteValidToolchain(root);
            var standardError = ReadmeScreenshotCheck.Capture(
                root,
                executable,
                new FakeScreenshotProcess(ResolveRepositoryRoot())
                {
                    VersionResult = new ScreenshotProcessResult(
                        0,
                        "4.7.1.stable.mono.official.a13da4feb\n",
                        "warning"),
                });
            Assert.False(standardError.Passed);
            Assert.Contains("toolchain mismatch", standardError.Failures.Single(), StringComparison.Ordinal);

            var multipleLines = ReadmeScreenshotCheck.Capture(
                root,
                executable,
                new FakeScreenshotProcess(ResolveRepositoryRoot())
                {
                    VersionResult = new ScreenshotProcessResult(
                        0,
                        "4.7.1.stable.mono.official.a13da4feb\nextra\n",
                        ""),
                });
            Assert.False(multipleLines.Passed);
            Assert.Contains("invalid output", multipleLines.Failures.Single(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Screenshot_command_validates_routes_and_argument_shapes()
    {
        WithTemporaryDirectory(root =>
        {
            WriteEvidenceFixture(root, FixedFingerprint);
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(
                1,
                RepositoryCheckCommand.Run(["screenshots", root], output, error));
            Assert.Contains("README screenshots check failed", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(["screenshots-write"], output, error));
            Assert.Contains("screenshots-write", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(["unknown"], output, error));
            Assert.Contains("screenshots", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static void WriteEvidenceFixture(string root, string fingerprint)
    {
        var screenshotDirectory = Path.Combine(root, "docs", "images", "screenshots");
        Directory.CreateDirectory(screenshotDirectory);
        foreach (var file in ScreenshotFiles())
        {
            File.Copy(
                Path.Combine(ResolveRepositoryRoot(), "docs", "images", "screenshots", file),
                Path.Combine(screenshotDirectory, file));
        }

        WriteReadme(root);
        File.WriteAllText(
            Path.Combine(screenshotDirectory, "manifest.json"),
            ReadmeScreenshotCheck.RenderManifest(root, fingerprint),
            new UTF8Encoding(false));
    }

    private static void WriteFingerprintFixture(string root)
    {
        foreach (var directory in new[]
        {
            "game",
            "native/src/VibeSnake.Rules",
            "native/src/VibeSnake.Persistence",
            "native/tools/RepositoryChecks",
            "config",
        })
        {
            Directory.CreateDirectory(Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar)));
        }

        WriteFile(root, "game/VibeSnake.Game.sln", "fixture\n");
        WriteFile(root, "native/src/VibeSnake.Rules/Rule.cs", "rule\n");
        WriteFile(root, "native/src/VibeSnake.Persistence/Store.cs", "store\n");
        WriteFile(root, "config/content_inventory.json", "{}\n");
        WriteValidToolchain(root);
        WriteFile(root, "native/tools/RepositoryChecks/ContentInventoryCheck.cs", "inventory\n");
        WriteFile(root, "native/tools/RepositoryChecks/ReadmeScreenshotCheck.cs", "screenshots\n");
        WriteFile(root, "native/tools/RepositoryChecks/PngHeaderReader.cs", "png\n");
    }

    private static void WriteReadme(string root)
    {
        WriteFile(
            root,
            "README.md",
            string.Join(
                "\n",
                ScreenshotFiles().Select(file => $"docs/images/screenshots/{file}"))
                + "\n");
    }

    private static void WriteValidToolchain(string root) =>
        WriteFile(
            root,
            "native/toolchain.json",
            "{\"godot\":{\"version\":\"4.7.1\",\"flavor\":\"dotnet\",\"commit\":\"a13da4feb\"}}\n");

    private static string[] ScreenshotFiles() =>
        ["main-menu.png", "powers-run.png", "customization.png", "ai-channel.png"];

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-screenshot-checks",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VERSION"))
                && Directory.Exists(Path.Combine(directory.FullName, "native")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private sealed record ScreenshotCall(
        string Executable,
        string[] Arguments,
        string WorkingDirectory,
        TimeSpan Timeout);

    private sealed class FakeScreenshotProcess(string sourceRoot) : IScreenshotCaptureProcess
    {
        public ScreenshotProcessResult VersionResult { get; init; } =
            new(0, "4.7.1.stable.mono.official.a13da4feb\n", "");

        public ScreenshotProcessResult BuildResult { get; init; } = new(0, "", "");

        public ScreenshotProcessResult CaptureResult { get; init; } =
            new(0, "VIBESNAKE_README_CAPTURE_OK count=4\n", "");

        public bool WriteExtraFile { get; init; }

        public Exception? RunException { get; init; }

        public List<ScreenshotCall> Calls { get; } = [];

        public ScreenshotProcessResult Run(
            string executable,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout)
        {
            if (RunException is not null)
            {
                throw RunException;
            }

            Calls.Add(new ScreenshotCall(executable, arguments.ToArray(), workingDirectory, timeout));
            if (Calls.Count == 1)
            {
                return VersionResult;
            }

            if (Calls.Count == 2)
            {
                return BuildResult;
            }

            if (CaptureResult.ExitCode == 0 && !CaptureResult.TimedOut)
            {
                var outputArgument = arguments.Single(argument => argument.StartsWith(
                    "--readme-capture-dir=",
                    StringComparison.Ordinal));
                var outputDirectory = outputArgument[(outputArgument.IndexOf('=') + 1)..];
                foreach (var file in ScreenshotFiles())
                {
                    File.Copy(
                        Path.Combine(sourceRoot, "docs", "images", "screenshots", file),
                        Path.Combine(outputDirectory, file));
                }

                if (WriteExtraFile)
                {
                    File.WriteAllText(Path.Combine(outputDirectory, "extra.txt"), "extra");
                }
            }

            return CaptureResult;
        }
    }
}
