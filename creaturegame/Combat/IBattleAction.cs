using creaturegame.Attacks;

namespace creaturegame.Combat;

public interface IBattleAction
{
    creaturegame.Creature.Creature Source { get; }
    int Priority { get; }
    Task ExecuteAsync();
}

public class AttackAction : IBattleAction
{
    public creaturegame.Creature.Creature Source { get; }
    public creaturegame.Creature.Creature Target { get; }
    public int Priority { get; }
    private readonly ITypeChart _typeChart;

    public AttackAction(creaturegame.Creature.Creature source, creaturegame.Creature.Creature target, ITypeChart typeChart)
    {
        Source = source;
        Target = target;
        _typeChart = typeChart;
        Priority = source.GetAvailableMove()?.Base.Priority ?? 0;
    }

    public Task ExecuteAsync()
    {
        if (!Source.IsAlive()) return Task.CompletedTask;

        bool usingStruggle = Source.IsOutOfPP;
        var availableMove = usingStruggle ? null : Source.GetAvailableMove();
        Attack attackToUse;
        if (usingStruggle)
        {
            attackToUse = Source.Struggle;
        }
        else
        {
            attackToUse = availableMove!.Base;
            availableMove.PowerPointsCurrent--;
        }

        Console.WriteLine($"{Source.Name} used {attackToUse.Name}!");

        // Accuracy check (Struggle always hits)
        if (!usingStruggle)
        {
            int accuracy = attackToUse.Accuracy;
            if (accuracy < 100 && Random.Shared.Next(1, 101) > accuracy)
            {
                Console.WriteLine("The attack missed!");
                return Task.CompletedTask;
            }
        }

        int damage = DamageCalculator.CalculateGen1Damage(Source, Target, attackToUse, _typeChart);
        Target.Attributes.ReceiveDamage(damage);
        Console.WriteLine($"{Target.Name} took {damage} damage!");

        // Struggle recoil: user takes 1/2 damage dealt (Gen 1 behaviour)
        if (usingStruggle)
        {
            int recoil = Math.Max(1, damage / 2);
            Source.Attributes.ReceiveDamage(recoil);
            Console.WriteLine($"{Source.Name} is hit by recoil! ({recoil} damage)");
        }

        return Task.CompletedTask;
    }
}
