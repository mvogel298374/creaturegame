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
        Assert.Equal(cost + 1, result.Attacker.Battle.SubstituteHp); // decoy has the cost +1 (Gen 1)
        Assert.Equal(200 - cost, result.Attacker.Attributes.HP); // user paid exactly the cost
        Assert.False(result.Has<DamageDealt>(), "Substitute deals no damage to the foe");
    }

    [Fact]
    public async Task DecoyAbsorbsAHitAndTheUserTakesNothing()
    {
        var defender = TestCreatures.Make("D", hp: 300, defense: 100);
        defender.Battle.SubstituteHp = 200; // plenty to survive one weak hit
        var attacker = TestCreatures.Make("A", attack: 80);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("tackle"));

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<DamageDealt>(), "the user behind the decoy takes no damage");
        Assert.Equal(300, result.Defender.Attributes.HP); // HP untouched
        Assert.InRange(result.Defender.Battle.SubstituteHp, 1, 199); // decoy soaked the hit but didn't break
    }

    // The decoy soaks every damage category, not just the Standard path — Seismic Toss (level-based,
    // bypasses the normal damage calc) must hit the decoy too, or the shared absorb helper has a leak.
    [Fact]
    public async Task DecoyAbsorbsNonStandardDamageCategories()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.SubstituteHp = 200;
        var attacker = TestCreatures.Make("A", level: 50); // Seismic Toss deals 50 (= level)

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("seismic-toss"));

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(300, result.Defender.Attributes.HP); // user untouched
        Assert.Equal(150, result.Defender.Battle.SubstituteHp); // decoy took the 50
    }

    [Fact]
    public async Task DecoyBreaksWhenDepletedAndOverflowIsLost()
    {
        var defender = TestCreatures.Make("D", hp: 300, defense: 100);
        defender.Battle.SubstituteHp = 1; // about to break; the incoming hit dwarfs it
        var attacker = TestCreatures.Make("A", attack: 150);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("tackle"));

        Assert.True(result.Has<SubstituteFaded>());
        Assert.Equal(0, result.Defender.Battle.SubstituteHp);
        // Gen 1: the overflow past the decoy's HP is NOT applied to the user.
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(300, result.Defender.Attributes.HP);
    }

    [Fact]
    public async Task FailsWhenASubstituteIsAlreadyUp()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Battle.SubstituteHp = 51;

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<SubstitutePutUp>());
        Assert.Equal(51, result.Attacker.Battle.SubstituteHp); // unchanged
        Assert.Equal(200, result.Attacker.Attributes.HP); // no extra HP paid
    }

    [Fact]
    public async Task FailsWhenHpTooLowToPayTheCost()
    {
        var user = TestCreatures.Make("A", hp: 200); // cost is 50
        user.Attributes.ReceiveDamage(160); // down to 40 (< cost)

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(0, result.Attacker.Battle.SubstituteHp); // no decoy raised
        Assert.Equal(40, result.Attacker.Attributes.HP); // HP unchanged
    }

    // pokered substitute.asm fails only on an underflow, so paying the user's last HP still raises the decoy.
    [Fact]
    public async Task SucceedsAtExactlyTheCostAndLeavesTheUserAtZeroHp()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Attributes.ReceiveDamage(150); // down to 50 (= cost)

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.True(result.Has<SubstitutePutUp>());
        Assert.Equal(51, result.Attacker.Battle.SubstituteHp);
        Assert.Equal(0, result.Attacker.Attributes.HP);
        Assert.False(result.Attacker.IsAlive());
    }

    [Fact]
    public async Task ShieldsTheUserFromTheFoesStatus()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move("thunder-wave")); // would paralyze, but the decoy shields it

        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
        Assert.False(result.Has<StatusApplied>());
    }

    [Fact]
    public async Task ShieldsTheUserFromTheFoesStatDrop()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move("growl")); // −Attack

        Assert.Equal(0, result.Defender.Battle.Stages.Attack);
        Assert.False(result.Has<StatStageChanged>());
    }

    [Fact]
    public async Task ShieldsTheUserFromTheFoesConfusion()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move("supersonic"));

        Assert.Equal(0, result.Defender.Battle.ConfusedTurns);
        Assert.False(result.Has<ConfusionStarted>());
    }

    // Gen 1: the shield holds even on the exact hit that breaks the decoy — a damaging move's
    // secondary status struck the substitute, so it doesn't reach the user, despite the decoy shattering
    // that turn. (Pinned because the shield is snapshotted at impact, before absorption zeroes the sub.)
    [Fact]
    public async Task SecondaryStatusIsBlockedEvenOnTheHitThatBreaksTheDecoy()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 300);
        defender.Battle.SubstituteHp = 1; // the incoming Body Slam shatters it this hit

        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance) // forces the paralysis chance to "land"
            .Defender(defender)
            .Use(Move("body-slam"));

        Assert.True(result.Has<SubstituteFaded>(), "the decoy broke on this hit");
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status); // …but the paralysis was shielded
        Assert.False(result.Has<StatusApplied>());
    }

    // Full-Battle proof: the decoy persists across turns, soaks the enemy's real attacks on later
    // turns, and protects the user's HP until it breaks. The player only knows Substitute (re-using it
    // while one is up just fails), so the enemy whittles the decoy then the user, terminating the battle.
    [Fact]
    public async Task DecoyPersistsAcrossTurnsAndProtectsHpUntilItBreaks()
    {
        // Deterministic damage (no miss, no crit, no variance) keeps the battle short and reproducible:
        // HP 210 (cost 52) leaves 2 HP after the fourth decoy, so the user can't afford a fifth and takes a real
        // hit; at an exact multiple it would pay its last HP and faint instead (Gen 1 exact-cost rule).
        var player = TestCreatures.Make("Player", hp: 210, speed: 200, defense: 80);
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

    private static Attack Tackle(int power) =>
        new()
        {
            Name = "Tackle",
            BaseDamage = power,
            Accuracy = 100,
            DamageType = DamageType.Normal,
            AttackType = AttackType.Physical,
        };

    // pokered core.asm:424-447 runs the faint handler right after the self-KO, before the foe acts.
    [Fact]
    public async Task PlayerSubstituteAtExactlyTheCostFaintsTheLeadAndTheBenchComesIn()
    {
        var lead = TestCreatures.Make("Lead", hp: 200, speed: 200);
        lead.Attributes.ReceiveDamage(150); // HP 50 = cost
        lead.AddAttack(Move("substitute"));
        var bench = TestCreatures.Make("Bench", hp: 300, attack: 999, speed: 150);
        bench.AddAttack(Tackle(120));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make("Enemy", type1: DamageType.Water, hp: 60, speed: 1);
        enemy.AddAttack(Tackle(50));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: NoVarianceNoCritHitRules.Instance,
            emitter: emitter,
            playerParty: party
        );
        await battle.StartFightAsync();

        Assert.Contains(emitter.Of<SubstitutePutUp>(), e => e.CreatureName == "Lead");
        Assert.Contains(emitter.Of<CreatureFainted>(), e => e.Name == "Lead");
        Assert.DoesNotContain(emitter.Of<DamageDealt>(), d => d.TargetName == "Lead");
        Assert.Equal("Bench", Assert.Single(emitter.Of<CreatureSwitchedIn>()).Name);
        Assert.True(battle.PlayerWon);
    }

    [Fact]
    public async Task EnemySubstituteAtExactlyTheCostFaintsTheFoeAndThePlayerWins()
    {
        var player = TestCreatures.Make("Player", hp: 200, speed: 1);
        player.AddAttack(Tackle(50));
        var enemy = TestCreatures.Make("Enemy", hp: 200, speed: 200);
        enemy.Attributes.ReceiveDamage(150); // HP 50 = cost
        enemy.AddAttack(Move("substitute"));

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

        Assert.True(battle.PlayerWon);
        Assert.Contains(emitter.Of<CreatureFainted>(), e => e.Name == "Enemy");
        Assert.Empty(emitter.Of<DamageDealt>());
        Assert.NotEmpty(emitter.Of<ExperienceGained>());
    }
}
