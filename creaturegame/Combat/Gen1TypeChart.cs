using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// Gen 1 (RBY) type effectiveness chart.
/// Notable Gen 1 quirks preserved:
///   - Ghost → Psychic = 0x (bug: should be 2x, but was 0x in RBY)
///   - Poison → Bug     = 2x (changed to 0.5x in Gen 2+)
///   - Bug   → Poison   = 2x (changed to 1x in Gen 2+)
///   - Bug   → Psychic  = 2x (changed to 1x in Gen 2+)
///   - Ice   → Fire     = 1x (changed to 0.5x in Gen 2+)
///   - No Dark, Steel, or Fairy types exist in Gen 1.
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
            // Gen 1 quirk: Ice → Fire = 1.0 (not 0.5 as in Gen 2+)
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
            [DamageType.Bug] = 2.0, // Gen 1 quirk: 2x (nerfed to 0.5x in Gen 2+)
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
            [DamageType.Psychic] = 2.0, // Gen 1 quirk: 2x (changed to 1x in Gen 2+)
            [DamageType.Ghost] = 0.5,
            [DamageType.Poison] = 2.0, // Gen 1 quirk: 2x (changed to 1x in Gen 2+)
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
