using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Web.Battle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static creaturegame.Creatures.EncounterSelector;

namespace creaturegame.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController(
    GameSessionManager sessionManager,
    IDbContextFactory<PokemonDbContext> pokemonFactory,
    IDbContextFactory<MovesDbContext> movesFactory
) : ControllerBase
{
    // The single generation switch. Learnset rows are tagged by generation; the runtime
    // filters by this constant so there is one place to change when more generations land.
    private const int ActiveGeneration = 1;

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartGameRequest req)
    {
        try
        {
            await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();
            var playerSpecies = await pokemonCtx
                .Species.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == req.SpeciesId);
            if (playerSpecies == null)
                return BadRequest(new { error = "Unknown species" });

            await using var movesCtx = await movesFactory.CreateDbContextAsync();
            var allMoves = await movesCtx.Moves.AsNoTracking().ToListAsync();
            if (allMoves.Count == 0)
                return StatusCode(500, new { error = "No moves in database" });

            int playerLevel = Math.Clamp(req.Level ?? 50, 5, 100);
            int playerBst = Bst(playerSpecies);

            var allSpecies = await pokemonCtx
                .Species.AsNoTracking()
                .Where(s => s.Id != playerSpecies.Id)
                .ToListAsync();
            var enemySpecies = PickByBst(allSpecies, playerBst);
            if (enemySpecies == null)
                return StatusCode(500, new { error = "No species in database" });

            int enemyLevel = Math.Clamp(playerLevel + Random.Shared.Next(-3, 4), 5, 100);

            // Learnsets for both combatants in the active generation (MoveId is a logical
            // reference into moves.db, resolved against allMoves inside the selector).
            var learnsets = await pokemonCtx
                .Learnsets.AsNoTracking()
                .Where(l =>
                    l.Generation == ActiveGeneration
                    && (l.SpeciesId == playerSpecies.Id || l.SpeciesId == enemySpecies.Id)
                )
                .ToListAsync();

            // Player gets the canonical most-recent moveset; the enemy gets a semi-random,
            // semi-intelligent set so encounters vary.
            var player = BuildCreature(
                playerSpecies,
                learnsets,
                allMoves,
                playerLevel,
                MoveSelectionStrategy.CanonicalLatest
            );
            var enemy = BuildCreature(
                enemySpecies,
                learnsets,
                allMoves,
                enemyLevel,
                MoveSelectionStrategy.WeightedSmart
            );

            var gameId = sessionManager.RegisterSession(player, enemy, allMoves);
            return Ok(new { gameId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameController] Start failed: {ex.Message}");
            return StatusCode(500, new { error = "Failed to start game" });
        }
    }

    private static Creature BuildCreature(
        PokemonSpecies species,
        List<PokemonLearnset> learnsets,
        List<Attack> allMoves,
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
}

public record StartGameRequest(int SpeciesId, int? Level = null);
