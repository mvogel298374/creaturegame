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

    // Rampage (Thrash/Petal Dance): turns left locked into the move; the move auto-repeats while
    // > 0 and the user confuses itself when it reaches 0.
    public int RampageTurnsRemaining { get; set; }
    public PokemonAttack? RampageMove { get; set; }

    // Disable (the move): one of this creature's moves is locked out by the foe. DisabledMove is
    // the exact PokemonAttack instance that can't be selected; DisableTurnsRemaining counts down
    // each end-of-turn and the move is re-enabled when it reaches 0.
    public PokemonAttack? DisabledMove { get; set; }
    public int DisableTurnsRemaining { get; set; }

    // Mist (the move): while set, the opponent cannot lower this creature's stat stages.
    // Gen 1 lasts until the battle ends, so it lives here and clears on the per-battle reset.
    public bool HasMist { get; set; }

    // Rage (the move): once used, the user is locked into Rage (auto-repeats every turn, like a
    // rampage with no turn limit) and gains an Attack stage each time it is hit. RageMove is the
    // exact PokemonAttack instance to keep selecting. Both clear on the per-battle reset.
    public bool IsRaging { get; set; }
    public PokemonAttack? RageMove { get; set; }

    // Counter: the damage this creature last took from a damaging move, and that move's type.
    // Counter returns 2× it when the type was Normal/Fighting (Gen 1). Persists until overwritten
    // by the next hit (a Gen 1 quirk — Counter can hit off a previous turn's damage).
    public int LastDamageTaken { get; set; }
    public DamageType? LastDamageType { get; set; }
}
