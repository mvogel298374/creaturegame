using creaturegame.Attacks;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Shared base for the Gen 1 attack "effect contract" tests. Each derived class groups the
/// contracts for <b>one capability</b> (deals damage, applies a secondary status, charges over two
/// turns, …) — classes are organised by <i>what the move does</i>, not by the import batch a move
/// happened to arrive in. When a new batch of move IDs is covered, its rows are added as
/// <see cref="InlineDataAttribute"/> on the matching capability class; a new class is created only
/// when a move introduces a genuinely new mechanic.
/// <para>
/// Every contract runs the <b>real, imported</b> move row from the live <c>moves.db</c>
/// (<see cref="MovesFixture"/>) through the real engine (<see cref="MoveScenario"/> →
/// <c>AttackAction.ExecuteAsync</c>). The only things substituted are RNG-gated rolls, via the
/// <see cref="IBattleRules"/> seam doubles in <see cref="DelegatingBattleRules"/> — never the
/// mechanic under test.
/// </para>
/// </summary>
public abstract class Gen1MoveContract(MovesFixture moves)
{
    /// <summary>The real, imported move row by its PokeAPI name (e.g. "fire-punch").</summary>
    protected Attack Move(string name) => moves.Get(name);
}
