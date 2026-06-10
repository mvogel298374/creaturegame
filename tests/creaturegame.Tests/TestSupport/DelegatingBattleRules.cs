using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;

namespace creaturegame.Tests.TestSupport;

/// <summary>
/// Base test double for <see cref="IBattleRules"/>: every member delegates to
/// <see cref="Gen1BattleRules.Instance"/> and is <c>virtual</c>, so a test that needs to
/// force one behaviour (always-hit, always-crit, a fixed stat) overrides just that member.
/// <para>
/// This exists so that adding a new <see cref="IBattleRules"/> member is a <b>one-line</b>
/// change here, instead of editing every hand-written shim. (Before this base there were
/// five near-identical ~20-line copies; adding <c>RollConfusionTurns</c> touched four of them.)
/// </para>
/// </summary>
public abstract class DelegatingBattleRules : IBattleRules
{
    private static IBattleRules Gen1 => Gen1BattleRules.Instance;

    public virtual bool CanThawFrozenTarget(Attack move) => Gen1.CanThawFrozenTarget(move);

    public virtual int FreezeRandomThawPercent => Gen1.FreezeRandomThawPercent;
    public virtual double StabMultiplier => Gen1.StabMultiplier;
    public virtual int ConfusionSelfHitPercent => Gen1.ConfusionSelfHitPercent;

    public virtual int GetSecondaryEffectChance(Attack m, SecondaryEffectKind e) =>
        Gen1.GetSecondaryEffectChance(m, e);

    public virtual int RollMultiHitCount() => Gen1.RollMultiHitCount();

    public virtual int PayDayCoinMultiplier => Gen1.PayDayCoinMultiplier;

    public virtual double RollDamageVariance() => Gen1.RollDamageVariance();

    public virtual int RollSleepTurns() => Gen1.RollSleepTurns();

    public virtual int RollConfusionTurns() => Gen1.RollConfusionTurns();

    public virtual int CalculateStruggleRecoil(Creature s, int d) =>
        Gen1.CalculateStruggleRecoil(s, d);

    public virtual int BurnDamageDenominator => Gen1.BurnDamageDenominator;
    public virtual int PoisonDamageDenominator => Gen1.PoisonDamageDenominator;

    public virtual double BadPoisonDamageFraction(int toxicCounter) =>
        Gen1.BadPoisonDamageFraction(toxicCounter);

    public virtual double GetStatMultiplier(int stage) => Gen1.GetStatMultiplier(stage);

    public virtual double GetAccuracyStageMultiplier(int stage) =>
        Gen1.GetAccuracyStageMultiplier(stage);

    public virtual int GetHitThreshold(int acc, int accStage, int evaStage) =>
        Gen1.GetHitThreshold(acc, accStage, evaStage);

    public virtual int AccuracyRollBound => Gen1.AccuracyRollBound;

    public virtual double GetCritChance(Creature a, Attack m) => Gen1.GetCritChance(a, m);

    public virtual double CritMultiplier => Gen1.CritMultiplier;
    public virtual bool CritIgnoresStatStages => Gen1.CritIgnoresStatStages;

    public virtual int RollBindingTurns() => Gen1.RollBindingTurns();

    public virtual int BindingDamageDenominator => Gen1.BindingDamageDenominator;

    public virtual int CalculateCrashDamage(Creature user) => Gen1.CalculateCrashDamage(user);

    public virtual int CalculateRecoilDamage(int damageDealt) =>
        Gen1.CalculateRecoilDamage(damageDealt);

    public virtual int RollRampageTurns() => Gen1.RollRampageTurns();

    public virtual int RollDisableTurns() => Gen1.RollDisableTurns();

    public virtual bool OneHitKoSucceeds(Creature u, Creature t) => Gen1.OneHitKoSucceeds(u, t);

    public virtual bool CounterQualifies(DamageType? lastDamageType) =>
        Gen1.CounterQualifies(lastDamageType);

