using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.AgentViewer;
using VibeSnake.Persistence;
using VibeSnake.Rules;

using static VibeSnake.Rules.Tests.AgentSurvivalTestFacts;

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
        var outcome = Assert.IsType<AgentStyleOutcomeV3>(completed.StyleOutcome);
        Assert.Equal(AgentStyleOutcomeV3.Contract, outcome.Schema);
        Assert.Equal(completed.Observation.StyleContract!.Criteria, outcome.Criteria);
        Assert.Equal(
            registry.GetResult(started.MatchHandle).Result!.ReplayPayloadHash,
            outcome.ReplayPayloadHash);
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
            passport: new AgentPassportV4(
                AgentPassportV4.Contract,
                "godot-smoke-agent",
                "policy-1",
                "Godot Smoke Agent",
                "redline",
                "signal-cyan",
                "global_coil",
                actionProfile: AgentPassportV4.FourDirectionBurstActionProfile),
            actionProfile: AgentPassportV4.FourDirectionBurstActionProfile);
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
        var validFrame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            0,
            AgentViewerOperationKind.Initial,
            StartTick: observation.Tick,
            StartStateHash: observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            SurvivalFor(observation),
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
        var validFrame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            0,
            AgentViewerOperationKind.Initial,
            observation.Tick,
            observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            SurvivalFor(observation),
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false);
        var validEvent = new AgentPublicEventV1(
            RunEventKind.Wrapped,
            Position: null,
            NewDirection: Direction.Up,
            Value: null,
            Cause: DeathCause.None,
            Power: PowerKind.Shield);
        AgentObservationV5[] invalidObservations =
        [
            observation with { MatchId = "" },
            observation with { RulesetId = "other-rules" },
            observation with { RulesVersion = observation.RulesVersion + 1 },
            observation with { ModeId = "other-mode" },
            observation with { ModeVersion = observation.ModeVersion + 1 },
            observation with { ConfigHashAlgorithm = "other-algorithm" },
            observation with { ConfigHash = "x" },
            observation with { ConfigHash = new string('0', 64) },
            observation with { Passport = null! },
            observation with { SeedVisibility = (AgentSeedVisibility)byte.MaxValue },
            observation with { GameplaySeed = null },
            observation with { SeedVisibility = AgentSeedVisibility.Blind },
            observation with { Tick = -1 },
            observation with { MaximumSteps = 0, StepsRemaining = 0 },
            observation with
            {
                MaximumSteps = AgentMatchOptions.MaximumAllowedSteps + 1,
                StepsRemaining = AgentMatchOptions.MaximumAllowedSteps + 1,
            },
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
            observation with { WrapsAtEdges = false },
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
            observation with
            {
                AdaptivePolicyId = AdaptiveDifficultyPolicy.CurrentPolicyId,
            },
            observation with { AdaptationEnabled = true },
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
    public async Task Viewer_client_rejects_invalid_style_catalog_identity_and_arithmetic()
    {
        var observation = new AgentMatchSession(new AgentMatchOptions(
            "invalid-style",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            9UL,
            AgentSeedVisibility.Open,
            styleContractId: AgentStyleContractCatalog.StillwaterId)).Observe();
        var progress = Assert.IsType<AgentStyleProgressV3>(observation.StyleContract);
        var count = progress.Criteria[0];
        var rate = progress.Criteria[1];
        var vibeConfig = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);
        var crownProgress = new AgentStyleEvidenceTracker(
            AgentStyleContractCatalog.CrownchaserId,
            RunModeCatalog.VibeId,
            vibeConfig,
            SnakeRun.Create(10UL, vibeConfig).GetSnapshot()).Snapshot();
        AgentStyleProgressV3[] invalidProgress =
        [
            progress with { Schema = "vibesnake-agent-style-progress-v1" },
            progress with { ContractId = "unknown" },
            progress with { DisplayName = "Other" },
            progress with { EvaluationPolicyId = "other-policy" },
            progress with { Criteria = null! },
            progress with { Criteria = [count] },
            progress with { Criteria = [rate, count] },
            progress with { Criteria = [null!, rate] },
            progress with { Criteria = [count with { CriterionId = "other" }, rate] },
            progress with { Criteria = [count with { DisplayName = "Other" }, rate] },
            progress with
            {
                Criteria =
                [
                    count with { Comparator = (AgentStyleCriterionComparator)byte.MaxValue },
                    rate,
                ],
            },
            progress with
            {
                Criteria = [count with { Unit = AgentStyleCriterionUnit.BasisPoints }, rate],
            },
            progress with { Criteria = [count with { Target = count.Target + 1 }, rate] },
            progress with { Criteria = [count with { Current = -1 }, rate] },
            progress with { Criteria = [count with { Numerator = 0 }, rate] },
            progress with { Criteria = [count with { ThresholdReached = true }, rate] },
            progress with { Criteria = [count, rate with { Numerator = -1 }] },
            progress with { Criteria = [count, rate with { Numerator = 1, Denominator = 0 }] },
            progress with { Criteria = [count, rate with { Numerator = 2, Denominator = 1 }] },
            progress with
            {
                Criteria = [count, rate with { Numerator = 1, Denominator = 2, Current = 4_999 }],
            },
            progress with { ThresholdsReached = 1 },
            progress with { AllThresholdsReached = true },
            crownProgress,
        ];
        var validFrame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            0,
            AgentViewerOperationKind.Initial,
            observation.Tick,
            observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            SurvivalFor(observation),
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false,
            StyleOutcome: null);

        foreach (var invalid in invalidProgress)
        {
            var pipeName = CreateTestPipeName();
            var server = ServePayloadAsync(
                pipeName,
                SerializeFrame(validFrame with
                {
                    Observation = observation with { StyleContract = invalid },
                }));
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
        var validFrame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            0,
            AgentViewerOperationKind.Initial,
            observation.Tick,
            observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            SurvivalFor(observation),
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false);
        var validPayload = SerializeFrame(validFrame);
        string[] invalidPayloads =
        [
            validPayload.Replace(
                AgentPassportV4.Contract,
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
    public async Task Viewer_client_rejects_unknown_fields_and_mixed_contract_generations()
    {
        var observation = new AgentMatchSession(new AgentMatchOptions(
            "strict-style-frame",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            9UL,
            AgentSeedVisibility.Open,
            styleContractId: AgentStyleContractCatalog.StillwaterId)).Observe();
        var validFrame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            0,
            AgentViewerOperationKind.Initial,
            observation.Tick,
            observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            SurvivalFor(observation),
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false,
            StyleOutcome: null);
        var validPayload = SerializeFrame(validFrame);
        string[] invalidPayloads =
        [
            "{\"unknown_frame_member\":true," + validPayload[1..],
            validPayload.Replace(
                "\"observation\":{",
                "\"observation\":{\"unknown_observation_member\":true,",
                StringComparison.Ordinal),
            validPayload.Replace(
                "\"style_contract\":{",
                "\"style_contract\":{\"unknown_style_member\":true,",
                StringComparison.Ordinal),
            validPayload.Replace(
                "\"criteria\":[{",
                "\"criteria\":[{\"unknown_criterion_member\":true,",
                StringComparison.Ordinal),
            validPayload.Replace(
                AgentViewerFrameV9.Contract,
                "vibesnake-agent-viewer-frame-v5",
                StringComparison.Ordinal),
            validPayload.Replace(
                AgentObservationV5.Contract,
                "vibesnake-agent-observation-v3",
                StringComparison.Ordinal),
            validPayload.Replace(
                AgentPassportV4.Contract,
                "vibesnake-agent-passport-v2",
                StringComparison.Ordinal),
            validPayload.Replace(
                AgentStyleProgressV3.Contract,
                "vibesnake-agent-style-progress-v1",
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
            Assert.False(client.TryTakeLatest(out _, out _));
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
            actionProfile: AgentPassportV4.FourDirectionBurstActionProfile);
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

        var wrongProfile = registry.PlayMove(
            started.MatchHandle,
            "after-terminal-wrong-profile",
            terminal.Observation.Tick,
            terminal.Observation.StateHash,
            AgentAction.Continue);
        var wrongProfileFrame = await TakeFrameAsync(client, minimumSequence: 4);
        Assert.Equal(AgentActionRejection.WrongActionProfile, wrongProfile.Rejection);
        Assert.Equal(0, wrongProfileFrame.StepsAdvanced);
        Assert.Equal(AgentMatchEndReason.StepLimit, wrongProfileFrame.EndReason);

        var conflict = registry.PlayBurst(
            started.MatchHandle,
            "terminal-viewer-burst",
            terminal.Observation.Tick,
            terminal.Observation.StateHash,
            AgentAction.Left,
            maximumSteps: 1);
        var conflictFrame = await TakeFrameAsync(client, minimumSequence: 5);
        Assert.Equal(AgentActionRejection.IdempotencyConflict, conflict.Rejection);
        Assert.Equal(0, conflictFrame.StepsAdvanced);
        Assert.Equal(AgentMatchEndReason.StepLimit, conflictFrame.EndReason);
    }

    [Fact]
    public async Task Viewer_client_rejects_immutable_identity_changes()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "identity-frame",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            3UL,
            AgentSeedVisibility.Open,
            styleContractId: AgentStyleContractCatalog.StillwaterId));
        var initial = session.Observe();
        var rejected = session.SubmitAction(new AgentActionRequest(
            "rejected-identity-frame",
            initial.Tick,
            initial.StateHash,
            AgentAction.Left)).Observation;
        var first = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            0,
            AgentViewerOperationKind.Initial,
            initial.Tick,
            initial.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            initial,
            SurvivalFor(initial),
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false);
        var second = first with
        {
            Sequence = 1,
            Operation = AgentViewerOperationKind.Step,
            Observation = rejected,
        };
        AgentPassportV4 AlternatePassport(
            string? agentId = null,
            string? avatarId = null,
            string? accentId = null,
            string? stationId = null) =>
            new(
                AgentPassportV4.Contract,
                agentId ?? initial.Passport.AgentId,
                initial.Passport.PolicyVersion,
                initial.Passport.DisplayName,
                avatarId ?? initial.Passport.AvatarId,
                accentId ?? initial.Passport.AccentId,
                stationId ?? initial.Passport.StationId,
                initial.Passport.ObservationProfile,
                initial.Passport.ActionProfile);
        var classicConfig = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);
        var redlineProgress = new AgentStyleEvidenceTracker(
            AgentStyleContractCatalog.RedlineId,
            RunModeCatalog.ClassicId,
            classicConfig,
            SnakeRun.Create(11UL, classicConfig).GetSnapshot()).Snapshot();
        AgentObservationV5[] changedIdentities =
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
            rejected with { StyleContract = null },
            rejected with { StyleContract = redlineProgress },
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
        var frame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            0,
            AgentViewerOperationKind.Initial,
            observation.Tick,
            observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            SurvivalFor(observation),
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
        (AgentViewerFrameV9 Frame, AgentViewerClientState State, string Status)[] cases =
        [
            (new AgentViewerFrameV9(
                AgentViewerFrameV9.Contract,
                1,
                AgentViewerOperationKind.Step,
                StartTick: completedStart.Tick,
                StartStateHash: completedStart.StateHash,
                StepsAdvanced: 1,
                BurstStopReason: null,
                BurstStopEvent: null,
                completed,
                SurvivalFor(completed),
                AgentMatchEndReason.StepLimit,
                VerifiedResultAvailable: true,
                VerifiedReplayPayloadHash: new string('a', 64)),
                AgentViewerClientState.Completed,
                "STEP LIMIT"),
            (new AgentViewerFrameV9(
                AgentViewerFrameV9.Contract,
                1,
                AgentViewerOperationKind.Finish,
                StartTick: abortedStart.Tick,
                StartStateHash: abortedStart.StateHash,
                StepsAdvanced: 0,
                BurstStopReason: null,
                BurstStopEvent: null,
                aborted,
                SurvivalFor(aborted),
                AgentMatchEndReason.AgentFinished,
                VerifiedResultAvailable: true,
                VerifiedReplayPayloadHash: new string('b', 64)),
                AgentViewerClientState.Completed,
                "AGENT FINISHED MATCH"),
            (new AgentViewerFrameV9(
                AgentViewerFrameV9.Contract,
                1,
                AgentViewerOperationKind.Finish,
                StartTick: failed.Tick,
                StartStateHash: failed.StateHash,
                StepsAdvanced: 0,
                BurstStopReason: null,
                BurstStopEvent: null,
                failed,
                SurvivalFor(failed),
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
        AgentViewerFrameV9[] frames =
        [
            new(
                AgentViewerFrameV9.Contract,
                0,
                AgentViewerOperationKind.Initial,
                StartTick: initial.Tick,
                StartStateHash: initial.StateHash,
                StepsAdvanced: 0,
                BurstStopReason: null,
                BurstStopEvent: null,
                initial,
                SurvivalFor(initial),
                AgentMatchEndReason.None,
                VerifiedResultAvailable: false),
            new(
                AgentViewerFrameV9.Contract,
                1,
                AgentViewerOperationKind.Step,
                StartTick: initial.Tick,
                StartStateHash: initial.StateHash,
                StepsAdvanced: 1,
                BurstStopReason: null,
                BurstStopEvent: null,
                first.Observation,
                SurvivalFor(first.Observation),
                AgentMatchEndReason.None,
                VerifiedResultAvailable: false),
            new(
                AgentViewerFrameV9.Contract,
                2,
                AgentViewerOperationKind.Step,
                StartTick: first.Observation.Tick,
                StartStateHash: first.Observation.StateHash,
                StepsAdvanced: 1,
                BurstStopReason: null,
                BurstStopEvent: null,
                second.Observation,
                SurvivalFor(second.Observation),
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
            actionProfile: AgentPassportV4.FourDirectionBurstActionProfile));
        var initial = session.Observe();
        var burst = session.SubmitBurst(new AgentBurstRequest(
            "burst",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up,
            maximumSteps: 2));
        Assert.Equal(2, burst.StepsAdvanced);
        var frame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            1,
            AgentViewerOperationKind.Burst,
            initial.Tick,
            initial.StateHash,
            burst.StepsAdvanced,
            burst.StopReason,
            burst.StopEvent,
            burst.Observation,
            SurvivalFor(burst.Observation),
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

    [Fact]
    public async Task Viewer_client_cross_checks_style_progress_against_observation_facts()
    {
        AgentObservationV5 StyleObservation(string styleId, string modeId, ulong seed) =>
            new AgentMatchSession(new AgentMatchOptions(
                $"strict-{styleId}",
                modeId,
                RunModeCatalog.CurrentModeVersion,
                seed,
                AgentSeedVisibility.Open,
                styleContractId: styleId)).Observe();

        AgentStyleProgressV3 ReplaceCriterion(
            AgentStyleProgressV3 progress,
            int index,
            AgentStyleCriterionProgressV3 criterion,
            int criteriaSatisfied = 0) =>
            progress with
            {
                Criteria = index == 0
                    ? [criterion, progress.Criteria[1]]
                    : [progress.Criteria[0], criterion],
                ThresholdsReached = criteriaSatisfied,
                AllThresholdsReached = false,
            };

        var stillwater = StyleObservation(
            AgentStyleContractCatalog.StillwaterId,
            RunModeCatalog.ClassicId,
            31UL);
        var stillwaterProgress = stillwater.StyleContract!;
        var lessonDefinition = AgentSignalSchoolCatalog.Get("first-turn");
        var lesson = new AgentMatchSession(new AgentMatchOptions(
            "strict-lesson-source",
            lessonDefinition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            lessonDefinition.PracticeSeed,
            AgentSeedVisibility.Open,
            lessonDefinition.MaximumSteps,
            lessonId: "first-turn")).Observe().LessonProgress;
        var crown = StyleObservation(
            AgentStyleContractCatalog.CrownchaserId,
            RunModeCatalog.VibeId,
            32UL);
        var crownProgress = crown.StyleContract!;
        var edge = StyleObservation(
            AgentStyleContractCatalog.EdgeProphetId,
            RunModeCatalog.VibeId,
            33UL);
        var edgeProgress = edge.StyleContract!;
        var mutagenist = StyleObservation(
            AgentStyleContractCatalog.MutagenistId,
            RunModeCatalog.VibeId,
            34UL);
        var mutagenistProgress = mutagenist.StyleContract!;
        var redline = StyleObservation(
            AgentStyleContractCatalog.RedlineId,
            RunModeCatalog.ClassicId,
            35UL);
        var redlineProgress = redline.StyleContract!;
        var impossibleTerminalStillwater = stillwater with
        {
            Tick = 1,
            StepsRemaining = stillwater.MaximumSteps - 1,
            Status = RunStatus.Dead,
            DeathCause = DeathCause.SelfCollision,
            EpisodeMetrics = stillwater.EpisodeMetrics with { SurvivalSteps = 1 },
            StyleContract = stillwaterProgress with
            {
                Criteria =
                [
                    stillwaterProgress.Criteria[0] with { Current = 1 },
                    stillwaterProgress.Criteria[1] with
                    {
                        Current = 10_000,
                        Numerator = 1,
                        Denominator = 1,
                        ThresholdReached = true,
                    },
                ],
                ThresholdsReached = 1,
                AllThresholdsReached = false,
            },
        };
        var impossibleTerminalRedline = redline with
        {
            Tick = 1,
            StepsRemaining = redline.MaximumSteps - 1,
            Status = RunStatus.Dead,
            DeathCause = DeathCause.Starvation,
            EpisodeMetrics = redline.EpisodeMetrics with { SurvivalSteps = 1 },
            StyleContract = redlineProgress with
            {
                Criteria =
                [
                    redlineProgress.Criteria[0],
                    redlineProgress.Criteria[1] with
                    {
                        Current = 10_000,
                        Numerator = 1,
                        Denominator = 1,
                        ThresholdReached = true,
                    },
                ],
                ThresholdsReached = 1,
                AllThresholdsReached = false,
            },
        };

        AgentObservationV5[] invalidObservations =
        [
            impossibleTerminalStillwater,
            impossibleTerminalRedline,
            stillwater with { LessonProgress = lesson },
            stillwater with { EpisodeMetrics = null! },
            stillwater with
            {
                EpisodeMetrics = stillwater.EpisodeMetrics with { Schema = "wrong" },
            },
            stillwater with
            {
                EpisodeMetrics = stillwater.EpisodeMetrics with { SurvivalSteps = 1 },
            },
            stillwater with
            {
                StyleContract = ReplaceCriterion(
                    stillwaterProgress,
                    0,
                    stillwaterProgress.Criteria[0] with { Current = 1 }),
            },
            stillwater with
            {
                StyleContract = ReplaceCriterion(
                    stillwaterProgress,
                    1,
                    stillwaterProgress.Criteria[1] with { Denominator = 1 }),
            },
            crown with
            {
                StyleContract = ReplaceCriterion(
                    crownProgress,
                    1,
                    crownProgress.Criteria[1] with
                    {
                        Current = 10_000,
                        Numerator = 1,
                        Denominator = 1,
                        ThresholdReached = true,
                    },
                    criteriaSatisfied: 1),
            },
            edge with
            {
                StyleContract = ReplaceCriterion(
                    edgeProgress,
                    0,
                    edgeProgress.Criteria[0] with { Current = 1 }),
            },
            mutagenist with
            {
                StyleContract = ReplaceCriterion(
                    mutagenistProgress,
                    0,
                    mutagenistProgress.Criteria[0] with { Current = 1 }),
            },
            redline with
            {
                StyleContract = ReplaceCriterion(
                    redlineProgress,
                    0,
                    redlineProgress.Criteria[0] with { Current = 1 }),
            },
            redline with
            {
                StyleContract = ReplaceCriterion(
                    redlineProgress,
                    1,
                    redlineProgress.Criteria[1] with { Denominator = 1 }),
            },
        ];

        foreach (var observation in invalidObservations)
        {
            await AssertViewerRejectsAsync(CreateInitialFrame(observation));
        }
    }

    [Fact]
    public async Task Viewer_client_accepts_canonical_progress_for_every_signal_school_lesson()
    {
        foreach (var definition in AgentSignalSchoolCatalog.All)
        {
            var observation = new AgentMatchSession(new AgentMatchOptions(
                $"viewer-{definition.Id}",
                definition.ModeId,
                RunModeCatalog.CurrentModeVersion,
                definition.PracticeSeed,
                AgentSeedVisibility.Open,
                definition.MaximumSteps,
                lessonId: definition.Id)).Observe();

            await AssertViewerAcceptsAsync(CreateInitialFrame(observation));
        }
    }

    [Fact]
    public async Task Viewer_client_enforces_canonical_lesson_progress_and_ownership()
    {
        var definition = AgentSignalSchoolCatalog.Get("first-turn");
        var observation = new AgentMatchSession(new AgentMatchOptions(
            "strict-lesson",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            lessonId: "first-turn")).Observe();
        var progress = Assert.IsType<AgentLessonProgressV3>(observation.LessonProgress);
        var first = progress.Requirements[0];
        var second = progress.Requirements[1];
        var rival = new AgentRivalObservationV1(
            "optimal",
            "Optimal",
            observation.Tick,
            RunStatus.Running,
            DeathCause.None,
            Score: 0);
        AgentObservationV5[] invalidObservations =
        [
            observation with { LessonProgress = progress with { LessonId = "unknown" } },
            observation with { LessonProgress = progress with { Schema = "wrong" } },
            observation with { LessonProgress = progress with { Title = "Wrong" } },
            observation with { LessonProgress = progress with { Instruction = "Wrong" } },
            observation with
            {
                LessonProgress = progress with { EvaluationPolicyId = "wrong" },
            },
            observation with
            {
                LessonProgress = progress with
                {
                    Requirements = [first with { EvidenceSource = AgentLessonEvidenceSource.ReplayTrace }, second],
                },
            },
            observation with
            {
                LessonProgress = progress with
                {
                    Requirements = [first with { Current = 1, Satisfied = true }, second],
                },
            },
            observation with
            {
                LessonProgress = progress with
                {
                    Requirements = [first with { Target = first.Target + 1 }, second],
                },
            },
            observation with
            {
                LessonProgress = progress with { RequirementsSatisfied = 1 },
            },
            observation with
            {
                LessonProgress = progress with { AllRequirementsSatisfied = true },
            },
            observation with
            {
                LessonProgress = progress with { EvidenceState = AgentLessonEvidenceState.Verified },
            },
            observation with
            {
                LessonProgress = progress with { AttemptEvidenceCount = 1 },
            },
            observation with
            {
                LessonProgress = progress with
                {
                    RetryDescriptor = AgentSignalSchoolCatalog.CreateRetryDescriptor(
                        progress.LessonId,
                        observation.Passport.ActionProfile),
                },
            },
            observation with
            {
                SeedVisibility = AgentSeedVisibility.Blind,
                GameplaySeed = null,
            },
            observation with { GameplaySeed = observation.GameplaySeed + 1 },
            observation with
            {
                MaximumSteps = observation.MaximumSteps + 1,
                StepsRemaining = observation.StepsRemaining + 1,
            },
            observation with { Rival = rival },
        ];

        foreach (var invalid in invalidObservations)
        {
            await AssertViewerRejectsAsync(CreateInitialFrame(invalid));
        }
    }

    [Fact]
    public async Task Viewer_client_requires_exact_replay_bound_style_outcomes()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "strict-style-outcome",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            41UL,
            AgentSeedVisibility.Open,
            maximumSteps: 1,
            styleContractId: AgentStyleContractCatalog.StillwaterId));
        var initial = session.Observe();
        var response = session.SubmitAction(new AgentActionRequest(
            "strict-style-step",
            initial.Tick,
            initial.StateHash,
            AgentAction.Continue));
        var terminal = response.Observation;
        var outcome = Assert.IsType<AgentStyleOutcomeV3>(response.MatchResult!.StyleOutcome);
        var validFrame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            Sequence: 1,
            AgentViewerOperationKind.Step,
            StartTick: initial.Tick,
            StartStateHash: initial.StateHash,
            StepsAdvanced: 1,
            BurstStopReason: null,
            BurstStopEvent: null,
            terminal,
            SurvivalFor(terminal),
            AgentMatchEndReason.StepLimit,
            VerifiedResultAvailable: true,
            outcome.ReplayPayloadHash,
            outcome);
        var first = outcome.Criteria[0];
        var rate = outcome.Criteria[1];
        var differingRate = rate with
        {
            Current = 0,
            Numerator = 0,
            Denominator = 1,
            ThresholdReached = false,
        };
        AgentViewerFrameV9[] invalidFrames =
        [
            validFrame with { StyleOutcome = null },
            validFrame with { StyleOutcome = outcome with { Schema = "wrong" } },
            validFrame with { StyleOutcome = outcome with { ReplayPayloadHash = "wrong" } },
            validFrame with
            {
                StyleOutcome = outcome with
                {
                    Criteria = [first with { Current = first.Current + 1 }, rate],
                },
            },
            validFrame with
            {
                StyleOutcome = outcome with
                {
                    Criteria = [first, differingRate],
                    ThresholdsReached = 0,
                },
            },
            CreateInitialFrame(initial) with { StyleOutcome = outcome },
            validFrame with
            {
                Observation = terminal with { StyleContract = null },
                StyleOutcome = outcome,
            },
            new AgentViewerFrameV9(
                AgentViewerFrameV9.Contract,
                Sequence: 1,
                AgentViewerOperationKind.Finish,
                StartTick: initial.Tick,
                StartStateHash: initial.StateHash,
                StepsAdvanced: 0,
                BurstStopReason: null,
                BurstStopEvent: null,
                initial with
                {
                    Lifecycle = AgentMatchLifecycle.FailedClosed,
                    IsActionAwaited = false,
                },
                SurvivalFor(initial),
                AgentMatchEndReason.ReplayFailure,
                VerifiedResultAvailable: false,
                null,
                outcome),
        ];

        await AssertViewerAcceptsAsync(validFrame);
        foreach (var invalid in invalidFrames)
        {
            await AssertViewerRejectsAsync(invalid);
        }
    }

    [Fact]
    public async Task Viewer_client_requires_exact_replay_bound_lesson_outcomes()
    {
        var lesson = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "strict-lesson-outcome",
            lesson.ModeId,
            RunModeCatalog.CurrentModeVersion,
            lesson.PracticeSeed,
            AgentSeedVisibility.Open,
            lesson.MaximumSteps,
            lessonId: lesson.Id));
        var initial = session.Observe();
        var rejected = session.SubmitAction(new AgentActionRequest(
            "strict-lesson-reversal",
            initial.Tick,
            initial.StateHash,
            AgentAction.Left));
        var accepted = session.SubmitAction(new AgentActionRequest(
            "strict-lesson-turn",
            rejected.Observation.Tick,
            rejected.Observation.StateHash,
            AgentAction.Up));
        var result = session.Finish();
        var terminal = session.Observe();
        var outcome = Assert.IsType<AgentLessonOutcomeV3>(result.LessonOutcome);
        var validFrame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            Sequence: 3,
            AgentViewerOperationKind.Finish,
            StartTick: terminal.Tick,
            StartStateHash: terminal.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            terminal,
            SurvivalFor(terminal),
            AgentMatchEndReason.AgentFinished,
            VerifiedResultAvailable: true,
            VerifiedReplayPayloadHash: outcome.ReplayPayloadHash,
            StyleOutcome: null,
            LessonOutcome: outcome);
        var first = outcome.Requirements[0];
        var second = outcome.Requirements[1];
        AgentViewerFrameV9[] invalidFrames =
        [
            validFrame with
            {
                Observation = terminal with { Lifecycle = AgentMatchLifecycle.Aborted },
            },
            validFrame with { LessonOutcome = null },
            validFrame with { LessonOutcome = outcome with { Schema = "wrong" } },
            validFrame with
            {
                LessonOutcome = outcome with { EndReason = AgentMatchEndReason.StepLimit },
            },
            validFrame with
            {
                LessonOutcome = outcome with { ReplayPayloadHash = new string('0', 64) },
            },
            validFrame with
            {
                LessonOutcome = outcome with { AttemptEvidenceHash = new string('0', 64) },
            },
            validFrame with
            {
                LessonOutcome = outcome with
                {
                    Requirements = [first with { Current = 0, Satisfied = false }, second],
                },
            },
            validFrame with
            {
                LessonOutcome = outcome with { RequirementsSatisfied = 1 },
            },
            validFrame with
            {
                LessonOutcome = outcome with
                {
                    RetryDescriptor = AgentSignalSchoolCatalog.CreateRetryDescriptor(
                        outcome.LessonId,
                        AgentPassportV4.FourDirectionBurstActionProfile),
                },
            },
            CreateInitialFrame(initial) with { LessonOutcome = outcome },
            validFrame with
            {
                Observation = terminal with { LessonProgress = null },
                LessonOutcome = outcome,
            },
            validFrame with
            {
                Observation = terminal with
                {
                    Lifecycle = AgentMatchLifecycle.FailedClosed,
                    IsActionAwaited = false,
                    LessonProgress = terminal.LessonProgress! with
                    {
                        EvidenceState = AgentLessonEvidenceState.FailedClosed,
                    },
                },
                EndReason = AgentMatchEndReason.ReplayFailure,
                VerifiedResultAvailable = false,
                LessonOutcome = outcome,
            },
        ];

        Assert.True(accepted.Observation.LessonProgress!.AllRequirementsSatisfied);
        await AssertViewerAcceptsAsync(validFrame);
        foreach (var invalid in invalidFrames)
        {
            await AssertViewerRejectsAsync(invalid);
        }
    }

    [Fact]
    public async Task Viewer_client_requires_nested_constructor_members()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "required-viewer-json",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            51UL,
            AgentSeedVisibility.Open,
            maximumSteps: 1,
            styleContractId: AgentStyleContractCatalog.StillwaterId));
        var initialFrame = CreateInitialFrame(session.Observe());
        var initialJson = JsonNode.Parse(SerializeFrame(initialFrame))!.AsObject();
        Action<JsonObject>[] mutations =
        [
            root => root.Remove("schema"),
            root => root["observation"]!.AsObject().Remove("match_id"),
            root => root["observation"]!.AsObject().Remove("food"),
            root => root["observation"]!["passport"]!.AsObject().Remove("agent_id"),
            root => root["observation"]!["episode_metrics"]!.AsObject().Remove("schema"),
            root => root["observation"]!["style_contract"]!.AsObject().Remove("contract_id"),
            root => root["observation"]!["style_contract"]!["criteria"]![0]!
                .AsObject().Remove("numerator"),
        ];

        foreach (var mutation in mutations)
        {
            var missing = initialJson.DeepClone().AsObject();
            mutation(missing);
            await AssertViewerRejectsPayloadAsync(missing.ToJsonString(ViewerJsonOptions) + "\n");
        }

        var response = session.SubmitAction(new AgentActionRequest(
            "required-viewer-step",
            initialFrame.Observation.Tick,
            initialFrame.Observation.StateHash,
            AgentAction.Continue));
        var result = Assert.IsType<AgentMatchResultV5>(response.MatchResult);
        var terminalFrame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            Sequence: 1,
            AgentViewerOperationKind.Step,
            StartTick: initialFrame.Observation.Tick,
            StartStateHash: initialFrame.Observation.StateHash,
            StepsAdvanced: 1,
            BurstStopReason: null,
            BurstStopEvent: null,
            response.Observation,
            SurvivalFor(response.Observation),
            result.EndReason,
            VerifiedResultAvailable: true,
            result.StyleOutcome!.ReplayPayloadHash,
            result.StyleOutcome);
        var terminalJson = JsonNode.Parse(SerializeFrame(terminalFrame))!.AsObject();
        terminalJson["style_outcome"]!.AsObject().Remove("replay_payload_hash");
        await AssertViewerRejectsPayloadAsync(terminalJson.ToJsonString(ViewerJsonOptions) + "\n");
    }

    [Fact]
    public async Task Viewer_client_rejects_duplicate_members_and_integer_enum_tokens()
    {
        var observation = new AgentMatchSession(new AgentMatchOptions(
            "strict-enum-json",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            61UL,
            AgentSeedVisibility.Open,
            styleContractId: AgentStyleContractCatalog.StillwaterId)).Observe();
        var valid = SerializeFrame(CreateInitialFrame(observation));
        string[] invalidPayloads =
        [
            $"{{\"schema\":\"{AgentViewerFrameV9.Contract}\"," + valid[1..],
            valid.Replace(
                "\"criterion_id\":\"survival_steps\"",
                "\"criterion_id\":\"survival_steps\","
                    + "\"criterion_id\":\"survival_steps\"",
                StringComparison.Ordinal),
            valid.Replace("\"operation\":\"initial\"", "\"operation\":0", StringComparison.Ordinal),
            valid.Replace("\"status\":\"running\"", "\"status\":0", StringComparison.Ordinal),
            valid.Replace("\"unit\":\"count\"", "\"unit\":0", StringComparison.Ordinal),
        ];

        Assert.All(invalidPayloads, payload => Assert.NotEqual(valid, payload));
        foreach (var invalid in invalidPayloads)
        {
            await AssertViewerRejectsPayloadAsync(invalid);
        }
    }

    [Fact]
    public async Task Viewer_client_rejects_invalid_rival_truth_and_identity_changes()
    {
        AgentMatchSession CreateSession(string matchId, string? rivalId = null) =>
            new(new AgentMatchOptions(
                matchId,
                RunModeCatalog.ClassicId,
                RunModeCatalog.CurrentModeVersion,
                71UL,
                AgentSeedVisibility.Open,
                maximumSteps: 10,
                rivalPersonalityId: rivalId));

        var rivalSession = CreateSession("strict-rival", "optimal");
        var rivalInitial = rivalSession.Observe();
        var rival = Assert.IsType<AgentRivalObservationV1>(rivalInitial.Rival);
        AgentRivalObservationV1[] malformedRivals =
        [
            rival with { PersonalityId = "unknown" },
            rival with { DisplayName = "Wrong" },
            rival with { Status = (RunStatus)byte.MaxValue },
            rival with { DeathCause = (DeathCause)byte.MaxValue },
            rival with { Status = RunStatus.Running, DeathCause = DeathCause.Starvation },
            rival with { Status = RunStatus.Won, DeathCause = DeathCause.SelfCollision },
            rival with { Status = RunStatus.Dead, DeathCause = DeathCause.None },
            rival with { Score = -1 },
            rival with { Score = SnakeRun.MaximumScore + 1 },
            rival with { Tick = rivalInitial.Tick + 1 },
        ];

        foreach (var malformed in malformedRivals)
        {
            await AssertViewerRejectsAsync(
                CreateInitialFrame(rivalInitial with { Rival = malformed }));
        }

        (RunStatus Status, DeathCause DeathCause)[] malformedPrimaryStates =
        [
            (RunStatus.Running, DeathCause.Starvation),
            (RunStatus.Won, DeathCause.SelfCollision),
            (RunStatus.Dead, DeathCause.None),
        ];
        foreach (var (status, deathCause) in malformedPrimaryStates)
        {
            await AssertViewerRejectsAsync(CreateInitialFrame(rivalInitial with
            {
                Status = status,
                DeathCause = deathCause,
            }));
        }

        var rivalRejected = rivalSession.SubmitAction(new AgentActionRequest(
            "strict-rival-rejected",
            rivalInitial.Tick,
            rivalInitial.StateHash,
            AgentAction.Left)).Observation;
        var noRivalSession = CreateSession("strict-no-rival");
        var noRivalInitial = noRivalSession.Observe();
        var noRivalRejected = noRivalSession.SubmitAction(new AgentActionRequest(
            "strict-no-rival-rejected",
            noRivalInitial.Tick,
            noRivalInitial.StateHash,
            AgentAction.Left)).Observation;
        var alternate = AiPersonalityCatalog.BuiltIn.First(
            personality => personality.Id != rival.PersonalityId);

        (AgentObservationV5 Initial, AgentObservationV5 Followup)[] identityChanges =
        [
            (noRivalInitial, noRivalRejected with { Rival = rival }),
            (rivalInitial, rivalRejected with { Rival = null }),
            (rivalInitial, rivalRejected with
            {
                Rival = rivalRejected.Rival! with
                {
                    PersonalityId = alternate.Id,
                    DisplayName = alternate.Name,
                },
            }),
        ];

        foreach (var (initial, followup) in identityChanges)
        {
            var first = CreateInitialFrame(initial);
            var second = first with
            {
                Sequence = 1,
                Operation = AgentViewerOperationKind.Step,
                Observation = followup,
            };
            await AssertViewerRejectsPayloadAsync(SerializeFrame(first) + SerializeFrame(second));
        }
    }

    [Fact]
    public async Task Viewer_client_accepts_monotonic_lesson_and_style_identity_across_frames()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var lessonSession = new AgentMatchSession(new AgentMatchOptions(
            "viewer-lesson-sequence",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            lessonId: definition.Id));
        var lessonInitial = lessonSession.Observe();
        var rejected = lessonSession.SubmitAction(new AgentActionRequest(
            "viewer-lesson-reversal",
            lessonInitial.Tick,
            lessonInitial.StateHash,
            AgentLessonRouteDriver.OppositeAction(lessonInitial)));
        Assert.Equal(AgentActionRejection.IllegalDirection, rejected.Rejection);
        var lessonFirst = CreateInitialFrame(lessonInitial);
        var lessonSecond = lessonFirst with
        {
            Sequence = 1,
            Operation = AgentViewerOperationKind.Step,
            Observation = rejected.Observation,
        };
        await AssertViewerAcceptsPayloadAsync(
            SerializeFrame(lessonFirst) + SerializeFrame(lessonSecond),
            minimumSequence: 1);

        var progressedWithoutPreviousAction = rejected.Observation with { PreviousAction = null };
        var stableFirst = CreateInitialFrame(progressedWithoutPreviousAction);
        var stableSecond = stableFirst with
        {
            Sequence = 1,
            Operation = AgentViewerOperationKind.Step,
            Observation = rejected.Observation,
        };
        await AssertViewerAcceptsPayloadAsync(
            SerializeFrame(stableFirst) + SerializeFrame(stableSecond),
            minimumSequence: 1);

        var styleSession = new AgentMatchSession(new AgentMatchOptions(
            "viewer-style-sequence",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            81UL,
            AgentSeedVisibility.Open,
            styleContractId: AgentStyleContractCatalog.StillwaterId));
        var styleInitial = styleSession.Observe();
        var styleRejected = styleSession.SubmitAction(new AgentActionRequest(
            "viewer-style-reversal",
            styleInitial.Tick,
            styleInitial.StateHash,
            AgentLessonRouteDriver.OppositeAction(styleInitial)));
        Assert.Equal(AgentActionRejection.IllegalDirection, styleRejected.Rejection);
        var styleFirst = CreateInitialFrame(styleInitial);
        var styleSecond = styleFirst with
        {
            Sequence = 1,
            Operation = AgentViewerOperationKind.Step,
            Observation = styleRejected.Observation,
        };
        await AssertViewerAcceptsPayloadAsync(
            SerializeFrame(styleFirst) + SerializeFrame(styleSecond),
            minimumSequence: 1);
    }

    [Fact]
    public async Task Viewer_client_rejects_lesson_identity_and_evidence_regressions_across_frames()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "viewer-lesson-regression",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            lessonId: definition.Id));
        var initial = session.Observe();
        var rejected = session.SubmitAction(new AgentActionRequest(
            "viewer-regression-reversal",
            initial.Tick,
            initial.StateHash,
            AgentLessonRouteDriver.OppositeAction(initial)));
        var progressed = rejected.Observation;
        var progressedWithoutPreviousAction = progressed with { PreviousAction = null };
        var first = CreateInitialFrame(progressedWithoutPreviousAction);
        var second = first with
        {
            Sequence = 1,
            Operation = AgentViewerOperationKind.Step,
            Observation = progressed,
        };
        var initialProgress = Assert.IsType<AgentLessonProgressV3>(initial.LessonProgress);
        var progressedProgress = Assert.IsType<AgentLessonProgressV3>(progressed.LessonProgress);
        AgentObservationV5[] regressions =
        [
            progressed with { LessonProgress = null },
            progressed with { LessonProgress = initialProgress },
            progressed with
            {
                LessonProgress = progressedProgress with
                {
                    AttemptEvidenceHash = new string('0', 64),
                },
            },
            progressed with
            {
                LessonProgress = progressedProgress with
                {
                    AttemptEvidenceCount = progressedProgress.AttemptEvidenceCount + 1,
                },
            },
        ];

        foreach (var regression in regressions)
        {
            await AssertViewerRejectsPayloadAsync(
                SerializeFrame(first) + SerializeFrame(second with { Observation = regression }));
        }

        var noLessonSession = new AgentMatchSession(new AgentMatchOptions(
            "viewer-lesson-injection",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps));
        var noLessonInitial = noLessonSession.Observe();
        var noLessonRejected = noLessonSession.SubmitAction(new AgentActionRequest(
            "viewer-injected-reversal",
            noLessonInitial.Tick,
            noLessonInitial.StateHash,
            AgentLessonRouteDriver.OppositeAction(noLessonInitial))).Observation;
        var noLessonFirst = CreateInitialFrame(noLessonInitial);
        var injectedSecond = noLessonFirst with
        {
            Sequence = 1,
            Operation = AgentViewerOperationKind.Step,
            Observation = noLessonRejected with { LessonProgress = initialProgress },
        };
        await AssertViewerRejectsPayloadAsync(
            SerializeFrame(noLessonFirst) + SerializeFrame(injectedSecond));
    }

    [Fact]
    public async Task Viewer_client_cross_checks_all_lesson_progress_against_live_run_facts()
    {
        foreach (var definition in AgentSignalSchoolCatalog.All)
        {
            var progressed = DriveLessonToSatisfiedProgress(definition);
            var invalid = definition.Id switch
            {
                AgentSignalSchoolCatalog.FirstTurnId => progressed with
                {
                    EpisodeMetrics = progressed.EpisodeMetrics with { DirectionChanges = 0 },
                },
                AgentSignalSchoolCatalog.WrapLineId => progressed with
                {
                    EpisodeMetrics = progressed.EpisodeMetrics with { Wraps = 0 },
                },
                AgentSignalSchoolCatalog.HungerRouteId or AgentSignalSchoolCatalog.ExitRouteId =>
                    progressed with
                    {
                        ComboCount = 0,
                        EpisodeMetrics = progressed.EpisodeMetrics with
                        {
                            FoodEaten = 0,
                            PeakCombo = 0,
                        },
                    },
                AgentSignalSchoolCatalog.PowerRouteId => progressed with
                {
                    EpisodeMetrics = progressed.EpisodeMetrics with
                    {
                        PowersCollected = 0,
                        PowersActivated = 0,
                    },
                },
                AgentSignalSchoolCatalog.RecoverRouteId => progressed with
                {
                    EpisodeMetrics = progressed.EpisodeMetrics with { Recoveries = 0 },
                },
                AgentSignalSchoolCatalog.ComboRouteId => progressed with
                {
                    ComboCount = Math.Min(progressed.ComboCount, 2),
                    EpisodeMetrics = progressed.EpisodeMetrics with
                    {
                        FoodEaten = 2,
                        PeakCombo = 2,
                    },
                },
                AgentSignalSchoolCatalog.DeathReadId => progressed with
                {
                    Status = RunStatus.Running,
                    DeathCause = DeathCause.None,
                },
                _ => throw new InvalidOperationException(definition.Id),
            };
            await AssertViewerRejectsAsync(CreateInitialFrame(invalid));

            if (definition.Id == AgentSignalSchoolCatalog.PowerRouteId)
            {
                await AssertViewerRejectsAsync(CreateInitialFrame(progressed with
                {
                    EpisodeMetrics = progressed.EpisodeMetrics with { PowersActivated = 0 },
                }));
            }
        }
    }

    [Fact]
    public async Task Viewer_client_binds_death_lesson_progress_to_the_visible_terminal_event()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.DeathReadId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "viewer-death-evidence",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            lessonId: definition.Id));
        AgentActionResponse? response = null;
        for (var index = 0; index < definition.MaximumSteps; index++)
        {
            var observation = session.Observe();
            response = session.SubmitAction(new AgentActionRequest(
                $"viewer-death-evidence-{index}",
                observation.Tick,
                observation.StateHash,
                AgentLessonRouteDriver.ChooseAction(definition.Id, observation)));
            Assert.True(response.Accepted, response.Rejection.ToString());
            if (response.MatchResult is not null)
            {
                break;
            }
        }

        var completed = Assert.IsType<AgentActionResponse>(response);
        var result = Assert.IsType<AgentMatchResultV5>(completed.MatchResult);
        var terminal = completed.Observation;
        var progress = Assert.IsType<AgentLessonProgressV3>(terminal.LessonProgress);
        var outcome = Assert.IsType<AgentLessonOutcomeV3>(result.LessonOutcome);
        var valid = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            Sequence: terminal.Tick,
            AgentViewerOperationKind.Step,
            StartTick: terminal.Tick - 1,
            StartStateHash: new string('0', 16),
            StepsAdvanced: 1,
            BurstStopReason: null,
            BurstStopEvent: null,
            terminal,
            SurvivalFor(terminal),
            AgentMatchEndReason.RulesTerminal,
            VerifiedResultAvailable: true,
            VerifiedReplayPayloadHash: outcome.ReplayPayloadHash,
            StyleOutcome: null,
            LessonOutcome: outcome);
        Assert.Equal(DeathCause.SelfCollision, terminal.DeathCause);
        Assert.Contains(terminal.PreviousEvents, item =>
            item.Kind == RunEventKind.Died && item.Cause == terminal.DeathCause);
        await AssertViewerAcceptsAsync(valid);

        var afterTerminal = session.SubmitAction(new AgentActionRequest(
            "viewer-death-evidence-after-terminal",
            terminal.Tick,
            terminal.StateHash,
            AgentAction.Continue));
        Assert.Equal(AgentActionRejection.MatchNotAwaitingAction, afterTerminal.Rejection);
        Assert.DoesNotContain(afterTerminal.Observation.PreviousEvents, item =>
            item.Kind == RunEventKind.Died);
        var postTerminalFrame = valid with
        {
            Sequence = valid.Sequence + 1,
            Operation = AgentViewerOperationKind.Step,
            StartTick = terminal.Tick,
            StartStateHash = terminal.StateHash,
            StepsAdvanced = 0,
            Observation = afterTerminal.Observation,
        };
        await AssertViewerAcceptsPayloadAsync(
            SerializeFrame(valid) + SerializeFrame(postTerminalFrame),
            minimumSequence: postTerminalFrame.Sequence);

        await AssertViewerRejectsAsync(valid with
        {
            Observation = terminal with
            {
                PreviousEvents = terminal.PreviousEvents
                    .Where(item => item.Kind != RunEventKind.Died)
                    .ToArray(),
            },
        });
        await AssertViewerRejectsAsync(valid with
        {
            Observation = terminal with
            {
                PreviousEvents = terminal.PreviousEvents.Select(item =>
                    item.Kind == RunEventKind.Died
                        ? item with { Cause = DeathCause.Starvation }
                        : item).ToArray(),
            },
        });

        var unmetRequirements = progress.Requirements
            .Select(item => item with { Current = 0, Satisfied = false })
            .ToArray();
        var retry = AgentSignalSchoolCatalog.CreateRetryDescriptor(
            progress.LessonId,
            terminal.Passport.ActionProfile);
        var unmetProgress = progress with
        {
            Requirements = unmetRequirements,
            RequirementsSatisfied = 0,
            AllRequirementsSatisfied = false,
            FirstUnmetRequirementId = unmetRequirements[0].RequirementId,
            RetryDescriptor = retry,
        };
        var unmetOutcome = outcome with
        {
            Requirements = unmetRequirements,
            RequirementsSatisfied = 0,
            AllRequirementsSatisfied = false,
            FirstUnmetRequirementId = unmetRequirements[0].RequirementId,
            ReviewCode = AgentLessonReviewCode.ReplayRequirementUnmet,
            RetryDescriptor = retry,
        };
        Assert.True(AgentSignalSchoolCatalog.IsValidProgress(unmetProgress));
        Assert.True(AgentSignalSchoolCatalog.IsValidOutcome(unmetOutcome));
        await AssertViewerRejectsAsync(valid with
        {
            Observation = terminal with { LessonProgress = unmetProgress },
            LessonOutcome = unmetOutcome,
        });
    }

    [Fact]
    public async Task Viewer_client_rejects_noncanonical_attempt_and_retry_ownership()
    {
        var wrap = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.WrapLineId);
        var wrapObservation = new AgentMatchSession(new AgentMatchOptions(
            "viewer-attempt-ownership",
            wrap.ModeId,
            RunModeCatalog.CurrentModeVersion,
            wrap.PracticeSeed,
            AgentSeedVisibility.Open,
            wrap.MaximumSteps,
            lessonId: wrap.Id)).Observe();
        var wrapProgress = Assert.IsType<AgentLessonProgressV3>(wrapObservation.LessonProgress);
        AgentLessonProgressV3[] invalidAttemptProgress =
        [
            wrapProgress with
            {
                AttemptEvidenceCount = 1,
                AttemptEvidenceHash = new string('1', 64),
            },
            wrapProgress with { AttemptEvidenceHash = new string('1', 64) },
        ];
        foreach (var invalid in invalidAttemptProgress)
        {
            await AssertViewerRejectsAsync(CreateInitialFrame(wrapObservation with
            {
                LessonProgress = invalid,
            }));
        }

        var firstTurn = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var terminalSession = new AgentMatchSession(new AgentMatchOptions(
            "viewer-retry-ownership",
            firstTurn.ModeId,
            RunModeCatalog.CurrentModeVersion,
            firstTurn.PracticeSeed,
            AgentSeedVisibility.Open,
            firstTurn.MaximumSteps,
            lessonId: firstTurn.Id));
        _ = terminalSession.Finish();
        var terminal = terminalSession.Observe();
        var terminalProgress = Assert.IsType<AgentLessonProgressV3>(terminal.LessonProgress);
        await AssertViewerRejectsAsync(CreateInitialFrame(terminal with
        {
            LessonProgress = terminalProgress with
            {
                RetryDescriptor = terminalProgress.RetryDescriptor! with
                {
                    ActionProfile = AgentPassportV4.FourDirectionBurstActionProfile,
                },
            },
        }));
    }

    [Fact]
    public async Task Viewer_client_requires_a_replay_identity_that_matches_the_verified_result()
    {
        var lesson = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.WrapLineId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "replay-identity",
            lesson.ModeId,
            RunModeCatalog.CurrentModeVersion,
            lesson.PracticeSeed,
            AgentSeedVisibility.Open,
            lesson.MaximumSteps,
            lessonId: lesson.Id));
        var live = session.Observe();
        var result = session.Finish();
        var terminal = session.Observe();
        var outcome = Assert.IsType<AgentLessonOutcomeV3>(result.LessonOutcome);
        Assert.Equal(result.ReplayPayloadHash, outcome.ReplayPayloadHash);

        var terminalFrame = new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            Sequence: 1,
            AgentViewerOperationKind.Finish,
            StartTick: terminal.Tick,
            StartStateHash: terminal.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            terminal,
            SurvivalFor(terminal),
            AgentMatchEndReason.AgentFinished,
            VerifiedResultAvailable: true,
            VerifiedReplayPayloadHash: result.ReplayPayloadHash,
            StyleOutcome: null,
            LessonOutcome: outcome);
        await AssertViewerAcceptsAsync(terminalFrame);

        // A verified result must publish its replay identity, keep it lowercase hex,
        // agree with the bound lesson outcome, and stay absent while none exists.
        await AssertViewerRejectsAsync(terminalFrame with
        {
            VerifiedReplayPayloadHash = null,
        });
        await AssertViewerRejectsAsync(terminalFrame with
        {
            VerifiedReplayPayloadHash = result.ReplayPayloadHash.ToUpperInvariant(),
        });
        await AssertViewerRejectsAsync(terminalFrame with
        {
            VerifiedReplayPayloadHash = new string('a', 63),
        });
        await AssertViewerRejectsAsync(terminalFrame with
        {
            VerifiedReplayPayloadHash = new string('a', 64),
        });
        await AssertViewerRejectsAsync(CreateInitialFrame(live) with
        {
            VerifiedReplayPayloadHash = result.ReplayPayloadHash,
        });
    }

    [Fact]
    public async Task Viewer_client_rejects_a_survival_block_that_disagrees_with_the_board()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "survival-truth",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            4242UL,
            AgentSeedVisibility.Open));
        var observation = session.Observe();
        var validFrame = CreateInitialFrame(observation);
        await AssertViewerAcceptsAsync(validFrame);

        var survival = validFrame.SurvivalState;
        var resources = survival.RecoveryResources.ToArray();
        AgentViewerFrameV9[] invalidFrames =
        [
            validFrame with { SurvivalState = survival with { Schema = "wrong" } },
            validFrame with
            {
                SurvivalState = survival with
                {
                    StructuralOpenExits = survival.StructuralOpenExits - 1,
                },
            },
            validFrame with { SurvivalState = survival with { CandidateExits = 4 } },
            validFrame with
            {
                SurvivalState = survival with { ExitPressure = AgentExitPressureV1.Trapped },
            },
            validFrame with { SurvivalState = survival with { HeldRecoveryCount = 1 } },
            validFrame with
            {
                SurvivalState = survival with
                {
                    RecoveryResources = resources.Reverse().ToArray(),
                },
            },
            validFrame with
            {
                SurvivalState = survival with
                {
                    RecoveryResources = resources.Take(3).ToArray(),
                },
            },
            validFrame with
            {
                SurvivalState = survival with
                {
                    RecoveryResources = resources
                        .Select(item => item.Kind == AgentRecoveryResourceKind.Shield
                            ? item with { Held = true, TicksRemaining = 40 }
                            : item)
                        .ToArray(),
                },
            },
        ];
        foreach (var invalid in invalidFrames)
        {
            await AssertViewerRejectsAsync(invalid);
        }
    }

    [Fact]
    public async Task Viewer_client_requires_the_survival_block_to_be_present()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "survival-required",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            99UL,
            AgentSeedVisibility.Open));
        var payload = JsonNode.Parse(SerializeFrame(CreateInitialFrame(session.Observe())))!
            .AsObject();
        Assert.NotNull(payload["survival_state"]);
        payload.Remove("survival_state");

        await AssertViewerRejectsPayloadAsync(payload.ToJsonString());
    }

    [Theory]
    [InlineData(1, AgentExitPressureV1.Narrow)]
    [InlineData(2, AgentExitPressureV1.Pinned)]
    [InlineData(3, AgentExitPressureV1.Trapped)]
    public async Task Viewer_client_tracks_a_board_that_closes_around_the_head(
        int blockedExits,
        AgentExitPressureV1 expected)
    {
        // The viewer must read the board rather than always answering OPEN, so
        // occupy exits one at a time and require the pressure tier to follow.
        var session = new AgentMatchSession(new AgentMatchOptions(
            "survival-closing",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            4242UL,
            AgentSeedVisibility.Open));
        var observation = session.Observe();
        var head = new GridPoint(observation.Head.X, observation.Head.Y);
        var tail = Wrapped(observation, head, observation.Direction.Opposite());
        var blockers = Enum.GetValues<Direction>()
            .Where(direction => direction != observation.Direction.Opposite())
            .Take(blockedExits)
            .Select(direction => Wrapped(observation, head, direction))
            .ToArray();
        var closed = observation with
        {
            Body = Array.AsReadOnly(
                new[] { tail }.Concat(blockers).Append(observation.Head).ToArray()),
        };
        var survival = AgentSurvivalTestFacts.SurvivalFor(closed);
        Assert.Equal(expected, survival.ExitPressure);
        Assert.Equal(
            AgentSurvivalStateV1.RunningCandidateExits - blockedExits,
            survival.StructuralOpenExits);

        await AssertViewerAcceptsAsync(
            CreateInitialFrame(closed) with { SurvivalState = survival });
    }

    private static AgentPointV1 Wrapped(
        AgentObservationV5 observation,
        GridPoint origin,
        Direction direction)
    {
        var point = origin
            .Add(direction.Offset())
            .Wrap(observation.BoardWidth, observation.BoardHeight);
        return new AgentPointV1(point.X, point.Y);
    }

    private static AgentViewerFrameV9 CreateInitialFrame(AgentObservationV5 observation) =>
        new(
            AgentViewerFrameV9.Contract,
            Sequence: 0,
            AgentViewerOperationKind.Initial,
            StartTick: observation.Tick,
            StartStateHash: observation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            observation,
            SurvivalFor(observation),
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false,
            StyleOutcome: null);

    private static Task AssertViewerRejectsAsync(AgentViewerFrameV9 frame) =>
        AssertViewerRejectsPayloadAsync(SerializeFrame(frame));

    private static async Task AssertViewerAcceptsAsync(AgentViewerFrameV9 frame)
    {
        var pipeName = CreateTestPipeName();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServePayloadAsync(pipeName, SerializeFrame(frame), release.Task);
        using var client = new AgentViewerClient(pipeName, "dG9rZW4");

        var received = await TakeFrameAsync(client, frame.Sequence);
        Assert.Equal(SerializeFrame(frame), SerializeFrame(received));
        Assert.NotEqual(AgentViewerClientState.Rejected, client.State);
        release.SetResult();
        await server;
    }

    private static async Task AssertViewerAcceptsPayloadAsync(
        string payload,
        long minimumSequence)
    {
        var pipeName = CreateTestPipeName();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServePayloadAsync(pipeName, payload, release.Task);
        using var client = new AgentViewerClient(pipeName, "dG9rZW4");

        _ = await TakeFrameAsync(client, minimumSequence);
        Assert.NotEqual(AgentViewerClientState.Rejected, client.State);
        release.SetResult();
        await server;
    }

    private static AgentObservationV5 DriveLessonToSatisfiedProgress(
        AgentSignalLessonDefinitionV2 definition)
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            $"viewer-facts-{definition.Id}",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            lessonId: definition.Id));
        if (definition.Id == AgentSignalSchoolCatalog.FirstTurnId)
        {
            var initial = session.Observe();
            var rejected = session.SubmitAction(new AgentActionRequest(
                $"viewer-facts-{definition.Id}-reversal",
                initial.Tick,
                initial.StateHash,
                AgentLessonRouteDriver.OppositeAction(initial)));
            Assert.Equal(AgentActionRejection.IllegalDirection, rejected.Rejection);
        }

        for (var index = 0; index < definition.MaximumSteps; index++)
        {
            var observation = session.Observe();
            if (observation.LessonProgress!.AllRequirementsSatisfied)
            {
                return observation;
            }

            var response = session.SubmitAction(new AgentActionRequest(
                $"viewer-facts-{definition.Id}-{index}",
                observation.Tick,
                observation.StateHash,
                AgentLessonRouteDriver.ChooseAction(definition.Id, observation)));
            Assert.True(response.Accepted, $"{definition.Id}: {response.Rejection}");
            if (response.Observation.LessonProgress!.AllRequirementsSatisfied)
            {
                return response.Observation;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"{definition.Id} did not reach all lesson requirements.");
    }

    private static async Task AssertViewerRejectsPayloadAsync(string payload)
    {
        var pipeName = CreateTestPipeName();
        var server = ServePayloadAsync(pipeName, payload);
        using var client = new AgentViewerClient(pipeName, "dG9rZW4");

        await server;
        await WaitForStateAsync(client, AgentViewerClientState.Rejected);
        Assert.False(client.TryTakeLatest(out _, out _));
    }

    private static string SerializeFrame(AgentViewerFrameV9 frame) =>
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

    private static async Task<AgentViewerFrameV9> TakeFrameAsync(
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
