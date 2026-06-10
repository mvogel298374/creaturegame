using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.Integration.Interactions;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// Level-up move learning: when a win levels the player into a move on its learnset, Battle either auto-adds
/// it (a free slot) or — with four moves already — emits a blocking <see cref="MoveReplacementRequired"/> and
/// applies the player's choice (forget a slot, or decline). Driven through the real moves DB +
/// <see cref="BattleScenario"/>, the same harness as the XP/level-up flow tests this builds on.
/// </summary>
[Collection(MovesCollection.Name)]
public class LearnsetLevelUpTests(MovesFixture moves) : InteractionTest(moves)
{
    // A level-5 player with a free slot that levels past a learnset entry auto-learns the move: one
    // MoveLearned (carrying the move name), the move is in the moveset, and no replacement is ever required.
    [Fact]
    public async Task Learnset_LevelUp_AddsNewMoveWhenSlotAvailable()
    {
        var ember = Move("ember");
        var player = LevelFiveBruiser("tackle"); // one move → three free slots
        player.Learnset = [new LearnsetMove(6, ember)];

        var result = await WinAndLevelUp(player);

        // Free slot → auto-learn at level 6, exactly once, with no prompt.
        Assert.False(result.Has<MoveReplacementRequired>());
        var learned = Assert.Single(result.All<MoveLearned>());
        Assert.Equal("Player", learned.CreatureName);
        Assert.Equal(ember.Name, learned.MoveName);
        Assert.Contains(player.MoveSet, m => m.Base.Id == ember.Id);
    }

    // A level-5 player whose four slots are full emits MoveReplacementRequired when it levels into a new move.
    // With the default (decline) input the moveset is untouched and a MoveLearnDeclined closes the prompt.
    [Fact]
    public async Task Learnset_LevelUp_EmitsMoveReplacementRequired_WhenFull()
    {
        var ember = Move("ember");
        var player = LevelFiveBruiser("tackle", "growl", "tail-whip", "scratch"); // four slots full
        player.Learnset = [new LearnsetMove(6, ember)];

        // No PlayerForgetsSlot(...) → the ScriptedInput declines (returns null), the canonical "don't learn".
        var result = await WinAndLevelUp(player);

        var prompt = Assert.Single(result.All<MoveReplacementRequired>());
        Assert.Equal("Player", prompt.CreatureName);
        Assert.Equal(ember.Name, prompt.NewMoveName);
        Assert.Equal(new[] { "tackle", "growl", "tail-whip", "scratch" }, prompt.CurrentMoves);

        // Declined: the prompt is closed by MoveLearnDeclined, nothing is learned or forgotten.
        Assert.Single(result.All<MoveLearnDeclined>());
        Assert.False(result.Has<MoveLearned>());
        Assert.False(result.Has<MoveForgotten>());
        Assert.DoesNotContain(player.MoveSet, m => m.Base.Id == ember.Id);
        Assert.Equal(4, player.MoveSet.Count);
    }

    // When the player picks a slot to forget, that move is replaced by the new one: MoveForgotten (old) then
    // MoveLearned (new), and the chosen slot now holds the learned move with full PP.
    [Fact]
    public async Task Learnset_LevelUp_ReplacesChosenSlot_WhenPlayerForgetsAMove()
    {
        var ember = Move("ember");
        var player = LevelFiveBruiser("tackle", "growl", "tail-whip", "scratch");
        player.Learnset = [new LearnsetMove(6, ember)];

        // Forget slot 1 (growl) to make room for ember.
        var result = await new BattleScenario()
            .Player(player)
            .Enemy(FaintableXpEnemy())
            .PlayerUses("tackle")
            .EnemyUses("splash")
            .PlayerForgetsSlot(1)
            .RunAsync();

        Assert.Single(result.All<MoveReplacementRequired>());
        var forgotten = Assert.Single(result.All<MoveForgotten>());
        Assert.Equal("growl", forgotten.MoveName);
        var learned = Assert.Single(result.All<MoveLearned>());
        Assert.Equal(ember.Name, learned.MoveName);

        // Slot 1 now holds ember (full PP); the other three are untouched.
        Assert.Equal(ember.Id, player.MoveSet[1].Base.Id);
        Assert.Equal(ember.PowerPointsMax, player.MoveSet[1].PowerPointsCurrent);
        Assert.Equal(4, player.MoveSet.Count);
        Assert.DoesNotContain(player.MoveSet, m => m.Base.Name == "growl");
    }

