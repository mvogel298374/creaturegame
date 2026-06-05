using creaturegame.Combat;

namespace creaturegame.Tests.TestSupport;

/// <summary>
/// Captures every emitted <see cref="BattleEvent"/> in order for assertion. Shared across the
/// test suites (replaces the per-file copies). Pair with <c>ConsoleBattleEventEmitter</c> when
/// you also want visible output.
/// </summary>
public sealed class RecordingEmitter : IBattleEventEmitter
{
    private readonly List<BattleEvent> _events = [];

    public IReadOnlyList<BattleEvent> Events => _events;

    public void Emit(BattleEvent evt) => _events.Add(evt);

    public IEnumerable<T> Of<T>()
        where T : BattleEvent => _events.OfType<T>();
}
