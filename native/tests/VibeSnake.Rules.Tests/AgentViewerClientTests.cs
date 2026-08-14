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
        Assert.Equal("AWAITING AGENT ACTION; RULES PAUSED", client.Status);
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
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            "321",
            maximumSteps: 3,
            watchEnabled: true,
            passport: new AgentPassportV2(
                AgentPassportV2.Contract,
                "godot-smoke-agent",
                "policy-1",
                "Godot Smoke Agent",
                "redline",
                "signal-cyan",
                "global_coil",
                actionProfile: AgentPassportV2.FourDirectionBurstActionProfile),
            actionProfile: AgentPassportV2.FourDirectionBurstActionProfile);
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
        var burst = registry.PlayBurst(
            started.MatchHandle,
            "godot-terminal-burst",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Up,
            maximumSteps: 3,
            declaredIntent: AgentPublicIntent.TakeRisk);
        Assert.Equal(3, burst.StepsAdvanced);
        Assert.Equal(AgentBurstStopReason.MatchStepLimit, burst.StopReason);
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

        Assert.False(client.TryTakeLatest(out var frame, out var coalescedFrames));
        Assert.Null(frame);
        Assert.Equal(0, coalescedFrames);
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
        await WaitForStateAsync(malformed, AgentViewerClientState.Rejected);

        var oversizedPipe = CreateTestPipeName();
        var oversizedServer = ServePayloadAsync(
            oversizedPipe,
            new string('x', AgentViewerClient.MaximumFrameBytes + 1));
        using var oversized = new AgentViewerClient(oversizedPipe, "dG9rZW4");
        await oversizedServer;
        await WaitForStateAsync(oversized, AgentViewerClientState.Rejected);
    }

    [Fact]
    public async Task Viewer_client_rejects_invalid_contracts_and_reports_clean_disconnect()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "invalid-frame",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            3UL,
            AgentSeedVisibility.Open));
        var observation = session.Observe();
        var rejectedObservation = session.SubmitAction(new AgentActionRequest(
            "rejected",
            observation.Tick,
            observation.StateHash,
            AgentAction.Left)).Observation;
        var advancedObservation = session.SubmitAction(new AgentActionRequest(
            "advanced",
            observation.Tick,
            observation.StateHash,
            AgentAction.Up)).Observation;
        var wrappedObservation = advancedObservation with
        {
            PreviousEvents =
            [
                new AgentPublicEventV1(
                    RunEventKind.Wrapped,
                    Position: null,
                    NewDirection: null,
                    Value: null,
                    Cause: null,
                    Power: null),
            ],
        };
        var validFrame = new AgentViewerFrameV5(
            AgentViewerFrameV5.Contract,
            0,
            AgentViewerOperationKind.Initial,
            StartTick: observation.Tick,
            StartStateHash: observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
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
        var rejectedStepFrame = validFrame with
        {
            Sequence = 1,
            Operation = AgentViewerOperationKind.Step,
            Observation = rejectedObservation,
        };
        var advancedStepFrame = validFrame with
        {
            Sequence = 1,
            Operation = AgentViewerOperationKind.Step,
            StepsAdvanced = 1,
            Observation = advancedObservation,
        };
        var acceptedAction = Assert.IsType<AgentPreviousActionV1>(
            advancedObservation.PreviousAction);
        var rejectedAction = Assert.IsType<AgentPreviousActionV1>(
            rejectedObservation.PreviousAction);
        string[] invalidPayloads =
        [
            "null\n",
            SerializeFrame(validFrame with { Schema = "wrong" }),
            SerializeFrame(validFrame with { Sequence = -1 }),
            SerializeFrame(validFrame with { StartTick = -1 }),
            SerializeFrame(validFrame with { StartStateHash = "" }),
            SerializeFrame(validFrame with { StartStateHash = "0000000000000000" }),
            SerializeFrame(validFrame with
            {
                Operation = (AgentViewerOperationKind)byte.MaxValue,
            }),
            SerializeFrame(validFrame with { StepsAdvanced = 1 }),
            SerializeFrame(validFrame with { StepsAdvanced = -1 }),
            SerializeFrame(validFrame with
            {
                BurstStopReason = AgentBurstStopReason.RequestedLimit,
            }),
            SerializeFrame(validFrame with
            {
                BurstStopReason = (AgentBurstStopReason)byte.MaxValue,
            }),
            SerializeFrame(validFrame with
            {
                BurstStopEvent = RunEventKind.Wrapped,
            }),
            SerializeFrame(validFrame with
            {
                BurstStopEvent = RunEventKind.Moved,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Observation = advancedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Step,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Step,
                StepsAdvanced = 2,
                Observation = advancedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Step,
                StepsAdvanced = 1,
                BurstStopReason = AgentBurstStopReason.RequestedLimit,
                Observation = advancedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Step,
                StepsAdvanced = 1,
                BurstStopEvent = RunEventKind.Wrapped,
                Observation = advancedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Step,
                Observation = advancedObservation,
            }),
            SerializeFrame(rejectedStepFrame with
            {
                Observation = rejectedObservation with
                {
                    PreviousAction = rejectedAction with
                    {
                        Action = (AgentAction)byte.MaxValue,
                    },
                },
            }),
            SerializeFrame(rejectedStepFrame with
            {
                Observation = rejectedObservation with
                {
                    PreviousAction = rejectedAction with
                    {
                        Rejection = (AgentActionRejection)byte.MaxValue,
                    },
                },
            }),
            SerializeFrame(rejectedStepFrame with
            {
                Observation = rejectedObservation with
                {
                    PreviousAction = rejectedAction with
                    {
                        DeclaredIntent = (AgentPublicIntent)byte.MaxValue,
                    },
                },
            }),
            SerializeFrame(advancedStepFrame with
            {
                Observation = advancedObservation with
                {
                    PreviousAction = acceptedAction with
                    {
                        Rejection = AgentActionRejection.StaleTick,
                    },
                },
            }),
            SerializeFrame(advancedStepFrame with
            {
                Observation = advancedObservation with
                {
                    PreviousAction = acceptedAction with { RulesAdvanced = false },
                },
            }),
            SerializeFrame(rejectedStepFrame with
            {
                Observation = rejectedObservation with
                {
                    PreviousAction = rejectedAction with
                    {
                        Rejection = AgentActionRejection.None,
                    },
                },
            }),
            SerializeFrame(advancedStepFrame with
            {
                Observation = advancedObservation with
                {
                    PreviousAction = rejectedAction with
                    {
                        RulesAdvanced = true,
                    },
                },
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                StepsAdvanced = AgentBurstRequest.MaximumBurstSteps + 1,
                BurstStopReason = AgentBurstStopReason.RequestedLimit,
                Observation = advancedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                StepsAdvanced = AgentBurstRequest.MaximumBurstSteps,
                BurstStopReason = AgentBurstStopReason.RequestedLimit,
                Observation = advancedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                StepsAdvanced = 1,
                Observation = advancedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                BurstStopEvent = RunEventKind.Wrapped,
                Observation = rejectedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                StepsAdvanced = 1,
                BurstStopReason = AgentBurstStopReason.RequestedLimit,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                StepsAdvanced = 1,
                BurstStopReason = AgentBurstStopReason.RequestedLimit,
                Observation = rejectedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                StepsAdvanced = 1,
                BurstStopReason = AgentBurstStopReason.RequestedLimit,
                BurstStopEvent = RunEventKind.Wrapped,
                Observation = wrappedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                StepsAdvanced = 1,
                BurstStopReason = AgentBurstStopReason.ReplayFailure,
                BurstStopEvent = RunEventKind.Wrapped,
                Observation = wrappedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                StepsAdvanced = 1,
                BurstStopReason = AgentBurstStopReason.DecisionEvent,
                Observation = wrappedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Burst,
                StepsAdvanced = 1,
                BurstStopReason = AgentBurstStopReason.DecisionEvent,
                BurstStopEvent = RunEventKind.AteFood,
                Observation = wrappedObservation,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Finish,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Finish,
                StepsAdvanced = 1,
                EndReason = AgentMatchEndReason.AgentFinished,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Finish,
                BurstStopReason = AgentBurstStopReason.RequestedLimit,
                EndReason = AgentMatchEndReason.AgentFinished,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Finish,
                BurstStopEvent = RunEventKind.Wrapped,
                EndReason = AgentMatchEndReason.AgentFinished,
            }),
            SerializeFrame(validFrame with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Finish,
                EndReason = AgentMatchEndReason.StepLimit,
            }),
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
            SerializeFrame(rejectedStepFrame with
            {
                Observation = rejectedObservation with { Status = RunStatus.Dead },
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
            SerializeFrame(validFrame) + SerializeFrame(advancedStepFrame with
            {
                StartStateHash = "0000000000000000",
            }),
            SerializeFrame(validFrame) + SerializeFrame(rejectedStepFrame with
            {
                StartTick = 1,
                Observation = rejectedObservation with
                {
                    Tick = 1,
                    StepsRemaining = rejectedObservation.StepsRemaining - 1,
                },
            }),
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
    public async Task Viewer_client_rejects_malformed_observation_fields()
    {
        var observation = new AgentMatchSession(new AgentMatchOptions(
            "invalid-observation",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            9UL,
            AgentSeedVisibility.Open)).Observe();
        var validFrame = new AgentViewerFrameV5(
            AgentViewerFrameV5.Contract,
            0,
            AgentViewerOperationKind.Initial,
            observation.Tick,
            observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false);
        var validEvent = new AgentPublicEventV1(
            RunEventKind.Wrapped,
            Position: null,
            NewDirection: Direction.Up,
            Value: null,
            Cause: DeathCause.None,
            Power: PowerKind.Shield);
        AgentObservationV3[] invalidObservations =
        [
            observation with { MatchId = "" },
            observation with { RulesetId = "other-rules" },
            observation with { RulesVersion = observation.RulesVersion + 1 },
            observation with { ModeId = "other-mode" },
            observation with { ModeVersion = observation.ModeVersion + 1 },
            observation with { ConfigHashAlgorithm = "other-algorithm" },
            observation with { ConfigHash = "x" },
            observation with { Passport = null! },
            observation with { SeedVisibility = (AgentSeedVisibility)byte.MaxValue },
            observation with { GameplaySeed = null },
            observation with { SeedVisibility = AgentSeedVisibility.Blind },
            observation with { Tick = -1 },
            observation with { MaximumSteps = 0, StepsRemaining = 0 },
            observation with
            {
                Tick = observation.MaximumSteps + 1,
                StepsRemaining = 0,
            },
            observation with { StepsRemaining = -1 },
            observation with { StepsRemaining = observation.StepsRemaining - 1 },
            observation with { StateHash = "x" },
            observation with { BoardWidth = 0 },
            observation with { BoardHeight = 0 },
            observation with { Body = null! },
            observation with { Body = [] },
            observation with { PendingDirections = null! },
            observation with { PendingDirections = [(Direction)byte.MaxValue] },
            observation with { PreviousEvents = null! },
            observation with { PreviousEvents = [null!] },
            observation with
            {
                PreviousEvents = [validEvent with { Kind = (RunEventKind)byte.MaxValue }],
            },
            observation with
            {
                PreviousEvents = [validEvent with { NewDirection = (Direction)byte.MaxValue }],
            },
            observation with
            {
                PreviousEvents = [validEvent with { Cause = (DeathCause)byte.MaxValue }],
            },
            observation with
            {
                PreviousEvents = [validEvent with { Power = (PowerKind)byte.MaxValue }],
            },
            observation with { DetachedObstacles = null! },
            observation with { Status = (RunStatus)byte.MaxValue },
            observation with { DeathCause = (DeathCause)byte.MaxValue },
            observation with { Direction = (Direction)byte.MaxValue },
            observation with
            {
                AdaptiveDifficultyState = (AdaptiveDifficultyState)byte.MaxValue,
            },
            observation with { AdaptivePolicyId = "" },
            observation with { Lifecycle = (AgentMatchLifecycle)byte.MaxValue },
        ];

        foreach (var invalidObservation in invalidObservations)
        {
            var pipeName = CreateTestPipeName();
            var server = ServePayloadAsync(
                pipeName,
                SerializeFrame(validFrame with { Observation = invalidObservation }));
            using var client = new AgentViewerClient(pipeName, "dG9rZW4");

            await server;
            await WaitForStateAsync(client, AgentViewerClientState.Rejected);
        }
    }

    [Fact]
    public async Task Viewer_client_rejects_unknown_or_downlevel_passport_identity()
    {
        var observation = new AgentMatchSession(new AgentMatchOptions(
            "invalid-passport",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            9UL,
            AgentSeedVisibility.Open)).Observe();
        var validFrame = new AgentViewerFrameV5(
            AgentViewerFrameV5.Contract,
            0,
            AgentViewerOperationKind.Initial,
            observation.Tick,
            observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false);
        var validPayload = SerializeFrame(validFrame);
        string[] invalidPayloads =
        [
            validPayload.Replace(
                AgentPassportV2.Contract,
                "vibesnake-agent-passport-v1",
                StringComparison.Ordinal),
            validPayload.Replace(
                $"\"avatar_id\":\"{observation.Passport.AvatarId}\"",
                "\"avatar_id\":\"unknown\"",
                StringComparison.Ordinal),
            validPayload.Replace(
                $"\"accent_id\":\"{observation.Passport.AccentId}\"",
                "\"accent_id\":\"unknown\"",
                StringComparison.Ordinal),
            validPayload.Replace(
                $"\"station_id\":\"{observation.Passport.StationId}\"",
                "\"station_id\":\"unknown\"",
                StringComparison.Ordinal),
        ];

        Assert.All(invalidPayloads, payload => Assert.NotEqual(validPayload, payload));
        foreach (var payload in invalidPayloads)
        {
            var pipeName = CreateTestPipeName();
            var server = ServePayloadAsync(pipeName, payload);
            using var client = new AgentViewerClient(pipeName, "dG9rZW4");

            await server;
            await WaitForStateAsync(client, AgentViewerClientState.Rejected);
        }
    }

    [Fact]
    public async Task Viewer_client_accepts_zero_step_burst_truth_before_and_after_terminal()
    {
        using var temporary = new TemporaryDirectory();
        using var registry = new AgentSessionRegistry(
            new ReplayStore(temporary.Path),
            () => "match_burst_rejection_viewer",
            () => 10UL);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            "10",
            maximumSteps: 1,
            watchEnabled: true,
            actionProfile: AgentPassportV2.FourDirectionBurstActionProfile);
        var connection = Assert.IsType<AgentViewerConnectionV1>(started.Viewer);
        using var client = new AgentViewerClient(connection.PipeName, connection.AccessToken);
        _ = await TakeFrameAsync(client);

        var stale = registry.PlayBurst(
            started.MatchHandle,
            "stale-viewer-burst",
            started.Observation.Tick + 1,
            started.Observation.StateHash,
            AgentAction.Up,
            maximumSteps: 1);
        var staleFrame = await TakeFrameAsync(client, minimumSequence: 1);
        Assert.Equal(AgentActionRejection.StaleTick, stale.Rejection);
        Assert.Equal(AgentViewerOperationKind.Burst, staleFrame.Operation);
        Assert.Equal(0, staleFrame.StepsAdvanced);
        Assert.Equal(staleFrame.StartTick, staleFrame.Observation.Tick);
        Assert.Equal(staleFrame.StartStateHash, staleFrame.Observation.StateHash);

        var terminal = registry.PlayBurst(
            started.MatchHandle,
            "terminal-viewer-burst",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Up,
            maximumSteps: 1);
        _ = await TakeFrameAsync(client, minimumSequence: 2);
        var after = registry.PlayBurst(
            started.MatchHandle,
            "after-terminal-viewer-burst",
            terminal.Observation.Tick,
            terminal.Observation.StateHash,
            AgentAction.Continue,
            maximumSteps: 1);
        var afterFrame = await TakeFrameAsync(client, minimumSequence: 3);

        Assert.Equal(AgentActionRejection.MatchNotAwaitingAction, after.Rejection);
        Assert.Equal(AgentViewerOperationKind.Burst, afterFrame.Operation);
        Assert.Equal(0, afterFrame.StepsAdvanced);
        Assert.Equal(AgentMatchEndReason.StepLimit, afterFrame.EndReason);
        Assert.True(afterFrame.VerifiedResultAvailable);
    }

    [Fact]
    public async Task Viewer_client_rejects_immutable_identity_changes()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "identity-frame",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            3UL,
            AgentSeedVisibility.Open));
        var initial = session.Observe();
        var rejected = session.SubmitAction(new AgentActionRequest(
            "rejected-identity-frame",
            initial.Tick,
            initial.StateHash,
            AgentAction.Left)).Observation;
        var first = new AgentViewerFrameV5(
            AgentViewerFrameV5.Contract,
            0,
            AgentViewerOperationKind.Initial,
            initial.Tick,
            initial.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            initial,
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false);
        var second = first with
        {
            Sequence = 1,
            Operation = AgentViewerOperationKind.Step,
            Observation = rejected,
        };
        AgentPassportV2 AlternatePassport(
            string? agentId = null,
            string? avatarId = null,
            string? accentId = null,
            string? stationId = null) =>
            new(
                AgentPassportV2.Contract,
                agentId ?? initial.Passport.AgentId,
                initial.Passport.PolicyVersion,
                initial.Passport.DisplayName,
                avatarId ?? initial.Passport.AvatarId,
                accentId ?? initial.Passport.AccentId,
                stationId ?? initial.Passport.StationId,
                initial.Passport.ObservationProfile,
                initial.Passport.ActionProfile);
        AgentObservationV3[] changedIdentities =
        [
            rejected with { MatchId = "other-match" },
            rejected with { RulesetId = "other-rules" },
            rejected with { RulesVersion = rejected.RulesVersion + 1 },
            rejected with { ModeId = RunModeCatalog.VibeId },
            rejected with { ModeVersion = rejected.ModeVersion + 1 },
            rejected with { ConfigHashAlgorithm = "other-config-hash" },
            rejected with { ConfigHash = "other-config" },
            rejected with { SeedVisibility = AgentSeedVisibility.Blind },
            rejected with { GameplaySeed = rejected.GameplaySeed + 1 },
            rejected with { Passport = AlternatePassport(agentId: "other-agent") },
            rejected with { Passport = AlternatePassport(avatarId: "redline") },
            rejected with { Passport = AlternatePassport(accentId: "coil-gold") },
            rejected with { Passport = AlternatePassport(stationId: "the_pit") },
            rejected with { MaximumSteps = rejected.MaximumSteps + 1 },
            rejected with { BoardWidth = rejected.BoardWidth + 1 },
            rejected with { BoardHeight = rejected.BoardHeight + 1 },
            rejected with { WrapsAtEdges = !rejected.WrapsAtEdges },
        ];

        foreach (var changed in changedIdentities)
        {
            var pipeName = CreateTestPipeName();
            var server = ServePayloadAsync(
                pipeName,
                SerializeFrame(first) + SerializeFrame(second with { Observation = changed }));
            using var client = new AgentViewerClient(pipeName, "dG9rZW4");

            await server;
            await WaitForStateAsync(client, AgentViewerClientState.Rejected);

            Assert.False(client.TryTakeLatest(out _, out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Viewer_client_clears_pending_frame_after_invalid_followup(bool oversized)
    {
        var observation = new AgentMatchSession(new AgentMatchOptions(
            "invalid-followup",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            4UL,
            AgentSeedVisibility.Open)).Observe();
        var frame = new AgentViewerFrameV5(
            AgentViewerFrameV5.Contract,
            0,
            AgentViewerOperationKind.Initial,
            observation.Tick,
            observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false);
        var invalid = oversized
            ? new string('x', AgentViewerClient.MaximumFrameBytes + 1)
            : "not-json\n";
        var pipeName = CreateTestPipeName();
        var server = ServePayloadAsync(pipeName, SerializeFrame(frame) + invalid);
        using var client = new AgentViewerClient(pipeName, "dG9rZW4");

        await server;
        await WaitForStateAsync(client, AgentViewerClientState.Rejected);

        Assert.False(client.TryTakeLatest(out var pending, out var coalescedFrames));
        Assert.Null(pending);
        Assert.Equal(0, coalescedFrames);
    }

    [Fact]
    public async Task Viewer_client_reports_each_verified_and_failed_terminal_outcome()
    {
        var completedSession = new AgentMatchSession(new AgentMatchOptions(
            "terminal-frames",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            5UL,
            AgentSeedVisibility.Open,
            maximumSteps: 1));
        var completedStart = completedSession.Observe();
        var completed = completedSession.SubmitAction(new AgentActionRequest(
            "complete",
            completedStart.Tick,
            completedStart.StateHash,
            AgentAction.Continue)).Observation;
        var abortedSession = new AgentMatchSession(new AgentMatchOptions(
            "aborted-frame",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            6UL,
            AgentSeedVisibility.Open));
        var abortedStart = abortedSession.Observe();
        _ = abortedSession.Finish();
        var aborted = abortedSession.Observe();
        var failed = abortedStart with
        {
            Lifecycle = AgentMatchLifecycle.FailedClosed,
            IsActionAwaited = false,
        };
        (AgentViewerFrameV5 Frame, AgentViewerClientState State, string Status)[] cases =
        [
            (new AgentViewerFrameV5(
                AgentViewerFrameV5.Contract,
                1,
                AgentViewerOperationKind.Step,
                StartTick: completedStart.Tick,
                StartStateHash: completedStart.StateHash,
                StepsAdvanced: 1,
                BurstStopReason: null,
                BurstStopEvent: null,
                completed,
                AgentMatchEndReason.StepLimit,
                VerifiedResultAvailable: true),
                AgentViewerClientState.Completed,
                "STEP LIMIT"),
            (new AgentViewerFrameV5(
                AgentViewerFrameV5.Contract,
                1,
                AgentViewerOperationKind.Finish,
                StartTick: abortedStart.Tick,
                StartStateHash: abortedStart.StateHash,
                StepsAdvanced: 0,
                BurstStopReason: null,
                BurstStopEvent: null,
                aborted,
                AgentMatchEndReason.AgentFinished,
                VerifiedResultAvailable: true),
                AgentViewerClientState.Completed,
                "AGENT FINISHED MATCH"),
            (new AgentViewerFrameV5(
                AgentViewerFrameV5.Contract,
                1,
                AgentViewerOperationKind.Finish,
                StartTick: failed.Tick,
                StartStateHash: failed.StateHash,
                StepsAdvanced: 0,
                BurstStopReason: null,
                BurstStopEvent: null,
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
    public async Task Viewer_client_reports_latest_frame_coalescing_from_source_sequences()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "coalesced-frames",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            5UL,
            AgentSeedVisibility.Open,
            maximumSteps: 10));
        var initial = session.Observe();
        var first = session.SubmitAction(new AgentActionRequest(
            "first",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up));
        var second = session.SubmitAction(new AgentActionRequest(
            "second",
            first.Observation.Tick,
            first.Observation.StateHash,
            AgentAction.Right));
        AgentViewerFrameV5[] frames =
        [
            new(
                AgentViewerFrameV5.Contract,
                0,
                AgentViewerOperationKind.Initial,
                StartTick: initial.Tick,
                StartStateHash: initial.StateHash,
                StepsAdvanced: 0,
                BurstStopReason: null,
                BurstStopEvent: null,
                initial,
                AgentMatchEndReason.None,
                VerifiedResultAvailable: false),
            new(
                AgentViewerFrameV5.Contract,
                1,
                AgentViewerOperationKind.Step,
                StartTick: initial.Tick,
                StartStateHash: initial.StateHash,
                StepsAdvanced: 1,
                BurstStopReason: null,
                BurstStopEvent: null,
                first.Observation,
                AgentMatchEndReason.None,
                VerifiedResultAvailable: false),
            new(
                AgentViewerFrameV5.Contract,
                2,
                AgentViewerOperationKind.Step,
                StartTick: first.Observation.Tick,
                StartStateHash: first.Observation.StateHash,
                StepsAdvanced: 1,
                BurstStopReason: null,
                BurstStopEvent: null,
                second.Observation,
                AgentMatchEndReason.None,
                VerifiedResultAvailable: false),
        ];
        var pipeName = CreateTestPipeName();
        var payload = string.Concat(frames.Select(SerializeFrame));
        var server = ServePayloadAsync(pipeName, payload);
        using var client = new AgentViewerClient(pipeName, "dG9rZW4");

        await server;
        await WaitForStateAsync(client, AgentViewerClientState.Disconnected);

        Assert.True(client.TryTakeLatest(out var latest, out var coalescedFrames));
        Assert.Equal(frames[^1].Observation.StateHash, latest!.Observation.StateHash);
        Assert.Equal(frames[^1].Observation.Tick, latest.Observation.Tick);
        Assert.Equal(2, latest.Sequence);
        Assert.Equal(2, coalescedFrames);
        Assert.False(client.TryTakeLatest(out _, out var emptyCoalescedFrames));
        Assert.Equal(0, emptyCoalescedFrames);

        var sourceGapPipe = CreateTestPipeName();
        var sourceGapServer = ServePayloadAsync(
            sourceGapPipe,
            SerializeFrame(frames[0]) + SerializeFrame(frames[2]));
        using var sourceGapClient = new AgentViewerClient(sourceGapPipe, "dG9rZW4");

        await sourceGapServer;
        await WaitForStateAsync(sourceGapClient, AgentViewerClientState.Disconnected);

        Assert.True(sourceGapClient.TryTakeLatest(
            out var sourceGapLatest,
            out var sourceGapCount));
        Assert.Equal(2, sourceGapLatest!.Sequence);
        Assert.Equal(2, sourceGapCount);
    }

    [Fact]
    public async Task Viewer_client_accepts_closed_burst_metadata()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "burst-frame",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            123UL,
            AgentSeedVisibility.Open,
            maximumSteps: 10,
            actionProfile: AgentPassportV2.FourDirectionBurstActionProfile));
        var initial = session.Observe();
        var burst = session.SubmitBurst(new AgentBurstRequest(
            "burst",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up,
            maximumSteps: 2));
        Assert.Equal(2, burst.StepsAdvanced);
        var frame = new AgentViewerFrameV5(
            AgentViewerFrameV5.Contract,
            1,
            AgentViewerOperationKind.Burst,
            initial.Tick,
            initial.StateHash,
            burst.StepsAdvanced,
            burst.StopReason,
            burst.StopEvent,
            burst.Observation,
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false);
        var pipeName = CreateTestPipeName();
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServePayloadAsync(pipeName, SerializeFrame(frame), release.Task);
        try
        {
            using var client = new AgentViewerClient(pipeName, "dG9rZW4");

            var received = await TakeFrameAsync(client, minimumSequence: 1);

            Assert.Equal(AgentViewerOperationKind.Burst, received.Operation);
            Assert.Equal(2, received.StepsAdvanced);
            Assert.Equal(AgentBurstStopReason.RequestedLimit, received.BurstStopReason);
            Assert.Null(received.BurstStopEvent);
        }
        finally
        {
            release.TrySetResult(true);
            await server;
        }

        var decisionEvent = new AgentPublicEventV1(
            RunEventKind.Wrapped,
            Position: null,
            NewDirection: null,
            Value: null,
            Cause: null,
            Power: null);
        var decisionFrame = frame with
        {
            BurstStopReason = AgentBurstStopReason.DecisionEvent,
            BurstStopEvent = RunEventKind.Wrapped,
            Observation = burst.Observation with { PreviousEvents = [decisionEvent] },
        };
        var decisionPipe = CreateTestPipeName();
        var decisionRelease = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var decisionServer = ServePayloadAsync(
            decisionPipe,
            SerializeFrame(decisionFrame),
            decisionRelease.Task);
        try
        {
            using var client = new AgentViewerClient(decisionPipe, "dG9rZW4");

            var received = await TakeFrameAsync(client, minimumSequence: 1);

            Assert.Equal(AgentBurstStopReason.DecisionEvent, received.BurstStopReason);
            Assert.Equal(RunEventKind.Wrapped, received.BurstStopEvent);
        }
        finally
        {
            decisionRelease.TrySetResult(true);
            await decisionServer;
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
        Assert.Throws<ArgumentException>(() => new AgentViewerClient("valid", "bad!"));
        using var validTokenCharacters = new AgentViewerClient("valid-pipe_2", "token_2-");
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

    private static string SerializeFrame(AgentViewerFrameV5 frame) =>
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

    private static async Task<AgentViewerFrameV5> TakeFrameAsync(
        AgentViewerClient client,
        long minimumSequence = 0)
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
