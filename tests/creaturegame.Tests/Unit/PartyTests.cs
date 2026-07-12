using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The run's <see cref="Party"/> container (the acquisition destination — <c>ENCOUNTER_DESIGN.md §4</c>) and the
/// roster behaviour that hangs off it: the party caps at <see cref="Party.MaxSize"/> six, <see cref="RunState.Player"/>
/// tracks the party's lead, and a Poké Center restores the <em>whole</em> party (not just the lead). No new
/// battle-seam surface — this is run-level state, generation-invariant.
/// </summary>
public class PartyTests
{
    [Fact]
    public void NewParty_HasTheSeededLeadAsItsSoleActiveMember()
    {
        var starter = Wounded("Starter");
        var party = new Party(starter);

        Assert.Equal(1, party.Count);
        Assert.Same(starter, party.Lead);
        Assert.Equal(0, party.LeadIndex);
        Assert.False(party.IsFull);
        Assert.Equal([starter], party.Members);
    }

    [Fact]
    public void Add_AppendsMembers_UntilTheSixSlotCeiling_ThenRefuses()
    {
        var party = new Party(Wounded("Lead"));
        for (int i = 1; i < Party.MaxSize; i++)
            Assert.True(party.Add(Wounded($"M{i}")));

        Assert.True(party.IsFull);
        Assert.Equal(Party.MaxSize, party.Count);

        // A seventh acquisition is refused (no change) — the caller must Replace or decline instead.
        var overflow = Wounded("Overflow");
        Assert.False(party.Add(overflow));
        Assert.Equal(Party.MaxSize, party.Count);
        Assert.DoesNotContain(overflow, party.Members);
    }

    [Fact]
    public void Replace_SwapsTheSlot_AndReplacingTheLeadSlotMakesTheNewCreatureLead()
    {
        var lead = Wounded("Lead");
        var bench = Wounded("Bench");
        var party = new Party(lead);
        party.Add(bench);

        var caught = Wounded("Caught");
        party.Replace(0, caught); // the lead's slot

        Assert.Same(caught, party.Lead); // the replacement occupies the lead slot → it's the new lead
        Assert.DoesNotContain(lead, party.Members);
        Assert.Equal(2, party.Count); // a replace swaps in place — count unchanged
        Assert.Equal([caught, bench], party.Members);
    }

    [Fact]
    public void Replace_OutOfRange_IsANoOp()
    {
        var party = new Party(Wounded("Lead"));
        var caught = Wounded("Caught");

        party.Replace(5, caught); // no such slot
        party.Replace(-1, caught);

        Assert.Equal(1, party.Count);
        Assert.DoesNotContain(caught, party.Members);
    }

    [Fact]
    public void SetLead_ChangesTheActiveMember_AndRunStatePlayerFollowsIt()
    {
        var lead = Wounded("Lead");
        var bench = Wounded("Bench");
        var party = new Party(lead);
        party.Add(bench);
        var state = new RunState(party);

        Assert.Same(lead, state.Player); // Player is the lead

        party.SetLead(1);

        Assert.Same(bench, party.Lead);
        Assert.Same(bench, state.Player); // the active creature followed the lead swap
    }

    [Fact]
    public void SetLead_OutOfRange_IsANoOp()
    {
        var lead = Wounded("Lead");
        var party = new Party(lead);
        party.Add(Wounded("Bench"));

        party.SetLead(9);
        party.SetLead(-2);

        Assert.Same(lead, party.Lead); // unchanged
    }

    [Fact]
    public void RunState_SingleCreatureConstructor_SeedsAPartyOfOne()
    {
        var starter = Wounded("Solo");
        var state = new RunState(starter);

        Assert.Equal(1, state.Party.Count);
        Assert.Same(starter, state.Player);
        Assert.Same(starter, state.Party.Lead);
    }

    [Fact]
    public async Task PokeCenterRecovery_HealsTheWholeParty_NotJustTheLead()
    {
        // The Poké Center caps each biome and must restore every owned creature — benched members keep permanent
        // HP across biomes, so a heal that only touched the lead would leave the bench wounded forever.
        var lead = Wounded("Lead");
        var bench = Wounded("Bench");
        var party = new Party(lead);
        party.Add(bench);
        var state = new RunState(party);
        var recorder = new RecordingEmitter();
        var ctx = new RunContext(
            state,
            recorder,
            AutoSelectInput.Instance,
            new SeededRandomSource(0)
        );

        // The default input accepts the recovery, so this heals.
        var outcome = await new RecoveryRunEvent().RunAsync(ctx);

        Assert.Equal(new RecoveryOutcome(true), outcome);
        foreach (var member in party.Members)
        {
            Assert.Equal(member.Attributes.MaxHP, member.Attributes.HP); // HP restored
            Assert.All(
                member.MoveSet,
                m => Assert.Equal(m.Base.PowerPointsMax, m.PowerPointsCurrent)
            );
            Assert.Equal(StatusCondition.None, member.Battle.Status); // status cured
        }
    }

    [Fact]
    public async Task PokeCenterRecovery_Declined_LeavesTheWholePartyAsWounded()
    {
        // Symmetry check: a declined heal must touch no one — a benched member's wounds are not silently fixed.
        var lead = Wounded("Lead");
        var bench = Wounded("Bench");
        var party = new Party(lead);
        party.Add(bench);
        var state = new RunState(party);
        var ctx = new RunContext(
            state,
            new RecordingEmitter(),
            new DeclineRecoveryInput(),
            new SeededRandomSource(0)
        );

        var outcome = await new RecoveryRunEvent().RunAsync(ctx);

        Assert.Equal(new RecoveryOutcome(false), outcome);
        foreach (var member in party.Members)
            Assert.Equal(1, member.Attributes.HP); // still at the wounded HP set below
    }

    // A wounded creature: HP dropped to 1, one move drained, and a persisting major status — so a full heal is
    // observable across all three components.
    private static Creature Wounded(string name)
    {
        var c = new Creature(name) { Level = 20, Type1 = DamageType.Normal };
        c.CalculateStats();
        c.Attributes.MaxHP = 50;
        c.AddAttack(
            new Attack
            {
                Name = "tackle",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
                PowerPointsMax = 35,
            }
        );
        c.Attributes.HP = 1;
        c.MoveSet[0].PowerPointsCurrent = 2;
        c.Battle.Status = StatusCondition.Poison;
        return c;
    }

    // An input that declines the Poké Center offer (the interactive "skip" path), for the declined-heal case.
    private sealed class DeclineRecoveryInput : IBattleInput
    {
        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context) =>
            throw new NotSupportedException();

        public Task<bool> ConfirmRecoveryAsync(RecoveryContext context) => Task.FromResult(false);
    }
}
