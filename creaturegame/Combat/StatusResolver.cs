using creaturegame.Creatures;

namespace creaturegame.Combat;

public static class StatusResolver
{
    public static int EffectiveSpeed(Creature creature) =>
        creature.Status == StatusCondition.Paralysis ? creature.Attributes.Speed / 4 : creature.Attributes.Speed;

    public static bool CanAct(Creature creature, IBattleRules? rules = null, IBattleEventEmitter? emitter = null)
    {
        var battleRules = rules ?? Gen1BattleRules.Instance;

        if (creature.Status == StatusCondition.Sleep)
        {
            creature.SleepTurns--;
            if (creature.SleepTurns <= 0)
            {
                creature.SleepTurns = 0;
                creature.Status = StatusCondition.None;
                emitter?.Emit(new StatusCleared(creature.Name, StatusCondition.Sleep));
            }
            else
            {
                emitter?.Emit(new ActionBlocked(creature.Name, StatusCondition.Sleep));
            }
            return false;
        }

        if (creature.Status == StatusCondition.Freeze)
        {
            if (battleRules.FreezeRandomThawPercent > 0
                && Random.Shared.Next(100) < battleRules.FreezeRandomThawPercent)
            {
                creature.Status = StatusCondition.None;
                emitter?.Emit(new StatusCleared(creature.Name, StatusCondition.Freeze));
                return true;
            }
            emitter?.Emit(new ActionBlocked(creature.Name, StatusCondition.Freeze));
            return false;
        }

        if (creature.Status == StatusCondition.Paralysis && Random.Shared.Next(4) == 0)
        {
            emitter?.Emit(new ActionBlocked(creature.Name, StatusCondition.Paralysis));
            return false;
        }

        if (creature.ConfusedTurns > 0)
        {
            emitter?.Emit(new ConfusionMessage(creature.Name));
            creature.ConfusedTurns--;
            if (creature.ConfusedTurns == 0)
            {
                emitter?.Emit(new ConfusionCleared(creature.Name));
                return true;
            }
            if (Random.Shared.Next(2) == 0)
            {
                int selfDamage = DamageCalculator.CalculateConfusionDamage(creature, battleRules);
                creature.Attributes.ReceiveDamage(selfDamage);
                emitter?.Emit(new ConfusionDamage(creature.Name, selfDamage, creature.Attributes.HP));
                return false;
            }
        }

        return true;
    }

    public static void ApplyEndOfTurnDamage(Creature creature, IBattleRules? rules = null, IBattleEventEmitter? emitter = null)
    {
        if (!creature.IsAlive()) return;

        var battleRules = rules ?? Gen1BattleRules.Instance;
        int damage = 0;
        StatusCondition source = StatusCondition.None;

        if (creature.Status == StatusCondition.Burn)
        {
            damage = Math.Max(1, creature.Attributes.MaxHP / battleRules.BurnDamageDenominator);
            source = StatusCondition.Burn;
        }
        else if (creature.Status == StatusCondition.Poison)
        {
            damage = Math.Max(1, creature.Attributes.MaxHP / battleRules.PoisonDamageDenominator);
            source = StatusCondition.Poison;
        }

        if (damage > 0)
        {
            creature.Attributes.ReceiveDamage(damage);
            emitter?.Emit(new StatusDamage(creature.Name, damage, source, creature.Attributes.HP));
        }
    }
}
