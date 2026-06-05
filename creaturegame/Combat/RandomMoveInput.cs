using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// Picks a uniformly random move from those with PP remaining. The baseline enemy input
/// (lowest AI tier / wild Pokémon) — unlike <see cref="AutoSelectInput"/>, which always
/// returns the first move and so spams whatever sits in slot 0 (often a low-level status
/// move), this varies its choice so the enemy actually uses its attacks.
/// <para>
/// Uses the <see cref="IRandomSource"/> seam so it's seedable/reproducible. A scoring
/// AI (<c>WeightedAIInput</c>/<c>GreedyAIInput</c> via <c>IMoveEvaluator</c>) is the
/// planned next tier — see the "AI Move Selection" section of TODO.
/// </para>
/// </summary>
public sealed class RandomMoveInput(IRandomSource? rng = null) : IBattleInput
{
    private readonly IRandomSource _rng = rng ?? SystemRandomSource.Instance;

    public Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
    {
        // Battle guarantees this is only called when CanSelectAnyMove == true, so at least one
        // move has PP and isn't Disabled.
        var available = context
            .Attacker.MoveSet.Where(m => m.PowerPointsCurrent > 0 && m != context.DisabledMove)
            .ToList();
        if (available.Count == 0)
            throw new InvalidOperationException(
                $"{context.Attacker.Name}: ChooseMoveAsync called with no selectable move. "
                    + "Battle should have bypassed IBattleInput and passed null (Struggle) directly."
            );

        return Task.FromResult(available[_rng.Next(available.Count)]);
    }
}
