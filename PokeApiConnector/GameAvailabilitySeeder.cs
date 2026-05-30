using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector;

public static class GameAvailabilitySeeder
{
    // Species IDs absent from each version (require trading to obtain)
    private static readonly HashSet<int> NotInRed = [
        27, 28,          // Sandshrew, Sandslash
        37, 38,          // Vulpix, Ninetales
        52, 53,          // Meowth, Persian
        69, 70, 71,      // Bellsprout, Weepinbell, Victreebel
        126,             // Magmar
        127,             // Pinsir  (also Yellow Game Corner — excluded from NotInYellow)
    ];

    private static readonly HashSet<int> NotInBlue = [
        23, 24,          // Ekans, Arbok
        43, 44, 45,      // Oddish, Gloom, Vileplume
        56, 57,          // Mankey, Primeape
        58, 59,          // Growlithe, Arcanine
        123,             // Scyther (also Yellow Game Corner — excluded from NotInYellow)
        125,             // Electabuzz
    ];

    // Species completely unobtainable in Yellow without cross-game trading.
    // Note: 123 (Scyther), 127 (Pinsir), 133 (Eevee) ARE available in Yellow
    // via the Game Corner, so they are intentionally absent from this set.
    private static readonly HashSet<int> NotInYellow = [
        13, 14, 15,      // Weedle, Kakuna, Beedrill  (in both RB, not Yellow)
        23, 24,          // Ekans, Arbok               (Red only)
        26,              // Raichu                     (can't evolve Pikachu in Yellow)
        27, 28,          // Sandshrew, Sandslash       (Blue only)
        37, 38,          // Vulpix, Ninetales          (Blue only)
        43, 44, 45,      // Oddish, Gloom, Vileplume   (Red only)
        52, 53,          // Meowth, Persian            (Blue only)
        56, 57,          // Mankey, Primeape           (Red only)
        58, 59,          // Growlithe, Arcanine        (Red only)
        69, 70, 71,      // Bellsprout line            (Blue only)
        109, 110,        // Koffing, Weezing           (in both RB, not Yellow)
        124,             // Jynx                       (in both RB via NPC trade, not Yellow)
        125,             // Electabuzz                 (Red only)
        126,             // Magmar                     (Blue only)
    ];

    // Per-version availability type overrides. Default is "Wild".
    private static readonly Dictionary<(int id, string version), string> TypeOverrides = new()
    {
        // --- Static encounters ---
        [(143, "red")] = "Static",  [(143, "blue")] = "Static",  [(143, "yellow")] = "Static",  // Snorlax
        [(144, "red")] = "Static",  [(144, "blue")] = "Static",  [(144, "yellow")] = "Static",  // Articuno
        [(145, "red")] = "Static",  [(145, "blue")] = "Static",  [(145, "yellow")] = "Static",  // Zapdos
        [(146, "red")] = "Static",  [(146, "blue")] = "Static",  [(146, "yellow")] = "Static",  // Moltres
        [(150, "red")] = "Static",  [(150, "blue")] = "Static",  [(150, "yellow")] = "Static",  // Mewtwo

        // --- Event only (never legitimately catchable in-game) ---
        [(151, "red")] = "Event",   [(151, "blue")] = "Event",   [(151, "yellow")] = "Event",   // Mew

        // --- Gift (NPC gives directly or fossil revival) ---
        [(106, "red")] = "Gift",    [(106, "blue")] = "Gift",    [(106, "yellow")] = "Gift",    // Hitmonlee
        [(107, "red")] = "Gift",    [(107, "blue")] = "Gift",    [(107, "yellow")] = "Gift",    // Hitmonchan
        [(131, "red")] = "Gift",    [(131, "blue")] = "Gift",    [(131, "yellow")] = "Gift",    // Lapras
        [(133, "red")] = "Gift",    [(133, "blue")] = "Gift",                                   // Eevee (Celadon Mansion)
        [(138, "red")] = "Gift",    [(138, "blue")] = "Gift",    [(138, "yellow")] = "Gift",    // Omanyte (Helix Fossil)
        [(139, "red")] = "Gift",    [(139, "blue")] = "Gift",    [(139, "yellow")] = "Gift",    // Omastar
        [(140, "red")] = "Gift",    [(140, "blue")] = "Gift",    [(140, "yellow")] = "Gift",    // Kabuto (Dome Fossil)
        [(141, "red")] = "Gift",    [(141, "blue")] = "Gift",    [(141, "yellow")] = "Gift",    // Kabutops
        [(142, "red")] = "Gift",    [(142, "blue")] = "Gift",    [(142, "yellow")] = "Gift",    // Aerodactyl (Old Amber)

        // --- Game Corner prizes ---
        [(137, "red")] = "GameCorner", [(137, "blue")] = "GameCorner", [(137, "yellow")] = "GameCorner", // Porygon
        [(123, "yellow")] = "GameCorner",  // Scyther  (Wild in Red; Game Corner only in Yellow)
        [(127, "yellow")] = "GameCorner",  // Pinsir   (Wild in Blue; Game Corner only in Yellow)
        [(133, "yellow")] = "GameCorner",  // Eevee    (Gift in RB; Game Corner only in Yellow)

        // --- NPC in-game trades ---
        [(83,  "red")]  = "Trade",  [(83,  "blue")]  = "Trade",                                // Farfetch'd (trade Spearow; Wild in Yellow)
        [(108, "red")]  = "Trade",  [(108, "blue")]  = "Trade",  [(108, "yellow")] = "Trade",  // Lickitung  (trade Slowbro)
        [(122, "red")]  = "Trade",  [(122, "blue")]  = "Trade",  [(122, "yellow")] = "Trade",  // Mr. Mime   (trade Abra)
        [(124, "red")]  = "Trade",  [(124, "blue")]  = "Trade",                                // Jynx       (trade Poliwhirl; not in Yellow)
    };

    public static async Task SeedGen1Async()
    {
        using var context = new PokemonDbContext();

        // Apply pending migrations first
        context.EnsureDatabaseCreated();

        // Clear all existing availability rows and re-seed cleanly
        await context.GameAvailability.ExecuteDeleteAsync();

        var rows = new List<PokemonGameAvailability>();

        foreach (var id in Enumerable.Range(1, 151))
        {
            foreach (var version in new[] { "red", "blue", "yellow" })
            {
                var excluded = version switch
                {
                    "red"    => NotInRed.Contains(id),
                    "blue"   => NotInBlue.Contains(id),
                    "yellow" => NotInYellow.Contains(id),
                    _        => false,
                };
                if (excluded) continue;

                TypeOverrides.TryGetValue((id, version), out var type);
                rows.Add(new PokemonGameAvailability
                {
                    SpeciesId       = id,
                    GameVersion     = version,
                    AvailabilityType = type ?? "Wild",
                });
            }
        }

        context.GameAvailability.AddRange(rows);
        await context.SaveChangesAsync();

        Console.WriteLine($"Seeded {rows.Count} Gen 1 game availability rows.");
    }
}
