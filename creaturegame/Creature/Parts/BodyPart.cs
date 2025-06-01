namespace creaturegame.Creature.Parts;

public class BodyPart(Body body, Brain brain)
{
    
    public Body Body { get; set; } = body;

    public Brain Brain { get; set; } = brain;

    public override string ToString()
    {
        return $"{Body.ToString()}, {Brain.ToString()}";
    }
}