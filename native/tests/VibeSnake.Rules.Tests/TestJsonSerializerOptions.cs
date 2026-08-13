using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Rules.Tests;

internal static class TestJsonSerializerOptions
{
    internal static JsonSerializerOptions CaseInsensitive { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static JsonSerializerOptions CamelCase { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static JsonSerializerOptions CamelCaseIndented { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static JsonSerializerOptions Indented { get; } = new()
    {
        WriteIndented = true,
    };

    internal static JsonSerializerOptions SnakeCase { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal static JsonSerializerOptions SnakeCaseIndented { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    internal static JsonSerializerOptions StrictCamelCase { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}
