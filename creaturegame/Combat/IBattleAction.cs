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
    public Attack Move { get; }
    public int Priority { get; }

    public AttackAction(creaturegame.Creature.Creature source, creaturegame.Creature.Creature target, Attack move)
    {
        Source = source;
        Target = target;
        Move = move;
        // In Gen 1, most moves have priority 0. Only Quick Attack (+1) etc. have different.
        Priority = 0; 
    }

    public Task ExecuteAsync()
    {
        if (!Source.IsAlive()) return Task.CompletedTask;

        Console.WriteLine($"{Source.Name} used {Move.Name}!");

        // Accuracy check
        int accuracy = Move.Accuracy;
        // Simplified accuracy check for now
        if (Random.Shared.Next(1, 101) > accuracy)
        {
            Console.WriteLine("The attack missed!");
            return Task.CompletedTask;
        }

        int damage = DamageCalculator.CalculateGen1Damage(Source, Target, Move);
        Target.Attributes.ReceiveDamage(damage);

        Console.WriteLine($"{Target.Name} took {damage} damage!");
        return Task.CompletedTask;
    }
}
