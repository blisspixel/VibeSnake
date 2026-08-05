using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Rules;

public sealed partial class RunReplay
{
    public static ReplayReadResult Read(string serializedReplay)
    {
        if (
            string.IsNullOrWhiteSpace(serializedReplay)
            || serializedReplay.Length > MaximumSerializedCharacters)
        {
            return Incompatible(
                ReplayCompatibilityCode.InvalidPayload,
                "The replay payload is empty or exceeds the size limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                serializedReplay,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });
            var root = RequireObject(document.RootElement, "root");
            var schemaVersion = ReadInt32(root, "schemaVersion");
            if (schemaVersion != SchemaVersion)
            {
                return Incompatible(
                    ReplayCompatibilityCode.UnsupportedSchema,
                    $"Replay schema {schemaVersion} is not supported.");
            }

            var kind = ReadString(root, "kind");
            if (!string.Equals(kind, Kind, StringComparison.Ordinal))
            {
                return Incompatible(
                    ReplayCompatibilityCode.UnsupportedKind,
                    "The replay kind is not supported.");
            }

            string? appVersion = null;
            if (root.TryGetProperty("appVersion", out var appVersionElement))
            {
                appVersion = appVersionElement.GetString();
                if (string.IsNullOrWhiteSpace(appVersion) || appVersion.Length > 64)
                {
                    return Incompatible(
                        ReplayCompatibilityCode.InvalidPayload,
                        "The replay appVersion is empty or exceeds 64 characters.");
                }
            }

            var rulesetElement = RequireObject(root.GetProperty("ruleset"), "ruleset");
            var rulesetId = ReadString(rulesetElement, "id");
            if (!string.Equals(
                rulesetId,
                RulesetIdentity.CurrentId,
                StringComparison.Ordinal))
            {
                return Incompatible(
                    ReplayCompatibilityCode.UnsupportedRuleset,
                    "The replay ruleset is not supported.");
            }

            var rulesVersion = ReadInt32(rulesetElement, "version");
            if (rulesVersion != RulesetIdentity.CurrentVersion)
            {
                return Incompatible(
                    ReplayCompatibilityCode.UnsupportedRulesVersion,
                    $"Rules version {rulesVersion} is not supported.");
            }

            var randomAlgorithm = ReadString(root, "rngAlgorithm");
            if (!string.Equals(
                randomAlgorithm,
                Pcg32.AlgorithmId,
                StringComparison.Ordinal))
            {
                return Incompatible(
                    ReplayCompatibilityCode.UnsupportedRandomAlgorithm,
                    "The replay random algorithm is not supported.");
            }

            var stateHashAlgorithm = ReadString(root, "stateHashAlgorithm");
            if (!string.Equals(
                stateHashAlgorithm,
                SnakeRun.StateHashAlgorithmId,
                StringComparison.Ordinal))
            {
                return Incompatible(
                    ReplayCompatibilityCode.UnsupportedStateHashAlgorithm,
                    "The replay state-hash algorithm is not supported.");
            }

            var hasConfigHash = root.TryGetProperty("configHash", out _);
            var hasConfigHashAlgorithm = root.TryGetProperty("configHashAlgorithm", out _);
            string? configHash = null;
            string? configHashAlgorithm = null;
            var writeConfigIdentity = hasConfigHash || hasConfigHashAlgorithm;
            if (writeConfigIdentity)
            {
                if (!hasConfigHash || !hasConfigHashAlgorithm)
                {
                    return Incompatible(
                        ReplayCompatibilityCode.InvalidPayload,
                        "Replay config identity fields must both be present when either is set.");
                }

                configHash = ReadString(root, "configHash");
                configHashAlgorithm = ReadString(root, "configHashAlgorithm");
                if (!IsLowerHex(configHash, 64))
                {
                    return Incompatible(
                        ReplayCompatibilityCode.InvalidPayload,
                        "The replay config hash must be a lowercase SHA-256 digest.");
                }

                if (!string.Equals(
                    configHashAlgorithm,
                    RunConfig.ConfigHashAlgorithmId,
                    StringComparison.Ordinal))
                {
                    return Incompatible(
                        ReplayCompatibilityCode.UnsupportedConfigHashAlgorithm,
                        "The replay config-hash algorithm is not supported.");
                }
            }

            var integrityElement = RequireObject(
                root.GetProperty("integrity"),
                "integrity");
            var integrityAlgorithm = ReadString(integrityElement, "algorithm");
            if (!string.Equals(
                integrityAlgorithm,
                IntegrityAlgorithmId,
                StringComparison.Ordinal))
            {
                return Incompatible(
                    ReplayCompatibilityCode.UnsupportedIntegrityAlgorithm,
                    "The replay integrity algorithm is not supported.");
            }

            var payloadHash = ReadString(integrityElement, "payloadHash");
            var initialCanonicalState = root.GetProperty("initialState").GetRawText();
            var checkpointInterval = ReadInt32(root, "checkpointInterval");
            if (checkpointInterval <= 0 || checkpointInterval > MaximumSteps)
            {
                throw new InvalidDataException(
                    "The replay checkpoint interval is outside the supported range.");
            }

            var steps = ReadSteps(root.GetProperty("steps"));
            var checkpoints = ReadCheckpoints(
                root.GetProperty("checkpoints"),
                steps.Count,
                checkpointInterval);
            var outcome = ReadOutcome(root.GetProperty("outcome"));
            var replay = new RunReplay(
                initialCanonicalState,
                steps,
                checkpointInterval,
                checkpoints,
                outcome,
                payloadHash,
                configHash,
                configHashAlgorithm,
                writeConfigIdentity,
                appVersion);

            var expectedPayloadHash = replay.ComputePayloadHash();
            if (!FixedHashEquals(payloadHash, expectedPayloadHash))
            {
                return Incompatible(
                    ReplayCompatibilityCode.IntegrityMismatch,
                    "The replay payload does not match its SHA-256 integrity hash.");
            }

            if (!string.Equals(
                serializedReplay,
                replay.Serialize(),
                StringComparison.Ordinal))
            {
                return Incompatible(
                    ReplayCompatibilityCode.InvalidPayload,
                    "The replay is valid JSON but does not use the canonical encoding.");
            }

            SnakeRun.RestoreCanonicalState(initialCanonicalState);
            return new ReplayReadResult(
                new ReplayCompatibility(
                    ReplayCompatibilityCode.Compatible,
                    "The replay contract is compatible."),
                replay);
        }
        catch (Exception exception) when (
            exception is JsonException
            or InvalidDataException
            or InvalidOperationException
            or KeyNotFoundException
            or ArgumentException
            or FormatException
            or OverflowException)
        {
            return Incompatible(
                ReplayCompatibilityCode.InvalidPayload,
                "The replay payload is malformed or violates schema 1.");
        }
    }

    private static IReadOnlyList<ReplayStep> ReadSteps(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The replay steps value must be an array.");
        }

        var stepCount = element.GetArrayLength();
        if (stepCount > MaximumSteps)
        {
            throw new InvalidDataException(
                $"A replay cannot contain more than {MaximumSteps} steps.");
        }

        var steps = new List<ReplayStep>(stepCount);
        foreach (var stepElement in element.EnumerateArray())
        {
            var stepObject = RequireObject(stepElement, "replay step");
            var commandsElement = stepObject.GetProperty("commands");
            if (commandsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Replay step commands must be an array.");
            }

            var commandCount = commandsElement.GetArrayLength();
            if (commandCount > ReplayStep.MaximumCommands)
            {
                throw new InvalidDataException(
                    $"A replay step cannot contain more than {ReplayStep.MaximumCommands} commands.");
            }

            var commands = new List<Direction>(commandCount);
            foreach (var commandElement in commandsElement.EnumerateArray())
            {
                commands.Add(ReadEnum<Direction>(commandElement));
            }

            steps.Add(
                new ReplayStep(
                    ReadInt32(stepObject, "step"),
                    commands));
        }

        return steps;
    }

