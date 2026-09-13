using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Generations;
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
    // Biome-map draw size (tuning lever) — see ENCOUNTER_DESIGN.md §2.1.
    public const int RunBiomeMapSize = 10;

    /// <summary>
    /// Loads the move pool and builds the player creature with its canonical moveset. Returns null if the
    /// species id is unknown or the move database is empty.
    /// </summary>
    /// <param name="profile">The run's generation profile. <b>Required, deliberately un-defaulted</b> — see the
    /// note on <see cref="BuildCreature"/>.</param>
    public async Task<RunSetup?> CreatePlayerSetupAsync(
        int speciesId,
        int level,
        GenerationProfile profile,
        IRandomSource? rng = null
    )
    {
        var source = rng ?? SystemRandomSource.Instance;
        await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();
        // Scoped via profile.ContentScope — every catalog read in this class goes through it, see
        // GENERATION_PROFILE.md §5(b). An out-of-scope starter is rejected like a nonexistent id.
        var species = await profile
            .ContentScope.Species(pokemonCtx.Species.AsNoTracking())
            .FirstOrDefaultAsync(s => s.Id == speciesId);
        if (species == null)
            return null;

        await using var movesCtx = await movesFactory.CreateDbContextAsync();
        // Loaded once and threaded everywhere, so scoping it here scopes every moveset the run ever builds.
        var allMoves = await profile
            .ContentScope.Moves(movesCtx.Moves.AsNoTracking())
            .ToListAsync();
        if (allMoves.Count == 0)
            return null;

        var learnsets = await LoadLearnsetsAsync(
            pokemonCtx,
            profile,
            species.Id,
            LearnMethod.LevelUp
        );

        var player = BuildCreature(
            species,
            learnsets,
            allMoves,
            level,
            MoveSelectionStrategy.CanonicalLatest,
            source,
            profile
        );
        // Only the player levels up, so only the player carries a learnset (its moves resolved up-front and
        // consulted by the battle loop on each level gained). Persists with the creature across the chain.
        player.Learnset = BuildLearnset(learnsets, allMoves);

        // Seed the run's starting bag; the run economy (battle drops, Treasure/Mystery) grows it from here —
        // consumed items stay gone across the chain (the Poké Center refills HP/PP/status, not the bag).
        await using var itemsCtx = await itemsFactory.CreateDbContextAsync();
        var allItems = await profile
            .ContentScope.Items(itemsCtx.Items.AsNoTracking())
            .ToListAsync();
        var bag = BuildStartingBag(allItems);

        // The run's biome map: a seeded connected subset of the region's playable biomes — ENCOUNTER_DESIGN.md
        // §2.1/§2.2. Same seed ⇒ same map.
        var playable = await ComputePlayableBiomesAsync(pokemonCtx, profile);
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

    /// <summary>The run's reward supplier for <see cref="RunDirector"/> (battle win / Treasure / Mystery). Reward
    /// roll mechanics (drop rates, rarity, gold, category bias) → <c>ENCOUNTER_DESIGN.md §5.1</c>.</summary>
    internal static Func<RewardContext, IRandomSource, RewardChoice> BuildRewardSupplier(
        IReadOnlyList<Item> allItems
    )
    {
        var usable = RewardCalculator.UsableItems(allItems);
        return (ctx, rng) => RewardCalculator.RollRewardChoice(ctx, usable, rng);
    }

    /// <summary>The run's shop supplier for <see cref="RunDirector"/> (Shop node stock/prices) — mirrors
    /// <see cref="BuildRewardSupplier"/>. See <c>GAME_LOOP.md §5</c> Shop row.</summary>
    internal static Func<ShopStockContext, IRandomSource, ShopOffer> BuildShopSupplier(
        IReadOnlyList<Item> allItems
    )
    {
        var usable = RewardCalculator.UsableItems(allItems);
        return (ctx, rng) => ShopCalculator.BuildStock(ctx.Depth, usable, rng);
    }

    /// <summary>The run's themed-draft supplier for <see cref="RunDirector"/> — the acquisition-side mirror of
    /// <see cref="BuildRewardSupplier"/>. Gate + fought-only guardrail → <c>ENCOUNTER_DESIGN.md §4</c>.</summary>
    public Func<DraftContext, IRandomSource, Task<Creature?>> BuildDraftSupplier(
        IReadOnlyList<Attack> allMoves,
        GenerationProfile profile
    ) => (ctx, rng) => TryBuildDraftAsync(ctx, allMoves, rng, profile);

    private async Task<Creature?> TryBuildDraftAsync(
        DraftContext ctx,
        IReadOnlyList<Attack> allMoves,
        IRandomSource rng,
        GenerationProfile profile
    )
    {
        // Gate first — no RNG unless it fires, so a non-offer win leaves the seeded run stream untouched.
        if (!DraftCalculator.ShouldOffer(ctx.BattlesWon, ctx.FoughtSpecies, rng))
            return null;

        await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();

        // The fought-only pool (ENCOUNTER_DESIGN.md §4); the enemy supplier never spawns the player's own
        // species, so it can't leak in here.
        var foughtIds = ctx.FoughtSpecies.ToHashSet();
        var pool = await profile
            .ContentScope.Species(pokemonCtx.Species.AsNoTracking())
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

        // Biased to the lead's depth-scaled BST band, like a wild encounter; the pool is already biome-themed
        // so no further biome filter.
        var species = PickByBst(pool, ScaleTargetBst(leadBst, ctx.Depth), rng, biome: null);
        if (species is null)
            return null;

        int level = ScaleWildLevel(ctx.Lead.Level, ctx.Depth, rng);
        var learnsets = await LoadLearnsetsAsync(
            pokemonCtx,
            profile,
            species.Id,
            LearnMethod.LevelUp
        );

        var creature = BuildCreature(
            species,
            learnsets,
            allMoves,
            level,
            MoveSelectionStrategy.CanonicalLatest,
            rng,
            profile
        );
        // May later become the lead (Stage 1d) and level up, so give it a learnset like the starter.
        creature.Learnset = BuildLearnset(learnsets, allMoves);
        return creature;
    }

    /// <summary>The run's boss-catch supplier for <see cref="RunDirector"/> — the boss channel's mirror of
    /// <see cref="BuildDraftSupplier"/>. Gate + caught-boss strength → <c>ENCOUNTER_DESIGN.md §4</c>.</summary>
    public Func<BossCatchContext, IRandomSource, Task<Creature?>> BuildBossCatchSupplier(
        IReadOnlyList<Attack> allMoves,
        GenerationProfile profile
    ) => (ctx, rng) => TryBuildBossCatchAsync(ctx, allMoves, rng, profile);

    private async Task<Creature?> TryBuildBossCatchAsync(
        BossCatchContext ctx,
        IReadOnlyList<Attack> allMoves,
        IRandomSource rng,
        GenerationProfile profile
    )
    {
        // Gate first — a non-catch never touches the DB.
        if (!BossCatchCalculator.ShouldOffer(rng))
            return null;

        await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();
        // The only candidate is the boss just beaten — a fresh full-HP copy of its species, not the fainted
        // battle instance.
        var species = await profile
            .ContentScope.Species(pokemonCtx.Species.AsNoTracking())
            .FirstOrDefaultAsync(s => s.Id == ctx.Boss.SpeciesId);
        if (species is null)
            return null;

        var learnsets = await LoadLearnsetsAsync(
            pokemonCtx,
            profile,
            species.Id,
            LearnMethod.LevelUp
        );

        // Matches the boss's own species/level at Superb DV quality — a strong pickup, but deliberately not the
        // boss's own Perfect-DV/Optimal-move build (ENCOUNTER_DESIGN.md §4).
        var creature = BuildCreature(
            species,
            learnsets,
            allMoves,
            ctx.Boss.Level,
            MoveSelectionStrategy.CanonicalLatest,
            rng,
            profile,
            DvQuality.Superb
        );
        // May later become the lead (Stage 1d) and level up, so give it a learnset like the starter.
        creature.Learnset = BuildLearnset(learnsets, allMoves);
        return creature;
    }

    /// <summary>
    /// The profile's biomes that can actually generate against the wild-available, generation-scoped species
    /// pool (same filter as <see cref="CreateEnemyAsync"/>); empty biomes never appear. Falls back to the full
    /// dex when no availability data exists, so the map never starves. Region/generation-scoping history →
    /// <c>GENERATION_PROFILE.md</c> §5(b)/§6.
    /// </summary>
    private static async Task<IReadOnlyList<BiomeDefinition>> ComputePlayableBiomesAsync(
        PokemonDbContext pokemonCtx,
        GenerationProfile profile
    )
    {
        var allSpecies = await profile
            .ContentScope.Species(pokemonCtx.Species.AsNoTracking())
            .ToListAsync();
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
        return Biomes.Playable(profile.BiomeRoster, wildPool);
    }

    /// <summary>
    /// The one home for the generation-filtered learnset read: the rows for <paramref name="speciesId"/> learned
    /// by any of <paramref name="methods"/> in the profile's generation. Every learnset query in this class goes
    /// through here — it was previously copy-pasted at five sites (each re-deriving the generation locally),
    /// which is the same duplication hazard the profile work keeps deleting, one query at a time.
    /// </summary>
    private static Task<List<PokemonLearnset>> LoadLearnsetsAsync(
        PokemonDbContext pokemonCtx,
        GenerationProfile profile,
        int speciesId,
        params LearnMethod[] methods
    )
    {
        int gen = (int)profile.Generation;
        return pokemonCtx
            .Learnsets.AsNoTracking()
            .Where(l =>
                l.Generation == gen && l.SpeciesId == speciesId && methods.Contains(l.Method)
            )
            .ToListAsync();
    }

    /// <summary>
    /// Builds a fresh wild enemy scaled to the player and the run's <paramref name="depth"/> (nodes traversed —
    /// <c>RunState.RunDepth</c>, threaded by <see cref="creaturegame.Combat.RunDirector"/>), excluding the
    /// player's own species. Filtered to wild-available species, further to <paramref name="biome"/>'s type
    /// theme when supplied. Depth/tier bands and levers → <c>ENCOUNTER_DESIGN.md §3</c>.
    /// </summary>
    /// <param name="profile">The run's generation profile. <b>Required, deliberately un-defaulted</b> — see the
    /// note on <see cref="BuildCreature"/>.</param>
    public async Task<Creature> CreateEnemyAsync(
        Creature player,
        IReadOnlyList<Attack> allMoves,
        GenerationProfile profile,
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

        var pool = await profile
            .ContentScope.Species(pokemonCtx.Species.AsNoTracking())
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
        var learnsets = await LoadLearnsetsAsync(
            pokemonCtx,
            profile,
            enemySpecies.Id,
            allowedMethods
        );

        return BuildCreature(
            enemySpecies,
            learnsets,
            allMoves,
            spec.Level,
            spec.Moves,
            source,
            profile,
            spec.Dvs,
            spec.MoveCount
        );
    }

    /// <summary>
    /// The evolution data/DB seam for the run loop (<see cref="RunDirector"/>): loads the player species'
    /// evolution edges, runs <see cref="IEvolutionRules"/> against the player's current level, and resolves a
    /// fired evolution's species + learnset into an <see cref="EvolutionOutcome"/> (null if nothing evolves).
    /// </summary>
    /// <param name="profile">Supplies both halves of the question — the rules seam and the generation whose
    /// edges are read — so they can never disagree. <b>Required, deliberately un-defaulted</b>, same reason as
    /// <see cref="BuildCreature"/>'s note (<c>docs/GENERATION_PROFILE.md</c> §4.2).</param>
    public async Task<EvolutionOutcome?> ResolvePlayerEvolutionAsync(
        Creature player,
        IReadOnlyList<Attack> allMoves,
        GenerationProfile profile
    )
    {
        int gen = (int)profile.Generation;
        await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();
        var edges = await pokemonCtx
            .Evolutions.AsNoTracking()
            .Where(e => e.Generation == gen && e.FromSpeciesId == player.SpeciesId)
            .ToListAsync();
        if (edges.Count == 0)
            return null;

        var result = profile.EvolutionRules.CheckEvolution(
            player,
            new EvolutionContext.LeveledTo(player.Level),
            edges
        );
        if (result is null)
            return null;

        // Scoped like every other species read here (GENERATION_PROFILE.md §5(b)) though redundant — the edges
        // above are already generation-filtered; uniformity costs nothing and beats a per-site judgement call.
        var newForm = await profile
            .ContentScope.Species(pokemonCtx.Species.AsNoTracking())
            .FirstOrDefaultAsync(s => s.Id == result.ToSpeciesId);
        if (newForm is null)
            return null;

        var learnsets = await LoadLearnsetsAsync(
            pokemonCtx,
            profile,
            newForm.Id,
            LearnMethod.LevelUp
        );

        return new EvolutionOutcome(newForm, BuildLearnset(learnsets, allMoves));
    }

    // Depth-scaling tuning (run-layer roguelite knobs, not Gen 1 mechanics — formula + worked example →
    // ENCOUNTER_DESIGN.md §3.2/§3.3).
    private const int BstGainPerDepth = 10;
    private const double LevelLiftPerDepth = 0.02;
    private const double MaxLevelLift = 0.40;

    /// <summary>The depth-scaled BST target for <see cref="PickByBst"/>. <c>internal</c> for tests.</summary>
    internal static int ScaleTargetBst(int playerBst, int depth) =>
        playerBst + Math.Max(0, depth) * BstGainPerDepth;

    /// <summary>The depth-scaled wild-encounter level band. <c>internal</c> for direct unit testing.</summary>
    internal static int ScaleWildLevel(int playerLevel, int depth, IRandomSource rng)
    {
        double lift = Math.Min(Math.Max(0, depth) * LevelLiftPerDepth, MaxLevelLift);
        int min = Math.Max(2, (int)(playerLevel * (0.5 + lift)));
        int max = Math.Max(min, (int)(playerLevel * (0.8 + lift)));
        return rng.Next(min, max + 1); // Next's upper bound is exclusive → +1 makes max inclusive
    }

    /// <param name="profile">The run's generation profile — source of the <see cref="IStatCalculator"/> seam.
    /// <b>Required, deliberately un-defaulted</b>, everywhere on this path: a <c>?? Gen1…</c> fallback would let
    /// a forgotten thread compile, pass every test, and silently run Gen 1 stat math
    /// (<c>docs/GENERATION_PROFILE.md</c> §4.2).</param>
    private static Creature BuildCreature(
        PokemonSpecies species,
        IReadOnlyList<PokemonLearnset> learnsets,
        IReadOnlyList<Attack> allMoves,
        int level,
        MoveSelectionStrategy strategy,
        IRandomSource rng,
        GenerationProfile profile,
        DvQuality dvQuality = DvQuality.Average,
        int maxMoves = LearnsetMoveSelector.MaxMoves
    )
    {
        // Re-seat the stat calculator on the run's seeded source and re-roll DVs at the requested quality, so a
        // fixed seed reproduces them (GENERATION_PROFILE.md — the profile exposes a factory, not a singleton,
        // precisely so it can be seeded per run).
        var creature = new Creature(species.Name.ToUpper()) { Level = level };
        creature.StatCalculator = profile.BuildStatCalculator(rng);
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
