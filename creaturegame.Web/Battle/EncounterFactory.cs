using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Items;
using Microsoft.EntityFrameworkCore;
using static creaturegame.Creatures.EncounterSelector;

namespace creaturegame.Web.Battle;

/// <summary>
/// Builds creatures from the databases for a run: the player once at the start, and a fresh wild enemy
/// scaled to the player before each encounter. Centralises the species/learnset/move queries and the
/// move-selection strategy so both the initial setup (<see cref="GameController"/>) and the chain loop
/// (<see cref="BattleRunner"/> via <see cref="GameSessionManager"/>) build creatures the same way.
/// </summary>
public sealed class EncounterFactory(
    IDbContextFactory<PokemonDbContext> pokemonFactory,
    IDbContextFactory<MovesDbContext> movesFactory,
    IDbContextFactory<ItemsDbContext> itemsFactory
)
{
    // The single generation switch — learnset rows are tagged by generation and filtered by this.
    private const int ActiveGeneration = 1;

    // Generous test-bag loadout: every imported item, this many each. The bag is transient (no save
    // layer yet) and per-run; the acquisition layer will replace this fixed seed later. (Items with no
    // in-battle effect yet — Balls, Revives — ride along and simply report "no effect" if used.)
    private const int TestBagQuantityEach = 20;

    /// <summary>
    /// Loads the move pool and builds the player creature with its canonical moveset. Returns null if the
    /// species id is unknown or the move database is empty.
    /// </summary>
    public async Task<RunSetup?> CreatePlayerSetupAsync(
        int speciesId,
        int level,
        IRandomSource? rng = null
    )
    {
        var source = rng ?? SystemRandomSource.Instance;
        await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();
        var species = await pokemonCtx
            .Species.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == speciesId);
        if (species == null)
            return null;

        await using var movesCtx = await movesFactory.CreateDbContextAsync();
        var allMoves = await movesCtx.Moves.AsNoTracking().ToListAsync();
        if (allMoves.Count == 0)
            return null;

        var learnsets = await pokemonCtx
            .Learnsets.AsNoTracking()
            .Where(l =>
                l.Generation == ActiveGeneration
                && l.SpeciesId == species.Id
                && l.Method == LearnMethod.LevelUp
            )
            .ToListAsync();

        var player = BuildCreature(
            species,
            learnsets,
            allMoves,
            level,
            MoveSelectionStrategy.CanonicalLatest,
            source
        );
        // Only the player levels up, so only the player carries a learnset (its moves resolved up-front and
        // consulted by the battle loop on each level gained). Persists with the creature across the chain.
        player.Learnset = BuildLearnset(learnsets, allMoves);

        // Seed the run's bag from the item catalog. Held by the session and threaded into every Battle;
        // consumed items stay gone across the chain (the Poké Center refills HP/PP/status, not the bag).
        await using var itemsCtx = await itemsFactory.CreateDbContextAsync();
        var allItems = await itemsCtx.Items.AsNoTracking().ToListAsync();
        var bag = Bag.WithEach(allItems.Select(i => i.Id), TestBagQuantityEach);

        return new RunSetup(player, allMoves, bag, allItems);
    }

    /// <summary>
    /// Builds a fresh wild enemy scaled to the player's current level and BST, excluding the player's own
    /// species, reusing the run's already-loaded move pool. The enemy gets a semi-random "smart" moveset so
    /// encounters vary. Enemy level sits in a roguelite band below the player (see <see cref="ScaleWildLevel"/>)
    /// so the chain stays winnable while still scaling up as the player levels.
    /// <para>The pool is restricted to <em>wild-available</em> species (excludes legendaries/statics/gifts).
    /// When <paramref name="biome"/> is supplied the pool is further filtered to that biome's type theme
    /// (<see cref="EncounterSelector.PickByBst"/>); it is null until Phase 3's biome graph selects one per
    /// encounter (see <c>ENCOUNTER_DESIGN.md §2</c>).</para>
    /// </summary>
    public async Task<Creature> CreateEnemyAsync(
        Creature player,
        IReadOnlyList<Attack> allMoves,
        IRandomSource? rng = null,
        BiomeDefinition? biome = null
    )
    {
        var source = rng ?? SystemRandomSource.Instance;
        await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();
        int playerBst =
            player.BaseHP
            + player.BaseAttack
            + player.BaseDefense
            + player.BaseSpecial
            + player.BaseSpeed;

        // Encounters draw only from wild-available species (excludes legendaries/statics/gifts/fossils — the
        // canonical lucky-spike hazard). Fall back to the full dex if availability data is absent (a
        // minimally-seeded DB), so the selector never starves.
        var wildIds = await pokemonCtx
            .GameAvailability.AsNoTracking()
            .Where(a => a.AvailabilityType == "Wild")
            .Select(a => a.SpeciesId)
            .Distinct()
            .ToListAsync();
        var wildSet = wildIds.ToHashSet();

        var pool = await pokemonCtx
            .Species.AsNoTracking()
            .Where(s => s.Id != player.SpeciesId)
            .ToListAsync();
        if (wildSet.Count > 0)
            pool = pool.Where(s => wildSet.Contains(s.Id)).ToList();

        var enemySpecies =
            PickByBst(pool, playerBst, source, biome)
            ?? throw new InvalidOperationException("No species available to build an encounter.");

        int enemyLevel = ScaleWildLevel(player.Level, source);

        var learnsets = await pokemonCtx
            .Learnsets.AsNoTracking()
            // Base-tier enemy moveset: level-up moves only. The TM/HM (Machine) rows are read by the
            // TmEnhanced/Optimal moveset tiers (Phase 2d), not the base selection here.
            .Where(l =>
                l.Generation == ActiveGeneration
                && l.SpeciesId == enemySpecies.Id
                && l.Method == LearnMethod.LevelUp
            )
            .ToListAsync();

        return BuildCreature(
            enemySpecies,
            learnsets,
            allMoves,
            enemyLevel,
            MoveSelectionStrategy.WeightedSmart,
            source
        );
    }

    /// <summary>
    /// The evolution data/DB seam for the run loop (<see cref="BattleRunner"/>). Loads the player species'
    /// evolution edges, runs the Gen 1 <see cref="IEvolutionRules"/> decision against the player's current
    /// level, and — if one fires — resolves the evolved species plus its learnset into an
    /// <see cref="EvolutionOutcome"/>. Returns null when nothing evolves, so the runner leaves the player as
    /// is. The generation choice is made here at the composition layer (like <c>Gen1TypeChart.Instance</c> in
    /// <see cref="GameSessionManager"/>), keeping the runner generation-agnostic.
    /// </summary>
    public async Task<EvolutionOutcome?> ResolvePlayerEvolutionAsync(
        Creature player,
        IReadOnlyList<Attack> allMoves
    )
    {
        await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();
        var edges = await pokemonCtx
            .Evolutions.AsNoTracking()
            .Where(e => e.Generation == ActiveGeneration && e.FromSpeciesId == player.SpeciesId)
            .ToListAsync();
        if (edges.Count == 0)
            return null;

        var result = Gen1EvolutionRules.Instance.CheckEvolution(
            player,
            new EvolutionContext.LeveledTo(player.Level),
            edges
        );
        if (result is null)
            return null;

        var newForm = await pokemonCtx
            .Species.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == result.ToSpeciesId);
        if (newForm is null)
            return null;

        var learnsets = await pokemonCtx
            .Learnsets.AsNoTracking()
            .Where(l =>
                l.Generation == ActiveGeneration
                && l.SpeciesId == newForm.Id
                && l.Method == LearnMethod.LevelUp
            )
            .ToListAsync();

        return new EvolutionOutcome(newForm, BuildLearnset(learnsets, allMoves));
    }

    /// <summary>
    /// Picks a wild encounter's level as a roguelite difficulty band: uniformly in [50%, 80%] of the player's
    /// current level (floored), never below 2. This deliberately keeps wild foes a step under the player so the
    /// endless chain stays winnable while still scaling up as the player levels. It is a run-layer tuning
    /// choice, not a Gen 1 mechanic (Gen 1 wild levels come from per-area encounter tables), so it lives here
    /// in the web/run layer rather than behind a battle seam. <c>internal</c> for direct unit testing.
    /// </summary>
    internal static int ScaleWildLevel(int playerLevel, IRandomSource rng)
    {
        int min = Math.Max(2, (int)(playerLevel * 0.5));
        int max = Math.Max(min, (int)(playerLevel * 0.8));
        return rng.Next(min, max + 1); // Next's upper bound is exclusive → +1 makes max inclusive
    }

    private static Creature BuildCreature(
        PokemonSpecies species,
        IReadOnlyList<PokemonLearnset> learnsets,
        IReadOnlyList<Attack> allMoves,
        int level,
        MoveSelectionStrategy strategy,
        IRandomSource rng
    )
    {
        // Construction rolls DVs (the Creature ctor used the global-RNG default calculator); re-seat the
        // stat calculator on the run's seeded source and re-roll so a run with a fixed seed reproduces the
        // same DVs. DV randomisation is a per-generation rule, so it stays behind IStatCalculator.
        var creature = new Creature(species.Name.ToUpper()) { Level = level };
        creature.StatCalculator = new Gen1StatCalculator(rng);
        // Ordinary DVs today; Phase 2d's enemy strength tier passes the spec's DvQuality here.
        creature.StatCalculator.RandomiseDvs(creature, DvQuality.Average);
        creature.InitializeFromSpecies(species);
        creature.Experience = creature.CalculateExperienceForLevel(level);

        var speciesLearnset = learnsets.Where(l => l.SpeciesId == species.Id).ToList();
        var moves = LearnsetMoveSelector.SelectWithFallback(
            strategy,
            speciesLearnset,
            allMoves,
            level,
            species.Type1,
            species.Type2,
            rng
        );

        foreach (var move in moves)
            creature.AddAttack(move);
        return creature;
    }

    /// <summary>
    /// Resolves a species' learnset rows (already filtered to the species + active generation) into the
    /// engine's <see cref="LearnsetMove"/> list, ordered by learn level. Rows whose move id isn't in the pool
    /// are skipped, mirroring <see cref="LearnsetMoveSelector"/>.
    /// </summary>
    private static IReadOnlyList<LearnsetMove> BuildLearnset(
        IReadOnlyList<PokemonLearnset> learnsets,
        IReadOnlyList<Attack> allMoves
    )
    {
        var movesById = allMoves.ToDictionary(m => m.Id);
        return learnsets
            .Where(l => movesById.ContainsKey(l.MoveId))
            .OrderBy(l => l.LearnLevel)
            .ThenBy(l => l.MoveId)
            .Select(l => new LearnsetMove(l.LearnLevel, movesById[l.MoveId]))
            .ToList();
    }
}

/// <summary>The starting state of a run: the built player, the shared move pool the chain reuses, the run's
/// starting <see cref="Bag"/>, and the item catalog (id → <see cref="Item"/>) used to resolve item uses and
/// render the bag.</summary>
public sealed record RunSetup(
    Creature Player,
    IReadOnlyList<Attack> AllMoves,
    Bag Bag,
    IReadOnlyList<Item> AllItems
);
