using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Rules;

public sealed partial class RunReplay
{
    public const int SchemaVersion = 1;
    public const string Kind = "vibesnake-run-replay";
    public const string IntegrityAlgorithmId = "sha256-canonical-replay-payload-v1";
    public const string CaptureTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    public const int DefaultCheckpointInterval = 200;
    public const int MaximumSteps = 100_000;
    public const int MaximumSerializedCharacters = 16 * 1024 * 1024;
    public const long MaximumVerificationWorkUnits = 16_000_000;

    private readonly ReadOnlyCollection<ReplayStep> _steps;
    private readonly ReadOnlyCollection<ReplayCheckpoint> _checkpoints;
    private readonly bool _writeConfigIdentity;
    private readonly bool _writeCaptureMetadata;

    private RunReplay(
        string initialCanonicalState,
        IEnumerable<ReplayStep> steps,
        int checkpointInterval,
        IEnumerable<ReplayCheckpoint> checkpoints,
        ReplayOutcome outcome,
        string? payloadHash = null,
        string? configHash = null,
        string? configHashAlgorithm = null,
        bool writeConfigIdentity = true,
        string? appVersion = null,
        string? capturedAtUtc = null,
        ulong? gameplaySeed = null,
        ulong? aiSeed = null,
        bool writeCaptureMetadata = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initialCanonicalState);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(outcome);
        if (initialCanonicalState.Length > MaximumSerializedCharacters)
        {
            throw new ArgumentException(
                "The initial state exceeds the replay size limit.",
                nameof(initialCanonicalState));
        }

        if (checkpointInterval <= 0 || checkpointInterval > MaximumSteps)
        {
            throw new ArgumentOutOfRangeException(nameof(checkpointInterval));
        }

        var stepCopy = steps.ToArray();
        if (stepCopy.Length > MaximumSteps)
        {
            throw new ArgumentException(
                $"A replay cannot contain more than {MaximumSteps} steps.",
                nameof(steps));
        }

        for (var index = 0; index < stepCopy.Length; index++)
        {
            if (stepCopy[index].StepIndex != index + 1)
            {
                throw new ArgumentException(
                    "Replay step indexes must be contiguous and start at one.",
                    nameof(steps));
            }
        }

        var checkpointCopy = checkpoints.ToArray();
        ValidateCheckpointSchedule(
            checkpointCopy,
            stepCopy.Length,
            checkpointInterval);
        if (outcome.StepCount != stepCopy.Length)
        {
            throw new ArgumentException(
                "The replay outcome step count must match the action stream.",
                nameof(outcome));
        }

        InitialCanonicalState = initialCanonicalState;
        _steps = Array.AsReadOnly(stepCopy);
        CheckpointInterval = checkpointInterval;
        _checkpoints = Array.AsReadOnly(checkpointCopy);
        Outcome = outcome;
        _writeConfigIdentity = writeConfigIdentity;
        if (appVersion is not null)
        {
            if (string.IsNullOrWhiteSpace(appVersion) || appVersion.Length > 64)
            {
                throw new ArgumentException(
                    "Replay appVersion must be non-empty and at most 64 characters when provided.",
                    nameof(appVersion));
            }

            AppVersion = appVersion.Trim();
        }
        else
        {
            AppVersion = null;
        }

        _writeCaptureMetadata = writeCaptureMetadata;
        if (writeCaptureMetadata)
        {
            if (capturedAtUtc is null || gameplaySeed is null || aiSeed is null)
            {
                throw new ArgumentException(
                    "Replay capture metadata requires a timestamp plus gameplay and AI seeds.");
            }

            if (!IsCanonicalCaptureTimestamp(capturedAtUtc))
            {
                throw new ArgumentException(
                    "Replay capturedAtUtc must use canonical UTC millisecond format.",
                    nameof(capturedAtUtc));
            }

            if (gameplaySeed.Value != aiSeed.Value)
            {
                throw new ArgumentException(
                    "Replay gameplay and AI seed inputs must match the current shared-master-seed contract.");
            }

            CapturedAtUtc = capturedAtUtc;
            GameplaySeed = gameplaySeed;
            AiSeed = aiSeed;
        }
        else
        {
            if (capturedAtUtc is not null || gameplaySeed is not null || aiSeed is not null)
            {
                throw new ArgumentException(
                    "Replay capture metadata fields cannot be supplied when metadata output is disabled.");
            }

            CapturedAtUtc = null;
            GameplaySeed = null;
            AiSeed = null;
        }

