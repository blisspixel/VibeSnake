using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.AgentHost;

internal static class AgentToolArgumentFilter
{
    private const int MaximumIdentifierLength = 128;

    private const string RulesResource = "vibesnake://agent/rules";
    private const string ModesResource = "vibesnake://agent/modes";
    private const string StylesResource = "vibesnake://agent/styles";
    private const string RivalsResource = "vibesnake://agent/rivals";
    private const string SignalSchoolResource = "vibesnake://agent/signal-school";

    // The closed vocabularies are read from the same catalogs the tools use, so
    // a new lesson, style, or rival cannot drift out of this rejection path.
    private static IReadOnlyList<string> ModeIds { get; } =
        [RunModeCatalog.ClassicId, RunModeCatalog.VibeId];

    private static IReadOnlyList<string> SeedVisibilities { get; } =
        WireNames<AgentSeedVisibility>();

    private static IReadOnlyList<string> Actions { get; } = WireNames<AgentAction>();

    private static IReadOnlyList<string> Intents { get; } = WireNames<AgentPublicIntent>();

    private static IReadOnlyList<string> ActionProfiles { get; } =
        [
            AgentPassportV4.FourDirectionActionProfile,
            AgentPassportV4.FourDirectionBurstActionProfile,
        ];

    private static IReadOnlyList<string> StyleIds { get; } =
        [.. AgentStyleContractCatalog.All.Select(style => style.Id).Order(StringComparer.Ordinal)];

    private static IReadOnlyList<string> RivalIds { get; } =
        [.. AiPersonalityCatalog.BuiltIn.Select(rival => rival.Id).Order(StringComparer.Ordinal)];

    private static IReadOnlyList<string> LessonIds { get; } =
        [.. AgentSignalSchoolCatalog.All.Select(lesson => lesson.Id).Order(StringComparer.Ordinal)];

