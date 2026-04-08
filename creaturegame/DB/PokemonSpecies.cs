using System.ComponentModel.DataAnnotations;
using creaturegame.Attacks;
using creaturegame.Creature;

namespace creaturegame.DB;

public class PokemonSpecies
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    public int BaseHP { get; set; }
    public int BaseAttack { get; set; }
    public int BaseDefense { get; set; }
    public int BaseSpecial { get; set; }
    public int BaseSpeed { get; set; }
    
    public DamageType Type1 { get; set; }
    public DamageType? Type2 { get; set; }
    public GrowthRate GrowthRate { get; set; } = GrowthRate.MediumFast;
    
    // New Gen 1 Properties
    public int CatchRate { get; set; }
    public int BaseExperience { get; set; }
    public string? PokedexEntry { get; set; }
}
