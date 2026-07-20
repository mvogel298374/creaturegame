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

    // Haze: set when this creature was cured of Sleep or Freeze by a Haze user earlier THIS turn.
    // Gen 1's HazeEffect_ marks the woken creature's already-chosen move invalid rather than letting
    // it act immediately just because the status cleared mid-turn — so the forfeit still applies even
    // though StatusResolver.CanAct's own Sleep/Freeze branch no longer has a status to check.
    // Self-clearing like IsFlinched when a same-turn CanAct call actually consumes it (the Haze user
    // was faster) — see Creature.ResetForHaze and StatusResolver.CanAct. NOT always same-turn, though:
    // if the target was faster and had already resolved its own blocked Sleep/Freeze turn before Haze
    // fires, this flag would otherwise sit unconsumed and wrongly block the target's NEXT turn too —
    // Battle's turn loop defensively nulls it for both creatures at end-of-turn so a flag that outlives
    // the turn it was set on can never leak forward (Gen 1's own invalidation write only ever matters
    // for the turn it's issued on; the next turn's fresh move selection overwrites it unread).
    public StatusCondition? HazeSuppressedStatus { get; set; }

    // Roar / Whirlwind: set on a creature when it is scared off (the move's target flees). The Battle loop
    // ends the encounter when either side has fled — no faint. Transient like the rest; the per-battle reset
    // clears it.
    public bool HasFled { get; set; }

    // Binding (Wrap/Bind/Clamp/Fire Spin) — Gen 1 partial trap, two halves of one effect:
    //  • On the VICTIM: BindingTurnsRemaining > 0 means trapped — it loses its turn (StatusResolver.CanAct)
    //    until the counter ticks to 0 end-of-turn. Gen 1 deals NO residual chip; the damage is the binder's
    //    move re-hitting each turn, not an end-of-turn tick.
    //  • On the BINDER: BindingMove is the move it is locked into and BindingTarget is the trapped creature.
    //    BindingMechanic.ForcedMove re-forces the move while the victim's counter is alive, so the binder
    //    can't act freely (Gen 1: "neither the user nor the target can select moves" during a bind).
    public int BindingTurnsRemaining { get; set; }
    public PokemonAttack? BindingMove { get; set; }
    public Creature? BindingTarget { get; set; }

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
/// undone at battle end (or the start of the creature's next battle — see
/// <see cref="Creature.ResetBattleState"/>). A mid-battle Haze does NOT undo it: Gen 1's Haze explicitly
/// keeps an active Transform's TRANSFORMED bit set (see <see cref="Creature.ResetForHaze"/>). Holds the
/// permanent fields those moves change — types, the four non-HP battle stats, SpeciesId, and the
/// original moveset wrappers. HP/MaxHP and level are never copied by Transform, so they aren't
/// snapshotted.
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
