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
    // Every member delegates to this inner Gen 1 ruleset. By default it is the shared global singleton, but a
    // subclass may pass a seeded IRandomSource so the inner ruleset's *unpinned* random rolls (RollSleepTurns,
    // RollMultiHitCount, RollDamageVariance, …) draw from that seed instead of Random.Shared. Without this hook
    // a seeded BattleScenario could never make those rolls reproducible — they would always hit the global RNG,
    // which is exactly the test-order flakiness this seam closes.
    private readonly IBattleRules _inner;

    protected DelegatingBattleRules()
        : this(null) { }

    protected DelegatingBattleRules(IRandomSource? rng) =>
        _inner = rng is null ? Gen1BattleRules.Instance : new Gen1BattleRules(rng);

    public virtual bool CanThawFrozenTarget(Attack move) => _inner.CanThawFrozenTarget(move);

    public virtual int FreezeRandomThawPercent => _inner.FreezeRandomThawPercent;
    public virtual double StabMultiplier => _inner.StabMultiplier;
    public virtual int ConfusionSelfHitPercent => _inner.ConfusionSelfHitPercent;

    public virtual int GetSecondaryEffectChance(Attack m, SecondaryEffectKind e) =>
        _inner.GetSecondaryEffectChance(m, e);

    public virtual bool SecondaryHits(int chancePercent, IRandomSource rng) =>
        _inner.SecondaryHits(chancePercent, rng);

    public virtual int RollMultiHitCount() => _inner.RollMultiHitCount();

    public virtual int PayDayCoinMultiplier => _inner.PayDayCoinMultiplier;

    public virtual double RollDamageVariance() => _inner.RollDamageVariance();

    public virtual int RollSleepTurns() => _inner.RollSleepTurns();

    public virtual int RollConfusionTurns() => _inner.RollConfusionTurns();

    public virtual int CalculateStruggleRecoil(Creature s, int d) =>
        _inner.CalculateStruggleRecoil(s, d);

    public virtual int BurnDamageDenominator => _inner.BurnDamageDenominator;
    public virtual int PoisonDamageDenominator => _inner.PoisonDamageDenominator;

    public virtual double BadPoisonDamageFraction(int toxicCounter) =>
        _inner.BadPoisonDamageFraction(toxicCounter);

    public virtual double GetStatMultiplier(int stage) => _inner.GetStatMultiplier(stage);

    public virtual double GetAccuracyStageMultiplier(int stage) =>
        _inner.GetAccuracyStageMultiplier(stage);

    public virtual int GetHitThreshold(int acc, int accStage, int evaStage) =>
        _inner.GetHitThreshold(acc, accStage, evaStage);

    public virtual int AccuracyRollBound => _inner.AccuracyRollBound;

    public virtual double GetCritChance(Creature a, Attack m) => _inner.GetCritChance(a, m);

    public virtual double CritMultiplier => _inner.CritMultiplier;
    public virtual bool CritIgnoresStatStages => _inner.CritIgnoresStatStages;

    public virtual int RollBindingTurns() => _inner.RollBindingTurns();

    public virtual int CalculateCrashDamage(Creature user) => _inner.CalculateCrashDamage(user);

    public virtual int CalculateRecoilDamage(int damageDealt) =>
        _inner.CalculateRecoilDamage(damageDealt);

    public virtual int RollRampageTurns() => _inner.RollRampageTurns();

    public virtual int RollDisableTurns() => _inner.RollDisableTurns();

    public virtual bool OneHitKoSucceeds(Creature u, Creature t) => _inner.OneHitKoSucceeds(u, t);

    public virtual bool CounterQualifies(DamageType? lastDamageType) =>
        _inner.CounterQualifies(lastDamageType);

    public virtual int SelfDestructDefenseDivisor => _inner.SelfDestructDefenseDivisor;
    public virtual int RageAttackStagesPerHit => _inner.RageAttackStagesPerHit;
    public virtual double RecoverHealFraction => _inner.RecoverHealFraction;
    public virtual int ScreenDefenseMultiplier => _inner.ScreenDefenseMultiplier;

    public virtual int RollBideTurns() => _inner.RollBideTurns();

    public virtual int BideDamageMultiplier => _inner.BideDamageMultiplier;

    public virtual int RollPsywaveDamage(Creature s, IRandomSource rng) =>
        _inner.RollPsywaveDamage(s, rng);

    public virtual int RestSleepTurns => _inner.RestSleepTurns;

    public virtual bool CanReceiveStatus(Creature t, StatusCondition s, DamageType mt) =>
        _inner.CanReceiveStatus(t, s, mt);

    public virtual bool PureStatusMoveChecksTypeImmunity(Attack move) =>
        _inner.PureStatusMoveChecksTypeImmunity(move);

    public virtual bool CanBeLeechSeeded(Creature t) => _inner.CanBeLeechSeeded(t);

    public virtual StatusCondition CarryStatusOutOfBattle(StatusCondition status) =>
        _inner.CarryStatusOutOfBattle(status);

    public virtual int CalculateXpAwarded(int baseExp, int enemyLevel) =>
        _inner.CalculateXpAwarded(baseExp, enemyLevel);

    public virtual int GetOffensiveStat(Creature a, AttackType t) => _inner.GetOffensiveStat(a, t);

    public virtual int GetDefensiveStat(Creature d, AttackType t) => _inner.GetDefensiveStat(d, t);
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