    private static IReadOnlyList<ReplayCheckpoint> ReadCheckpoints(
        JsonElement element,
        int stepCount,
        int checkpointInterval)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The replay checkpoints value must be an array.");
        }

        var checkpointCount = element.GetArrayLength();
        var expectedCount = ExpectedCheckpointCount(stepCount, checkpointInterval);
        if (checkpointCount != expectedCount)
        {
            throw new InvalidDataException(
                "Replay checkpoints do not match the declared interval.");
        }

        var checkpoints = new List<ReplayCheckpoint>(checkpointCount);
        foreach (var checkpointElement in element.EnumerateArray())
        {
            var checkpointObject = RequireObject(
                checkpointElement,
                "replay checkpoint");
            checkpoints.Add(
                new ReplayCheckpoint(
                    ReadInt32(checkpointObject, "step"),
                    ReadString(checkpointObject, "stateHash")));
        }

        return checkpoints;
    }

    private static ReplayOutcome ReadOutcome(JsonElement element)
    {
        var outcome = RequireObject(element, "replay outcome");
        return new ReplayOutcome(
            ReadInt32(outcome, "stepCount"),
            ReadInt32(outcome, "finalTick"),
            ReadEnum<RunStatus>(outcome.GetProperty("status")),
            ReadEnum<DeathCause>(outcome.GetProperty("deathCause")),
            ReadInt32(outcome, "score"),
            ReadString(outcome, "stateHash"));
    }

    private static JsonElement RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"The {name} value must be an object.");
        }

        return element;
    }

    private static int ReadInt32(JsonElement parent, string propertyName) =>
        parent.GetProperty(propertyName).GetInt32();

    private static string ReadString(JsonElement parent, string propertyName) =>
        parent.GetProperty(propertyName).GetString()
        ?? throw new InvalidDataException(
            $"The {propertyName} value cannot be null.");

    private static TEnum ReadEnum<TEnum>(JsonElement element)
        where TEnum : struct, Enum
    {
        var value = element.GetByte();
        var parsed = (TEnum)Enum.ToObject(typeof(TEnum), value);
        if (!Enum.IsDefined(parsed))
        {
            throw new InvalidDataException(
                $"The value is not a defined {typeof(TEnum).Name}.");
        }

        return parsed;
    }

    private static ReplayReadResult Incompatible(
        ReplayCompatibilityCode code,
        string message) =>
        new(new ReplayCompatibility(code, message), null);

    private static bool FixedHashEquals(string left, string right)
    {
        if (!IsLowerHex(left, 64) || !IsLowerHex(right, 64))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }
}
