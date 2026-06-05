using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// A read-only snapshot of the relevant battle state for one turn's move decision.
/// Passed to <see cref="IBattleInput.ChooseMoveAsync"/> so any input source —
/// player UI, AI, network — has everything it needs without reaching into Battle.
///
/// Add new properties here as battle dimensions grow (weather, field effects, team
/// state, turn number) without changing the IBattleInput or IMoveEvaluator contracts.
/// </summary>
public sealed class TurnContext
{
    public required Creature Attacker { get; init; }
    public required Creature Defender { get; init; }
    public required ITypeChart TypeChart { get; init; }
    public required IBattleRules Rules { get; init; }
    public int TurnNumber { get; init; }

    /// <summary>
    /// The attacker's move currently locked out by Disable, if any. Input sources must not
    /// select it; it is excluded from the candidate moves. Null when nothing is disabled.
    /// </summary>
    public PokemonAttack? DisabledMove { get; init; }
}
