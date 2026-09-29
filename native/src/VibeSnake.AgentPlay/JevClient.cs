using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VibeSnake.AgentPlay;

public sealed class JevDecisionException : Exception
{
    public JevDecisionException(string message)
        : base(message)
    {
    }

    public JevDecisionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record JevAnswer(
    string Action,
    string Intent,
    double Probability,
    string Model,
    double? CostUsd);

public sealed class JevClient
{
    public const int MaximumResponseBytes = 1_048_576;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly JevEndpoint _endpoint;
    private readonly HttpMessageInvoker _http;
    private readonly Func<string, string?> _environment;
    private readonly TimeSpan _timeout;

    public JevClient(
        JevEndpoint endpoint,
        HttpMessageInvoker http,
        Func<string, string?>? environment = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(http);
        _endpoint = endpoint;
        _http = http;
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Jev timeout must be between one tick and 60 seconds.");
        }
    }

    public async Task<JevAnswer> AskAsync(
        string stateJson,
        IReadOnlyList<string> legalActions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateJson);
        ArgumentNullException.ThrowIfNull(legalActions);
        if (legalActions.Count == 0 || legalActions.Any(static action => string.IsNullOrWhiteSpace(action)))
        {
            throw new ArgumentException("Jev needs at least one legal action.", nameof(legalActions));
        }

        var key = _endpoint.ApiKeyVariable is null
            ? null
            : _environment(_endpoint.ApiKeyVariable);
        if (_endpoint.ApiKeyVariable is not null && string.IsNullOrWhiteSpace(key))
        {
            throw new JevDecisionException(
                "Jev endpoint requires environment variable " + _endpoint.ApiKeyVariable + ".");
        }

        string payload;
        try
        {
            payload = BuildRequest(stateJson, legalActions);
        }
        catch (JsonException exception)
        {
            throw new JevDecisionException("Jev state must be JSON.", exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint.Address)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new JevDecisionException("Jev request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new JevDecisionException("Jev request failed before a response.", exception);
        }

        using (response)
        {
            var body = await ReadBoundedAsync(response, key, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new JevDecisionException(
                    "Jev request failed with HTTP "
                    + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                    + ": "
                    + Redact(Truncate(body), key));
            }

            return ParseAnswer(body, key);
        }
    }

    private string BuildRequest(string stateJson, IReadOnlyList<string> legalActions)
    {
        using var state = JsonDocument.Parse(stateJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _endpoint.Model);
            writer.WritePropertyName("state");
            state.RootElement.WriteTo(writer);
            writer.WritePropertyName("questions");
            writer.WriteStartObject();
            writer.WritePropertyName("action");
            writer.WriteStartObject();
            writer.WriteString("type", "choice");
            writer.WriteString(
                "instructions",
                "Which one legal action should the snake take for this single step?");
            writer.WritePropertyName("criteria");
            writer.WriteStartObject();
            foreach (var action in legalActions)
            {
                writer.WriteString(action, ActionCriterion(action));
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WritePropertyName("intent");
            writer.WriteStartObject();
            writer.WriteString("type", "choice");
            writer.WriteString(
                "instructions",
                "Which spectator label fits this step? The label does not change rules, score, or collision.");
            writer.WritePropertyName("criteria");
            writer.WriteStartObject();
            writer.WriteString("seek_food", "The step is aimed at food.");
            writer.WriteString("seek_power", "The step is aimed at a power.");
            writer.WriteString("preserve_space", "The step keeps later routes open.");
            writer.WriteString("take_risk", "The step accepts a tighter route.");
            writer.WriteString("recover", "The step leaves pressure or a dead end.");
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ActionCriterion(string action) => action switch
    {
        "continue" => "Keep the current heading.",
        "up" => "Turn up.",
        "right" => "Turn right.",
        "down" => "Turn down.",
        "left" => "Turn left.",
        _ => "Take this legal action.",
    };

    private JevAnswer ParseAnswer(string body, string? key)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object)
            {
                throw new JevDecisionException("Jev response did not contain an answers object.");
            }

            if (!answers.TryGetProperty("action", out var actionElement)
                || actionElement.ValueKind != JsonValueKind.Object
                || !actionElement.TryGetProperty("choice", out var choiceElement)
                || choiceElement.ValueKind != JsonValueKind.String)
            {
                throw new JevDecisionException("Jev response did not contain an action choice.");
            }

            var action = choiceElement.GetString() ?? string.Empty;
            var intent = "undeclared";
            if (answers.TryGetProperty("intent", out var intentElement)
                && intentElement.ValueKind == JsonValueKind.Object
                && intentElement.TryGetProperty("choice", out var intentChoice)
                && intentChoice.ValueKind == JsonValueKind.String
                && intentChoice.GetString() is { Length: > 0 } intentValue)
            {
                intent = intentValue;
            }

            var probability = ReadProbability(actionElement, action);
            var model = root.TryGetProperty("model", out var modelElement)
                && modelElement.ValueKind == JsonValueKind.String
                ? modelElement.GetString() ?? _endpoint.Model
                : _endpoint.Model;
            double? cost = null;
            if (root.TryGetProperty("usage", out var usage)
                && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("cost", out var costElement)
                && costElement.ValueKind == JsonValueKind.Number
                && costElement.TryGetDouble(out var parsedCost)
                && double.IsFinite(parsedCost)
                && parsedCost >= 0)
            {
                cost = parsedCost;
            }

            return new JevAnswer(action, intent, probability, model, cost);
        }
        catch (JevDecisionException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new JevDecisionException(
                "Jev response was not readable JSON: " + Redact(Truncate(exception.Message), key),
                exception);
        }
    }

    private static double ReadProbability(JsonElement action, string choice)
    {
        if (action.TryGetProperty("probabilities", out var probabilities)
            && probabilities.ValueKind == JsonValueKind.Object
            && probabilities.TryGetProperty(choice, out var selected)
            && selected.ValueKind == JsonValueKind.Number
            && selected.TryGetDouble(out var probability)
            && double.IsFinite(probability))
        {
            return Math.Clamp(probability, 0, 1);
        }

        if (action.TryGetProperty("confidence", out var confidence)
            && confidence.ValueKind == JsonValueKind.Number
            && confidence.TryGetDouble(out var confidenceValue)
            && double.IsFinite(confidenceValue))
        {
            return Math.Clamp(confidenceValue, 0, 1);
        }

        return 0;
    }

    private static async Task<string> ReadBoundedAsync(
        HttpResponseMessage response,
        string? key,
        CancellationToken cancellationToken)
    {
        var content = response.Content;
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new JevDecisionException("Jev response exceeds the 1048576-byte limit.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaximumResponseBytes)
            {
                throw new JevDecisionException("Jev response exceeds the 1048576-byte limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Redact(Encoding.UTF8.GetString(buffer.ToArray()), key);
    }

    private static string Redact(string text, string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            text = text.Replace(key, "[redacted]", StringComparison.Ordinal);
        }

        return text;
    }

    private static string Truncate(string text)
    {
        const int limit = 300;
        if (text.Length <= limit)
        {
            return text.Replace('\r', ' ').Replace('\n', ' ');
        }

        return text[..limit].Replace('\r', ' ').Replace('\n', ' ');
    }
}
