using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public static class StatusResolver
{
    public static int EffectiveSpeed(Creature creature, IBattleRules? rules = null)
    {
        var r = rules ?? Gen1BattleRules.Instance;
        double speed =
            creature.Attributes.Speed * r.GetStatMultiplier(creature.Battle.Stages.Speed);
        if (creature.Battle.Status == StatusCondition.Paralysis)
            speed /= r.ParalysisSpeedDivisor;
        return (int)speed;
    }

    public static bool CanAct(
        Creature creature,
        IBattleRules? rules = null,
        IBattleEventEmitter? emitter = null,
        IRandomSource? rng = null,
        Creature? foe = null,
        PokemonAttack? selectedMove = null
    )
    {
        var battleRules = rules ?? Gen1BattleRules.Instance;
        var random = rng ?? SystemRandomSource.Instance;

        // Haze: self-clearing flag set by a faster Haze user this turn that just cured this
        // creature's Sleep/Freeze — Gen 1 still forfeits the already-chosen action rather than
        // letting it act the instant it wakes (see BattleState.HazeSuppressedStatus).
        if (creature.Battle.HazeSuppressedStatus is { } suppressedStatus)
        {
            creature.Battle.HazeSuppressedStatus = null;
            emitter?.Emit(new ActionBlocked(creature.Name, creature.Id, suppressedStatus));
            return false;
        }

        foreach (var check in battleRules.PreMoveCheckOrder)
        {
            bool lost = check switch
            {
                PreMoveCheck.Sleep => SleepStops(creature, battleRules, emitter),
                PreMoveCheck.Freeze => FreezeStops(creature, battleRules, emitter, random),
                PreMoveCheck.HeldByTrap => TrapStops(creature, emitter),
                PreMoveCheck.Flinch => FlinchStops(creature, emitter),
                PreMoveCheck.Recharge => RechargeStops(creature, emitter),
                PreMoveCheck.DisableCountdown => TickDisable(creature, emitter),
                PreMoveCheck.Confusion => ConfusionStops(
                    creature,
                    foe,
                    battleRules,
                    emitter,
                    random
                ),
                PreMoveCheck.UsingDisabledMove => DisabledMoveStops(
                    creature,
                    selectedMove,
                    battleRules,
                    emitter
                ),
                PreMoveCheck.Paralysis => ParalysisStops(creature, battleRules, emitter, random),
                _ => throw new ArgumentOutOfRangeException(nameof(check), check, null),
            };
            if (!lost)
                continue;
            if (battleRules.PreMoveCheckCancelsLockIn(check))
                CancelLockIn(creature);
            if (battleRules.LostTurnEndsSemiInvulnerability(check))
                creature.Battle.SemiInvulnerability = SemiInvulnerability.None;
            return false;
        }

        return true;
    }

    private static void ForgetLastMove(Creature creature, IBattleRules rules)
    {
        if (rules.SleepOrFreezeTurnForgetsLastMove)
            creature.Battle.LastMoveUsed = null;
    }

    private static bool SleepStops(
        Creature creature,
        IBattleRules rules,
        IBattleEventEmitter? emitter
    )
    {
        if (creature.Battle.Status != StatusCondition.Sleep)
            return false;

        ForgetLastMove(creature, rules);
        creature.Battle.SleepTurns--;
        if (creature.Battle.SleepTurns <= 0)
        {
            creature.Battle.SleepTurns = 0;
            creature.Battle.Status = StatusCondition.None;
            emitter?.Emit(new StatusCleared(creature.Name, creature.Id, StatusCondition.Sleep));
        }
        else
        {
            emitter?.Emit(new ActionBlocked(creature.Name, creature.Id, StatusCondition.Sleep));
        }
        return true;
    }

    private static bool FreezeStops(
        Creature creature,
        IBattleRules rules,
        IBattleEventEmitter? emitter,
        IRandomSource random
    )
    {
        if (creature.Battle.Status != StatusCondition.Freeze)
            return false;

        if (rules.FreezeRandomThawPercent > 0 && random.Next(100) < rules.FreezeRandomThawPercent)
        {
            creature.Battle.Status = StatusCondition.None;
            emitter?.Emit(new StatusCleared(creature.Name, creature.Id, StatusCondition.Freeze));
            return false;
        }
        ForgetLastMove(creature, rules);
        emitter?.Emit(new ActionBlocked(creature.Name, creature.Id, StatusCondition.Freeze));
        return true;
    }

    private static bool TrapStops(Creature creature, IBattleEventEmitter? emitter)
    {
        if (creature.Battle.BindingTurnsRemaining == 0)
            return false;
        emitter?.Emit(new BindingBlocked(creature.Name, creature.Id));
        return true;
    }

    private static bool FlinchStops(Creature creature, IBattleEventEmitter? emitter)
    {
        if (!creature.Battle.IsFlinched)
            return false;
        creature.Battle.IsFlinched = false;
        emitter?.Emit(new FlinchBlocked(creature.Name, creature.Id));
        return true;
    }

    private static bool RechargeStops(Creature creature, IBattleEventEmitter? emitter)
    {
        if (!creature.Battle.IsRecharging)
            return false;
        creature.Battle.IsRecharging = false;
        emitter?.Emit(new Recharging(creature.Name, creature.Id));
        return true;
    }

    // Never costs the turn; it only counts this attempt down.
    private static bool TickDisable(Creature creature, IBattleEventEmitter? emitter)
    {
        if (creature.Battle.DisableTurnsRemaining == 0)
            return false;

        creature.Battle.DisableTurnsRemaining--;
        if (creature.Battle.DisableTurnsRemaining == 0 && creature.Battle.DisabledMove != null)
        {
            string reEnabled = creature.Battle.DisabledMove.Base.Name ?? "";
            creature.Battle.DisabledMove = null;
            emitter?.Emit(new MoveReEnabled(creature.Name, creature.Id, reEnabled));
        }
        return false;
    }

    private static bool ConfusionStops(
        Creature creature,
        Creature? foe,
        IBattleRules rules,
        IBattleEventEmitter? emitter,
        IRandomSource random
    )
    {
        if (creature.Battle.ConfusedTurns == 0)
            return false;

        creature.Battle.ConfusedTurns--;
        bool ended = creature.Battle.ConfusedTurns == 0;
        if (!ended || rules.ConfusionEndTurnPrintsConfusedLine)
            emitter?.Emit(new ConfusionMessage(creature.Name, creature.Id));
        if (ended)
        {
            emitter?.Emit(new ConfusionCleared(creature.Name, creature.Id));
            return false;
        }
        if (random.Next(100) >= rules.ConfusionSelfHitPercent)
            return false;

        int selfDamage = DamageCalculator.CalculateConfusionDamage(creature, foe, rules);
        SelfInflictedDamage.Apply(
            creature,
            foe,
            selfDamage,
            rules,
            emitter,
            hp => new ConfusionDamage(creature.Name, creature.Id, selfDamage, hp)
        );
        return true;
    }

    private static bool DisabledMoveStops(
        Creature creature,
        PokemonAttack? selectedMove,
        IBattleRules rules,
        IBattleEventEmitter? emitter
    )
    {
        if (selectedMove is null || selectedMove != creature.Battle.DisabledMove)
            return false;

        if (rules.DisabledMoveBlockEndsCharge)
        {
            creature.Battle.IsTwoTurnCharging = false;
            creature.Battle.ChargingMove = null;
        }
        emitter?.Emit(new MoveIsDisabled(creature.Name, creature.Id, selectedMove.Base.Name ?? ""));
        return true;
    }

    private static bool ParalysisStops(
        Creature creature,
        IBattleRules rules,
        IBattleEventEmitter? emitter,
        IRandomSource random
    )
    {
        if (
            creature.Battle.Status != StatusCondition.Paralysis
            || !rules.RollsFullParalysis(random)
        )
            return false;
        emitter?.Emit(new ActionBlocked(creature.Name, creature.Id, StatusCondition.Paralysis));
        return true;
    }

    private static void CancelLockIn(Creature creature)
    {
        var state = creature.Battle;
        state.BideTurnsRemaining = 0;
        state.BideDamageAccumulated = 0;
        state.BideMove = null;
        state.RampageTurnsRemaining = 0;
        state.RampageMove = null;
        state.IsTwoTurnCharging = false;
        state.ChargingMove = null;
        ReleaseTrap(creature);
    }

    /// <summary>Ends any trap <paramref name="binder"/> holds, freeing its victim at once.</summary>
    internal static void ReleaseTrap(Creature binder)
    {
        if (binder.Battle.BindingTarget is { } victim)
            victim.Battle.BindingTurnsRemaining = 0;
        binder.Battle.BindingMove = null;
        binder.Battle.BindingTarget = null;
    }

    /// <summary>Clears <paramref name="victim"/>'s pending recharge when the rules say this infliction does.</summary>
    internal static void ClearRechargeOn(
        Creature victim,
        RechargeClearingInfliction infliction,
        IBattleRules rules
    )
    {
        if (rules.InflictionClearsRecharge(infliction))
            victim.Battle.IsRecharging = false;
    }

    /// <summary>Ticks the binding countdown every turn (GENERATION_SEAMS.md §2, Pre-move checks).</summary>
    public static void TickTurnCounters(Creature creature)
    {
        if (!creature.IsAlive())
            return;

        if (creature.Battle.BindingTurnsRemaining > 0)
            creature.Battle.BindingTurnsRemaining--;
    }

    public static void ApplyEndOfTurnDamage(
        Creature creature,
        IBattleRules? rules = null,
        IBattleEventEmitter? emitter = null
    )
    {
        if (!creature.IsAlive())
            return;

        var battleRules = rules ?? Gen1BattleRules.Instance;

        // Status damage
        int damage = 0;
        StatusCondition source = StatusCondition.None;

        if (creature.Battle.Status == StatusCondition.Burn)
        {
            damage = Math.Max(1, creature.Attributes.MaxHP / battleRules.BurnDamageDenominator);
            source = StatusCondition.Burn;
        }
        else if (creature.Battle.Status == StatusCondition.Poison)
        {
            damage = Math.Max(1, creature.Attributes.MaxHP / battleRules.PoisonDamageDenominator);
            source = StatusCondition.Poison;
        }
        else if (creature.Battle.Status == StatusCondition.BadPoison)
        {
            damage = Math.Max(
                1,
                (int)
                    Math.Floor(
                        creature.Attributes.MaxHP
                            * battleRules.BadPoisonDamageFraction(creature.Battle.ToxicCounter)
                    )
            );
            source = StatusCondition.BadPoison;
            creature.Battle.ToxicCounter++;
        }

        if (damage > 0)
        {
            creature.Attributes.ReceiveDamage(damage);
            emitter?.Emit(
                new StatusDamage(creature.Name, creature.Id, damage, source, creature.Attributes.HP)
            );
        }
    }
}
