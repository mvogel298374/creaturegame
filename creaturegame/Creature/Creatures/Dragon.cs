namespace creaturegame.Creature.Creatures;

public class Dragon : Creature
{
    public Dragon()
    {
        Name = "Dragon";
        Level = 1;
        Attributes.SetAttributesByCreatureType(CreatureType.Dragon);
    }
    
    public Dragon(string creatureName)
        {
            Name = creatureName;
            Level = 1;
            Attributes.SetAttributesByCreatureType(CreatureType.Dragon);
        }
    
}