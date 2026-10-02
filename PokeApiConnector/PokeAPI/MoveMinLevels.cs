using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector.PokeAPI;

/// <summary>
/// Curated <c>Attack.MinLevel</c> floors for every Gen 1 TM/HM move, keyed by PokeAPI move name (the same
/// keying as <c>MoveImport.Gen1MoveEffects</c>). Hand-curated like <see cref="GameAvailabilitySeeder"/> — PokeAPI
/// doesn't carry where a TM is obtainable. Each floor is the typical party level on reaching the TM's earliest
/// Red/Blue source (a TM with several sources, e.g. Mt. Moon / Celadon Department Store, takes the earliest), so
/// a Strong/Boss foe isn't "taught" a TM move before a player could plausibly own it. Source: pokemondb.net's
/// Red/Blue TM and HM tables; design → docs/ENCOUNTER_DESIGN.md §3.5.
/// </summary>
public static class MoveMinLevels
{
    // Progression anchors (approximate party level at that point in a Red/Blue run).
    private const int MtMoon = 12; // Mt. Moon, Route 4, Pewter Gym, Route 2 (Flash)
    private const int Cerulean = 16; // Routes 24/25, Cerulean City + Gym
    private const int SsAnne = 22; // S.S. Anne, Route 9
    private const int Vermilion = 24; // Vermilion Gym
    private const int Celadon = 30; // Celadon Dept. Store/Game Corner/Gym, Rocket Hideout, Routes 12/15/16
    private const int PowerPlant = 35;
    private const int Mansion = 40; // Pokémon Mansion, Saffron, Fuchsia Gym, Safari Zone
    private const int Silph = 42; // Silph Co., Cinnabar Lab
    private const int Cinnabar = 45; // Cinnabar Gym, Victory Road
    private const int Viridian = 47; // Viridian Gym / City (post-Giovanni)

    public static IReadOnlyDictionary<string, int> Floors { get; } =
        new Dictionary<string, int>
        {
            // TM01–TM10
            ["mega-punch"] = MtMoon,
            ["razor-wind"] = Celadon,
            ["swords-dance"] = Silph,
            ["whirlwind"] = MtMoon,
            ["mega-kick"] = Celadon,
            ["toxic"] = Mansion,
            ["horn-drill"] = Celadon,
            ["body-slam"] = SsAnne,
            ["take-down"] = Celadon,
            ["double-edge"] = Celadon,
            // TM11–TM20
            ["bubble-beam"] = Cerulean,
            ["water-gun"] = MtMoon,
            ["ice-beam"] = Celadon,
            ["blizzard"] = Mansion,
            ["hyper-beam"] = Celadon,
            ["pay-day"] = Celadon,
            ["submission"] = Celadon,
            ["counter"] = Celadon,
            ["seismic-toss"] = Cerulean,
            ["rage"] = Celadon,
            // TM21–TM30
            ["mega-drain"] = Celadon,
            ["solar-beam"] = Mansion,
            ["dragon-rage"] = Celadon,
            ["thunderbolt"] = Vermilion,
            ["thunder"] = PowerPlant,
            ["earthquake"] = Silph,
            ["fissure"] = Viridian,
            ["dig"] = Cerulean,
            ["psychic"] = Mansion,
            ["teleport"] = SsAnne,
            // TM31–TM40
            ["mimic"] = Mansion,
            ["double-team"] = Celadon,
            ["reflect"] = Celadon,
            ["bide"] = MtMoon,
            ["metronome"] = Silph,
            ["self-destruct"] = Silph,
            ["egg-bomb"] = Celadon,
            ["fire-blast"] = Cinnabar,
            ["swift"] = Celadon,
            ["skull-bash"] = Mansion,
            // TM41–TM50
            ["soft-boiled"] = Celadon,
            ["dream-eater"] = Viridian,
            ["sky-attack"] = Cinnabar,
            ["rest"] = SsAnne,
            ["thunder-wave"] = Cerulean,
            ["psywave"] = Mansion,
            ["explosion"] = Cinnabar,
            ["rock-slide"] = Celadon,
            ["tri-attack"] = Celadon,
            ["substitute"] = Celadon,
            // HM01–HM05
            ["cut"] = SsAnne,
            ["fly"] = Celadon,
            ["surf"] = Mansion,
            ["strength"] = Mansion,
            ["flash"] = MtMoon,
        };

    /// <summary>Applies <see cref="Floors"/> to an already-imported <c>moves.db</c> without a network re-import
    /// (a full import sets it through <c>MoveImport.MapToAttack</c>). Clears every row first, so a name dropped
    /// from the table doesn't leave a stale floor. Returns how many moves got a floor.</summary>
    public static async Task<int> ApplyToDatabaseAsync(MovesDbContext context)
    {
        await context.Moves.ExecuteUpdateAsync(s => s.SetProperty(m => m.MinLevel, (int?)null));
        int applied = 0;
        foreach (var (name, floor) in Floors)
            applied += await context
                .Moves.Where(m => m.Name == name)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.MinLevel, (int?)floor));
        return applied;
    }

    /// <summary>The floor for a move name, or null when it isn't a TM/HM move.</summary>
    public static int? For(string? moveName) =>
        moveName is not null && Floors.TryGetValue(moveName, out var floor) ? floor : null;
}
