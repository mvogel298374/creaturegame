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
}
