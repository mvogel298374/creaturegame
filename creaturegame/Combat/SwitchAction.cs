using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// A turn spent voluntarily switching the active creature out for a benched party member (In-Combat Switching).
/// The switch analogue of <see cref="AttackAction"/> / <see cref="ItemAction"/>: it carries the highest turn
/// priority so it always resolves before either side's move (Gen 1 switching goes before even an item use), and
/// delegates the actual send-in to <see cref="Battle"/> (which owns the active-creature reassignment and the
/// shared forced/voluntary send-in machinery).
/// <para>The pick is validated at build time in <see cref="Battle.BuildPlayerActionAsync"/> (in range, alive,
/// not the already-active member, not trapped by a partial-trap bind), so by the time this executes the index is
/// known-good.</para>
/// </summary>
public sealed class SwitchAction : IBattleAction
{
    /// <summary>Above <see cref="ItemAction.ItemPriority"/> (6) so a switch always resolves first — Gen 1
    /// switching precedes even an item, and must beat the enemy's move regardless of speed or move priority.</summary>
    public const int SwitchPriority = 7;

    private readonly Battle _battle;
    private readonly int _partyIndex;

    public SwitchAction(Battle battle, Creature source, int partyIndex)
    {
        _battle = battle;
        Source = source;
        _partyIndex = partyIndex;
    }

    /// <summary>The outgoing creature — the one leaving the field. Read by Battle's turn loop for the alive gate
    /// (a voluntary switcher is always alive), never as a move target.</summary>
    public Creature Source { get; }

    public int Priority => SwitchPriority;

    public Task ExecuteAsync()
    {
        _battle.PerformVoluntarySwitch(_partyIndex);
        return Task.CompletedTask;
    }
}