        (ConfigHash, ConfigHashAlgorithm) = ResolveConfigIdentity(
            initialCanonicalState,
            configHash,
            configHashAlgorithm);
        PayloadHash = payloadHash ?? ComputePayloadHash();
        if (!IsLowerHex(PayloadHash, 64))
        {
            throw new ArgumentException(
                "The replay payload hash must be a lowercase SHA-256 digest.",
                nameof(payloadHash));
        }

        if (SerializeEnvelopeBytes(includeIntegrity: true).Length > MaximumSerializedCharacters)
        {
            throw new ArgumentException(
                "The complete replay envelope exceeds the serialized size limit.",
                nameof(steps));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance property is retained for public API compatibility.")]
    public RulesetIdentity Ruleset => RulesetIdentity.Current;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance property is retained for public API compatibility.")]
    public string RandomAlgorithmId => Pcg32.AlgorithmId;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance property is retained for public API compatibility.")]
    public string StateHashAlgorithmId => SnakeRun.StateHashAlgorithmId;

    public string InitialCanonicalState { get; }

    /// <summary>
    /// Optional application version string supplied by the shell at capture time.
    /// Absent on legacy envelopes; not used for rules determinism.
    /// </summary>
    public string? AppVersion { get; }

    /// <summary>
    /// Optional canonical UTC capture timestamp supplied by the shell. Rules do
    /// not read a clock; legacy schema-1 envelopes omit this field.
    /// </summary>
    public string? CapturedAtUtc { get; }

    /// <summary>
    /// Seed input for the deterministic gameplay stream. The current stream
    /// bank uses a shared master seed with distinct fixed sequence IDs.
    /// </summary>
    public ulong? GameplaySeed { get; }

    /// <summary>Seed input for the deterministic AI stream.</summary>
    public ulong? AiSeed { get; }

    /// <summary>
    /// Effective rules configuration digest captured at recording time.
    /// Verification rejects any restored or mid-run config identity drift.
    /// </summary>
    public string ConfigHash { get; }

    /// <summary>Algorithm id for <see cref="ConfigHash"/>.</summary>
    public string ConfigHashAlgorithm { get; }

    public IReadOnlyList<ReplayStep> Steps => _steps;

    public int CheckpointInterval { get; }

    public IReadOnlyList<ReplayCheckpoint> Checkpoints => _checkpoints;

    public ReplayOutcome Outcome { get; }

    public string PayloadHash { get; }

    public static RunReplay Capture(
        SnakeRun initialRun,
        IEnumerable<IReadOnlyList<Direction>> commandsByStep,
        int checkpointInterval = DefaultCheckpointInterval,
        string? appVersion = null,
        string? capturedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(initialRun);
        ArgumentNullException.ThrowIfNull(commandsByStep);
        if (initialRun.Status != RunStatus.Running)
        {
            throw new ArgumentException(
                "Replay capture must begin from a running state.",
                nameof(initialRun));
        }

        var initialCanonicalState = initialRun.SerializeCanonicalState();
        var simulation = SnakeRun.RestoreCanonicalState(initialCanonicalState);
        var recorder = new RunReplayRecorder(
            initialRun,
            checkpointInterval,
            appVersion,
            capturedAtUtc);

        foreach (var commands in commandsByStep)
        {
            ArgumentNullException.ThrowIfNull(commands);
            if (recorder.RecordedStepCount >= MaximumSteps)
            {
                throw new ArgumentException(
                    $"A replay cannot contain more than {MaximumSteps} steps.",
                    nameof(commandsByStep));
            }

            if (simulation.Status != RunStatus.Running)
            {
                throw new ArgumentException(
                    "Replay commands cannot continue after a terminal outcome.",
                    nameof(commandsByStep));
            }

            foreach (var command in commands)
            {
                if (!recorder.TryRecordCommand(command))
                {
                    throw new ArgumentException(
                        recorder.FailureMessage,
                        nameof(commandsByStep));
                }

                simulation.QueueDirection(command);
            }

            var result = simulation.Step();
            if (!recorder.TryCompleteStep(result, simulation))
            {
                throw new InvalidOperationException(
                    "Offline replay capture diverged: " + recorder.FailureMessage);
            }
        }

        var finalized = recorder.Finish(simulation);
        if (!finalized.IsSuccessful || finalized.Replay is null)
        {
            throw new InvalidOperationException(
                "Offline replay capture could not finalize: " + finalized.Message);
        }

        return finalized.Replay;
    }

    internal static RunReplay CreateRecorded(
        string initialCanonicalState,
        IEnumerable<ReplayStep> steps,
        int checkpointInterval,
        IEnumerable<ReplayCheckpoint> checkpoints,
        ReplayOutcome outcome,
        string? configHash = null,
        string? configHashAlgorithm = null,
        string? appVersion = null,
        string? capturedAtUtc = null,
        ulong? gameplaySeed = null,
        ulong? aiSeed = null,
        bool writeCaptureMetadata = false) =>
        new(
            initialCanonicalState,
            steps,
            checkpointInterval,
            checkpoints,
            outcome,
            configHash: configHash,
            configHashAlgorithm: configHashAlgorithm,
            writeConfigIdentity: true,
            appVersion: appVersion,
            capturedAtUtc: capturedAtUtc,
            gameplaySeed: gameplaySeed,
            aiSeed: aiSeed,
            writeCaptureMetadata: writeCaptureMetadata);

    public ReplayVerificationResult Verify(
        long maximumWorkUnits = MaximumVerificationWorkUnits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWorkUnits);

        SnakeRun simulation;
        try
        {
            simulation = SnakeRun.RestoreCanonicalState(InitialCanonicalState);
        }
        catch (InvalidDataException)
        {
            return VerificationFailure(
                ReplayVerificationCode.InvalidInitialState,
                0,
                "The initial canonical state cannot be restored.");
        }

        if (!ConfigIdentityMatches(simulation))
        {
            return VerificationFailure(
                ReplayVerificationCode.ConfigIdentityDiverged,
                0,
                "The restored run configuration does not match the replay envelope.");
        }

        // Version 4 body length never decreases, so this is a safe lower bound for
        // the initial, per-step, and final canonical hashes. Grid scans are charged
        // dynamically before each step because their cadence depends on run state.
        // A future detachment rule must replace this preflight when it advances the
        // rules identity.
        var minimumHashPasses = _steps.Count + 2L;
        if (simulation.Body.Count > maximumWorkUnits / minimumHashPasses)
        {
            return VerificationFailure(
                ReplayVerificationCode.WorkLimitExceeded,
                0,
                "Replay verification exceeded the deterministic work limit.");
        }

        var verificationWork = 0L;
        if (!TryConsumeVerificationWork(
            ref verificationWork,
            simulation.Body.Count,
            maximumWorkUnits))
        {
            return VerificationFailure(
                ReplayVerificationCode.WorkLimitExceeded,
                0,
                "Replay verification exceeded the deterministic work limit.");
        }

        var checkpointIndex = 0;
        var initialCheckpoint = _checkpoints[checkpointIndex++];
        var initialHash = simulation.ComputeStateHash();
        if (!string.Equals(
            initialCheckpoint.StateHash,
            initialHash,
            StringComparison.Ordinal))
        {
            return VerificationFailure(
                ReplayVerificationCode.InitialCheckpointDiverged,
                0,
                "The initial replay checkpoint diverged.",
                initialCheckpoint.StateHash,
                initialHash);
        }

        foreach (var step in _steps)
        {
            if (simulation.Status != RunStatus.Running)
            {
                return VerificationFailure(
                    ReplayVerificationCode.ActionsAfterTerminal,
                    step.StepIndex,
                    "The replay contains actions after a terminal outcome.");
            }

            foreach (var command in step.Commands)
            {
                simulation.QueueDirection(command);
            }

            if (!TryConsumeVerificationWork(
                ref verificationWork,
                simulation.GetNextStepVerificationWorkUnits(),
                maximumWorkUnits))
            {
                return VerificationFailure(
                    ReplayVerificationCode.WorkLimitExceeded,
                    step.StepIndex,
                    "Replay verification exceeded the deterministic work limit.");
            }

            var result = simulation.Step();
            if (!ConfigIdentityMatches(simulation))
            {
                return VerificationFailure(
                    ReplayVerificationCode.ConfigIdentityDiverged,
                    step.StepIndex,
                    "The run configuration changed during replay verification.");
            }

            if (
                checkpointIndex < _checkpoints.Count
                && _checkpoints[checkpointIndex].StepIndex == step.StepIndex)
            {
                var checkpoint = _checkpoints[checkpointIndex++];
                if (!string.Equals(
                    checkpoint.StateHash,
                    result.StateHash,
                    StringComparison.Ordinal))
                {
                    return VerificationFailure(
                        ReplayVerificationCode.CheckpointDiverged,
                        step.StepIndex,
                        "A replay checkpoint diverged.",
                        checkpoint.StateHash,
                        result.StateHash);
                }
            }
        }

        if (!TryConsumeVerificationWork(
            ref verificationWork,
            simulation.Body.Count,
            maximumWorkUnits))
        {
            return VerificationFailure(
                ReplayVerificationCode.WorkLimitExceeded,
                _steps.Count,
                "Replay verification exceeded the deterministic work limit.");
        }

        var snapshot = simulation.GetSnapshot();
        if (
            Outcome.FinalTick != snapshot.Tick
            || Outcome.Status != snapshot.Status
            || Outcome.DeathCause != snapshot.DeathCause
            || Outcome.Score != snapshot.Score
            || !string.Equals(
                Outcome.StateHash,
                snapshot.StateHash,
                StringComparison.Ordinal))
        {
            return VerificationFailure(
                ReplayVerificationCode.OutcomeDiverged,
                _steps.Count,
                "The replay outcome diverged.",
                Outcome.StateHash,
                snapshot.StateHash);
        }

        return new ReplayVerificationResult(
            ReplayVerificationCode.Verified,
            null,
            "The replay is compatible and deterministic.");
    }

