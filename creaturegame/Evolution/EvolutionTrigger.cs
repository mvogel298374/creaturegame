namespace creaturegame.Evolution;

/// <summary>
/// The kind of condition that causes one species to evolve into another. These are the
/// <b>Gen 1</b> triggers; the value is stored <i>faithfully</i> on <see cref="DB.PokemonEvolution"/>
/// (a trade evolution is recorded as <see cref="Trade"/>, not pre-converted) — the
/// generation/game-mode interpretation lives on <see cref="IEvolutionRules"/>, never in the data.
/// <para>
/// Later generations add more triggers (friendship, time-of-day, held-item trade, the Tyrogue
/// stat-ratio rule). A new generation is a new <see cref="IEvolutionRules"/> implementation plus,
/// where the data differs, new rows — not edits here.
/// </para>
/// </summary>
public enum EvolutionTrigger
{
    /// <summary>Evolves on reaching a level (PokeAPI <c>level-up</c> with a <c>min_level</c>).</summary>
    Level = 0,

    /// <summary>Evolves when an evolution stone is used (PokeAPI <c>use-item</c>). Gen 1: the five stones.</summary>
    Stone = 1,

    /// <summary>Evolves when traded (PokeAPI <c>trade</c>). Gen 1: Kadabra, Machoke, Graveler, Haunter.</summary>
    Trade = 2,
}
