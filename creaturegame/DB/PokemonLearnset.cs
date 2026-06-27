using System.ComponentModel.DataAnnotations;

namespace creaturegame.DB;

/// <summary>
/// How a species learns a move. Gen 1 has two channels we model: <see cref="LevelUp"/> (the canonical
/// level-up learnset) and <see cref="Machine"/> (TM/HM). Stored as an int; <see cref="LevelUp"/> is 0 so a
/// schema migration defaults pre-existing (level-up-only) rows correctly.
/// </summary>
public enum LearnMethod
{
    LevelUp = 0,
    Machine = 1,
}

/// <summary>
/// One move a species can learn, tagged by generation and <see cref="LearnMethod"/>. Lives in
/// <c>pokemon.db</c> (Pokémon-world data) alongside <see cref="PokemonSpecies"/>.
/// <para>
/// <see cref="MoveId"/> is a <i>logical</i> reference to <c>moves.db</c>'s <c>Moves.Id</c>
/// — the two-database split means it can't be an enforced cross-DB foreign key, exactly
/// like the engine already treats move references.
/// </para>
/// <para>
/// <see cref="Generation"/> keeps generations separated in one table: every row is 1
/// today; a future generation seeds its own rows and the runtime query filters by the
/// active generation (see <c>EncounterFactory.ActiveGeneration</c>).
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

    /// <summary>
    /// The level at which the move is learned (PokeAPI <c>level_learned_at</c>; 1 = known from the start).
    /// 0 for <see cref="LearnMethod.Machine"/> rows (TM/HM moves aren't tied to a level).
    /// </summary>
    public int LearnLevel { get; set; }

    /// <summary>
    /// How the move is learned. Defaults to <see cref="LearnMethod.LevelUp"/> so the migration that adds this
    /// column tags every pre-existing row correctly. Level-up paths (base moveset selection, level-up move
    /// learning) must filter to <see cref="LearnMethod.LevelUp"/>; the TM/HM moveset tiers also read Machine.
    /// </summary>
    public LearnMethod Method { get; set; } = LearnMethod.LevelUp;

    /// <summary>The generation this learnset entry belongs to. Gen 1 today.</summary>
    public int Generation { get; set; } = 1;
}
