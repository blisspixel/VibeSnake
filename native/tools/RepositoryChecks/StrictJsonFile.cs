using System.Text;
using System.Text.Json;

namespace RepositoryChecks;

internal static class StrictJsonFile
{
    internal const int MaximumDepth = 64;
    internal const long MaximumBytes = 4 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal readonly record struct Read(JsonDocument? Document, byte[]? Bytes);

    internal static Read Load(string path, string label, List<string> errors)
    {
        var display = Display(path);
        if (string.IsNullOrWhiteSpace(path)
            || !File.Exists(path)
            || Directory.Exists(path)
            || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            errors.Add($"missing {label}: {display}");
            return default;
        }

        var length = new FileInfo(path).Length;
        if (length > MaximumBytes)
        {
            errors.Add($"{label} exceeds the {MaximumBytes}-byte limit: {display}");
            return default;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"unreadable {label}: {display}: {SingleLine(exception.Message)}");
            return default;
        }

        try
        {
            _ = StrictUtf8.GetString(bytes);
            RejectDuplicateProperties(bytes);
            var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaximumDepth,
                });
            return new Read(document, bytes);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or JsonException
            or DecoderFallbackException)
        {
            errors.Add($"unreadable {label}: {display}: {SingleLine(exception.Message)}");
            return new Read(null, bytes);
        }
    }

    internal static string Display(string path) => path.Replace('\\', '/');

    internal static string Quote(string value) =>
        "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    internal static string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => Quote(value.GetString() ?? string.Empty),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        JsonValueKind.Null => "None",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(Format)) + "]",
        JsonValueKind.Object => "{" + string.Join(
            ", ",
            value.EnumerateObject().Select(property => Quote(property.Name) + ": " + Format(property.Value))) + "}",
        _ => "None",
    };

    internal static string FormatStrings(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(Quote)) + "]";

    internal static string FormatFields(IEnumerable<string> fields) =>
        FormatStrings(fields.OrderBy(field => field, StringComparer.Ordinal));

    internal static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    internal static void RejectDuplicateProperties(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(
            json,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth,
            });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objects.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    if (objects.Count > 0)
                    {
                        objects.Pop();
                    }

                    break;
                case JsonTokenType.PropertyName:
                    if (objects.Count == 0)
                    {
                        throw new InvalidDataException("JSON property is outside an object");
                    }

                    var name = reader.GetString() ?? string.Empty;
                    if (!objects.Peek().Add(name))
                    {
                        throw new InvalidDataException($"duplicate JSON field: {name}");
                    }

                    break;
            }
        }
    }
}
