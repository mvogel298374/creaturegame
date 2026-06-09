using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// A creature's major status carried between encounters in an endless run. Gen 1 keeps major status
/// (Sleep/Poison/Burn/Paralysis/Freeze) on a Pokémon out of battle; volatile conditions (confusion, stat
/// stages, …) do not persist and are never part of this. <see cref="SleepTurns"/> carries the remaining
/// sleep counter so a sleeping creature keeps counting down across the encounter boundary. The generation
/// decides what each status becomes out of battle via <see cref="IBattleRules.CarryStatusOutOfBattle"/>.
/// </summary>
public record CarriedStatus(StatusCondition Status, int SleepTurns);
