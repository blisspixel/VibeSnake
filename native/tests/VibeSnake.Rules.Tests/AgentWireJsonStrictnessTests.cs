using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;

namespace VibeSnake.Rules.Tests;

public sealed class AgentWireJsonStrictnessTests
{
    [Fact]
    public void Host_serializer_requires_every_constructor_member_and_rejects_unknown_members()
    {
        var options = Program.CreateSerializerOptions();
        const string valid =
            "{\"schema\":\"vibesnake-agent-match-result-status-v5\","
            + "\"match_handle\":\"match_test\",\"is_available\":false,\"result\":null}";

        var deserialized = JsonSerializer.Deserialize<AgentMatchResultStatusV5>(valid, options);
        Assert.NotNull(deserialized);
        Assert.Equal(AgentMatchResultStatusV5.Contract, deserialized.Schema);
        Assert.False(options.AllowDuplicateProperties);
        Assert.True(options.RespectRequiredConstructorParameters);
        Assert.Equal(JsonUnmappedMemberHandling.Disallow, options.UnmappedMemberHandling);

        string[] requiredMembers = ["schema", "match_handle", "is_available", "result"];
        foreach (var requiredMember in requiredMembers)
        {
            using var document = JsonDocument.Parse(valid);
            var retained = document.RootElement.EnumerateObject()
                .Where(property => property.Name != requiredMember)
                .Select(property => $"\"{property.Name}\":{property.Value.GetRawText()}");
            var missing = "{" + string.Join(",", retained) + "}";

            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<AgentMatchResultStatusV5>(missing, options));
        }

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AgentMatchResultStatusV5>(
                valid[..^1] + ",\"unknown\":true}",
                options));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AgentMatchResultStatusV5>(
                valid.Replace("\"match_handle\"", "\"Match_Handle\"", StringComparison.Ordinal),
                options));
    }

    [Fact]
    public void Host_serializer_rejects_duplicate_known_members_and_integer_enums()
    {
        var options = Program.CreateSerializerOptions();
        const string valid =
            "{\"schema\":\"vibesnake-agent-match-result-status-v5\","
            + "\"match_handle\":\"match_test\",\"is_available\":false,\"result\":null}";
        string[] duplicateMembers =
        [
            "{\"schema\":\"vibesnake-agent-match-result-status-v5\"," + valid[1..],
            valid.Replace(
                "\"is_available\":false",
                "\"is_available\":false,\"is_available\":false",
                StringComparison.Ordinal),
        ];

        foreach (var duplicate in duplicateMembers)
        {
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<AgentMatchResultStatusV5>(duplicate, options));
        }

        Assert.Equal(
            AgentSeedVisibility.Open,
            JsonSerializer.Deserialize<AgentSeedVisibility>("\"open\"", options));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AgentSeedVisibility>("0", options));
    }
}
