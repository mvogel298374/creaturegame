using creaturegame.Creatures;

namespace creaturegame.Combat;

public static class StatusResolver
{
    public static int EffectiveSpeed(Creature creature) =>
        creature.Status == StatusCondition.Paralysis ? creature.Attributes.Speed / 4 : creature.Attributes.Speed;

    public static bool CanAct(Creature creature, IBattleRules? rules = null)
    {
        var battleRules = rules ?? Gen1BattleRules.Instance;

        if (creature.Status == StatusCondition.Sleep)
        {
            creature.SleepTurns--;
            if (creature.SleepTurns <= 0)
            {
                creature.SleepTurns = 0;
                creature.Status = StatusCondition.None;
                Console.WriteLine($"{creature.Name} woke up!");
            }
            else
            {
                Console.WriteLine($"{creature.Name} is fast asleep!");
            }
            return false;
        }

        if (creature.Status == StatusCondition.Freeze)
        {
            if (battleRules.FreezeRandomThawPercent > 0
                && Random.Shared.Next(100) < battleRules.FreezeRandomThawPercent)
            {
                creature.Status = StatusCondition.None;
                Console.WriteLine($"{creature.Name} thawed out!");
                return true;
            }
            Console.WriteLine($"{creature.Name} is frozen solid!");
            return false;
        }

        if (creature.Status == StatusCondition.Paralysis && Random.Shared.Next(4) == 0)
        {
            Console.WriteLine($"{creature.Name} is fully paralyzed! It can't move!");
            return false;
        }

        if (creature.ConfusedTurns > 0)
        {
            Console.WriteLine($"{creature.Name} is confused!");
            creature.ConfusedTurns--;
            if (creature.ConfusedTurns == 0)
            {
                Console.WriteLine($"{creature.Name} snapped out of confusion!");
                return true;
            }
            if (Random.Shared.Next(2) == 0)
            {
                Console.WriteLine("It hurt itself in its confusion!");
                int selfDamage = DamageCalculator.CalculateConfusionDamage(creature);
                creature.Attributes.ReceiveDamage(selfDamage);
                Console.WriteLine($"{creature.Name} took {selfDamage} damage!");
                return false;
            }
        }

        return true;
    }

    public static void ApplyEndOfTurnDamage(Creature creature)
    {
        if (!creature.IsAlive()) return;

        int damage = 0;
        string message = "";

        if (creature.Status == StatusCondition.Burn)
        {
            damage = Math.Max(1, creature.Attributes.MaxHP / 16);
            message = $"{creature.Name} is hurt by its burn!";
        }
        else if (creature.Status == StatusCondition.Poison)
        {
            damage = Math.Max(1, creature.Attributes.MaxHP / 16);
            message = $"{creature.Name} is hurt by poison!";
        }

        if (damage > 0)
        {
            creature.Attributes.ReceiveDamage(damage);
            Console.WriteLine($"{message} ({damage} damage)");
        }
    }
}
