using creaturegame.Attacks;
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

    private static PokemonSpecies Species(int id, DamageType t1, DamageType? t2) =>
        new()
        {
            Id = id,
            Type1 = t1,
            Type2 = t2,
        };
}
