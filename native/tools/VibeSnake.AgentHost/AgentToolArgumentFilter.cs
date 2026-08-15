using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace VibeSnake.AgentHost;

internal static class AgentToolArgumentFilter
{
    private static readonly Dictionary<string, ArgumentContract> Contracts =
        new Dictionary<string, ArgumentContract>(StringComparer.Ordinal)
        {
            ["play_move"] = new(
                ["matchHandle", "idempotencyKey", "expectedTick", "expectedStateHash", "action"],
                ["declaredIntent"]),
            ["play_burst"] = new(
                [
                    "matchHandle",
                    "idempotencyKey",
                    "expectedTick",
                    "expectedStateHash",
                    "initialAction",
                    "maximumSteps",
                ],
                ["declaredIntent"]),
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
            .Where(name => arguments is null || !arguments.ContainsKey(name))
            .ToArray();
        var unexpected = arguments?.Keys
            .Where(name => !contract.Allowed.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (missing.Length == 0 && unexpected.Length == 0)
        {
            return null;
        }

        var problems = new List<string>(2);
        if (unexpected.Length > 0)
        {
            problems.Add($"unexpected argument name(s): {FormatNames(unexpected)}");
        }

        if (missing.Length > 0)
        {
            problems.Add($"missing required argument(s): {FormatNames(missing)}");
        }

        var message =
            $"Invalid arguments for '{request.Name}': {string.Join("; ", problems)}. "
            + $"Use the exact discovered camelCase fields. Required: {FormatNames(contract.Required)}. "
            + $"Optional: {FormatNames(contract.Optional)}. No match state changed.";
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
        };
    }

    private static string FormatNames(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => JsonSerializer.Serialize(name)));

    private sealed class ArgumentContract
    {
        public ArgumentContract(string[] required, string[] optional)
        {
            Required = required;
            Optional = optional;
            Allowed = new HashSet<string>(required.Concat(optional), StringComparer.Ordinal);
        }

        public string[] Required { get; }

        public string[] Optional { get; }

        public HashSet<string> Allowed { get; }
    }
}
