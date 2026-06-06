using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Rest (Gen 1): the user fully restores HP, cures any major status, then forces itself asleep for a
/// fixed number of turns (<see cref="IBattleRules.RestSleepTurns"/>, distinct from the random sleep a
/// status move inflicts). It fails if the user is already at full HP. The forced sleep is consumed by
/// the normal turn-loop sleep handling, so the last test drives a full <see cref="Battle"/> to prove
/// the Rest user actually skips turns rather than just carrying a counter.
/// </summary>
[Collection(MovesCollection.Name)]
public class RestContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task HealsToFullAndFallsAsleepForTheFixedDuration()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Attributes.ReceiveDamage(150); // down to 50/200

        var result = await new MoveScenario().Attacker(user).Use(Move("rest"));

        Assert.False(result.Has<DamageDealt>(), "Rest is a status move — no damage");
        Assert.Equal(user.Attributes.MaxHP, user.Attributes.HP); // fully healed
        Assert.Equal(StatusCondition.Sleep, result.Attacker.Status);
        // The sleep is self-inflicted — Rest must NOT put the foe to sleep (TryApplyStatus runs first).
        Assert.Equal(StatusCondition.None, result.Defender.Status);
        Assert.Equal(Gen1BattleRules.Instance.RestSleepTurns, result.Attacker.SleepTurns);

        var healed = result.First<Healed>();
        Assert.NotNull(healed);
        Assert.Equal(150, healed!.HealAmount); // the *actual* amount restored, not MaxHP
        Assert.Contains(
            result.Events,
            e => e is StatusApplied s && s.Status == StatusCondition.Sleep
        );
    }

    [Fact]
    public async Task FailsWhenAlreadyAtFullHp()
    {
        var user = TestCreatures.Make("A", hp: 200); // starts at full HP

        var result = await new MoveScenario().Attacker(user).Use(Move("rest"));

        Assert.True(result.Has<MoveMissed>(), "Rest fails (state precondition) at full HP");
        Assert.False(result.Has<Healed>());
        Assert.Equal(StatusCondition.None, result.Attacker.Status); // didn't put itself to sleep
        Assert.Equal(user.Attributes.MaxHP, user.Attributes.HP);
    }

    [Fact]
    public async Task CuresAnExistingMajorStatus()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Attributes.ReceiveDamage(80);
        user.Status = StatusCondition.Poison; // pre-existing status Rest should clear

        var result = await new MoveScenario().Attacker(user).Use(Move("rest"));

        // Poison is overwritten by Rest's own Sleep — the Gen 1 "cures status" behaviour.
        Assert.Equal(StatusCondition.Sleep, result.Attacker.Status);
        Assert.Equal(user.Attributes.MaxHP, user.Attributes.HP);
    }

    // Full-Battle proof that Rest's sleep is actually enforced across the turn loop: a Rest-only user,
    // started below full HP, Rests on turn 1 and is then forced to skip turns (ActionBlocked: Sleep)
    // while the foe keeps hitting. The foe out-damages a single Rest heal over the sleep window, so the
    // battle terminates with the user fainting — guaranteeing the user is asleep (and blocked) for at
    // least the turn after Rest, never getting another action off.
    [Fact]
    public async Task RestUserIsForcedToSkipTurnsWhileAsleep()
    {
        var player = TestCreatures.Make("Player", hp: 120, speed: 200);
        player.Attributes.ReceiveDamage(80); // 40/120 so Rest succeeds on turn 1
        player.AddAttack(Move("rest")); // only move ⇒ AutoSelect always picks Rest

        var enemy = TestCreatures.Make("Enemy", attack: 200, speed: 1);
        enemy.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 60,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: AlwaysHitRules.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        // Rest fired: healed + put itself to sleep.
        Assert.Contains(emitter.Events, e => e is Healed h && h.CreatureName == "Player");
        Assert.Contains(
            emitter.Events,
            e => e is StatusApplied s && s.Status == StatusCondition.Sleep
        );
        // The forced sleep was consumed in the loop — the player lost a turn to it.
        Assert.Contains(
            emitter.Events,
            e =>
                e is ActionBlocked a
                && a.CreatureName == "Player"
                && a.Reason == StatusCondition.Sleep
        );
        // The player never got a second action off (only ever used Rest, then slept until it fainted).
        Assert.DoesNotContain(
            emitter.Events,
            e => e is MoveUsed m && m.AttackerName == "Player" && m.MoveName != "rest"
        );
        // Rest's sleep is self-inflicted — the enemy must never be put to sleep by it.
        Assert.NotEqual(StatusCondition.Sleep, enemy.Status);
        Assert.DoesNotContain(
            emitter.Events,
            e =>
                e is StatusApplied s && s.TargetName == "Enemy" && s.Status == StatusCondition.Sleep
        );
        Assert.False(player.IsAlive());
    }
}
