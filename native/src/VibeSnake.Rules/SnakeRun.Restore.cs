using System.Globalization;
using System.Text.Json;

namespace VibeSnake.Rules;

public sealed partial class SnakeRun
{
    public const int MaximumCanonicalStateCharacters = 8 * 1024 * 1024;

    public static SnakeRun RestoreCanonicalState(string canonicalState)
    {
        ArgumentNullException.ThrowIfNull(canonicalState);
        if (canonicalState.Length > MaximumCanonicalStateCharacters)
        {
            throw new InvalidDataException(
                "The canonical run state exceeds the size limit.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalState);

        try
        {
            var restored = RestoreCanonicalStateCore(canonicalState);
            if (!string.Equals(
                canonicalState,
                restored.SerializeCanonicalState(),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The state is valid but does not use the canonical encoding.");
            }

            return restored;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
            or FormatException
            or InvalidOperationException
            or KeyNotFoundException
            or ArgumentException
            or OverflowException)
        {
            throw new InvalidDataException("The canonical run state is invalid.", exception);
        }
    }

    private static SnakeRun RestoreCanonicalStateCore(string canonicalState)
    {
        using var document = JsonDocument.Parse(
            canonicalState,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
        var root = RequireObject(document.RootElement, "root");

        RequireEqual(
            ReadInt32(root, "schemaVersion"),
            CanonicalStateSchemaVersion,
            "canonical state schema");
        RequireEqual(
            ReadInt32(root, "rulesVersion"),
            RulesVersion,
            "rules version");
        RequireEqual(
            ReadString(root, "hashAlgorithm"),
            StateHashAlgorithmId,
            "state hash algorithm");
        RequireEqual(
            ReadString(root, "rngAlgorithm"),
            Pcg32.AlgorithmId,
            "random algorithm");

        var configElement = RequireObject(root.GetProperty("config"), "config");
        RequireEqual(
            ReadInt32(configElement, "rulesTickMilliseconds"),
            RunConfig.RulesTickMilliseconds,
            "rules tick duration");
        var config = new RunConfig(
            Width: ReadInt32(configElement, "width"),
            Height: ReadInt32(configElement, "height"),
            StarvationTicks: ReadInt32(configElement, "starvationTicks"),
            MaximumDirectionQueue: ReadInt32(
                configElement,
                "maximumDirectionQueue"),
            FoodScore: ReadInt32(configElement, "foodScore"),
            ComboWindowTicks: ReadInt32(configElement, "comboWindowTicks"),
            SpeedBonusTicks: ReadInt32(configElement, "speedBonusTicks"),
            PowerSpawnIntervalTicks: ReadInt32(
                configElement,
                "powerSpawnIntervalTicks"),
            PowerVisibleTicks: ReadInt32(configElement, "powerVisibleTicks"),
            ShieldDurationTicks: ReadInt32(configElement, "shieldDurationTicks"));
        config.Validate();

        var randomElement = RequireObject(root.GetProperty("random"), "random");
        var randomState = ReadUInt64String(randomElement, "state");
        var randomIncrement = ReadUInt64String(randomElement, "increment");
        var body = ReadPoints(root.GetProperty("body"), config);
        var foodElement = root.GetProperty("food");
        GridPoint? food = foodElement.ValueKind == JsonValueKind.Null
            ? null
            : ReadPoint(foodElement, "food");
        var pendingDirections = ReadDirections(
            root.GetProperty("pendingDirections"),
            config.MaximumDirectionQueue);
        var powerPickup = ReadPowerPickup(root.GetProperty("powerPickup"));

        var restored = new SnakeRun(
            config,
            body,
            ReadEnum<Direction>(root, "direction"),
            food,
            ReadInt32(root, "hungerTicksRemaining"),
            ReadInt32(root, "score"),
            ReadInt32(root, "comboCount"),
            ReadInt32(root, "ticksSinceLastFood"),
            ReadInt32(root, "tick"),
            ReadEnum<RunStatus>(root, "status"),
            ReadEnum<DeathCause>(root, "deathCause"),
            new Pcg32(randomState, randomIncrement, restoreState: true),
            powerPickup,
            ReadInt32(root, "powerSpawnTicksElapsed"),
            ReadInt32(root, "shieldTicksRemaining"),
            pendingDirections);
        restored.ValidateRestoredProductionState();
        return restored;
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
        ?? throw new InvalidDataException($"The {propertyName} value cannot be null.");

    private static ulong ReadUInt64String(JsonElement parent, string propertyName)
    {
        var value = ReadString(parent, propertyName);
        if (!ulong.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed))
        {
            throw new InvalidDataException(
                $"The {propertyName} value must be an unsigned decimal integer.");
        }

        return parsed;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement parent, string propertyName)
        where TEnum : struct, Enum
    {
        var value = parent.GetProperty(propertyName).GetByte();
        var parsed = (TEnum)Enum.ToObject(typeof(TEnum), value);
        if (!Enum.IsDefined(parsed))
        {
            throw new InvalidDataException(
                $"The {propertyName} value is not a defined {typeof(TEnum).Name}.");
        }

        return parsed;
    }

    private static IReadOnlyList<GridPoint> ReadPoints(
        JsonElement element,
        RunConfig config)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The body value must be an array.");
        }

        var capacity = config.Width * config.Height;
        var count = element.GetArrayLength();
        if (count > capacity)
        {
            throw new InvalidDataException("The body exceeds the grid capacity.");
        }

        var points = new List<GridPoint>(count);
        foreach (var pointElement in element.EnumerateArray())
        {
            points.Add(ReadPoint(pointElement, "body segment"));
        }

        return points;
    }

    private static GridPoint ReadPoint(JsonElement element, string name)
    {
        var point = RequireObject(element, name);
        return new GridPoint(
            ReadInt32(point, "x"),
            ReadInt32(point, "y"));
    }

    private static IReadOnlyList<Direction> ReadDirections(
        JsonElement element,
        int maximumCount)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The pendingDirections value must be an array.");
        }

        var count = element.GetArrayLength();
        if (count > maximumCount)
        {
            throw new InvalidDataException(
                "The pending direction queue exceeds its configured capacity.");
        }

        var directions = new List<Direction>(count);
        foreach (var directionElement in element.EnumerateArray())
        {
            var value = directionElement.GetByte();
            var direction = (Direction)value;
            if (!Enum.IsDefined(direction))
            {
                throw new InvalidDataException(
                    "A pending direction is not defined.");
            }

            directions.Add(direction);
        }

        return directions;
    }

    private static PowerPickup? ReadPowerPickup(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var pickup = RequireObject(element, "power pickup");
        return new PowerPickup(
            ReadEnum<PowerKind>(pickup, "kind"),
            ReadPoint(pickup.GetProperty("position"), "power pickup position"),
            ReadInt32(pickup, "visibilityTicksRemaining"));
    }

    private static void RequireEqual<T>(T actual, T expected, string name)
        where T : IEquatable<T>
    {
        if (!actual.Equals(expected))
        {
            throw new InvalidDataException($"Unsupported {name}: {actual}.");
        }
    }

    private void ValidateRestoredProductionState()
    {
        if (Status != RunStatus.Won && Food is null)
        {
            throw new InvalidDataException(
                "A non-winning production state must contain food.");
        }

        if (ComboCount > _body.Count - 1)
        {
            throw new InvalidDataException(
                "The combo count cannot exceed collected growth.");
        }

        if (TicksSinceLastFood > Tick)
        {
            throw new InvalidDataException(
                "Ticks since the last food cannot exceed the run tick.");
        }

        if (ComboCount > 0 && TicksSinceLastFood > _config.ComboWindowTicks)
        {
            throw new InvalidDataException(
                "An active combo cannot exceed its configured window.");
        }

    }
}
