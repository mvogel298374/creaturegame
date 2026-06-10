using creaturegame.Attacks;

namespace creaturegame.Creatures;

/// <summary>
/// One entry in a creature's level-up learnset: the move it gains at <paramref name="Level"/>, already
/// resolved from a <see cref="DB.PokemonLearnset"/> row to a real <see cref="Attack"/>. The caller resolves
/// and filters these to the active generation (the learnset row carries the generation tag), so this type is
/// generation-agnostic. Lives on the permanent half of <see cref="Creature"/>; only the player populates it.
/// </summary>
public sealed record LearnsetMove(int Level, Attack Move);
