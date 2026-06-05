using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Disable locks one of the target's moves out of selection for several turns. The move-level
/// contract proves it picks a real foe move, deals no damage, and won't stack onto an already-
/// disabled target; the full-<see cref="Battle"/> test proves the lock is actually <i>enforced</i>
/// at move-selection time (the user can't pick it and Struggles when it's their only move) and that
/// the move is re-enabled when the counter runs out.
/// </summary>
[Collection(MovesCollection.Name)]
public class DisableContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task DisableLocksAFoeMoveWithoutDealingDamage()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("pound"));
        defender.AddAttack(Move("scratch"));

        var result = await new MoveScenario().Defender(defender).Use(Move("disable"));

        Assert.False(result.Has<DamageDealt>(), "Disable deals no damage");
        var disabled = result.First<MoveDisabled>();
        Assert.NotNull(disabled);
        Assert.Equal("D", disabled!.TargetName);
        Assert.True(result.Defender.DisableTurnsRemaining > 0);
        Assert.NotNull(result.Defender.DisabledMove);
        Assert.Contains(result.Defender.MoveSet, m => m == result.Defender.DisabledMove);
    }

    [Fact]
    public async Task DisableDoesNotStackWhenAMoveIsAlreadyDisabled()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("pound"));
        defender.AddAttack(Move("scratch"));
        defender.DisabledMove = defender.MoveSet[0];
        defender.DisableTurnsRemaining = 3;

        var result = await new MoveScenario().Defender(defender).Use(Move("disable"));

        Assert.DoesNotContain(result.Events, e => e is MoveDisabled);
        Assert.Equal(3, result.Defender.DisableTurnsRemaining); // unchanged
        Assert.Same(defender.MoveSet[0], result.Defender.DisabledMove); // same locked move
    }

    [Fact]
    public async Task DisabledMoveCannotBeSelectedAndIsReEnabledWhenTheLockExpires()
    {
        // Player knows only Pound, so once Disable locks it the player has no other move and must
        // Struggle until it re-enables. The enemy is faster and Disables; its later Disables fail
        // (the move's already locked). A fixed 2-turn lock + always-hit makes the timeline exact:
        //   turn 1 — player Pounds (chosen before Disable lands); enemy Disables Pound.
        //   turn 2 — Pound is locked → player Struggles; end of turn the lock expires (re-enabled).
        // The enemy never attacks, so the player (huge HP) outlasts the enemy, which dies to the
        // accumulating Pound/Struggle damage — the loop is guaranteed to terminate.
        var player = TestCreatures.Make("Player", hp: 99999, attack: 150, speed: 1);
        player.AddAttack(Move("pound"));

        var enemy = TestCreatures.Make("Enemy", hp: 1500, defense: 60, speed: 200);
        enemy.AddAttack(Move("disable"));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: new FixedDisableRules(2),
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        Assert.Contains(
            emitter.Events,
            e => e is MoveDisabled d && d.TargetName == "Player" && d.MoveName == "pound"
        );
        Assert.Contains(
            emitter.Events,
            e => e is MoveReEnabled r && r.CreatureName == "Player" && r.MoveName == "pound"
        );
        // Enforcement: while Pound was locked the player had no other move, so it Struggled.
        Assert.Contains(
            emitter.Events,
            e => e is MoveUsed m && m.AttackerName == "Player" && m.MoveName == "Struggle"
        );
    }

    /// <summary>Always hits and locks Disable for a fixed number of turns — deterministic timeline.</summary>
    private sealed class FixedDisableRules(int turns) : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256; // always hit

        public override int RollDisableTurns() => turns;
    }
}
