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

    [Fact]
    public void EncounterSelector_PickByBst_WithBiome_OnlyReturnsThemedSpecies()
    {
        // Mixed pool at the same BST; the Fire biome must never surface a Water species.
        var fire = new BiomeDefinition("fire", "Fire", Region.Kanto, [DamageType.Fire], []);
        var pool = new List<PokemonSpecies>
        {
            Typed(1, DamageType.Fire, null, 60),
            Typed(2, DamageType.Fire, null, 60),
            Typed(3, DamageType.Water, null, 60),
            Typed(4, DamageType.Water, null, 60),
        };

        for (int i = 0; i < 100; i++)
        {
            var result = EncounterSelector.PickByBst(pool, 300, biome: fire);
            Assert.NotNull(result);
            Assert.Equal(DamageType.Fire, result!.Type1);
        }
    }

    [Fact]
    public void EncounterSelector_PickByBst_WithBiome_ReturnsNull_WhenNoThemedSpecies()
    {
        var fire = new BiomeDefinition("fire", "Fire", Region.Kanto, [DamageType.Fire], []);
        var pool = new List<PokemonSpecies> { Typed(1, DamageType.Water, null, 60) };

        Assert.Null(EncounterSelector.PickByBst(pool, 300, biome: fire));
    }

    [Fact]
    public void EncounterSelector_PickByBst_WithBiome_FarBst_PicksNearestThemed_NeverOffTheme()
    {
        // playerBst 100: both Fire species sit beyond ±100% (BST 500 & 800); a Water species is in-band (BST
        // 100). The theme is inviolable — it must return the nearest-BST Fire (500), not the in-band Water.
        var fire = new BiomeDefinition("fire", "Fire", Region.Kanto, [DamageType.Fire], []);
        var pool = new List<PokemonSpecies>
        {
            Typed(1, DamageType.Fire, null, 100), // BST 500
            Typed(2, DamageType.Fire, null, 160), // BST 800
            Typed(3, DamageType.Water, null, 20), // BST 100 — in band, but off-theme
        };

        var result = EncounterSelector.PickByBst(pool, 100, biome: fire);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public void EncounterSelector_PickByBst_WithBiome_SameSeed_PicksSameSpecies()
    {
        var fire = new BiomeDefinition("fire", "Fire", Region.Kanto, [DamageType.Fire], []);
        var pool = new List<PokemonSpecies>
        {
            Typed(1, DamageType.Fire, null, 58),
            Typed(2, DamageType.Fire, null, 60),
            Typed(3, DamageType.Fire, null, 62),
        };

        var a = EncounterSelector.PickByBst(pool, 300, new SeededRandomSource(7), fire);
        var b = EncounterSelector.PickByBst(pool, 300, new SeededRandomSource(7), fire);

        Assert.NotNull(a);
        Assert.Same(a, b);
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

    // A typed species whose five base stats are all <paramref name="statEach"/> ⇒ BST = 5 × statEach.
    private static PokemonSpecies Typed(int id, DamageType t1, DamageType? t2, int statEach) =>
        new()
        {
            Id = id,
            Type1 = t1,
            Type2 = t2,
            BaseHP = statEach,
            BaseAttack = statEach,
            BaseDefense = statEach,
            BaseSpecial = statEach,
            BaseSpeed = statEach,
        };
}
