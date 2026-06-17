using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// Scores a move by the share of the target's current HP it is expected to remove this turn, discounted by
/// accuracy. A move that can KO outright scores above 1.0 so a lethal option is always the strongest damage
/// signal; otherwise the score is the expected-damage fraction in [0, 1]. Non-damaging moves score 0.
/// The estimate is deterministic (no crit, no variance — see <see cref="DamageCalculator.EstimateDamage"/>)
/// and honours the live damage seams, so it stays correct for any generation.
/// </summary>
public sealed class DamageEvaluator : IMoveEvaluator
{
    // A securable KO is worth more than merely large damage, so it outranks a non-lethal heavy hit.
    private const double KnockOutScore = 1.5;

    public double Score(PokemonAttack move, TurnContext context)
    {
        var attacker = context.Attacker;
        var defender = context.Defender;
        int estimate = EstimateDamage(move.Base, attacker, defender, context);
        if (estimate <= 0)
            return 0;

        double accuracy = move.Base.NeverMisses
            ? 1.0
            : Math.Clamp(move.Base.Accuracy / 100.0, 0.0, 1.0);
        int targetHp = Math.Max(1, defender.Attributes.HP);
        double fraction = (double)estimate / targetHp;
        double value = fraction >= 1.0 ? KnockOutScore : fraction;
        return value * accuracy;
    }

    // Resolves a move's expected damage across the damage categories. Standard/Drain/SelfDestruct go through
    // the shared formula; the fixed/level/HP-fraction categories are computed directly. The exact figure only
    // needs to be good enough to rank moves and spot a likely KO.
    private static int EstimateDamage(
        Attack move,
        Creature attacker,
        Creature defender,
        TurnContext context
    ) =>
        move.DamageCategory switch
        {
            DamageCategory.Fixed => move.FixedDamageValue ?? 0,
            DamageCategory.LevelBased => attacker.Level,
            DamageCategory.SuperFang => Math.Max(1, defender.Attributes.HP / 2),
            DamageCategory.OHKO => defender.Attributes.HP,
            // Psywave deals 0–1.5× level (avg ~0.75×); the user's level is a fair central estimate.
            DamageCategory.Psywave => attacker.Level,
            _ => DamageCalculator.EstimateDamage(
                attacker,
                defender,
                move,
                context.TypeChart,
                context.Rules
            ),
        };
}

/// <summary>
/// An extra nudge toward super-effective moves and away from resisted/no-effect ones, on top of the damage
/// the matchup already produces. This reproduces the authentic Gen 1 trainer-AI bias (the "encourage
/// super-effective / discourage not-very-effective" layer), so the brain leans on type advantage the way the
/// 1996 game did rather than purely maximising a damage number. Non-damaging moves score 0; a no-effect (0×)
/// matchup is strongly penalised so the AI won't waste a turn on an immune target.
/// </summary>
public sealed class TypeEffectivenessEvaluator : IMoveEvaluator
{
    private const double NoEffectPenalty = -2.0;
    private const double PerDoublingBonus = 0.4; // ×2 ⇒ +0.4, ×0.5 ⇒ −0.4, ×4 ⇒ +0.8, ×0.25 ⇒ −0.8

    public double Score(PokemonAttack move, TurnContext context)
    {
        if (move.Base.BaseDamage == 0)
            return 0;

        double multiplier = DamageCalculator.GetTypeEffectiveness(
            move.Base.DamageType,
            context.Defender.Type1,
            context.Defender.Type2,
            context.TypeChart
        );
        if (multiplier <= 0)
            return NoEffectPenalty;

        // Log scale so each doubling/halving is one even step in either direction.
        return Math.Log2(multiplier) * PerDoublingBonus;
    }
}

/// <summary>
/// Values a move's stat-stage change: raising the user's own stat is good while there's headroom (and worth
/// less the more boosted it already is), lowering the foe's stat is good while it isn't already floored, and
/// either at its cap is a wasted turn (small penalty). Mirrors the Gen 1 AI "don't keep boosting a maxed
/// stat" discouragement. Reads the stat through the same <see cref="StageStat"/> the engine applies, so it
/// covers both pure stat moves and the stat-drop secondary on a damaging move.
/// </summary>
public sealed class StatStageMoveEvaluator : IMoveEvaluator
{
    private const double RaiseValue = 0.30;
    private const double LowerValue = 0.25;
    private const double CappedPenalty = -0.30;

    public double Score(PokemonAttack move, TurnContext context)
    {
        var effect = move.Base.StatEffect;
        if (effect is null || effect.Delta == 0)
            return 0;

        bool affectsSelf = effect.Target == StageTarget.Self;
        var subject = affectsSelf ? context.Attacker : context.Defender;
        int stage = StageOf(subject, effect.Stat);

        if (effect.Delta > 0) // raising (a self-buff in Gen 1)
        {
            if (stage >= 6)
                return CappedPenalty;
            return RaiseValue * ((6 - stage) / 6.0);
        }

        // lowering (a debuff on the foe)
        if (stage <= -6)
            return CappedPenalty;
        return LowerValue * ((6 + stage) / 6.0);
    }

    private static int StageOf(Creature c, StageStat stat) =>
        stat switch
        {
            StageStat.Attack => c.Battle.Stages.Attack,
            StageStat.Defense => c.Battle.Stages.Defense,
            StageStat.Special => c.Battle.Stages.Special,
            StageStat.Speed => c.Battle.Stages.Speed,
            StageStat.Accuracy => c.Battle.Stages.Accuracy,
            StageStat.Evasion => c.Battle.Stages.Evasion,
            _ => 0,
        };
}

/// <summary>
/// Values a move that inflicts a major status. A fresh status on a healthy foe is worth a lot — the
/// disabling ones (Sleep/Freeze) most, then Paralysis, then Burn (it also chips Attack), then Poison — but
/// it is a wasted turn if the foe already carries a major status or is type-immune to this one, which the Gen
/// 1 AI explicitly avoids (a small penalty here). Reads <see cref="Attack.StatusEffect"/>, so it covers both
/// pure status moves and the status secondary on a damaging move. Non-status moves score 0.
/// </summary>
public sealed class StatusMoveEvaluator : IMoveEvaluator
{
    private const double RedundantPenalty = -0.30;

    public double Score(PokemonAttack move, TurnContext context)
    {
        var status = move.Base.StatusEffect;
        if (status == StatusCondition.None)
            return 0;

        var foe = context.Defender;
        if (foe.Battle.Status != StatusCondition.None) // only one major status at a time in Gen 1
            return RedundantPenalty;
        // Type immunity is the authoritative gen-variable rule on IBattleRules (Gen 1: Poison can't be
        // poisoned, Fire can't be burned, a Normal move can't paralyse a Normal-type — and notably Freeze
        // has NO type immunity in Gen 1). Route through the seam rather than re-deriving it here, so the AI's
        // judgement always matches what the engine will actually do and never enshrines a wrong/stale fact.
        if (!context.Rules.CanReceiveStatus(foe, status, move.Base.DamageType))
            return RedundantPenalty;

        return status switch
        {
            StatusCondition.Sleep or StatusCondition.Freeze => 0.60,
            StatusCondition.Paralysis => 0.50,
            StatusCondition.Burn => 0.40,
            StatusCondition.Poison or StatusCondition.BadPoison => 0.35,
            _ => 0,
        };
    }
}
