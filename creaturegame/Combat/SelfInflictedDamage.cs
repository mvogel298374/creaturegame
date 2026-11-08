using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>A confusion self-hit or a crash landing. See GENERATION_SEAMS.md §2.</summary>
public static class SelfInflictedDamage
{
    public static void Apply(
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

        if (struckFoe is null)
            user.Attributes.ReceiveDamage(damage);
        emitter?.Emit(announce(user.Attributes.HP));

        if (struckFoe is not null)
            StrikeFoeSubstitute(struckFoe, damage, rules, emitter);
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

        emitter?.Emit(new SubstituteAbsorbedHit(foe.Name, foe.Id, after));
        if (broke)
            emitter?.Emit(new SubstituteFaded(foe.Name, foe.Id));
    }
}
