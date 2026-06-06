using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Substitute (Gen 1): the user spends floor(maxHP/4) HP to raise a decoy with floor(maxHP/4)+1 HP.
/// While it stands, the foe's hits strike the decoy (the user takes nothing, and overflow damage is
/// lost), and the foe's status / stat-drops / confusion are shielded. It fails if a Substitute is
/// already up or the user can't pay the HP cost. The decoy persists across turns, so the last test
/// drives a full <see cref="Battle"/> to prove it soaks real enemy attacks on later turns and protects
/// the user's HP until it breaks.
/// </summary>
[Collection(MovesCollection.Name)]
public class SubstituteContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task PutsUpADecoyForAQuarterOfMaxHp()
    {
        var user = TestCreatures.Make("A", hp: 200);

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        int cost = 200 / 4; // 50
        Assert.True(result.Has<SubstitutePutUp>());
        Assert.Equal(cost + 1, result.Attacker.SubstituteHp); // decoy has the cost +1 (Gen 1)
        Assert.Equal(200 - cost, result.Attacker.Attributes.HP); // user paid exactly the cost
        Assert.False(result.Has<DamageDealt>(), "Substitute deals no damage to the foe");
    }

    [Fact]
    public async Task DecoyAbsorbsAHitAndTheUserTakesNothing()
    {
        var defender = TestCreatures.Make("D", hp: 300, defense: 100);
        defender.SubstituteHp = 200; // plenty to survive one weak hit
        var attacker = TestCreatures.Make("A", attack: 80);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("tackle"));

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<DamageDealt>(), "the user behind the decoy takes no damage");
        Assert.Equal(300, result.Defender.Attributes.HP); // HP untouched
        Assert.InRange(result.Defender.SubstituteHp, 1, 199); // decoy soaked the hit but didn't break
    }

    // The decoy soaks every damage category, not just the Standard path — Seismic Toss (level-based,
    // bypasses the normal damage calc) must hit the decoy too, or the shared absorb helper has a leak.
    [Fact]
    public async Task DecoyAbsorbsNonStandardDamageCategories()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.SubstituteHp = 200;
        var attacker = TestCreatures.Make("A", level: 50); // Seismic Toss deals 50 (= level)

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("seismic-toss"));

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(300, result.Defender.Attributes.HP); // user untouched
        Assert.Equal(150, result.Defender.SubstituteHp); // decoy took the 50
    }

    [Fact]
    public async Task DecoyBreaksWhenDepletedAndOverflowIsLost()
    {
        var defender = TestCreatures.Make("D", hp: 300, defense: 100);
        defender.SubstituteHp = 1; // about to break; the incoming hit dwarfs it
        var attacker = TestCreatures.Make("A", attack: 150);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("tackle"));

        Assert.True(result.Has<SubstituteFaded>());
        Assert.Equal(0, result.Defender.SubstituteHp);
        // Gen 1: the overflow past the decoy's HP is NOT applied to the user.
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(300, result.Defender.Attributes.HP);
    }

    [Fact]
    public async Task FailsWhenASubstituteIsAlreadyUp()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.SubstituteHp = 51;

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<SubstitutePutUp>());
        Assert.Equal(51, result.Attacker.SubstituteHp); // unchanged
        Assert.Equal(200, result.Attacker.Attributes.HP); // no extra HP paid
    }

    [Fact]
    public async Task FailsWhenHpTooLowToPayTheCost()
    {
        var user = TestCreatures.Make("A", hp: 200); // cost is 50
        user.Attributes.ReceiveDamage(160); // down to 40 (≤ cost)

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(0, result.Attacker.SubstituteHp); // no decoy raised
        Assert.Equal(40, result.Attacker.Attributes.HP); // HP unchanged
    }

    [Fact]
    public async Task ShieldsTheUserFromTheFoesStatus()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move("thunder-wave")); // would paralyze, but the decoy shields it

        Assert.Equal(StatusCondition.None, result.Defender.Status);
        Assert.False(result.Has<StatusApplied>());
    }

    [Fact]
    public async Task ShieldsTheUserFromTheFoesStatDrop()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move("growl")); // −Attack

        Assert.Equal(0, result.Defender.Stages.Attack);
        Assert.False(result.Has<StatStageChanged>());
    }

    [Fact]
    public async Task ShieldsTheUserFromTheFoesConfusion()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move("supersonic"));

        Assert.Equal(0, result.Defender.ConfusedTurns);
        Assert.False(result.Has<ConfusionStarted>());
    }

    // Gen 1: the shield holds even on the exact hit that breaks the decoy — a damaging move's
    // secondary status struck the substitute, so it doesn't reach the user, despite the decoy shattering
    // that turn. (Pinned because the shield is snapshotted at impact, before absorption zeroes the sub.)
    [Fact]
    public async Task SecondaryStatusIsBlockedEvenOnTheHitThatBreaksTheDecoy()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 300);
        defender.SubstituteHp = 1; // the incoming Body Slam shatters it this hit

        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance) // forces the paralysis chance to "land"
            .Defender(defender)
            .Use(Move("body-slam"));

        Assert.True(result.Has<SubstituteFaded>(), "the decoy broke on this hit");
        Assert.Equal(StatusCondition.None, result.Defender.Status); // …but the paralysis was shielded
        Assert.False(result.Has<StatusApplied>());
    }

    // Full-Battle proof: the decoy persists across turns, soaks the enemy's real attacks on later
    // turns, and protects the user's HP until it breaks. The player only knows Substitute (re-using it
    // while one is up just fails), so the enemy whittles the decoy then the user, terminating the battle.
    [Fact]
    public async Task DecoyPersistsAcrossTurnsAndProtectsHpUntilItBreaks()
    {
        // Deterministic damage (no miss, no crit, no variance) keeps the battle short and reproducible:
        // the player (only Substitute) raises a 51-HP decoy turn 1, the enemy soaks into it over the next
        // turns, it breaks, then the enemy whittles the player down — all within Substitute's 10 PP so the
        // player never falls back to Struggle. The enemy is Water-typed so its Normal Tackle gets no STAB.
        var player = TestCreatures.Make("Player", hp: 200, speed: 200, defense: 80);
        player.AddAttack(Move("substitute")); // only move ⇒ AutoSelect always picks it

        var enemy = TestCreatures.Make("Enemy", type1: DamageType.Water, attack: 110, speed: 1);
        enemy.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 50,
                Accuracy = 100,
                DamageType = DamageType.Normal,
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
            rules: NoVarianceNoCritHitRules.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        var events = emitter.Events.ToList();
        // The player re-raises the decoy each time it breaks (paying the cost again) until it can no
        // longer afford it — so a Substitute goes up at least once and soaks real enemy hits on later turns.
        Assert.Contains(events, e => e is SubstitutePutUp);
        Assert.Contains(events, e => e is SubstituteAbsorbedHit);

        int fadedIdx = events.FindIndex(e => e is SubstituteFaded);
        int playerHitIdx = events.FindIndex(e => e is DamageDealt d && d.TargetName == "Player");
        Assert.True(fadedIdx >= 0, "the decoy eventually broke");
        // The user only takes real HP damage once it can no longer keep a decoy up — never while one
        // stands (the decoy soaks everything). So the first hit on the user follows a faded decoy.
        Assert.True(
            playerHitIdx > fadedIdx,
            "the user took no real damage until the decoy broke (Gen 1)"
        );
        Assert.False(player.IsAlive());
    }
}
