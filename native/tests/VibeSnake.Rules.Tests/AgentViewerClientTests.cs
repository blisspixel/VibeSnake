using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.AgentViewer;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

[Collection(AgentHostIntegrationGroup.Name)]
public sealed class AgentViewerClientTests
{
    public const string GodotExecutableEnvironmentVariable =
        "VIBESNAKE_AGENT_VIEWER_GODOT_EXECUTABLE";

    private static readonly JsonSerializerOptions ViewerJsonOptions = CreateViewerJsonOptions();

    [Fact]
    public async Task Viewer_client_connects_projects_and_reaches_completed_state()
    {
        using var temporary = new TemporaryDirectory();
        using var registry = new AgentSessionRegistry(
            new ReplayStore(temporary.Path),
            () => "match_viewer",
            () => 123UL);
        var started = registry.StartMatch(
            RunModeCatalog.VibeId,
            AgentSeedVisibility.Open,
            "123",
            maximumSteps: 1,
            styleContractId: AgentStyleContractCatalog.StillwaterId,
            rivalPersonalityId: "optimal",
            watchEnabled: true);
        var connection = Assert.IsType<AgentViewerConnectionV1>(started.Viewer);
        using var client = new AgentViewerClient(connection.PipeName, connection.AccessToken);

        var initial = await TakeFrameAsync(client);
        var initialSnapshot = AgentViewerPresentation.ProjectSnapshot(initial.Observation);
        Assert.Equal(AgentViewerClientState.Watching, client.State);
        Assert.Equal("WATCHING AGENT LIVE", client.Status);
        Assert.Equal(started.Observation.StateHash, initialSnapshot.StateHash);
        Assert.Equal(started.Observation.Head.X, initialSnapshot.Head.X);
        Assert.NotNull(initial.Observation.Rival);

        _ = registry.PlayMove(
            started.MatchHandle,
            "viewer-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Up,
            AgentPublicIntent.SeekPower);
        var completed = await TakeFrameAsync(client, minimumSequence: 1);
        var completedSnapshot = AgentViewerPresentation.ProjectSnapshot(completed.Observation);
        await WaitForStateAsync(client, AgentViewerClientState.Completed);

        Assert.Equal(1, completedSnapshot.Tick);
        Assert.Equal(AgentMatchEndReason.StepLimit, completed.EndReason);
        Assert.True(completed.VerifiedResultAvailable);
        Assert.Equal(
            AgentPublicIntent.SeekPower,
            completed.Observation.PreviousAction!.DeclaredIntent);
        Assert.Equal(AgentViewerClientState.Completed, client.State);
        Assert.Contains("VERIFIED REPLAY", client.Status, StringComparison.Ordinal);
        client.Dispose();
    }

