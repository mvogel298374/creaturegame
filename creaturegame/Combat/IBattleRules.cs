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
}