    public virtual int SelfDestructDefenseDivisor => Gen1.SelfDestructDefenseDivisor;
    public virtual int RageAttackStagesPerHit => Gen1.RageAttackStagesPerHit;
    public virtual double RecoverHealFraction => Gen1.RecoverHealFraction;
    public virtual int ScreenDefenseMultiplier => Gen1.ScreenDefenseMultiplier;

    public virtual int RollBideTurns() => Gen1.RollBideTurns();

    public virtual int BideDamageMultiplier => Gen1.BideDamageMultiplier;

    public virtual int RollPsywaveDamage(Creature s, IRandomSource rng) =>
        Gen1.RollPsywaveDamage(s, rng);

    public virtual int RestSleepTurns => Gen1.RestSleepTurns;

    public virtual bool CanReceiveStatus(Creature t, StatusCondition s, DamageType mt) =>
        Gen1.CanReceiveStatus(t, s, mt);

    public virtual bool PureStatusMoveChecksTypeImmunity(Attack move) =>
        Gen1.PureStatusMoveChecksTypeImmunity(move);

    public virtual bool CanBeLeechSeeded(Creature t) => Gen1.CanBeLeechSeeded(t);

    public virtual StatusCondition CarryStatusOutOfBattle(StatusCondition status) =>
        Gen1.CarryStatusOutOfBattle(status);

    public virtual int CalculateXpAwarded(int baseExp, int enemyLevel) =>
        Gen1.CalculateXpAwarded(baseExp, enemyLevel);

    public virtual int GetOffensiveStat(Creature a, AttackType t) => Gen1.GetOffensiveStat(a, t);

    public virtual int GetDefensiveStat(Creature d, AttackType t) => Gen1.GetDefensiveStat(d, t);
}

/// <summary>Always-hits double — overrides only the accuracy threshold. Shared by tests
/// that need to remove the Gen 1 1/256-miss flakiness without caring about other mechanics.</summary>
public sealed class AlwaysHitRules : DelegatingBattleRules
{
    public static readonly AlwaysHitRules Instance = new();

    public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256; // ≥ AccuracyRollBound → never misses
}

/// <summary>Always-crits, no damage variance — for deterministic crit tests.</summary>
public sealed class AlwaysCritRules : DelegatingBattleRules
{
    public static readonly AlwaysCritRules Instance = new();

    public override double RollDamageVariance() => 1.0;

    public override double GetCritChance(Creature a, Attack m) => 1.0;
}

/// <summary>Never hits — the accuracy roll always fails (threshold 0 ⇒ roll ≥ 0 is always true).</summary>
public sealed class NeverHitRules : DelegatingBattleRules
{
    public static readonly NeverHitRules Instance = new();

    public override int GetHitThreshold(int acc, int accStage, int evaStage) => 0;
}

/// <summary>Always hits, no crit, no damage variance — deterministic damage for exact-math asserts.</summary>
public sealed class NoVarianceNoCritHitRules : DelegatingBattleRules
{
    public static readonly NoVarianceNoCritHitRules Instance = new();

    public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256; // always hit

    public override double GetCritChance(Creature a, Attack m) => 0.0;

    public override double RollDamageVariance() => 1.0;
}

/// <summary>Always hits, no crit, and forces any secondary effect to land (chance 100).
/// Lets a status/effect test assert the effect without fighting the random roll.</summary>
public sealed class ForceSecondaryRules : DelegatingBattleRules
{
    public static readonly ForceSecondaryRules Instance = new();

    public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

    public override double GetCritChance(Creature a, Attack m) => 0.0;

    public override int GetSecondaryEffectChance(Attack m, SecondaryEffectKind e) => 100;
}

/// <summary>Always hits with a fixed multi-hit count — pins the number of strikes deterministically.</summary>
public sealed class FixedMultiHitRules(int hits) : DelegatingBattleRules
{
    public override int RollMultiHitCount() => hits;

    public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256; // always hit

    public override double GetCritChance(Creature a, Attack m) => 0.0;
}
