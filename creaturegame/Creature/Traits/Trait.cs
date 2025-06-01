namespace creaturegame.Creature.Traits;

public class Trait(TraitType type, string name, string description)
{
    public TraitType Type { get; set; } = type;
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;

    public override string ToString()
    {
        return $"Name: {Name}, TraitType: {Type.ToString()}, Description: {Description}";
    }
}


