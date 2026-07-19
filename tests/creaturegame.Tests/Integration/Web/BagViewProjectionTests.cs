using creaturegame.Creatures;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The bag-view wire projection (<see cref="GameSessionManager.ProjectBagView"/>) — the seam that carries a
/// held bag + item catalog to the client's <see cref="BagItemView"/>. Guards the field-level projections that
/// engine tests can't see: <see cref="BagItemView.UsableInBattle"/> (server's single source of truth for
/// "does anything in battle", from the <see cref="creaturegame.Combat.ItemEffects"/> registry — the client no
/// longer re-encodes the category list) and <see cref="BagItemView.RestoresPpAllMoves"/>. A negated/mis-wired
/// projection here would silently hide a usable item (or offer a dead one) with no other test catching it.
/// </summary>
public class BagViewProjectionTests
{
    private static Item MakeItem(int id, ItemCategory category, bool restoresPpAllMoves = false) =>
        new()
        {
            Id = id,
            Name = $"item{id}",
            Category = category,
            Description = $"desc{id}",
            RestoresPpAllMoves = restoresPpAllMoves,
        };

    private static IReadOnlyDictionary<int, Item> Catalog(params Item[] items) =>
        items.ToDictionary(i => i.Id);

    [Fact]
    public void ProjectBagView_MarksItemsWithAnEngineEffectUsableInBattle()
    {
        // Every category the ItemEffects registry implements should project UsableInBattle == true.
        var catalog = Catalog(
            MakeItem(1, ItemCategory.Healing),
            MakeItem(2, ItemCategory.StatusCure),
            MakeItem(3, ItemCategory.PpRestore),
            MakeItem(4, ItemCategory.BattleStatBoost)
        );
        var bag = new Bag();
        foreach (var id in new[] { 1, 2, 3, 4 })
            bag.Add(id, 1);

        var view = GameSessionManager.ProjectBagView(bag, catalog);

        Assert.All(view, v => Assert.True(v.UsableInBattle, $"item {v.Id} ({v.Category})"));
    }

    [Fact]
    public void ProjectBagView_MarksItemsWithoutAnEngineEffectNotUsableInBattle()
    {
        // Ball (catch — deferred) has no in-battle effect yet ⇒ UsableInBattle == false ⇒ the bag menu hides
        // it rather than letting the player waste a turn.
        var catalog = Catalog(MakeItem(1, ItemCategory.Ball));
        var bag = new Bag();
        bag.Add(1, 1);

        var view = GameSessionManager.ProjectBagView(bag, catalog);

        Assert.All(view, v => Assert.False(v.UsableInBattle, $"item {v.Id} ({v.Category})"));
    }

    [Fact]
    public void ProjectBagView_MarksReviveUsableOnlyWhenAPartyMemberIsFainted()
    {
        // Revive is the one state-dependent category: it has an engine effect, but is only usable when a
        // fainted member exists to target — so the menu hides it while the whole roster is up.
        var catalog = Catalog(MakeItem(1, ItemCategory.Revive));
        var bag = new Bag();
        bag.Add(1, 1);

        var healthyParty = new Party(TestCreatures.Make("Lead"));
        healthyParty.Add(TestCreatures.Make("Bench")); // all up
        var upView = GameSessionManager.ProjectBagView(bag, catalog, healthyParty);
        Assert.False(upView.Single().UsableInBattle);

        var faintedParty = new Party(TestCreatures.Make("Lead"));
        var bench = TestCreatures.Make("Bench", hp: 100);
        bench.Attributes.ReceiveDamage(100); // faint the bench member
        faintedParty.Add(bench);
        var downView = GameSessionManager.ProjectBagView(bag, catalog, faintedParty);
        Assert.True(downView.Single().UsableInBattle);
    }

    [Fact]
    public void ProjectBagView_ReviveNotUsableWithNoParty()
    {
        // No party wired (legacy single-creature battle) ⇒ no revive target ⇒ hidden.
        var catalog = Catalog(MakeItem(1, ItemCategory.Revive));
        var bag = new Bag();
        bag.Add(1, 1);

        var view = GameSessionManager.ProjectBagView(bag, catalog);
        Assert.False(view.Single().UsableInBattle);
    }

    [Fact]
    public void ProjectBagView_CarriesRestoresPpAllMovesFlagPerItem()
    {
        var catalog = Catalog(
            MakeItem(1, ItemCategory.PpRestore, restoresPpAllMoves: false), // Ether — one move
            MakeItem(2, ItemCategory.PpRestore, restoresPpAllMoves: true) // Elixir — whole moveset
        );
        var bag = new Bag();
        bag.Add(1, 1);
        bag.Add(2, 1);

        var view = GameSessionManager.ProjectBagView(bag, catalog);

        Assert.False(view.Single(v => v.Id == 1).RestoresPpAllMoves);
        Assert.True(view.Single(v => v.Id == 2).RestoresPpAllMoves);
    }

    [Fact]
    public void ProjectBagView_OmitsZeroQuantityAndUnknownItems_OrderedById()
    {
        var catalog = Catalog(
            MakeItem(3, ItemCategory.Healing),
            MakeItem(1, ItemCategory.StatusCure)
        );
        var bag = new Bag();
        bag.Add(3, 2);
        bag.Add(1, 1);
        bag.Add(99, 5); // not in the catalog — dropped

        var view = GameSessionManager.ProjectBagView(bag, catalog);

        Assert.Equal(new[] { 1, 3 }, view.Select(v => v.Id)); // ordered by id, unknown 99 omitted
        Assert.Equal(2, view.Count);
    }
}
