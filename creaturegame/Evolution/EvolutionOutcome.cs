using creaturegame.Creatures;
using creaturegame.DB;

namespace creaturegame.Evolution;

/// <summary>
/// A resolved evolution ready for the run loop to apply: the evolved <see cref="NewForm"/> species and the
/// <see cref="NewLearnset"/> to seat on the creature afterwards (so the evolved form's level-up moves are
/// the ones consulted from then on).
/// <para>
/// Produced by the web/data layer — it owns the DB query (evolution edges → <see cref="IEvolutionRules"/>
/// decision → evolved species + learnset) so the core <see cref="creaturegame.Combat.BattleRunner"/> stays
/// generation- and data-agnostic, exactly like the enemy supplier. The runner just applies it.
/// </para>
/// </summary>
public sealed record EvolutionOutcome(
    PokemonSpecies NewForm,
    IReadOnlyList<LearnsetMove> NewLearnset
);
