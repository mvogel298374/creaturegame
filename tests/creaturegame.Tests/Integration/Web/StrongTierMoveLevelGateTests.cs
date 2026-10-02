using creaturegame.Combat;
using creaturegame.DB;
using creaturegame.Generations;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The Strong/Boss moveset level gate against the live databases (docs/ENCOUNTER_DESIGN.md §3.5): an enemy is
/// never given a move before its legality level — a level-up row's <c>LearnLevel</c>, or a TM/HM move's curated
/// <c>Attack.MinLevel</c> floor. The regression: an L10 Elite Psyduck rolling Hydro Pump.
/// </summary>
public class StrongTierMoveLevelGateTests
{
    private static EncounterFactory BuildFactory() =>
        new(
            new LiveDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()),
            new LiveDbContextFactory<MovesDbContext>(() => new MovesDbContext()),
            new LiveDbContextFactory<ItemsDbContext>(() => new ItemsDbContext())
        );

    [Fact]
    public async Task EveryMachineLearnedMove_HasAMinLevelFloor_AndNoOtherMoveDoes()
    {
        // MinLevel is curated data (PokeApiConnector MoveMinLevels). Every move any species learns by TM/HM must
        // have a floor — a missing one leaves that move ungated — and a non-machine move must not carry one.
        await using var pokemonCtx = new PokemonDbContext();
        await using var movesCtx = new MovesDbContext();
        var machineMoveIds = (
            await pokemonCtx
                .Learnsets.AsNoTracking()
                .Where(l => l.Method == LearnMethod.Machine)
                .Select(l => l.MoveId)
                .Distinct()
                .ToListAsync()
        ).ToHashSet();
        var moves = await movesCtx.Moves.AsNoTracking().ToListAsync();

        Assert.NotEmpty(machineMoveIds);
        Assert.All(
            moves.Where(m => machineMoveIds.Contains(m.Id)),
            m => Assert.True(m.MinLevel is > 0, $"{m.Name} is TM/HM-learned but has no MinLevel")
        );
        Assert.All(
            moves.Where(m => !machineMoveIds.Contains(m.Id)),
            m => Assert.True(m.MinLevel is null, $"{m.Name} isn't TM/HM-learned but has a MinLevel")
        );
    }

    [Fact]
    public async Task EverySpecies_HasALevelOneLevelUpMove_SoTheStrongTierLevelGateNeverEmpties()
    {
        // The strong tiers gate their pool on level (LearnsetMoveSelector.IsLevelLegal). They need no
        // "gate came up empty" fallback only because every species has a level-1 level-up row, which is legal at
        // any level. If an import ever drops that, SelectWithFallback's random-move fallback would bypass the
        // gate — so this pins the invariant the design relies on (ENCOUNTER_DESIGN.md §3.5).
        await using var ctx = new PokemonDbContext();
        var speciesIds = await ctx.Species.AsNoTracking().Select(s => s.Id).ToListAsync();
        var withLevelOneMove = (
            await ctx
                .Learnsets.AsNoTracking()
                .Where(l =>
                    l.Generation == 1 && l.Method == LearnMethod.LevelUp && l.LearnLevel <= 1
                )
                .Select(l => l.SpeciesId)
                .Distinct()
                .ToListAsync()
        ).ToHashSet();

        Assert.All(speciesIds, id => Assert.Contains(id, withLevelOneMove));
    }

    [Theory]
    [InlineData(nameof(EnemyArchetypes.Strong))]
    [InlineData(nameof(EnemyArchetypes.Boss))]
    public async Task StrongTierEnemies_NeverHoldAMoveBeforeItsLegalityLevel(string tier)
    {
        var archetype =
            tier == nameof(EnemyArchetypes.Boss) ? EnemyArchetypes.Boss : EnemyArchetypes.Strong;
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(
            1,
            10, // a low-level lead keeps enemy levels low, where the old ungated pool was most wrong
            Gen1Profile.Instance,
            new SeededRandomSource(5)
        );
        Assert.NotNull(setup);
        var movesById = setup!.AllMoves.ToDictionary(m => m.Id);
        await using var ctx = new PokemonDbContext();

        for (int i = 0; i < 60; i++)
        {
            var enemy = await factory.CreateEnemyAsync(
                setup.Player,
                setup.AllMoves,
                Gen1Profile.Instance,
                new SeededRandomSource(i),
                depth: i % 4,
                archetype: archetype
            );

            var rows = await ctx
                .Learnsets.AsNoTracking()
                .Where(l => l.SpeciesId == enemy.SpeciesId && l.Generation == 1)
                .ToListAsync();

            foreach (var move in enemy.MoveSet.Select(m => m.Base))
            {
                var row = rows.First(r => r.MoveId == move.Id);
                int legalFrom =
                    row.Method == LearnMethod.Machine
                        ? movesById[move.Id].MinLevel ?? 0
                        : row.LearnLevel;
                Assert.True(
                    enemy.Level >= legalFrom,
                    $"{enemy.Name} L{enemy.Level} holds {move.Name}, legal from L{legalFrom}"
                );
            }
        }
    }
}
