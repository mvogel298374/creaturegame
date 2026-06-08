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
            speed /= 4;
        return (int)speed;
    }

    public static bool CanAct(
        Creature creature,
        IBattleRules? rules = null,
        IBattleEventEmitter? emitter = null,
        IRandomSource? rng = null
    )
    {
        var battleRules = rules ?? Gen1BattleRules.Instance;
        var random = rng ?? SystemRandomSource.Instance;

        // Flinch: self-clearing flag set by a faster attacker this turn
        if (creature.Battle.IsFlinched)
        {
            creature.Battle.IsFlinched = false;
            emitter?.Emit(new FlinchBlocked(creature.Name));
            return false;
        }

        // Binding: trapped by Wrap/Bind/Clamp/Fire Spin
        if (creature.Battle.BindingTurnsRemaining > 0)
        {
            emitter?.Emit(new BindingBlocked(creature.Name));
            return false;
        }

        if (creature.Battle.Status == StatusCondition.Sleep)
        {
            creature.Battle.SleepTurns--;
            if (creature.Battle.SleepTurns <= 0)
            {
                creature.Battle.SleepTurns = 0;
                creature.Battle.Status = StatusCondition.None;
                emitter?.Emit(new StatusCleared(creature.Name, StatusCondition.Sleep));
            }
            else
            {
                emitter?.Emit(new ActionBlocked(creature.Name, StatusCondition.Sleep));
            }
            return false;
        }

        if (creature.Battle.Status == StatusCondition.Freeze)
        {
            if (
                battleRules.FreezeRandomThawPercent > 0
                && random.Next(100) < battleRules.FreezeRandomThawPercent
            )
            {
                creature.Battle.Status = StatusCondition.None;
                emitter?.Emit(new StatusCleared(creature.Name, StatusCondition.Freeze));
                return true;
            }
            emitter?.Emit(new ActionBlocked(creature.Name, StatusCondition.Freeze));
            return false;
        }

        if (creature.Battle.Status == StatusCondition.Paralysis && random.Next(4) == 0)
        {
            emitter?.Emit(new ActionBlocked(creature.Name, StatusCondition.Paralysis));
            return false;
        }

        if (creature.Battle.ConfusedTurns > 0)
        {
            emitter?.Emit(new ConfusionMessage(creature.Name));
            creature.Battle.ConfusedTurns--;
            if (creature.Battle.ConfusedTurns == 0)
            {
                emitter?.Emit(new ConfusionCleared(creature.Name));
                return true;
            }
            if (random.Next(100) < battleRules.ConfusionSelfHitPercent)
            {
                int selfDamage = DamageCalculator.CalculateConfusionDamage(creature, battleRules);
                creature.Attributes.ReceiveDamage(selfDamage);
                emitter?.Emit(
                    new ConfusionDamage(creature.Name, selfDamage, creature.Attributes.HP)
                );
                return false;
            }
        }

        return true;
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

        // Disable countdown — tick down each turn and re-enable the move when the lock expires.
        if (creature.Battle.DisableTurnsRemaining > 0)
        {
            creature.Battle.DisableTurnsRemaining--;
            if (creature.Battle.DisableTurnsRemaining == 0 && creature.Battle.DisabledMove != null)
            {
                string reEnabled = creature.Battle.DisabledMove.Base.Name ?? "";
                creature.Battle.DisabledMove = null;
                emitter?.Emit(new MoveReEnabled(creature.Name, reEnabled));
            }
        }

        // Binding damage — decrement counter and deal 1/16 max HP
        if (creature.Battle.BindingTurnsRemaining > 0)
        {
            creature.Battle.BindingTurnsRemaining--;
            int bindDamage = Math.Max(
                1,
                creature.Attributes.MaxHP / battleRules.BindingDamageDenominator
            );
            creature.Attributes.ReceiveDamage(bindDamage);
            emitter?.Emit(new BindingDamage(creature.Name, bindDamage, creature.Attributes.HP));
            if (!creature.IsAlive())
                return;
        }

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
            emitter?.Emit(new StatusDamage(creature.Name, damage, source, creature.Attributes.HP));
        }
    }
}
