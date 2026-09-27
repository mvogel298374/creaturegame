using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The move-teach reward option (<see cref="MoveTeachRewardOption"/>) end-to-end through
/// <see cref="RewardResolution.OfferAndApplyAsync"/> — the apply path <c>RollMoveTeachOption</c>'s own tests
/// (<c>RewardCalculatorTests</c>, web layer) don't reach, since they only pin the roll. The quirk under test:
/// picking the option raises the real teach-target picker (<see cref="MoveTeachTargetRequired"/>), then routes
/// into the exact same free-slot-auto-learn / forget-a-move-prompt sequence a level-up already uses
/// (<see cref="MoveLearning"/>), and — the Gen 1 fidelity fact this covers — a <em>fainted</em> party member is
/// still a fully valid teach target (Gen 1 lets you use a TM/HM on a fainted Pokémon; nothing here gates on
/// <see cref="Creature.IsAlive"/>).
/// </summary>
public class MoveTeachRewardTests
{
    private static Attack Move(int id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            PowerPointsMax = 15,
        };

    private static Creature MakeCreature(string name, params Attack[] knownMoves)
    {
        var creature = new Creature(name) { Level = 20 };
        creature.CalculateStats();
        foreach (var move in knownMoves)
            creature.AddAttack(move);
        return creature;
    }

    [Fact]
    public async Task OfferAndApplyAsync_WithMoveTeachOption_AutoLearnsIntoAFreeSlot()
    {
        var creature = MakeCreature("CHARMANDER", Move(1, "Scratch"));
        var move = Move(7, "Flamethrower");
        var emitter = new RecordingEmitter();
        var ctx = new RunContext(
            new RunState(creature),
            emitter,
            new ScriptedInput(), // ChooseRewardAsync defaults to option 0; teach-target defaults to the first ABLE slot
            new SeededRandomSource(1)
        );
        var choice = new RewardChoice([new MoveTeachRewardOption(move, AbleBySlot: [true])]);

        await RewardResolution.OfferAndApplyAsync(
            choice,
            "Battle",
            wallet: null,
            playerBag: null,
            ctx
        );

        Assert.Contains(creature.MoveSet, m => m.Base.Id == move.Id);
        Assert.Single(emitter.Of<MoveTeachTargetRequired>());
        Assert.Contains(emitter.Of<MoveLearned>(), e => e.MoveName == "Flamethrower");
        Assert.Empty(emitter.Of<MoveReplacementRequired>());
        var granted = Assert.Single(emitter.Of<RewardGranted>());
        Assert.Contains("Flamethrower", granted.ItemNames);
    }

    [Fact]
    public async Task OfferAndApplyAsync_WithMoveTeachOption_OnAFullMoveset_RoutesThroughTheForgetPrompt()
    {
        var creature = MakeCreature(
            "CHARMANDER",
            Move(1, "Scratch"),
            Move(2, "Growl"),
            Move(3, "Ember"),
            Move(4, "Smokescreen")
        );
        var move = Move(7, "Flamethrower");
        var emitter = new RecordingEmitter();
        var ctx = new RunContext(
            new RunState(creature),
            emitter,
            new ScriptedInput().ForgetsSlot(2), // forget Ember (slot 2)
            new SeededRandomSource(1)
        );
        var choice = new RewardChoice([new MoveTeachRewardOption(move, AbleBySlot: [true])]);

        await RewardResolution.OfferAndApplyAsync(
            choice,
            "Battle",
            wallet: null,
            playerBag: null,
            ctx
        );

        Assert.Single(emitter.Of<MoveReplacementRequired>());
        Assert.Contains(emitter.Of<MoveForgotten>(), e => e.MoveName == "Ember");
        Assert.Contains(emitter.Of<MoveLearned>(), e => e.MoveName == "Flamethrower");
        Assert.DoesNotContain(creature.MoveSet, m => m.Base.Id == 3); // Ember is gone
        Assert.Contains(creature.MoveSet, m => m.Base.Id == move.Id); // Flamethrower took its slot
    }

    [Fact]
    public async Task OfferAndApplyAsync_WithMoveTeachOption_CanTeachAFaintedPartyMember()
    {
        // Slot 0 (the active lead) is healthy but NOT the able one; slot 1 is FAINTED (0 HP) but IS the able
        // one. The default teach-target picker auto-picks the first able slot — proving both that an
        // ineligible slot is skipped AND that a fainted member is never excluded from being picked.
        var lead = MakeCreature("SQUIRTLE", Move(1, "Tackle"));
        var bench = MakeCreature("CHARMANDER", Move(1, "Scratch"));
        bench.Attributes.HP = 0;
        Assert.False(bench.IsAlive());
        var party = new Party(lead);
        party.Add(bench);

        var move = Move(7, "Flamethrower");
        var emitter = new RecordingEmitter();
        var ctx = new RunContext(
            new RunState(party),
            emitter,
            new ScriptedInput(),
            new SeededRandomSource(1)
        );
        var choice = new RewardChoice([new MoveTeachRewardOption(move, AbleBySlot: [false, true])]);

        await RewardResolution.OfferAndApplyAsync(
            choice,
            "Battle",
            wallet: null,
            playerBag: null,
            ctx
        );

        Assert.DoesNotContain(lead.MoveSet, m => m.Base.Id == move.Id);
        Assert.Contains(bench.MoveSet, m => m.Base.Id == move.Id);
    }

