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
        // PokeAPI returns each move's MODERN stats; Gen 1 often differed (special moves were
        // stronger, Blizzard was 90% accurate, and several moves were a different type — e.g.
        // Bite/Gust/Karate Chop/Sand Attack were Normal). PokeAPI records the history in
        // `past_values`: each entry's value was in effect in every generation *before* its
        // version_group, so the EARLIEST recorded value is the Gen 1 one. We resolve power /
        // accuracy / pp / effect_chance / type from it here so every downstream decision (STAB,
        // type chart, the type-derived physical/special split, damage) is Gen-1-correct. This is
        // the single, data-driven source for Gen 1 move data — no per-move hardcoded corrections.
        // (A future multi-gen importer would store one row per generation; today it's Gen 1 only.)
        var pasts = pokeMove.PastValues ?? new List<MovePastValue>();
        int     gen1Power     = pasts.Select(p => p.Power).FirstOrDefault(v => v != null) ?? pokeMove.Power ?? 0;
        int     gen1Accuracy  = pasts.Select(p => p.Accuracy).FirstOrDefault(v => v != null) ?? pokeMove.Accuracy ?? 100;
        int     gen1Pp        = pasts.Select(p => p.Pp).FirstOrDefault(v => v != null) ?? pokeMove.Pp ?? 30;
        int?    gen1EffChance = pasts.Select(p => p.EffectChance).FirstOrDefault(v => v != null) ?? pokeMove.EffectChance;
        string? gen1TypeName  = pasts.Select(p => p.Type?.Name).FirstOrDefault(v => v != null) ?? pokeMove.Type?.Name;

        Attack attack = new Attack
        {
            Id = pokeMove.Id,
            Name = pokeMove.Name,
            BaseDamage = gen1Power,
            Accuracy = gen1Accuracy,
            PowerPointsMax = gen1Pp,
            Description = pokeMove.EffectEntries?
                .FirstOrDefault(e => e.Language?.Name == "en")?.ShortEffect ?? "No description available.",
            Priority = pokeMove.Priority,
            EffectChance = gen1EffChance
        };

        if (Enum.TryParse<DamageType>(gen1TypeName, true, out var damageType))
        {
            attack.DamageType = damageType;
        }
        else
        {
            attack.DamageType = DamageType.Normal; // Default
        }

        // Gen 1: a damaging move's physical/special split is decided by its TYPE, not the
        // move. PokeAPI's damage_class is the Gen 4+ per-move split (e.g. it calls Fire
        // Punch "physical" and Hyper Beam "special"), which is wrong for Gen 1 — so for any
        // move that deals damage we derive the category from DamageType. Status moves
        // (damage_class "status") stay Undefined.
        attack.AttackType = pokeMove.DamageClass?.Name?.ToLower() switch
        {
            "physical" or "special" => Gen1DamageCategory(attack.DamageType),
            _                       => AttackType.Undefined
        };

        // PokeAPI reports a target's *status condition* here, not the move's special mechanic — so
        // Toxic comes through as plain "poison" (the badly-poison escalation is a move effect, not a
        // distinct ailment). Toxic → BadPoison is restored in the layer-2 override block below; no Gen 1
        // move emits a "bad-poison" ailment, so there's no arm for it here.
        attack.StatusEffect = pokeMove.Meta?.Ailment?.Name switch
        {
            "paralysis"  => StatusCondition.Paralysis,
            "sleep"      => StatusCondition.Sleep,
            "burn"       => StatusCondition.Burn,
            "poison"     => StatusCondition.Poison,
            "freeze"     => StatusCondition.Freeze,
            _            => StatusCondition.None
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
                // Pure stat moves always succeed; secondary effects on damaging moves use the
                // (Gen-1-resolved) effect chance.
                attack.StatEffectChance = attack.BaseDamage > 0
                    ? (attack.EffectChance ?? 100) : 100;
            }
        }

        // Special move effects. Most are a fixed name → MoveEffect mapping (see Gen1MoveEffects); the
        // two meta-based fallbacks below only apply when no name matched. That ordering preserves the
        // Gen 1 rule that a rampage move (Thrash / Petal Dance) maps to Rampage rather than to its
        // self-confusion ailment — the name lookup wins over the confusion-ailment fallback.
        if (pokeMove.Name is { } moveName && Gen1MoveEffects.TryGetValue(moveName, out var namedEffect))
            attack.Effect = namedEffect;
        // Confusion isn't a StatusCondition (it's a separate per-battle counter), so it's modelled as a
        // move effect. EffectChance (already set) gates the secondary confusion on damaging moves
        // (Psybeam 10%); pure confusion moves (Supersonic, Confuse Ray) have none ⇒ AttackAction
        // treats null as always-confuse.
        else if (pokeMove.Meta?.Ailment?.Name == "confusion")
            attack.Effect = MoveEffect.Confuse;
        else if (pokeMove.Meta?.FlinchChance > 0)
            attack.Effect = MoveEffect.Flinch;

        // Fixed-count multi-hit — the strike count is stable move data (always 2), not a gen rule, so
        // it rides alongside the MoveEffect.MultiHit set via the map above. Twineedle also carries its
        // own 20% poison secondary (set from the ailment); Bonemerang joins here in its coverage batch.
        if (pokeMove.Name is "double-kick" or "twineedle")
            attack.MultiHitCount = 2;

        // ── Gen 1 secondary-effect corrections (layer 2: facts PokeAPI can't express) ──────────
        // PokeAPI reports each move's MODERN secondary chance/target and almost never backfills
        // `past_values` for them, so these are applied here from an authority (Bulbapedia). Keep the
        // list short, verified, and commented — see DATA_IMPORT.md §4.1/§5.5. Runs last so it wins
        // over the stat-change and effect mapping above.
        switch (pokeMove.Name)
        {
            case "acid":         // Gen 1: 33% to lower Defense (modern: 10% Sp. Def; past_values empty)
                attack.StatEffectStat   = StageStat.Defense;
                attack.StatEffectDelta  = -1;
                attack.StatEffectTarget = StageTarget.Foe;
                attack.StatEffectChance = 33;
                attack.EffectChance     = 33;
                break;
            case "aurora-beam":  // Gen 1: 33% to lower Attack (modern: 10%)
            case "bubble-beam":  // Gen 1: 33% to lower Speed (modern: 10%)
                attack.StatEffectChance = 33;
                attack.EffectChance     = 33;
                break;
            case "bite":         // Gen 1: 10% flinch (modern: 30%)
                attack.EffectChance     = 10;
                break;
            case "low-kick":     // Gen 1: 30% flinch (modern: weight-based power, no flinch)
                attack.Effect           = MoveEffect.Flinch;
                attack.EffectChance     = 30;
                break;
            case "poison-sting": // Gen 1: 20% poison (modern: 30%)
                attack.EffectChance     = 20;
                break;
            case "fire-blast":   // Gen 1: 30% burn (modern: 10%)
                attack.EffectChance     = 30;
                break;
            case "waterfall":    // Gen 1–3: no secondary effect; the 20% flinch was added in Gen 4
                attack.Effect           = MoveEffect.None;
                attack.EffectChance     = null;
                break;
            case "skull-bash":   // Gen 1: plain two-turn charge (mapped to TwoTurn above). PokeAPI
                                 // carries effect_chance=100 for the Gen 2+ charge-turn Defense boost,
                                 // which doesn't exist in Gen 1 — clear the stale, inert chance.
                attack.EffectChance     = null;
                break;
            case "growth":       // Gen 1: raises Special +1 (modern: +1 Attack & +1 Sp. Atk)
                attack.StatEffectStat   = StageStat.Special;
                attack.StatEffectDelta  = 1;
                attack.StatEffectTarget = StageTarget.Self;
                attack.StatEffectChance = 100;
                break;
            case "string-shot":  // Gen 1–5: lowers Speed by 1 (modern: by 2)
                attack.StatEffectDelta  = -1;
                break;
            case "thunder":      // Gen 1: 10% paralysis (modern: 30%)
                attack.EffectChance     = 10;
                break;
            case "toxic":        // Toxic badly-poisons; PokeAPI reports its ailment as plain "poison",
                                 // so promote it to BadPoison (escalating damage via ToxicCounter).
                attack.StatusEffect     = StatusCondition.BadPoison;
                break;
        }

        return attack;
    }

    // Gen 1 special move mechanics keyed by PokeAPI move name. These are behaviours PokeAPI's
    // meta/ailment data can't express, so they're mapped explicitly here (effects derivable from
    // data — secondary status, stat changes — stay inline in MapToAttack). A few entries need extra
    // per-move data beyond the effect (e.g. fixed multi-hit count); that's set in MapToAttack.
    private static readonly Dictionary<string, MoveEffect> Gen1MoveEffects = new()
    {
        ["haze"]       = MoveEffect.Haze,
        ["leech-seed"] = MoveEffect.LeechSeed,
        ["hyper-beam"] = MoveEffect.Recharge,
        // Binding — damages + traps the target for 2–5 turns.
        ["wrap"] = MoveEffect.Binding, ["bind"] = MoveEffect.Binding,
        ["clamp"] = MoveEffect.Binding, ["fire-spin"] = MoveEffect.Binding,
        // Two-turn charge moves. Gen 1 Skull Bash is a plain charge — it does NOT raise Defense on the
        // charge turn (that boost was added in Gen 2), so it maps to plain TwoTurn like the others.
        ["fly"] = MoveEffect.TwoTurn, ["dig"] = MoveEffect.TwoTurn, ["solar-beam"] = MoveEffect.TwoTurn,
        ["razor-wind"] = MoveEffect.TwoTurn, ["sky-attack"] = MoveEffect.TwoTurn,
        ["skull-bash"] = MoveEffect.TwoTurn,
        ["metronome"] = MoveEffect.Metronome,
        // Variable multi-hit (2–5 strikes; count drawn from the gen rules at runtime).
        ["double-slap"] = MoveEffect.MultiHit, ["comet-punch"] = MoveEffect.MultiHit,
        ["fury-attack"] = MoveEffect.MultiHit, ["pin-missile"] = MoveEffect.MultiHit,
        ["barrage"] = MoveEffect.MultiHit, ["fury-swipes"] = MoveEffect.MultiHit,
        ["spike-cannon"] = MoveEffect.MultiHit,
        // Fixed-count multi-hit (always 2 — MultiHitCount is set in MapToAttack).
        ["double-kick"] = MoveEffect.MultiHit, ["twineedle"] = MoveEffect.MultiHit,
        // Gen 1: a missed Jump Kick deals crash damage to the user. Hi Jump Kick joins in its batch.
        ["jump-kick"] = MoveEffect.Crash,
        // Recoil — the user takes back a fraction of the damage dealt.
        ["take-down"] = MoveEffect.Recoil, ["double-edge"] = MoveEffect.Recoil,
        ["submission"] = MoveEffect.Recoil,
        // Counter returns 2× the (Normal/Fighting) physical damage the user last took (priority −5).
        ["counter"] = MoveEffect.Counter,
        // Rage locks the user in and raises Attack each time it is hit (enforced in the engine).
        ["rage"] = MoveEffect.Rage,
        // Recover / Soft-Boiled restore half the user's max HP (the heal fraction is a battle rule).
        ["recover"] = MoveEffect.Heal, ["soft-boiled"] = MoveEffect.Heal,
        // Mimic copies a random move from the target for the rest of the battle.
        ["mimic"] = MoveEffect.Mimic,
        // Reflect / Light Screen double the user's Defense / Special vs the matching damage.
        ["reflect"] = MoveEffect.Reflect, ["light-screen"] = MoveEffect.LightScreen,
        // Focus Energy: Gen 1's bugged crit modifier (applied in Gen1BattleRules.GetCritChance).
        ["focus-energy"] = MoveEffect.FocusEnergy,
        // Bide stores damage for 2–3 turns then unleashes 2× (multi-turn lock-in in the engine).
        ["bide"] = MoveEffect.Bide,
        // Mirror Move re-executes the opponent's last move (engine reads the foe's last-used move).
        ["mirror-move"] = MoveEffect.MirrorMove,
        // Rampage — lock in for 2–3 turns, then self-confuse. Mapped here so the name lookup wins over
        // the confusion-ailment fallback in MapToAttack. Petal Dance joins in its batch.
        ["thrash"] = MoveEffect.Rampage, ["petal-dance"] = MoveEffect.Rampage,
        ["pay-day"] = MoveEffect.PayDay,
        // Mist shrouds the user so the opponent can't lower its stats.
        ["mist"] = MoveEffect.Mist,
        // Disable locks one of the target's moves (enforced at move-selection time).
        ["disable"] = MoveEffect.Disable,
    };

    // Gen 1 physical types: Normal, Fighting, Flying, Poison, Ground, Rock, Bug, Ghost.
    // Every other (damaging) type — Fire, Water, Grass, Electric, Psychic, Ice, Dragon —
    // is Special. (Steel/Dark/Fairy don't exist in Gen 1.)
    private static readonly HashSet<DamageType> Gen1PhysicalTypes =
    [
        DamageType.Normal, DamageType.Fighting, DamageType.Flying, DamageType.Poison,
        DamageType.Ground, DamageType.Rock, DamageType.Bug, DamageType.Ghost,
    ];

    private static AttackType Gen1DamageCategory(DamageType type) =>
        Gen1PhysicalTypes.Contains(type) ? AttackType.Physical : AttackType.Special;
}