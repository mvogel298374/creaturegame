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
    private readonly ITypeChart _typeChart;

    public AttackAction(creaturegame.Creature.Creature source, creaturegame.Creature.Creature target, Attack move, ITypeChart typeChart)
    {
        Source = source;
        Target = target;
        Move = move;
        Priority = move.Priority;
        _typeChart = typeChart;
    }

    public Task ExecuteAsync()
    {
        if (!Source.IsAlive()) return Task.CompletedTask;

        Console.WriteLine($"{Source.Name} used {Move.Name}!");

        // Accuracy check
        int accuracy = Move.Accuracy;
        if (accuracy < 100 && Random.Shared.Next(1, 101) > accuracy)
        {
            Console.WriteLine("The attack missed!");
            return Task.CompletedTask;
        }

        int damage = DamageCalculator.CalculateGen1Damage(Source, Target, Move, _typeChart);
        Target.Attributes.ReceiveDamage(damage);

        Console.WriteLine($"{Target.Name} took {damage} damage!");
        return Task.CompletedTask;
    }
}
