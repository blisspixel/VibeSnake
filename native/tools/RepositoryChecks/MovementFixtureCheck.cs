using System.Numerics;
using System.Text;
using System.Text.Json;

namespace RepositoryChecks;

public static class MovementFixtureCheck
{
    public const int CaseCount = 100;
    public const int SchemaVersion = 2;
    public const int StepsPerCase = 256;
    public const string Contract = "movement-input-long-v2";
    public const string FixtureRelativePath = "tests/fixtures/shared/core_movement_v2.json";

    private const uint CommandSeedMask = 0xC0115EED;
    private const int GridHeight = 33;
    private const int GridWidth = 64;
    private const int MaximumDirectionQueue = 3;
    private const string RandomnessPolicy = "positions-injected-or-random-output-normalized-v2";
    private const string SourceEngine = "python-production-snake-v2";

    private static readonly TraceDirection[] PythonDirectionOrder =
    [
        TraceDirection.Up,
        TraceDirection.Down,
        TraceDirection.Left,
        TraceDirection.Right,
    ];

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        try
        {
            var expected = BuildFixtureBytes();
            var actual = LargeCanonicalFixtureFile.Read(
                repositoryRoot,
                FixtureRelativePath,
                "Movement fixture");
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                return Failed(
                    "Movement fixture is stale or noncanonical; run "
                        + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
                        + "-- movement-write .");
            }

