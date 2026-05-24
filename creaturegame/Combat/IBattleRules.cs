using creaturegame.Attacks;

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
}
