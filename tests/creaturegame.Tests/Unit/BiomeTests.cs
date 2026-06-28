using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;

namespace creaturegame.Tests.Unit;

public class BiomeTests
{
    // The 15 types in play in Gen 1 (Steel/Dark/Fairy arrived later).
    private static readonly DamageType[] Gen1Types =
    [
        DamageType.Normal,
        DamageType.Fighting,
        DamageType.Flying,
        DamageType.Poison,
        DamageType.Ground,
        DamageType.Rock,
        DamageType.Bug,
        DamageType.Ghost,
        DamageType.Fire,
        DamageType.Water,
        DamageType.Grass,
        DamageType.Electric,
        DamageType.Psychic,
        DamageType.Ice,
        DamageType.Dragon,
    ];

    [Fact]
    public void Kanto_Has18Biomes()
    {
        Assert.Equal(18, Biomes.Kanto.Count);
    }

    [Fact]
    public void Kanto_HomesEveryGen1Type()
    {
        var homed = Biomes.Kanto.SelectMany(b => b.Types).ToHashSet();
        foreach (var t in Gen1Types)
            Assert.Contains(t, homed);
    }

    [Fact]
    public void Kanto_UsesNoPostGen1Types()
    {
        // Complement of HomesEveryGen1Type: a future biome edit can't smuggle in Steel/Dark/Fairy unnoticed
        // (the spread tests only iterate Gen1Types, so they wouldn't catch it).
        Assert.All(Biomes.Kanto.SelectMany(b => b.Types), t => Assert.Contains(t, Gen1Types));
    }

    [Fact]
    public void Kanto_SpreadsEachTypeAcrossTwoOrThreeBiomes()
    {
        foreach (var t in Gen1Types)
        {
            int count = Biomes.Kanto.Count(b => b.Types.Contains(t));
            Assert.True(count is >= 2 and <= 3, $"{t} appears in {count} biomes (want 2–3)");
        }
    }

    [Fact]
    public void Kanto_NeighbourLinksAreSymmetric()
    {
        var byId = Biomes.Kanto.ToDictionary(b => b.Id);
        foreach (var b in Biomes.Kanto)
        foreach (var n in b.Neighbours)
        {
            Assert.True(byId.ContainsKey(n), $"{b.Id} links to unknown biome '{n}'");
            Assert.Contains(b.Id, byId[n].Neighbours); // the link must be reciprocated
        }
    }

    [Fact]
    public void Kanto_GraphIsFullyConnected()
    {
        var byId = Biomes.Kanto.ToDictionary(b => b.Id);
        var seen = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(Biomes.Kanto[0].Id);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id))
                continue;
            foreach (var n in byId[id].Neighbours)
                stack.Push(n);
        }
        Assert.Equal(Biomes.Kanto.Count, seen.Count); // every biome reachable from the first
    }

    [Fact]
    public void Contains_MatchesOnEitherType()
    {
        var forest = new BiomeDefinition(
            "f",
            "F",
            Region.Kanto,
            [DamageType.Bug, DamageType.Grass],
            []
        );
        // Secondary-type match: Bug/Flying belongs via its primary; Water/Flying does not belong at all.
        Assert.True(forest.Contains(Species(1, DamageType.Bug, DamageType.Flying)));
        Assert.True(forest.Contains(Species(2, DamageType.Grass, null)));
        Assert.False(forest.Contains(Species(3, DamageType.Water, DamageType.Flying)));
    }

    [Fact]
    public void Playable_DropsBiomesWithNoMembersInPool()
    {
        // A pool of one Bug species: only Bug-listing biomes are playable; the rest are excluded.
        var pool = new List<PokemonSpecies> { Species(1, DamageType.Bug, null) };
        var playable = Biomes.Playable(Region.Kanto, pool);

        Assert.NotEmpty(playable);
        Assert.All(playable, b => Assert.Contains(DamageType.Bug, b.Types));
        Assert.Equal(Biomes.Kanto.Count(b => b.Types.Contains(DamageType.Bug)), playable.Count);
    }

    // --- Per-run biome-map randomisation (ENCOUNTER_DESIGN.md §2.1) ---

    [Fact]
    public void RandomConnectedMap_DrawsAConnectedSubsetOfTheRequestedSize()
    {
        var map = Biomes.RandomConnectedMap(Biomes.Kanto, 10, new SeededRandomSource(7));

        Assert.Equal(10, map.Count);
        Assert.Equal(10, map.Select(b => b.Id).Distinct().Count()); // no duplicates
        Assert.All(map, b => Assert.Contains(b, Biomes.Kanto)); // all real Kanto biomes
        AssertConnectedWithinSubset(map); // the route can never strand the player
    }

    [Fact]
    public void RandomConnectedMap_IsReproducibleFromSeed()
    {
        var a = Biomes.RandomConnectedMap(Biomes.Kanto, 10, new SeededRandomSource(123));
        var b = Biomes.RandomConnectedMap(Biomes.Kanto, 10, new SeededRandomSource(123));

        Assert.Equal(a.Select(x => x.Id), b.Select(x => x.Id)); // same seed → same map (biomes + order)
    }

    [Fact]
    public void RandomConnectedMap_DifferentSeeds_GiveDifferentMaps()
    {
        var a = Biomes
            .RandomConnectedMap(Biomes.Kanto, 10, new SeededRandomSource(1))
            .Select(b => b.Id)
            .ToHashSet();
        var b = Biomes
            .RandomConnectedMap(Biomes.Kanto, 10, new SeededRandomSource(2))
            .Select(x => x.Id)
            .ToHashSet();

        Assert.NotEqual(a, b); // which biomes appear actually varies run to run
    }

    [Fact]
    public void RandomConnectedMap_ReturnsWholeSet_WhenCountAtLeastAvailable()
    {
        var map = Biomes.RandomConnectedMap(Biomes.Kanto, 999, new SeededRandomSource(0));
        Assert.Equal(Biomes.Kanto.Count, map.Count);
    }

    [Fact]
    public void RandomConnectedMap_Empty_ForZeroCountOrEmptyPool()
    {
        Assert.Empty(Biomes.RandomConnectedMap(Biomes.Kanto, 0, new SeededRandomSource(0)));
        Assert.Empty(Biomes.RandomConnectedMap([], 5, new SeededRandomSource(0)));
    }

    // Every biome in the map is reachable from the first using only edges that stay inside the map — i.e. the
    // induced subgraph is connected, so route-choice never reaches an island.
    private static void AssertConnectedWithinSubset(IReadOnlyList<BiomeDefinition> map)
    {
        var ids = map.Select(b => b.Id).ToHashSet();
        var byId = map.ToDictionary(b => b.Id);
        var seen = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(map[0].Id);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id))
                continue;
            foreach (var n in byId[id].Neighbours.Where(ids.Contains))
                stack.Push(n);
        }
        Assert.Equal(map.Count, seen.Count);
    }

    private static PokemonSpecies Species(int id, DamageType t1, DamageType? t2) =>
        new()
        {
            Id = id,
            Type1 = t1,
            Type2 = t2,
        };
}
