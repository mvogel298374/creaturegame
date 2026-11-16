namespace creaturegame.Combat;

/// <summary>The last damage either side's move produced, one value for both sides (pokered's <c>wDamage</c>). See GENERATION_SEAMS.md §2.</summary>
public sealed class LastDamage
{
    public int Value { get; set; }
}
