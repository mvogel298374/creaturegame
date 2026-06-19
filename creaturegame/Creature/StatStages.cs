using creaturegame.Attacks;

namespace creaturegame.Creatures;

/// <summary>
/// Per-battle stat stage modifiers, each clamped to [-6, +6].
/// Cleared at the start of each battle and by moves like Haze.
///
/// Declared as a class so mutations via Raise* helpers are visible to callers
/// without the copy-back ceremony required for structs assigned through properties.
/// </summary>
public class StatStages
{
    public int Attack { get; private set; }
    public int Defense { get; private set; }
    public int Special { get; private set; }
    public int Speed { get; private set; }
    public int Accuracy { get; private set; }
    public int Evasion { get; private set; }

    public void Clear() => Attack = Defense = Special = Speed = Accuracy = Evasion = 0;

    /// <summary>
    /// Returns an independent copy with the same stage values — used by Transform, which copies the
    /// target's current stat stages onto the user (a separate object so later changes don't alias).
    /// </summary>
    public StatStages Copy() =>
        new()
        {
            Attack = Attack,
            Defense = Defense,
            Special = Special,
            Speed = Speed,
            Accuracy = Accuracy,
            Evasion = Evasion,
        };

    private static int Clamp(int v) => Math.Clamp(v, -6, 6);

    public void RaiseAttack(int delta) => Attack = Clamp(Attack + delta);

    public void RaiseDefense(int delta) => Defense = Clamp(Defense + delta);

    public void RaiseSpecial(int delta) => Special = Clamp(Special + delta);

    public void RaiseSpeed(int delta) => Speed = Clamp(Speed + delta);

    public void RaiseAccuracy(int delta) => Accuracy = Clamp(Accuracy + delta);

    public void RaiseEvasion(int delta) => Evasion = Clamp(Evasion + delta);

    /// <summary>
    /// Raises one stat stage by <paramref name="delta"/> (clamped to [-6, +6]) and returns the resulting
    /// stage. The generic counterpart to the per-stat Raise* helpers — lets callers that hold a
    /// <see cref="StageStat"/> value (X-items, move stat effects) apply it without a six-arm switch.
    /// </summary>
    public int Raise(StageStat stat, int delta)
    {
        switch (stat)
        {
            case StageStat.Attack:
                RaiseAttack(delta);
                return Attack;
            case StageStat.Defense:
                RaiseDefense(delta);
                return Defense;
            case StageStat.Special:
                RaiseSpecial(delta);
                return Special;
            case StageStat.Speed:
                RaiseSpeed(delta);
                return Speed;
            case StageStat.Accuracy:
                RaiseAccuracy(delta);
                return Accuracy;
            case StageStat.Evasion:
                RaiseEvasion(delta);
                return Evasion;
            default:
                return 0;
        }
    }

    /// <summary>Reads the current stage for <paramref name="stat"/> without mutating it.</summary>
    public int Of(StageStat stat) =>
        stat switch
        {
            StageStat.Attack => Attack,
            StageStat.Defense => Defense,
            StageStat.Special => Special,
            StageStat.Speed => Speed,
            StageStat.Accuracy => Accuracy,
            StageStat.Evasion => Evasion,
            _ => 0,
        };
}
