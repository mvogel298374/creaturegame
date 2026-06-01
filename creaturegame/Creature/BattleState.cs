using creaturegame.Attacks;

namespace creaturegame.Creatures;

/// <summary>
/// Transient per-battle state for a <see cref="Creature"/> — everything that must be
/// cleared between fights. Held as <see cref="Creature.Battle"/> and reset by assigning
/// a fresh instance, so a newly added field can never be missed by a manual reset (the
/// shape of a real past bug). The permanent half of a Creature — identity, level, base
/// stats, DVs, Stat Exp, XP — lives on Creature itself and is what a save system persists;
/// this object is the half that is thrown away.
/// </summary>
public sealed class BattleState
{
    public StatusCondition Status { get; set; } = StatusCondition.None;
    public int SleepTurns { get; set; }
    public int ConfusedTurns { get; set; }

    // Gen 1 Toxic counter starts at 1 and escalates each turn; 1 is the reset baseline.
    public int ToxicCounter { get; set; } = 1;

    // Stat stages, [-6, +6] per stat.
    public StatStages Stages { get; set; } = new StatStages();

    public bool IsRecharging { get; set; }
    public bool IsFlinched { get; set; }
    public bool HasLeechSeed { get; set; }
    public int BindingTurnsRemaining { get; set; }
    public bool IsTwoTurnCharging { get; set; }
    public PokemonAttack? ChargingMove { get; set; }
}
