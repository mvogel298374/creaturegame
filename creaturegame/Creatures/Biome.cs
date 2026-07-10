using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.DB;

namespace creaturegame.Creatures;

/// <summary>
/// The overarching origin a biome belongs to. Mostly flavour today (the roster is Gen 1 / Kanto), but it is
/// also the axis the multi-generation roadmap grows along — a new region declares its own biome list over that
/// generation's dex, reusing the biome *archetypes* (forest, cave, shore) rather than the run loop. See
/// <c>ENCOUNTER_DESIGN.md §2</c>.
/// </summary>
public enum Region
{
    Kanto,
}

/// <summary>
/// An authored encounter region: a type theme plus map adjacency. The theme drives the encounter pool (a
/// species belongs if <em>either</em> of its types is on-theme — <see cref="Contains"/>), which in turn is the
/// fought-only acquisition pool. Biomes are design content, not imported data (there is no per-area location
/// table in <c>pokemon.db</c>), so they live in the static <see cref="Biomes"/> registry. <see cref="Neighbours"/>
/// is authored now but only <em>traversed</em> in Phase 3 (the biome graph / <c>chooseNextEvent</c>).
/// </summary>
public sealed record BiomeDefinition(
    string Id,
    string Name,
    Region Region,
    IReadOnlyList<DamageType> Types,
    IReadOnlyList<string> Neighbours,
    // Authored 2-D map coordinates (0–100 each, y increasing downward) for the client's region-map overlay —
    // presentation metadata on this authored biome content (the Encounter Map feature; ENCOUNTER_DESIGN.md §2.1).
    // Positioned to roughly reflect the neighbour graph so drawn edges read as a coherent overworld. Default 0 so
    // non-registry constructions (tests) need not supply them; the Kanto roster below sets real positions.
    int MapX = 0,
    int MapY = 0
)
{
    /// <summary>Either-type match: a species is on-theme if its primary or secondary type is in this biome.</summary>
    public bool Contains(PokemonSpecies s) =>
        Types.Contains(s.Type1) || (s.Type2 is { } t2 && Types.Contains(t2));

    /// <summary>True if any species in <paramref name="pool"/> is on-theme — used to drop empty biomes.</summary>
    public bool HasAnyIn(IEnumerable<PokemonSpecies> pool) => pool.Any(Contains);
}

/// <summary>
/// The authored biome registry, grouped by <see cref="Region"/>. The Kanto roster (18 biomes) homes all 15
/// Gen 1 types in 2–3 biomes each, with broad types (Poison, Water) confined to a few biomes to avoid flooding
/// and thin types (Ice, Dragon, Ghost) paired with a carrier so no biome is razor-thin. Pool sizes are verified
/// against <c>pokemon.db</c> in <c>ENCOUNTER_DESIGN.md §2.3</c>.
/// </summary>
public static class Biomes
{
    // Neighbours form one connected, bidirectional graph (guarded by BiomeTests). Phase 1 only reads Types;
    // the graph is traversed in Phase 3.
    public static readonly IReadOnlyList<BiomeDefinition> Kanto =
    [
        new(
            "meadow-trail",
            "Meadow Trail",
            Region.Kanto,
            [DamageType.Normal, DamageType.Flying],
            ["whispering-woods", "bramble-thicket", "storm-plateau"],
            12,
            34
        ),
        new(
            "whispering-woods",
            "Whispering Woods",
            Region.Kanto,
            [DamageType.Bug, DamageType.Grass],
            ["meadow-trail", "bramble-thicket", "verdant-glade"],
            16,
            54
        ),
        new(
            "bramble-thicket",
            "Bramble Thicket",
            Region.Kanto,
            [DamageType.Grass, DamageType.Poison],
            ["meadow-trail", "whispering-woods", "mire-swamp", "verdant-glade"],
            28,
            64
        ),
        new(
            "mire-swamp",
            "Mire Swamp",
            Region.Kanto,
            [DamageType.Poison, DamageType.Ground],
            ["bramble-thicket", "phantom-marsh", "tranquil-lake"],
            40,
            80
        ),
        new(
            "crystal-cavern",
            "Crystal Cavern",
            Region.Kanto,
            [DamageType.Rock, DamageType.Ground],
            ["sunbaked-canyon", "granite-cliffs", "magma-ridge"],
            58,
            32
        ),
        new(
            "sunbaked-canyon",
            "Sunbaked Canyon",
            Region.Kanto,
            [DamageType.Ground, DamageType.Fighting],
            ["crystal-cavern", "granite-cliffs", "magma-ridge"],
            62,
            48
        ),
        new(
            "granite-cliffs",
            "Granite Cliffs",
            Region.Kanto,
            [DamageType.Rock, DamageType.Flying, DamageType.Fighting],
            ["crystal-cavern", "sunbaked-canyon", "storm-plateau"],
            44,
            40
        ),
        new(
            "storm-plateau",
            "Storm Plateau",
            Region.Kanto,
            [DamageType.Electric, DamageType.Flying],
            ["meadow-trail", "granite-cliffs", "sparkwire-ruins"],
            32,
            20
        ),
        new(
            "sparkwire-ruins",
            "Sparkwire Ruins",
            Region.Kanto,
            [DamageType.Electric, DamageType.Psychic],
            ["storm-plateau", "haunted-spire"],
            50,
            24
        ),
        new(
            "magma-ridge",
            "Magma Ridge",
            Region.Kanto,
            [DamageType.Fire, DamageType.Rock],
            ["crystal-cavern", "sunbaked-canyon", "cinder-hollow"],
            74,
            38
        ),
        new(
            "cinder-hollow",
            "Cinder Hollow",
            Region.Kanto,
            [DamageType.Fire, DamageType.Ghost],
            ["magma-ridge", "haunted-spire"],
            82,
            52
        ),
        new(
            "haunted-spire",
            "Haunted Spire",
            Region.Kanto,
            [DamageType.Ghost, DamageType.Psychic],
            ["sparkwire-ruins", "cinder-hollow", "phantom-marsh"],
            68,
            62
        ),
        new(
            "phantom-marsh",
            "Phantom Marsh",
            Region.Kanto,
            [DamageType.Ghost, DamageType.Poison],
            ["mire-swamp", "haunted-spire", "tranquil-lake"],
            54,
            84
        ),
        new(
            "tranquil-lake",
            "Tranquil Lake",
            Region.Kanto,
            [DamageType.Water, DamageType.Psychic],
            ["mire-swamp", "phantom-marsh", "frostbound-shore"],
            56,
            66
        ),
        new(
            "frostbound-shore",
            "Frostbound Shore",
            Region.Kanto,
            [DamageType.Water, DamageType.Ice],
            ["tranquil-lake", "glacier-hollow", "abyssal-reef"],
            74,
            80
        ),
        new(
            "glacier-hollow",
            "Glacier Hollow",
            Region.Kanto,
            [DamageType.Ice, DamageType.Dragon],
            ["frostbound-shore", "abyssal-reef"],
            90,
            88
        ),
        new(
            "abyssal-reef",
            "Abyssal Reef",
            Region.Kanto,
            [DamageType.Water, DamageType.Dragon],
            ["frostbound-shore", "glacier-hollow"],
            92,
            68
        ),
        new(
            "verdant-glade",
            "Verdant Glade",
            Region.Kanto,
            [DamageType.Grass, DamageType.Normal, DamageType.Bug],
            ["whispering-woods", "bramble-thicket"],
            10,
            74
        ),
    ];

