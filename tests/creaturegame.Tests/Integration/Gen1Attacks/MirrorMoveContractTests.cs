using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>Mirror Move copying the foe's last announced move; GENERATION_SEAMS.md §2 (Mirror Move).</summary>
[Collection(MovesCollection.Name)]
public class MirrorMoveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task MirrorMoveReExecutesTheFoesLastMove()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.Battle.LastMoveUsed = Move("tackle"); // the foe's last move

        var result = await new MoveScenario().Defender(defender).Use(Move("mirror-move"));

        Assert.True(result.Has<DamageDealt>(), "the copied Tackle deals damage to the foe");
        Assert.Contains(result.Events, e => e is MoveUsed m && m.MoveName == "tackle");
    }

    [Fact]
    public async Task MirrorMoveFailsWhenTheFoeHasNotMovedYet()
    {
        var defender = TestCreatures.Make("D", hp: 500); // no LastMoveUsed

        var result = await new MoveScenario().Defender(defender).Use(Move("mirror-move"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task AFailedMirrorMoveStaysTheUsersLastMoveSoTheFoesMirrorMoveFails()
    {
        // The enemy's turn-2 Mirror Move fails on the fresh switch-in; the send-out erase is off to isolate that.
        var lead = TestCreatures.Make("Lead", hp: 9999, speed: 1);
        lead.AddAttack(Move("tackle"));
        var bench = TestCreatures.Make("Bench", hp: 9999, attack: 999, speed: 300);
        bench.AddAttack(Move("mirror-move"));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make("Enemy", hp: 300, speed: 200);
        enemy.AddAttack(Move("tackle"));
        enemy.AddAttack(Move("mirror-move"));

        var log = await FullBattle.Fight(
            lead,
            enemy,
            new ScriptedInput("tackle", "mirror-move").TurnPlan(null, 1),
            new ScriptedInput("tackle", "mirror-move", "tackle"),
            new SendOutRules(clearsBoth: false),
            playerParty: party
        );

        Assert.Single(log.Of<MoveMissed>(), m => m.AttackerName == "Enemy");
        Assert.Single(log.Of<MoveMissed>(), m => m.AttackerName == "Bench");
        Assert.False(enemy.IsAlive());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MirrorMoveCopiesTheFoesStruggleUnlessTheSeamSaysNo(bool gen1)
    {
        var player = TestCreatures.Make("Player", hp: 9999, speed: 1);
        player.AddAttack(Move("mirror-move"));
        var enemy = TestCreatures.Make("Enemy", hp: 150, speed: 200);
        enemy.AddAttack(Move("tackle"));
        enemy.MoveSet[0].PowerPointsCurrent = 0;

        var log = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("mirror-move"),
            new ScriptedInput("tackle"),
            gen1 ? null : new StruggleNotCopyableRules()
        );

        Assert.Equal(
            gen1,
            log.Of<MoveUsed>().Any(m => m.AttackerName == "Player" && m.MoveName == "Struggle")
        );
        Assert.Equal(gen1, log.Of<RecoilDamage>().Any(r => r.SourceName == "Player"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSeamDecidesWhetherASendOutErasesTheFoesLastMove(bool clearsBoth)
    {
        // The enemy is fully paralysed from its second attempt, so it never moves after the switch.
        var lead = TestCreatures.Make("Lead", hp: 9999, speed: 300);
        lead.AddAttack(Move("thunder-wave"));
        var bench = TestCreatures.Make("Bench", hp: 9999, attack: 999, speed: 300);
        bench.AddAttack(Move("mirror-move"));
        bench.AddAttack(Move("strength"));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make("Enemy", hp: 500, speed: 200);
        enemy.AddAttack(Move("tackle"));

        var log = await FullBattle.Fight(
            lead,
            enemy,
            new ScriptedInput("thunder-wave", "mirror-move", "strength").TurnPlan(null, 1),
            new ScriptedInput("tackle"),
            new SendOutRules(clearsBoth),
            playerParty: party
        );

        bool benchCopiedTackle = log.Of<MoveUsed>()
            .Any(m => m.AttackerName == "Bench" && m.MoveName == "tackle");
        Assert.Equal(!clearsBoth, benchCopiedTackle);
        Assert.Equal(
            clearsBoth,
            log.Of<MoveMissed>().Any(m => m.AttackerName == "Bench" && m.MoveName == "mirror-move")
        );
    }

    private sealed class StruggleNotCopyableRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override bool MirrorMoveCanCopy(Attack lastMove) =>
            lastMove.Name != "Struggle" && base.MirrorMoveCanCopy(lastMove);
    }

    private sealed class SendOutRules(bool clearsBoth) : DelegatingBattleRules
    {
        private int _paralysisChecks;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0;

        public override bool RollsFullParalysis(IRandomSource rng) => ++_paralysisChecks >= 2;

        public override bool SendOutClearsBothLastMoves => clearsBoth;
    }
}
