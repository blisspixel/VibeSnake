using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class OptionalPackRemovalConsentTests
{
    private static readonly InstalledOptionalPack Flow = new(
        "vibesnake.radio.flow-signal",
        "1.0.0",
        "The Flow Signal");

    private static readonly InstalledOptionalPack Night = new(
        "vibesnake.radio.neon-night",
        "1.2.0",
        "Neon Night");

    [Fact]
    public void Request_requires_explicit_confirmation_and_never_targets_player_data()
    {
        var request = OptionalPackRemovalConsent.Request([Flow, Night], Flow.Id);

        Assert.True(request.IsReady);
        Assert.NotNull(request.Consent);
        Assert.True(request.Consent.RequiresExplicitConfirmation);
        Assert.False(request.Consent.RemovesSaveData);
        Assert.False(request.Consent.RemovesProfiles);
        Assert.False(request.Consent.RemovesReplays);
        Assert.Contains("Saves and replays are retained", request.Message);
    }

    [Fact]
    public void Cancel_preserves_the_complete_installed_selection()
    {
        InstalledOptionalPack[] installed = [Flow, Night];
        var consent = OptionalPackRemovalConsent.Request(installed, Flow.Id).Consent!;

        var result = consent.Cancel(installed);

        Assert.True(result.IsSuccess);
        Assert.Equal(installed, result.RemainingPacks);
    }

    [Fact]
    public void Confirm_removes_only_the_selected_optional_pack()
    {
        var consent = OptionalPackRemovalConsent.Request([Flow, Night], Flow.Id).Consent!;

        var result = consent.Confirm([Flow, Night]);

        Assert.True(result.IsSuccess);
        Assert.Equal([Night], result.RemainingPacks);
        Assert.Contains("Saves and replays were retained", result.Message);
    }

    [Fact]
    public void Confirm_rejects_a_stale_or_replaced_pack_without_mutation()
    {
        var consent = OptionalPackRemovalConsent.Request([Flow], Flow.Id).Consent!;
        InstalledOptionalPack[] changed = [Flow with { Version = "1.1.0" }];

        var result = consent.Confirm(changed);

        Assert.False(result.IsSuccess);
        Assert.Equal(OptionalPackRemovalCode.StaleRequest, result.Code);
        Assert.Equal(changed, result.RemainingPacks);
    }

    [Theory]
    [InlineData("vibesnake.core", OptionalPackRemovalCode.CorePackProtected)]
    [InlineData("radio.other", OptionalPackRemovalCode.InvalidPackId)]
    [InlineData("vibesnake.radio.", OptionalPackRemovalCode.InvalidPackId)]
    [InlineData("VibeSnake.radio.other", OptionalPackRemovalCode.InvalidPackId)]
    [InlineData("vibesnake.radio.missing", OptionalPackRemovalCode.NotInstalled)]
    public void Request_rejects_protected_invalid_or_missing_targets(
        string packId,
        OptionalPackRemovalCode expected)
    {
        var request = OptionalPackRemovalConsent.Request([Flow], packId);

        Assert.False(request.IsReady);
        Assert.Equal(expected, request.Code);
        Assert.Null(request.Consent);
    }

    [Fact]
    public void Request_rejects_ambiguous_installed_state()
    {
        var duplicate = OptionalPackRemovalConsent.Request([Flow, Flow], Flow.Id);
        var badVersion = OptionalPackRemovalConsent.Request(
            [Flow with { Version = "1.0" }],
            Flow.Id);

        Assert.Equal(OptionalPackRemovalCode.DuplicateInstalledId, duplicate.Code);
        Assert.Equal(OptionalPackRemovalCode.InvalidPackId, badVersion.Code);
    }
}
