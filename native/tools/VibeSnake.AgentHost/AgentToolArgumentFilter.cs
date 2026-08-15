using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace VibeSnake.AgentHost;

internal static class AgentToolArgumentFilter
{
    private static readonly Dictionary<string, ArgumentContract> Contracts =
        new Dictionary<string, ArgumentContract>(StringComparer.Ordinal)
        {
            ["start_match"] = new(
                [
                    Text("modeId"),
                    Text("seedVisibility"),
                ],
                [
                    NullableText("gameplaySeed"),
                    NullableInteger("maximumSteps"),
                    NullableText("styleContractId"),
                    NullableText("rivalPersonalityId"),
                    Boolean("watchEnabled"),
                    NullableObject("passport"),
                    Text("actionProfile"),
                ]),
            ["start_lesson"] = new(
                [Text("lessonId")],
                [
                    Boolean("watchEnabled"),
                    NullableObject("passport"),
                    Text("actionProfile"),
                ]),
            ["observe_match"] = new([Text("matchHandle")], []),
            ["play_move"] = new(
                [
                    Text("matchHandle"),
                    Text("idempotencyKey"),
                    Integer("expectedTick"),
                    Text("expectedStateHash"),
                    Text("action"),
                ],
                [Text("declaredIntent")]),
            ["play_burst"] = new(
                [
                    Text("matchHandle"),
                    Text("idempotencyKey"),
                    Integer("expectedTick"),
                    Text("expectedStateHash"),
                    Text("initialAction"),
                    Integer("maximumSteps"),
                ],
                [Text("declaredIntent")]),
            ["finish_match"] = new([Text("matchHandle")], []),
            ["get_match_result"] = new([Text("matchHandle")], []),
            ["get_exhibition_receipt"] = new([Text("matchHandle")], []),
            ["save_verified_replay"] = new([Text("matchHandle")], []),
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
        if (missing.Length == 0 && unexpected.Length == 0 && mismatched.Length == 0)
        {
            return null;
        }

        var problems = new List<string>(3);
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

    private static ArgumentField Text(string name) =>
        new(name, ArgumentJsonType.Text, Nullable: false);

    private static ArgumentField NullableText(string name) =>
        new(name, ArgumentJsonType.Text, Nullable: true);

    private static ArgumentField Integer(string name) =>
        new(name, ArgumentJsonType.Integer, Nullable: false);

    private static ArgumentField NullableInteger(string name) =>
        new(name, ArgumentJsonType.Integer, Nullable: true);

    private static ArgumentField Boolean(string name) =>
        new(name, ArgumentJsonType.Boolean, Nullable: false);

    private static ArgumentField NullableObject(string name) =>
        new(name, ArgumentJsonType.Object, Nullable: true);

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

    private static string FormatNames(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => JsonSerializer.Serialize(name)));

    private static string FormatFields(IEnumerable<ArgumentField> fields) =>
        FormatNames(fields.Select(field => field.Name));

    private enum ArgumentJsonType : byte
    {
        Text = 0,
        Integer = 1,
        Boolean = 2,
        Object = 3,
    }

    private sealed record ArgumentField(string Name, ArgumentJsonType Type, bool Nullable)
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
        public ArgumentContract(ArgumentField[] required, ArgumentField[] optional)
        {
            Required = required;
            Optional = optional;
            Fields = [.. required, .. optional];
            Allowed = new HashSet<string>(
                Fields.Select(field => field.Name),
                StringComparer.Ordinal);
        }

        public ArgumentField[] Required { get; }

        public ArgumentField[] Optional { get; }

        public ArgumentField[] Fields { get; }

        public HashSet<string> Allowed { get; }
    }
}
