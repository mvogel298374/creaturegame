using System.ComponentModel.DataAnnotations;

namespace creaturegame.DB;

/// <summary>
/// One level-up move a species can learn, tagged by generation. Lives in
/// <c>pokemon.db</c> (Pokémon-world data) alongside <see cref="PokemonSpecies"/>.
/// <para>
/// <see cref="MoveId"/> is a <i>logical</i> reference to <c>moves.db</c>'s <c>Moves.Id</c>
/// — the two-database split means it can't be an enforced cross-DB foreign key, exactly
/// like the engine already treats move references.
/// </para>
/// <para>
/// <see cref="Generation"/> keeps generations separated in one table: every row is 1
/// today; a future generation seeds its own rows and the runtime query filters by the
/// active generation (see <c>GameController.ActiveGeneration</c>).
/// </para>
/// </summary>
public class PokemonLearnset
{
    [Key]
    public int Id { get; set; }

    /// <summary>FK → <see cref="PokemonSpecies.Id"/> (same database, enforced).</summary>
    public int SpeciesId { get; set; }

    /// <summary>Logical reference to <c>moves.db</c> <c>Moves.Id</c> (cross-DB, not enforced).</summary>
    public int MoveId { get; set; }

    /// <summary>The level at which the move is learned (PokeAPI <c>level_learned_at</c>; 1 = known from the start).</summary>
    public int LearnLevel { get; set; }

    /// <summary>The generation this learnset entry belongs to. Gen 1 today.</summary>
    public int Generation { get; set; } = 1;
}
