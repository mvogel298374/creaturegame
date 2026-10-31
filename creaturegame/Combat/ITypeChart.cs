using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>A generation's type effectiveness chart.</summary>
public interface ITypeChart
{
    /// <summary>
    /// Returns the damage multiplier for a move of <paramref name="attackType"/> hitting a target of <paramref name="defenderType"/>.
    /// </summary>
    double GetMultiplier(DamageType attackType, DamageType defenderType);
}
