using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

internal readonly record struct UpstreamInspection(string? LoadError, string[] Errors);

internal static class AgentInteropUpstreamCheck
{
    internal const int MaximumBaselineBytes = 65_536;

    internal const int MaximumDepth = 64;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumDepth,
    };

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumDepth,
    };

    private readonly record struct Pin(string Label, string UrlName, string DigestName);

    private static readonly Pin[] Pins =
    [
        new("specification", "spec_source_url", "spec_source_sha256"),
        new("plugin schema", "plugin_schema_url", "plugin_schema_sha256"),
        new("mcp schema", "mcp_schema_url", "mcp_schema_sha256"),
    ];

    internal static UpstreamInspection Inspect(string repositoryRoot, Func<string, byte[]> fetch)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(fetch);
        try
        {
            var path = Path.GetFullPath(Path.Combine(repositoryRoot, AgentInteropCheck.BaselinePath));
            if (IsReparsePoint(path) || Directory.Exists(path))
            {
                return LoadFailure("interoperability baseline must be a regular file");
            }

            if (!File.Exists(path))
            {
                return LoadFailure("interoperability baseline is missing");
            }

            if (new FileInfo(path).Length > MaximumBaselineBytes)
            {
                return LoadFailure(
                    "interoperability baseline exceeds "
                    + MaximumBaselineBytes.ToString(CultureInfo.InvariantCulture)
                    + " bytes");
            }

            using var document = ParseBaseline(File.ReadAllBytes(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return LoadFailure("the interoperability baseline root must be an object");
            }

            return new UpstreamInspection(null, Probe(document.RootElement, fetch));
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return LoadFailure(exception.Message);
        }
    }

    private static string[] Probe(JsonElement root, Func<string, byte[]> fetch)
    {
        if (!root.TryGetProperty("agent_plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Object)
        {
            return ["agent_plugins must be an object"];
        }

        var errors = new List<string>();
        foreach (var pin in Pins)
        {
            if (!TryReadPin(plugins, pin, out var url, out var expected))
            {
                errors.Add("agent_plugins " + pin.Label + " pin is incomplete");
                continue;
            }

            try
            {
                var actual = Convert.ToHexStringLower(SHA256.HashData(fetch(url)));
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    errors.Add(
                        "upstream "
                        + pin.Label
                        + " digest changed: expected "
                        + expected
                        + ", got "
                        + actual);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add(
                    "could not fetch "
                    + pin.Label
                    + " "
                    + url
                    + ": "
                    + SingleLine(exception.Message));
            }
        }

        return errors.ToArray();
    }

    private static bool TryReadPin(JsonElement plugins, Pin pin, out string url, out string expected)
    {
        url = string.Empty;
        expected = string.Empty;
        if (!plugins.TryGetProperty(pin.UrlName, out var urlNode) || urlNode.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (!plugins.TryGetProperty(pin.DigestName, out var digestNode) || digestNode.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var urlValue = urlNode.GetString()!;
        var digestValue = digestNode.GetString()!;
        if (!urlValue.StartsWith("https://", StringComparison.Ordinal) || !Sha256Pattern.IsMatch(digestValue))
        {
            return false;
        }

        url = urlValue;
        expected = digestValue;
        return true;
    }

    private static JsonDocument ParseBaseline(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Utf8Bom))
        {
            throw new InvalidDataException("interoperability baseline must be UTF-8 without a BOM");
        }

        try
        {
            _ = StrictUtf8.GetString(bytes);
            RejectDuplicateKeys(bytes);
            return JsonDocument.Parse(bytes, DocumentOptions);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException("interoperability baseline is not valid UTF-8");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "interoperability baseline is not valid JSON: " + SingleLine(exception.Message));
        }
    }

    private static void RejectDuplicateKeys(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, ReaderOptions);
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objects.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    objects.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    var name = reader.GetString()!;
                    if (!objects.Peek().Add(name))
                    {
                        throw new InvalidDataException("duplicate JSON key: " + name);
                    }

                    break;
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static UpstreamInspection LoadFailure(string message) =>
        new(SingleLine(message), []);

    private static string SingleLine(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ').Trim();
}
