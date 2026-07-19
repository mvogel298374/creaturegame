using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Stat-stage moves change a battler's stat stages and deal no damage. Swords Dance raises the
/// <b>user's</b> Attack by two stages (a self-targeting buff). Foe-targeting drops (Growl, Leer, …)
/// join this class as later batches add them.
/// </summary>
[Collection(MovesCollection.Name)]
public class StatStageMoveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task SwordsDanceRaisesUserAttackByTwoStages()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Use(Move("swords-dance"));

        Assert.Equal(2, result.Attacker.Battle.Stages.Attack);
        Assert.False(result.Has<DamageDealt>(), "Swords Dance is a status move — no damage");

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Attacker.Name, change!.CreatureName); // affects the user, not the foe
        Assert.Equal(2, change.Delta);
        Assert.Equal(2, change.NewStage);
    }

    [Fact]
    public async Task GrowthRaisesUserSpecialByOneStage()
    {
        // Gen 1 Growth raises the (combined) Special stat by one stage — not Attack, as modern data
        // reports. This pins the importer's Gen 1 override.
        var result = await new MoveScenario().Attacker(TestCreatures.Make("A")).Use(Move("growth"));

        Assert.Equal(1, result.Attacker.Battle.Stages.Special);
        Assert.False(result.Has<DamageDealt>(), "Growth is a status move — no damage");

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Attacker.Name, change!.CreatureName); // affects the user
        Assert.Equal("Special", change.Stat);
        Assert.Equal(1, change.Delta);
        Assert.Equal(1, change.NewStage);
    }

    [Fact]
    public async Task MeditateRaisesUserAttackByOneStage()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Use(Move("meditate"));

        Assert.Equal(1, result.Attacker.Battle.Stages.Attack);
        Assert.False(result.Has<DamageDealt>(), "Meditate is a status move — no damage");

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Attacker.Name, change!.CreatureName); // affects the user
        Assert.Equal("Attack", change.Stat);
        Assert.Equal(1, change.Delta);
        Assert.Equal(1, change.NewStage);
    }

    [Fact]
    public async Task AgilityRaisesUserSpeedByTwoStages()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Use(Move("agility"));

        Assert.Equal(2, result.Attacker.Battle.Stages.Speed);
        Assert.False(result.Has<DamageDealt>(), "Agility is a status move — no damage");

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Attacker.Name, change!.CreatureName); // affects the user
        Assert.Equal("Speed", change.Stat);
        Assert.Equal(2, change.Delta);
        Assert.Equal(2, change.NewStage);
    }

    // Self-targeting raises beyond the special cases above: Harden / Withdraw (+1 Defense),
    // Double Team (+1 Evasion), Minimize (+2 Evasion — first Evasion mover).
    [Theory]
    [InlineData("harden", "Defense", 1)]
    [InlineData("withdraw", "Defense", 1)]
    [InlineData("defense-curl", "Defense", 1)]
    [InlineData("double-team", "Evasion", 1)]
    [InlineData("minimize", "Evasion", 2)]
    [InlineData("barrier", "Defense", 2)]
    [InlineData("acid-armor", "Defense", 2)]
    [InlineData("amnesia", "Special", 2)] // Gen 1: +2 to the combined Special stat
    [InlineData("sharpen", "Attack", 1)]
    public async Task RaisesUserStatBySpecifiedStages(string moveName, string stat, int delta)
    {
        var result = await new MoveScenario().Attacker(TestCreatures.Make("A")).Use(Move(moveName));

        Assert.False(result.Has<DamageDealt>(), "a stat-stage move deals no damage");

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Attacker.Name, change!.CreatureName); // affects the user
        Assert.Equal(stat, change.Stat);
        Assert.Equal(delta, change.Delta);
        Assert.Equal(delta, change.NewStage);
    }

    [Fact]
    public async Task ScreechLowersFoeDefenseByTwoStages()
    {
        // Screech is the first −2 foe drop (the others above are −1).
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move("screech"));

        Assert.False(result.Has<DamageDealt>(), "Screech deals no damage");

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Defender.Name, change!.CreatureName);
        Assert.Equal("Defense", change.Stat);
        Assert.Equal(-2, change.Delta);
        Assert.Equal(-2, change.NewStage);
    }

    // Foe-targeting stat drops: Sand Attack / Smokescreen (−1 Accuracy), Tail Whip / Leer
    // (−1 Defense), Growl (−1 Attack).
    [Theory]
    [InlineData("sand-attack", "Accuracy")]
    [InlineData("smokescreen", "Accuracy")]
    [InlineData("tail-whip", "Defense")]
    [InlineData("leer", "Defense")]
    [InlineData("growl", "Attack")]
    [InlineData("string-shot", "Speed")] // Gen 1: −1 Speed (modern: −2)
    [InlineData("kinesis", "Accuracy")] // −1 foe Accuracy
    [InlineData("flash", "Accuracy")] // −1 foe Accuracy
    public async Task LowersFoeStatByOneStage(string moveName, string stat)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.False(result.Has<DamageDealt>(), "a pure stat move deals no damage");

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Defender.Name, change!.CreatureName); // affects the foe, not the user
        Assert.Equal(stat, change.Stat);
        Assert.Equal(-1, change.Delta);
        Assert.Equal(-1, change.NewStage);
    }

    // ── At the ±6 cap (Gen 1: "Nothing happened!" for a primary move, silent for a side effect) ──────────

    [Fact]
    public async Task PrimaryRaiseAtTheCap_SaysNothingHappened_NotAPhantomRise()
    {
        // A primary stat move on a stat that's already +6 doesn't move it — Gen 1 prints "Nothing happened!"
        // and does NOT announce a rise. (Old bug: it emitted a phantom StatStageChanged → "rose!".)
        var attacker = TestCreatures.Make("A");
        attacker.Battle.Stages.RaiseAttack(6); // already capped
        var result = await new MoveScenario().Attacker(attacker).Use(Move("swords-dance"));

        Assert.Equal(6, result.Attacker.Battle.Stages.Attack); // unchanged
        Assert.False(result.Has<StatStageChanged>(), "no phantom rose! at the cap");
        Assert.True(
            result.Has<ButNothingHappened>(),
            "Gen 1 prints 'Nothing happened!' at the cap"
        );
    }

    [Fact]
    public async Task PrimaryDropAtTheCap_SaysNothingHappened_NotAPhantomFall()
    {
        var defender = TestCreatures.Make("Defender", hp: 500);
        defender.Battle.Stages.RaiseAttack(-6); // already at the −6 floor
        var result = await new MoveScenario().Defender(defender).Use(Move("growl"));

        Assert.Equal(-6, result.Defender.Battle.Stages.Attack); // unchanged
        Assert.False(result.Has<StatStageChanged>(), "no phantom fell! at the cap");
        Assert.True(
            result.Has<ButNothingHappened>(),
            "Gen 1 prints 'Nothing happened!' at the cap"
        );
    }

    [Fact]
    public async Task SecondaryDropAtTheCap_FailsSilently_NoMessage()
    {
        // A side-effect stat drop riding a damaging move (Acid → −1 Defense) at the −6 floor: Gen 1 prints
        // NOTHING (the damage line already showed) — unlike a primary move. So neither StatStageChanged nor
        // ButNothingHappened, distinguishing the two cap paths (pokered: primary → text, side effect → ret nc).
        var defender = TestCreatures.Make("Defender", hp: 500);
        defender.Battle.Stages.RaiseDefense(-6);
        var result = await new MoveScenario()
            .Defender(defender)
            .Rules(ForceSecondaryRules.Instance)
            .Use(Move("acid"));

        Assert.True(result.Has<DamageDealt>(), "Acid is a damaging move — the hit still lands");
        Assert.Equal(-6, result.Defender.Battle.Stages.Defense); // unchanged
        Assert.False(result.Has<StatStageChanged>(), "no phantom fell! at the cap");
        Assert.False(
            result.Has<ButNothingHappened>(),
            "a side effect at the cap is silent in Gen 1"
        );
    }

    [Fact]
    public async Task RaiseThatStillMovesTheStage_NearTheCap_StillAnnounces()
    {
        // Regression guard: a +2 move at +5 clamps to +6 but DID move (by 1), so it still announces the rise
        // (Gen 1 shows the move's own +2 "sharply" text regardless of the clamp). Only a true no-op is suppressed.
        var attacker = TestCreatures.Make("A");
        attacker.Battle.Stages.RaiseAttack(5);
        var result = await new MoveScenario().Attacker(attacker).Use(Move("swords-dance"));

        Assert.Equal(6, result.Attacker.Battle.Stages.Attack);
        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(2, change!.Delta); // the move's amount, not the clamped delta
        Assert.Equal(6, change.NewStage);
        Assert.False(result.Has<ButNothingHappened>());
    }
}
