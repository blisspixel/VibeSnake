using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentMatchOptionsTests
{
    [Fact]
    public void Options_accept_official_modes_and_preserve_public_identity()
    {
        var options = new AgentMatchOptions(
            "match_01.alpha",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            42UL,
            AgentSeedVisibility.Open,
            maximumSteps: 7);

        Assert.Equal("match_01.alpha", options.MatchId);
        Assert.Equal(RunModeCatalog.ClassicId, options.ModeId);
        Assert.Equal(RunModeCatalog.CurrentModeVersion, options.ModeVersion);
        Assert.Equal(42UL, options.GameplaySeed);
        Assert.Equal(AgentSeedVisibility.Open, options.SeedVisibility);
        Assert.Equal(7, options.MaximumSteps);
        Assert.Null(options.StyleContractId);
        Assert.Null(options.RivalPersonalityId);
        Assert.Same(AgentPassportV1.Anonymous, options.Passport);
        Assert.Equal(RunModeCatalog.ClassicId, options.CreateRunConfig().ModeId);
    }

    [Fact]
    public void Passport_validates_public_identity_and_fixed_profiles()
    {
        var passport = new AgentPassportV1(
            AgentPassportV1.Contract,
            "coil-agent",
            "policy-2",
            "Coil Agent",
            "#a0b1c2",
            "neon-shed",
            "ourotron");
        var options = new AgentMatchOptions(
            "passport",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Open,
            passport: passport);

        Assert.Same(passport, options.Passport);
        Assert.Equal("#A0B1C2", passport.Color);
        Assert.Equal(AgentPassportV1.SymbolicStepObservationProfile, passport.ObservationProfile);
        Assert.Equal(AgentPassportV1.FourDirectionActionProfile, passport.ActionProfile);
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            "wrong", "agent", "v1", "Agent", "#FFFFFF", "shed", "station"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract, "bad id", "v1", "Agent", "#FFFFFF", "shed", "station"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract, "agent", "v1", " Agent", "#FFFFFF", "shed", "station"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract, "agent", "v1", "A\u0001", "#FFFFFF", "shed", "station"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract,
            "agent",
            "v1",
            new string('a', AgentPassportV1.MaximumDisplayNameLength + 1),
            "#FFFFFF",
            "shed",
            "station"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract, "agent", "v1", "Agent", "FFFFFF", "shed", "station"));
        Assert.ThrowsAny<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract, "agent", "v1", "Agent", null!, "shed", "station"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract, "agent", "v1", "Agent", "#ZZZZZZ", "shed", "station"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract, "agent", "v1", "Agent", "#FFFFFF", "bad shed", "station"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract, "agent", "v1", "Agent", "#FFFFFF", "shed", "bad station"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract,
            "agent",
            "v1",
            "Agent",
            "#FFFFFF",
            "shed",
            "station",
            observationProfile: "visual-v1"));
        Assert.Throws<ArgumentException>(() => new AgentPassportV1(
            AgentPassportV1.Contract,
            "agent",
            "v1",
            "Agent",
            "#FFFFFF",
            "shed",
            "station",
            actionProfile: "burst-v1"));
    }

    [Fact]
    public void Options_accept_only_built_in_rivals()
    {
        var options = new AgentMatchOptions(
            "rivalry",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Open,
            rivalPersonalityId: "optimal");

        Assert.Equal("optimal", options.RivalPersonalityId);
        Assert.Throws<ArgumentException>(() => new AgentMatchOptions(
            "unknown-rival",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Open,
            rivalPersonalityId: "unknown"));
    }

    [Fact]
    public void Options_validate_style_contract_mode_compatibility()
    {
        var styled = new AgentMatchOptions(
            "styled",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Blind,
            styleContractId: AgentStyleContractCatalog.CrownchaserId);

        Assert.Equal(AgentStyleContractCatalog.CrownchaserId, styled.StyleContractId);
        Assert.Throws<ArgumentException>(() => new AgentMatchOptions(
            "classic-combo",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Open,
            styleContractId: AgentStyleContractCatalog.CrownchaserId));
        Assert.Throws<ArgumentException>(() => new AgentMatchOptions(
            "unknown-style",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Open,
            styleContractId: "unknown"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not allowed")]
    [InlineData("slash/not-allowed")]
    public void Options_reject_invalid_match_ids(string? matchId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AgentMatchOptions(
            matchId!,
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Blind));
    }

    [Fact]
    public void Options_reject_oversized_match_ids()
    {
        Assert.Throws<ArgumentException>(() => new AgentMatchOptions(
            new string('a', AgentMatchOptions.MaximumMatchIdLength + 1),
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Blind));
    }

    [Theory]
    [InlineData("unknown", 1)]
    [InlineData("classic", 2)]
    public void Options_reject_unknown_mode_identities(string modeId, int modeVersion)
    {
        Assert.Throws<ArgumentException>(() => new AgentMatchOptions(
            "match",
            modeId,
            modeVersion,
            1UL,
            AgentSeedVisibility.Blind));
    }

    [Fact]
    public void Options_reject_unknown_seed_visibility()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentMatchOptions(
            "match",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            (AgentSeedVisibility)255));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AgentMatchOptions.MaximumAllowedSteps + 1)]
    public void Options_reject_invalid_step_limits(int maximumSteps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentMatchOptions(
            "match",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Blind,
            maximumSteps));
    }

    [Fact]
    public void Action_requests_validate_bounded_transport_fields()
    {
        var request = new AgentActionRequest(
            "move-1",
            0,
            "0123456789abcdef",
            AgentAction.Up,
            AgentPublicIntent.SeekFood);

        Assert.Equal("move-1", request.IdempotencyKey);
        Assert.Equal(0, request.ExpectedTick);
        Assert.Equal("0123456789abcdef", request.ExpectedStateHash);
        Assert.Equal(AgentAction.Up, request.Action);
        Assert.Equal(AgentPublicIntent.SeekFood, request.DeclaredIntent);
        Assert.Throws<ArgumentException>(
            () => new AgentActionRequest("bad key", 0, "hash", AgentAction.Continue));
        Assert.Throws<ArgumentException>(
            () => new AgentActionRequest(
                new string('a', AgentActionRequest.MaximumIdempotencyKeyLength + 1),
                0,
                "hash",
                AgentAction.Continue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentActionRequest("move", -1, "hash", AgentAction.Continue));
        Assert.Throws<ArgumentException>(
            () => new AgentActionRequest("move", 0, " ", AgentAction.Continue));
        Assert.Throws<ArgumentException>(
            () => new AgentActionRequest("move", 0, new string('a', 65), AgentAction.Continue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AgentActionRequest(
                "move",
                0,
                "hash",
                AgentAction.Continue,
                (AgentPublicIntent)255));
    }
}
