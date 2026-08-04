namespace VibeSnake.Rules.Tests;

public sealed class RulesEventCatalogTests
{
    [Fact]
    public void Catalog_covers_every_defined_event_kind_exactly_once()
    {
        var defined = Enum.GetValues<RunEventKind>();
        Assert.Equal(defined.Length, RulesEventCatalog.OrderedKinds.Count);
        Assert.Equal(
            defined.OrderBy(kind => (byte)kind),
            RulesEventCatalog.OrderedKinds.OrderBy(kind => (byte)kind));
        Assert.Equal(
            RulesEventCatalog.OrderedKinds.Count,
            RulesEventCatalog.OrderedKinds.Distinct().Count());
    }

    [Fact]
    public void Wire_names_are_stable_snake_case_identifiers()
    {
        foreach (var kind in RulesEventCatalog.OrderedKinds)
        {
            Assert.True(RulesEventCatalog.IsKnown(kind));
            var wire = RulesEventCatalog.ToWireName(kind);
            Assert.Matches("^[a-z]+(_[a-z]+)*$", wire);
        }

        Assert.Equal("near_miss", RulesEventCatalog.ToWireName(RunEventKind.NearMiss));
    }

    [Fact]
    public void Presentation_priority_orders_recovery_above_spawn_and_food()
    {
        Assert.True(
            RulesEventCatalog.PresentationPriority(RunEventKind.CollisionPrevented)
            > RulesEventCatalog.PresentationPriority(RunEventKind.PowerSpawned));
        Assert.True(
            RulesEventCatalog.PresentationPriority(RunEventKind.StarvationWarning)
            > RulesEventCatalog.PresentationPriority(RunEventKind.AteFood));
        Assert.True(
            RulesEventCatalog.PresentationPriority(RunEventKind.Died)
            > RulesEventCatalog.PresentationPriority(RunEventKind.NearMiss));
    }

    [Fact]
    public void Presentation_priority_is_positive_for_every_catalog_kind()
    {
        foreach (var kind in RulesEventCatalog.OrderedKinds)
        {
            Assert.True(
                RulesEventCatalog.PresentationPriority(kind) > 0,
                kind + " must have a positive presentation priority.");
        }
    }

    [Fact]
    public void SelectPrimaryKind_returns_the_highest_priority_kind()
    {
        Assert.Null(RulesEventCatalog.SelectPrimaryKind([]));
        Assert.Equal(
            RunEventKind.Died,
            RulesEventCatalog.SelectPrimaryKind(
            [
                RunEventKind.Moved,
                RunEventKind.AteFood,
                RunEventKind.Died,
                RunEventKind.PowerSpawned,
            ]));
        Assert.Equal(
            RunEventKind.StarvationWarning,
            RulesEventCatalog.SelectPrimaryKind(
            [
                RunEventKind.NearMiss,
                RunEventKind.StarvationWarning,
                RunEventKind.AteFood,
            ]));
    }
}
