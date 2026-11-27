using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>A confusion self-hit or a crash landing. See GENERATION_SEAMS.md §2.</summary>
public static class SelfInflictedDamage
{
    /// <returns>The damage recorded as the last damage (<see cref="IBattleRules.DrainAndRecoilUseHpLost"/>).</returns>
    public static int Apply(
        Creature user,
        Creature? foe,
        int damage,
        IBattleRules rules,
        IBattleEventEmitter? emitter,
        Func<int, BattleEvent> announce
    )
    {
        var struckFoe =
            damage > 0
            && user.Battle.SubstituteHp > 0
            && rules.SelfInflictedDamageStrikesFoeSubstitute
                ? foe
                : null;

        int recorded = damage;
        if (struckFoe is null)
        {
            if (rules.DrainAndRecoilUseHpLost)
                recorded = Math.Min(damage, user.Attributes.HP);
            user.Attributes.ReceiveDamage(damage);
        }
        emitter?.Emit(announce(user.Attributes.HP));

        if (struckFoe is not null)
            StrikeFoeSubstitute(struckFoe, damage, rules, emitter);
        return recorded;
    }

    private static void StrikeFoeSubstitute(
        Creature foe,
        int damage,
        IBattleRules rules,
        IBattleEventEmitter? emitter
    )
    {
        var state = foe.Battle;
        bool broke;
        int after;
        if (state.SubstituteHp > 0)
        {
            broke = SubstituteStrikes.StrikeDecoy(state, damage, rules);
            after = state.SubstituteHp;
        }
        else
        {
            var strike = rules.StrikeSubstituteByte(state.SubstituteRemnant, damage);
            state.SubstituteRemnant = strike.Remnant;
            broke = strike.Broke;
            after = broke ? 0 : strike.Remnant;
        }

        SubstituteStrikes.AnnounceStrike(foe, after, broke, rules, emitter);
    }
}
