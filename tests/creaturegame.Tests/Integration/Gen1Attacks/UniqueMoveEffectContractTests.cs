using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Moves whose effect is singular — it doesn't (yet) form a family with other moves. Each gets its
/// own focused contract here until a second move of the same kind justifies promoting it to its own
/// capability class.
/// <list type="bullet">
/// <item><b>Pay Day</b> — deals normal damage and scatters coins = multiplier × user level.</item>
/// <item><b>Roar / Whirlwind</b> — end a wild battle in Gen 1: the target is scared off and flees
/// (<c>MoveEffect.ForceFlee</c>). Pinned below via the real imported rows. <b>Teleport</b> (player flee /
/// switch) still has no home with one creature, so it stays a deliberate announced-but-harmless no-op; all
/// three keep their Gen 1 −6 priority.</item>
/// <item><b>Haze / Metronome / Self-Destruct</b> — engine mechanics unit-tested in CoreMechanicsTests;
/// here the <i>real imported rows</i> are driven through <c>AttackAction</c> to prove the mapping works.</item>
/// </list>
/// </summary>
[Collection(MovesCollection.Name)]
public class UniqueMoveEffectContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task PayDayScattersCoinsEqualToMultiplierTimesLevel()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("pay-day"));

        var coins = result.First<CoinsScattered>();
        Assert.NotNull(coins);
        Assert.Equal(Gen1BattleRules.Instance.PayDayCoinMultiplier * 50, coins!.Amount);
        Assert.True(result.Has<DamageDealt>(), "Pay Day also deals damage");
    }

    // Teleport: still no home with a single creature (player flee / switch), so it stays a deliberate no-op —
    // announced, harmless, PP spent. Roar/Whirlwind moved to the flee contract below now they end a battle.
    [Theory]
    [InlineData("teleport")]
    public async Task SwitchMoveIsAnnouncedButHasNoCombatEffect(string moveName)
    {
        var move = Move(moveName);
        var result = await new MoveScenario().Defender(TestCreatures.Make("D", hp: 500)).Use(move);

        Assert.True(result.Has<MoveUsed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(result.Defender.Attributes.MaxHP, result.Defender.Attributes.HP);
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
        Assert.False(result.Defender.Battle.HasFled); // teleport is still a no-op (no flee mapped)
        Assert.Equal(move.PowerPointsMax - 1, result.Move.PowerPointsCurrent);
    }

    // Roar / Whirlwind (real imported rows): in a wild battle the target is scared off — it's flagged to flee
    // (the Battle loop ends the encounter on it; see ForceFleeTests). No damage, PP spent. Pins the DB row →
    // MoveEffect.ForceFlee mapping + the effect, driven through the real AttackAction.
    [Theory]
    [InlineData("roar")]
    [InlineData("whirlwind")]
    public async Task RoarAndWhirlwindScareTheTargetIntoFleeing(string moveName)
    {
        var move = Move(moveName);
        var result = await new MoveScenario().Defender(TestCreatures.Make("D", hp: 500)).Use(move);

        Assert.True(result.Has<MoveUsed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.True(result.Defender.Battle.HasFled); // scared off — the wild battle will end on this
        // Roar/Whirlwind only scare the foe off — they inflict NO status (TryApplyStatus runs before the
        // effect, so a DB row that resolved a non-None StatusEffect would leak a status onto the fleeing foe).
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
        Assert.Equal(move.PowerPointsMax - 1, result.Move.PowerPointsCurrent);
    }

    [Theory]
    [InlineData("whirlwind")]
    [InlineData("roar")]
    [InlineData("teleport")]
    public void SwitchMoveHasGen1NegativePriority(string moveName) =>
        Assert.Equal(-6, Move(moveName).Priority);

    // Haze (real imported row) clears every stat stage on both battlers. The escalation math is unit-
    // tested in CoreMechanicsTests; this proves the imported move drives it through AttackAction.
    [Fact]
    public async Task HazeClearsStatStagesOnBothBattlers()
    {
        var attacker = TestCreatures.Make("A");
        attacker.Battle.Stages.RaiseAttack(2);
        var defender = TestCreatures.Make("D", hp: 500);
        defender.Battle.Stages.RaiseDefense(2);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("haze"));

        Assert.Equal(0, result.Attacker.Battle.Stages.Attack);
        Assert.Equal(0, result.Defender.Battle.Stages.Defense);
        Assert.True(result.Has<HazeClearedStages>());
    }

    [Fact]
    public async Task HazeCuringSleepStillForfeitsTheTargetsSameTurnAction()
    {
        // Gen 1: a Haze that cures a Sleep/Frozen target doesn't let it act the instant it wakes —
        // the already-chosen move is still forfeited for that turn (pokered marks the move register
        // invalid rather than re-checking status). Drives the real imported "haze" row through
        // AttackAction (proving HazeEffect -> ResetForHaze sets the flag), then checks the production
        // StatusResolver.CanAct gate the real Battle turn loop calls next for the other side's action.
        var attacker = TestCreatures.Make("A");
        var defender = TestCreatures.Make("D", hp: 500);
        defender.Battle.Status = StatusCondition.Sleep;
        defender.Battle.SleepTurns = 3;

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("haze"));

        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status); // Haze cured it
        Assert.False(StatusResolver.CanAct(result.Defender)); // still forfeits this turn's action
        Assert.True(StatusResolver.CanAct(result.Defender)); // free to act again next turn
    }

    [Fact]
    public async Task HazeSuppressionDoesNotLeakIntoTheNextTurnWhenTheTargetIsFaster()
    {
        // Regression: when the TARGET is faster than the Haze user, the target already resolves its
        // own legitimate blocked Sleep turn (CanAct's ordinary Sleep branch) before Haze fires later
        // that same turn — so HazeSuppressedStatus gets set too late for this turn's own CanAct call
        // to consume it. Gen 1's move-invalidation write is turn-scoped (the next turn's fresh move
        // selection overwrites it, unread), so the target must be free to act NEXT turn, not forfeit a
        // second turn it never should. Drives a full Battle turn loop, not just AttackAction/CanAct in
        // isolation, since the bug is specifically about state surviving a turn boundary.
        //
        // StartFightAsync() calls ResetBattleState() on both creatures before turn 1, which would wipe
        // a Sleep status set directly on either Creature beforehand — so the sleepy, faster creature is
        // seeded via the `player` role's playerEntryStatus (the one path Battle applies AFTER its own
        // reset), and the Haze user is the (slower) `enemy` role.
        var sleepyAndFaster = TestCreatures.Make("Player", speed: 200);
        sleepyAndFaster.AddAttack(Move("tackle"));

        var hazeUser = TestCreatures.Make("Enemy", hp: 500, speed: 100);
        hazeUser.AddAttack(Move("haze"));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            sleepyAndFaster,
            hazeUser,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: AlwaysHitRules.Instance,
            emitter: emitter,
            playerEntryStatus: new CarriedStatus(StatusCondition.Sleep, 3)
        );
        await battle.StartFightAsync();

        // Exactly one legitimate block (Player's own natural Sleep turn, turn 1) — never a second,
        // bogus one from a HazeSuppressedStatus that leaked past the turn boundary.
        int playerSleepBlocks = emitter
            .Events.OfType<ActionBlocked>()
            .Count(b => b.CreatureName == "Player" && b.Reason == StatusCondition.Sleep);
        Assert.Equal(1, playerSleepBlocks);
        Assert.Contains(emitter.Events, e => e is MoveUsed m && m.AttackerName == "Player");
    }

    // Metronome (real imported row) calls a move from the pool; a single-move pool makes it deterministic.
    [Fact]
    public async Task MetronomeCallsAMoveFromThePool()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .MovePool(Move("tackle"))
            .Use(Move("metronome"));

        Assert.Contains(result.Events, e => e is MoveUsed m && m.MoveName == "tackle");
        Assert.True(result.Has<DamageDealt>(), "the called move deals damage");
    }

    // Self-Destruct and Explosion (real imported rows, DamageCategory.SelfDestruct) damage the foe and
    // faint the user — Explosion is the higher-power sibling sharing the same category.
    [Theory]
    [InlineData("self-destruct")]
    [InlineData("explosion")]
    public async Task SelfDestructMoveDamagesTheFoeAndFaintsTheUser(string moveName)
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>());
        Assert.True(result.Defender.Attributes.HP < 500);
        Assert.False(
            result.Attacker.IsAlive(),
            "the user faints from a Self-Destruct-category move"
        );
    }

    // Splash (real imported row) is the Gen 1 no-op: it announces "But nothing happened!" and leaves
    // both battlers exactly as they were — no damage, no status, no stat change. The dedicated event
    // is what distinguishes a faithfully-wired no-op from an unimplemented move.
    [Fact]
    public async Task SplashDoesNothingButAnnouncesIt()
    {
        var attacker = TestCreatures.Make("A");
        attacker.Battle.Stages.RaiseAttack(2);
        var defender = TestCreatures.Make("D", hp: 500);
        defender.Battle.Stages.RaiseDefense(2);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("splash"));

        Assert.True(result.Has<ButNothingHappened>());
        Assert.False(result.Has<DamageDealt>(), "Splash deals no damage");
        // Nothing about either battler changed.
        Assert.Equal(result.Attacker.Attributes.MaxHP, result.Attacker.Attributes.HP);
        Assert.Equal(result.Defender.Attributes.MaxHP, result.Defender.Attributes.HP);
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
        Assert.Equal(2, result.Attacker.Battle.Stages.Attack);
        Assert.Equal(2, result.Defender.Battle.Stages.Defense);
    }
}
