using creaturegame.Combat;
using creaturegame.Generations;
using creaturegame.Web.Battle;
using Microsoft.AspNetCore.Mvc;

namespace creaturegame.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController(GameSessionManager sessionManager, EncounterFactory encounters)
    : ControllerBase
{
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartGameRequest req)
    {
        try
        {
            int playerLevel = Math.Clamp(req.Level ?? 50, 5, 100);
            var difficulty = ParseDifficulty(req.Difficulty);
            var generation = ParseGeneration(req.Generation);

            // One seed per run. The client may supply one (replay / deterministic E2E); otherwise we pick a
            // random one. Either way the whole run — player DVs/moves, every enemy's species/level/DVs/moves,
            // the battle rolls, and the AI's choices — flows from this single seeded source, so the run is
            // reproducible by its seed. Random.Shared here only *chooses* a seed; no run draw is unseeded.
            int seed = req.Seed ?? Random.Shared.Next();
            var rng = new SeededRandomSource(seed);

            // The run's profile, resolved here because the STARTER is built before a session exists — the same
            // profile GameSessionManager resolves again from the stored Generation when the run is claimed.
            var setup = await encounters.CreatePlayerSetupAsync(
                req.SpeciesId,
                playerLevel,
                GameSessionManager.ProfileFor(generation),
                rng
            );
            if (setup == null)
                return BadRequest(new { error = "Unknown species or empty move database" });

            // The session holds the persistent player, the shared move pool, the run's bag + item catalog, and
            // the run's seeded RNG; each encounter's enemy is built by the run loop via the EncounterFactory
            // (so the chain can keep producing foes), all drawing from the same stream.
            var gameId = sessionManager.RegisterSession(
                setup.Player,
                setup.AllMoves,
                setup.Bag,
                setup.Wallet,
                setup.AllItems,
                rng,
                setup.PlayableBiomes,
                difficulty,
                generation
            );
            Console.WriteLine(
                $"[GameController] Started run {gameId} with seed {seed}, difficulty {difficulty}, generation {generation}."
            );
            return Ok(new { gameId, seed });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameController] Start failed: {ex.Message}");
            return StatusCode(500, new { error = "Failed to start game" });
        }
    }

    /// <summary>Falls back to <see cref="Difficulty.Normal"/> on a missing/unrecognised value — never a dead
    /// request over a client typo or a stale client sending nothing. <c>internal</c> (not private) so the
    /// parsing/fallback behaviour is directly testable rather than only exercised through the full endpoint.</summary>
    internal static Difficulty ParseDifficulty(string? value) =>
        Enum.TryParse<Difficulty>(value, ignoreCase: true, out var parsed)
            ? parsed
            : Difficulty.Normal;

    /// <summary>Falls back to <see cref="Generation.One"/> on a missing/unrecognised value — same contract as
    /// <see cref="ParseDifficulty"/>, so a stale client that sends nothing still starts a Gen 1 run.
    /// <c>internal</c> so the parse/fallback is directly testable.
    /// <para>Also rejects a value that names a real enum member with <b>no registered profile</b>, so the
    /// untrusted boundary can never hand <c>GenerationProfiles.For</c> something it would throw on — the parse
    /// is what makes that method's throw an internal-wiring assertion rather than a 500 on user input.</para></summary>
    internal static Generation ParseGeneration(string? value) =>
        Enum.TryParse<Generation>(value, ignoreCase: true, out var parsed)
        && GenerationProfiles.Registered.Contains(parsed)
            ? parsed
            : Generation.One;

    /// <summary>
    /// On-demand snapshot of the run's live player creature for the in-battle overview (CHECK POKEMON):
    /// actual stats, DVs, Stat-Exp, XP, and full move data. Reads the in-session <see cref="creaturegame.Creatures.Creature"/>.
    /// </summary>
    [HttpGet("{gameId}/player")]
    public IActionResult GetPlayer(string gameId)
    {
        var player = sessionManager.GetPlayerCreature(gameId);
        var generation = sessionManager.GetGeneration(gameId);
        if (player is null || generation is null)
            return NotFound(new { error = "No active game with that id" });
        return Ok(PlayerOverviewDto.From(player, generation.Value));
    }

    /// <summary>
    /// The run's current bag contents (held quantity joined with item data) for the in-battle bag menu.
    /// Reads the live session bag; 404 if the game is unknown or not yet started.
    /// </summary>
    [HttpGet("{gameId}/bag")]
    public IActionResult GetBag(string gameId)
    {
        var bag = sessionManager.GetBagContents(gameId);
        if (bag is null)
            return NotFound(new { error = "No active game with that id" });
        return Ok(bag);
    }

    /// <summary>The run's current gold balance for the HUD. 404 if the game is unknown or not yet started —
    /// parity with <see cref="GetBag"/>.</summary>
    [HttpGet("{gameId}/gold")]
    public IActionResult GetGold(string gameId)
    {
        var gold = sessionManager.GetWallet(gameId);
        if (gold is null)
            return NotFound(new { error = "No active game with that id" });
        return Ok(new { gold });
    }

    /// <summary>The run's current party roster for the roster panel to hydrate on load / after a reconnect
    /// (events don't replay across a disconnect gap). Same wire shape as the pushed <c>PartyUpdated</c> event.
    /// 404 if the game is unknown — parity with <see cref="GetBag"/> / <see cref="GetGold"/>.</summary>
    [HttpGet("{gameId}/party")]
    public IActionResult GetParty(string gameId)
    {
        var party = sessionManager.GetParty(gameId);
        if (party is null)
            return NotFound(new { error = "No active game with that id" });
        return Ok(party);
    }
}

public record StartGameRequest(
    int SpeciesId,
    int? Level = null,
    int? Seed = null,
    string? Difficulty = null,
    string? Generation = null
);
