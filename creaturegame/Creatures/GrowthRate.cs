namespace creaturegame.Creatures;

public enum GrowthRate
{
    Fast, // 0.8 * n^3
    MediumFast, // n^3
    MediumSlow, // 1.2 * n^3 - 15 * n^2 + 100 * n - 140
    Slow, // 1.25 * n^3
}
