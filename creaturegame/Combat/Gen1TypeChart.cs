using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// Gen 1 (RBY) type effectiveness chart, matching pret/pokered data/types/type_matchups.asm. The quirks it
/// preserves (Ghost → Psychic = 0x, Poison ↔ Bug, Ice → Fire = 1x) and how later generations differ are in
/// docs/GEN_DIFFERENCES.md → Type Chart Quirks.
/// </summary>
public class Gen1TypeChart : ITypeChart
{
    public static readonly Gen1TypeChart Instance = new();

    // Outer key = attacking type, inner key = defending type, value = multiplier.
    // Only non-1.0 entries are stored; missing entries default to 1.0.
    private static readonly Dictionary<DamageType, Dictionary<DamageType, double>> Chart = new()
    {
        [DamageType.Normal] = new() { [DamageType.Rock] = 0.5, [DamageType.Ghost] = 0.0 },
        [DamageType.Fire] = new()
        {
            [DamageType.Fire] = 0.5,
            [DamageType.Water] = 0.5,
            [DamageType.Grass] = 2.0,
            [DamageType.Ice] = 2.0,
            [DamageType.Bug] = 2.0,
            [DamageType.Rock] = 0.5,
            [DamageType.Dragon] = 0.5,
        },
        [DamageType.Water] = new()
        {
            [DamageType.Fire] = 2.0,
            [DamageType.Water] = 0.5,
            [DamageType.Grass] = 0.5,
            [DamageType.Ground] = 2.0,
            [DamageType.Rock] = 2.0,
            [DamageType.Dragon] = 0.5,
        },
        [DamageType.Electric] = new()
        {
            [DamageType.Water] = 2.0,
            [DamageType.Electric] = 0.5,
            [DamageType.Grass] = 0.5,
            [DamageType.Ground] = 0.0,
            [DamageType.Flying] = 2.0,
            [DamageType.Dragon] = 0.5,
        },
        [DamageType.Grass] = new()
        {
            [DamageType.Fire] = 0.5,
            [DamageType.Water] = 2.0,
            [DamageType.Grass] = 0.5,
            [DamageType.Poison] = 0.5,
            [DamageType.Ground] = 2.0,
            [DamageType.Flying] = 0.5,
            [DamageType.Bug] = 0.5,
            [DamageType.Rock] = 2.0,
            [DamageType.Dragon] = 0.5,
        },
        [DamageType.Ice] = new()
        {
            [DamageType.Water] = 0.5,
            [DamageType.Grass] = 2.0,
            [DamageType.Ice] = 0.5,
            [DamageType.Ground] = 2.0,
            [DamageType.Flying] = 2.0,
            [DamageType.Dragon] = 2.0,
        },
        [DamageType.Fighting] = new()
        {
            [DamageType.Normal] = 2.0,
            [DamageType.Ice] = 2.0,
            [DamageType.Poison] = 0.5,
            [DamageType.Flying] = 0.5,
            [DamageType.Psychic] = 0.5,
            [DamageType.Bug] = 0.5,
            [DamageType.Rock] = 2.0,
            [DamageType.Ghost] = 0.0,
        },
        [DamageType.Poison] = new()
        {
            [DamageType.Grass] = 2.0,
            [DamageType.Poison] = 0.5,
            [DamageType.Ground] = 0.5,
            [DamageType.Bug] = 2.0, // Gen 1 quirk
            [DamageType.Rock] = 0.5,
            [DamageType.Ghost] = 0.5,
        },
        [DamageType.Ground] = new()
        {
            [DamageType.Fire] = 2.0,
            [DamageType.Electric] = 2.0,
            [DamageType.Grass] = 0.5,
            [DamageType.Poison] = 2.0,
            [DamageType.Flying] = 0.0,
            [DamageType.Bug] = 0.5,
            [DamageType.Rock] = 2.0,
        },
        [DamageType.Flying] = new()
        {
            [DamageType.Electric] = 0.5,
            [DamageType.Grass] = 2.0,
            [DamageType.Fighting] = 2.0,
            [DamageType.Bug] = 2.0,
            [DamageType.Rock] = 0.5,
        },
        [DamageType.Psychic] = new()
        {
            [DamageType.Fighting] = 2.0,
            [DamageType.Poison] = 2.0,
            [DamageType.Psychic] = 0.5,
            // Gen 1 bug: Ghost → Psychic = 0x is on the Ghost row, not here
        },
        [DamageType.Bug] = new()
        {
            [DamageType.Fire] = 0.5,
            [DamageType.Grass] = 2.0,
            [DamageType.Fighting] = 0.5,
            [DamageType.Flying] = 0.5,
            [DamageType.Psychic] = 2.0,
            [DamageType.Ghost] = 0.5,
            [DamageType.Poison] = 2.0, // Gen 1 quirk
        },
        [DamageType.Rock] = new()
        {
            [DamageType.Fire] = 2.0,
            [DamageType.Ice] = 2.0,
            [DamageType.Fighting] = 0.5,
            [DamageType.Ground] = 0.5,
            [DamageType.Flying] = 2.0,
            [DamageType.Bug] = 2.0,
        },
        [DamageType.Ghost] = new()
        {
            [DamageType.Normal] = 0.0,
            [DamageType.Psychic] = 0.0, // Gen 1 bug: Ghost is immune to Psychic (should be 2x)
            [DamageType.Ghost] = 2.0,
        },
        [DamageType.Dragon] = new() { [DamageType.Dragon] = 2.0 },
    };

    /// <inheritdoc />
    public double GetMultiplier(DamageType attackType, DamageType defenderType)
    {
        if (
            Chart.TryGetValue(attackType, out var defenderMap)
            && defenderMap.TryGetValue(defenderType, out double multiplier)
        )
        {
            return multiplier;
        }
        return 1.0;
    }
}
