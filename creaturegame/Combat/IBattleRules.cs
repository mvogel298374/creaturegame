using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// The kind of secondary effect a move can roll for on hit. Generations differ in how the
/// chance is stored: Gen 1 keeps a single per-move chance (so every kind reads the same
/// value), while later generations can attach independent chances per effect. Call sites
/// ask <see cref="IBattleRules.GetSecondaryEffectChance"/> by kind so they never assume the
/// Gen 1 single-column layout.
/// </summary>
public enum SecondaryEffectKind { Status, Flinch, Confuse }

public interface IBattleRules
{
    /// <summary>
    /// Returns whether the given move thaws a frozen target when it hits.
    /// Gen 1: only damaging Fire-type moves that can inflict burn (not Fire Spin).
    /// Gen 2+: any Fire-type move.
    /// </summary>
    bool CanThawFrozenTarget(Attack move);

    /// <summary>
    /// Per-turn chance (0–100) for a frozen Pokémon to thaw spontaneously at the start of its turn.
    /// Gen 1: 0 (freeze is permanent until hit by the right move).
    /// Gen 2+: 20.
    /// </summary>
    int FreezeRandomThawPercent { get; }

    /// <summary>
    /// Same-Type Attack Bonus multiplier applied when a move's type matches the attacker's.
    /// Gen 1–5: 1.5. (Later mechanics like Adaptability/Terastal layer on top of this base.)
    /// </summary>
    double StabMultiplier { get; }

    /// <summary>
    /// Percent chance (0–100) that a confused creature hits itself instead of acting.
    /// Gen 1–6: 50. Gen 7+: 33.
    /// </summary>
    int ConfusionSelfHitPercent { get; }

    /// <summary>
    /// Number of times a multi-hit move (Double Slap, Comet Punch, Fury Attack…) strikes when it
    /// connects. Gen 1: 2–5 hits, weighted 2 = 3/8, 3 = 3/8, 4 = 1/8, 5 = 1/8. (Gen 5+ reweights to
    /// favour 3 more.) Drawn once per use.
    /// </summary>
    int RollMultiHitCount();

    /// <summary>
    /// Coins-per-level scattered by Pay Day. The money picked up after the battle is this value
    /// times the user's level. Gen 1: 2× level. (Later generations: 5× level.)
    /// </summary>
    int PayDayCoinMultiplier { get; }

    /// <summary>
    /// The percent chance (0–100) a move's secondary <paramref name="effect"/> applies on hit.
    /// Gen 1 stores one chance per move, so every <see cref="SecondaryEffectKind"/> reads the
    /// same column; a later generation that splits chances per effect overrides this to read
    /// the right field. Call sites stay generation-agnostic by asking here rather than reading
    /// the move's chance column directly.
    /// </summary>
    int GetSecondaryEffectChance(Attack move, SecondaryEffectKind effect);

    /// <summary>
    /// Returns the random damage multiplier for one hit.
    /// Gen 1: uniform draw from 217–255, divided by 255.
    /// Gen 2+: uniform draw from 85–100, divided by 100.
    /// </summary>
    double RollDamageVariance();

    /// <summary>
    /// Returns the number of turns the target will sleep (drawn randomly each time Sleep is applied).
    /// Gen 1: 1–7. Gen 2+: 2–5.
    /// </summary>
    int RollSleepTurns();

    /// <summary>
    /// Returns the confusion counter set when a creature becomes confused. Note this is the raw
    /// counter, one higher than the number of self-hit turns because <see cref="StatusResolver"/>
    /// decrements before its cleared-check. Gen 1: 2–5 (≈1–4 turns of confusion).
    /// </summary>
    int RollConfusionTurns();

    /// <summary>
    /// Returns the recoil damage dealt to a Struggle user.
    /// Gen 1: half the damage dealt. Gen 2: half max HP. Gen 3+: quarter max HP.
    /// </summary>
    int CalculateStruggleRecoil(Creature source, int damageDealt);

    /// <summary>
    /// Divisor applied to max HP for end-of-turn Burn damage (e.g. 16 → 1/16 max HP).
    /// Gen 1–5: 16. Gen 6+: 8.
    /// </summary>
    int BurnDamageDenominator { get; }

    /// <summary>
    /// Divisor applied to max HP for end-of-turn Poison damage (e.g. 16 → 1/16 max HP).
    /// Gen 1–5: 16. Gen 6+: 8.
    /// </summary>
    int PoisonDamageDenominator { get; }

    /// <summary>
    /// Returns the fraction of max HP dealt by Bad Poison (Toxic) on the given counter tick.
    /// Gen 1: counter/16 — escalates each turn with no cap (counter starts at 1, increments after damage).
    /// </summary>
    double BadPoisonDamageFraction(int toxicCounter);

