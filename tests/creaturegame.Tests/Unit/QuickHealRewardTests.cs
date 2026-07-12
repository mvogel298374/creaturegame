using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The "quick heal" reward option (<see cref="HealRewardOption"/>) — a potion-style heal applied on the spot by
/// <see cref="RewardResolution.ApplyHeal"/>. The quirk under test: it is <em>adaptive and bounded</em> — it
/// restores only the components the option carries (HP / status / PP), HP caps at MaxHP, and it emits the same
/// gen-invariant events as item use (<see cref="Healed"/> / <see cref="StatusCleared"/> / <see cref="PpRestored"/>)
/// so the client renders it like a potion/status-cure. See <c>ENCOUNTER_DESIGN.md §3.1</c> / <c>GAME_LOOP.md §5</c>.
/// </summary>
public class QuickHealRewardTests
{
    private static Creature Wounded()
    {
        var creature = new Creature("Healme") { Level = 20 };
        creature.CalculateStats();
        creature.AddAttack(
            new Attack
            {
                Id = 1,
                Name = "Tackle",
                PowerPointsMax = 35,
            }
        );
        creature.AddAttack(
            new Attack
            {
                Id = 2,
                Name = "Ember",
                PowerPointsMax = 25,
            }
        );
        // Wound it: HP down, one move drained, and a persisting major status.
        creature.Attributes.HP = 1;
        creature.MoveSet[0].PowerPointsCurrent = 2;
        creature.Battle.Status = StatusCondition.BadPoison;
        creature.Battle.ToxicCounter = 5;
        return creature;
    }

    [Fact]
    public void ApplyHeal_RestoresHp_CuresStatus_AndTopsLowPp_WhenAllComponentsSet()
    {
        var creature = Wounded();
        int max = creature.Attributes.MaxHP;
        var emitter = new RecordingEmitter();

        RewardResolution.ApplyHeal(
            new HealRewardOption(
                HpRestore: max,
                CureStatus: true,
                RestoreLowPp: true,
                Label: "Quick Heal"
            ),
            creature,
            emitter
        );

        Assert.Equal(max, creature.Attributes.HP); // HP restored, capped at MaxHP
        Assert.Equal(StatusCondition.None, creature.Battle.Status); // status cured
        Assert.Equal(35, creature.MoveSet[0].PowerPointsCurrent); // low move topped to max
        Assert.Equal(25, creature.MoveSet[1].PowerPointsCurrent); // already-full move stays full

        Assert.Single(emitter.Of<Healed>());
        Assert.Single(emitter.Of<StatusCleared>());
        Assert.Single(emitter.Of<PpRestored>()); // only the one low move fired a PP event
    }

    [Fact]
    public void ApplyHeal_HpNeverExceedsMissing_AndCapsAtMax()
    {
        var creature = Wounded();
        int max = creature.Attributes.MaxHP;
        creature.Attributes.HP = max - 5; // only 5 missing

        RewardResolution.ApplyHeal(
            new HealRewardOption(
                HpRestore: 9999,
                CureStatus: false,
                RestoreLowPp: false,
                Label: "Quick Heal"
            ),
            creature,
            new RecordingEmitter()
        );

        Assert.Equal(max, creature.Attributes.HP); // clamped, never overheals
    }

    [Fact]
    public void ApplyHeal_OnlyTouchesTheComponentsTheOptionCarries()
    {
        var creature = Wounded();
        var emitter = new RecordingEmitter();

        // HP-only heal: status and PP must be left alone.
        RewardResolution.ApplyHeal(
            new HealRewardOption(
                HpRestore: 10,
                CureStatus: false,
                RestoreLowPp: false,
                Label: "Quick Heal"
            ),
            creature,
            emitter
        );

        Assert.Equal(StatusCondition.BadPoison, creature.Battle.Status); // untouched
        Assert.Equal(2, creature.MoveSet[0].PowerPointsCurrent); // untouched
        Assert.Empty(emitter.Of<StatusCleared>());
        Assert.Empty(emitter.Of<PpRestored>());
    }

    [Fact]
    public void PlayerCondition_From_SnapshotsHp_Status_AndLowestPpFraction()
    {
        var creature = Wounded(); // HP 1/max, Tackle 2/35, Ember 25/25, BadPoison
        int max = creature.Attributes.MaxHP;

        var condition = PlayerCondition.From(creature);

        Assert.Equal(1, condition.CurrentHp);
        Assert.Equal(max, condition.MaxHp);
        Assert.True(condition.HasStatus);
        // Minimum current/max PP ratio across moves — the drained Tackle (2/35), not the full Ember (25/25).
        Assert.Equal(2.0 / 35.0, condition.LowestPpFraction, precision: 5);
    }

    [Fact]
    public void PlayerCondition_From_HealthyCreature_ReportsFullPp_NoStatus_FullHp()
    {
        var creature = new Creature("Fit") { Level = 20 };
        creature.CalculateStats();
        creature.AddAttack(
            new Attack
            {
                Id = 1,
                Name = "Tackle",
                PowerPointsMax = 35,
            }
        );

        var condition = PlayerCondition.From(creature);

        Assert.Equal(creature.Attributes.MaxHP, condition.CurrentHp);
        Assert.False(condition.HasStatus);
        Assert.Equal(1.0, condition.LowestPpFraction); // every move full
    }

    [Fact]
    public void PlayerCondition_From_NoMoves_ReportsFullPpFraction()
    {
        var creature = new Creature("Moveless") { Level = 20 };
        creature.CalculateStats();

        // No moves → the "lowest PP" is vacuously full (1.0), so an empty moveset never triggers a PP heal.
        Assert.Equal(1.0, PlayerCondition.From(creature).LowestPpFraction);
    }

    [Fact]
    public async Task OfferAndApplyAsync_WithHealOption_HealsPlayer_AndAnnouncesLabelWithNoGold()
    {
        // End-to-end through the reward-resolution dispatch (not just ApplyHeal): the HealRewardOption case heals
        // the run's player creature and closes with a RewardGranted carrying the heal label and zero gold.
        var creature = Wounded();
        int max = creature.Attributes.MaxHP;
        var emitter = new RecordingEmitter();
        var ctx = new RunContext(
            new RunState(creature),
            emitter,
            new ScriptedInput(), // ChooseRewardAsync defaults to option 0
            new SeededRandomSource(1)
        );
        var choice = new RewardChoice([
            new HealRewardOption(max, CureStatus: true, RestoreLowPp: true, Label: "Quick Heal"),
        ]);

        await RewardResolution.OfferAndApplyAsync(
            choice,
            "Battle",
            wallet: null,
            playerBag: null,
            ctx
        );

        Assert.Equal(max, creature.Attributes.HP);
        Assert.Equal(StatusCondition.None, creature.Battle.Status);
        var granted = Assert.Single(emitter.Of<RewardGranted>());
        Assert.Equal(0, granted.Gold);
        Assert.Contains("Quick Heal", granted.ItemNames);
    }
}
