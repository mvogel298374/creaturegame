using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public sealed class Gen1BattleRules : IBattleRules
{
    public static readonly Gen1BattleRules Instance = new();

    // Gen 1: only Fire-type moves that can inflict burn thaw a frozen target.
    // Fire Spin cannot burn, so it does not thaw — even though it is Fire-type.
    public bool CanThawFrozenTarget(Attack move) =>
        move.DamageType == DamageType.Fire && move.StatusEffect == StatusCondition.Burn;

    // Gen 1 freeze is permanent until hit by the right move; no random per-turn thaw.
    public int FreezeRandomThawPercent => 0;

    // Gen 1 damage roll: uniform integer in [217, 255], divided by 255.
    public double RollDamageVariance() => Random.Shared.Next(217, 256) / 255.0;

    // Gen 1 sleep lasts 1–7 turns.
    public int RollSleepTurns() => Random.Shared.Next(1, 8);

    // Gen 1 Struggle recoil: half the damage dealt to the target.
    public int CalculateStruggleRecoil(Creature source, int damageDealt) =>
        Math.Max(1, damageDealt / 2);

    // Gen 1–5: Burn and Poison each deal 1/16 max HP per turn.
    public int BurnDamageDenominator   => 16;
    public int PoisonDamageDenominator => 16;
}
