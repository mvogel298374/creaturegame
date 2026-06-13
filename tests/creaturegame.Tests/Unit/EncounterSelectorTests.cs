using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class EncounterSelectorTests
{
    [Fact]
    public void EncounterSelector_Bst_SumsAllFiveStats()
    {
        var s = new PokemonSpecies
        {
            BaseHP = 45,
            BaseAttack = 49,
            BaseDefense = 49,
            BaseSpecial = 65,
            BaseSpeed = 45,
        };
        Assert.Equal(253, EncounterSelector.Bst(s));
    }

    [Fact]
    public void EncounterSelector_PickByBst_ReturnsSpeciesWithinFifteenPercent()
    {
        // Pool: one species at exactly playerBst, one far outside ±15%.
        int playerBst = 300;
        var pool = new List<PokemonSpecies>
        {
            Species(1, 60, 60, 60, 60, 60), // BST 300 — inside window
            Species(2, 20, 20, 20, 20, 20), // BST 100 — outside ±15% of 300
        };

        var result = EncounterSelector.PickByBst(pool, playerBst);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }

    [Fact]
    public void EncounterSelector_PickByBst_FallsBack_WhenNoCandidatesInDefaultWindow()
    {
        // Nothing within ±15% (playerBst=300, only species at BST 100 and 500).
        // Should widen until it finds something — both are within ±50% / 1.0 window.
        int playerBst = 300;
        var pool = new List<PokemonSpecies>
        {
            Species(1, 20, 20, 20, 20, 20), // BST 100
            Species(2, 100, 100, 100, 100, 100), // BST 500
        };

        var result = EncounterSelector.PickByBst(pool, playerBst);

        Assert.NotNull(result); // fallback must return something
    }

    [Fact]
    public void EncounterSelector_PickByBst_ReturnsNull_WhenPoolIsEmpty()
    {
        var result = EncounterSelector.PickByBst([], 300);
        Assert.Null(result);
    }

    [Fact]
    public void EncounterSelector_PickByBst_NeverExceedsPoolMembers()
    {
        // Run many times; result must always come from the pool.
        var pool = new List<PokemonSpecies>
        {
            Species(1, 50, 50, 50, 50, 50),
            Species(2, 55, 55, 55, 55, 55),
            Species(3, 60, 60, 60, 60, 60),
        };
        var ids = pool.Select(s => s.Id).ToHashSet();

        for (int i = 0; i < 100; i++)
        {
            var result = EncounterSelector.PickByBst(pool, 270);
            Assert.NotNull(result);
            Assert.Contains(result.Id, ids);
        }
    }

    [Fact]
    public void EncounterSelector_PickByBst_SameSeed_PicksSameSpecies()
    {
        // Several candidates inside the ±15% window so the pick is a real random choice.
        var pool = new List<PokemonSpecies>
        {
            Species(1, 58, 58, 58, 58, 58), // BST 290
            Species(2, 60, 60, 60, 60, 60), // BST 300
            Species(3, 62, 62, 62, 62, 62), // BST 310
            Species(4, 64, 64, 64, 64, 64), // BST 320
        };

        var a = EncounterSelector.PickByBst(pool, 300, new SeededRandomSource(7));
        var b = EncounterSelector.PickByBst(pool, 300, new SeededRandomSource(7));

        Assert.NotNull(a);
        Assert.Same(a, b); // same seed + same pool ⇒ same pick
    }

    private static PokemonSpecies Species(int id, int hp, int atk, int def, int spc, int spd) =>
        new()
        {
            Id = id,
            BaseHP = hp,
            BaseAttack = atk,
            BaseDefense = def,
            BaseSpecial = spc,
            BaseSpeed = spd,
        };
}
