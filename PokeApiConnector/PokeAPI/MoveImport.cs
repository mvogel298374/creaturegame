using System.Text.Json;
using PokeApiConnector.Generation_1;
using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector.PokeAPI;

public class MoveImport
{
    public static async Task FetchMovesByGeneration(int generation)
    {
        string url = $"https://pokeapi.co/api/v2/generation/{generation}/";
        using HttpClient client = new HttpClient();

        try
        {
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            var genResponse = JsonSerializer.Deserialize<Gen1Response>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (genResponse?.moves != null)
            {
                using var context = new MovesDbContext();
                foreach (var moveResource in genResponse.moves)
                {
                    if (moveResource.url == null) continue;
                    await FetchMoveDataByUrl(moveResource.url, context);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching moves by generation: {ex.Message}");
        }
    }
    
    public static async Task FetchMoveData(int moveId)
    {
        string url = $"https://pokeapi.co/api/v2/move/{moveId}/";
        using var context = new MovesDbContext();
        await FetchMoveDataByUrl(url, context);
    }

    private static async Task FetchMoveDataByUrl(string url, MovesDbContext context)
    {
        using HttpClient client = new HttpClient();
        try
        {
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            PokeApiMove? pokeMove = JsonSerializer.Deserialize<PokeApiMove>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (pokeMove != null)
            {
                Attack attack = MapToAttack(pokeMove);
                
                var existingMove = await context.Moves.AsNoTracking().FirstOrDefaultAsync(m => m.Id == attack.Id);
                if (existingMove == null)
                {
                    context.Moves.Add(attack);
                    Console.WriteLine($"Imported New Move: {attack.Name} (ID: {attack.Id})");
                }
                else
                {
                    context.Moves.Update(attack);
                    Console.WriteLine($"Updated Existing Move: {attack.Name} (ID: {attack.Id})");
                }
                
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching move data from {url}: {ex.Message}");
        }
    }

    private static Attack MapToAttack(PokeApiMove pokeMove)
    {
        Attack attack = new Attack
        {
            Id = pokeMove.Id,
            Name = pokeMove.Name,
            BaseDamage = pokeMove.Power ?? 0,
            Accuracy = pokeMove.Accuracy ?? 100,
            PowerPointsMax = pokeMove.Pp ?? 30,
            Description = pokeMove.EffectEntries?
                .FirstOrDefault(e => e.Language?.Name == "en")?.ShortEffect ?? "No description available.",
            Priority = pokeMove.Priority,
            EffectChance = pokeMove.EffectChance
        };

        if (Enum.TryParse<DamageType>(pokeMove.Type?.Name, true, out var damageType))
        {
            attack.DamageType = damageType;
        }
        else
        {
            attack.DamageType = DamageType.Normal; // Default
        }

        attack.AttackType = pokeMove.DamageClass?.Name?.ToLower() switch
        {
            "physical" => AttackType.Physical,
            "special" => AttackType.Special,
            _ => AttackType.Undefined
        };

        attack.StatusEffect = pokeMove.Meta?.Ailment?.Name switch
        {
            "paralysis" => StatusCondition.Paralysis,
            "sleep"     => StatusCondition.Sleep,
            "burn"      => StatusCondition.Burn,
            "poison"    => StatusCondition.Poison,
            "freeze"    => StatusCondition.Freeze,
            _           => StatusCondition.None
        };

        if (attack.StatusEffect != StatusCondition.None && pokeMove.Meta?.AilmentChance > 0)
            attack.EffectChance = pokeMove.Meta.AilmentChance;

        attack.IsHighCrit = pokeMove.Meta?.CritRate > 0;

        // Damage category — derived from meta.category and specific move IDs
        attack.DamageCategory = pokeMove.Meta?.Category?.Name switch
        {
            "damage-heal"  => DamageCategory.Drain,
            "ohko"         => DamageCategory.OHKO,
            _              => DamageCategory.Standard
        };

        // Moves that PokeAPI doesn't classify via meta.category — identify by ID or name
        if (pokeMove.Id is 120 or 153)              // Self-Destruct, Explosion
            attack.DamageCategory = DamageCategory.SelfDestruct;
        else if (pokeMove.Id is 69 or 101)          // Seismic Toss, Night Shade
            attack.DamageCategory = DamageCategory.LevelBased;
        else if (pokeMove.Id == 162)                // Super Fang
            attack.DamageCategory = DamageCategory.SuperFang;
        else if (pokeMove.Id == 49)                 // Sonic Boom (fixed 20 damage)
        { attack.DamageCategory = DamageCategory.Fixed; attack.FixedDamageValue = 20; }
        else if (pokeMove.Id == 82)                 // Dragon Rage (fixed 40 damage)
        { attack.DamageCategory = DamageCategory.Fixed; attack.FixedDamageValue = 40; }

        // Drain percentage (default 50; Mega Drain / Absorb / Leech Life all drain 50%)
        if (attack.DamageCategory == DamageCategory.Drain && pokeMove.Meta?.Drain > 0)
            attack.DrainPercent = pokeMove.Meta.Drain;

        // Never-miss moves (Swift — bypasses accuracy roll entirely)
        if (pokeMove.Id == 129)
            attack.NeverMisses = true;

        // Stat-stage effect — take the first entry (Gen 1 moves have at most one)
        var statChange = pokeMove.StatChanges?.FirstOrDefault();
        if (statChange?.Stat?.Name != null)
        {
            StageStat? mappedStat = statChange.Stat.Name switch
            {
                "attack"                                    => StageStat.Attack,
                "defense"                                   => StageStat.Defense,
                "special-attack" or "special-defense"
                    or "special"                            => StageStat.Special,
                "speed"                                     => StageStat.Speed,
                "accuracy"                                  => StageStat.Accuracy,
                "evasion" or "evasiveness"                  => StageStat.Evasion,
                _                                           => null
            };
            if (mappedStat.HasValue)
            {
                attack.StatEffectStat   = mappedStat;
                attack.StatEffectDelta  = statChange.Change;
                attack.StatEffectTarget = pokeMove.Target?.Name == "user"
                    ? StageTarget.Self : StageTarget.Foe;
                // Pure stat moves always succeed; secondary effects on damaging moves use EffectChance
                attack.StatEffectChance = attack.BaseDamage > 0
                    ? (pokeMove.EffectChance ?? 100) : 100;
            }
        }

        // Special move effects
        if (pokeMove.Name == "haze")
            attack.Effect = MoveEffect.Haze;
        else if (pokeMove.Name == "leech-seed")
            attack.Effect = MoveEffect.LeechSeed;
        else if (pokeMove.Name is "hyper-beam")
            attack.Effect = MoveEffect.Recharge;
        else if (pokeMove.Name is "wrap" or "bind" or "clamp" or "fire-spin")
            attack.Effect = MoveEffect.Binding;
        else if (pokeMove.Name is "fly" or "dig" or "solar-beam" or "razor-wind" or "sky-attack")
            attack.Effect = MoveEffect.TwoTurn;
        else if (pokeMove.Meta?.FlinchChance > 0)
            attack.Effect = MoveEffect.Flinch;

        return attack;
    }
}