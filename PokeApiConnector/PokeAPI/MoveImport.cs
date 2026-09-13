using System.Text.Json;
using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.DB;
using Microsoft.EntityFrameworkCore;
using PokeApiConnector.Generation_1;

namespace PokeApiConnector.PokeAPI;

public class MoveImport
{
    public static async Task FetchMovesByGeneration(int generation)
    {
        string url = $"https://pokeapi.co/api/v2/generation/{generation}/";

        try
        {
            HttpResponseMessage response = await PokeApiHttp.Client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            var genResponse = JsonSerializer.Deserialize<Gen1Response>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (genResponse?.moves != null)
            {
                using var context = new MovesDbContext();
                foreach (var moveResource in genResponse.moves)
                {
                    if (moveResource.url == null)
                        continue;
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
        try
        {
            HttpResponseMessage response = await PokeApiHttp.Client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            PokeApiMove? pokeMove = JsonSerializer.Deserialize<PokeApiMove>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (pokeMove != null)
            {
                Attack attack = MapToAttack(pokeMove);

                var existingMove = await context
                    .Moves.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == attack.Id);
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

    private const int SelfDestructId = 120;
    private const int ExplosionId = 153;
    private const int SeismicTossId = 69;
    private const int NightShadeId = 101;
    private const int SuperFangId = 162;
    private const int PsywaveId = 149;
    private const int SonicBoomId = 49;
    private const int DragonRageId = 82;
    private const int SwiftId = 129; // never-miss

    /// <summary>Maps a PokeAPI move to a Gen-1-correct <see cref="Attack"/> (DATA_IMPORT.md §4.1). Order
    /// matters — corrections run last so they win over the mappings before them.</summary>
    public static Attack MapToAttack(PokeApiMove pokeMove)
    {
        Attack attack = BuildGen1Attack(pokeMove);
        ApplyDamageCategory(attack, pokeMove);
        ApplyStatStageEffect(attack, pokeMove);
        ApplySpecialEffects(attack, pokeMove);
        ApplyGen1Corrections(attack, pokeMove);
        return attack;
    }

    /// <summary>Base stats + type/category resolved from <c>past_values</c> (DATA_IMPORT.md §4.1). One
    /// row per move today; a future generation adds a row per (move, generation) — TODO.md →
    /// Multi-Generation.</summary>
    private static Attack BuildGen1Attack(PokeApiMove pokeMove)
    {
        var pasts = pokeMove.PastValues ?? new List<MovePastValue>();
        int gen1Power =
            pasts.Select(p => p.Power).FirstOrDefault(v => v != null) ?? pokeMove.Power ?? 0;
        int gen1Accuracy =
            pasts.Select(p => p.Accuracy).FirstOrDefault(v => v != null)
            ?? pokeMove.Accuracy
            ?? 100;
        int gen1Pp = pasts.Select(p => p.Pp).FirstOrDefault(v => v != null) ?? pokeMove.Pp ?? 30;
        int? gen1EffChance =
            pasts.Select(p => p.EffectChance).FirstOrDefault(v => v != null)
            ?? pokeMove.EffectChance;
        string? gen1TypeName =
            pasts.Select(p => p.Type?.Name).FirstOrDefault(v => v != null) ?? pokeMove.Type?.Name;

        Attack attack = new Attack
        {
            Id = pokeMove.Id,
            Name = pokeMove.Name,
            BaseDamage = gen1Power,
            Accuracy = gen1Accuracy,
            PowerPointsMax = gen1Pp,
            Description =
                pokeMove.EffectEntries?.FirstOrDefault(e => e.Language?.Name == "en")?.ShortEffect
                ?? "No description available.",
            Priority = pokeMove.Priority,
            EffectChance = gen1EffChance,
        };

        if (Enum.TryParse<DamageType>(gen1TypeName, true, out var damageType))
        {
            attack.DamageType = damageType;
        }
        else
        {
            attack.DamageType = DamageType.Normal;
        }

        // Gen 1: category derives from the move's type, not PokeAPI's damage_class (DATA_IMPORT.md §4.1).
        attack.AttackType = pokeMove.DamageClass?.Name?.ToLower() switch
        {
            "physical" or "special" => Gen1DamageCategory(attack.DamageType),
            _ => AttackType.Undefined,
        };

        // Toxic → BadPoison is restored in the layer-2 corrections below; PokeAPI reports it as plain
        // "poison" here.
        attack.StatusEffect = pokeMove.Meta?.Ailment?.Name switch
        {
            "paralysis" => StatusCondition.Paralysis,
            "sleep" => StatusCondition.Sleep,
            "burn" => StatusCondition.Burn,
            "poison" => StatusCondition.Poison,
            "freeze" => StatusCondition.Freeze,
            _ => StatusCondition.None,
        };

        if (attack.StatusEffect != StatusCondition.None && pokeMove.Meta?.AilmentChance > 0)
            attack.EffectChance = pokeMove.Meta.AilmentChance;

        attack.IsHighCrit = pokeMove.Meta?.CritRate > 0;

        return attack;
    }

    /// <summary>Damage category, drain %, and never-miss (DATA_IMPORT.md §4.1).</summary>
    private static void ApplyDamageCategory(Attack attack, PokeApiMove pokeMove)
    {
        attack.DamageCategory = pokeMove.Meta?.Category?.Name switch
        {
            "damage-heal" => DamageCategory.Drain,
            "ohko" => DamageCategory.OHKO,
            _ => DamageCategory.Standard,
        };

        if (pokeMove.Id is SelfDestructId or ExplosionId)
            attack.DamageCategory = DamageCategory.SelfDestruct;
        else if (pokeMove.Id is SeismicTossId or NightShadeId)
            attack.DamageCategory = DamageCategory.LevelBased;
        else if (pokeMove.Id == SuperFangId)
            attack.DamageCategory = DamageCategory.SuperFang;
        else if (pokeMove.Id == PsywaveId)
            attack.DamageCategory = DamageCategory.Psywave;
        else if (pokeMove.Id == SonicBoomId)
        {
            attack.DamageCategory = DamageCategory.Fixed;
            attack.FixedDamageValue = 20;
        }
        else if (pokeMove.Id == DragonRageId)
        {
            attack.DamageCategory = DamageCategory.Fixed;
            attack.FixedDamageValue = 40;
        }

        if (attack.DamageCategory == DamageCategory.Drain && pokeMove.Meta?.Drain > 0)
            attack.DrainPercent = pokeMove.Meta.Drain;

        if (pokeMove.Id == SwiftId)
            attack.NeverMisses = true;
    }

    /// <summary>The move's stat-stage effect (Gen 1 moves carry at most one).</summary>
    private static void ApplyStatStageEffect(Attack attack, PokeApiMove pokeMove)
    {
        var statChange = pokeMove.StatChanges?.FirstOrDefault();
        if (statChange?.Stat?.Name != null)
        {
            StageStat? mappedStat = statChange.Stat.Name switch
            {
                "attack" => StageStat.Attack,
                "defense" => StageStat.Defense,
                "special-attack" or "special-defense" or "special" => StageStat.Special,
                "speed" => StageStat.Speed,
                "accuracy" => StageStat.Accuracy,
                "evasion" or "evasiveness" => StageStat.Evasion,
                _ => null,
            };
            if (mappedStat.HasValue)
            {
                attack.StatEffectStat = mappedStat;
                attack.StatEffectDelta = statChange.Change;
                attack.StatEffectTarget =
                    pokeMove.Target?.Name == "user" ? StageTarget.Self : StageTarget.Foe;
                attack.StatEffectChance =
                    attack.BaseDamage > 0 ? (attack.EffectChance ?? 100) : 100;
            }
        }
    }

    /// <summary>Special move effects — name lookup (DATA_IMPORT.md §4.1 catalog), then the
    /// confusion/flinch fallbacks. Name lookup wins so Thrash/Petal Dance map to Rampage.</summary>
    private static void ApplySpecialEffects(Attack attack, PokeApiMove pokeMove)
    {
        if (
            pokeMove.Name is { } moveName
            && Gen1MoveEffects.TryGetValue(moveName, out var namedEffect)
        )
            attack.Effect = namedEffect;
        else if (pokeMove.Meta?.Ailment?.Name == "confusion")
            attack.Effect = MoveEffect.Confuse;
        else if (pokeMove.Meta?.FlinchChance > 0)
            attack.Effect = MoveEffect.Flinch;

        if (pokeMove.Name is "double-kick" or "twineedle" or "bonemerang")
            attack.MultiHitCount = 2;
    }

    /// <summary>Layer-2 hand-verified Gen 1 corrections PokeAPI can't express (DATA_IMPORT.md §4.1/§5.5).
    /// Runs last so it wins over the mapping above.</summary>
    private static void ApplyGen1Corrections(Attack attack, PokeApiMove pokeMove)
    {
        switch (pokeMove.Name)
        {
            case "acid": // Gen 1: 33% to lower Defense (modern: 10% Sp. Def; past_values empty)
                attack.StatEffectStat = StageStat.Defense;
                attack.StatEffectDelta = -1;
                attack.StatEffectTarget = StageTarget.Foe;
                attack.StatEffectChance = 33;
                attack.EffectChance = 33;
                break;
            case "aurora-beam": // Gen 1: 33% to lower Attack (modern: 10%)
            case "bubble-beam": // Gen 1: 33% to lower Speed (modern: 10%)
            case "bubble": // Gen 1: 33% to lower Speed (modern: 10%; past_values lacks the chance)
            case "constrict": // Gen 1: 33% to lower Speed (modern: 10%)
                attack.StatEffectChance = 33;
                attack.EffectChance = 33;
                break;
            case "bite": // Gen 1: 10% flinch (modern: 30%)
                attack.EffectChance = 10;
                break;
            case "low-kick": // Gen 1: 30% flinch (modern: weight-based power, no flinch)
                attack.Effect = MoveEffect.Flinch;
                attack.EffectChance = 30;
                break;
            case "poison-sting": // Gen 1: 20% poison (modern: 30%)
                attack.EffectChance = 20;
                break;
            case "fire-blast": // Gen 1: 30% burn (modern: 10%)
                attack.EffectChance = 30;
                break;
            case "waterfall": // Gen 1–3: no secondary effect; the 20% flinch was added in Gen 4
            case "dizzy-punch": // Gen 1: no secondary effect; the 20% confusion was added in Gen 5
            case "rock-slide": // Gen 1: no secondary effect; the 30% flinch was added in Gen 2
            case "tri-attack": // Gen 1: no secondary effect; the 20% random burn/freeze/para is Gen 2+
                attack.Effect = MoveEffect.None;
                attack.EffectChance = null;
                break;
            case "sky-attack": // Gen 1–2: plain two-turn charge (mapped to TwoTurn above). The 30%
                // flinch was added in Gen 3; clear the stale, inert chance PokeAPI reports.
                attack.EffectChance = null;
                break;
            case "skull-bash": // Gen 1: plain two-turn charge (mapped to TwoTurn above). PokeAPI
                // carries effect_chance=100 for the Gen 2+ charge-turn Defense boost,
                // which doesn't exist in Gen 1 — clear the stale, inert chance.
                attack.EffectChance = null;
                break;
            case "growth": // Gen 1: raises Special +1 (modern: +1 Attack & +1 Sp. Atk)
                attack.StatEffectStat = StageStat.Special;
                attack.StatEffectDelta = 1;
                attack.StatEffectTarget = StageTarget.Self;
                attack.StatEffectChance = 100;
                break;
            case "string-shot": // Gen 1–5: lowers Speed by 1 (modern: by 2)
                attack.StatEffectDelta = -1;
                break;
            case "thunder": // Gen 1: 10% paralysis (modern: 30%)
                attack.EffectChance = 10;
                break;
            case "toxic": // Toxic badly-poisons; PokeAPI reports its ailment as plain "poison",
                // so promote it to BadPoison (escalating damage via ToxicCounter).
                attack.StatusEffect = StatusCondition.BadPoison;
                break;
        }
    }

    // Gen 1 special move mechanics keyed by PokeAPI move name — full catalog in DATA_IMPORT.md §4.1.
    private static readonly Dictionary<string, MoveEffect> Gen1MoveEffects = new()
    {
        ["haze"] = MoveEffect.Haze,
        ["leech-seed"] = MoveEffect.LeechSeed,
        ["hyper-beam"] = MoveEffect.Recharge,
        ["wrap"] = MoveEffect.Binding,
        ["bind"] = MoveEffect.Binding,
        ["clamp"] = MoveEffect.Binding,
        ["fire-spin"] = MoveEffect.Binding,
        ["fly"] = MoveEffect.TwoTurn,
        ["dig"] = MoveEffect.TwoTurn,
        ["solar-beam"] = MoveEffect.TwoTurn,
        ["razor-wind"] = MoveEffect.TwoTurn,
        ["sky-attack"] = MoveEffect.TwoTurn,
        ["skull-bash"] = MoveEffect.TwoTurn,
        ["metronome"] = MoveEffect.Metronome,
        ["double-slap"] = MoveEffect.MultiHit,
        ["comet-punch"] = MoveEffect.MultiHit,
        ["fury-attack"] = MoveEffect.MultiHit,
        ["pin-missile"] = MoveEffect.MultiHit,
        ["barrage"] = MoveEffect.MultiHit,
        ["fury-swipes"] = MoveEffect.MultiHit,
        ["spike-cannon"] = MoveEffect.MultiHit,
        ["double-kick"] = MoveEffect.MultiHit, // fixed ×2 — MultiHitCount set in MapToAttack
        ["twineedle"] = MoveEffect.MultiHit, // fixed ×2 — MultiHitCount set in MapToAttack
        ["jump-kick"] = MoveEffect.Crash,
        ["high-jump-kick"] = MoveEffect.Crash,
        ["take-down"] = MoveEffect.Recoil,
        ["double-edge"] = MoveEffect.Recoil,
        ["submission"] = MoveEffect.Recoil,
        ["counter"] = MoveEffect.Counter,
        ["rage"] = MoveEffect.Rage,
        ["recover"] = MoveEffect.Heal,
        ["soft-boiled"] = MoveEffect.Heal,
        ["mimic"] = MoveEffect.Mimic,
        ["reflect"] = MoveEffect.Reflect,
        ["light-screen"] = MoveEffect.LightScreen,
        ["focus-energy"] = MoveEffect.FocusEnergy,
        ["bide"] = MoveEffect.Bide,
        ["mirror-move"] = MoveEffect.MirrorMove,
        ["thrash"] = MoveEffect.Rampage,
        ["petal-dance"] = MoveEffect.Rampage,
        ["pay-day"] = MoveEffect.PayDay,
        ["mist"] = MoveEffect.Mist,
        ["disable"] = MoveEffect.Disable,
        ["dream-eater"] = MoveEffect.DreamEater,
        ["splash"] = MoveEffect.Splash,
        ["rest"] = MoveEffect.Rest,
        ["bonemerang"] = MoveEffect.MultiHit, // fixed ×2 — MultiHitCount set in MapToAttack
        ["substitute"] = MoveEffect.Substitute,
        ["transform"] = MoveEffect.Transform,
        ["conversion"] = MoveEffect.Conversion,
        ["roar"] = MoveEffect.ForceFlee,
        ["whirlwind"] = MoveEffect.ForceFlee,
    };

    // DATA_IMPORT.md §4.1 — Gen 1's physical/special split by type.
    private static readonly HashSet<DamageType> Gen1PhysicalTypes =
    [
        DamageType.Normal,
        DamageType.Fighting,
        DamageType.Flying,
        DamageType.Poison,
        DamageType.Ground,
        DamageType.Rock,
        DamageType.Bug,
        DamageType.Ghost,
    ];

    private static AttackType Gen1DamageCategory(DamageType type) =>
        Gen1PhysicalTypes.Contains(type) ? AttackType.Physical : AttackType.Special;
}
