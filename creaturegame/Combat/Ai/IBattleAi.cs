using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>The move-selection brain, hosted by <see cref="AiBattleInput"/>; only called with a non-empty candidate list.</summary>
public interface IBattleAi
{
    PokemonAttack ChooseMove(IReadOnlyList<PokemonAttack> candidates, TurnContext context);
}