    /// <summary>The authored biome list for a region (empty for regions not yet rostered).</summary>
    public static IReadOnlyList<BiomeDefinition> For(Region region) =>
        region switch
        {
            Region.Kanto => Kanto,
            _ => [],
        };

    /// <summary>
    /// The region's biomes that can actually generate against <paramref name="pool"/> — those with at least one
    /// on-theme species. Empty biomes never generate (no off-theme fallback), so map generation draws only from
    /// this set.
    /// </summary>
    public static IReadOnlyList<BiomeDefinition> Playable(
        Region region,
        IReadOnlyList<PokemonSpecies> pool
    ) => For(region).Where(b => b.HasAnyIn(pool)).ToList();

    /// <summary>
    /// A single run's biome map: a seeded, <strong>connected</strong> random subset of <paramref name="playable"/>
    /// of up to <paramref name="count"/> biomes (<c>ENCOUNTER_DESIGN.md §2.1</c> — <em>which</em> biomes appear is
    /// randomised per run, so runs traverse different slices of the region; same seed ⇒ same map). Grown by
    /// randomized frontier expansion over the authored neighbour graph restricted to <paramref name="playable"/>:
    /// each added biome is adjacent (within the subset) to one already chosen, so the induced subgraph is always
    /// connected — the route can never strand the player. Returns all of <paramref name="playable"/> when
    /// <paramref name="count"/> ≥ its size, and <c>[]</c> when it is empty. All randomness is drawn from
    /// <paramref name="rng"/> so the map replays from the run seed.
    /// </summary>
    public static IReadOnlyList<BiomeDefinition> RandomConnectedMap(
        IReadOnlyList<BiomeDefinition> playable,
        int count,
        IRandomSource rng
    )
    {
        if (count <= 0 || playable.Count == 0)
            return [];
        if (count >= playable.Count)
            return playable;

        var byId = playable.ToDictionary(b => b.Id);
        var chosen = new List<BiomeDefinition>();
        var inMap = new HashSet<string>();
        var frontier = new List<string>(); // playable ids adjacent to the chosen set, not yet chosen
        var frontierSet = new HashSet<string>(); // de-dupes the frontier (a biome adjacent to several chosen)

        void Add(BiomeDefinition b)
        {
            chosen.Add(b);
            inMap.Add(b.Id);
            foreach (var id in b.Neighbours)
                if (byId.ContainsKey(id) && !inMap.Contains(id) && frontierSet.Add(id))
                    frontier.Add(id);
        }

        Add(playable[rng.Next(playable.Count)]); // a random seed biome
        while (chosen.Count < count && frontier.Count > 0)
        {
            int i = rng.Next(frontier.Count);
            var id = frontier[i];
            frontier[i] = frontier[^1]; // O(1) swap-remove (order is irrelevant — the pick is random)
            frontier.RemoveAt(frontier.Count - 1);
            frontierSet.Remove(id);
            Add(byId[id]);
        }
        return chosen;
    }
}
