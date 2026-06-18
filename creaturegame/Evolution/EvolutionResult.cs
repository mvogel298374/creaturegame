namespace creaturegame.Evolution;

/// <summary>
/// The outcome of a fired evolution check: the matched edge. The caller resolves
/// <see cref="ToSpeciesId"/> to a <see cref="DB.PokemonSpecies"/> and applies it
/// (<c>Creature.EvolveTo</c>, Stage 2). <see cref="Trigger"/> is carried for logging/UI
/// ("evolved by leveling up" vs. a future stone/trade message).
/// </summary>
public sealed record EvolutionResult(int FromSpeciesId, int ToSpeciesId, EvolutionTrigger Trigger);
