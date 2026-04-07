using creaturegame.Attacks;

namespace creaturegame.DB;

public class AttackSeeder
{
    private readonly AttackService _attackService;

    public AttackSeeder(AttackService attackService)
    {
        _attackService = attackService;
    }

    public async Task SeedTackleAsync()
    {
        var tackle = await _attackService.GetAttackByIdAsync(33);
        if (tackle == null)
        {
            Attack newattack = new Attack()
            {
                Id = 33, // Tackle's ID in PokeAPI is 33
                Accuracy = 100,
                Name = "tackle",
                Description = "A Normal-type attack. Many Pokémon know this attack right from the start.",
                BaseDamage = 40,
                PowerPointsMax = 35,
                AttackType = AttackType.Physical,
                DamageType = DamageType.Normal
            };
            await _attackService.UpsertAttackAsync(newattack);
        }
    }

    /// <summary>
    /// Assigns a default move (tackle) to a creature if available in the database.
    /// </summary>
    public async Task GiveDefaultMoveAsync(Creature.Creature creature)
    {
        var move = await _attackService.GetAttackByNameAsync("tackle");
        if (move != null)
        {
            creature.AddAttack(move);
        }
    }

    /// <summary>
    /// Assigns a random move from the database to a creature.
    /// </summary>
    public async Task GiveRandomMoveAsync(Creature.Creature creature)
    {
        var move = await _attackService.GetRandomAttackAsync();
        if (move != null)
        {
            creature.AddAttack(move);
        }
    }
}