using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>What a strike leaves in a side's Substitute byte. See GENERATION_SEAMS.md §2.</summary>
public readonly record struct SubstituteStrike(int Remnant, bool Broke);

public static class SubstituteStrikes
{
    /// <summary>Strikes a standing decoy and its byte; true when the decoy broke.</summary>
    public static bool StrikeDecoy(BattleState state, int damage, IBattleRules rules)
    {
        state.SubstituteRemnant = rules
            .StrikeSubstituteByte(state.SubstituteRemnant, damage)
            .Remnant;
        state.SubstituteHp = Math.Max(0, state.SubstituteHp - damage);
        return state.SubstituteHp == 0;
    }
}
