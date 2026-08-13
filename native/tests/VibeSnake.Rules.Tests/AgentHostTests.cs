using System.Globalization;
using System.Text.Json;
using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentHostTests
{
    [Fact]
    public void Registry_runs_complete_match_and_saves_only_after_explicit_request()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var registry = CreateRegistry(temporary.Path);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            "18446744073709551615",
            maximumSteps: 1);

        var before = registry.GetResult(started.MatchHandle);
        var moved = registry.PlayMove(
            started.MatchHandle,
            "move-1",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Continue);
        var after = registry.GetResult(started.MatchHandle);
        var saved = registry.SaveVerifiedReplay(started.MatchHandle);
        var savedAgain = registry.SaveVerifiedReplay(started.MatchHandle);

        Assert.Equal(StartAgentMatchV1.Contract, started.Schema);
        Assert.Equal("match_test", started.MatchHandle);
        Assert.Equal(AgentSessionRegistry.RetentionPolicy, started.RetentionPolicy);
        Assert.False(before.IsAvailable);
        Assert.Null(before.Result);
        Assert.True(moved.Accepted);
        Assert.True(after.IsAvailable);
        var result = Assert.IsType<AgentMatchSummaryV1>(after.Result);
        AssertSummary(result, "match_test", ulong.MaxValue.ToString(CultureInfo.InvariantCulture));
        Assert.True(saved.IsSuccess);
        Assert.Equal(ReplaySaveCode.Saved, saved.Code);
        Assert.Equal(ReplayVerificationCode.Verified, saved.ReplayVerificationCode);
        Assert.NotNull(saved.FileName);
        Assert.True(File.Exists(Path.Combine(
            temporary.Path,
            ReplayStore.ReplayDirectoryName,
            saved.FileName)));
        Assert.True(savedAgain.IsSuccess);
        Assert.Equal(ReplaySaveCode.AlreadyExists, savedAgain.Code);
        Assert.Equal(saved.FileName, savedAgain.FileName);

        var loaded = new ReplayStore(temporary.Path).Load(saved.FileName!);
        Assert.True(loaded.IsSuccess, loaded.Message);
        var playback = new RunReplayPlayback(Assert.IsType<RunReplay>(loaded.Replay));
        while (playback.TryAdvance(out _))
        {
        }

        Assert.True(playback.IsComplete);
        Assert.Equal(result.FinalTick, playback.StepIndex);
        Assert.Equal(result.FinalStateHash, playback.CurrentSnapshot.StateHash);
    }

    [Fact]
    public void Registry_generates_hidden_seed_and_reveals_it_only_in_result()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var registry = CreateRegistry(temporary.Path, seed: 987UL);

        var started = registry.StartMatch(
            RunModeCatalog.VibeId,
            AgentSeedVisibility.Blind,
            gameplaySeed: null,
            maximumSteps: null);
        var finished = registry.Finish(started.MatchHandle);

        Assert.Null(started.Observation.GameplaySeed);
        Assert.Equal("987", finished.GameplaySeed);
        Assert.Equal(AgentMatchLifecycle.Aborted, finished.Lifecycle);
        Assert.Equal(AgentMatchEndReason.AgentFinished, finished.EndReason);
        Assert.Equal(finished, registry.Finish(started.MatchHandle));
    }

    [Fact]
    public void Registry_validates_closed_inputs_and_unknown_handles()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var registry = CreateRegistry(temporary.Path);

        Assert.ThrowsAny<ArgumentException>(() => registry.StartMatch(
            " ", AgentSeedVisibility.Open, null, null));
        Assert.Throws<ArgumentException>(() => registry.StartMatch(
            "arcade", AgentSeedVisibility.Open, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId, (AgentSeedVisibility)255, null, null));
        Assert.Throws<ArgumentException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId, AgentSeedVisibility.Blind, "1", null));
        Assert.Throws<ArgumentException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId, AgentSeedVisibility.Open, "", null));
        Assert.Throws<ArgumentException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId, AgentSeedVisibility.Open, "-1", null));
        Assert.Throws<ArgumentException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            new string('1', 21),
            null));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId, AgentSeedVisibility.Open, null, 0));
        Assert.Throws<ArgumentException>(() => registry.Observe("bad"));
        Assert.Throws<ArgumentException>(() => registry.Observe(
            "match_" + new string('a', AgentMatchOptions.MaximumMatchIdLength)));
        Assert.Throws<ArgumentException>(() => registry.Observe("match_invalid!"));
        Assert.ThrowsAny<ArgumentException>(() => registry.Observe(null!));
        Assert.Throws<KeyNotFoundException>(() => registry.Observe("match_unknown"));
        Assert.Throws<InvalidOperationException>(() => registry.SaveVerifiedReplay(
            registry.StartMatch(
                RunModeCatalog.ClassicId,
                AgentSeedVisibility.Open,
                null,
                null).MatchHandle));
    }

    [Fact]
    public void Registry_bounds_live_capacity_and_evicts_oldest_completed_match()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var next = 0;
        var registry = new AgentSessionRegistry(
            new ReplayStore(temporary.Path),
            () => $"match_{next++}",
            () => 1UL);
        var handles = Enumerable.Range(0, AgentSessionRegistry.MaximumRetainedMatches)
            .Select(_ => registry.StartMatch(
                RunModeCatalog.ClassicId,
                AgentSeedVisibility.Open,
                null,
                null).MatchHandle)
            .ToArray();

        Assert.Throws<InvalidOperationException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null));
        registry.Finish(handles[0]);
        var replacement = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null);

        Assert.Equal("match_8", replacement.MatchHandle);
        Assert.Throws<KeyNotFoundException>(() => registry.Observe(handles[0]));
        Assert.Equal(0, registry.Observe(handles[1]).Tick);
    }

    [Fact]
    public void Registry_rejects_invalid_or_repeated_generated_handles()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var invalid = CreateRegistry(temporary.Path, handle: "invalid");
        Assert.Throws<ArgumentException>(() => invalid.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null));

        var repeated = CreateRegistry(temporary.Path, handle: "match_same");
        repeated.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null);
        Assert.Throws<InvalidOperationException>(() => repeated.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null));
    }

    [Fact]
    public void Registry_default_generators_mint_bounded_opaque_identity()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var registry = new AgentSessionRegistry(new ReplayStore(temporary.Path));

        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            maximumSteps: 1);

        Assert.StartsWith("match_", started.MatchHandle, StringComparison.Ordinal);
        Assert.InRange(started.MatchHandle.Length, 7, AgentMatchOptions.MaximumMatchIdLength);
        Assert.NotNull(started.Observation.GameplaySeed);
    }

    [Fact]
    public void Mcp_tools_expose_six_safe_operations_and_sanitized_failures()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var registry = CreateRegistry(temporary.Path);
        var tools = new McpAgentTools(registry);
        var started = tools.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            "123",
            2,
            AgentStyleContractCatalog.StillwaterId,
            "optimal");

        var observed = tools.ObserveMatch(started.MatchHandle);
        var moved = tools.PlayMove(
            started.MatchHandle,
            "tool-move",
            observed.Tick,
            observed.StateHash,
            AgentAction.Up,
            AgentPublicIntent.SeekFood);
        var pending = tools.GetMatchResult(started.MatchHandle);
        var finished = tools.FinishMatch(started.MatchHandle);
        var saved = tools.SaveVerifiedReplay(started.MatchHandle);

        Assert.True(moved.Accepted);
        Assert.Equal(
            AgentPublicIntent.SeekFood,
            moved.Observation.PreviousAction!.DeclaredIntent);
        Assert.Equal(AgentActionResponseV1.Contract, moved.Schema);
        Assert.Null(moved.MatchResult);
        Assert.False(pending.IsAvailable);
        Assert.Equal(AgentMatchEndReason.AgentFinished, finished.EndReason);
        Assert.True(saved.IsSuccess);
        Assert.NotNull(saved.RivalFileName);
        Assert.Equal(ReplaySaveCode.Saved, saved.RivalCode);
        Assert.Equal(ReplayVerificationCode.Verified, saved.RivalReplayVerificationCode);
        var failure = Assert.Throws<McpException>(() => tools.ObserveMatch("bad"));
        Assert.Equal("The match handle is invalid. (Parameter 'value')", failure.Message);
        Assert.Throws<ArgumentNullException>(() => new McpAgentTools(null!));
    }

    [Fact]
    public void Mcp_tool_metadata_declares_closed_world_and_structured_outputs()
    {
        var methods = typeof(McpAgentTools).GetMethods()
            .Select(method => new
            {
                Method = method,
                Tool = method.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
                    .Cast<McpServerToolAttribute>()
                    .SingleOrDefault(),
            })
            .Where(value => value.Tool is not null)
            .ToArray();

        Assert.Equal(
            [
                "finish_match",
                "get_match_result",
                "observe_match",
                "play_move",
                "save_verified_replay",
                "start_match",
            ],
            methods.Select(value => value.Tool!.Name!).Order().ToArray());
        Assert.All(methods, value =>
        {
            Assert.False(value.Tool!.OpenWorld);
            Assert.True(value.Tool.UseStructuredContent);
            Assert.NotNull(value.Tool.OutputSchemaType);
        });
        Assert.True(methods.Single(value => value.Tool!.Name == "observe_match").Tool!.ReadOnly);
        Assert.True(methods.Single(value => value.Tool!.Name == "get_match_result").Tool!.ReadOnly);
        Assert.False(methods.Single(value => value.Tool!.Name == "save_verified_replay").Tool!.Destructive);
        Assert.False(methods.Single(value => value.Tool!.Name == "start_match").Tool!.Idempotent);
        Assert.True(methods.Single(value => value.Tool!.Name == "play_move").Tool!.Idempotent);
    }

    [Fact]
    public void Resources_publish_closed_rules_official_modes_and_playbook()
    {
        using var rules = JsonDocument.Parse(AgentResources.GetRules());
        using var modes = JsonDocument.Parse(AgentResources.GetModes());
        var playbook = AgentResources.GetPlaybook();

        Assert.Equal(
            "vibesnake-agent-rules-resource-v1",
            rules.RootElement.GetProperty("contract").GetString());
        Assert.Equal(
            AgentMatchOptions.MaximumAllowedSteps,
            rules.RootElement.GetProperty("maximum_steps").GetInt32());
        Assert.Equal(
            ["continue", "up", "right", "down", "left"],
            rules.RootElement.GetProperty("actions")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());
        Assert.Equal(
            ["undeclared", "seek_food", "seek_power", "preserve_space", "take_risk", "recover"],
            rules.RootElement.GetProperty("public_intents")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());
        using var styles = JsonDocument.Parse(AgentResources.GetStyles());
        using var school = JsonDocument.Parse(AgentResources.GetSignalSchool());
        using var rivals = JsonDocument.Parse(AgentResources.GetRivals());
        Assert.Equal(2, modes.RootElement.GetProperty("modes").GetArrayLength());
        Assert.Equal(
            AgentStyleContractCatalog.All.Count,
            styles.RootElement.GetProperty("styles").GetArrayLength());
        Assert.Equal(
            AgentSignalSchoolCatalog.All.Count,
            school.RootElement.GetProperty("lessons").GetArrayLength());
        Assert.Equal(
            AiPersonalityCatalog.BuiltIn.Count,
            rivals.RootElement.GetProperty("rivals").GetArrayLength());
        Assert.Contains("start_match", playbook, StringComparison.Ordinal);
        Assert.Contains("save_verified_replay", playbook, StringComparison.Ordinal);
        Assert.DoesNotContain("chain of thought", AgentResources.GetRules(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Host_paths_match_each_Godot_platform_layout()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "agent-host-path"));

        var windows = AgentHostDataPaths.ResolveGodotUserDataRoot(
            root,
            AgentHostPlatform.Windows);
        var mac = AgentHostDataPaths.ResolveGodotUserDataRoot(
            root,
            AgentHostPlatform.MacOS);
        var linux = AgentHostDataPaths.ResolveGodotUserDataRoot(
            root,
            AgentHostPlatform.Linux);

        Assert.EndsWith(
            Path.Combine("Godot", "app_userdata", "Vibe Snake"),
            windows,
            StringComparison.Ordinal);
        Assert.Equal(windows, mac);
        Assert.EndsWith(
            Path.Combine("godot", "app_userdata", "Vibe Snake"),
            linux,
            StringComparison.Ordinal);
        Assert.True(Path.IsPathFullyQualified(AgentHostDataPaths.ResolveGodotUserDataRoot()));
        Assert.Throws<InvalidOperationException>(() =>
            AgentHostDataPaths.ResolveGodotUserDataRoot("relative", AgentHostPlatform.Linux));
        Assert.Throws<InvalidOperationException>(() =>
            AgentHostDataPaths.ResolveGodotUserDataRoot(" ", AgentHostPlatform.Linux));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentHostDataPaths.ResolveGodotUserDataRoot(root, (AgentHostPlatform)255));
    }

    [Fact]
    public void Host_builder_uses_strict_snake_case_protocol_serialization()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var options = Program.CreateSerializerOptions();
        var json = JsonSerializer.Serialize(
            new AgentMatchResultStatusV1(
                AgentMatchResultStatusV1.Contract,
                "match_test",
                IsAvailable: false,
                Result: null),
            options);

        Assert.Contains("\"match_handle\"", json, StringComparison.Ordinal);
        Assert.Contains("\"is_available\"", json, StringComparison.Ordinal);
        Assert.False(options.PropertyNameCaseInsensitive);
        var builder = Program.CreateHostApplicationBuilder([], temporary.Path);
        using var host = builder.Build();
        var defaultBuilder = Program.CreateHostApplicationBuilder([]);
        using var defaultHost = defaultBuilder.Build();
        Assert.NotNull(host.Services);
        Assert.NotNull(defaultHost.Services);
        Assert.Equal("vibesnake-agent-host", Program.HostName);
        Assert.Equal("0.1.0", Program.HostVersion);
        Assert.Throws<ArgumentNullException>(() =>
            Program.CreateHostApplicationBuilder(null!, temporary.Path));
    }

    [Fact]
    public void Host_summary_and_save_contracts_are_complete()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var registry = CreateRegistry(temporary.Path);
        var started = registry.StartMatch(
            RunModeCatalog.VibeId,
            AgentSeedVisibility.Open,
            "123",
            maximumSteps: 1);
        registry.PlayMove(
            started.MatchHandle,
            "move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Continue);
        var result = registry.GetResult(started.MatchHandle).Result!;
        var saved = registry.SaveVerifiedReplay(started.MatchHandle);

        Assert.Equal(AgentMatchSummaryV1.Contract, result.Schema);
        Assert.Equal(AgentMatchLifecycle.Completed, result.Lifecycle);
        Assert.Equal(AgentMatchEndReason.StepLimit, result.EndReason);
        Assert.Equal(RulesetIdentity.CurrentId, result.RulesetId);
        Assert.Equal(RulesetIdentity.CurrentVersion, result.RulesVersion);
        Assert.Equal(RunModeCatalog.VibeId, result.ModeId);
        Assert.Equal(RunModeCatalog.CurrentModeVersion, result.ModeVersion);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, result.ConfigHashAlgorithm);
        Assert.Equal(started.Observation.ConfigHash, result.ConfigHash);
        Assert.Equal(AgentSeedVisibility.Open, result.SeedVisibility);
        Assert.Equal("123", result.GameplaySeed);
        Assert.Equal(1, result.FinalTick);
        Assert.Equal(RunStatus.Running, result.RunStatus);
        Assert.Equal(DeathCause.None, result.DeathCause);
        Assert.Equal(0, result.Score);
        Assert.Equal(started.Observation.MatchId, result.MatchHandle);
        Assert.NotEqual(started.Observation.StateHash, result.FinalStateHash);
        Assert.Equal(64, result.ReplayPayloadHash.Length);
        Assert.Equal(ReplayVerificationCode.Verified, result.ReplayVerificationCode);
        Assert.Equal(AgentEpisodeMetricsV1.Contract, result.EpisodeMetrics.Schema);
        Assert.Equal(1, result.EpisodeMetrics.SurvivalSteps);
        Assert.Null(result.StyleContract);
        Assert.Equal(AgentReplaySaveV1.Contract, saved.Schema);
        Assert.Equal(result.MatchHandle, saved.MatchHandle);
        Assert.NotEmpty(saved.Message);

        var terminalRegistry = CreateRegistry(
            temporary.Path,
            handle: "match_terminal",
            seed: 456UL);
        var terminalStarted = terminalRegistry.StartMatch(
                RunModeCatalog.ClassicId,
                AgentSeedVisibility.Open,
                "456",
                maximumSteps: 1);
        var terminalResponse = terminalRegistry.PlayMove(
            terminalStarted.MatchHandle,
            "terminal-move",
            terminalStarted.Observation.Tick,
            terminalStarted.Observation.StateHash,
            AgentAction.Continue);
        Assert.NotNull(terminalResponse.MatchResult);
        Assert.DoesNotContain(
            terminalResponse.MatchResult!.GetType().GetProperties(),
            property => property.Name == nameof(AgentMatchResult.VerifiedReplay));
    }

    [Fact]
    public async Task Stdio_host_negotiates_current_MCP_and_completes_a_golden_transcript()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var hostAssembly = typeof(Program).Assembly.Location;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Vibe Snake Agent Host Test",
            Command = "dotnet",
            Arguments = [hostAssembly],
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        });
        await using var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ProtocolVersion = "2026-07-28",
                ClientInfo = new Implementation
                {
                    Name = "vibesnake-agent-host-tests",
                    Version = "1.0.0",
                },
            },
            cancellationToken: timeout.Token);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var resources = await client.ListResourcesAsync(cancellationToken: timeout.Token);
        var rules = await client.ReadResourceAsync(
            "vibesnake://agent/rules",
            cancellationToken: timeout.Token);
        var started = await client.CallToolAsync(
            "start_match",
            new Dictionary<string, object?>
            {
                ["modeId"] = "classic",
                ["seedVisibility"] = "open",
                ["gameplaySeed"] = "456",
                ["maximumSteps"] = 2,
                ["styleContractId"] = AgentStyleContractCatalog.StillwaterId,
                ["rivalPersonalityId"] = "optimal",
                ["passport"] = new Dictionary<string, object?>
                {
                    ["schema"] = AgentPassportV1.Contract,
                    ["agent_id"] = "golden-agent",
                    ["policy_version"] = "policy-1",
                    ["display_name"] = "Golden Agent",
                    ["color"] = "#64FFFF",
                    ["shed_id"] = "agent-default",
                    ["station_affinity"] = "open-frequency",
                    ["observation_profile"] = AgentPassportV1.SymbolicStepObservationProfile,
                    ["action_profile"] = AgentPassportV1.FourDirectionActionProfile,
                },
            },
            cancellationToken: timeout.Token);
        var startDiagnostic = string.Join(
            " | ",
            started.Content.OfType<TextContentBlock>().Select(content => content.Text));
        Assert.False(started.IsError ?? false, startDiagnostic);
        Assert.True(started.StructuredContent.HasValue, startDiagnostic);
        var startedJson = Assert.IsType<JsonElement>(started.StructuredContent);
        var handle = startedJson.GetProperty("match_handle").GetString()!;
        var observation = startedJson.GetProperty("observation");
        Assert.Equal(
            "golden-agent",
            observation.GetProperty("passport").GetProperty("agent_id").GetString());
        Assert.Equal(
            AgentStyleContractCatalog.StillwaterId,
            observation.GetProperty("style_contract").GetProperty("contract_id").GetString());
        Assert.Equal(
            "optimal",
            observation.GetProperty("rival").GetProperty("personality_id").GetString());
        var moved = await client.CallToolAsync(
            "play_move",
            new Dictionary<string, object?>
            {
                ["matchHandle"] = handle,
                ["idempotencyKey"] = "golden-1",
                ["expectedTick"] = observation.GetProperty("tick").GetInt32(),
                ["expectedStateHash"] = observation.GetProperty("state_hash").GetString(),
                ["action"] = "up",
                ["declaredIntent"] = "preserve_space",
            },
            cancellationToken: timeout.Token);
        var finished = await client.CallToolAsync(
            "finish_match",
            new Dictionary<string, object?> { ["matchHandle"] = handle },
            cancellationToken: timeout.Token);

        Assert.Equal("2026-07-28", client.NegotiatedProtocolVersion);
        Assert.Null(client.SessionId);
        Assert.Equal(
            [
                "finish_match",
                "get_match_result",
                "observe_match",
                "play_move",
                "save_verified_replay",
                "start_match",
            ],
            tools.Select(tool => tool.Name).Order().ToArray());
        Assert.Equal(6, resources.Count);
        Assert.Contains(
            resources,
            resource => resource.Uri == "vibesnake://agent/playbook");
        Assert.Contains(
            resources,
            resource => resource.Uri == "vibesnake://agent/signal-school");
        Assert.Contains(
            resources,
            resource => resource.Uri == "vibesnake://agent/styles");
        Assert.Contains(
            resources,
            resource => resource.Uri == "vibesnake://agent/rivals");
        var rulesText = Assert.IsType<TextResourceContents>(Assert.Single(rules.Contents));
        Assert.Contains(
            "vibesnake-agent-rules-resource-v1",
            rulesText.Text,
            StringComparison.Ordinal);
        Assert.False(moved.IsError ?? false);
        Assert.True(moved.StructuredContent!.Value.GetProperty("accepted").GetBoolean());
        Assert.Equal(
            "preserve_space",
            moved.StructuredContent.Value
                .GetProperty("observation")
                .GetProperty("previous_action")
                .GetProperty("declared_intent")
                .GetString());
        Assert.False(finished.IsError ?? false);
        Assert.Equal(
            "agent_finished",
            finished.StructuredContent!.Value.GetProperty("end_reason").GetString());
    }

    [Fact]
    public async Task Named_pipe_viewer_is_one_time_read_only_and_receives_latest_frames()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var temporary = new AgentHostTemporaryDirectory();
        using var registry = CreateRegistry(temporary.Path);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            "654",
            maximumSteps: 2,
            watchEnabled: true);
        var connection = Assert.IsType<AgentViewerConnectionV1>(started.Viewer);
        Assert.Equal(AgentViewerConnectionV1.Contract, connection.Schema);
        Assert.Equal("named-pipe", connection.Transport);
        Assert.NotEmpty(connection.RetentionPolicy);

        await using var pipe = new NamedPipeClientStream(
            ".",
            connection.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        var token = Encoding.ASCII.GetBytes(connection.AccessToken + "\n");
        await pipe.WriteAsync(token, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var initialLine = await reader.ReadLineAsync(timeout.Token);
        using var initialFrame = JsonDocument.Parse(Assert.IsType<string>(initialLine));
        Assert.Equal(0, initialFrame.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(0, initialFrame.RootElement.GetProperty("observation").GetProperty("tick").GetInt32());

        _ = registry.PlayMove(
            started.MatchHandle,
            "view-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Up);
        var movedLine = await reader.ReadLineAsync(timeout.Token);
        using var movedFrame = JsonDocument.Parse(Assert.IsType<string>(movedLine));
        Assert.Equal(1, movedFrame.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(1, movedFrame.RootElement.GetProperty("observation").GetProperty("tick").GetInt32());
        Assert.Equal(1, registry.Observe(started.MatchHandle).Tick);
    }

    [Fact]
    public void Viewer_server_validates_capabilities_and_registry_disposal()
    {
        Assert.ThrowsAny<ArgumentException>(() => new AgentViewerServer("bad pipe", [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentViewerServer("valid", []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentViewerServer("valid", new byte[65]));

        using var server = new AgentViewerServer("valid_" + Guid.NewGuid().ToString("N"), [1, 2, 3]);
        Assert.Throws<ArgumentNullException>(() => server.TryPublish(null!));
        Assert.True(server.TryPublish(new AgentViewerFrameV1(
            AgentViewerFrameV1.Contract,
            0,
            new AgentMatchSession(new AgentMatchOptions(
                "frame",
                RunModeCatalog.ClassicId,
                RunModeCatalog.CurrentModeVersion,
                1UL,
                AgentSeedVisibility.Open)).Observe())));
        server.Dispose();
        Assert.False(server.TryPublish(new AgentViewerFrameV1(
            AgentViewerFrameV1.Contract,
            1,
            new AgentMatchSession(new AgentMatchOptions(
                "frame-two",
                RunModeCatalog.ClassicId,
                RunModeCatalog.CurrentModeVersion,
                2UL,
                AgentSeedVisibility.Open)).Observe())));

        using var temporary = new AgentHostTemporaryDirectory();
        var registry = CreateRegistry(temporary.Path);
        registry.Dispose();
        registry.Dispose();
        Assert.Throws<ObjectDisposedException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null));
        Assert.Throws<ObjectDisposedException>(() => registry.Observe("match_test"));
    }

    private static AgentSessionRegistry CreateRegistry(
        string root,
        string handle = "match_test",
        ulong seed = 123UL) =>
        new(
            new ReplayStore(root),
            () => handle,
            () => seed);

    private static void AssertSummary(
        AgentMatchSummaryV1 result,
        string expectedHandle,
        string expectedSeed)
    {
        Assert.Equal(AgentMatchSummaryV1.Contract, result.Schema);
        Assert.Equal(expectedHandle, result.MatchHandle);
        Assert.Equal(expectedSeed, result.GameplaySeed);
        Assert.Equal(AgentMatchLifecycle.Completed, result.Lifecycle);
        Assert.Equal(AgentMatchEndReason.StepLimit, result.EndReason);
        Assert.Equal(ReplayVerificationCode.Verified, result.ReplayVerificationCode);
    }

    private sealed class AgentHostTemporaryDirectory : IDisposable
    {
        public AgentHostTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentHostTests",
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
