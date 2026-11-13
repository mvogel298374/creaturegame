using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for Gen 1 binding moves (Wrap, Bind, Clamp, Fire Spin). Gen 1 partial trapping locks the
/// BINDER into re-using the move every turn — dealing its damage — while the trapped foe can't act ("neither
/// the user nor the target will be able to select moves") and takes NO residual chip. So a faster binder traps
/// the foe from turn 1, grinds it down with the move alone, never switches to another move, and never lets the
/// foe act. (Source: Bulbapedia, Bind — Generation I effect.)
/// </summary>
[Collection(MovesCollection.Name)]
public class BindingInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    [Fact]
    public async Task WrapLocksTheBinderIntoTheMoveAndTrapsTheFoeSoItNeverActs()
    {
        // The binder is faster and knows Wrap + Tackle, and is SCRIPTED to use Tackle after the first Wrap.
        // The Gen 1 lock must override that script — the binder is forced to keep Wrapping — so Tackle never
        // appears. The foe is tuned to die from Wrap alone (no residual chip exists) before the 5-turn bind ends.
        var player = Mon(
            "Player",
            hp: 999,
            attack: 200,
            speed: 200,
            DamageType.Normal,
            "wrap",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 60, attack: 80, speed: 1, DamageType.Normal, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap", "tackle") // 2nd choice is Tackle — the lock overrides it
            .EnemyUses("tackle")
            .Rules(new ScriptableRules().Deterministic().BindingTurns(5))
            .RunAsync();

        // The foe was trapped from the start...
        Assert.Contains(result.All<BindingStarted>(), b => b.TargetName == "Enemy");
        // ...blocked every turn it would have acted, and never got a move off.
        Assert.True(result.Count<BindingBlocked>() >= 2);
        Assert.DoesNotContain(result.All<MoveUsed>(), m => m.AttackerName == "Enemy");

        // The binder was LOCKED into Wrap: it Wrapped on multiple turns and never used the scripted Tackle.
        var playerMoves = result.All<MoveUsed>().Where(m => m.AttackerName == "Player").ToList();
        Assert.Equal(["wrap"], playerMoves.Select(m => m.MoveName));
        Assert.Contains(result.All<BindingContinues>(), b => b.CreatureName == "Player");

        Assert.Equal("Player", result.Winner);
    }

    [Fact]
    public async Task AWrapThatFailsItsAccuracyRollTrapsNoOneAndLeavesItsUserFree()
    {
        var player = Mon(
            "Player",
            hp: 300,
            attack: 50,
            speed: 200,
            DamageType.Normal,
            "wrap",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 999, attack: 60, speed: 1, DamageType.Normal, "swift");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap", "tackle")
            .EnemyUses("swift")
            .Rules(NeverHitRules.Instance)
            .RunAsync();

        AssertMissedWrapLeftBothSidesFree(result, foeMove: "swift");
    }

    [Fact]
    public async Task AWrapThatMissesAnUndergroundFoeTrapsNoOneAndLeavesItsUserFree()
    {
        var player = Mon(
            "Player",
            hp: 999,
            attack: 200,
            speed: 1,
            DamageType.Normal,
            "wrap",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 60, attack: 20, speed: 200, DamageType.Normal, "dig");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap", "tackle")
            .EnemyUses("dig")
            .Rules(new ScriptableRules().Deterministic())
            .RunAsync();

        Assert.Contains(result.All<MoveMissed>(), m => m.AttackerName == "Player");
        AssertMissedWrapLeftBothSidesFree(result, foeMove: "dig");
    }

    [Fact]
    public async Task ContinuationTurnsSkipTheHitTestAndReplayTheFirstStrikesDamageCritIncluded()
    {
        var player = Mon("Player", hp: 150, attack: 100, speed: 200, DamageType.Normal, "wrap");
        var enemy = Mon("Enemy", hp: 999, attack: 100, speed: 1, DamageType.Normal, "swift");
        var rules = new FirstStrikeOnlyRules(bindingTurns: 4);

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap")
            .EnemyUses("swift")
            .Rules(rules)
            .RunAsync();

        var lockHits = result.DamageTo("Enemy").Take(4).ToList();
        Assert.Equal(4, lockHits.Count);
        Assert.True(lockHits[0].IsCrit);
        Assert.All(lockHits, h => Assert.Equal(lockHits[0].Damage, h.Damage));
        Assert.All(lockHits.Skip(1), h => Assert.False(h.IsCrit));
        Assert.Equal(3, result.Count<BindingContinues>());

        var wrap = result.Player.MoveSet.Single(m => m.Base.Name == "wrap");
        int wrapsAnnounced = result
            .All<MoveUsed>()
            .Count(m => m.AttackerName == "Player" && m.MoveName == "wrap");
        Assert.Equal(wrapsAnnounced, wrap.Base.PowerPointsMax - wrap.PowerPointsCurrent);
    }

    [Fact]
    public async Task WrapOnAGhostWhoseAccuracyRollPassesLocksBothSidesInForNoDamage()
    {
        var player = Mon(
            "Player",
            hp: 999,
            attack: 100,
            speed: 200,
            DamageType.Normal,
            "wrap",
            "ember"
        );
        var enemy = Mon("Enemy", hp: 60, attack: 20, speed: 1, DamageType.Ghost, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap", "ember")
            .EnemyUses("tackle")
            .Rules(new ScriptableRules().Deterministic().BindingTurns(3))
            .RunAsync();

        AssertTypeMissLockedBothSidesInForNoDamage(result);
        Assert.Contains(result.All<MoveHadNoEffect>(), e => e.TargetName == "Enemy");
    }

    [Fact]
    public async Task WrapOnAGhostWhoseAccuracyRollFailsLocksNoOne()
    {
        var player = Mon(
            "Player",
            hp: 300,
            attack: 100,
            speed: 200,
            DamageType.Normal,
            "wrap",
            "ember"
        );
        var enemy = Mon("Enemy", hp: 999, attack: 60, speed: 1, DamageType.Ghost, "swift");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap", "ember")
            .EnemyUses("swift")
            .Rules(NeverHitRules.Instance)
            .RunAsync();

        Assert.Contains(result.All<MoveHadNoEffect>(), e => e.TargetName == "Enemy");
        AssertMissedWrapLeftBothSidesFree(result, foeMove: "swift", binderSecondMove: "ember");
    }

    [Fact]
    public async Task AFireSpinTheTypeMultiplierRoundsToZeroStillLocksBothSidesIn()
    {
        var player = TestCreatures.Make("Player", level: 2, hp: 999, attack: 5, special: 5);
        player.AddAttack(Move("fire-spin"));
        player.AddAttack(Move("tackle"));
        var enemy = TestCreatures.Make(
            "Enemy",
            type1: DamageType.Rock,
            type2: DamageType.Water,
            hp: 5,
            attack: 5,
            defense: 100,
            special: 500,
            speed: 1
        );
        enemy.AddAttack(Move("tackle"));

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("fire-spin", "tackle")
            .EnemyUses("tackle")
            .Rules(new ScriptableRules().Deterministic().BindingTurns(3))
            .RunAsync();

        AssertTypeMissLockedBothSidesInForNoDamage(result);
        Assert.Contains(
            result.All<MoveMissed>(),
            m => m.AttackerName == "Player" && m.MoveName == "fire-spin"
        );
    }

    [Fact]
    public async Task ASubstituteTakesTheReplayedHitAndTheStoredDamageCarriesPastTheBreak()
    {
        var player = Mon("Player", hp: 999, attack: 100, speed: 1, DamageType.Normal, "wrap");
        var enemy = Mon(
            "Enemy",
            hp: 80,
            attack: 20,
            speed: 200,
            DamageType.Normal,
            "substitute",
            "tackle"
        );
        var rules = new FirstStrikeOnlyRules(bindingTurns: 4, critFirst: false, missLater: false);

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap")
            .EnemyUses("substitute", "tackle")
            .Rules(rules)
            .RunAsync();

        int decoyHp = result.First<SubstituteCreated>()!.SubstituteHp;
        var soaks = result
            .All<SubstituteAbsorbedHit>()
            .Where(s => s.CreatureName == "Enemy")
            .ToList();
        int stored = decoyHp - soaks[0].SubstituteHpAfter;
        Assert.Equal(2, soaks.Count);
        Assert.Equal(Math.Max(0, soaks[0].SubstituteHpAfter - stored), soaks[1].SubstituteHpAfter);
        Assert.Contains(result.All<SubstituteBroke>(), b => b.CreatureName == "Enemy");
        var hitsAfterBreak = result.DamageTo("Enemy").Take(2).ToList();
        Assert.Equal(2, hitsAfterBreak.Count);
        Assert.All(hitsAfterBreak, h => Assert.Equal(stored, h.Damage));
    }

    [Fact]
    public async Task AFirstStrikeThatBreaksASubstituteStillLocksBothSidesIn()
    {
        var player = Mon("Player", hp: 999, attack: 150, speed: 1, DamageType.Normal, "wrap");
        var enemy = Mon(
            "Enemy",
            hp: 40,
            attack: 20,
            speed: 200,
            DamageType.Normal,
            "substitute",
            "tackle"
        );
        var rules = new FirstStrikeOnlyRules(bindingTurns: 3, critFirst: false, missLater: false);

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap")
            .EnemyUses("substitute", "tackle")
            .Rules(rules)
            .RunAsync();

        Assert.Equal(0, Assert.Single(result.All<SubstituteAbsorbedHit>()).SubstituteHpAfter);
        Assert.Contains(result.All<SubstituteBroke>(), b => b.CreatureName == "Enemy");
        Assert.Contains(result.All<BindingStarted>(), b => b.TargetName == "Enemy");
        Assert.Contains(result.All<BindingBlocked>(), b => b.CreatureName == "Enemy");
        Assert.Single(result.All<MoveUsed>(), m => m.AttackerName == "Player");
        var lockHits = result.DamageTo("Enemy").Take(2).ToList();
        Assert.Equal(2, lockHits.Count);
        Assert.Equal(lockHits[0].Damage, lockHits[1].Damage);
        Assert.True(lockHits[0].Damage > result.First<SubstituteCreated>()!.SubstituteHp);
    }

    [Fact]
    public async Task TheRulesCanRerunEveryBindingTurnInFull()
    {
        var player = Mon("Player", hp: 999, attack: 200, speed: 200, DamageType.Normal, "wrap");
        var enemy = Mon("Enemy", hp: 60, attack: 80, speed: 1, DamageType.Normal, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap")
            .EnemyUses("tackle")
            .Rules(new NoReplayRules())
            .RunAsync();

        Assert.Empty(result.All<BindingContinues>());
        Assert.True(
            result.All<MoveUsed>().Count(m => m.AttackerName == "Player" && m.MoveName == "wrap")
                >= 2
        );
    }

    [Fact]
    public async Task TheRulesCanLetATypeMissFreeTheBinder()
    {
        var player = Mon(
            "Player",
            hp: 999,
            attack: 100,
            speed: 200,
            DamageType.Normal,
            "wrap",
            "ember"
        );
        var enemy = Mon("Enemy", hp: 60, attack: 20, speed: 1, DamageType.Ghost, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap", "ember")
            .EnemyUses("tackle")
            .Rules(new NoTypeMissLockRules())
            .RunAsync();

        Assert.Contains(result.All<MoveHadNoEffect>(), e => e.TargetName == "Enemy");
        AssertMissedWrapLeftBothSidesFree(result, foeMove: "tackle", binderSecondMove: "ember");
    }

    private static void AssertTypeMissLockedBothSidesInForNoDamage(BattleScenarioResult result)
    {
        Assert.Contains(result.All<BindingStarted>(), b => b.TargetName == "Enemy");
        int continues = result.All<BindingContinues>().Count(b => b.CreatureName == "Player");
        Assert.True(continues >= 1);
        Assert.Equal(continues, result.DamageTo("Enemy").Count(h => h.Damage == 0));
        Assert.Equal(continues + 1, result.Count<BindingBlocked>());
    }

    private static void AssertMissedWrapLeftBothSidesFree(
        BattleScenarioResult result,
        string foeMove,
        string binderSecondMove = "tackle"
    )
    {
        Assert.Empty(result.All<BindingStarted>());
        Assert.Empty(result.All<BindingBlocked>());
        Assert.Contains(
            result.All<MoveUsed>(),
            m => m.AttackerName == "Enemy" && m.MoveName == foeMove
        );
        var playerMoves = result
            .All<MoveUsed>()
            .Where(m => m.AttackerName == "Player")
            .Select(m => m.MoveName)
            .Take(2);
        Assert.Equal(["wrap", binderSecondMove], playerMoves);
    }

    // The first strike hits (and crits, at full variance); every later roll would miss, not crit, and roll low.
    private sealed class FirstStrikeOnlyRules(
        int bindingTurns,
        bool critFirst = true,
        bool missLater = true
    ) : DelegatingBattleRules
    {
        private int _hitCalls;
        private int _critCalls;
        private int _varianceCalls;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) =>
            _hitCalls++ == 0 || !missLater ? 256 : 0;

        public override double GetCritChance(Creature a, Attack m) =>
            _critCalls++ == 0 && critFirst ? 1.0 : 0.0;

        public override double RollDamageVariance() => _varianceCalls++ == 0 ? 1.0 : 217 / 255.0;

        public override int RollBindingTurns() => bindingTurns;
    }

    private sealed class NoReplayRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override bool BindingContinuationReplaysDamage => false;
    }

    private sealed class NoTypeMissLockRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override bool BindingLockSurvivesTypeMiss => false;
    }
}
