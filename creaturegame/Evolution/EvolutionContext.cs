namespace creaturegame.Evolution;

/// <summary>
/// The trigger event that just occurred, handed to <see cref="IEvolutionRules.CheckEvolution"/> to
/// decide whether a creature evolves. A closed hierarchy — exactly one case per Gen 1 trigger shape.
/// <para>
/// The game loop currently only ever produces <see cref="LeveledTo"/> (evolution is checked between
/// battles after a level-up). <see cref="StoneUsed"/> and <see cref="Traded"/> exist so the seam is
/// already complete for when the item/bag layer lands — no caller produces them yet. This is the
/// "documented shape exists for later" pattern from <c>GENERATION_SEAMS.md §5.0</c>.
/// </para>
/// </summary>
public abstract record EvolutionContext
{
    private EvolutionContext() { }

    /// <summary>The creature just reached <paramref name="Level"/>. Drives Level evolutions (and, in
    /// this roguelite, the level-converted Trade evolutions — see <see cref="Gen1EvolutionRules"/>).</summary>
    public sealed record LeveledTo(int Level) : EvolutionContext;

    /// <summary>An evolution stone was used. <paramref name="StoneItemId"/> is the logical item id
    /// (PokeAPI item id, cross-referenced like <c>PokemonLearnset.MoveId</c>). Dormant until the bag exists.</summary>
    public sealed record StoneUsed(int StoneItemId) : EvolutionContext;

    /// <summary>The creature was traded. No caller produces this today (no trading in a single-player
    /// roguelite); kept so a future mode/generation can drive canonical trade evolutions.</summary>
    public sealed record Traded : EvolutionContext;
}
