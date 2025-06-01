namespace creaturegame.Creature.Creatures;

public class Dragon : Creature
{
    public Dragon()
    {
        Name = "Dragon";
        Level = 1;
        Attributes.SetAttributesByCreatureType(CreatureType.Dragon);
    }
    
}