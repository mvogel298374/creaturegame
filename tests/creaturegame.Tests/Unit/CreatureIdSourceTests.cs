using creaturegame.Creatures;

namespace creaturegame.Tests.Unit;

public class CreatureIdSourceTests
{
    [Fact]
    public void ANewCreature_IsUnassigned()
    {
        Assert.Equal(0, new Creature("PIDGEY").Id);
    }

    [Fact]
    public void Next_CountsUpFromOne_AndTracksTheHighWaterMark()
    {
        var ids = new CreatureIdSource();
        Assert.Equal(0, ids.HighWater);

        Assert.Equal(1, ids.Next());
        Assert.Equal(2, ids.Next());
        Assert.Equal(2, ids.HighWater);
    }

    [Fact]
    public void Assign_GivesSameNamedCreaturesDistinctIds()
    {
        // The whole point: identical display names, distinct identities.
        var ids = new CreatureIdSource();
        var a = ids.Assign(new Creature("PIDGEY"));
        var b = ids.Assign(new Creature("PIDGEY"));

        Assert.Equal(a.Name, b.Name);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Assign_IsIdempotent_AnIdentifiedCreatureKeepsItsId()
    {
        var ids = new CreatureIdSource();
        var c = ids.Assign(new Creature("PIDGEY"));
        int first = c.Id;

        ids.Assign(c);

        Assert.Equal(first, c.Id);
        Assert.Equal(1, ids.HighWater); // no id was burned on the second call
    }

    [Fact]
    public void TwoSources_AreIndependent_SoIdsAreScopedToARun()
    {
        var run1 = new CreatureIdSource();
        var run2 = new CreatureIdSource();
        run1.Next();
        run1.Next();

        Assert.Equal(1, run2.Next());
    }

    [Fact]
    public void Id_SurvivesNicknamingAndIdentitySnapshotRestore()
    {
        // Name is a display string and Transform/Mimic swap species identity in place; neither may touch Id.
        var c = new CreatureIdSource().Assign(new Creature("PIDGEY"));
        int id = c.Id;

        c.Name = "BIRDY";
        c.SnapshotIdentityForMutation();
        c.SpeciesId = 132; // Transform copies the target's species identity
        c.RestoreOriginalIdentity();

        Assert.Equal(id, c.Id);
    }
}
