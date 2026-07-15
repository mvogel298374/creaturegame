using creaturegame.Creatures;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The active-creature resolution rule (<see cref="GameSessionManager.ActiveCreature"/>) behind the on-demand
/// CHECK POKEMON overview (<c>GET /api/game/{id}/player</c>). The creature on the field <em>moves</em> — the
/// between-biome lead swap (Phase 4 Stage 1d) reassigns the lead — so the session's captured starter is only a
/// fallback: resolving to it once a party is wired shows the panel a benched creature's stats/moves/PP instead of
/// the one actually fighting. Pure rule, pinned on its own (the
/// <see cref="GameSessionManager.ProjectBagView"/> / <c>BagViewProjectionTests</c> precedent).
/// </summary>
public class ActiveCreatureResolutionTests
{
    private static Creature Mon(string name)
    {
        var c = new Creature(name) { Level = 5, Type1 = creaturegame.Attacks.DamageType.Normal };
        c.CalculateStats();
        return c;
    }

    [Fact]
    public void ActiveCreature_WithNoPartyWired_FallsBackToTheSessionStarter()
    {
        // The legacy / not-yet-partied session: the starter is the creature on the field.
        var starter = Mon("Starter");

        Assert.Same(starter, GameSessionManager.ActiveCreature(null, starter));
    }

    [Fact]
    public void ActiveCreature_WithAPartyWired_ResolvesToTheLead_NotTheStarter()
    {
        var starter = Mon("Starter");
        var party = new Party(starter);

        Assert.Same(starter, GameSessionManager.ActiveCreature(party, starter));
    }

    [Fact]
    public void ActiveCreature_TracksTheLeadAcrossASwitch_NotTheCapturedStarter()
    {
        // A lead swap moves Party.Lead. The panel must follow it — resolving to the captured starter here is
        // exactly the stale read that describes a benched creature instead of the one on the field.
        var starter = Mon("Starter");
        var bench = Mon("Bench");
        var party = new Party(starter);
        party.Add(bench);

        party.SetLead(1);

        Assert.Same(bench, GameSessionManager.ActiveCreature(party, starter));
    }
}
