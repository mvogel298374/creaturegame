using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

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
}
