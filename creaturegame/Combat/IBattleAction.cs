using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// A single combatant's action for one turn (currently only <see cref="AttackAction"/>). Battle sorts
/// queued actions by <see cref="Priority"/> then effective Speed, then runs each via
/// <see cref="ExecuteAsync"/>.
/// </summary>
public interface IBattleAction
{
    Creature Source { get; }
    int Priority { get; }
    Task ExecuteAsync();
}
