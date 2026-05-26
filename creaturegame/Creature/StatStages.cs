namespace creaturegame.Creatures;

/// <summary>
/// Per-battle stat stage modifiers, each clamped to [-6, +6].
/// Cleared at the end of battle or by moves like Haze.
/// </summary>
public struct StatStages
{
    public int Attack;
    public int Defense;
    public int Special;
    public int Speed;
    public int Accuracy;
    public int Evasion;

    public void Clear() =>
        Attack = Defense = Special = Speed = Accuracy = Evasion = 0;

    private static int Clamp(int v) => Math.Clamp(v, -6, 6);

    public void RaiseAttack(int delta)   => Attack   = Clamp(Attack   + delta);
    public void RaiseDefense(int delta)  => Defense  = Clamp(Defense  + delta);
    public void RaiseSpecial(int delta)  => Special  = Clamp(Special  + delta);
    public void RaiseSpeed(int delta)    => Speed    = Clamp(Speed    + delta);
    public void RaiseAccuracy(int delta) => Accuracy = Clamp(Accuracy + delta);
    public void RaiseEvasion(int delta)  => Evasion  = Clamp(Evasion  + delta);
}
