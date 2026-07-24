using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// A creature's major status carried between encounters in an endless run. Gen 1 keeps major status
/// (Sleep/Poison/Burn/Paralysis/Freeze) on a Pokémon out of battle; volatile conditions (confusion, stat
/// stages, …) do not persist and are never part of this. <see cref="SleepTurns"/> carries the remaining
/// sleep counter so a sleeping creature keeps counting down across the encounter boundary. The generation
/// decides what each status becomes out of battle via <see cref="IBattleRules.CarryStatusOutOfBattle"/>.
/// </summary>
public record CarriedStatus(StatusCondition Status, int SleepTurns)
{
    /// <summary>
    /// Captures <paramref name="creature"/>'s current major status as it leaves the field, in its out-of-battle
    /// form (the generation decides — Gen 1 reverts Toxic → Poison via <paramref name="rules"/>). The sleep
    /// counter carries so a sleeping creature keeps counting down; every other status carries a 0 counter. Returns
    /// <c>null</c> when the creature is statusless. Volatiles (confusion, stat stages, …) live only in
    /// <c>BattleState</c> and are deliberately never captured. The single capture rule shared by the run loop
    /// (post-battle, per member) and <see cref="Battle"/> (a voluntary switch-out of a still-alive creature).
    /// </summary>
    public static CarriedStatus? Capture(IBattleRules rules, Creature creature)
    {
        var status = rules.CarryStatusOutOfBattle(creature.Battle.Status);
        return status == StatusCondition.None
            ? null
            : new CarriedStatus(
                status,
                status == StatusCondition.Sleep ? creature.Battle.SleepTurns : 0
            );
    }
}
