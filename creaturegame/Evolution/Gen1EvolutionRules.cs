using creaturegame.Creatures;
using creaturegame.DB;

namespace creaturegame.Evolution;

/// <summary>
/// Gen 1 evolution rules, adapted for this single-player roguelite. Default singleton
/// (<see cref="Instance"/>) used everywhere, mirroring <c>Gen1BattleRules.Instance</c> et al.
/// <para>Gen 1 has exactly three triggers; this is how each is interpreted:</para>
/// <list type="bullet">
/// <item><b>Level</b> — fires when the creature has reached the edge's <see cref="PokemonEvolution.LevelThreshold"/>.</item>
/// <item><b>Trade</b> — there is no trading here, so a trade evolution is converted to a level
/// evolution at <see cref="TradeEvolutionLevel"/> (a game-mode rule, kept on the seam, not in the data).
/// A genuine <see cref="EvolutionContext.Traded"/> also fires it, for a future trade-capable mode.</item>
/// <item><b>Stone</b> — fires only on a <see cref="EvolutionContext.StoneUsed"/> whose item matches the
/// edge. No caller produces that context yet (the bag/item layer is deferred with the Catch mechanic),
/// so stone lines are dormant in practice — but the rule is complete and ready.</item>
/// </list>
/// </summary>
public sealed class Gen1EvolutionRules : IEvolutionRules
{
    public static readonly Gen1EvolutionRules Instance = new();

    /// <summary>
    /// The level at which a Gen 1 <see cref="EvolutionTrigger.Trade"/> evolution fires in this roguelite,
    /// standing in for the unavailable trade. Flat across all four trade lines (Alakazam, Machamp, Golem,
    /// Gengar) for consistency. A game-mode constant, not a canonical Gen 1 value — a different mode or
    /// generation would override it (or restore real trading).
    /// </summary>
    public const int TradeEvolutionLevel = 37;

    public EvolutionResult? CheckEvolution(
        Creature creature,
        EvolutionContext context,
        IReadOnlyList<PokemonEvolution> edges
    )
    {
        foreach (var edge in edges)
        {
            if (edge.FromSpeciesId != creature.SpeciesId)
                continue;

            if (Fires(edge, context))
                return new EvolutionResult(edge.FromSpeciesId, edge.ToSpeciesId, edge.Trigger);
        }

        return null;
    }

    private static bool Fires(PokemonEvolution edge, EvolutionContext context) =>
        edge.Trigger switch
        {
            EvolutionTrigger.Level => context is EvolutionContext.LeveledTo lt
                && edge.LevelThreshold is int threshold
                && lt.Level >= threshold,

            // Roguelite adaptation: a level-up reaching TradeEvolutionLevel stands in for the trade;
            // a real Traded context (future mode) also fires it.
            EvolutionTrigger.Trade => context is EvolutionContext.Traded
                || (context is EvolutionContext.LeveledTo lt && lt.Level >= TradeEvolutionLevel),

            EvolutionTrigger.Stone => context is EvolutionContext.StoneUsed used
                && edge.StoneItemId == used.StoneItemId,

            _ => false,
        };
}
