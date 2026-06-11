using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
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
    IDbContextFactory<MovesDbContext> movesFactory
)
{
    // The single generation switch — learnset rows are tagged by generation and filtered by this.
    private const int ActiveGeneration = 1;

    /// <summary>
    /// Loads the move pool and builds the player creature with its canonical moveset. Returns null if the
    /// species id is unknown or the move database is empty.
    /// </summary>
    public async Task<RunSetup?> CreatePlayerSetupAsync(int speciesId, int level)
    {
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
            .Where(l => l.Generation == ActiveGeneration && l.SpeciesId == species.Id)
            .ToListAsync();

        var player = BuildCreature(
            species,
            learnsets,
            allMoves,
            level,
            MoveSelectionStrategy.CanonicalLatest
        );
        // Only the player levels up, so only the player carries a learnset (its moves resolved up-front and
        // consulted by the battle loop on each level gained). Persists with the creature across the chain.
        player.Learnset = BuildLearnset(learnsets, allMoves);
        return new RunSetup(player, allMoves);
    }

    /// <summary>
    /// Builds a fresh wild enemy scaled to the player's current level and BST, excluding the player's own
    /// species, reusing the run's already-loaded move pool. The enemy gets a semi-random "smart" moveset so
    /// encounters vary. Enemy level sits in a roguelite band below the player (see <see cref="ScaleWildLevel"/>)
    /// so the chain stays winnable while still scaling up as the player levels.
    /// </summary>
    public async Task<Creature> CreateEnemyAsync(
        Creature player,
        IReadOnlyList<Attack> allMoves,
        IRandomSource? rng = null
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

        var pool = await pokemonCtx
            .Species.AsNoTracking()
            .Where(s => s.Id != player.SpeciesId)
            .ToListAsync();
        var enemySpecies =
            PickByBst(pool, playerBst, source)
            ?? throw new InvalidOperationException("No species available to build an encounter.");

        int enemyLevel = ScaleWildLevel(player.Level, source);

        var learnsets = await pokemonCtx
            .Learnsets.AsNoTracking()
            .Where(l => l.Generation == ActiveGeneration && l.SpeciesId == enemySpecies.Id)
            .ToListAsync();

        return BuildCreature(
            enemySpecies,
            learnsets,
            allMoves,
            enemyLevel,
            MoveSelectionStrategy.WeightedSmart
        );
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
        MoveSelectionStrategy strategy
    )
    {
        var creature = new Creature(species.Name.ToUpper()) { Level = level };
        creature.InitializeFromSpecies(species);
        creature.Experience = creature.CalculateExperienceForLevel(level);

        var speciesLearnset = learnsets.Where(l => l.SpeciesId == species.Id).ToList();
        var moves = LearnsetMoveSelector.SelectWithFallback(
            strategy,
            speciesLearnset,
            allMoves,
            level,
            species.Type1,
            species.Type2
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

/// <summary>The starting state of a run: the built player and the shared move pool the chain reuses.</summary>
public sealed record RunSetup(Creature Player, IReadOnlyList<Attack> AllMoves);
