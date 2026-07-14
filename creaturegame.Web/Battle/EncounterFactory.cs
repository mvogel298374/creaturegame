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
/// (<see cref="RunDirector"/> via <see cref="GameSessionManager"/>) build creatures the same way.
/// </summary>
public sealed class EncounterFactory(
    IDbContextFactory<PokemonDbContext> pokemonFactory,
    IDbContextFactory<MovesDbContext> movesFactory,
    IDbContextFactory<ItemsDbContext> itemsFactory
)
{
    // The single generation switch — learnset rows are tagged by generation and filtered by this.
    private const int ActiveGeneration = 1;

    // How many biomes a single run's map draws from the region's playable set (ENCOUNTER_DESIGN.md §2.1):
    // a seeded connected subset, so runs traverse different slices of Kanto. Tuning lever — smaller = a more
    // distinct per-run "region", larger = richer route choice. The full set has 18; if it ever has fewer than
    // this, the whole set is used.
    public const int RunBiomeMapSize = 10;

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

        // Seed the run's starting bag from the item catalog — a curated modest loadout (not the whole
        // catalog), gated so a lucky early haul can't trivialise a run; the run economy (battle drops,
        // Treasure/Mystery) grows it from here. Held by the session and threaded into every Battle; consumed
        // items stay gone across the chain (the Poké Center refills HP/PP/status, not the bag).
        await using var itemsCtx = await itemsFactory.CreateDbContextAsync();
        var allItems = await itemsCtx.Items.AsNoTracking().ToListAsync();
        var bag = BuildStartingBag(allItems);

        // The run's biome map: a seeded, connected random subset of the region's playable biomes (the ones that
        // can generate against the wild-available pool — empty biomes never appear, ENCOUNTER_DESIGN.md §2.2).
        // Randomising *which* biomes appear makes each run traverse a different slice of Kanto (§2.1); the subset
        // is connected so the route never strands, and the same Wild filter CreateEnemyAsync applies, so every
        // offered biome is guaranteed an encounter (PickByBst can't starve on its themed pool). Same seed ⇒ same
        // map. Threaded into the RunDirector, which charts the route through it.
        var playable = await ComputePlayableBiomesAsync(pokemonCtx, Region.Kanto);
        var runMap = Biomes.RandomConnectedMap(playable, RunBiomeMapSize, source);

        return new RunSetup(player, allMoves, bag, new Wallet(), allItems, runMap);
    }

    /// <summary>
    /// A curated, modest starting loadout by category/cost (no hardcoded item ids, so it survives a catalog
    /// re-import): the cheapest Healing item ×4, the two cheapest StatusCure items ×1, the cheapest PpRestore
    /// item ×1. Replaces the old fixed "every item ×20" test seed now that the run economy (battle drops,
    /// Treasure/Mystery) grows the bag from here — a lucky early haul still can't trivialise a run because
    /// this start is deliberately light.
    /// </summary>
    internal static Bag BuildStartingBag(IReadOnlyList<Item> allItems)
    {
        var bag = new Bag();
        AddCheapest(bag, allItems, ItemCategory.Healing, count: 1, quantity: 4);
        AddCheapest(bag, allItems, ItemCategory.StatusCure, count: 2, quantity: 1);
        AddCheapest(bag, allItems, ItemCategory.PpRestore, count: 1, quantity: 1);
        return bag;
    }

    private static void AddCheapest(
        Bag bag,
        IReadOnlyList<Item> allItems,
        ItemCategory category,
        int count,
        int quantity
    )
    {
        foreach (
            var item in allItems.Where(i => i.Category == category).OrderBy(i => i.Cost).Take(count)
        )
            bag.Add(item.Id, quantity);
    }

    /// <summary>
    /// Builds the run's reward supplier: the injected <c>Func&lt;RewardContext, IRandomSource, RewardChoice&gt;</c>
    /// <see cref="RunDirector"/> rolls after a battle win and on Treasure/Mystery nodes. Closes over the
    /// catalog's usable-item subset once per run; <see cref="RewardCalculator.RollRewardChoice"/> dispatches by
    /// node kind (drop rates / rarity curve / gold curve / item eligibility — run-layer tuning, not a battle
    /// seam).
    /// </summary>
    internal static Func<RewardContext, IRandomSource, RewardChoice> BuildRewardSupplier(
        IReadOnlyList<Item> allItems
    )
    {
        var usable = RewardCalculator.UsableItems(allItems);
        return (ctx, rng) => RewardCalculator.RollRewardChoice(ctx, usable, rng);
    }

    /// <summary>
    /// Builds the run's shop supplier: the injected <c>Func&lt;ShopStockContext, IRandomSource, ShopOffer&gt;</c>
    /// <see cref="RunDirector"/> rolls when a Shop node opens. Closes over the same usable-item subset as the
    /// reward supplier once per run; <see cref="ShopCalculator.BuildStock"/> rolls the per-visit stock and its
    /// run-scaled prices (spend-side run-layer tuning, not a battle seam — the mirror of the reward supplier).
    /// </summary>
    internal static Func<ShopStockContext, IRandomSource, ShopOffer> BuildShopSupplier(
        IReadOnlyList<Item> allItems
    )
    {
        var usable = RewardCalculator.UsableItems(allItems);
        return (ctx, rng) => ShopCalculator.BuildStock(ctx.Depth, usable, rng);
    }

    /// <summary>
    /// Builds the run's themed-draft supplier: the injected
    /// <c>Func&lt;DraftContext, IRandomSource, Task&lt;Creature?&gt;&gt;</c> <see cref="RunDirector"/> rolls after
    /// every win. The policy gate (cadence × n% × non-empty fought pool) is <see cref="DraftCalculator"/>; when it
    /// fires, a creature is built here from the <em>fought-only</em> pool (the guardrail — never an un-fought
    /// species), scaled to the lead's BST/level and run depth like a wild encounter, with its best natural
    /// moveset + a learnset (so it can level up if it later becomes the lead). Returns null on any gate miss (the
    /// common case) — the acquisition-side mirror of <see cref="BuildRewardSupplier"/>.
    /// </summary>
    public Func<DraftContext, IRandomSource, Task<Creature?>> BuildDraftSupplier(
        IReadOnlyList<Attack> allMoves
    ) => (ctx, rng) => TryBuildDraftAsync(ctx, allMoves, rng);

    private async Task<Creature?> TryBuildDraftAsync(
        DraftContext ctx,
        IReadOnlyList<Attack> allMoves,
        IRandomSource rng
    )
    {
        // Policy gate first (no RNG unless a cadence win with a non-empty pool) — a non-offer win leaves the
        // seeded run stream untouched.
        if (!DraftCalculator.ShouldOffer(ctx.BattlesWon, ctx.FoughtSpecies, rng))
            return null;

        await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();

        // The fought-only pool: exactly the species faced in this biome (ENCOUNTER_DESIGN.md §4). The enemy
        // supplier never spawns the player's own species, so it can't leak in here.
        var foughtIds = ctx.FoughtSpecies.ToHashSet();
        var pool = await pokemonCtx
            .Species.AsNoTracking()
            .Where(s => foughtIds.Contains(s.Id))
            .ToListAsync();
        if (pool.Count == 0)
            return null;

        int leadBst =
            ctx.Lead.BaseHP
            + ctx.Lead.BaseAttack
            + ctx.Lead.BaseDefense
            + ctx.Lead.BaseSpecial
            + ctx.Lead.BaseSpeed;

        // Bias toward a fought species near the lead's depth-scaled BST band (same target as a wild encounter);
        // the pool is already biome-themed, so no further biome filter. Level rides the same depth band.
        var species = PickByBst(pool, ScaleTargetBst(leadBst, ctx.Depth), rng, biome: null);
        if (species is null)
            return null;

        int level = ScaleWildLevel(ctx.Lead.Level, ctx.Depth, rng);
        var learnsets = await pokemonCtx
            .Learnsets.AsNoTracking()
            .Where(l =>
                l.Generation == ActiveGeneration
                && l.SpeciesId == species.Id
                && l.Method == LearnMethod.LevelUp
            )
            .ToListAsync();

        var creature = BuildCreature(
            species,
            learnsets,
            allMoves,
            level,
            MoveSelectionStrategy.CanonicalLatest,
            rng
        );
        // A drafted member may later become the lead (Stage 1d) and level up, so give it a learnset like the
        // starter — resolved up-front, consulted on each level gained.
        creature.Learnset = BuildLearnset(learnsets, allMoves);
        return creature;
    }

    /// <summary>
    /// Builds the run's boss-catch supplier: the injected
    /// <c>Func&lt;BossCatchContext, IRandomSource, Task&lt;Creature?&gt;&gt;</c> <see cref="RunDirector"/> rolls
    /// after a Boss win. The policy gate (a small n% catch chance) is <see cref="BossCatchCalculator"/>; when it
    /// fires, a fresh party-ready copy of the defeated boss's species is built here at the boss's level, with its
    /// best natural moveset + a learnset (so it can level up if it later becomes the lead). Returns null on the
    /// (common) no-catch roll — the boss channel's mirror of <see cref="BuildDraftSupplier"/>.
    /// </summary>
    public Func<BossCatchContext, IRandomSource, Task<Creature?>> BuildBossCatchSupplier(
        IReadOnlyList<Attack> allMoves
    ) => (ctx, rng) => TryBuildBossCatchAsync(ctx, allMoves, rng);

    private async Task<Creature?> TryBuildBossCatchAsync(
        BossCatchContext ctx,
        IReadOnlyList<Attack> allMoves,
        IRandomSource rng
    )
    {
        // Policy gate first (a single n% roll) — a Boss win that doesn't catch leaves the seeded run stream after
        // just this roll, and a non-catch never touches the DB.
        if (!BossCatchCalculator.ShouldOffer(rng))
            return null;

        await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();
        // The only candidate is the boss you just beat — look its species up by id and build a fresh full-HP copy
        // (the "catch" model: a party-ready specimen of that species, not the fainted battle instance).
        var species = await pokemonCtx
            .Species.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == ctx.Boss.SpeciesId);
        if (species is null)
            return null;

        var learnsets = await pokemonCtx
            .Learnsets.AsNoTracking()
            .Where(l =>
                l.Generation == ActiveGeneration
                && l.SpeciesId == species.Id
                && l.Method == LearnMethod.LevelUp
            )
            .ToListAsync();

        // Built at the boss's own level with its canonical latest moveset — the caught creature matches the boss
        // you fought in species and level (the biome's themed apex), unlike the draft which picks by BST band.
        // Rolled at Superb DV quality (each value a 50% shot at the 12–15 top band): a strong, earned pickup that
        // reflects beating the boss, without handing the player the boss's own Perfect-DV / all-pool-move build.
        var creature = BuildCreature(
            species,
            learnsets,
            allMoves,
            ctx.Boss.Level,
            MoveSelectionStrategy.CanonicalLatest,
            rng,
            DvQuality.Superb
        );
        // A caught boss may later become the lead (Stage 1d) and level up, so give it a learnset like the starter.
        creature.Learnset = BuildLearnset(learnsets, allMoves);
        return creature;
    }

    /// <summary>
    /// The biomes for <paramref name="region"/> that can actually generate against the active generation's
    /// wild-available species (legendaries/statics/gifts excluded — the same filter as
    /// <see cref="CreateEnemyAsync"/>). Empty biomes never appear; if no availability data exists (a minimally
    /// seeded DB) the full dex is the pool, mirroring the encounter fallback so the map never starves.
    /// </summary>
    private static async Task<IReadOnlyList<BiomeDefinition>> ComputePlayableBiomesAsync(
        PokemonDbContext pokemonCtx,
        Region region
    )
    {
        var allSpecies = await pokemonCtx.Species.AsNoTracking().ToListAsync();
        var wildSet = (
            await pokemonCtx
                .GameAvailability.AsNoTracking()
                .Where(a => a.AvailabilityType == "Wild")
                .Select(a => a.SpeciesId)
                .Distinct()
                .ToListAsync()
        ).ToHashSet();

        var wildPool =
            wildSet.Count > 0 ? allSpecies.Where(s => wildSet.Contains(s.Id)).ToList() : allSpecies;
        return Biomes.Playable(region, wildPool);
    }

    /// <summary>
    /// Builds a fresh wild enemy scaled to the player and the run's <paramref name="depth"/>, excluding the
    /// player's own species, reusing the run's already-loaded move pool. The enemy gets a semi-random "smart"
    /// moveset so encounters vary. Both the target BST (<see cref="ScaleTargetBst"/>) and the level band
    /// (<see cref="ScaleWildLevel"/>) climb with depth: at depth 0 the foe sits a step under the player (the
    /// original behaviour); deeper foes target stronger species and higher levels.
    /// <para>The pool is restricted to <em>wild-available</em> species (excludes legendaries/statics/gifts).
    /// When <paramref name="biome"/> is supplied the pool is further filtered to that biome's type theme
    /// (<see cref="EncounterSelector.PickByBst"/>); it is null until Phase 3's biome graph selects one per
    /// encounter (see <c>ENCOUNTER_DESIGN.md §2</c>). <paramref name="depth"/> is the run's <c>battlesWon</c>,
    /// threaded by <see cref="creaturegame.Combat.RunDirector"/>; Phase 2d's enemy tier modulates the band
    /// further.</para>
    /// </summary>
    public async Task<Creature> CreateEnemyAsync(
        Creature player,
        IReadOnlyList<Attack> allMoves,
        IRandomSource? rng = null,
        BiomeDefinition? biome = null,
        int depth = 0,
        IEnemyArchetype? archetype = null
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

        // The strength tier resolves the levers (BST target, level, DV quality, moveset) from the run context;
        // Medium ≈ the pre-tier behaviour. Tier selection per encounter is Phase 3 (defaults to Medium here).
        var spec = (archetype ?? EnemyArchetypes.Default).Build(
            new EnemyContext(player.Level, playerBst, depth, source)
        );

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
            PickByBst(pool, spec.TargetBst, source, biome)
            ?? throw new InvalidOperationException("No species available to build an encounter.");

        // The TmEnhanced tier draws from level-up AND TM/HM (Machine) moves; base tiers use level-up only and
        // Optimal ranks the whole move pool, so neither needs Machine rows. Include them only when used.
        var allowedMethods =
            spec.Moves == MoveSelectionStrategy.TmEnhanced
                ? new[] { LearnMethod.LevelUp, LearnMethod.Machine }
                : new[] { LearnMethod.LevelUp };
        var learnsets = await pokemonCtx
            .Learnsets.AsNoTracking()
            .Where(l =>
                l.Generation == ActiveGeneration
                && l.SpeciesId == enemySpecies.Id
                && allowedMethods.Contains(l.Method)
            )
            .ToListAsync();

        return BuildCreature(
            enemySpecies,
            learnsets,
            allMoves,
            spec.Level,
            spec.Moves,
            source,
            spec.Dvs,
            spec.MoveCount
        );
    }

    /// <summary>
    /// The evolution data/DB seam for the run loop (<see cref="RunDirector"/>). Loads the player species'
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

    // Depth-scaling tuning (run-layer roguelite knobs, not Gen 1 mechanics — see ScaleWildLevel's note).
    private const int BstGainPerDepth = 10; // each step deeper raises the target BST by this (the TODO curve)
    private const double LevelLiftPerDepth = 0.02; // each step lifts the level band's fractions by this
    private const double MaxLevelLift = 0.40; // …capped here, so the band tops out around [90%, 120%] of player

    /// <summary>
    /// The depth-scaled BST the encounter aims for: the player's BST plus <c>depth × <see cref="BstGainPerDepth"/></c>.
    /// At depth 0 it is exactly the player's BST (the old behaviour); deeper encounters target progressively
    /// stronger species. <see cref="PickByBst"/> bands around this and the pool naturally caps it (no species
    /// exceeds the highest BST available). A run-layer tuning choice, not a battle seam. <c>internal</c> for tests.
    /// </summary>
    internal static int ScaleTargetBst(int playerBst, int depth) =>
        playerBst + Math.Max(0, depth) * BstGainPerDepth;

    /// <summary>
    /// Picks a wild encounter's level as a roguelite difficulty band that climbs with <paramref name="depth"/>:
    /// uniformly within a [min%, max%] window of the player's current level (floored, never below 2). At depth 0
    /// the window is [50%, 80%] (the original behaviour — foes a step under the player); each step deeper lifts
    /// both ends by <see cref="LevelLiftPerDepth"/>, capped at <see cref="MaxLevelLift"/> (≈ [90%, 120%]), so
    /// deep foes reach and then exceed the player's level. A run-layer tuning choice, not a Gen 1 mechanic
    /// (Gen 1 wild levels come from per-area encounter tables), so it lives here, not behind a battle seam.
    /// <c>internal</c> for direct unit testing.
    /// </summary>
    internal static int ScaleWildLevel(int playerLevel, int depth, IRandomSource rng)
    {
        double lift = Math.Min(Math.Max(0, depth) * LevelLiftPerDepth, MaxLevelLift);
        int min = Math.Max(2, (int)(playerLevel * (0.5 + lift)));
        int max = Math.Max(min, (int)(playerLevel * (0.8 + lift)));
        return rng.Next(min, max + 1); // Next's upper bound is exclusive → +1 makes max inclusive
    }

    private static Creature BuildCreature(
        PokemonSpecies species,
        IReadOnlyList<PokemonLearnset> learnsets,
        IReadOnlyList<Attack> allMoves,
        int level,
        MoveSelectionStrategy strategy,
        IRandomSource rng,
        DvQuality dvQuality = DvQuality.Average,
        int maxMoves = LearnsetMoveSelector.MaxMoves
    )
    {
        // Construction rolls DVs (the Creature ctor used the global-RNG default calculator); re-seat the
        // stat calculator on the run's seeded source and re-roll at the requested quality so a run with a fixed
        // seed reproduces the same DVs. DV randomisation is a per-generation rule, so it stays behind IStatCalculator.
        var creature = new Creature(species.Name.ToUpper()) { Level = level };
        creature.StatCalculator = new Gen1StatCalculator(rng);
        creature.StatCalculator.RandomiseDvs(creature, dvQuality);
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
            rng,
            maxMoves
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
/// starting <see cref="Bag"/> and <see cref="Wallet"/> (both transient — lost on death, no save layer yet),
/// the item catalog (id → <see cref="Item"/>) used to resolve item uses and render the bag, and the run's
/// <see cref="PlayableBiomes"/> map (the region's non-empty biomes the RunDirector charts a route through).</summary>
public sealed record RunSetup(
    Creature Player,
    IReadOnlyList<Attack> AllMoves,
    Bag Bag,
    Wallet Wallet,
    IReadOnlyList<Item> AllItems,
    IReadOnlyList<BiomeDefinition> PlayableBiomes
);