    private static IReadOnlyList<string> WireNames<TEnum>()
        where TEnum : struct, Enum =>
        [
            .. Enum.GetNames<TEnum>()
                .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)
                .Order(StringComparer.Ordinal),
        ];

    private static readonly Dictionary<string, ArgumentContract> Contracts =
        new Dictionary<string, ArgumentContract>(StringComparer.Ordinal)
        {
            ["start_match"] = new(
                [
                    Text("modeId", ClosedSet(ModeIds, ModesResource)),
                    Text("seedVisibility", ClosedSet(SeedVisibilities, ModesResource)),
                ],
                [
                    NullableText("gameplaySeed", Seed()),
                    NullableInteger(
                        "maximumSteps",
                        Range(1, AgentMatchOptions.MaximumAllowedSteps)),
                    NullableText("styleContractId", ClosedSet(StyleIds, StylesResource)),
                    NullableText("rivalPersonalityId", ClosedSet(RivalIds, RivalsResource)),
                    Boolean("watchEnabled"),
                    NullableObject("passport"),
                    Text("actionProfile", ClosedSet(ActionProfiles, RulesResource)),
                ]),
            ["start_lesson"] = new(
                [Text("lessonId", ClosedSet(LessonIds, SignalSchoolResource))],
                [
                    Boolean("watchEnabled"),
                    NullableObject("passport"),
                    Text("actionProfile", ClosedSet(ActionProfiles, RulesResource)),
                ]),
            ["observe_match"] = new([Text("matchHandle", Handle())], []),
            ["play_move"] = new(
                [
                    Text("matchHandle", Handle()),
                    Text("idempotencyKey", Token()),
                    Integer("expectedTick", AtLeast(0)),
                    Text("expectedStateHash", StateHash()),
                    Text("action", ClosedSet(Actions, RulesResource)),
                ],
                [Text("declaredIntent", ClosedSet(Intents, RulesResource))]),
            ["play_burst"] = new(
                [
                    Text("matchHandle", Handle()),
                    Text("idempotencyKey", Token()),
                    Integer("expectedTick", AtLeast(0)),
                    Text("expectedStateHash", StateHash()),
                    Text("initialAction", ClosedSet(Actions, RulesResource)),
                    Integer(
                        "maximumSteps",
                        Range(1, AgentBurstRequest.MaximumBurstSteps)),
                ],
                [Text("declaredIntent", ClosedSet(Intents, RulesResource))]),
            ["finish_match"] = new([Text("matchHandle", Handle())], []),
            ["get_match_result"] = new([Text("matchHandle", Handle())], []),
            ["get_exhibition_receipt"] = new([Text("matchHandle", Handle())], []),
            ["save_verified_replay"] = new([Text("matchHandle", Handle())], []),
            ["archive_exhibition"] = new([Text("matchHandle", Handle())], []),
            ["list_exhibitions"] = new(
                [],
                [NullableText("routeIdentityHash", Identifier("a walked route identity"))]),
            ["get_exhibition_story"] = new(
                [Text("receiptHash", Identifier("an archived receipt identity"))],
                []),
            ["get_qualification_report"] = new([], [NullableText("agentId")]),
            ["forget_exhibition"] = new([], [NullableText("receiptHash")]),
            ["record_passport"] = new(
                [],
                [
                    NullableText("matchHandle", Handle()),
                    NullableText("receiptHash", Identifier("an archived receipt identity")),
                ],
                exactlyOneOf: ["matchHandle", "receiptHash"]),
            ["list_passports"] = new(
                [],
                [NullableText("agentId", Identifier("a recorded agent identity"))]),
            ["forget_passport"] = new([], [NullableText("agentId")]),
        };

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create() =>
        next => async (context, cancellationToken) =>
        {
            var error = Validate(context.Params);
            return error ?? await next(context, cancellationToken).ConfigureAwait(false);
        };

    internal static CallToolResult? Validate(CallToolRequestParams request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Contracts.TryGetValue(request.Name, out var contract))
        {
            return null;
        }

        var arguments = request.Arguments;
        var missing = contract.Required
            .Where(field => arguments is null || !arguments.ContainsKey(field.Name))
            .Select(field => field.Name)
            .ToArray();
        var unexpected = arguments?.Keys
            .Where(name => !contract.Allowed.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];
        var mismatched = contract.Fields
            .Where(field => arguments is not null
                && arguments.TryGetValue(field.Name, out var value)
                && !field.Accepts(value))
            .Select(field => (Field: field, Value: arguments![field.Name]))
            .ToArray();

        // A value contract is only consulted once the name and JSON type are
        // already correct, so one wrong argument never produces two complaints.
        var outOfContract = contract.Fields
            .Where(field => field.Value is not null
                && arguments is not null
                && arguments.TryGetValue(field.Name, out var value)
                && field.Accepts(value)
                && value.ValueKind != JsonValueKind.Null
                && !field.Value.IsSatisfied(value))
            .Select(field => (Field: field, Value: arguments![field.Name]))
            .ToArray();
        var choiceSatisfied = contract.ChoiceSatisfied(arguments);
        if (missing.Length == 0
            && unexpected.Length == 0
            && mismatched.Length == 0
            && outOfContract.Length == 0
            && choiceSatisfied)
        {
            return null;
        }

        var problems = new List<string>(5);
        if (!choiceSatisfied)
        {
            problems.Add(
                $"exactly one of {FormatNames(contract.Choice!)} is required");
        }

        if (unexpected.Length > 0)
        {
            problems.Add($"unexpected argument name(s): {FormatNames(unexpected)}");
        }

        if (missing.Length > 0)
        {
            problems.Add($"missing required argument(s): {FormatNames(missing)}");
        }

        if (mismatched.Length > 0)
        {
            var descriptions = mismatched.Select(entry =>
                $"{JsonSerializer.Serialize(entry.Field.Name)} must be {entry.Field.Expectation} "
                + $"but received {DescribeValue(entry.Value)}");
            problems.Add($"wrong argument type(s): {string.Join(", ", descriptions)}");
        }

        if (outOfContract.Length > 0)
        {
            var descriptions = outOfContract.Select(entry =>
                $"{JsonSerializer.Serialize(entry.Field.Name)} must be "
                + $"{entry.Field.Value!.Expectation} but received {DescribeContent(entry.Value)}");
            problems.Add($"out-of-contract argument value(s): {string.Join(", ", descriptions)}");
        }

        var quotingHint = mismatched.Any(entry =>
            entry.Field.Type == ArgumentJsonType.Text
            && entry.Value.ValueKind == JsonValueKind.Number)
            ? " Quote a decimal text value, for example \"42\"."
            : string.Empty;
        var message =
            $"Invalid arguments for '{request.Name}': {string.Join("; ", problems)}. "
            + $"Use the exact discovered camelCase fields. Required: {FormatFields(contract.Required)}. "
            + $"Optional: {FormatFields(contract.Optional)}.{quotingHint} No match state changed.";
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
        };
    }

    private static ArgumentField Text(string name, ArgumentValueContract? value = null) =>
        new(name, ArgumentJsonType.Text, Nullable: false, value);

    private static ArgumentField NullableText(string name, ArgumentValueContract? value = null) =>
        new(name, ArgumentJsonType.Text, Nullable: true, value);

    private static ArgumentField Integer(string name, ArgumentValueContract? value = null) =>
        new(name, ArgumentJsonType.Integer, Nullable: false, value);

    private static ArgumentField NullableInteger(
        string name,
        ArgumentValueContract? value = null) =>
        new(name, ArgumentJsonType.Integer, Nullable: true, value);

    private static ArgumentField Boolean(string name) =>
        new(name, ArgumentJsonType.Boolean, Nullable: false, Value: null);

    private static ArgumentField NullableObject(string name) =>
        new(name, ArgumentJsonType.Object, Nullable: true, Value: null);

    private static ArgumentValueContract ClosedSet(
        IReadOnlyList<string> values,
        string resourceUri)
    {
        var closed = new HashSet<string>(values, StringComparer.Ordinal);
        return new ArgumentValueContract(
            $"one of {FormatNames(values.Order(StringComparer.Ordinal))} from {resourceUri}",
            value => closed.Contains(value.GetString() ?? string.Empty));
    }

    private static ArgumentValueContract Seed() =>
        new(
            "an unsigned 64-bit decimal string such as \"42\"",
            value =>
            {
                var text = value.GetString();
                return text is { Length: > 0 and <= 20 }
                    && ulong.TryParse(
                        text,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out _);
            });

    private static ArgumentValueContract Handle() =>
        new(
            "an opaque match handle returned by start_match or start_lesson",
            value =>
            {
                var text = value.GetString();
                return text is { Length: > 0 }
                    && text.Length <= AgentMatchOptions.MaximumMatchIdLength
                    && text.StartsWith("match_", StringComparison.Ordinal)
                    && text.All(character =>
                        char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
            });

    private static ArgumentValueContract Token() =>
        new(
            "1 to "
                + AgentActionRequest.MaximumIdempotencyKeyLength.ToString(
                    CultureInfo.InvariantCulture)
                + " ASCII letters, digits, \"-\", \"_\", or \".\" characters",
            value =>
            {
                var text = value.GetString();
                return text is { Length: > 0 }
                    && text.Length <= AgentActionRequest.MaximumIdempotencyKeyLength
                    && text.All(character =>
                        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
            });

    private static ArgumentValueContract Identifier(string description) =>
        new(
            $"{description} of 1 to {MaximumIdentifierLength.ToString(CultureInfo.InvariantCulture)} "
                + "non-whitespace characters",
            value =>
            {
                var text = value.GetString();
                return text is { Length: > 0 and <= MaximumIdentifierLength }
                    && !string.IsNullOrWhiteSpace(text);
            });

    private static ArgumentValueContract StateHash() =>
        new(
            "the 1 to 64 character state hash from the observation being acted upon",
            value =>
            {
                var text = value.GetString();
                return text is { Length: > 0 and <= 64 } && !string.IsNullOrWhiteSpace(text);
            });

    private static ArgumentValueContract Range(int minimum, int maximum) =>
        new(
            $"an integer from {minimum.ToString(CultureInfo.InvariantCulture)} through "
                + maximum.ToString(CultureInfo.InvariantCulture),
            value => value.TryGetInt64(out var number)
                && number >= minimum
                && number <= maximum);

    private static ArgumentValueContract AtLeast(int minimum) =>
        new(
            $"an integer of {minimum.ToString(CultureInfo.InvariantCulture)} or more",
            value => value.TryGetInt64(out var number) && number >= minimum);

    private static string DescribeValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.Null => "null",
        _ => "an undefined value",
    };

    // The rejected value is echoed so the caller can see the typo, but it is
    // bounded because an agent may send a very long string.
    private static string DescribeContent(JsonElement value)
    {
        const int MaximumEchoLength = 64;
        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
        var truncated = text.Length > MaximumEchoLength;
        if (truncated)
        {
            text = string.Concat(text.AsSpan(0, MaximumEchoLength), "...");
        }

        return value.ValueKind == JsonValueKind.String
            ? JsonSerializer.Serialize(text)
            : text;
    }

    private static string FormatNames(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => JsonSerializer.Serialize(name)));

    // A tool with no optional arguments says so, because an empty list reads
    // like truncated output rather than a closed contract.
    private static string FormatFields(IEnumerable<ArgumentField> fields)
    {
        var names = fields.Select(field => field.Name).ToArray();
        return names.Length == 0 ? "none" : FormatNames(names);
    }

    private enum ArgumentJsonType : byte
    {
        Text = 0,
        Integer = 1,
        Boolean = 2,
        Object = 3,
    }

    private sealed record ArgumentValueContract(
        string Expectation,
        Func<JsonElement, bool> IsSatisfied);

    private sealed record ArgumentField(
        string Name,
        ArgumentJsonType Type,
        bool Nullable,
        ArgumentValueContract? Value)
    {
        public string Expectation =>
            (Type switch
            {
                ArgumentJsonType.Text => "a JSON string",
                ArgumentJsonType.Integer => "a JSON integer",
                ArgumentJsonType.Boolean => "a JSON boolean",
                _ => "a JSON object",
            })
            + (Nullable ? " or null" : string.Empty);

        public bool Accepts(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return Nullable;
            }

            return Type switch
            {
                ArgumentJsonType.Text => value.ValueKind == JsonValueKind.String,
                ArgumentJsonType.Integer => value.ValueKind == JsonValueKind.Number
                    && value.TryGetInt64(out _),
                ArgumentJsonType.Boolean => value.ValueKind
                    is JsonValueKind.True or JsonValueKind.False,
                _ => value.ValueKind == JsonValueKind.Object,
            };
        }
    }

    private sealed class ArgumentContract
    {
        public ArgumentContract(
            ArgumentField[] required,
            ArgumentField[] optional,
            string[]? exactlyOneOf = null)
        {
            Required = required;
            Optional = optional;
            Fields = [.. required, .. optional];
            Allowed = new HashSet<string>(
                Fields.Select(field => field.Name),
                StringComparer.Ordinal);
            Choice = exactlyOneOf;
        }

        public ArgumentField[] Required { get; }

        public ArgumentField[] Optional { get; }

        public ArgumentField[] Fields { get; }

        public HashSet<string> Allowed { get; }

        // Some tools accept one of two alternative sources rather than a fixed
        // required field. Neither and both are argument mistakes, not states.
        public string[]? Choice { get; }

        public bool ChoiceSatisfied(IDictionary<string, JsonElement>? arguments) =>
            Choice is null
            || Choice.Count(name => arguments is not null
                && arguments.TryGetValue(name, out var value)
                && value.ValueKind != JsonValueKind.Null) == 1;
    }
}