            return Passed("verified", expected.Length);
        }
        catch (Exception exception) when (
            LargeCanonicalFixtureFile.IsExpectedFailure(exception))
        {
            return Failed(LargeCanonicalFixtureFile.SingleLine(exception.Message));
        }
    }

    public static RepositoryCheckResult Write(string repositoryRoot)
    {
        try
        {
            var bytes = BuildFixtureBytes();
            LargeCanonicalFixtureFile.Write(
                repositoryRoot,
                FixtureRelativePath,
                "Movement fixture",
                bytes);

            var verification = Inspect(repositoryRoot);
            if (!verification.Passed)
            {
                return new RepositoryCheckResult(
                    "Movement fixture",
                    false,
                    string.Empty,
                    verification.Failures
                        .Select(failure => "write verification failed: " + failure)
                        .ToArray());
            }

            return Passed("written", bytes.Length);
        }
        catch (Exception exception) when (
            LargeCanonicalFixtureFile.IsExpectedFailure(exception))
        {
            return Failed(LargeCanonicalFixtureFile.SingleLine(exception.Message));
        }
    }

    internal static byte[] BuildFixtureBytes() =>
        BuildFixtureBytes(CaseCount, StepsPerCase);

    internal static byte[] BuildFixtureBytes(int caseCount, int stepsPerCase)
    {
        if (caseCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(caseCount),
                caseCount,
                "case count must be positive");
        }

        if (stepsPerCase <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepsPerCase),
                stepsPerCase,
                "steps per case must be positive");
        }

        var totalSteps = checked(caseCount * stepsPerCase);
        return LargeCanonicalFixtureJson.Render("Movement fixture", writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("case_count", caseCount);
            writer.WritePropertyName("cases");
            writer.WriteStartArray();
            for (var seed = 0; seed < caseCount; seed++)
            {
                WriteCase(writer, seed, stepsPerCase);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("comparison_scope");
            WriteStringArray(
                writer,
                [
                    "bounded_direction_queue",
                    "command_acceptance",
                    "duplicate_rejection",
                    "reversal_rejection",
                    "overflow_rejection",
                    "direction_consumption",
                    "head_position",
                    "body_length",
                    "edge_wrapping",
                    "survival",
                ]);
            writer.WriteString("contract", Contract);
            writer.WritePropertyName("direction_symbols");
            writer.WriteStartObject();
            writer.WriteString("DOWN", "D");
            writer.WriteString("LEFT", "L");
            writer.WriteString("RIGHT", "R");
            writer.WriteString("UP", "U");
            writer.WriteEndObject();
            writer.WritePropertyName("excluded_scope");
            WriteStringArray(
                writer,
                [
                    "food",
                    "growth",
                    "score",
                    "combo",
                    "starvation",
                    "collision",
                    "random_stream",
                ]);
            writer.WritePropertyName("grid");
            writer.WriteStartObject();
            writer.WriteNumber("height", GridHeight);
            writer.WriteNumber("width", GridWidth);
            writer.WriteEndObject();
            writer.WriteString("randomness_policy", RandomnessPolicy);
            writer.WritePropertyName("ruleset");
            writer.WriteStartObject();
            writer.WriteString("id", "vibesnake-core");
            writer.WriteNumber("version", 4);
            writer.WriteEndObject();
            writer.WriteNumber("schema_version", SchemaVersion);
            writer.WriteString("source_engine", SourceEngine);
            writer.WritePropertyName("step_encoding");
            WriteStringArray(
                writer,
                [
                    "command_symbols",
                    "command_acceptance_bits",
                    "direction_symbol",
                    "head_x",
                    "head_y",
                    "body_length",
                    "pending_direction_symbols",
                    "wrapped",
                    "alive",
                ]);
            writer.WriteNumber("steps_per_case", stepsPerCase);
            writer.WriteNumber("total_steps", totalSteps);
            writer.WriteEndObject();
        });
    }

    private static void WriteCase(
        Utf8JsonWriter writer,
        int seed,
        int stepsPerCase)
    {
        const int startX = GridWidth / 2;
        const int startY = GridHeight / 2;

        var random = new PythonRandom(unchecked((uint)seed) ^ CommandSeedMask);
        var state = new MovementState(startX, startY, TraceDirection.Right);

        writer.WriteStartObject();
        writer.WriteString("id", $"movement-seed-{seed:000}");
        writer.WritePropertyName("initial");
        writer.WriteStartObject();
        writer.WritePropertyName("body");
        writer.WriteStartArray();
        writer.WriteStartArray();
        writer.WriteNumberValue(startX);
        writer.WriteNumberValue(startY);
        writer.WriteEndArray();
        writer.WriteEndArray();
        writer.WriteString("direction", "RIGHT");
        writer.WriteEndObject();
        writer.WriteNumber("seed", seed);
        writer.WritePropertyName("steps");
        writer.WriteStartArray();

        for (var stepIndex = 0; stepIndex < stepsPerCase; stepIndex++)
        {
            var commandSymbols = new StringBuilder(5);
            var acceptanceBits = new StringBuilder(5);
            if (stepIndex >= Math.Min(40, stepsPerCase))
            {
                var commandCount = random.NextBelow(6);
                for (var commandIndex = 0; commandIndex < commandCount; commandIndex++)
                {
                    var command = PythonDirectionOrder[random.NextBelow(
                        PythonDirectionOrder.Length)];
                    commandSymbols.Append(Symbol(command));
                    acceptanceBits.Append(state.QueueDirection(command) ? '1' : '0');
                }
            }

            var wrapped = state.Move();
            writer.WriteStartArray();
            writer.WriteStringValue(commandSymbols.ToString());
            writer.WriteStringValue(acceptanceBits.ToString());
            writer.WriteStringValue(Symbol(state.Direction).ToString());
            writer.WriteNumberValue(state.HeadX);
            writer.WriteNumberValue(state.HeadY);
            writer.WriteNumberValue(1);
            writer.WriteStringValue(state.PendingSymbols());
            writer.WriteBooleanValue(wrapped);
            writer.WriteBooleanValue(true);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static char Symbol(TraceDirection direction) => direction switch
    {
        TraceDirection.Up => 'U',
        TraceDirection.Right => 'R',
        TraceDirection.Down => 'D',
        TraceDirection.Left => 'L',
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "unknown direction"),
    };

    private static bool IsOpposite(
        TraceDirection first,
        TraceDirection second) =>
        (first == TraceDirection.Up && second == TraceDirection.Down)
        || (first == TraceDirection.Down && second == TraceDirection.Up)
        || (first == TraceDirection.Left && second == TraceDirection.Right)
        || (first == TraceDirection.Right && second == TraceDirection.Left);

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        IReadOnlyList<string> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static RepositoryCheckResult Passed(string action, int byteCount) =>
        new(
            "Movement fixture",
            true,
            $"Shared Movement fixture {action}: cases={CaseCount} steps={CaseCount * StepsPerCase} bytes={byteCount}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Movement fixture", false, string.Empty, [failure]);

    private enum TraceDirection
    {
        Up,
        Down,
        Left,
        Right,
    }

    private sealed class MovementState(
        int headX,
        int headY,
        TraceDirection direction)
    {
        private readonly Queue<TraceDirection> pending = new(MaximumDirectionQueue);

        internal int HeadX { get; private set; } = headX;

        internal int HeadY { get; private set; } = headY;

        internal TraceDirection Direction { get; private set; } = direction;

        internal bool QueueDirection(TraceDirection command)
        {
            if (pending.Count >= MaximumDirectionQueue)
            {
                return false;
            }

            var effective = pending.Count == 0 ? Direction : pending.Last();
            if (command == effective || IsOpposite(command, effective))
            {
                return false;
            }

            pending.Enqueue(command);
            return true;
        }

        internal bool Move()
        {
            if (pending.TryDequeue(out var proposed)
                && !IsOpposite(proposed, Direction))
            {
                Direction = proposed;
            }

            var beforeX = HeadX;
            var beforeY = HeadY;
            switch (Direction)
            {
                case TraceDirection.Up:
                    HeadY = (HeadY + GridHeight - 1) % GridHeight;
                    break;
                case TraceDirection.Down:
                    HeadY = (HeadY + 1) % GridHeight;
                    break;
                case TraceDirection.Left:
                    HeadX = (HeadX + GridWidth - 1) % GridWidth;
                    break;
                case TraceDirection.Right:
                    HeadX = (HeadX + 1) % GridWidth;
                    break;
                default:
                    throw new InvalidDataException("Movement fixture direction is invalid.");
            }

            return Math.Abs(HeadX - beforeX) > 1 || Math.Abs(HeadY - beforeY) > 1;
        }

        internal string PendingSymbols() =>
            string.Concat(pending.Select(Symbol));
    }

    // CPython's integer-seeded MT19937 and _randbelow_with_getrandbits path.
    // The frozen source uses only randrange(6) and choice over four directions,
    // so no floating-point random contract is present here.
    private sealed class PythonRandom
    {
        private const int StateSize = 624;
        private const int MiddleWord = 397;
        private const uint MatrixA = 0x9908B0DF;
        private const uint UpperMask = 0x80000000;
        private const uint LowerMask = 0x7FFFFFFF;

        private readonly uint[] state = new uint[StateSize];
        private int index;

        internal PythonRandom(uint seed)
        {
            InitializeByArray([seed]);
        }

        internal int NextBelow(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveUpperBound),
                    exclusiveUpperBound,
                    "upper bound must be positive");
            }

            var bitCount = 32 - BitOperations.LeadingZeroCount(
                checked((uint)exclusiveUpperBound));
            uint candidate;
            do
            {
                candidate = NextUInt32() >> (32 - bitCount);
            }
            while (candidate >= exclusiveUpperBound);

            return checked((int)candidate);
        }

        private void Initialize(uint seed)
        {
            state[0] = seed;
            for (var position = 1; position < StateSize; position++)
            {
                state[position] = unchecked(
                    1_812_433_253U
                        * (state[position - 1] ^ (state[position - 1] >> 30))
                        + (uint)position);
            }

            index = StateSize;
        }

        private void InitializeByArray(IReadOnlyList<uint> keys)
        {
            Initialize(19_650_218U);
            var stateIndex = 1;
            var keyIndex = 0;
            for (var remaining = Math.Max(StateSize, keys.Count); remaining > 0; remaining--)
            {
                state[stateIndex] = unchecked(
                    (state[stateIndex]
                        ^ ((state[stateIndex - 1] ^ (state[stateIndex - 1] >> 30))
                            * 1_664_525U))
                    + keys[keyIndex]
                    + (uint)keyIndex);
                stateIndex++;
                keyIndex++;
                if (stateIndex >= StateSize)
                {
                    state[0] = state[StateSize - 1];
                    stateIndex = 1;
                }

                if (keyIndex >= keys.Count)
                {
                    keyIndex = 0;
                }
            }

            for (var remaining = StateSize - 1; remaining > 0; remaining--)
            {
                state[stateIndex] = unchecked(
                    (state[stateIndex]
                        ^ ((state[stateIndex - 1] ^ (state[stateIndex - 1] >> 30))
                            * 1_566_083_941U))
                    - (uint)stateIndex);
                stateIndex++;
                if (stateIndex >= StateSize)
                {
                    state[0] = state[StateSize - 1];
                    stateIndex = 1;
                }
            }

            state[0] = UpperMask;
        }

        private uint NextUInt32()
        {
            if (index >= StateSize)
            {
                Twist();
            }

            var value = state[index++];
            value ^= value >> 11;
            value ^= (value << 7) & 0x9D2C5680;
            value ^= (value << 15) & 0xEFC60000;
            value ^= value >> 18;
            return value;
        }

        private void Twist()
        {
            for (var position = 0; position < StateSize - MiddleWord; position++)
            {
                var value = (state[position] & UpperMask)
                    | (state[position + 1] & LowerMask);
                state[position] = state[position + MiddleWord]
                    ^ (value >> 1)
                    ^ ((value & 1) == 0 ? 0 : MatrixA);
            }

            for (var position = StateSize - MiddleWord; position < StateSize - 1; position++)
            {
                var value = (state[position] & UpperMask)
                    | (state[position + 1] & LowerMask);
                state[position] = state[position + (MiddleWord - StateSize)]
                    ^ (value >> 1)
                    ^ ((value & 1) == 0 ? 0 : MatrixA);
            }

            var finalValue = (state[StateSize - 1] & UpperMask)
                | (state[0] & LowerMask);
            state[StateSize - 1] = state[MiddleWord - 1]
                ^ (finalValue >> 1)
                ^ ((finalValue & 1) == 0 ? 0 : MatrixA);
            index = 0;
        }
    }
}
