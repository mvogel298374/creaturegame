using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// Defines a type effectiveness chart for a specific generation.
/// Swap implementations to change generation rules (e.g. Gen1TypeChart → Gen2TypeChart).
/// </summary>
public interface ITypeChart
{
    /// <summary>
    /// Returns the damage multiplier for a move of <paramref name="attackType"/> hitting a target of <paramref name="defenderType"/>.
    /// </summary>
    double GetMultiplier(DamageType attackType, DamageType defenderType);
}
