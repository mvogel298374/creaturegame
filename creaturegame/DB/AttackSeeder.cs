using creaturegame.Attacks;

namespace creaturegame.DB;

public class AttackSeeder
{
    public void AddAttack()
    {
        Attack newattack = new Attack()
        {
            Accuracy = 95,
            Name = "Tackle",
            Description = "A Normal-type attack. Many Pokémon know this attack right from the start.",
            BaseDamage = 40,
            PowerPointsMax = 30,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Normal
        };
        
        
        using var context = new GameDbContext();
        context.Moves.Add(newattack);
        context.SaveChanges();
    }
}