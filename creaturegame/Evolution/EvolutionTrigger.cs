namespace creaturegame.Evolution;

/// <summary>The Gen 1 evolution triggers, stored faithfully; their interpretation lives on <see cref="IEvolutionRules"/>.</summary>
public enum EvolutionTrigger
{
    /// <summary>Evolves on reaching a level (PokeAPI <c>level-up</c> with a <c>min_level</c>).</summary>
    Level = 0,

    /// <summary>Evolves when an evolution stone is used (PokeAPI <c>use-item</c>). Gen 1: the five stones.</summary>
    Stone = 1,

    /// <summary>Evolves when traded (PokeAPI <c>trade</c>). Gen 1: Kadabra, Machoke, Graveler, Haunter.</summary>
    Trade = 2,
}
