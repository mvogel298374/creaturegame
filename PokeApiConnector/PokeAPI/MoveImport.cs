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
        else if (pokeMove.Name == "metronome")
            attack.Effect = MoveEffect.Metronome;
        // Gen 1 multi-hit (variable 2–5 strikes) — count drawn from the gen rules at runtime.
        else if (pokeMove.Name is "double-slap" or "comet-punch" or "fury-attack"
                              or "pin-missile" or "barrage" or "fury-swipes" or "spike-cannon")
            attack.Effect = MoveEffect.MultiHit;
        // Fixed-count multi-hit — the count is stable move data (always 2), stored on the move.
        // Twineedle strikes twice and carries its own 20% poison secondary (set above from the
        // ailment); the fixed count drives the strike loop while TryApplyStatus handles the poison.
        // Bonemerang joins here in its own coverage batch.
        else if (pokeMove.Name is "double-kick" or "twineedle")
        { attack.Effect = MoveEffect.MultiHit; attack.MultiHitCount = 2; }
        // Gen 1: a missed Jump Kick deals crash damage to the user. Hi Jump Kick joins in its batch.
        else if (pokeMove.Name == "jump-kick")
            attack.Effect = MoveEffect.Crash;
        // Recoil moves — the user takes back a fraction of the damage dealt.
        else if (pokeMove.Name is "take-down" or "double-edge" or "submission")
            attack.Effect = MoveEffect.Recoil;
        // Counter returns twice the (Normal/Fighting) physical damage the user last took (priority −5).
        else if (pokeMove.Name == "counter")
            attack.Effect = MoveEffect.Counter;
        // Rage locks the user into the move and raises its Attack each time it is hit (enforced in
        // the battle engine). PokeAPI's modern ailment data doesn't capture this Gen 1 mechanic.
        else if (pokeMove.Name == "rage")
            attack.Effect = MoveEffect.Rage;
        // Recover / Soft-Boiled restore half the user's max HP (the heal fraction is a battle rule).
        else if (pokeMove.Name is "recover" or "soft-boiled")
            attack.Effect = MoveEffect.Heal;
        // Mimic copies a random move from the target for the rest of the battle (enforced in the engine).
        else if (pokeMove.Name == "mimic")
            attack.Effect = MoveEffect.Mimic;
        // Reflect / Light Screen double the user's Defense / Special vs the matching damage (battle rule).
        else if (pokeMove.Name == "reflect")
            attack.Effect = MoveEffect.Reflect;
        else if (pokeMove.Name == "light-screen")
            attack.Effect = MoveEffect.LightScreen;
        // Focus Energy: Gen 1's bugged crit modifier (applied in Gen1BattleRules.GetCritChance).
        else if (pokeMove.Name == "focus-energy")
            attack.Effect = MoveEffect.FocusEnergy;
        // Bide stores damage for 2–3 turns then unleashes 2× (multi-turn lock-in in the engine).
        else if (pokeMove.Name == "bide")
            attack.Effect = MoveEffect.Bide;
        // Mirror Move re-executes the opponent's last move (engine reads the foe's last-used move).
        else if (pokeMove.Name == "mirror-move")
            attack.Effect = MoveEffect.MirrorMove;
        // Rampage moves — lock the user in for 2–3 turns, then self-confuse. Matched by name BEFORE
        // the confusion-ailment branch so they map to Rampage (the confusion is a self-effect of the
        // lock, not a targeted secondary). Petal Dance joins in its batch.
        else if (pokeMove.Name is "thrash" or "petal-dance")
            attack.Effect = MoveEffect.Rampage;
        else if (pokeMove.Name == "pay-day")
            attack.Effect = MoveEffect.PayDay;
        // Mist shrouds the user so the opponent can't lower its stats (enforced in the battle engine).
        else if (pokeMove.Name == "mist")
            attack.Effect = MoveEffect.Mist;
        // Disable locks one of the target's moves for a few turns; the lock itself is enforced at
        // move-selection time. Matched by name (its ailment "disable" maps to no StatusCondition).
        else if (pokeMove.Name == "disable")
            attack.Effect = MoveEffect.Disable;
        // Confusion isn't a StatusCondition (it's a separate per-battle counter), so it's
        // modelled as a move effect. EffectChance (already set from the move) gates the
        // secondary confusion on damaging moves (Psybeam 10%); pure confusion moves
        // (Supersonic, Confuse Ray) have none ⇒ AttackAction treats null as always-confuse.
        else if (pokeMove.Meta?.Ailment?.Name == "confusion")
            attack.Effect = MoveEffect.Confuse;
        else if (pokeMove.Meta?.FlinchChance > 0)
            attack.Effect = MoveEffect.Flinch;

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