    [Fact]
    public async Task Godot_watch_screen_receives_real_host_frame_when_qualified()
    {
        var godotExecutable = Environment.GetEnvironmentVariable(
            GodotExecutableEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(godotExecutable))
        {
            return;
        }

        Assert.True(File.Exists(godotExecutable), "Configured Godot executable is missing.");
        using var temporary = new TemporaryDirectory();
        using var registry = new AgentSessionRegistry(
            new ReplayStore(temporary.Path),
            () => "match_godot_viewer",
            () => 321UL);
        var started = registry.StartMatch(
            RunModeCatalog.VibeId,
            AgentSeedVisibility.Open,
            "321",
            maximumSteps: 1,
            styleContractId: AgentStyleContractCatalog.StillwaterId,
            rivalPersonalityId: "optimal",
            watchEnabled: true,
            passport: new AgentPassportV1(
                AgentPassportV1.Contract,
                "godot-smoke-agent",
                "policy-1",
                "Godot Smoke Agent",
                "#64FFFF",
                "agent-default",
                "open-frequency"));
        var connection = Assert.IsType<AgentViewerConnectionV1>(started.Viewer);
        var userDataRoot = System.IO.Path.Combine(temporary.Path, "godot-user-data");
        Directory.CreateDirectory(userDataRoot);
        var startInfo = new ProcessStartInfo(godotExecutable)
        {
            WorkingDirectory = ResolveRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--verbose");
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(System.IO.Path.Combine(ResolveRepositoryRoot(), "game"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--agent-watch-pipe=" + connection.PipeName);
        startInfo.ArgumentList.Add("--agent-watch-token=" + connection.AccessToken);
        startInfo.ArgumentList.Add("--agent-watch-smoke");
        startInfo.ArgumentList.Add("--smoke-user-data-root=" + userDataRoot);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Godot viewer smoke did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        _ = registry.PlayMove(
            started.MatchHandle,
            "godot-terminal-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Up,
            AgentPublicIntent.TakeRisk);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Godot agent viewer smoke timed out.");
        }

        var output = (await standardOutput) + (await standardError);
        Assert.True(process.ExitCode == 0, output);
        Assert.True(
            output.Contains("VIBESNAKE_AGENT_VIEWER_SMOKE_OK", StringComparison.Ordinal),
            output);
        Assert.True(!output.Contains("ERROR:", StringComparison.Ordinal), output);
        Assert.True(!output.Contains("WARNING:", StringComparison.Ordinal), output);
    }

    [Fact]
    public async Task Viewer_client_rejects_wrong_capability_without_affecting_match()
    {
        using var temporary = new TemporaryDirectory();
        using var registry = new AgentSessionRegistry(
            new ReplayStore(temporary.Path),
            () => "match_rejected",
            () => 123UL);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            maximumSteps: 2,
            watchEnabled: true);
        var connection = Assert.IsType<AgentViewerConnectionV1>(started.Viewer);
        using var client = new AgentViewerClient(connection.PipeName, "d3Jvbmc");

        await WaitForStateAsync(client, AgentViewerClientState.Rejected);

        Assert.False(client.TryTakeLatest(out var frame));
        Assert.Null(frame);
        Assert.Equal(0, registry.Observe(started.MatchHandle).Tick);
        Assert.Contains("REJECTED", client.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Viewer_client_rejects_malformed_and_oversized_frames()
    {
        var malformedPipe = CreateTestPipeName();
        var malformedServer = ServePayloadAsync(malformedPipe, "not-json\n");
        using var malformed = new AgentViewerClient(malformedPipe, "dG9rZW4");
        await malformedServer;
        await WaitForStateAsync(malformed, AgentViewerClientState.Disconnected);

        var oversizedPipe = CreateTestPipeName();
        var oversizedServer = ServePayloadAsync(
            oversizedPipe,
            new string('x', AgentViewerClient.MaximumFrameBytes + 1));
        using var oversized = new AgentViewerClient(oversizedPipe, "dG9rZW4");
        await oversizedServer;
        await WaitForStateAsync(oversized, AgentViewerClientState.Disconnected);
    }

    [Fact]
    public async Task Viewer_client_rejects_invalid_contracts_and_reports_clean_disconnect()
    {
        var observation = new AgentMatchSession(new AgentMatchOptions(
            "invalid-frame",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            3UL,
            AgentSeedVisibility.Open)).Observe();
        var validFrame = new AgentViewerFrameV2(
            AgentViewerFrameV2.Contract,
            0,
            observation,
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false);
        var completedObservation = observation with
        {
            Lifecycle = AgentMatchLifecycle.Completed,
            IsActionAwaited = false,
        };
        var failedObservation = observation with
        {
            Lifecycle = AgentMatchLifecycle.FailedClosed,
            IsActionAwaited = false,
        };
        string[] invalidPayloads =
        [
            "null\n",
            SerializeFrame(validFrame with { Schema = "wrong" }),
            SerializeFrame(validFrame with { Observation = null! }),
            SerializeFrame(validFrame with
            {
                Observation = observation with { Schema = "wrong" },
            }),
            SerializeFrame(validFrame with
            {
                EndReason = AgentMatchEndReason.StepLimit,
            }),
            SerializeFrame(validFrame with
            {
                VerifiedResultAvailable = true,
            }),
            SerializeFrame(validFrame with
            {
                Observation = completedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Observation = completedObservation,
                EndReason = AgentMatchEndReason.ReplayFailure,
            }),
            SerializeFrame(validFrame with
            {
                Observation = completedObservation,
                EndReason = AgentMatchEndReason.AgentFinished,
                VerifiedResultAvailable = true,
            }),
            SerializeFrame(validFrame with
            {
                Observation = failedObservation,
                EndReason = AgentMatchEndReason.StepLimit,
            }),
            SerializeFrame(validFrame with
            {
                Observation = failedObservation,
                EndReason = AgentMatchEndReason.ReplayFailure,
                VerifiedResultAvailable = true,
            }),
            SerializeFrame(validFrame) + SerializeFrame(validFrame),
        ];

        foreach (var payload in invalidPayloads)
        {
            var pipeName = CreateTestPipeName();
            var server = ServePayloadAsync(pipeName, payload);
            using var client = new AgentViewerClient(pipeName, "dG9rZW4");
            await server;
            await WaitForStateAsync(client, AgentViewerClientState.Rejected);
        }

        var disconnectPipe = CreateTestPipeName();
        var disconnectServer = ServePayloadAsync(disconnectPipe, SerializeFrame(validFrame));
        using var disconnected = new AgentViewerClient(disconnectPipe, "dG9rZW4");
        await disconnectServer;
        await WaitForStateAsync(disconnected, AgentViewerClientState.Disconnected);
        Assert.Contains("MATCH CONTROL REMAINS WITH HOST", disconnected.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Viewer_client_reports_each_verified_and_failed_terminal_outcome()
    {
        var observation = new AgentMatchSession(new AgentMatchOptions(
            "terminal-frames",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            5UL,
            AgentSeedVisibility.Open)).Observe();
        var completed = observation with
        {
            Lifecycle = AgentMatchLifecycle.Completed,
            IsActionAwaited = false,
        };
        var aborted = observation with
        {
            Lifecycle = AgentMatchLifecycle.Aborted,
            IsActionAwaited = false,
        };
        var failed = observation with
        {
            Lifecycle = AgentMatchLifecycle.FailedClosed,
            IsActionAwaited = false,
        };
        (AgentViewerFrameV2 Frame, AgentViewerClientState State, string Status)[] cases =
        [
            (new AgentViewerFrameV2(
                AgentViewerFrameV2.Contract,
                0,
                completed,
                AgentMatchEndReason.RulesTerminal,
                VerifiedResultAvailable: true),
                AgentViewerClientState.Completed,
                "ENDED BY RULES"),
            (new AgentViewerFrameV2(
                AgentViewerFrameV2.Contract,
                0,
                aborted,
                AgentMatchEndReason.AgentFinished,
                VerifiedResultAvailable: true),
                AgentViewerClientState.Completed,
                "AGENT FINISHED MATCH"),
            (new AgentViewerFrameV2(
                AgentViewerFrameV2.Contract,
                0,
                failed,
                AgentMatchEndReason.ReplayFailure,
                VerifiedResultAvailable: false),
                AgentViewerClientState.FailedClosed,
                "NO VERIFIED REPLAY"),
        ];

        foreach (var item in cases)
        {
            var pipeName = CreateTestPipeName();
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var server = ServePayloadAsync(pipeName, SerializeFrame(item.Frame), release.Task);
            try
            {
                using var client = new AgentViewerClient(pipeName, "dG9rZW4");
                _ = await TakeFrameAsync(client);
                await WaitForStateAsync(client, item.State);
                Assert.Contains(item.Status, client.Status, StringComparison.Ordinal);
            }
            finally
            {
                release.TrySetResult(true);
                await server;
            }
        }
    }

    [Fact]
    public void Viewer_validates_tokens_and_observation_shape()
    {
        Assert.ThrowsAny<ArgumentException>(() => new AgentViewerClient(" ", "token"));
        Assert.Throws<ArgumentException>(() => new AgentViewerClient("bad pipe", "token"));
        Assert.Throws<ArgumentException>(() => new AgentViewerClient(
            new string('a', AgentViewerTransport.MaximumPipeNameLength + 1),
            "token"));
        Assert.Throws<ArgumentException>(() => new AgentViewerClient(
            "valid",
            new string('a', 129)));
        using var validTokenCharacters = new AgentViewerClient("valid-pipe_2", "token_2");
        Assert.Throws<ArgumentNullException>(() => AgentViewerPresentation.ProjectSnapshot(null!));

        var observation = new AgentMatchSession(new AgentMatchOptions(
            "projection",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Open)).Observe();
        Assert.Throws<ArgumentException>(() => AgentViewerPresentation.ProjectSnapshot(
            observation with { Schema = "wrong" }));
        Assert.Throws<ArgumentException>(() => AgentViewerPresentation.ProjectSnapshot(
            observation with { Body = [] }));
        Assert.Throws<ArgumentException>(() => AgentViewerPresentation.ProjectSnapshot(
            observation with { Body = null! }));
        Assert.Throws<ArgumentException>(() => AgentViewerPresentation.ProjectSnapshot(
            observation with { PendingDirections = null! }));
        Assert.Throws<ArgumentException>(() => AgentViewerPresentation.ProjectSnapshot(
            observation with { DetachedObstacles = null! }));
        Assert.Throws<ArgumentException>(() => AgentViewerPresentation.ProjectSnapshot(
            observation with { BoardWidth = 0 }));
        Assert.Throws<ArgumentException>(() => AgentViewerPresentation.ProjectSnapshot(
            observation with { BoardHeight = 0 }));
        Assert.Throws<ArgumentException>(() => AgentViewerPresentation.ProjectSnapshot(
            observation with { StateHash = "" }));

        var projected = AgentViewerPresentation.ProjectSnapshot(observation with
        {
            Food = null,
            PowerPickup = new AgentPowerPickupV1(
                PowerKind.Shield,
                new AgentPointV1(4, 5),
                6),
            BaitPosition = new AgentPointV1(7, 8),
        });
        Assert.Null(projected.Food);
        Assert.Equal(new GridPoint(4, 5), projected.PowerPickup?.Position);
        Assert.Equal(new GridPoint(7, 8), projected.BaitPosition);
    }

    private static string SerializeFrame(AgentViewerFrameV2 frame) =>
        JsonSerializer.Serialize(frame, ViewerJsonOptions) + "\n";

    private static string CreateTestPipeName() =>
        "t_" + Guid.NewGuid().ToString("N")[..16];

    private static JsonSerializerOptions CreateViewerJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private static async Task<AgentViewerFrameV2> TakeFrameAsync(
        AgentViewerClient client,
        long minimumSequence = 0)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (client.TryTakeLatest(out var frame)
                && frame is not null
                && frame.Sequence >= minimumSequence)
            {
                return frame;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The viewer did not receive the expected frame.");
    }

    private static async Task WaitForStateAsync(
        AgentViewerClient client,
        AgentViewerClientState expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (client.State == expected)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException(
            $"Viewer state {client.State} did not reach {expected}: {client.Status}");
    }

    private static async Task ServePayloadAsync(
        string pipeName,
        string payload,
        Task? holdOpen = null)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync();
        var single = new byte[1];
        do
        {
            if (await server.ReadAsync(single) == 0)
            {
                return;
            }
        }
        while (single[0] != (byte)'\n');

        try
        {
            await server.WriteAsync(Encoding.UTF8.GetBytes(payload));
            await server.FlushAsync();
            if (holdOpen is not null)
            {
                await holdOpen;
            }
        }
        catch (IOException)
        {
            // Oversized-frame rejection may close the pipe before the producer drains.
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(
                    directory.FullName,
                    "game",
                    "project.godot")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentViewerTests",
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
