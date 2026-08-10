using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Rules;

[Flags]
public enum OfflineChallengeOptions : byte
{
    None = 0,
    SameSeedRun = 1 << 0,
    GhostRace = 1 << 1,
    HouseholdRival = 1 << 2,
}

public enum SeedCodeReadCode : byte
{
    Valid = 0,
    InvalidFormat = 1,
    IntegrityMismatch = 2,
    UnsupportedSchema = 3,
    UnsupportedRuleset = 4,
    UnsupportedContent = 5,
    UnsupportedMode = 6,
    UnsupportedConfig = 7,
    UnsupportedOptions = 8,
}

public sealed record SeedCodeReadResult(
    SeedCodeReadCode Code,
    string Message,
    SeedChallengeDescriptor? Challenge = null)
{
    public bool IsValid => Code == SeedCodeReadCode.Valid && Challenge is not null;
}

/// <summary>
/// Closed, tamper-evident, offline challenge identity. The code contains no
/// player identity, arbitrary text, paths, or mutable rules state.
/// </summary>
public sealed record SeedChallengeDescriptor(
    int SchemaVersion,
    string Kind,
    string RulesetId,
    int RulesVersion,
    string ContentContractId,
    string ModeId,
    int ModeVersion,
    bool AdaptationEnabled,
    string AdaptivePolicyId,
    string ConfigHashAlgorithm,
    string ConfigHash,
    ulong GameplaySeed,
    OfflineChallengeOptions AllowedOptions)
{
    public const int CurrentSchemaVersion = 1;
    public const string KindId = "vibesnake-seed-challenge-v1";
    public const string CurrentContentContractId = "vibesnake-core-content@1";
    public const string CodePrefix = "VS1";
    public const int IntegrityHexCharacters = 16;
    public const int MaximumCodeCharacters = 1_024;
    public const OfflineChallengeOptions AllOptions =
        OfflineChallengeOptions.SameSeedRun
        | OfflineChallengeOptions.GhostRace
        | OfflineChallengeOptions.HouseholdRival;

    private static readonly string[] ExactPropertyNames =
    [
        "schemaVersion",
        "kind",
        "rulesetId",
        "rulesVersion",
        "contentContractId",
        "modeId",
        "modeVersion",
        "adaptationEnabled",
        "adaptivePolicyId",
        "configHashAlgorithm",
        "configHash",
        "gameplaySeed",
        "allowedOptions",
    ];

    public static SeedChallengeDescriptor Create(
        RunReplay replay,
        OfflineChallengeOptions allowedOptions = AllOptions)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var verification = replay.Verify();
        if (!verification.IsValid || replay.GameplaySeed is null)
        {
            throw new ArgumentException(
                "A seed challenge requires a verified replay with explicit capture seeds.",
                nameof(replay));
        }

        var initial = SnakeRun.RestoreCanonicalState(replay.InitialCanonicalState);
        EnsureCanonicalProductConfig(initial.Configuration);
        ValidateOptions(allowedOptions);
        return new SeedChallengeDescriptor(
            CurrentSchemaVersion,
            KindId,
            replay.Ruleset.Id,
            replay.Ruleset.Version,
            CurrentContentContractId,
            initial.Configuration.ModeId,
            initial.Configuration.ModeVersion,
            initial.Configuration.EnableAdaptation,
            initial.Configuration.AdaptivePolicyId,
            replay.ConfigHashAlgorithm,
            replay.ConfigHash,
            replay.GameplaySeed.Value,
            allowedOptions);
    }

    public SnakeRun CreateRun(OfflineChallengeOptions requestedOption)
    {
        Validate();
        if (requestedOption == OfflineChallengeOptions.None
            || (requestedOption & (requestedOption - 1)) != 0
            || !AllowedOptions.HasFlag(requestedOption))
        {
            throw new ArgumentException(
                "The requested challenge option is not allowed by this seed code.",
                nameof(requestedOption));
        }

        var mode = RunModeCatalog.Get(ModeId, ModeVersion);
        var config = RunModeCatalog.CreateConfig(mode, AdaptationEnabled);
        if (!string.Equals(config.ComputeConfigHash(), ConfigHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The local product configuration no longer matches this challenge.");
        }

        return SnakeRun.Create(GameplaySeed, config);
    }

    public string Encode()
    {
        Validate();
        var payload = SerializeCanonicalPayload();
        var encoded = Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var integrity = Convert.ToHexString(SHA256.HashData(payload))
            .ToLowerInvariant()[..IntegrityHexCharacters];
        var code = $"{CodePrefix}.{encoded}.{integrity}";
        if (code.Length > MaximumCodeCharacters)
        {
            throw new InvalidOperationException("The seed code exceeds its size bound.");
        }

        return code;
    }

    public static SeedCodeReadResult Read(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)
            || code.Length > MaximumCodeCharacters
            || code.Any(character => character > 127))
        {
            return Failure(SeedCodeReadCode.InvalidFormat, "The seed code format is invalid.");
        }

        var segments = code.Split('.');
        if (segments.Length != 3
            || !string.Equals(segments[0], CodePrefix, StringComparison.Ordinal)
            || !IsLowerHex(segments[2], IntegrityHexCharacters))
        {
            return Failure(SeedCodeReadCode.InvalidFormat, "The seed code format is invalid.");
        }

        byte[] payload;
        try
        {
            var base64 = segments[1].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            payload = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return Failure(SeedCodeReadCode.InvalidFormat, "The seed code payload is invalid.");
        }

        var expectedIntegrity = Convert.ToHexString(SHA256.HashData(payload))
            .ToLowerInvariant()[..IntegrityHexCharacters];
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedIntegrity),
            Encoding.ASCII.GetBytes(segments[2])))
        {
            return Failure(
                SeedCodeReadCode.IntegrityMismatch,
                "The seed code changed after it was created.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 4,
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !HasExactProperties(root, ExactPropertyNames))
            {
                return Failure(
                    SeedCodeReadCode.InvalidFormat,
                    "The seed code has an unknown or missing field.");
            }

            var challenge = new SeedChallengeDescriptor(
                root.GetProperty("schemaVersion").GetInt32(),
                root.GetProperty("kind").GetString()!,
                root.GetProperty("rulesetId").GetString()!,
                root.GetProperty("rulesVersion").GetInt32(),
                root.GetProperty("contentContractId").GetString()!,
                root.GetProperty("modeId").GetString()!,
                root.GetProperty("modeVersion").GetInt32(),
                root.GetProperty("adaptationEnabled").GetBoolean(),
                root.GetProperty("adaptivePolicyId").GetString()!,
                root.GetProperty("configHashAlgorithm").GetString()!,
                root.GetProperty("configHash").GetString()!,
                root.GetProperty("gameplaySeed").GetUInt64(),
                (OfflineChallengeOptions)root.GetProperty("allowedOptions").GetByte());
            return challenge.CompatibilityResult();
        }
        catch (Exception exception) when (
            exception is JsonException
                or InvalidOperationException
                or FormatException
                or OverflowException)
        {
            return Failure(SeedCodeReadCode.InvalidFormat, "The seed code payload is invalid.");
        }
    }

    public void Validate()
    {
        var result = CompatibilityResult();
        if (!result.IsValid)
        {
            throw new ArgumentException(result.Message);
        }
    }

    private SeedCodeReadResult CompatibilityResult()
    {
        if (SchemaVersion != CurrentSchemaVersion || !string.Equals(Kind, KindId, StringComparison.Ordinal))
        {
            return Failure(SeedCodeReadCode.UnsupportedSchema, "The seed code schema is unsupported.");
        }

        if (!string.Equals(RulesetId, RulesetIdentity.CurrentId, StringComparison.Ordinal)
            || RulesVersion != RulesetIdentity.CurrentVersion)
        {
            return Failure(SeedCodeReadCode.UnsupportedRuleset, "The seed code rules are unsupported.");
        }

        if (!string.Equals(ContentContractId, CurrentContentContractId, StringComparison.Ordinal))
        {
            return Failure(SeedCodeReadCode.UnsupportedContent, "The seed code content version is unsupported.");
        }

        if (!RunModeCatalog.IsSupportedIdentity(ModeId, ModeVersion))
        {
            return Failure(SeedCodeReadCode.UnsupportedMode, "The seed code mode is unsupported.");
        }

        if (!string.Equals(ConfigHashAlgorithm, RunConfig.ConfigHashAlgorithmId, StringComparison.Ordinal)
            || !IsLowerHex(ConfigHash, 64)
            || !IsCanonicalConfiguration())
        {
            return Failure(SeedCodeReadCode.UnsupportedConfig, "The seed code configuration is unsupported.");
        }

        if (AllowedOptions == OfflineChallengeOptions.None
            || !AllowedOptions.HasFlag(OfflineChallengeOptions.SameSeedRun)
            || (AllowedOptions & ~AllOptions) != 0)
        {
            return Failure(SeedCodeReadCode.UnsupportedOptions, "The seed code options are unsupported.");
        }

        return new SeedCodeReadResult(SeedCodeReadCode.Valid, "The seed code is valid.", this);
    }

    private bool IsCanonicalConfiguration()
    {
        try
        {
            var mode = RunModeCatalog.Get(ModeId, ModeVersion);
            var config = RunModeCatalog.CreateConfig(mode, AdaptationEnabled);
            return string.Equals(config.AdaptivePolicyId, AdaptivePolicyId, StringComparison.Ordinal)
                && string.Equals(config.ComputeConfigHash(), ConfigHash, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private byte[] SerializeCanonicalPayload()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("kind", Kind);
            writer.WriteString("rulesetId", RulesetId);
            writer.WriteNumber("rulesVersion", RulesVersion);
            writer.WriteString("contentContractId", ContentContractId);
            writer.WriteString("modeId", ModeId);
            writer.WriteNumber("modeVersion", ModeVersion);
            writer.WriteBoolean("adaptationEnabled", AdaptationEnabled);
            writer.WriteString("adaptivePolicyId", AdaptivePolicyId);
            writer.WriteString("configHashAlgorithm", ConfigHashAlgorithm);
            writer.WriteString("configHash", ConfigHash);
            writer.WriteNumber("gameplaySeed", GameplaySeed);
            writer.WriteNumber("allowedOptions", (byte)AllowedOptions);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void EnsureCanonicalProductConfig(RunConfig config)
    {
        var mode = RunModeCatalog.Get(config.ModeId, config.ModeVersion);
        var canonical = RunModeCatalog.CreateConfig(mode, config.EnableAdaptation);
        if (!string.Equals(canonical.ComputeConfigHash(), config.ComputeConfigHash(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Seed codes support only closed product configurations.",
                nameof(config));
        }
    }

    private static void ValidateOptions(OfflineChallengeOptions options)
    {
        if (options == OfflineChallengeOptions.None
            || !options.HasFlag(OfflineChallengeOptions.SameSeedRun)
            || (options & ~AllOptions) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static bool HasExactProperties(JsonElement element, IReadOnlyList<string> names)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        return actual.Length == names.Count
            && actual.SequenceEqual(names, StringComparer.Ordinal);
    }

    private static bool IsLowerHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static SeedCodeReadResult Failure(SeedCodeReadCode code, string message) =>
        new(code, message);
}

public sealed record GhostRaceFrame(
    int StepIndex,
    RunStepResult PlayerResult,
    RunSnapshot Player,
    RunSnapshot Ghost,
    int ScoreDelta,
    int LengthDelta);

/// <summary>
/// Equal-rules local race. The replay advances beside the player but never
/// enters player collision, scoring, random state, or persistence.
/// </summary>
public sealed class GhostRaceSession
{
    private readonly RunReplayPlayback _ghost;

    public GhostRaceSession(SeedChallengeDescriptor challenge, RunReplay replay)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(replay);
        challenge.Validate();
        if (!challenge.AllowedOptions.HasFlag(OfflineChallengeOptions.GhostRace)
            || replay.GameplaySeed != challenge.GameplaySeed
            || !string.Equals(replay.ConfigHash, challenge.ConfigHash, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The replay does not match an allowed equal-rules ghost challenge.",
                nameof(replay));
        }

        PlayerRun = challenge.CreateRun(OfflineChallengeOptions.GhostRace);
        _ghost = new RunReplayPlayback(replay);
    }

    public SnakeRun PlayerRun { get; }

    public RunSnapshot GhostSnapshot => _ghost.CurrentSnapshot;

    public bool GhostComplete => _ghost.IsComplete;

    public bool QueuePlayerDirection(Direction direction) => PlayerRun.QueueDirection(direction);

    public bool TryAdvance(out GhostRaceFrame? frame)
    {
        if (PlayerRun.Status != RunStatus.Running)
        {
            frame = null;
            return false;
        }

        var playerResult = PlayerRun.Step();
        if (!_ghost.IsComplete)
        {
            _ = _ghost.TryAdvance(out _);
        }

        var player = PlayerRun.GetSnapshot();
        var ghost = _ghost.CurrentSnapshot;
        frame = new GhostRaceFrame(
            player.Tick,
            playerResult,
            player,
            ghost,
            player.Score - ghost.Score,
            player.Body.Count - ghost.Body.Count);
        return true;
    }
}
