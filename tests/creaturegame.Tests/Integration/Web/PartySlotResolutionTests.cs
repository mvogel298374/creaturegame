using creaturegame.Creatures;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The party-slot resolution rule (<see cref="GameSessionManager.PartyMemberAt"/>) behind the CHECK POKEMON
/// party-member picker (docs/TODO.md). Unlike <see cref="GameSessionManager.ActiveCreature"/>, which always
/// resolves to the battling lead, this reads whichever slot the client asked for — fainted/benched included —
/// so the picker can show any party member's sheet. Pure rule, pinned on its own (the
/// <see cref="ActiveCreatureResolutionTests"/> precedent).
/// </summary>
public class PartySlotResolutionTests
{
    private static Creature Mon(string name)
    {
        var c = new Creature(name) { Level = 5, Type1 = creaturegame.Attacks.DamageType.Normal };
        c.CalculateStats();
        return c;
    }

    [Fact]
    public void PartyMemberAt_WithNoPartyWired_ResolvesSlotZeroToTheStarter()
    {
        var starter = Mon("Starter");

        Assert.Same(starter, GameSessionManager.PartyMemberAt(null, starter, 0));
    }

    [Fact]
    public void PartyMemberAt_WithNoPartyWired_RejectsAnySlotButZero()
    {
        var starter = Mon("Starter");

        Assert.Null(GameSessionManager.PartyMemberAt(null, starter, 1));
    }

    [Fact]
    public void PartyMemberAt_WithAPartyWired_ResolvesEachMemberByItsOwnIndex_NotJustTheLead()
    {
        var starter = Mon("Starter");
        var bench = Mon("Bench");
        var party = new Party(starter);
        party.Add(bench);
        party.SetLead(1); // the bench member is now battling; slot 0 must still read the starter

        Assert.Same(starter, GameSessionManager.PartyMemberAt(party, starter, 0));
        Assert.Same(bench, GameSessionManager.PartyMemberAt(party, starter, 1));
    }

    [Fact]
    public void PartyMemberAt_ReturnsAFaintedMember_NotJustLiveOnes()
    {
        var starter = Mon("Starter");
        var bench = Mon("Bench");
        bench.Attributes.HP = 0;
        var party = new Party(starter);
        party.Add(bench);

        Assert.Same(bench, GameSessionManager.PartyMemberAt(party, starter, 1));
    }

    [Fact]
    public void PartyMemberAt_WithAPartyWired_RejectsAnOutOfRangeSlot()
    {
        var starter = Mon("Starter");
        var party = new Party(starter);

        Assert.Null(GameSessionManager.PartyMemberAt(party, starter, 1));
        Assert.Null(GameSessionManager.PartyMemberAt(party, starter, -1));
    }
}