    // ── Stat stages ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the damage-stat multiplier for a stage in [-6, +6].
    /// Gen 1/2: stage≤0 → 2/(2+|stage|); stage>0 → (2+stage)/2.
    /// (Ranges from 0.25× at -6 to 4.0× at +6.)
    /// </summary>
    double GetStatMultiplier(int stage);

    /// <summary>
    /// Returns the accuracy/evasion multiplier for a stage in [-6, +6].
    /// Gen 1: stage≤0 → 3/(3+|stage|); stage>0 → (3+stage)/3.
    /// (Ranges from 0.333× at -6 to 3.0× at +6.)
    /// </summary>
    double GetAccuracyStageMultiplier(int stage);

    /// <summary>
    /// Converts a move's accuracy % and the combatants' accuracy/evasion stages
    /// to an internal hit threshold on the [0, AccuracyRollBound) scale.
    /// Roll Random.Next(AccuracyRollBound) and miss if roll >= threshold.
    /// </summary>
    int GetHitThreshold(int accuracyPercent, int attackerAccStage, int defenderEvaStage);

    /// <summary>
    /// Exclusive upper bound for the accuracy roll.
    /// Gen 1: 256 (roll 0–255; a roll of 255 always misses — the 1/256 bug).
    /// Gen 2+: 101 (roll 0–100).
    /// </summary>
    int AccuracyRollBound { get; }

    /// <summary>
    /// Returns the number of turns a binding move traps the target.
    /// Gen 1: 2–5 turns.
    /// </summary>
    int RollBindingTurns();

    /// <summary>
    /// Divisor applied to max HP for end-of-turn binding damage (e.g. 16 → 1/16 max HP).
    /// Gen 1–5: 16.
    /// </summary>
    int BindingDamageDenominator { get; }

    /// <summary>
    /// Returns the crash damage a jump-kick user takes when the move misses.
    /// Gen 1: a flat 1 HP (a famous quirk). Gen 2–4: based on the damage that would have been
    /// dealt; Gen 5+: half the user's max HP — those generations would read <paramref name="user"/>
    /// (and could extend the signature with the would-be damage) rather than returning a constant.
    /// </summary>
    int CalculateCrashDamage(Creature user);

    /// <summary>
    /// Returns the recoil damage a recoil move (Take Down, Double-Edge, Submission) deals back to
    /// the user, given the damage it dealt to the target.
    /// Gen 1: 1/4 of the damage dealt (minimum 1). Gen 2+: same fraction; Gen 7+ rounds differently.
    /// </summary>
    int CalculateRecoilDamage(int damageDealt);

    /// <summary>
    /// Returns how many turns a rampage move (Thrash, Petal Dance) locks the user in before it
    /// confuses itself. Gen 1: 2–3 turns. (Gen 2+: 2–3 as well; Gen 5+ reworks the confusion.)
    /// </summary>
    int RollRampageTurns();

    // ── Stat selection ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the offensive stat value used in damage calculation for the given move type.
    /// Gen 1: Physical → Attack; Special → Special (combined stat).
    /// Gen 2+: Physical → Attack; Special → SpAtk.
    /// </summary>
    int GetOffensiveStat(Creature attacker, AttackType moveType);

    /// <summary>
    /// Returns the defensive stat value used in damage calculation for the given move type.
    /// Gen 1: Physical → Defense; Special → Special (combined stat).
    /// Gen 2+: Physical → Defense; Special → SpDef.
    /// </summary>
    int GetDefensiveStat(Creature defender, AttackType moveType);

    // ── Critical hits ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the critical-hit probability (0.0–1.0) for one attack.
    /// Gen 1 normal:   floor(attacker.BaseSpeed / 2) / 256.
    /// Gen 1 high-crit: min(floor(attacker.BaseSpeed / 2) * 8, 255) / 256.
    /// Gen 1 uses BaseSpeed (unmodified by stages or status).
    /// </summary>
    double GetCritChance(Creature attacker, Attack move);

    /// <summary>
    /// Critical hit damage multiplier. Gen 1–5: 2.0. Gen 6+: 1.5.
    /// </summary>
    double CritMultiplier { get; }

    /// <summary>
    /// Whether crits bypass all stat stage modifiers and the Burn Attack penalty.
    /// True in Gen 1: crits use computed Attack/Defense/Special directly (no stages, no Burn).
    /// False in Gen 2+.
    /// </summary>
    bool CritIgnoresStatStages { get; }

    // ── Experience ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns XP awarded to the winner when an enemy faints.
    /// Gen 1 wild formula: floor(BaseExperience × EnemyLevel / 7).
    /// Gen 5+: gain / party size; trainer battles apply 1.5× modifier.
    /// </summary>
    int CalculateXpAwarded(int baseExp, int enemyLevel);
}
