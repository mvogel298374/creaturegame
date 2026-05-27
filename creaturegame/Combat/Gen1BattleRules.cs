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

    // Gen 1: binding traps for 2–5 turns.
    public int RollBindingTurns() => Random.Shared.Next(2, 6);
    public int BindingDamageDenominator => 16;

    // ── Stat stages ────────────────────────────────────────────────────────────

    // Gen 1/2 battle-stat table: 2/(2+|n|) for n≤0, (2+n)/2 for n>0.
    // Yields 0.25× at -6, 1.0× at 0, 4.0× at +6.
    public double GetStatMultiplier(int stage)
    {
        stage = Math.Clamp(stage, -6, 6);
        return stage <= 0 ? 2.0 / (2 - stage) : (2.0 + stage) / 2.0;
    }

    // Gen 1 accuracy/evasion table: 3/(3+|n|) for n≤0, (3+n)/3 for n>0.
    // Yields 0.333× at -6, 1.0× at 0, 3.0× at +6.
    public double GetAccuracyStageMultiplier(int stage)
    {
        stage = Math.Clamp(stage, -6, 6);
        return stage <= 0 ? 3.0 / (3 - stage) : (3.0 + stage) / 3.0;
    }

    // Gen 1: convert accuracy % to 0-255 scale, apply stage multipliers.
    // A roll of 255 always misses (1/256 bug) because the threshold caps at 255.
    public int GetHitThreshold(int accuracyPercent, int attackerAccStage, int defenderEvaStage)
    {
        int threshold = (int)Math.Floor(accuracyPercent * 255.0 / 100.0);
        double accMult = GetAccuracyStageMultiplier(attackerAccStage);
        double evaMult = GetAccuracyStageMultiplier(defenderEvaStage);
        return Math.Clamp((int)Math.Floor(threshold * accMult / evaMult), 0, 255);
    }

    // Gen 1: roll 0-255; roll >= threshold → miss. Roll 255 always misses for 100%-acc moves.
    public int AccuracyRollBound => 256;

    // ── Critical hits ──────────────────────────────────────────────────────────

    // Gen 1: normal = floor(BaseSpeed/2)/256; high-crit = min(floor(BaseSpeed/2)*8, 255)/256.
    // BaseSpeed is the raw unmodified base speed stat.
    public double GetCritChance(Creature attacker, Attack move)
    {
        double numerator = Math.Floor(attacker.BaseSpeed / 2.0);
        if (move.IsHighCrit)
            numerator = Math.Min(numerator * 8, 255);
        return numerator / 256.0;
    }

    public double CritMultiplier => 2.0;

    // Gen 1: crits use raw computed stats, bypassing stages and the Burn Attack penalty.
    public bool CritIgnoresStatStages => true;
}
