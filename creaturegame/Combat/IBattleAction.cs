using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public interface IBattleAction
{
    Creature Source { get; }
    int Priority { get; }
    Task ExecuteAsync();
}

public class AttackAction : IBattleAction
{
    public Creature Source { get; }
    public Creature Target { get; }
    public int Priority { get; }
    private readonly ITypeChart _typeChart;

    // Null means Struggle — Battle passes null when Source.IsOutOfPP, bypassing IBattleInput.
    private readonly PokemonAttack? _selectedMove;

    /// <param name="selectedMove">
    /// The move committed to this turn, as chosen by IBattleInput.
    /// Pass null to force Struggle (all PP exhausted).
    /// </param>
    public AttackAction(Creature source, Creature target,
                        PokemonAttack? selectedMove, ITypeChart typeChart)
    {
        Source        = source;
        Target        = target;
        _typeChart    = typeChart;
        _selectedMove = selectedMove;
        Priority      = selectedMove?.Base.Priority ?? 0;
    }

    public Task ExecuteAsync()
    {
        if (!Source.IsAlive()) return Task.CompletedTask;

        bool usingStruggle = _selectedMove == null;
        Attack attackToUse = usingStruggle ? Source.Struggle : _selectedMove!.Base;

        if (!usingStruggle)
            _selectedMove!.PowerPointsCurrent--;

        Console.WriteLine($"{Source.Name} used {attackToUse.Name}!");

        // Accuracy check (Struggle always hits)
        if (!usingStruggle && attackToUse.Accuracy < 100
            && Random.Shared.Next(1, 101) > attackToUse.Accuracy)
        {
            Console.WriteLine("The attack missed!");
            return Task.CompletedTask;
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
