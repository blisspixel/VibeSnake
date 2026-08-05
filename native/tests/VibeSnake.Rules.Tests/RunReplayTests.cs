using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class RunReplayTests
{
    [Fact]
    public void Capture_round_trips_and_verifies_without_mutating_the_source_run()
    {
        var initial = SnakeRun.Create(
            1234UL,
            new RunConfig(Width: 12, Height: 8, StarvationTicks: 100));
        var initialHash = initial.ComputeStateHash();
        IReadOnlyList<Direction>[] commands =
        [
            [Direction.Up],
            [],
            [Direction.Left, Direction.Down],
            [Direction.Right],
            [],
        ];

        var replay = RunReplay.Capture(
            initial,
            commands,
            checkpointInterval: 2);
        var serialized = replay.Serialize();
        var read = RunReplay.Read(serialized);

        Assert.Equal(initialHash, initial.ComputeStateHash());
        Assert.Equal([0, 2, 4, 5], replay.Checkpoints.Select(value => value.StepIndex));
        Assert.Equal(5, replay.Outcome.StepCount);
        Assert.Equal(5, replay.Outcome.FinalTick);
        Assert.False(replay.Outcome.IsTerminal);
        Assert.Equal(64, replay.PayloadHash.Length);
        Assert.True(read.Compatibility.IsCompatible);
        Assert.NotNull(read.Replay);
        Assert.Equal(serialized, read.Replay.Serialize());
        Assert.Equal(replay.PayloadHash, read.Replay.PayloadHash);

        var verification = read.Replay.Verify();
        Assert.True(verification.IsValid, verification.Message);
        Assert.Equal(ReplayVerificationCode.Verified, verification.Code);
        Assert.Null(verification.FirstDivergentStep);
        Assert.Null(verification.ExpectedStateHash);
        Assert.Null(verification.ActualStateHash);

        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;
        Assert.Equal(RunReplay.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(RunReplay.Kind, root.GetProperty("kind").GetString());
        Assert.Equal(
            JsonValueKind.Object,
            root.GetProperty("initialState").ValueKind);
        Assert.Equal(
            RunReplay.IntegrityAlgorithmId,
            root.GetProperty("integrity").GetProperty("algorithm").GetString());
        Assert.Equal(initial.ConfigHash, root.GetProperty("configHash").GetString());
        Assert.Equal(
            RunConfig.ConfigHashAlgorithmId,
            root.GetProperty("configHashAlgorithm").GetString());
        Assert.Equal(initial.ConfigHash, replay.ConfigHash);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, replay.ConfigHashAlgorithm);
    }

    [Fact]
    public void Capture_with_recorder_app_version_round_trips()
    {
        var initial = SnakeRun.Create(55UL);
        var recorder = new RunReplayRecorder(initial, checkpointInterval: 1, appVersion: "0.2.1");
        Assert.True(recorder.TryRecordCommand(Direction.Up));
        initial.QueueDirection(Direction.Up);
        var result = initial.Step();
        Assert.True(recorder.TryCompleteStep(result, initial));
        var finalized = recorder.Finish(initial);
        Assert.True(finalized.IsSuccessful);
        Assert.NotNull(finalized.Replay);
        Assert.Equal("0.2.1", finalized.Replay.AppVersion);

        var serialized = finalized.Replay.Serialize();
        var read = RunReplay.Read(serialized);
        Assert.True(read.Compatibility.IsCompatible);
        Assert.Equal("0.2.1", read.Replay!.AppVersion);
        using var document = JsonDocument.Parse(serialized);
        Assert.Equal("0.2.1", document.RootElement.GetProperty("appVersion").GetString());
    }

    [Fact]
    public void Offline_Capture_accepts_optional_app_version()
    {
        var initial = SnakeRun.Create(56UL);
        var replay = RunReplay.Capture(
            initial,
            [[Direction.Up], []],
            checkpointInterval: 1,
            appVersion: "0.2.1-test");

        Assert.Equal("0.2.1-test", replay.AppVersion);
        Assert.True(replay.Verify().IsValid);
        Assert.Equal("0.2.1-test", RunReplay.Read(replay.Serialize()).Replay!.AppVersion);
    }

    [Fact]
    public void Offline_Capture_omits_app_version_when_not_supplied()
    {
        var initial = SnakeRun.Create(57UL);
        var replay = RunReplay.Capture(
            initial,
            [[Direction.Up]],
            checkpointInterval: 1);

        Assert.Null(replay.AppVersion);
        using var document = JsonDocument.Parse(replay.Serialize());
        Assert.False(document.RootElement.TryGetProperty("appVersion", out _));
        Assert.Null(RunReplay.Read(replay.Serialize()).Replay!.AppVersion);
    }

    [Fact]
    public void Recorder_rejects_blank_or_oversized_app_version()
    {
        var initial = SnakeRun.Create(58UL);
        Assert.Throws<ArgumentException>(
            () => new RunReplayRecorder(initial, appVersion: " "));
        Assert.Throws<ArgumentException>(
            () => new RunReplayRecorder(initial, appVersion: new string('x', 65)));
    }

    [Fact]
    public void Verify_rejects_config_identity_mismatch_on_restore()
    {
        var initial = SnakeRun.Create(4242UL, new RunConfig(Width: 10, Height: 8));
        var valid = RunReplay.Capture(
            initial,
            Array.Empty<IReadOnlyList<Direction>>());
        var mismatched = RunReplay.CreateForTesting(
            valid.InitialCanonicalState,
            valid.Steps,
            valid.CheckpointInterval,
            valid.Checkpoints,
            valid.Outcome,
            configHash: new string('a', 64),
            configHashAlgorithm: RunConfig.ConfigHashAlgorithmId);

        var verification = mismatched.Verify();

        Assert.False(verification.IsValid);
        Assert.Equal(
            ReplayVerificationCode.ConfigIdentityDiverged,
            verification.Code);
        Assert.Equal(0, verification.FirstDivergentStep);
    }

    [Fact]
    public void Capture_preserves_rejected_and_abusive_logical_commands()
    {
        var initial = SnakeRun.Create(77UL);
        IReadOnlyList<Direction>[] commands =
        [
            [Direction.Right, Direction.Left, Direction.Up, Direction.Down],
            [Direction.Up, Direction.Left, Direction.Right],
        ];

        var replay = RunReplay.Capture(initial, commands, checkpointInterval: 1);

        Assert.Equal(commands[0], replay.Steps[0].Commands);
        Assert.Equal(commands[1], replay.Steps[1].Commands);
        Assert.True(replay.Verify().IsValid);
        Assert.Equal(
            replay.Serialize(),
            RunReplay.Capture(initial, commands, checkpointInterval: 1).Serialize());
    }

    [Fact]
    public void Capture_supports_an_empty_in_progress_replay()
    {
        var initial = SnakeRun.Create(88UL);
        var replay = RunReplay.Capture(
            initial,
            Array.Empty<IReadOnlyList<Direction>>());

        Assert.Empty(replay.Steps);
        Assert.Single(replay.Checkpoints);
        Assert.Equal(0, replay.Outcome.StepCount);
        Assert.Equal(RunStatus.Running, replay.Outcome.Status);
        Assert.True(replay.Verify().IsValid);
    }

    [Fact]
    public void Capture_rejects_terminal_origins_and_actions_after_terminal_outcomes()
    {
        var terminal = SnakeRun.Create(
            99UL,
            new RunConfig(StarvationTicks: 1));
        terminal.Step();
        Assert.Equal(RunStatus.Dead, terminal.Status);
        Assert.Throws<ArgumentException>(
            () => RunReplay.Capture(
                terminal,
                Array.Empty<IReadOnlyList<Direction>>()));

        var initial = SnakeRun.Create(
            100UL,
            new RunConfig(StarvationTicks: 1));
        IReadOnlyList<Direction>[] tooManySteps = [[], []];
        Assert.Throws<ArgumentException>(
            () => RunReplay.Capture(initial, tooManySteps));
    }

    [Fact]
    public void Capture_rejects_invalid_checkpoint_intervals()
    {
        var initial = SnakeRun.Create(101UL);
        var commands = Array.Empty<IReadOnlyList<Direction>>();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => RunReplay.Capture(initial, commands, checkpointInterval: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RunReplay.Capture(
                initial,
                commands,
                checkpointInterval: RunReplay.MaximumSteps + 1));
    }

    [Fact]
    public void Replay_construction_rejects_an_oversized_complete_envelope()
    {
        var oversizedInitialState = "\""
            + new string('a', RunReplay.MaximumSerializedCharacters - 2)
            + "\"";

        var exception = Assert.Throws<ArgumentException>(() =>
            RunReplay.CreateForTesting(
                oversizedInitialState,
                [],
                RunReplay.DefaultCheckpointInterval,
                [new ReplayCheckpoint(0, "0000000000000000")],
                new ReplayOutcome(
                    0,
                    0,
                    RunStatus.Running,
                    DeathCause.None,
                    0,
                    "0000000000000000")));

        Assert.Contains("size limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replay_step_checkpoint_and_outcome_reject_invalid_contracts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ReplayStep(0, []));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ReplayStep(1, [(Direction)255]));
        Assert.Throws<ArgumentException>(
            () => new ReplayStep(
                1,
                Enumerable.Repeat(Direction.Up, ReplayStep.MaximumCommands + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ReplayCheckpoint(-1, "0000000000000000"));
        Assert.Throws<ArgumentException>(
            () => new ReplayCheckpoint(0, "INVALID"));
        Assert.Throws<ArgumentException>(
            () => new ReplayOutcome(
                0,
                0,
                RunStatus.Running,
                DeathCause.None,
                0,
                "ABCDEF0000000000"));
    }

    [Theory]
    [MemberData(nameof(UnsupportedContracts))]
    public void Read_reports_specific_incompatible_contracts(
        string current,
        string replacement,
        ReplayCompatibilityCode expectedCode)
    {
        var serialized = CreateSingleStepReplay().Serialize();
        var incompatible = ReplaceOnce(serialized, current, replacement);

        var result = RunReplay.Read(incompatible);

        Assert.Equal(expectedCode, result.Compatibility.Code);
        Assert.False(result.Compatibility.IsCompatible);
        Assert.Null(result.Replay);
    }

    public static TheoryData<string, string, ReplayCompatibilityCode> UnsupportedContracts =>
        new()
        {
            {
                "\"schemaVersion\":1",
                "\"schemaVersion\":2",
                ReplayCompatibilityCode.UnsupportedSchema
            },
            {
                "\"kind\":\"vibesnake-run-replay\"",
                "\"kind\":\"unknown-replay\"",
                ReplayCompatibilityCode.UnsupportedKind
            },
            {
                "\"id\":\"vibesnake-core\"",
                "\"id\":\"unknown-rules\"",
                ReplayCompatibilityCode.UnsupportedRuleset
            },
            {
                "\"version\":4",
                "\"version\":3",
                ReplayCompatibilityCode.UnsupportedRulesVersion
            },
            {
                "\"rngAlgorithm\":\"pcg-xsh-rr-32-v1\"",
                "\"rngAlgorithm\":\"unknown-rng\"",
                ReplayCompatibilityCode.UnsupportedRandomAlgorithm
            },
            {
                "\"stateHashAlgorithm\":\"fnv1a64-canonical-json-v3\"",
                "\"stateHashAlgorithm\":\"unknown-state-hash\"",
                ReplayCompatibilityCode.UnsupportedStateHashAlgorithm
            },
            {
                "\"algorithm\":\"sha256-canonical-replay-payload-v1\"",
                "\"algorithm\":\"unknown-integrity\"",
                ReplayCompatibilityCode.UnsupportedIntegrityAlgorithm
            },
            {
                "\"configHashAlgorithm\":\"sha256-canonical-runconfig-v1\"",
                "\"configHashAlgorithm\":\"unknown-config-hash\"",
                ReplayCompatibilityCode.UnsupportedConfigHashAlgorithm
            },
        };

    [Fact]
    public void Read_detects_action_tampering_before_execution()
    {
        var serialized = CreateSingleStepReplay().Serialize();
        var tampered = ReplaceOnce(
            serialized,
            "\"commands\":[0]",
            "\"commands\":[1]");

        var result = RunReplay.Read(tampered);

        Assert.Equal(
            ReplayCompatibilityCode.IntegrityMismatch,
            result.Compatibility.Code);
        Assert.Null(result.Replay);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData("{}")]
    public void Read_rejects_empty_and_malformed_payloads(string payload)
    {
        var result = RunReplay.Read(payload);

        Assert.Equal(
            ReplayCompatibilityCode.InvalidPayload,
            result.Compatibility.Code);
        Assert.Null(result.Replay);
    }

    [Fact]
    public void Read_rejects_payload_over_the_size_limit_before_parsing()
    {
        var oversized = "{" + new string(
            ' ',
            RunReplay.MaximumSerializedCharacters);

        var result = RunReplay.Read(oversized);

        Assert.Equal(
            ReplayCompatibilityCode.InvalidPayload,
            result.Compatibility.Code);
        Assert.Contains(
            "size limit",
            result.Compatibility.Message,
            StringComparison.Ordinal);
        Assert.Null(result.Replay);
    }

    [Fact]
    public void Read_never_reflects_an_untrusted_contract_identifier()
    {
        var serialized = CreateSingleStepReplay().Serialize();
        var marker = new string('x', 32_768);
        var incompatible = ReplaceOnce(
            serialized,
            "\"kind\":\"vibesnake-run-replay\"",
            $"\"kind\":\"{marker}\"");

        var result = RunReplay.Read(incompatible);

        Assert.Equal(ReplayCompatibilityCode.UnsupportedKind, result.Compatibility.Code);
        Assert.DoesNotContain(marker, result.Compatibility.Message, StringComparison.Ordinal);
        Assert.InRange(result.Compatibility.Message.Length, 1, 128);
    }

    [Fact]
    public void Verify_stops_before_exceeding_the_deterministic_work_budget()
    {
        var replay = CreateSingleStepReplay();

        var verification = replay.Verify(maximumWorkUnits: 1);

        Assert.False(verification.IsValid);
        Assert.Equal(ReplayVerificationCode.WorkLimitExceeded, verification.Code);
        Assert.Equal(0, verification.FirstDivergentStep);
        Assert.Contains("work limit", verification.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentOutOfRangeException>(() => replay.Verify(maximumWorkUnits: 0));
    }

    [Fact]
    public void Verify_rejects_a_large_body_step_product_before_executing_it()
    {
        var config = new RunConfig(
            Width: 4_096,
            Height: 64,
            StarvationTicks: RunConfig.MaximumConfiguredTicks,
            PowerSpawnIntervalTicks: 0);
        var path = Enumerable
            .Range(0, config.Height)
            .SelectMany(y => Enumerable
                .Range(0, config.Width)
                .Select(offset => new GridPoint(
                    y % 2 == 0 ? offset : config.Width - 1 - offset,
                    y)))
            .ToArray();
        const int bodyLength = 20_000;
        const int stepCount = 1_000;
        var initial = SnakeRun.CreateForTesting(
            config,
            path.Take(bodyLength),
            Direction.Right,
            path[bodyLength + stepCount],
            hungerTicksRemaining: config.StarvationTicks);
        var initialHash = initial.ComputeStateHash();
        var replay = RunReplay.CreateForTesting(
            initial.SerializeCanonicalState(),
            Enumerable
                .Range(1, stepCount)
                .Select(step => new ReplayStep(step, [])),
            stepCount,
            [
                new ReplayCheckpoint(0, initialHash),
                new ReplayCheckpoint(stepCount, initialHash),
            ],
            new ReplayOutcome(
                stepCount,
                stepCount,
                RunStatus.Running,
                DeathCause.None,
                0,
                initialHash));

        var verification = replay.Verify();

        Assert.False(verification.IsValid);
        Assert.Equal(ReplayVerificationCode.WorkLimitExceeded, verification.Code);
        Assert.Equal(0, verification.FirstDivergentStep);
    }

    [Fact]
    public void Verify_charges_large_grid_power_spawn_scans_before_executing_them()
    {
        var config = new RunConfig(
            Width: 4_096,
            Height: 64,
            StarvationTicks: RunConfig.MaximumConfiguredTicks,
            PowerSpawnIntervalTicks: 1,
            PowerVisibleTicks: RunConfig.MinimumPowerVisibleTicks);
        var initial = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(0, 0)],
            Direction.Right,
            new GridPoint(0, 1),
            hungerTicksRemaining: config.StarvationTicks);
        var initialHash = initial.ComputeStateHash();
        const int stepCount = 1_000;
        var replay = RunReplay.CreateForTesting(
            initial.SerializeCanonicalState(),
            Enumerable
                .Range(1, stepCount)
                .Select(step => new ReplayStep(step, [])),
            stepCount,
            [
                new ReplayCheckpoint(0, initialHash),
                new ReplayCheckpoint(stepCount, initialHash),
            ],
            new ReplayOutcome(
                stepCount,
                stepCount,
                RunStatus.Running,
                DeathCause.None,
                0,
                initialHash));

        var verification = replay.Verify();

        Assert.False(verification.IsValid);
        Assert.Equal(ReplayVerificationCode.WorkLimitExceeded, verification.Code);
        Assert.NotNull(verification.FirstDivergentStep);
        Assert.InRange(verification.FirstDivergentStep.Value, 1, 64);
    }

    [Fact]
    public void Read_rejects_step_and_checkpoint_arrays_above_contract_bounds()
    {
        var serialized = CreateSingleStepReplay().Serialize();
        var tooManySteps = ReplaceOnce(
            serialized,
            "\"steps\":[{\"step\":1,\"commands\":[0]}]",
            $"\"steps\":[{string.Join(',', Enumerable.Repeat("null", RunReplay.MaximumSteps + 1))}]");
        var tooManyCheckpoints = ReplaceOnce(
            serialized,
            "\"checkpoints\":[{\"step\":0,\"stateHash\":",
            "\"checkpoints\":[null,{\"step\":0,\"stateHash\":");

        Assert.Equal(
            ReplayCompatibilityCode.InvalidPayload,
            RunReplay.Read(tooManySteps).Compatibility.Code);
        Assert.Equal(
            ReplayCompatibilityCode.InvalidPayload,
            RunReplay.Read(tooManyCheckpoints).Compatibility.Code);
    }

    [Fact]
    public void Read_rejects_noncanonical_encoding_and_unknown_properties()
    {
        var serialized = CreateSingleStepReplay().Serialize();

        Assert.Equal(
            ReplayCompatibilityCode.InvalidPayload,
            RunReplay.Read(serialized + "\n").Compatibility.Code);
        Assert.Equal(
            ReplayCompatibilityCode.InvalidPayload,
            RunReplay.Read(
                serialized.Insert(serialized.Length - 1, ",\"unexpected\":true"))
                .Compatibility.Code);
    }

    [Fact]
    public void Read_rejects_an_integrity_valid_replay_with_impossible_shield_state()
    {
        var starvation = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 1),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 1,
            shieldTicksRemaining: 2);
        starvation.Step();
        var impossibleInitialState = ReplaceOnce(
            starvation.SerializeCanonicalState(),
            "\"deathCause\":2",
            "\"deathCause\":1");
        var valid = CreateSingleStepReplay();
        var integrityValid = RunReplay.CreateForTesting(
            impossibleInitialState,
            valid.Steps,
            valid.CheckpointInterval,
            valid.Checkpoints,
            valid.Outcome);

        var result = RunReplay.Read(integrityValid.Serialize());

        Assert.Equal(ReplayCompatibilityCode.InvalidPayload, result.Compatibility.Code);
        Assert.Null(result.Replay);
    }

    [Fact]
    public void Verification_distinguishes_integrity_from_deterministic_divergence()
    {
        var valid = CreateSingleStepReplay();
        var wrongHash = AlternateHash(valid.Checkpoints[1].StateHash);
        var internallyConsistent = RunReplay.CreateForTesting(
            valid.InitialCanonicalState,
            valid.Steps,
            valid.CheckpointInterval,
            [valid.Checkpoints[0], new ReplayCheckpoint(1, wrongHash)],
            valid.Outcome);

        var read = RunReplay.Read(internallyConsistent.Serialize());
        Assert.True(read.Compatibility.IsCompatible);
        Assert.NotNull(read.Replay);

        var verification = read.Replay.Verify();
        Assert.False(verification.IsValid);
        Assert.Equal(1, verification.FirstDivergentStep);
        Assert.Equal(wrongHash, verification.ExpectedStateHash);
        Assert.Equal(valid.Checkpoints[1].StateHash, verification.ActualStateHash);
    }

    [Fact]
    public void Verification_reports_initial_outcome_and_post_terminal_divergence()
    {
        var valid = CreateSingleStepReplay();
        var wrongInitial = RunReplay.CreateForTesting(
            valid.InitialCanonicalState,
            valid.Steps,
            valid.CheckpointInterval,
            [
                new ReplayCheckpoint(0, AlternateHash(valid.Checkpoints[0].StateHash)),
                valid.Checkpoints[1],
            ],
            valid.Outcome);
        Assert.Equal(0, wrongInitial.Verify().FirstDivergentStep);

        var wrongOutcome = RunReplay.CreateForTesting(
            valid.InitialCanonicalState,
            valid.Steps,
            valid.CheckpointInterval,
            valid.Checkpoints,
            new ReplayOutcome(
                valid.Outcome.StepCount,
                valid.Outcome.FinalTick,
                valid.Outcome.Status,
                valid.Outcome.DeathCause,
                valid.Outcome.Score + 1,
                valid.Outcome.StateHash));
        Assert.Equal(1, wrongOutcome.Verify().FirstDivergentStep);

        var terminalInitial = SnakeRun.Create(
            500UL,
            new RunConfig(StarvationTicks: 1));
        var terminalState = terminalInitial.SerializeCanonicalState();
        var terminalSimulation = SnakeRun.RestoreCanonicalState(terminalState);
        var initialHash = terminalSimulation.ComputeStateHash();
        var terminalResult = terminalSimulation.Step();
        var afterTerminal = RunReplay.CreateForTesting(
            terminalState,
            [new ReplayStep(1, []), new ReplayStep(2, [])],
            1,
            [
                new ReplayCheckpoint(0, initialHash),
                new ReplayCheckpoint(1, terminalResult.StateHash),
                new ReplayCheckpoint(2, terminalResult.StateHash),
            ],
            new ReplayOutcome(
                2,
                terminalSimulation.Tick,
                terminalSimulation.Status,
                terminalSimulation.DeathCause,
                terminalSimulation.Score,
                terminalResult.StateHash));
        Assert.Equal(2, afterTerminal.Verify().FirstDivergentStep);
    }

    private static RunReplay CreateSingleStepReplay()
    {
        IReadOnlyList<Direction>[] commands = [[Direction.Up]];
        return RunReplay.Capture(
            SnakeRun.Create(42UL),
            commands,
            checkpointInterval: 1);
    }

    private static string AlternateHash(string hash) =>
        (hash[0] == '0' ? "1" : "0") + hash[1..];

    private static string ReplaceOnce(
        string value,
        string current,
        string replacement)
    {
        var index = value.IndexOf(current, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Expected replay fragment was not found: {current}");
        return string.Concat(
            value.AsSpan(0, index),
            replacement,
            value.AsSpan(index + current.Length));
    }
}