    [Fact]
    public async Task OfferAndApplyAsync_WithMoveTeachOption_DecliningTheTargetPicker_TeachesNothing()
    {
        // Declining the "teach to a Pokémon?" screen (null) must not teach anything, even though the reward
        // itself was already granted (RewardGranted fires before the picker, per RewardResolution's own order).
        var creature = MakeCreature("CHARMANDER", Move(1, "Scratch"));
        var move = Move(7, "Flamethrower");
        var emitter = new RecordingEmitter();
        var ctx = new RunContext(
            new RunState(creature),
            emitter,
            new ScriptedInput().TeachesSlot(null),
            new SeededRandomSource(1)
        );
        var choice = new RewardChoice([new MoveTeachRewardOption(move, AbleBySlot: [true])]);

        await RewardResolution.OfferAndApplyAsync(
            choice,
            "Battle",
            wallet: null,
            playerBag: null,
            ctx
        );

        Assert.DoesNotContain(creature.MoveSet, m => m.Base.Id == move.Id);
        Assert.Empty(emitter.Of<MoveLearned>());
        Assert.Single(emitter.Of<RewardGranted>()); // the reward pick itself still went through
    }

    [Fact]
    public async Task OfferAndApplyAsync_WithMoveTeachOption_PickingANotAbleSlot_TeachesNothing()
    {
        // A stale or tampered client pick landing on a NOT-ABLE slot must be a no-op, not a bypass of the
        // legality gate — RewardResolution re-checks AbleBySlot itself rather than trusting the client's index.
        var lead = MakeCreature("SQUIRTLE", Move(1, "Tackle"));
        var party = new Party(lead);
        var move = Move(7, "Flamethrower");
        var emitter = new RecordingEmitter();
        var ctx = new RunContext(
            new RunState(party),
            emitter,
            new ScriptedInput().TeachesSlot(0), // slot 0 exists but is NOT able
            new SeededRandomSource(1)
        );
        var choice = new RewardChoice([new MoveTeachRewardOption(move, AbleBySlot: [false])]);

        await RewardResolution.OfferAndApplyAsync(
            choice,
            "Battle",
            wallet: null,
            playerBag: null,
            ctx
        );

        Assert.DoesNotContain(lead.MoveSet, m => m.Base.Id == move.Id);
        Assert.Empty(emitter.Of<MoveLearned>());
    }

    [Fact]
    public async Task OfferAndApplyAsync_WithMoveTeachOption_PickingAnOutOfRangeSlot_TeachesNothing()
    {
        var creature = MakeCreature("CHARMANDER", Move(1, "Scratch"));
        var move = Move(7, "Flamethrower");
        var emitter = new RecordingEmitter();
        var ctx = new RunContext(
            new RunState(creature),
            emitter,
            new ScriptedInput().TeachesSlot(5), // out of range for a 1-member party
            new SeededRandomSource(1)
        );
        var choice = new RewardChoice([new MoveTeachRewardOption(move, AbleBySlot: [true])]);

        await RewardResolution.OfferAndApplyAsync(
            choice,
            "Battle",
            wallet: null,
            playerBag: null,
            ctx
        );

        Assert.DoesNotContain(creature.MoveSet, m => m.Base.Id == move.Id);
        Assert.Empty(emitter.Of<MoveLearned>());
    }

    [Fact]
    public async Task OfferAndApplyAsync_WithMoveTeachOption_CandidatesAbleFlagMatchesTheRolledAbleBySlot()
    {
        var able = MakeCreature("CHARMANDER", Move(1, "Scratch"));
        var notAble = MakeCreature("BULBASAUR", Move(1, "Tackle"));
        var party = new Party(able);
        party.Add(notAble);
        var move = Move(7, "Flamethrower");
        var emitter = new RecordingEmitter();
        var ctx = new RunContext(
            new RunState(party),
            emitter,
            new ScriptedInput(),
            new SeededRandomSource(1)
        );
        var choice = new RewardChoice([new MoveTeachRewardOption(move, AbleBySlot: [true, false])]);

        await RewardResolution.OfferAndApplyAsync(
            choice,
            "Battle",
            wallet: null,
            playerBag: null,
            ctx
        );

        var required = Assert.Single(emitter.Of<MoveTeachTargetRequired>());
        Assert.Equal(2, required.Candidates.Count);
        Assert.True(required.Candidates[0].Able);
        Assert.False(required.Candidates[1].Able);
    }
}
