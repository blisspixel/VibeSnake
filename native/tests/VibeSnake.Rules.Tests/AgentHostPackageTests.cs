using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.AgentViewer;

namespace VibeSnake.Rules.Tests;

[Collection(AgentHostIntegrationGroup.Name)]
public sealed class AgentHostPackageTests
{
    public const string HostRootEnvironmentVariable = "VIBESNAKE_AGENT_HOST_ROOT";

    [Fact]
    public void Packaged_host_root_selects_the_single_manifest_directory()
    {
        using var temporary = new IsolatedUserDataDirectory();
        var rid = Path.Combine(temporary.Path, "win-x64");
        Directory.CreateDirectory(rid);
        File.WriteAllText(Path.Combine(rid, "host-manifest.json"), "{}\n");

        Assert.Equal(rid, ResolvePackagedHostRoot(temporary.Path));
        Assert.Equal(rid, ResolvePackagedHostRoot(rid));
        Assert.ThrowsAny<Exception>(() => ResolvePackagedHostRoot(Path.Combine(temporary.Path, "missing")));
    }

    [Fact]
    public async Task Packaged_self_contained_host_opens_a_live_viewer_without_writing_into_the_package()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(HostRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return;
        }

        var packageRoot = ResolvePackagedHostRoot(configuredRoot);
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(packageRoot, "host-manifest.json")));
        Assert.Equal(
            "vibesnake-agent-host-package-v1",
            manifest.RootElement.GetProperty("schema").GetString());
        Assert.True(manifest.RootElement.GetProperty("self_contained").GetBoolean());
        Assert.False(manifest.RootElement.GetProperty("publication_eligible").GetBoolean());
        Assert.Equal("unsigned", manifest.RootElement.GetProperty("signing").GetString());
        var executableName = Assert.IsType<string>(
            manifest.RootElement.GetProperty("executable").GetString());
        var executablePath = Path.Combine(packageRoot, executableName);
        Assert.True(File.Exists(executablePath), $"Packaged host is missing: {executablePath}");

        using var userData = new IsolatedUserDataDirectory();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var transportOptions = new StdioClientTransportOptions
        {
            Name = "Vibe Snake Packaged Host Viewer Qualification",
            Command = executablePath,
            WorkingDirectory = packageRoot,
            InheritEnvironmentVariables = true,
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };
        transportOptions.EnvironmentVariables ??= new Dictionary<string, string?>();
        transportOptions.EnvironmentVariables[AgentHostDataPaths.UserDataRootEnvironmentVariable] =
            userData.Path;
        var transport = new StdioClientTransport(transportOptions);
        await using var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ProtocolVersion = Program.McpProtocolVersion,
                InitializationTimeout = TimeSpan.FromSeconds(45),
                DiscoverProbeTimeout = TimeSpan.FromSeconds(30),
                ClientInfo = new Implementation
                {
                    Name = "vibesnake-agent-host-package-tests",
                    Version = Program.HostVersion,
                },
            },
            cancellationToken: timeout.Token);

        Assert.Equal(Program.HostName, client.ServerInfo.Name);
        Assert.Equal(Program.HostVersion, client.ServerInfo.Version);

        var started = await client.CallToolAsync(
            "start_match",
            new Dictionary<string, object?>
            {
                ["modeId"] = "classic",
                ["seedVisibility"] = "open",
                ["gameplaySeed"] = "456",
                ["maximumSteps"] = 2,
                ["watchEnabled"] = true,
                ["actionProfile"] = AgentPassportV4.FourDirectionBurstActionProfile,
            },
            cancellationToken: timeout.Token);
        var startDiagnostic = string.Join(
            " | ",
            started.Content.OfType<TextContentBlock>().Select(content => content.Text));
        Assert.False(started.IsError ?? false, startDiagnostic);
        var startedJson = Assert.IsType<JsonElement>(started.StructuredContent);
        var handle = startedJson.GetProperty("match_handle").GetString()!;
        var observation = startedJson.GetProperty("observation");
        var viewer = startedJson.GetProperty("viewer");
        using var viewerClient = new AgentViewerClient(
            viewer.GetProperty("pipe_name").GetString()!,
            viewer.GetProperty("access_token").GetString()!);
        var initial = await TakeViewerFrameAsync(viewerClient, 0);
        Assert.Equal(AgentViewerFrameV9.Contract, initial.Schema);
        Assert.Equal(AgentViewerOperationKind.Initial, initial.Operation);
        Assert.Equal(0, initial.StepsAdvanced);

        var burst = await client.CallToolAsync(
            "play_burst",
            new Dictionary<string, object?>
            {
                ["matchHandle"] = handle,
                ["idempotencyKey"] = "package-viewer-burst",
                ["expectedTick"] = observation.GetProperty("tick").GetInt32(),
                ["expectedStateHash"] = observation.GetProperty("state_hash").GetString(),
                ["initialAction"] = "up",
                ["maximumSteps"] = 2,
            },
            cancellationToken: timeout.Token);
        var burstDiagnostic = string.Join(
            " | ",
            burst.Content.OfType<TextContentBlock>().Select(content => content.Text));
        Assert.False(burst.IsError ?? false, burstDiagnostic);
        _ = await client.CallToolAsync(
            "finish_match",
            new Dictionary<string, object?> { ["matchHandle"] = handle },
            cancellationToken: timeout.Token);
        var advanced = await TakeViewerFrameAsync(viewerClient, 1);
        Assert.True(advanced.Sequence >= 1, advanced.Operation.ToString());
        Assert.True(
            advanced.Operation is AgentViewerOperationKind.Burst or AgentViewerOperationKind.Finish);
        Assert.True(advanced.StepsAdvanced > 0 || advanced.VerifiedResultAvailable);

        AssertPackageHasNoPreviewData(packageRoot);
        Assert.False(Directory.Exists(Path.Combine(userData.Path, "agent_arena")));
    }

    [Fact]
    public async Task Godot_watch_screen_receives_packaged_host_frame_when_qualified()
    {
        var godotExecutable = Environment.GetEnvironmentVariable(
            AgentViewerClientTests.GodotExecutableEnvironmentVariable);
        var configuredRoot = Environment.GetEnvironmentVariable(HostRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(godotExecutable)
            || string.IsNullOrWhiteSpace(configuredRoot))
        {
            return;
        }

        Assert.True(File.Exists(godotExecutable), "Configured Godot executable is missing.");
        var packageRoot = ResolvePackagedHostRoot(configuredRoot);
        var executablePath = ResolvePackagedHostExecutable(packageRoot);
        using var userData = new IsolatedUserDataDirectory();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var transportOptions = new StdioClientTransportOptions
        {
            Name = "Vibe Snake Packaged Host Godot Qualification",
            Command = executablePath,
            WorkingDirectory = packageRoot,
            InheritEnvironmentVariables = true,
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };
        transportOptions.EnvironmentVariables ??= new Dictionary<string, string?>();
        transportOptions.EnvironmentVariables[AgentHostDataPaths.UserDataRootEnvironmentVariable] =
            userData.Path;
        var transport = new StdioClientTransport(transportOptions);
        await using var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ProtocolVersion = Program.McpProtocolVersion,
                InitializationTimeout = TimeSpan.FromSeconds(45),
                DiscoverProbeTimeout = TimeSpan.FromSeconds(30),
                ClientInfo = new Implementation
                {
                    Name = "vibesnake-agent-host-godot-package-tests",
                    Version = Program.HostVersion,
                },
            },
            cancellationToken: timeout.Token);

        var started = await client.CallToolAsync(
            "start_match",
            new Dictionary<string, object?>
            {
                ["modeId"] = "classic",
                ["seedVisibility"] = "open",
                ["gameplaySeed"] = "321",
                ["maximumSteps"] = 3,
                ["watchEnabled"] = true,
                ["actionProfile"] = AgentPassportV4.FourDirectionBurstActionProfile,
                ["passport"] = new Dictionary<string, object?>
                {
                    ["schema"] = AgentPassportV4.Contract,
                    ["agent_id"] = "godot-package-smoke-agent",
                    ["policy_version"] = "policy-1",
                    ["display_name"] = "Godot Package Smoke Agent",
                    ["avatar_id"] = "redline",
                    ["accent_id"] = "signal-cyan",
                    ["station_id"] = "global_coil",
                    ["observation_profile"] = AgentPassportV4.SymbolicStepObservationProfile,
                    ["action_profile"] = AgentPassportV4.FourDirectionBurstActionProfile,
                },
            },
            cancellationToken: timeout.Token);
        var startDiagnostic = string.Join(
            " | ",
            started.Content.OfType<TextContentBlock>().Select(content => content.Text));
        Assert.False(started.IsError ?? false, startDiagnostic);
        var startedJson = Assert.IsType<JsonElement>(started.StructuredContent);
        var handle = startedJson.GetProperty("match_handle").GetString()!;
        var observation = startedJson.GetProperty("observation");
        var viewer = startedJson.GetProperty("viewer");
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var godotUserData = Path.Combine(userData.Path, "godot-user-data");
        Directory.CreateDirectory(godotUserData);
        var startInfo = new ProcessStartInfo(godotExecutable)
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--verbose");
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "game"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--agent-watch-pipe=" + viewer.GetProperty("pipe_name").GetString());
        startInfo.ArgumentList.Add("--agent-watch-token=" + viewer.GetProperty("access_token").GetString());
        startInfo.ArgumentList.Add("--agent-watch-smoke");
        startInfo.ArgumentList.Add("--smoke-user-data-root=" + godotUserData);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Godot packaged-host viewer smoke did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            var burst = await client.CallToolAsync(
                "play_burst",
                new Dictionary<string, object?>
                {
                    ["matchHandle"] = handle,
                    ["idempotencyKey"] = "godot-package-terminal-burst",
                    ["expectedTick"] = observation.GetProperty("tick").GetInt32(),
                    ["expectedStateHash"] = observation.GetProperty("state_hash").GetString(),
                    ["initialAction"] = "up",
                    ["maximumSteps"] = 3,
                    ["declaredIntent"] = "take_risk",
                },
                cancellationToken: timeout.Token);
            var burstDiagnostic = string.Join(
                " | ",
                burst.Content.OfType<TextContentBlock>().Select(content => content.Text));
            Assert.False(burst.IsError ?? false, burstDiagnostic);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Godot packaged-host viewer smoke timed out.");
        }

        var output = (await standardOutput) + (await standardError);
        Assert.True(process.ExitCode == 0, output);
        Assert.True(
            output.Contains("VIBESNAKE_AGENT_VIEWER_SMOKE_OK", StringComparison.Ordinal),
            output);
        Assert.True(
            output.Contains("operation=Burst steps=3", StringComparison.Ordinal),
            output);
        Assert.True(output.Contains("coalesced=", StringComparison.Ordinal), output);
        Assert.True(output.Contains("motion=snap", StringComparison.Ordinal), output);
        Assert.True(
            output.Contains(
                "accessibility=muted,high-contrast,reduced-motion,text-150",
                StringComparison.Ordinal),
            output);
        Assert.True(output.Contains("avatar=redline", StringComparison.Ordinal), output);
        Assert.True(!output.Contains("ERROR:", StringComparison.Ordinal), output);
        Assert.True(!output.Contains("WARNING:", StringComparison.Ordinal), output);
        AssertPackageHasNoPreviewData(packageRoot);
    }

    internal static string ResolvePackagedHostRoot(string configuredRoot)
    {
        var root = Path.GetFullPath(configuredRoot);
        if (File.Exists(Path.Combine(root, "host-manifest.json")))
        {
            return root;
        }

        Assert.True(Directory.Exists(root), $"Packaged host root is missing: {root}");
        var packages = Directory.GetDirectories(root)
            .Where(candidate => File.Exists(Path.Combine(candidate, "host-manifest.json")))
            .ToArray();
        Assert.True(
            packages.Length == 1,
            $"VIBESNAKE_AGENT_HOST_ROOT must contain exactly one host package: {root}");
        return packages[0];
    }

    private static string ResolvePackagedHostExecutable(string packageRoot)
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(packageRoot, "host-manifest.json")));
        var executableName = Assert.IsType<string>(
            manifest.RootElement.GetProperty("executable").GetString());
        var executablePath = Path.Combine(packageRoot, executableName);
        Assert.True(File.Exists(executablePath), $"Packaged host is missing: {executablePath}");
        return executablePath;
    }

    private static async Task<AgentViewerFrameV9> TakeViewerFrameAsync(
        AgentViewerClient client,
        long minimumSequence)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (client.TryTakeLatest(out var frame, out _)
                && frame is not null
                && frame.Sequence >= minimumSequence)
            {
                return frame;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException(
            $"Packaged Agent Host viewer did not publish sequence {minimumSequence}: {client.Status}");
    }

    private static void AssertPackageHasNoPreviewData(string packageRoot)
    {
        string[] forbidden =
        [
            "agent_arena",
            "preferences.json",
            "agent_passports.json",
            "exhibition_archive.json",
        ];
        foreach (var name in forbidden)
        {
            Assert.False(
                Directory.Exists(Path.Combine(packageRoot, name))
                    || File.Exists(Path.Combine(packageRoot, name)),
                $"{name} must not appear inside the host package.");
        }
    }

    private sealed class IsolatedUserDataDirectory : IDisposable
    {
        public IsolatedUserDataDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentHostPackageTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
