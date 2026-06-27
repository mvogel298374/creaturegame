using creaturegame.Attacks;
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
    IReadOnlyList<string> Neighbours
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
            ["whispering-woods", "bramble-thicket", "storm-plateau"]
        ),
        new(
            "whispering-woods",
            "Whispering Woods",
            Region.Kanto,
            [DamageType.Bug, DamageType.Grass],
            ["meadow-trail", "bramble-thicket", "verdant-glade"]
        ),
        new(
            "bramble-thicket",
            "Bramble Thicket",
            Region.Kanto,
            [DamageType.Grass, DamageType.Poison],
            ["meadow-trail", "whispering-woods", "mire-swamp", "verdant-glade"]
        ),
        new(
            "mire-swamp",
            "Mire Swamp",
            Region.Kanto,
            [DamageType.Poison, DamageType.Ground],
            ["bramble-thicket", "phantom-marsh", "tranquil-lake"]
        ),
        new(
            "crystal-cavern",
            "Crystal Cavern",
            Region.Kanto,
            [DamageType.Rock, DamageType.Ground],
            ["sunbaked-canyon", "granite-cliffs", "magma-ridge"]
        ),
        new(
            "sunbaked-canyon",
            "Sunbaked Canyon",
            Region.Kanto,
            [DamageType.Ground, DamageType.Fighting],
            ["crystal-cavern", "granite-cliffs", "magma-ridge"]
        ),
        new(
            "granite-cliffs",
            "Granite Cliffs",
            Region.Kanto,
            [DamageType.Rock, DamageType.Flying, DamageType.Fighting],
            ["crystal-cavern", "sunbaked-canyon", "storm-plateau"]
        ),
        new(
            "storm-plateau",
            "Storm Plateau",
            Region.Kanto,
            [DamageType.Electric, DamageType.Flying],
            ["meadow-trail", "granite-cliffs", "sparkwire-ruins"]
        ),
        new(
            "sparkwire-ruins",
            "Sparkwire Ruins",
            Region.Kanto,
            [DamageType.Electric, DamageType.Psychic],
            ["storm-plateau", "haunted-spire"]
        ),
        new(
            "magma-ridge",
            "Magma Ridge",
            Region.Kanto,
            [DamageType.Fire, DamageType.Rock],
            ["crystal-cavern", "sunbaked-canyon", "cinder-hollow"]
        ),
        new(
            "cinder-hollow",
            "Cinder Hollow",
            Region.Kanto,
            [DamageType.Fire, DamageType.Ghost],
            ["magma-ridge", "haunted-spire"]
        ),
        new(
            "haunted-spire",
            "Haunted Spire",
            Region.Kanto,
            [DamageType.Ghost, DamageType.Psychic],
            ["sparkwire-ruins", "cinder-hollow", "phantom-marsh"]
        ),
        new(
            "phantom-marsh",
            "Phantom Marsh",
            Region.Kanto,
            [DamageType.Ghost, DamageType.Poison],
            ["mire-swamp", "haunted-spire", "tranquil-lake"]
        ),
        new(
            "tranquil-lake",
            "Tranquil Lake",
            Region.Kanto,
            [DamageType.Water, DamageType.Psychic],
            ["mire-swamp", "phantom-marsh", "frostbound-shore"]
        ),
        new(
            "frostbound-shore",
            "Frostbound Shore",
            Region.Kanto,
            [DamageType.Water, DamageType.Ice],
            ["tranquil-lake", "glacier-hollow", "abyssal-reef"]
        ),
        new(
            "glacier-hollow",
            "Glacier Hollow",
            Region.Kanto,
            [DamageType.Ice, DamageType.Dragon],
            ["frostbound-shore", "abyssal-reef"]
        ),
        new(
            "abyssal-reef",
            "Abyssal Reef",
            Region.Kanto,
            [DamageType.Water, DamageType.Dragon],
            ["frostbound-shore", "glacier-hollow"]
        ),
        new(
            "verdant-glade",
            "Verdant Glade",
            Region.Kanto,
            [DamageType.Grass, DamageType.Normal, DamageType.Bug],
            ["whispering-woods", "bramble-thicket"]
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
}
