namespace creaturegame.Attacks;

public class PokemonAttack(Attack baseAttack)
{
    public Attack Base { get; set; } = baseAttack;
    public int PowerPointsCurrent { get; set; } = baseAttack.PowerPointsMax;
    
}