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
    public PokemonAttack Move { get; }
    public int Priority { get; }
    private readonly ITypeChart _typeChart;
    private readonly Attack _struggle;

    public AttackAction(creaturegame.Creature.Creature source, creaturegame.Creature.Creature target, PokemonAttack move, ITypeChart typeChart, Attack struggle)
    {
        Source = source;
        Target = target;
        Move = move;
        Priority = move.Base.Priority;
        _typeChart = typeChart;
        _struggle = struggle;
    }

    public Task ExecuteAsync()
    {
        if (!Source.IsAlive()) return Task.CompletedTask;

        // If this move has no PP, use Struggle instead
        Attack attackToUse;
        bool usingStruggle = false;
        if (Move.PowerPointsCurrent <= 0)
        {
            attackToUse = _struggle;
            usingStruggle = true;
        }
        else
        {
            attackToUse = Move.Base;
            Move.PowerPointsCurrent--;
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
