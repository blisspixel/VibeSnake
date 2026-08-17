using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.AgentViewer;
using VibeSnake.Persistence;
using VibeSnake.Rules;

using static VibeSnake.Rules.Tests.AgentSurvivalTestFacts;

namespace VibeSnake.Rules.Tests;

[Collection(AgentHostIntegrationGroup.Name)]
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

        Assert.Equal(StartAgentMatchV5.Contract, started.Schema);
        Assert.Equal("match_test", started.MatchHandle);
        Assert.Equal(AgentSessionRegistry.RetentionPolicy, started.RetentionPolicy);
        Assert.False(before.IsAvailable);
        Assert.Null(before.Result);
        Assert.True(moved.Accepted);
        Assert.True(after.IsAvailable);
        var result = Assert.IsType<AgentMatchSummaryV5>(after.Result);
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
    public void Registry_starts_only_canonical_signal_school_practice()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        using var registry = CreateRegistry(temporary.Path);
        var lesson = AgentSignalSchoolCatalog.Get("first-turn");

        var started = registry.StartLesson(
            lesson.Id,
            actionProfile: AgentPassportV4.FourDirectionBurstActionProfile);

        Assert.Equal(lesson.ModeId, started.Observation.ModeId);
        Assert.Equal(lesson.PracticeSeed, started.Observation.GameplaySeed);
        Assert.Equal(lesson.MaximumSteps, started.Observation.MaximumSteps);
        Assert.Equal(lesson.Id, started.Observation.LessonProgress!.LessonId);
        Assert.Null(started.Observation.StyleContract);
        Assert.Null(started.Observation.Rival);
        Assert.Throws<ArgumentException>(() => registry.StartLesson("unknown"));
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
    public void Registry_validates_replacement_before_capacity_eviction()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var next = 0;
        using var registry = new AgentSessionRegistry(
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
        _ = registry.Finish(handles[0]);

        Assert.Throws<ArgumentException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null,
            actionProfile: "unsupported-profile"));

        Assert.True(registry.GetResult(handles[0]).IsAvailable);
        Assert.Equal(0, registry.Observe(handles[1]).Tick);
        var replacement = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null);
        Assert.Throws<KeyNotFoundException>(() => registry.Observe(handles[0]));
        Assert.Equal("match_9", replacement.MatchHandle);
    }

    [Fact]
    public void Registry_reclaims_only_expired_live_matches_without_creating_results()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var clock = new ManualTimeProvider();
        var next = 0;
        using var registry = new AgentSessionRegistry(
            new ReplayStore(temporary.Path),
            () => $"match_{next++}",
            () => 1UL,
            clock);
        var handles = Enumerable.Range(0, AgentSessionRegistry.MaximumRetainedMatches)
            .Select(_ => registry.StartMatch(
                RunModeCatalog.ClassicId,
                AgentSeedVisibility.Open,
                null,
                null).MatchHandle)
            .ToArray();

        clock.Advance(TimeSpan.FromMinutes(
            AgentSessionRegistry.LiveMatchIdleLeaseMinutes - 1));
        _ = registry.Observe(handles[0]);
        Assert.Throws<InvalidOperationException>(() => registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null));

        clock.Advance(TimeSpan.FromMinutes(1));
        var replacement = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            null);

        Assert.Equal("match_8", replacement.MatchHandle);
        Assert.Equal(0, registry.Observe(handles[0]).Tick);
        Assert.Throws<KeyNotFoundException>(() => registry.Observe(handles[1]));
        Assert.False(Directory.Exists(Path.Combine(
            temporary.Path,
            ReplayStore.ReplayDirectoryName)));
        Assert.Contains("without a result or replay", AgentSessionRegistry.RetentionPolicy, StringComparison.Ordinal);
        Assert.Contains("Viewer activity never refreshes", AgentSessionRegistry.RetentionPolicy, StringComparison.Ordinal);
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
    public void Mcp_tools_expose_eight_safe_operations_and_sanitized_failures()
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
        using var burstRegistry = CreateRegistry(temporary.Path, handle: "match_burst");
        var burstTools = new McpAgentTools(burstRegistry);
        var burstStarted = burstTools.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            "321",
            4,
            actionProfile: AgentPassportV4.FourDirectionBurstActionProfile);
        var burst = burstTools.PlayBurst(
            burstStarted.MatchHandle,
            "tool-burst",
            burstStarted.Observation.Tick,
            burstStarted.Observation.StateHash,
            AgentAction.Up,
            2,
            AgentPublicIntent.PreserveSpace);

        Assert.True(moved.Accepted);
        Assert.Equal(
            AgentPublicIntent.SeekFood,
            moved.Observation.PreviousAction!.DeclaredIntent);
        Assert.Equal(AgentActionResponseV5.Contract, moved.Schema);
        Assert.Null(moved.MatchResult);
        Assert.False(pending.IsAvailable);
        Assert.Equal(AgentMatchEndReason.AgentFinished, finished.EndReason);
        Assert.True(saved.IsSuccess);
        Assert.NotNull(saved.RivalFileName);
        Assert.Equal(ReplaySaveCode.Saved, saved.RivalCode);
        Assert.Equal(ReplayVerificationCode.Verified, saved.RivalReplayVerificationCode);
        Assert.Equal(AgentBurstResponseV5.Contract, burst.Schema);
        Assert.True(burst.Accepted);
        Assert.Equal(2, burst.StepsAdvanced);
        Assert.Equal(AgentBurstStopReason.RequestedLimit, burst.StopReason);
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
                "get_exhibition_receipt",
                "get_match_result",
                "observe_match",
                "play_burst",
                "play_move",
                "save_verified_replay",
                "start_lesson",
                "start_match",
            ],
            methods.Select(value => value.Tool!.Name!).Order().ToArray());
        Assert.All(methods, value =>
        {
            Assert.False(value.Tool!.OpenWorld);
            Assert.True(value.Tool.UseStructuredContent);
            Assert.Equal(
                value.Tool.Name switch
                {
                    "start_match" or "start_lesson" => typeof(StartAgentMatchV5),
                    "observe_match" => typeof(AgentObservationV5),
                    "get_exhibition_receipt" => typeof(AgentExhibitionReceiptStatusV1),
                    "play_move" => typeof(AgentActionResponseV5),
                    "play_burst" => typeof(AgentBurstResponseV5),
                    "finish_match" => typeof(AgentMatchSummaryV5),
                    "get_match_result" => typeof(AgentMatchResultStatusV5),
                    "save_verified_replay" => typeof(AgentReplaySaveV1),
                    _ => throw new InvalidOperationException(
                        $"Unexpected MCP tool {value.Tool.Name}."),
                },
                value.Tool.OutputSchemaType);
        });
        Assert.True(methods.Single(value => value.Tool!.Name == "observe_match").Tool!.ReadOnly);
        Assert.True(methods.Single(value => value.Tool!.Name == "get_match_result").Tool!.ReadOnly);
        Assert.True(
            methods.Single(value => value.Tool!.Name == "get_exhibition_receipt").Tool!.ReadOnly);
        Assert.False(methods.Single(value => value.Tool!.Name == "save_verified_replay").Tool!.Destructive);
        Assert.False(methods.Single(value => value.Tool!.Name == "start_match").Tool!.Idempotent);
        Assert.False(methods.Single(value => value.Tool!.Name == "start_lesson").Tool!.Idempotent);
        Assert.True(methods.Single(value => value.Tool!.Name == "play_move").Tool!.Idempotent);
        Assert.True(methods.Single(value => value.Tool!.Name == "play_burst").Tool!.Idempotent);
        var moveDescription = methods.Single(value => value.Tool!.Name == "play_move")
            .Method
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), inherit: false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .Single()
            .Description;
        Assert.Contains("including action", moveDescription, StringComparison.Ordinal);
        Assert.Contains("before this tool runs", moveDescription, StringComparison.Ordinal);
        Assert.Contains("recommended_next_tool", moveDescription, StringComparison.Ordinal);
        var burstDescription = methods.Single(value => value.Tool!.Name == "play_burst")
            .Method
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), inherit: false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .Single()
            .Description;
        Assert.Contains("initialAction", burstDescription, StringComparison.Ordinal);
        Assert.Contains("maximumSteps", burstDescription, StringComparison.Ordinal);
        Assert.Contains("before this tool runs", burstDescription, StringComparison.Ordinal);
        Assert.Contains("recommended_next_tool", burstDescription, StringComparison.Ordinal);
        var finishDescription = methods.Single(value => value.Tool!.Name == "finish_match")
            .Method
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), inherit: false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .Single()
            .Description;
        Assert.Contains("requirements satisfied", finishDescription, StringComparison.Ordinal);
        Assert.Contains("not pass/fail grades", finishDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_argument_filter_names_missing_and_unexpected_move_fields()
    {
        static JsonElement JsonValue(object value) => JsonSerializer.SerializeToElement(value);

        Assert.Throws<ArgumentNullException>(() => AgentToolArgumentFilter.Validate(null!));
        Assert.Null(AgentToolArgumentFilter.Validate(new CallToolRequestParams
        {
            Name = "list_leaderboards",
        }));
        Assert.Null(AgentToolArgumentFilter.Validate(new CallToolRequestParams
        {
            Name = "play_move",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["matchHandle"] = JsonValue("match"),
                ["idempotencyKey"] = JsonValue("key"),
                ["expectedTick"] = JsonValue(0),
                ["expectedStateHash"] = JsonValue("hash"),
                ["action"] = JsonValue("continue"),
                ["declaredIntent"] = JsonValue("undeclared"),
            },
        }));

        var missingAll = Assert.IsType<CallToolResult>(
            AgentToolArgumentFilter.Validate(new CallToolRequestParams
            {
                Name = "play_move",
            }));
        var missingAllText = Assert.Single(missingAll.Content.OfType<TextContentBlock>()).Text;
        Assert.True(missingAll.IsError);
        Assert.Contains("missing required argument(s)", missingAllText, StringComparison.Ordinal);
        Assert.Contains("\"action\"", missingAllText, StringComparison.Ordinal);
        Assert.Contains("No match state changed", missingAllText, StringComparison.Ordinal);

        var wrongMove = Assert.IsType<CallToolResult>(
            AgentToolArgumentFilter.Validate(new CallToolRequestParams
            {
                Name = "play_move",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["matchHandle"] = JsonValue("match"),
                    ["idempotencyKey"] = JsonValue("key"),
                    ["expectedTick"] = JsonValue(0),
                    ["expectedStateHash"] = JsonValue("hash"),
                    ["direction"] = JsonValue("up"),
                },
            }));
        var wrongMoveText = Assert.Single(wrongMove.Content.OfType<TextContentBlock>()).Text;
        Assert.Contains("unexpected argument name(s): \"direction\"", wrongMoveText, StringComparison.Ordinal);
        Assert.Contains("missing required argument(s): \"action\"", wrongMoveText, StringComparison.Ordinal);

        var extraBurst = Assert.IsType<CallToolResult>(
            AgentToolArgumentFilter.Validate(new CallToolRequestParams
            {
                Name = "play_burst",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["matchHandle"] = JsonValue("match"),
                    ["idempotencyKey"] = JsonValue("key"),
                    ["expectedTick"] = JsonValue(0),
                    ["expectedStateHash"] = JsonValue("hash"),
                    ["initialAction"] = JsonValue("continue"),
                    ["maximumSteps"] = JsonValue(4),
                    ["action"] = JsonValue("continue"),
                },
            }));
        var extraBurstText = Assert.Single(extraBurst.Content.OfType<TextContentBlock>()).Text;
        Assert.Contains("unexpected argument name(s): \"action\"", extraBurstText, StringComparison.Ordinal);
        Assert.DoesNotContain("missing required argument(s)", extraBurstText, StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_argument_filter_names_wrong_json_types_for_every_tool()
    {
        static JsonElement JsonValue(object? value) => JsonSerializer.SerializeToElement(value);

        Assert.Null(AgentToolArgumentFilter.Validate(new CallToolRequestParams
        {
            Name = "start_match",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["modeId"] = JsonValue("classic"),
                ["seedVisibility"] = JsonValue("open"),
                ["gameplaySeed"] = JsonValue("42"),
                ["maximumSteps"] = JsonValue(120),
                ["styleContractId"] = JsonValue(null),
                ["rivalPersonalityId"] = JsonValue(null),
                ["watchEnabled"] = JsonValue(true),
                ["passport"] = JsonValue(null),
                ["actionProfile"] = JsonValue(AgentPassportV4.FourDirectionActionProfile),
            },
        }));

        var numericSeed = Assert.IsType<CallToolResult>(
            AgentToolArgumentFilter.Validate(new CallToolRequestParams
            {
                Name = "start_match",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["modeId"] = JsonValue("classic"),
                    ["seedVisibility"] = JsonValue("open"),
                    ["gameplaySeed"] = JsonValue(42),
                },
            }));
        var numericSeedText = Assert.Single(numericSeed.Content.OfType<TextContentBlock>()).Text;
        Assert.True(numericSeed.IsError);
        Assert.Contains(
            "wrong argument type(s): \"gameplaySeed\" must be a JSON string or null "
                + "but received a number",
            numericSeedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "Quote a decimal text value, for example \"42\".",
            numericSeedText,
            StringComparison.Ordinal);
        Assert.Contains("No match state changed", numericSeedText, StringComparison.Ordinal);
        Assert.DoesNotContain("missing required", numericSeedText, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected argument", numericSeedText, StringComparison.Ordinal);

        var missingLesson = Assert.IsType<CallToolResult>(
            AgentToolArgumentFilter.Validate(new CallToolRequestParams
            {
                Name = "start_lesson",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["lesson"] = JsonValue("first-turn"),
                },
            }));
        var missingLessonText = Assert.Single(missingLesson.Content.OfType<TextContentBlock>()).Text;
        Assert.Contains(
            "unexpected argument name(s): \"lesson\"",
            missingLessonText,
            StringComparison.Ordinal);
        Assert.Contains(
            "missing required argument(s): \"lessonId\"",
            missingLessonText,
            StringComparison.Ordinal);

        var fractionalTick = Assert.IsType<CallToolResult>(
            AgentToolArgumentFilter.Validate(new CallToolRequestParams
            {
                Name = "play_move",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["matchHandle"] = JsonValue("match"),
                    ["idempotencyKey"] = JsonValue("key"),
                    ["expectedTick"] = JsonValue(1.5),
                    ["expectedStateHash"] = JsonValue("hash"),
                    ["action"] = JsonValue("continue"),
                },
            }));
        Assert.Contains(
            "\"expectedTick\" must be a JSON integer but received a number",
            Assert.Single(fractionalTick.Content.OfType<TextContentBlock>()).Text,
            StringComparison.Ordinal);

        foreach (var readOnlyTool in new[]
        {
            "observe_match",
            "finish_match",
            "get_match_result",
            "get_exhibition_receipt",
            "save_verified_replay",
        })
        {
            Assert.Null(AgentToolArgumentFilter.Validate(new CallToolRequestParams
            {
                Name = readOnlyTool,
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["matchHandle"] = JsonValue("match"),
                },
            }));
            var wrongHandle = Assert.IsType<CallToolResult>(
                AgentToolArgumentFilter.Validate(new CallToolRequestParams
                {
                    Name = readOnlyTool,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["handle"] = JsonValue("match"),
                    },
                }));
            var wrongHandleText = Assert.Single(
                wrongHandle.Content.OfType<TextContentBlock>()).Text;
            Assert.Contains(
                $"Invalid arguments for '{readOnlyTool}'",
                wrongHandleText,
                StringComparison.Ordinal);
            Assert.Contains(
                "unexpected argument name(s): \"handle\"",
                wrongHandleText,
                StringComparison.Ordinal);
            Assert.Contains(
                "missing required argument(s): \"matchHandle\"",
                wrongHandleText,
                StringComparison.Ordinal);
            Assert.Contains("No match state changed", wrongHandleText, StringComparison.Ordinal);
        }

        Assert.Null(AgentToolArgumentFilter.Validate(new CallToolRequestParams
        {
            Name = "unknown_tool",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["anything"] = JsonValue(1),
            },
        }));
    }

    [Fact]
    public void Tool_argument_filter_describes_every_rejected_json_value_kind()
    {
        static JsonElement JsonValue(object? value) => JsonSerializer.SerializeToElement(value);

        static Dictionary<string, JsonElement> StartArguments(
            string name,
            JsonElement value)
        {
            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["modeId"] = JsonValue("classic"),
                ["seedVisibility"] = JsonValue("open"),
            };
            arguments[name] = value;
            return arguments;
        }

        static string Reject(Dictionary<string, JsonElement> arguments)
        {
            var result = Assert.IsType<CallToolResult>(
                AgentToolArgumentFilter.Validate(new CallToolRequestParams
                {
                    Name = "start_match",
                    Arguments = arguments,
                }));
            Assert.True(result.IsError);
            var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
            Assert.Contains("No match state changed", text, StringComparison.Ordinal);
            return text;
        }

        Assert.Contains(
            "\"watchEnabled\" must be a JSON boolean but received a string",
            Reject(StartArguments("watchEnabled", JsonValue("true"))),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"passport\" must be a JSON object or null but received an array",
            Reject(StartArguments("passport", JsonValue(Enumerable.Range(1, 1)))),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"actionProfile\" must be a JSON string but received null",
            Reject(StartArguments("actionProfile", JsonValue(null))),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"maximumSteps\" must be a JSON integer or null but received a string",
            Reject(StartArguments("maximumSteps", JsonValue("12"))),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"modeId\" must be a JSON string but received a boolean",
            Reject(StartArguments("modeId", JsonValue(true))),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"modeId\" must be a JSON string but received an object",
            Reject(StartArguments("modeId", JsonValue(new { unexpected = 1 }))),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"modeId\" must be a JSON string but received an undefined value",
            Reject(StartArguments("modeId", default)),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Quote a decimal text value",
            Reject(StartArguments("modeId", JsonValue(true))),
            StringComparison.Ordinal);

        Assert.Null(AgentToolArgumentFilter.Validate(new CallToolRequestParams
        {
            Name = "start_match",
            Arguments = StartArguments("watchEnabled", JsonValue(false)),
        }));
        Assert.Null(AgentToolArgumentFilter.Validate(new CallToolRequestParams
        {
            Name = "start_match",
            Arguments = StartArguments(
                "passport",
                JsonValue(new { schema = AgentPassportV4.Contract })),
        }));
    }

    [Fact]
    public void Resources_publish_closed_rules_official_modes_and_playbook()
    {
        using var rules = JsonDocument.Parse(AgentResources.GetRules());
        using var modes = JsonDocument.Parse(AgentResources.GetModes());
        using var identity = JsonDocument.Parse(AgentResources.GetIdentity());
        var playbook = AgentResources.GetPlaybook();

        Assert.Equal(
            "vibesnake-agent-rules-resource-v12",
            rules.RootElement.GetProperty("contract").GetString());
        var lifecycleSemantics = rules.RootElement.GetProperty("lifecycle_semantics");
        Assert.Contains(
            "never describes the snake",
            lifecycleSemantics.GetProperty("lifecycle").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "run_status running",
            lifecycleSemantics.GetProperty("run_status").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "never merged",
            lifecycleSemantics.GetProperty("pairing").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "Satisfying requirements never ends a match",
            lifecycleSemantics.GetProperty("is_action_awaited").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "not a grade",
            lifecycleSemantics.GetProperty("requirement_satisfied").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "may keep playing instead",
            lifecycleSemantics.GetProperty("recommended_next_tool").GetString(),
            StringComparison.Ordinal);
        var argumentBinding = rules.RootElement.GetProperty("argument_binding").GetString();
        Assert.Contains("wrong-typed", argumentBinding, StringComparison.Ordinal);
        Assert.Contains("quoted decimal string", argumentBinding, StringComparison.Ordinal);
        Assert.Contains("change no match state", argumentBinding, StringComparison.Ordinal);
        Assert.Equal(
            AgentObservationV5.Contract,
            rules.RootElement.GetProperty("observation_schema").GetString());
        Assert.Equal(
            AgentPassportV4.SymbolicStepObservationProfile,
            rules.RootElement.GetProperty("observation_profile").GetString());
        Assert.Equal(
            AgentPassportV4.Contract,
            rules.RootElement.GetProperty("passport_schema").GetString());
        Assert.Equal(
            AgentMatchResultV5.Contract,
            rules.RootElement.GetProperty("result_schema").GetString());
        Assert.Equal(
            "vibesnake://agent/identity",
            rules.RootElement.GetProperty("identity_resource").GetString());
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
            [
                AgentPassportV4.FourDirectionActionProfile,
                AgentPassportV4.FourDirectionBurstActionProfile,
            ],
            rules.RootElement.GetProperty("action_profiles")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());
        Assert.Equal(
            AgentBurstRequest.MaximumBurstSteps,
            rules.RootElement.GetProperty("burst").GetProperty("maximum_steps").GetInt32());
        var viewer = rules.RootElement.GetProperty("viewer");
        Assert.Equal(
            AgentViewerFrameV9.Contract,
            viewer.GetProperty("frame_contract").GetString());
        Assert.Equal(
            ["initial", "step", "burst", "finish"],
            viewer.GetProperty("operations")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());
        Assert.True(viewer.GetProperty("exact_steps_advanced").GetBoolean());
        Assert.True(viewer.GetProperty("pre_mutation_tick_and_state_hash").GetBoolean());
        Assert.True(viewer.GetProperty("burst_stop_reason_and_event").GetBoolean());
        Assert.True(viewer.GetProperty("monotonic_sequence").GetBoolean());
        Assert.Equal(
            AgentSessionRegistry.LiveMatchIdleLeaseMinutes,
            rules.RootElement.GetProperty("live_match_idle_lease_minutes").GetInt32());
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
            "vibesnake-agent-style-catalog-v3",
            styles.RootElement.GetProperty("contract").GetString());
        Assert.Equal(
            AgentStyleProgressV3.Contract,
            styles.RootElement.GetProperty("progress_schema").GetString());
        Assert.Equal(
            AgentStyleOutcomeV3.Contract,
            styles.RootElement.GetProperty("outcome_schema").GetString());
        var publishedStyles = styles.RootElement.GetProperty("styles").EnumerateArray().ToArray();
        Assert.Equal(
            AgentStyleContractCatalog.All.Select(value => value.Id),
            publishedStyles.Select(value => value.GetProperty("id").GetString()!));
        Assert.All(publishedStyles, style =>
        {
            Assert.Equal(
                AgentStyleContractCatalog.EvaluationPolicyId,
                style.GetProperty("evaluation_policy_id").GetString());
            Assert.Equal(2, style.GetProperty("criteria").GetArrayLength());
            Assert.All(
                style.GetProperty("criteria").EnumerateArray(),
                criterion => Assert.Equal(
                    "at_least",
                    criterion.GetProperty("comparator").GetString()));
        });
        Assert.Contains(
            "rules-advanced-step facts",
            styles.RootElement.GetProperty("semantics").GetProperty("live").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "verified replay",
            styles.RootElement.GetProperty("semantics").GetProperty("terminal").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "ThresholdReached",
            styles.RootElement.GetProperty("semantics").GetProperty("interpretation").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(
            AgentSignalSchoolCatalog.All.Count,
            school.RootElement.GetProperty("lessons").GetArrayLength());
        Assert.Equal(8, AgentSignalSchoolCatalog.All.Count);
        Assert.Equal(
            "vibesnake-agent-signal-school-v4",
            school.RootElement.GetProperty("contract").GetString());
        Assert.Equal(
            AgentSignalSchoolCatalog.EvaluationPolicyId,
            school.RootElement.GetProperty("evaluation_policy").GetString());
        Assert.Equal(
            AgentSignalSchoolCatalog.MaximumAttemptWitnesses,
            school.RootElement.GetProperty("maximum_attempt_witnesses").GetInt32());
        Assert.Equal(
            AgentLessonProgressV3.Contract,
            school.RootElement.GetProperty("progress_schema").GetString());
        Assert.Equal(
            AgentLessonProgressDeltaV2.Contract,
            school.RootElement.GetProperty("delta_schema").GetString());
        Assert.Equal(
            AgentLessonOutcomeV3.Contract,
            school.RootElement.GetProperty("outcome_schema").GetString());
        Assert.Equal(
            AgentLessonRetryDescriptorV1.Contract,
            school.RootElement.GetProperty("retry_schema").GetString());
        var publishedLessons = school.RootElement.GetProperty("lessons")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            [
                "first-turn",
                "wrap-line",
                "hunger-route",
                "exit-route",
                "power-route",
                "recover-route",
                "combo-route",
                "death-read",
            ],
            publishedLessons.Select(value => value.GetProperty("id").GetString()!).ToArray());
        Assert.All(publishedLessons, lesson =>
        {
            Assert.False(string.IsNullOrWhiteSpace(lesson.GetProperty("instruction").GetString()));
            var requirements = lesson.GetProperty("requirements").EnumerateArray().ToArray();
            Assert.Equal(2, requirements.Length);
            Assert.Equal(
                requirements.Length,
                requirements.Select(value => value.GetProperty("id").GetString())
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.All(requirements, requirement =>
            {
                var source = requirement.GetProperty("evidence_source").GetString();
                Assert.True(source is "replay_trace" or "attempt_witness");
            });
        });
        Assert.Contains(
            publishedLessons[0].GetProperty("requirements").EnumerateArray(),
            requirement => requirement.GetProperty("evidence_source").GetString()
                == "attempt_witness");
        Assert.Equal(
            AiPersonalityCatalog.BuiltIn.Count,
            rivals.RootElement.GetProperty("rivals").GetArrayLength());
        Assert.Equal(
            "vibesnake-agent-identity-resource-v3",
            identity.RootElement.GetProperty("contract").GetString());
        Assert.Equal(
            AgentPassportV4.Contract,
            identity.RootElement.GetProperty("passport_schema").GetString());
        Assert.Equal(
            CosmeticSetCatalog.Sets.Select(value => value.Id).ToArray(),
            identity.RootElement.GetProperty("avatars")
                .EnumerateArray()
                .Select(value => value.GetProperty("id").GetString()!)
                .ToArray());
        Assert.Equal(
            AgentAccentCatalog.All.Select(value => value.Id).ToArray(),
            identity.RootElement.GetProperty("accents")
                .EnumerateArray()
                .Select(value => value.GetProperty("id").GetString()!)
                .ToArray());
        Assert.Equal(
            StationIdentityCatalog.All.Select(value => value.Id).ToArray(),
            identity.RootElement.GetProperty("stations")
                .EnumerateArray()
                .Select(value => value.GetProperty("id").GetString()!)
                .ToArray());
        var identitySemantics = identity.RootElement.GetProperty("semantics");
        Assert.Contains(
            "not authenticated",
            identitySemantics.GetProperty("declaration").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "presentation-only",
            identitySemantics.GetProperty("presentation").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "independent",
            identitySemantics.GetProperty("independence").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "not approval",
            identitySemantics.GetProperty("station_boundary").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("start_match", playbook, StringComparison.Ordinal);
        Assert.Contains("start_lesson", playbook, StringComparison.Ordinal);
        Assert.Contains("play_burst", playbook, StringComparison.Ordinal);
        Assert.Contains("recommended_next_tool", playbook, StringComparison.Ordinal);
        Assert.Contains("save_verified_replay", playbook, StringComparison.Ordinal);
        Assert.Contains("vibesnake://agent/identity", playbook, StringComparison.Ordinal);
        var lessonSemantics = school.RootElement.GetProperty("evidence_semantics");
        Assert.Contains(
            "verified replay",
            lessonSemantics.GetProperty("replay").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "distinct from replay schema 1",
            lessonSemantics.GetProperty("attempts").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "fresh session",
            lessonSemantics.GetProperty("failed_closed").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "reached target omits retry guidance",
            lessonSemantics.GetProperty("terminal").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "integer count",
            school.RootElement.GetProperty("practice_semantics").GetString(),
            StringComparison.Ordinal);
        var interactionAccounting = school.RootElement.GetProperty("interaction_accounting");
        Assert.Equal(
            "mcp-tool-arguments-and-structured-response-json-v1",
            interactionAccounting.GetProperty("policy").GetString());
        Assert.Contains(
            "play_move and play_burst",
            interactionAccounting.GetProperty("action_calls").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(
            [
                "mcp_or_json_rpc_framing",
                "logs_or_stderr",
                "viewer_traffic",
                "token_estimates",
            ],
            interactionAccounting.GetProperty("excluded")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());
        var qualificationEvidence = school.RootElement.GetProperty("qualification_evidence");
        Assert.Equal(
            "measured",
            qualificationEvidence.GetProperty("status").GetString());
        Assert.Equal(
            ["start_lesson", "play_move", "play_burst", "finish_match"],
            qualificationEvidence.GetProperty("included_calls")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());
        Assert.Contains(
            "observation-derived maximumSteps between 1 and 16",
            qualificationEvidence.GetProperty("burst_measurement").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "at least six of eight lessons must use fewer",
            qualificationEvidence.GetProperty("regression_policy").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "only required arguments",
            qualificationEvidence.GetProperty("request_policy").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "match_route-{lesson_id}",
            qualificationEvidence.GetProperty("fixture_policy").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(
            ["lesson_id", "action_profile"],
            qualificationEvidence.GetProperty("dimensions")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());
        Assert.Equal(
            [
                "action_calls",
                "request_utf8_bytes",
                "response_utf8_bytes",
                "total_utf8_bytes",
            ],
            qualificationEvidence.GetProperty("measures")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());
        var qualificationObservations = qualificationEvidence.GetProperty("observations")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(16, qualificationObservations.Length);
        Assert.Equal(
            AgentSignalSchoolCatalog.All.SelectMany(lesson => new[]
            {
                $"{lesson.Id}/{AgentPassportV4.FourDirectionActionProfile}",
                $"{lesson.Id}/{AgentPassportV4.FourDirectionBurstActionProfile}",
            }),
            qualificationObservations.Select(value =>
                $"{value.GetProperty("lesson_id").GetString()}/{value.GetProperty("action_profile").GetString()}"));
        Assert.All(qualificationObservations, value => Assert.Equal(
            value.GetProperty("request_utf8_bytes").GetInt32()
                + value.GetProperty("response_utf8_bytes").GetInt32(),
            value.GetProperty("total_utf8_bytes").GetInt32()));
        var replayContract = rules.RootElement.GetProperty("replay").GetString();
        Assert.Contains("verified lane result", replayContract, StringComparison.Ordinal);
        Assert.Contains("other nonterminal early finishes report aborted", replayContract, StringComparison.Ordinal);
        Assert.Contains("not match grades", replayContract, StringComparison.Ordinal);
        Assert.Contains("Failed-closed", replayContract, StringComparison.Ordinal);
        Assert.DoesNotContain("replay receipt", replayContract, StringComparison.Ordinal);
        Assert.Contains("finalize a completed lesson", playbook, StringComparison.Ordinal);
        Assert.Contains("aborted early finish", playbook, StringComparison.Ordinal);
        Assert.Contains("Confirm that finalization returned a verified result", playbook, StringComparison.Ordinal);
        Assert.Contains("canonical accepted-step history", AgentViewerServer.ViewerRetentionPolicy, StringComparison.Ordinal);
        Assert.Contains("bounded attempt evidence", AgentViewerServer.ViewerRetentionPolicy, StringComparison.Ordinal);
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
    public void Signal_school_publishes_exact_canonical_route_interaction_evidence()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var measured = AgentSignalSchoolCatalog.All
            .SelectMany(definition => new[]
            {
                MeasureQualificationRoute(
                    definition,
                    AgentPassportV4.FourDirectionActionProfile,
                    temporary.Path),
                MeasureQualificationRoute(
                    definition,
                    AgentPassportV4.FourDirectionBurstActionProfile,
                    temporary.Path),
            })
            .ToArray();
        using var school = JsonDocument.Parse(AgentResources.GetSignalSchool());
        var evidence = school.RootElement.GetProperty("qualification_evidence");
        var observations = evidence.GetProperty("observations");

        Assert.Equal(16, measured.Length);
        Assert.All(measured, value =>
        {
            Assert.Equal(
                AgentLessonRouteDriver.DriveSession(
                    AgentSignalSchoolCatalog.Get(value.LessonId),
                    value.ActionProfile).Calls.Count,
                value.ActionCalls);
            Assert.True(value.ActionCalls > 0);
            Assert.True(value.RequestUtf8Bytes > 0);
            Assert.True(value.ResponseUtf8Bytes > 0);
            Assert.Equal(
                value.RequestUtf8Bytes + value.ResponseUtf8Bytes,
                value.TotalUtf8Bytes);
        });
        var actionCallsByLesson = measured
            .GroupBy(value => value.LessonId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    value => value.ActionProfile,
                    value => value.ActionCalls,
                    StringComparer.Ordinal),
                StringComparer.Ordinal);
        Assert.All(actionCallsByLesson.Values, actionCalls => Assert.True(
            actionCalls[AgentPassportV4.FourDirectionBurstActionProfile]
                <= actionCalls[AgentPassportV4.FourDirectionActionProfile]));
        Assert.True(actionCallsByLesson.Values.Count(actionCalls =>
            actionCalls[AgentPassportV4.FourDirectionBurstActionProfile]
                < actionCalls[AgentPassportV4.FourDirectionActionProfile]) >= 6);
        Assert.Equal("measured", evidence.GetProperty("status").GetString());
        var measuredElement = JsonSerializer.SerializeToElement(
            measured,
            Program.CreateSerializerOptions());
        Assert.True(
            JsonElement.DeepEquals(measuredElement, observations),
            measuredElement.GetRawText());
    }

    [Fact]
    public void Host_builder_uses_strict_snake_case_protocol_serialization()
    {
        using var temporary = new AgentHostTemporaryDirectory();
        var options = Program.CreateSerializerOptions();
        var json = JsonSerializer.Serialize(
            new AgentMatchResultStatusV5(
                AgentMatchResultStatusV5.Contract,
                "match_test",
                IsAvailable: false,
                Result: null),
            options);

        Assert.Contains("\"match_handle\"", json, StringComparison.Ordinal);
        Assert.Contains("\"is_available\"", json, StringComparison.Ordinal);
        var styleSession = new AgentMatchSession(new AgentMatchOptions(
            "style-wire",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Open,
            maximumSteps: 1,
            styleContractId: AgentStyleContractCatalog.StillwaterId));
        var styleJson = JsonSerializer.Serialize(styleSession.Observe().StyleContract, options);
        Assert.Contains("\"threshold_reached\"", styleJson, StringComparison.Ordinal);
        Assert.Contains("\"thresholds_reached\"", styleJson, StringComparison.Ordinal);
        Assert.Contains("\"all_thresholds_reached\"", styleJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"satisfied\"", styleJson, StringComparison.Ordinal);
        Assert.False(options.PropertyNameCaseInsensitive);
        Assert.False(options.AllowDuplicateProperties);
        Assert.True(options.RespectRequiredConstructorParameters);
        Assert.Equal(JsonUnmappedMemberHandling.Disallow, options.UnmappedMemberHandling);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AgentMatchResultStatusV5>(
            json[..^1] + ",\"legacy_result\":true}",
            options));
        var builder = Program.CreateHostApplicationBuilder([], temporary.Path);
        using var host = builder.Build();
        var defaultBuilder = Program.CreateHostApplicationBuilder([]);
        using var defaultHost = defaultBuilder.Build();
        Assert.NotNull(host.Services);
        Assert.NotNull(defaultHost.Services);
        Assert.Equal("vibesnake-agent-host", Program.HostName);
        Assert.Equal("0.11.0", Program.HostVersion);
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

        Assert.Equal(AgentMatchSummaryV5.Contract, result.Schema);
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
        Assert.Null(result.StyleOutcome);
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
            property => property.Name == nameof(AgentMatchResultV5.VerifiedReplay));
    }

    [Fact]
    public async Task Stdio_host_uses_current_stateless_MCP_and_completes_a_burst_transcript()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var transport = new StdioClientTransport(CreateHostTransportOptions());
        await using var client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ProtocolVersion = Program.McpProtocolVersion,
                InitializationTimeout = TimeSpan.FromSeconds(45),
                DiscoverProbeTimeout = TimeSpan.FromSeconds(30),
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
                ["watchEnabled"] = true,
                ["styleContractId"] = AgentStyleContractCatalog.StillwaterId,
                ["rivalPersonalityId"] = "optimal",
                ["actionProfile"] = AgentPassportV4.FourDirectionBurstActionProfile,
                ["passport"] = new Dictionary<string, object?>
                {
                    ["schema"] = AgentPassportV4.Contract,
                    ["agent_id"] = "golden-agent",
                    ["policy_version"] = "policy-1",
                    ["display_name"] = "Golden Agent",
                    ["avatar_id"] = "redline",
                    ["accent_id"] = "coil-gold",
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
        Assert.True(started.StructuredContent.HasValue, startDiagnostic);
        var startedJson = Assert.IsType<JsonElement>(started.StructuredContent);
        Assert.Equal(StartAgentMatchV5.Contract, startedJson.GetProperty("schema").GetString());
        var handle = startedJson.GetProperty("match_handle").GetString()!;
        var observation = startedJson.GetProperty("observation");
        var viewer = startedJson.GetProperty("viewer");
        using var viewerClient = new AgentViewerClient(
            viewer.GetProperty("pipe_name").GetString()!,
            viewer.GetProperty("access_token").GetString()!);
        var initialViewerFrame = await TakeViewerFrameAsync(viewerClient, minimumSequence: 0);
        Assert.Equal(AgentViewerOperationKind.Initial, initialViewerFrame.Operation);
        Assert.Equal(0, initialViewerFrame.StepsAdvanced);
        Assert.Equal(
            "golden-agent",
            observation.GetProperty("passport").GetProperty("agent_id").GetString());
        Assert.Equal(
            AgentStyleContractCatalog.StillwaterId,
            observation.GetProperty("style_contract").GetProperty("contract_id").GetString());
        Assert.Equal(
            "optimal",
            observation.GetProperty("rival").GetProperty("personality_id").GetString());
        var wrongBurst = await client.CallToolAsync(
            "play_burst",
            new Dictionary<string, object?>
            {
                ["matchHandle"] = handle,
                ["idempotencyKey"] = "wrong-burst-field",
                ["expectedTick"] = observation.GetProperty("tick").GetInt32(),
                ["expectedStateHash"] = observation.GetProperty("state_hash").GetString(),
                ["action"] = "up",
                ["maximumSteps"] = 2,
            },
            cancellationToken: timeout.Token);
        var wrongBurstText = Assert.Single(wrongBurst.Content.OfType<TextContentBlock>()).Text;
        Assert.True(wrongBurst.IsError);
        Assert.Contains("unexpected argument name(s): \"action\"", wrongBurstText, StringComparison.Ordinal);
        Assert.Contains(
            "missing required argument(s): \"initialAction\"",
            wrongBurstText,
            StringComparison.Ordinal);
        Assert.Contains("No match state changed", wrongBurstText, StringComparison.Ordinal);
        var observedAfterWrongBurst = await client.CallToolAsync(
            "observe_match",
            new Dictionary<string, object?> { ["matchHandle"] = handle },
            cancellationToken: timeout.Token);
        var observationAfterWrongBurst = Assert.IsType<JsonElement>(
            observedAfterWrongBurst.StructuredContent);
        Assert.Equal(
            observation.GetProperty("tick").GetInt32(),
            observationAfterWrongBurst.GetProperty("tick").GetInt32());
        Assert.Equal(
            observation.GetProperty("state_hash").GetString(),
            observationAfterWrongBurst.GetProperty("state_hash").GetString());
        var moved = await client.CallToolAsync(
            "play_burst",
            new Dictionary<string, object?>
            {
                ["matchHandle"] = handle,
                ["idempotencyKey"] = "golden-1",
                ["expectedTick"] = observation.GetProperty("tick").GetInt32(),
                ["expectedStateHash"] = observation.GetProperty("state_hash").GetString(),
                ["initialAction"] = "up",
                ["maximumSteps"] = 2,
                ["declaredIntent"] = "preserve_space",
            },
            cancellationToken: timeout.Token);
        var finished = await client.CallToolAsync(
            "finish_match",
            new Dictionary<string, object?> { ["matchHandle"] = handle },
            cancellationToken: timeout.Token);
        var terminalViewerFrame = await TakeViewerFrameAsync(
            viewerClient,
            minimumSequence: 1);
        var lessonStarted = await client.CallToolAsync(
            "start_lesson",
            new Dictionary<string, object?>
            {
                ["lessonId"] = AgentSignalSchoolCatalog.FirstTurnId,
                ["actionProfile"] = AgentPassportV4.FourDirectionActionProfile,
            },
            cancellationToken: timeout.Token);
        var lessonDiagnostic = string.Join(
            " | ",
            lessonStarted.Content.OfType<TextContentBlock>().Select(content => content.Text));
        Assert.False(lessonStarted.IsError ?? false, lessonDiagnostic);
        var lessonJson = Assert.IsType<JsonElement>(lessonStarted.StructuredContent);
        Assert.Equal(StartAgentMatchV5.Contract, lessonJson.GetProperty("schema").GetString());
        var lessonHandle = lessonJson.GetProperty("match_handle").GetString()!;
        var lessonObservation = lessonJson.GetProperty("observation");
        Assert.Equal(
            AgentObservationV5.Contract,
            lessonObservation.GetProperty("schema").GetString());
        var lessonProgress = lessonObservation.GetProperty("lesson_progress");
        Assert.Equal(
            AgentSignalSchoolCatalog.FirstTurnId,
            lessonProgress.GetProperty("lesson_id").GetString());
        Assert.Equal(AgentLessonProgressV3.Contract, lessonProgress.GetProperty("schema").GetString());
        Assert.Equal("live", lessonProgress.GetProperty("evidence_state").GetString());
        Assert.Equal(0, lessonProgress.GetProperty("attempt_evidence_count").GetInt32());
        Assert.Equal(2, lessonProgress.GetProperty("requirements").GetArrayLength());

        var oppositeAction = lessonObservation.GetProperty("direction").GetString() switch
        {
            "up" => "down",
            "right" => "left",
            "down" => "up",
            "left" => "right",
            var direction => throw new Xunit.Sdk.XunitException(
                $"Unexpected first-turn direction {direction}."),
        };
        var legalAction = lessonObservation.GetProperty("direction").GetString() is "up" or "down"
            ? "right"
            : "up";
        var rejectedArguments = new Dictionary<string, object?>
        {
            ["matchHandle"] = lessonHandle,
            ["idempotencyKey"] = "lesson-reversal",
            ["expectedTick"] = lessonObservation.GetProperty("tick").GetInt32(),
            ["expectedStateHash"] = lessonObservation.GetProperty("state_hash").GetString(),
            ["action"] = oppositeAction,
        };
        var wrongLessonMove = await client.CallToolAsync(
            "play_move",
            new Dictionary<string, object?>
            {
                ["matchHandle"] = lessonHandle,
                ["idempotencyKey"] = "wrong-move-field",
                ["expectedTick"] = lessonObservation.GetProperty("tick").GetInt32(),
                ["expectedStateHash"] = lessonObservation.GetProperty("state_hash").GetString(),
                ["direction"] = legalAction,
            },
            cancellationToken: timeout.Token);
        var wrongLessonMoveText = Assert.Single(
            wrongLessonMove.Content.OfType<TextContentBlock>()).Text;
        Assert.True(wrongLessonMove.IsError);
        Assert.Contains(
            "unexpected argument name(s): \"direction\"",
            wrongLessonMoveText,
            StringComparison.Ordinal);
        Assert.Contains(
            "missing required argument(s): \"action\"",
            wrongLessonMoveText,
            StringComparison.Ordinal);
        var observedAfterWrongMove = await client.CallToolAsync(
            "observe_match",
            new Dictionary<string, object?> { ["matchHandle"] = lessonHandle },
            cancellationToken: timeout.Token);
        var observationAfterWrongMove = Assert.IsType<JsonElement>(
            observedAfterWrongMove.StructuredContent);
        Assert.Equal(
            lessonObservation.GetProperty("tick").GetInt32(),
            observationAfterWrongMove.GetProperty("tick").GetInt32());
        Assert.Equal(
            lessonObservation.GetProperty("state_hash").GetString(),
            observationAfterWrongMove.GetProperty("state_hash").GetString());
        var rejectedLessonMove = await client.CallToolAsync(
            "play_move",
            rejectedArguments,
            cancellationToken: timeout.Token);
        var rejectedLessonJson = Assert.IsType<JsonElement>(rejectedLessonMove.StructuredContent);
        Assert.False(rejectedLessonMove.IsError ?? false);
        Assert.Equal(AgentActionResponseV5.Contract, rejectedLessonJson.GetProperty("schema").GetString());
        Assert.False(rejectedLessonJson.GetProperty("accepted").GetBoolean());
        Assert.False(rejectedLessonJson.GetProperty("rules_advanced").GetBoolean());
        Assert.Equal("illegal_direction", rejectedLessonJson.GetProperty("rejection").GetString());
        var rejectionDelta = rejectedLessonJson.GetProperty("lesson_delta");
        Assert.Equal(AgentLessonProgressDeltaV2.Contract, rejectionDelta.GetProperty("schema").GetString());
        Assert.Equal(
            ["opposite_reversal_rejected"],
            rejectionDelta.GetProperty("newly_satisfied_requirement_ids")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());
        Assert.Equal(1, rejectionDelta.GetProperty("attempt_evidence_count").GetInt32());

        var exactRetry = await client.CallToolAsync(
            "play_move",
            rejectedArguments,
            cancellationToken: timeout.Token);
        Assert.False(exactRetry.IsError ?? false);
        Assert.Equal(
            rejectedLessonJson.GetRawText(),
            Assert.IsType<JsonElement>(exactRetry.StructuredContent).GetRawText());

        var afterRejection = rejectedLessonJson.GetProperty("observation");
        var recoveredLessonMove = await client.CallToolAsync(
            "play_move",
            new Dictionary<string, object?>
            {
                ["matchHandle"] = lessonHandle,
                ["idempotencyKey"] = "lesson-recovery",
                ["expectedTick"] = afterRejection.GetProperty("tick").GetInt32(),
                ["expectedStateHash"] = afterRejection.GetProperty("state_hash").GetString(),
                ["action"] = legalAction,
            },
            cancellationToken: timeout.Token);
        Assert.False(recoveredLessonMove.IsError ?? false);
        var recoveredLessonJson = Assert.IsType<JsonElement>(recoveredLessonMove.StructuredContent);
        Assert.True(recoveredLessonJson.GetProperty("accepted").GetBoolean());
        Assert.True(recoveredLessonJson.GetProperty("rules_advanced").GetBoolean());
        Assert.True(
            recoveredLessonJson.GetProperty("lesson_delta")
                .GetProperty("all_requirements_reached_this_mutation")
                .GetBoolean());
        Assert.True(
            recoveredLessonJson.GetProperty("observation")
                .GetProperty("lesson_progress")
                .GetProperty("all_requirements_satisfied")
                .GetBoolean());
        Assert.Equal(
            "finish_match",
            recoveredLessonJson.GetProperty("observation")
                .GetProperty("lesson_progress")
                .GetProperty("recommended_next_tool")
                .GetString());

        var completedLesson = await client.CallToolAsync(
            "finish_match",
            new Dictionary<string, object?> { ["matchHandle"] = lessonHandle },
            cancellationToken: timeout.Token);
        Assert.False(completedLesson.IsError ?? false);
        var completedLessonJson = Assert.IsType<JsonElement>(completedLesson.StructuredContent);
        Assert.Equal(AgentMatchSummaryV5.Contract, completedLessonJson.GetProperty("schema").GetString());
        var completedOutcome = completedLessonJson.GetProperty("lesson_outcome");
        Assert.Equal(AgentLessonOutcomeV3.Contract, completedOutcome.GetProperty("schema").GetString());
        Assert.True(completedOutcome.GetProperty("all_requirements_satisfied").GetBoolean());
        Assert.Equal("target_reached", completedOutcome.GetProperty("review_code").GetString());
        Assert.False(completedOutcome.TryGetProperty("retry_descriptor", out _));
        Assert.Equal(
            completedLessonJson.GetProperty("replay_payload_hash").GetString(),
            completedOutcome.GetProperty("replay_payload_hash").GetString());
        Assert.NotEqual(
            completedOutcome.GetProperty("replay_payload_hash").GetString(),
            completedOutcome.GetProperty("attempt_evidence_hash").GetString());
        Assert.Equal(64, completedOutcome.GetProperty("evidence_hash").GetString()!.Length);

        var incompleteStarted = await client.CallToolAsync(
            "start_lesson",
            new Dictionary<string, object?>
            {
                ["lessonId"] = AgentSignalSchoolCatalog.FirstTurnId,
                ["actionProfile"] = AgentPassportV4.FourDirectionActionProfile,
            },
            cancellationToken: timeout.Token);
        Assert.False(incompleteStarted.IsError ?? false);
        var incompleteStartJson = Assert.IsType<JsonElement>(incompleteStarted.StructuredContent);
        var incompleteHandle = incompleteStartJson.GetProperty("match_handle").GetString()!;
        var incompleteFinished = await client.CallToolAsync(
            "finish_match",
            new Dictionary<string, object?> { ["matchHandle"] = incompleteHandle },
            cancellationToken: timeout.Token);
        Assert.False(incompleteFinished.IsError ?? false);
        var incompleteOutcome = Assert.IsType<JsonElement>(incompleteFinished.StructuredContent)
            .GetProperty("lesson_outcome");
        Assert.False(incompleteOutcome.GetProperty("all_requirements_satisfied").GetBoolean());
        Assert.Equal(
            "opposite_reversal_rejected",
            incompleteOutcome.GetProperty("first_unmet_requirement_id").GetString());
        Assert.Equal(
            "insufficient_attempt_evidence",
            incompleteOutcome.GetProperty("review_code").GetString());
        var retry = incompleteOutcome.GetProperty("retry_descriptor");
        Assert.Equal(AgentLessonRetryDescriptorV1.Contract, retry.GetProperty("schema").GetString());
        Assert.Equal("start_lesson", retry.GetProperty("tool").GetString());
        Assert.True(retry.GetProperty("fresh_session_required").GetBoolean());
        var retriedLesson = await client.CallToolAsync(
            retry.GetProperty("tool").GetString()!,
            new Dictionary<string, object?>
            {
                ["lessonId"] = retry.GetProperty("lesson_id").GetString(),
                ["actionProfile"] = retry.GetProperty("action_profile").GetString(),
            },
            cancellationToken: timeout.Token);
        Assert.False(retriedLesson.IsError ?? false);
        Assert.NotEqual(
            incompleteHandle,
            Assert.IsType<JsonElement>(retriedLesson.StructuredContent)
                .GetProperty("match_handle")
                .GetString());

        var liveReceipt = await client.CallToolAsync(
            "get_exhibition_receipt",
            new Dictionary<string, object?> { ["matchHandle"] = incompleteHandle },
            cancellationToken: timeout.Token);
        Assert.False(liveReceipt.IsError ?? false);
        var liveReceiptJson = Assert.IsType<JsonElement>(liveReceipt.StructuredContent);
        Assert.Equal(
            AgentExhibitionReceiptStatusV1.Contract,
            liveReceiptJson.GetProperty("schema").GetString());

        var finishedReceipt = await client.CallToolAsync(
            "get_exhibition_receipt",
            new Dictionary<string, object?> { ["matchHandle"] = handle },
            cancellationToken: timeout.Token);
        Assert.False(finishedReceipt.IsError ?? false);
        var receiptJson = Assert.IsType<JsonElement>(finishedReceipt.StructuredContent);
        Assert.True(receiptJson.GetProperty("is_available").GetBoolean());
        var receiptBody = receiptJson.GetProperty("receipt");
        Assert.Equal(
            AgentExhibitionReceiptV2.Contract,
            receiptBody.GetProperty("schema").GetString());
        Assert.Equal(
            finished.StructuredContent!.Value.GetProperty("replay_payload_hash").GetString(),
            receiptBody.GetProperty("agent_replay_payload_hash").GetString());
        Assert.Equal(64, receiptBody.GetProperty("receipt_hash").GetString()!.Length);
        Assert.Equal(
            AgentDivisionIdentityV1.Contract,
            receiptBody.GetProperty("division").GetProperty("schema").GetString());
        Assert.False(receiptBody.TryGetProperty("display_time_utc", out _));

        var numericSeedMatch = await client.CallToolAsync(
            "start_match",
            new Dictionary<string, object?>
            {
                ["modeId"] = RunModeCatalog.ClassicId,
                ["seedVisibility"] = "open",
                ["gameplaySeed"] = 42,
            },
            cancellationToken: timeout.Token);
        var numericSeedText = Assert.Single(
            numericSeedMatch.Content.OfType<TextContentBlock>()).Text;
        Assert.True(numericSeedMatch.IsError);
        Assert.Contains(
            "\"gameplaySeed\" must be a JSON string or null but received a number",
            numericSeedText,
            StringComparison.Ordinal);
        Assert.Contains(
            "Quote a decimal text value, for example \"42\".",
            numericSeedText,
            StringComparison.Ordinal);
        Assert.Contains("No match state changed", numericSeedText, StringComparison.Ordinal);
        var quotedSeedMatch = await client.CallToolAsync(
            "start_match",
            new Dictionary<string, object?>
            {
                ["modeId"] = RunModeCatalog.ClassicId,
                ["seedVisibility"] = "open",
                ["gameplaySeed"] = "42",
            },
            cancellationToken: timeout.Token);
        Assert.False(quotedSeedMatch.IsError ?? false);
        Assert.Equal(
            42UL,
            Assert.IsType<JsonElement>(quotedSeedMatch.StructuredContent)
                .GetProperty("observation")
                .GetProperty("gameplay_seed")
                .GetUInt64());

        var legacyPassport = await client.CallToolAsync(
            "start_lesson",
            new Dictionary<string, object?>
            {
                ["lessonId"] = AgentSignalSchoolCatalog.FirstTurnId,
                ["passport"] = new Dictionary<string, object?>
                {
                    ["schema"] = "vibesnake-agent-passport-v3",
                    ["agent_id"] = "legacy-agent",
                    ["policy_version"] = "v1",
                    ["display_name"] = "Legacy Agent",
                    ["avatar_id"] = "redline",
                    ["accent_id"] = "coil-gold",
                    ["station_id"] = "global_coil",
                    ["observation_profile"] = "symbolic-step-v3",
                    ["action_profile"] = AgentPassportV4.FourDirectionActionProfile,
                },
            },
            cancellationToken: timeout.Token);
        Assert.True(legacyPassport.IsError ?? false);
        var mixedPassport = await client.CallToolAsync(
            "start_lesson",
            new Dictionary<string, object?>
            {
                ["lessonId"] = AgentSignalSchoolCatalog.FirstTurnId,
                ["passport"] = new Dictionary<string, object?>
                {
                    ["schema"] = AgentPassportV4.Contract,
                    ["agent_id"] = "mixed-agent",
                    ["policy_version"] = "v1",
                    ["display_name"] = "Mixed Agent",
                    ["avatar_id"] = "redline",
                    ["accent_id"] = "coil-gold",
                    ["station_id"] = "global_coil",
                    ["observation_profile"] = AgentPassportV4.SymbolicStepObservationProfile,
                    ["action_profile"] = AgentPassportV4.FourDirectionActionProfile,
                    ["legacy_name"] = "not allowed",
                },
            },
            cancellationToken: timeout.Token);
        Assert.True(mixedPassport.IsError ?? false);

        Assert.Equal(Program.McpProtocolVersion, client.NegotiatedProtocolVersion);
        Assert.Null(client.SessionId);
        Assert.Equal(
            [
                "finish_match",
                "get_exhibition_receipt",
                "get_match_result",
                "observe_match",
                "play_burst",
                "play_move",
                "save_verified_replay",
                "start_lesson",
                "start_match",
            ],
            tools.Select(tool => tool.Name).Order().ToArray());
        Assert.Equal(7, resources.Count);
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
        Assert.Contains(
            resources,
            resource => resource.Uri == "vibesnake://agent/identity");
        var rulesText = Assert.IsType<TextResourceContents>(Assert.Single(rules.Contents));
        Assert.Contains(
            "vibesnake-agent-rules-resource-v12",
            rulesText.Text,
            StringComparison.Ordinal);
        Assert.False(moved.IsError ?? false);
        Assert.Equal(
            AgentBurstResponseV5.Contract,
            moved.StructuredContent!.Value.GetProperty("schema").GetString());
        Assert.True(moved.StructuredContent!.Value.GetProperty("accepted").GetBoolean());
        Assert.Equal(2, moved.StructuredContent.Value.GetProperty("steps_advanced").GetInt32());
        Assert.Equal(AgentViewerOperationKind.Burst, terminalViewerFrame.Operation);
        Assert.Equal(AgentViewerFrameV9.Contract, terminalViewerFrame.Schema);
        Assert.Equal(2, terminalViewerFrame.StepsAdvanced);
        Assert.Equal(AgentBurstStopReason.MatchStepLimit, terminalViewerFrame.BurstStopReason);
        Assert.Equal(AgentMatchEndReason.StepLimit, terminalViewerFrame.EndReason);
        Assert.True(terminalViewerFrame.VerifiedResultAvailable);
        Assert.Equal(
            "preserve_space",
            moved.StructuredContent.Value
                .GetProperty("observation")
                .GetProperty("previous_action")
                .GetProperty("declared_intent")
                .GetString());
        Assert.False(finished.IsError ?? false);
        Assert.Equal(
            AgentMatchSummaryV5.Contract,
            finished.StructuredContent!.Value.GetProperty("schema").GetString());
        Assert.Equal(
            "step_limit",
            finished.StructuredContent!.Value.GetProperty("end_reason").GetString());
    }

    private static StdioClientTransportOptions CreateHostTransportOptions()
    {
        var packagedRoot = Environment.GetEnvironmentVariable(
            "VIBESNAKE_AGENT_PLUGIN_ROOT");
        if (string.IsNullOrWhiteSpace(packagedRoot))
        {
            var sourceHostAssembly = typeof(Program).Assembly.Location;
            Assert.True(
                File.Exists(sourceHostAssembly),
                $"Agent host assembly is missing: {sourceHostAssembly}");
            return new StdioClientTransportOptions
            {
                Name = "Vibe Snake Agent Host Test",
                Command = "dotnet",
                Arguments = [sourceHostAssembly],
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            };
        }

        var pluginRoot = Path.GetFullPath(packagedRoot);
        var configurationPath = Path.Combine(pluginRoot, "mcp.json");
        Assert.True(
            File.Exists(configurationPath),
            $"Packaged MCP declaration is missing: {configurationPath}");
        using var configuration = JsonDocument.Parse(File.ReadAllText(configurationPath));
        var servers = configuration.RootElement.GetProperty("mcpServers");
        var serverProperties = servers.EnumerateObject().ToArray();
        var server = Assert.Single(serverProperties);
        Assert.Equal("vibesnake-agent", server.Name);
        Assert.Equal("stdio", server.Value.GetProperty("type").GetString());
        var command = Assert.IsType<string>(
            server.Value.GetProperty("command").GetString());
        Assert.DoesNotContain(' ', command);
        var arguments = server.Value.GetProperty("args")
            .EnumerateArray()
            .Select(argument => ExpandPluginRoot(
                Assert.IsType<string>(argument.GetString()),
                pluginRoot))
            .ToArray();
        var workingDirectory = ExpandPluginRoot(
            Assert.IsType<string>(server.Value.GetProperty("cwd").GetString()),
            pluginRoot);
        Assert.Equal("dotnet", command);
        var hostAssembly = Assert.Single(arguments);
        Assert.True(
            File.Exists(hostAssembly),
            $"Declared Agent Host assembly is missing: {hostAssembly}");
        Assert.Equal(pluginRoot, workingDirectory);
        return new StdioClientTransportOptions
        {
            Name = "Vibe Snake Packaged Agent Host Test",
            Command = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };
    }

    private static async Task<AgentViewerFrameV9> TakeViewerFrameAsync(
        AgentViewerClient client,
        long minimumSequence) =>
        (await TakeViewerDeliveryAsync(client, minimumSequence)).Frame;

    private static async Task<(AgentViewerFrameV9 Frame, long CoalescedFrames)>
        TakeViewerDeliveryAsync(
            AgentViewerClient client,
            long minimumSequence)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (client.TryTakeLatest(out var frame, out var coalescedFrames)
                && frame is not null
                && frame.Sequence >= minimumSequence)
            {
                return (frame, coalescedFrames);
            }

            await Task.Delay(10);
        }

        throw new TimeoutException(
            $"Packaged Agent Host viewer did not publish sequence {minimumSequence}: {client.Status}");
    }

    private static string ExpandPluginRoot(string value, string pluginRoot)
    {
        const string placeholder = "${PLUGIN_ROOT}";
        Assert.StartsWith(placeholder, value, StringComparison.Ordinal);
        var relative = value[placeholder.Length..].TrimStart('/', '\\');
        if (relative.Length == 0)
        {
            return pluginRoot;
        }

        var expanded = Path.GetFullPath(Path.Combine(
            pluginRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        Assert.StartsWith(
            pluginRoot + Path.DirectorySeparatorChar,
            expanded,
            StringComparison.OrdinalIgnoreCase);
        return expanded;
    }

    [Fact]
    public async Task Stdio_host_rejects_legacy_initialize_era_protocol_clients()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Vibe Snake Legacy Protocol Rejection Test",
            Command = "dotnet",
            Arguments = [typeof(Program).Assembly.Location],
            ShutdownTimeout = TimeSpan.FromSeconds(2),
        });

        var exception = await Record.ExceptionAsync(() => McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ProtocolVersion = "2025-11-25",
                InitializationTimeout = TimeSpan.FromSeconds(5),
                DiscoverProbeTimeout = TimeSpan.FromSeconds(5),
                ClientInfo = new Implementation
                {
                    Name = "vibesnake-legacy-client-test",
                    Version = "1.0.0",
                },
            },
            cancellationToken: timeout.Token));
        Assert.NotNull(exception);
        Assert.IsNotType<OperationCanceledException>(exception);
        Assert.Equal(
            "ModelContextProtocol.UnsupportedProtocolVersionException",
            exception.GetType().FullName);
        Assert.Contains("protocol", exception.ToString(), StringComparison.OrdinalIgnoreCase);
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
    public async Task Viewer_server_retains_only_the_newest_unsent_frame()
    {
        using var server = new AgentViewerServer(
            "t_" + Guid.NewGuid().ToString("N")[..16],
            [4, 5, 6]);
        var session = new AgentMatchSession(
            new AgentMatchOptions(
                "coalesced-server",
                RunModeCatalog.ClassicId,
                RunModeCatalog.CurrentModeVersion,
                7UL,
                AgentSeedVisibility.Open,
                maximumSteps: 2),
            server);
        var initial = session.Observe();
        var first = session.SubmitAction(new AgentActionRequest(
            "server-first",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up));
        _ = session.SubmitAction(new AgentActionRequest(
            "server-second",
            first.Observation.Tick,
            first.Observation.StateHash,
            AgentAction.Right));
        using var client = new AgentViewerClient(server.PipeName, server.AccessToken);

        var delivery = await TakeViewerDeliveryAsync(client, minimumSequence: 2);

        Assert.Equal(2, delivery.Frame.Sequence);
        Assert.Equal(2, delivery.CoalescedFrames);
        Assert.Equal(AgentViewerOperationKind.Step, delivery.Frame.Operation);
        Assert.Equal(1, delivery.Frame.StepsAdvanced);
        Assert.Equal(AgentMatchEndReason.StepLimit, delivery.Frame.EndReason);
    }

    [Fact]
    public void Viewer_server_validates_capabilities_and_registry_disposal()
    {
        Assert.ThrowsAny<ArgumentException>(() => new AgentViewerServer("bad pipe", [1]));
        Assert.Throws<ArgumentException>(() => new AgentViewerServer(
            new string('a', AgentViewerTransport.MaximumPipeNameLength + 1),
            [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentViewerServer("valid", []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentViewerServer("valid", new byte[65]));

        using var server = new AgentViewerServer(
            "t_" + Guid.NewGuid().ToString("N")[..16],
            [1, 2, 3]);
        Assert.Throws<ArgumentNullException>(() => server.TryPublish(null!));
        var initialObservation = new AgentMatchSession(new AgentMatchOptions(
            "frame",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Open)).Observe();
        Assert.True(server.TryPublish(new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            0,
            AgentViewerOperationKind.Initial,
            StartTick: initialObservation.Tick,
            StartStateHash: initialObservation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            initialObservation,
            SurvivalFor(initialObservation),
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false)));
        server.Dispose();
        var secondObservation = new AgentMatchSession(new AgentMatchOptions(
            "frame-two",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            2UL,
            AgentSeedVisibility.Open)).Observe();
        Assert.False(server.TryPublish(new AgentViewerFrameV9(
            AgentViewerFrameV9.Contract,
            1,
            AgentViewerOperationKind.Initial,
            StartTick: secondObservation.Tick,
            StartStateHash: secondObservation.StateHash,
            StepsAdvanced: 0,
            BurstStopReason: null,
            BurstStopEvent: null,
            secondObservation,
            SurvivalFor(secondObservation),
            AgentMatchEndReason.None,
            VerifiedResultAvailable: false)));

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

    private static SignalSchoolQualificationMeasurement MeasureQualificationRoute(
        AgentSignalLessonDefinitionV2 definition,
        string actionProfile,
        string root)
    {
        var serializerOptions = Program.CreateSerializerOptions();
        var handle = $"match_route-{definition.Id}";
        using var registry = CreateRegistry(root, handle, definition.PracticeSeed);
        var tools = new McpAgentTools(registry);
        var requestUtf8Bytes = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["lessonId"] = definition.Id,
                ["actionProfile"] = actionProfile,
            },
            serializerOptions).Length;
        var started = tools.StartLesson(definition.Id, actionProfile: actionProfile);
        var responseUtf8Bytes = JsonSerializer.SerializeToUtf8Bytes(
            started,
            serializerOptions).Length;
        var actionCalls = 0;
        var observation = started.Observation;
        AgentMatchSummaryV5? result = null;

        AgentMatchSummaryV5? Submit(string key, AgentAction action)
        {
            actionCalls++;
            if (actionProfile == AgentPassportV4.FourDirectionActionProfile)
            {
                requestUtf8Bytes += JsonSerializer.SerializeToUtf8Bytes(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["matchHandle"] = handle,
                        ["idempotencyKey"] = key,
                        ["expectedTick"] = observation.Tick,
                        ["expectedStateHash"] = observation.StateHash,
                        ["action"] = action,
                    },
                    serializerOptions).Length;
                var response = tools.PlayMove(
                    handle,
                    key,
                    observation.Tick,
                    observation.StateHash,
                    action);
                responseUtf8Bytes += JsonSerializer.SerializeToUtf8Bytes(
                    response,
                    serializerOptions).Length;
                observation = response.Observation;
                return response.MatchResult;
            }

            Assert.Equal(AgentPassportV4.FourDirectionBurstActionProfile, actionProfile);
            var maximumSteps = AgentLessonRouteDriver.ChooseBurstMaximumSteps(
                definition.Id,
                observation,
                action);
            requestUtf8Bytes += JsonSerializer.SerializeToUtf8Bytes(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["matchHandle"] = handle,
                    ["idempotencyKey"] = key,
                    ["expectedTick"] = observation.Tick,
                    ["expectedStateHash"] = observation.StateHash,
                    ["initialAction"] = action,
                    ["maximumSteps"] = maximumSteps,
                },
                serializerOptions).Length;
            var burst = tools.PlayBurst(
                handle,
                key,
                observation.Tick,
                observation.StateHash,
                action,
                maximumSteps);
            responseUtf8Bytes += JsonSerializer.SerializeToUtf8Bytes(
                burst,
                serializerOptions).Length;
            observation = burst.Observation;
            return burst.MatchResult;
        }

        var keyPrefix = $"route-{definition.Id}";
        if (definition.Id == AgentSignalSchoolCatalog.FirstTurnId)
        {
            result = Submit(
                $"{keyPrefix}-reversal",
                AgentLessonRouteDriver.OppositeAction(observation));
        }

        for (var step = 0; step < definition.MaximumSteps && result is null; step++)
        {
            if (observation.LessonProgress!.AllRequirementsSatisfied)
            {
                break;
            }

            result = Submit(
                $"{keyPrefix}-{step}",
                AgentLessonRouteDriver.ChooseAction(definition.Id, observation));
        }

        requestUtf8Bytes += JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["matchHandle"] = handle,
            },
            serializerOptions).Length;
        var finished = tools.FinishMatch(handle);
        responseUtf8Bytes += JsonSerializer.SerializeToUtf8Bytes(
            finished,
            serializerOptions).Length;
        Assert.True(
            finished.LessonOutcome!.AllRequirementsSatisfied,
            $"{definition.Id}/{actionProfile}: first unmet "
            + finished.LessonOutcome.FirstUnmetRequirementId);
        Assert.Equal(AgentLessonReviewCode.TargetReached, finished.LessonOutcome.ReviewCode);
        return new SignalSchoolQualificationMeasurement(
            definition.Id,
            actionProfile,
            actionCalls,
            requestUtf8Bytes,
            responseUtf8Bytes,
            checked(requestUtf8Bytes + responseUtf8Bytes));
    }

    private static void AssertSummary(
        AgentMatchSummaryV5 result,
        string expectedHandle,
        string expectedSeed)
    {
        Assert.Equal(AgentMatchSummaryV5.Contract, result.Schema);
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

    private sealed record SignalSchoolQualificationMeasurement(
        string LessonId,
        string ActionProfile,
        int ActionCalls,
        int RequestUtf8Bytes,
        int ResponseUtf8Bytes,
        int TotalUtf8Bytes);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