    public string Serialize() =>
        Encoding.UTF8.GetString(SerializeEnvelopeBytes(includeIntegrity: true));

    internal static RunReplay CreateForTesting(
        string initialCanonicalState,
        IEnumerable<ReplayStep> steps,
        int checkpointInterval,
        IEnumerable<ReplayCheckpoint> checkpoints,
        ReplayOutcome outcome,
        string? configHash = null,
        string? configHashAlgorithm = null,
        string? appVersion = null,
        string? capturedAtUtc = null,
        ulong? gameplaySeed = null,
        ulong? aiSeed = null,
        bool writeCaptureMetadata = false) =>
        CreateRecorded(
            initialCanonicalState,
            steps,
            checkpointInterval,
            checkpoints,
            outcome,
            configHash,
            configHashAlgorithm,
            appVersion,
            capturedAtUtc,
            gameplaySeed,
            aiSeed,
            writeCaptureMetadata);

    internal static bool IsStateHash(string? value) => IsLowerHex(value, 16);

    internal static bool IsCanonicalCaptureTimestamp(string? value) =>
        value is not null
        && value.Length == 24
        && DateTimeOffset.TryParseExact(
            value,
            CaptureTimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
        && parsed.Offset == TimeSpan.Zero
        && string.Equals(
            parsed.ToString(CaptureTimestampFormat, CultureInfo.InvariantCulture),
            value,
            StringComparison.Ordinal);

    private static ReplayVerificationResult VerificationFailure(
        ReplayVerificationCode code,
        int step,
        string message,
        string? expectedStateHash = null,
        string? actualStateHash = null) =>
        new(code, step, message, expectedStateHash, actualStateHash);

    private static bool TryConsumeVerificationWork(
        ref long consumed,
        long requested,
        long limit)
    {
        if (requested > limit - consumed)
        {
            return false;
        }

        consumed += requested;
        return true;
    }

    private static void ValidateCheckpointSchedule(
        ReplayCheckpoint[] checkpoints,
        int stepCount,
        int checkpointInterval)
    {
        if (checkpoints.Length != ExpectedCheckpointCount(stepCount, checkpointInterval))
        {
            throw new ArgumentException(
                "Replay checkpoints do not match the declared interval.",
                nameof(checkpoints));
        }

        var expectedIndexes = new List<int>(checkpoints.Length) { 0 };
        for (var step = checkpointInterval; step <= stepCount; step += checkpointInterval)
        {
            expectedIndexes.Add(step);
        }

        if (expectedIndexes[^1] != stepCount)
        {
            expectedIndexes.Add(stepCount);
        }

        for (var index = 0; index < expectedIndexes.Count; index++)
        {
            if (checkpoints[index].StepIndex != expectedIndexes[index])
            {
                throw new ArgumentException(
                    "Replay checkpoints do not match the declared interval.",
                    nameof(checkpoints));
            }
        }
    }

    private static int ExpectedCheckpointCount(
        int stepCount,
        int checkpointInterval)
    {
        if (stepCount == 0)
        {
            return 1;
        }

        return 1
            + (stepCount / checkpointInterval)
            + (stepCount % checkpointInterval == 0 ? 0 : 1);
    }

    private string ComputePayloadHash()
    {
        var payload = SerializeEnvelopeBytes(includeIntegrity: false);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private byte[] SerializeEnvelopeBytes(bool includeIntegrity)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("kind", Kind);
            if (AppVersion is not null)
            {
                writer.WriteString("appVersion", AppVersion);
            }

            if (_writeCaptureMetadata)
            {
                writer.WriteString("capturedAtUtc", CapturedAtUtc);
                writer.WriteNumber("gameplaySeed", GameplaySeed!.Value);
                writer.WriteNumber("aiSeed", AiSeed!.Value);
            }

            writer.WriteStartObject("ruleset");
            writer.WriteString("id", Ruleset.Id);
            writer.WriteNumber("version", Ruleset.Version);
            writer.WriteEndObject();

            writer.WriteString("rngAlgorithm", RandomAlgorithmId);
            writer.WriteString("stateHashAlgorithm", StateHashAlgorithmId);
            if (_writeConfigIdentity)
            {
                writer.WriteString("configHash", ConfigHash);
                writer.WriteString("configHashAlgorithm", ConfigHashAlgorithm);
            }

            writer.WriteNumber("checkpointInterval", CheckpointInterval);
            writer.WritePropertyName("initialState");
            writer.WriteRawValue(InitialCanonicalState, skipInputValidation: false);

            writer.WriteStartArray("steps");
            foreach (var step in _steps)
            {
                writer.WriteStartObject();
                writer.WriteNumber("step", step.StepIndex);
                writer.WriteStartArray("commands");
                foreach (var command in step.Commands)
                {
                    writer.WriteNumberValue((byte)command);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("checkpoints");
            foreach (var checkpoint in _checkpoints)
            {
                writer.WriteStartObject();
                writer.WriteNumber("step", checkpoint.StepIndex);
                writer.WriteString("stateHash", checkpoint.StateHash);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartObject("outcome");
            writer.WriteNumber("stepCount", Outcome.StepCount);
            writer.WriteNumber("finalTick", Outcome.FinalTick);
            writer.WriteNumber("status", (byte)Outcome.Status);
            writer.WriteNumber("deathCause", (byte)Outcome.DeathCause);
            writer.WriteNumber("score", Outcome.Score);
            writer.WriteString("stateHash", Outcome.StateHash);
            writer.WriteEndObject();

            if (includeIntegrity)
            {
                writer.WriteStartObject("integrity");
                writer.WriteString("algorithm", IntegrityAlgorithmId);
                writer.WriteString("payloadHash", PayloadHash);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private bool ConfigIdentityMatches(SnakeRun simulation) =>
        string.Equals(ConfigHash, simulation.ConfigHash, StringComparison.Ordinal)
        && string.Equals(
            ConfigHashAlgorithm,
            simulation.ConfigHashAlgorithm,
            StringComparison.Ordinal);

    private static (string ConfigHash, string ConfigHashAlgorithm) ResolveConfigIdentity(
        string initialCanonicalState,
        string? configHash,
        string? configHashAlgorithm)
    {
        if (configHash is not null || configHashAlgorithm is not null)
        {
            if (
                configHash is null
                || configHashAlgorithm is null
                || !IsLowerHex(configHash, 64)
                || string.IsNullOrWhiteSpace(configHashAlgorithm))
            {
                throw new ArgumentException(
                    "Replay config identity requires a 64-character lowercase hex hash and algorithm id.");
            }

            return (configHash, configHashAlgorithm);
        }

        // Derive from the embedded initial state when capture did not pass hashes
        // explicitly (tests and offline construction). Unrestorable states fall
        // through to envelope size checks with a stable sentinel identity.
        try
        {
            var restored = SnakeRun.RestoreCanonicalState(initialCanonicalState);
            return (restored.ConfigHash, restored.ConfigHashAlgorithm);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or JsonException
            or ArgumentException
            or FormatException)
        {
            return (new string('0', 64), RunConfig.ConfigHashAlgorithmId);
        }
    }

    private static bool IsLowerHex(string? value, int length)
    {
        if (value is null || value.Length != length)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (
                (character < '0' || character > '9')
                && (character < 'a' || character > 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
