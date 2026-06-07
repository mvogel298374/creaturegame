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

    // Reflect / Light Screen: while up, the holder's Defense / Special is doubled when taking
    // physical / special damage (crits ignore it). Gen 1 lasts until the battle ends.
    public bool HasReflect { get; set; }
    public bool HasLightScreen { get; set; }

    // Focus Energy: set while the user is "focused" (Gen 1 quarters its crit rate — the famous bug).
    public bool HasFocusEnergy { get; set; }

    // Bide: turns left committed (storing then releasing); the damage absorbed while committed; and
    // the move to auto-repeat while locked in (mirrors the rampage lock pattern).
    public int BideTurnsRemaining { get; set; }
    public int BideDamageAccumulated { get; set; }
    public PokemonAttack? BideMove { get; set; }

    // The last move this creature actually used — Mirror Move copies the opponent's.
    public Attack? LastMoveUsed { get; set; }

    // Mimic (the move): when used, the Mimic slot's Base is swapped to a copied foe move for the rest
    // of the battle. MimicWrapper is the slot whose Base was swapped and MimicOriginalBase is the move
    // to put back; Battle restores it at battle end so the swap never leaks into the permanent MoveSet.
    public PokemonAttack? MimicWrapper { get; set; }
    public Attack? MimicOriginalBase { get; set; }

    // Substitute (the move): while > 0, this creature has a decoy that soaks incoming damage from the
    // foe (Gen 1: the user takes nothing until the substitute breaks; overflow damage is lost). Created
    // for floor(maxHP/4)+1 HP at a cost of floor(maxHP/4) of the user's own HP. Clears on battle reset.
    public int SubstituteHp { get; set; }

    // Counter: the damage this creature last took from a damaging move, and that move's type.
    // Counter returns 2× it when the type was Normal/Fighting (Gen 1). Persists until overwritten
    // by the next hit (a Gen 1 quirk — Counter can hit off a previous turn's damage).
    public int LastDamageTaken { get; set; }
    public DamageType? LastDamageType { get; set; }

    // Transform / Conversion: both mutate the creature's *permanent* identity (types, the four non-HP
    // battle stats, SpeciesId, and — for Transform — the whole moveset). This snapshot of the original
    // identity is taken the FIRST time either move mutates it, so a later mutation can't overwrite the
    // true original; Creature.RestoreOriginalIdentity puts everything back on the per-battle reset (the
    // same pattern as the Mimic move-swap revert). Null = identity untouched this battle.
    public IdentitySnapshot? OriginalIdentity { get; set; }
}

/// <summary>
/// The pre-mutation identity of a <see cref="Creature"/>, captured so Transform / Conversion can be
/// undone at battle end (or on a mid-battle Haze reset). Holds the permanent fields those moves change
/// — types, the four non-HP battle stats, SpeciesId, and the original moveset wrappers. HP/MaxHP and
/// level are never copied by Transform, so they aren't snapshotted.
/// </summary>
public sealed class IdentitySnapshot
{
    public DamageType? Type1 { get; init; }
    public DamageType? Type2 { get; init; }
    public int SpeciesId { get; init; }
    public int Attack { get; init; }
    public int Defense { get; init; }
    public int Special { get; init; }
    public int Speed { get; init; }
    public required List<PokemonAttack> MoveSet { get; init; }
}
