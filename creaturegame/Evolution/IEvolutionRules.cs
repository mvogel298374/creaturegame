using creaturegame.Creatures;
using creaturegame.DB;

namespace creaturegame.Evolution;

/// <summary>
/// Generation seam for evolution: given a creature, the trigger that just happened, and the
/// evolution edges for its species, decides whether (and into what) it evolves. A parallel to
/// <see cref="creaturegame.Combat.ITypeChart"/> / <see cref="creaturegame.Combat.IBattleRules"/> /
/// <see cref="IStatCalculator"/> — the caller stays generation-agnostic and never branches on a
/// generation enum; a new generation is a new implementation, not edits to the loop.
/// <para>
/// The <i>data</i> (<see cref="PokemonEvolution"/>) is faithful to the game (a trade evo is a
/// <see cref="EvolutionTrigger.Trade"/> row); the <i>interpretation</i> — including this roguelite's
/// "trade evolutions happen at a level instead" adaptation — lives here.
/// </para>
/// </summary>
public interface IEvolutionRules
{
    /// <summary>
    /// Returns the evolution that fires for <paramref name="creature"/> given <paramref name="context"/>,
    /// or <c>null</c> if none. <paramref name="edges"/> are the candidate evolution edges (the caller
    /// passes the rows for this species; the implementation also filters defensively by
    /// <see cref="PokemonEvolution.FromSpeciesId"/>).
    /// <para>
    /// <b>Assumption:</b> a species' edges are mutually exclusive under any single context — at most one
    /// can fire. This holds for Gen 1 (a species has one outgoing trigger type; stone branches are
    /// disambiguated by the used item). If a later generation's data could have two edges fire at once,
    /// that generation's implementation must define the precedence deliberately rather than relying on
    /// edge order (the Gen 1 impl returns the first match).
    /// </para>
    /// </summary>
    EvolutionResult? CheckEvolution(
        Creature creature,
        EvolutionContext context,
        IReadOnlyList<PokemonEvolution> edges
    );

    /// <summary>
    /// The floor <paramref name="edge"/> alone imposes on <see cref="PokemonEvolution.ToSpeciesId"/> — this
    /// generation's interpretation of what level that edge's trigger requires, independent of the rest of the
    /// chain (<see cref="EvolutionMinLevel"/> walks the chain and combines each edge's own floor with its
    /// predecessor's). Drives the encounter-generation invariant that a wild/draft creature can never be a
    /// post-evolution species below the level it takes to reach that form (<c>ENCOUNTER_DESIGN.md §3.8</c>).
    /// Gen 1: a <see cref="EvolutionTrigger.Level"/> edge returns its own threshold; a
    /// <see cref="EvolutionTrigger.Trade"/> edge returns this roguelite's trade-to-level stand-in
    /// (<see cref="Gen1EvolutionRules.TradeEvolutionLevel"/>); a <see cref="EvolutionTrigger.Stone"/> edge
    /// returns 0 — a stone can be used at any level in real Gen 1, so it imposes no floor of its own.
    /// </summary>
    int MinLevelFor(PokemonEvolution edge);
}
