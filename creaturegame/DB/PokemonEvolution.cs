using System.ComponentModel.DataAnnotations;
using creaturegame.Evolution;

namespace creaturegame.DB;

/// <summary>
/// One evolution edge: species <see cref="FromSpeciesId"/> evolves into <see cref="ToSpeciesId"/>
/// under <see cref="Trigger"/>. Lives in <c>pokemon.db</c> (Pokémon-world data) alongside
/// <see cref="PokemonSpecies"/> and <see cref="PokemonLearnset"/>.
/// <para>
/// The trigger is stored <b>faithfully</b> — a Gen 1 trade evolution is a <see cref="EvolutionTrigger.Trade"/>
/// row, not pre-converted to a level. How a generation/game-mode interprets it is the seam's job
/// (<see cref="Evolution.IEvolutionRules"/>): this game converts Trade → a level threshold, but the data
/// stays honest so a trade-capable mode or a later generation needs no re-import.
/// </para>
/// <para>
/// <see cref="Generation"/> keeps generations separated in one table (every row is 1 today), mirroring
/// <see cref="PokemonLearnset.Generation"/>; a future generation seeds its own rows and the runtime query
/// filters by the active generation.
/// </para>
/// </summary>
public class PokemonEvolution
{
    [Key]
    public int Id { get; set; }

    /// <summary>FK → <see cref="PokemonSpecies.Id"/> (same database, enforced) — the pre-evolution.</summary>
    public int FromSpeciesId { get; set; }

    /// <summary>The evolved form. A logical reference to <see cref="PokemonSpecies.Id"/> (same DB but not a
    /// second enforced FK, to avoid multiple cascade paths — the same modelling choice as cross-DB ids).</summary>
    public int ToSpeciesId { get; set; }

    /// <summary>The Gen 1 trigger, stored faithfully (see the type remarks).</summary>
    public EvolutionTrigger Trigger { get; set; }

    /// <summary>The level for a <see cref="EvolutionTrigger.Level"/> edge (PokeAPI <c>min_level</c>); null otherwise.</summary>
    public int? LevelThreshold { get; set; }

    /// <summary>The stone item for a <see cref="EvolutionTrigger.Stone"/> edge (logical PokeAPI item id,
    /// cross-referenced like <see cref="PokemonLearnset.MoveId"/>); null otherwise. Dormant until the bag exists.</summary>
    public int? StoneItemId { get; set; }

    /// <summary>The generation this edge belongs to. Gen 1 today.</summary>
    public int Generation { get; set; } = 1;
}
