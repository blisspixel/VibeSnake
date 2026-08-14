using System.Text.Json;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentIdentityTests
{
    [Fact]
    public void Identity_catalogs_are_closed_unique_and_case_sensitive()
    {
        Assert.Equal(8, CosmeticSetCatalog.Sets.Count);
        Assert.Equal(
            CosmeticSetCatalog.Sets.Count,
            CosmeticSetCatalog.Sets.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [
                "signal-cyan",
                "coil-gold",
                "pit-orange",
                "archive-magenta",
                "flow-seafoam",
                "bureau-ivory",
                "strike-red",
                "underground-violet",
            ],
            AgentAccentCatalog.All.Select(value => value.Id).ToArray());
        Assert.Equal(
            [
                "flow_signal",
                "chaos_theory",
                "global_coil",
                "ourotron",
                "the_pit",
                "the_bureau",
                "the_strike",
                "underground_scales",
            ],
            StationIdentityCatalog.All.Select(value => value.Id).ToArray());
        Assert.Equal(
            AgentAccentCatalog.All.Count,
            AgentAccentCatalog.All.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            StationIdentityCatalog.All.Count,
            StationIdentityCatalog.All.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            AgentAccentCatalog.All.Count,
            AgentAccentCatalog.All.Select(value => value.Color).Distinct().Count());
        Assert.All(AgentAccentCatalog.All, value =>
        {
            Assert.False(string.IsNullOrWhiteSpace(value.DisplayName));
            Assert.Same(value, AgentAccentCatalog.Get(value.Id));
        });
        Assert.All(StationIdentityCatalog.All, value =>
        {
            Assert.False(string.IsNullOrWhiteSpace(value.DisplayName));
            Assert.Same(value, StationIdentityCatalog.Get(value.Id));
        });

        Assert.Throws<ArgumentException>(() => AgentAccentCatalog.Get("SIGNAL-CYAN"));
        Assert.Throws<ArgumentException>(() => AgentAccentCatalog.Get("unknown"));
        Assert.ThrowsAny<ArgumentException>(() => AgentAccentCatalog.Get(null!));
        Assert.Throws<ArgumentException>(() => StationIdentityCatalog.Get("GLOBAL_COIL"));
        Assert.Throws<ArgumentException>(() => StationIdentityCatalog.Get("unknown"));
        Assert.ThrowsAny<ArgumentException>(() => StationIdentityCatalog.Get(null!));
    }

    [Fact]
    public void Anonymous_passport_uses_closed_defaults_and_no_legacy_presentation_fields()
    {
        var passport = AgentPassportV2.Anonymous;

        Assert.Equal(AgentPassportV2.Contract, passport.Schema);
        Assert.Equal("anonymous-agent", passport.AgentId);
        Assert.Equal("unversioned", passport.PolicyVersion);
        Assert.Equal("External Agent", passport.DisplayName);
        Assert.Equal("classic-signal", passport.AvatarId);
        Assert.Equal(AgentAccentCatalog.SignalCyanId, passport.AccentId);
        Assert.Equal("global_coil", passport.StationId);
        Assert.NotNull(CosmeticSetCatalog.Find(passport.AvatarId));
        Assert.Same(AgentAccentCatalog.Get(passport.AccentId), AgentAccentCatalog.All[0]);
        Assert.Equal("The Global Coil", StationIdentityCatalog.Get(passport.StationId).DisplayName);
        Assert.Equal(
            [
                "AccentId",
                "ActionProfile",
                "AgentId",
                "AvatarId",
                "DisplayName",
                "ObservationProfile",
                "PolicyVersion",
                "Schema",
                "StationId",
            ],
            typeof(AgentPassportV2)
                .GetProperties()
                .Where(property => property.GetMethod?.IsStatic == false)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Passport_json_rejects_legacy_and_mixed_schema_shapes()
    {
        var options = Program.CreateSerializerOptions();
        var json = JsonSerializer.Serialize(AgentPassportV2.Anonymous, options);
        var roundTripped = JsonSerializer.Deserialize<AgentPassportV2>(json, options);

        Assert.Equal(AgentPassportV2.Anonymous, roundTripped);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AgentPassportV2>(
            """
            {
              "schema": "vibesnake-agent-passport-v1",
              "agent_id": "legacy-agent",
              "policy_version": "policy-1",
              "display_name": "Legacy Agent",
              "color": "#64FFFF",
              "shed_id": "agent-default",
              "station_affinity": "open-frequency",
              "observation_profile": "symbolic-step-v1",
              "action_profile": "four-direction-step-v1"
            }
            """,
            options));
        var mixedJson = json.Replace(
            "\"action_profile\"",
            "\"color\":\"#64FFFF\",\"action_profile\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, mixedJson);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AgentPassportV2>(mixedJson, options));
    }
}