    // Cross-cut with Transform: a move learned on the winning level-up must survive Transform's
    // end-of-battle identity restore. The player Transforms into the enemy (copying its moveset), KOs it
    // with a copied move, then levels into a learnset move. The learn must act on the RESTORED original
    // moveset — not the transient transformed copy that RestoreOriginalIdentity discards at battle end —
    // or the learned move is silently lost. Guards the win-branch "restore identity, then learn" order.
    [Fact]
    public async Task Learnset_LevelUp_AfterTransform_PersistsLearnedMoveOntoOriginalMoveset()
    {
        var ember = Move("ember");
        var player = new Creature("Player")
        {
            Level = 5,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        player.CalculateStats();
        player.Experience = player.CalculateExperienceForLevel(5);
        player.Attributes.HP = player.Attributes.MaxHP = 500;
        player.Attributes.Attack = 60;
        player.Attributes.Defense = 100; // pin so the +1-priority copied move's enemy can't OHKO first
        player.Attributes.Speed = 250; // outspeed so Transform lands turn 1
        player.AddAttack(Move("transform"));
        player.Learnset = [new LearnsetMove(6, ember)];

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Water };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 6;
        enemy.Attributes.Attack = 200;
        enemy.Attributes.Defense = 10;
        enemy.Attributes.Speed = 1;
        enemy.SpeciesBaseExperience = 100; // the win awards enough XP to cross level 6
        enemy.AddAttack(Move("quick-attack")); // the priority move the player copies and KOs with

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: NoVarianceNoCritHitRules.Instance,
            emitter: emitter,
            rng: new SeededRandomSource(0)
        );
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.Level >= 6, "the win must carry the player past the learnset level");
        Assert.NotNull(emitter.Of<TransformedInto>().FirstOrDefault());

        // Ember was learned onto the restored original moveset; the copied Quick Attack is gone with the
        // revert, and the Transform snapshot is cleared.
        Assert.Contains(player.MoveSet, m => m.Base.Id == ember.Id);
        Assert.DoesNotContain(player.MoveSet, m => m.Base.Name == "quick-attack");
        Assert.Null(player.Battle.OriginalIdentity);
    }

    // A level-5 one-shotter sitting exactly on its level floor, fast and strong enough to win cleanly.
    private Creature LevelFiveBruiser(params string[] moveNames)
    {
        var c = new Creature("Player")
        {
            Level = 5,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(5);
        c.Attributes.MaxHP = 999;
        c.Attributes.HP = 999;
        c.Attributes.Attack = 999;
        c.Attributes.Speed = 200;
        foreach (var n in moveNames)
            c.AddAttack(Move(n));
        return c;
    }

    // A 1-HP enemy with real base XP at level 30 → ~857 XP, enough to carry the level-5 player past level 6
    // (so a learnset entry at level 6 fires). Uses Splash so it never threatens the player.
    private Creature FaintableXpEnemy()
    {
        var enemy = Mon("Enemy", hp: 1, attack: 1, speed: 1, DamageType.Normal, "splash");
        enemy.Level = 30;
        enemy.SpeciesBaseExperience = 200;
        return enemy;
    }

    private async Task<BattleScenarioResult> WinAndLevelUp(Creature player)
    {
        var result = await new BattleScenario()
            .Player(player)
            .Enemy(FaintableXpEnemy())
            .PlayerUses("tackle")
            .EnemyUses("splash")
            .RunAsync();
        Assert.Equal("Player", result.Winner);
        Assert.True(
            player.Level >= 6,
            "the XP award must carry the player past the learnset level"
        );
        return result;
    }
}
