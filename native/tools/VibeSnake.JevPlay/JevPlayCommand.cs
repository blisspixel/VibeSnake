using System.Globalization;
using VibeSnake.AgentPlay;

namespace VibeSnake.JevPlay;

public static class JevPlayCommand
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        Func<JevEndpoint, HttpMessageInvoker>? clientFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (arguments.Count == 0 || arguments[0] is "-h" or "--help" or "help")
        {
            WriteUsage(output);
            return arguments.Count == 0 ? 2 : 0;
        }

        if (!string.Equals(arguments[0], "watch", StringComparison.Ordinal))
        {
            error.WriteLine("Unknown Jev command. Use: watch");
            WriteUsage(error);
            return 2;
        }

        if (!TryParse(arguments, error, out var endpoint, out var seed, out var steps, out var minimum))
        {
            return 2;
        }

        var ownsClient = clientFactory is null;
        var http = ownsClient
            ? new HttpClient(CreateHandler())
            : clientFactory!(endpoint);
        try
        {
            var client = new JevClient(endpoint, http);
            var report = await JevWatch.PlayAsync(client, seed, steps, minimum, cancellationToken)
                .ConfigureAwait(false);
            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Jev watch seed={report.Seed} route={endpoint.Kind} model={endpoint.Model} steps={report.Steps} status={report.Status} score={report.Score}"));
            foreach (var line in report.Lines)
            {
                output.WriteLine(JevSnakeAdvisor.FormatStep(line));
            }

            if (report.CostUsd is { } cost)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"costUsd={cost:0.000000}"));
            }

            return 0;
        }
        catch (JevDecisionException exception)
        {
            error.WriteLine("Jev watch failed: " + exception.Message);
            return 1;
        }
        finally
        {
            if (ownsClient)
            {
                http.Dispose();
            }
        }
    }

    private static bool TryParse(
        IReadOnlyList<string> arguments,
        TextWriter error,
        out JevEndpoint endpoint,
        out ulong seed,
        out int steps,
        out double minimum)
    {
        endpoint = JevEndpoint.OpenRouterDecisions();
        seed = 1;
        steps = 100;
        minimum = JevSnakeAdvisor.DefaultMinimumProbability;
        string? route = null;
        string? model = null;
        Uri? localAddress = null;
        for (var index = 1; index < arguments.Count; index++)
        {
            var name = arguments[index];
            if (index + 1 >= arguments.Count)
            {
                error.WriteLine("Missing value for " + name);
                return false;
            }

            var value = arguments[++index];
            switch (name)
            {
                case "--route":
                    route = value;
                    break;
                case "--model":
                    model = value;
                    break;
                case "--seed":
                    if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                    {
                        error.WriteLine("Seed must be a non-negative integer.");
                        return false;
                    }

                    break;
                case "--steps":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out steps))
                    {
                        error.WriteLine("Steps must be an integer from 1 through 2000.");
                        return false;
                    }

                    break;
                case "--min-probability":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out minimum))
                    {
                        error.WriteLine("Minimum probability must be a number from 0 through 1.");
                        return false;
                    }

                    break;
                case "--endpoint":
                    if (!Uri.TryCreate(value, UriKind.Absolute, out localAddress))
                    {
                        error.WriteLine("Endpoint must be an absolute http or https URL.");
                        return false;
                    }

                    break;
                default:
                    error.WriteLine("Unknown option " + name);
                    return false;
            }
        }

        try
        {
            endpoint = (route ?? "openrouter") switch
            {
                "openrouter" => JevEndpoint.OpenRouterDecisions(model),
                "openrouter-systemone" => JevEndpoint.OpenRouterSystemOne(model),
                "typesafe" => JevEndpoint.TypeSafeHosted(model),
                "local" => localAddress is null
                    ? throw new ArgumentException("A local route requires --endpoint.")
                    : JevEndpoint.Local(localAddress, model ?? "local-jev"),
                _ => throw new ArgumentException("Route must be openrouter, openrouter-systemone, typesafe, or local."),
            };
        }
        catch (ArgumentException exception)
        {
            error.WriteLine(exception.Message);
            return false;
        }

        if (steps is < 1 or > JevWatch.MaximumSteps)
        {
            error.WriteLine("Steps must be an integer from 1 through 2000.");
            return false;
        }

        if (!double.IsFinite(minimum) || minimum is < 0 or > 1)
        {
            error.WriteLine("Minimum probability must be a number from 0 through 1.");
            return false;
        }

        return true;
    }

    public static HttpClientHandler CreateHandler() =>
        new()
        {
            AllowAutoRedirect = false,
        };

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("VibeSnake.JevPlay watch --route openrouter|openrouter-systemone|typesafe|local [--model id] [--seed n] [--steps n] [--min-probability 0.34] [--endpoint url]");
        writer.WriteLine("OpenRouter reads OPENROUTER_API_KEY. TypeSafe reads TYPESAFE_API_KEY. Local endpoints send no key.");
        writer.WriteLine("The watch prints one factual ticker line per step. It does not change the offline Let's Play channels.");
    }
}
