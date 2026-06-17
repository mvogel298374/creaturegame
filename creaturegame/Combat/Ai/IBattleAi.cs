using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// A generation/game-specific move-selection brain: given the moves a creature may legally use this turn and
/// a read-only <see cref="TurnContext"/>, decide which one to use. This is the seam the AI's <i>decision
/// quality and personality</i> hangs on — a wild-Pokémon brain, a trainer brain, a gym-leader brain, or a
/// future <c>Gen2Ai</c> are all just different implementations, swapped in at the composition root without
/// the engine ever branching on generation (mirrors <see cref="ITypeChart"/> / <see cref="IBattleRules"/>).
///
/// <para>It is split from <see cref="IBattleInput"/> on purpose: <see cref="IBattleInput"/> is the I/O
/// plumbing (player UI, network, console, automated), while <see cref="IBattleAi"/> is the thinking. A single
/// <see cref="AiBattleInput"/> hosts any brain, so brains and plumbing vary independently.</para>
///
/// <para>The brain is only ever called with a non-empty candidate list — <see cref="Battle"/> handles the
/// no-selectable-move case (Struggle) and lock-in continuations before any input is consulted.</para>
/// </summary>
public interface IBattleAi
{
    PokemonAttack ChooseMove(IReadOnlyList<PokemonAttack> candidates, TurnContext context);
}
