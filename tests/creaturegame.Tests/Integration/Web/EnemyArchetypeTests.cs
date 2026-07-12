using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The enemy strength tiers (<see cref="IEnemyArchetype"/>): each composes an <see cref="EnemyTierSpec"/> from
/// the run context. Verifies the per-tier levers (DV quality, moveset strategy, move count) and that BST and
/// level climb Weak → Medium → Strong → Boss off the shared depth baseline.
/// </summary>
public class EnemyArchetypeTests
{
    private static EnemyContext Ctx(int seed = 7) =>
        new(PlayerLevel: 50, PlayerBst: 400, Depth: 0, Rng: new SeededRandomSource(seed));

    [Fact]
    public void Tiers_DvQuality_ClimbsWeakToBoss()
    {
        Assert.Equal(DvQuality.Poor, EnemyArchetypes.Weak.Build(Ctx()).Dvs);
        Assert.Equal(DvQuality.Average, EnemyArchetypes.Medium.Build(Ctx()).Dvs);
        Assert.Equal(DvQuality.High, EnemyArchetypes.Strong.Build(Ctx()).Dvs);
        Assert.Equal(DvQuality.Perfect, EnemyArchetypes.Boss.Build(Ctx()).Dvs);
    }

    [Fact]
    public void Tiers_MovesetStrategy_MatchesTheTier()
    {
        Assert.Equal(MoveSelectionStrategy.WeightedSmart, EnemyArchetypes.Weak.Build(Ctx()).Moves);
        Assert.Equal(
            MoveSelectionStrategy.WeightedSmart,
            EnemyArchetypes.Medium.Build(Ctx()).Moves
        );
        Assert.Equal(MoveSelectionStrategy.TmEnhanced, EnemyArchetypes.Strong.Build(Ctx()).Moves);
        Assert.Equal(MoveSelectionStrategy.Optimal, EnemyArchetypes.Boss.Build(Ctx()).Moves);
    }

    [Fact]
    public void Weak_GetsFewerMoves_OthersGetFour()
    {
        Assert.Equal(3, EnemyArchetypes.Weak.Build(Ctx()).MoveCount);
        Assert.Equal(4, EnemyArchetypes.Medium.Build(Ctx()).MoveCount);
        Assert.Equal(4, EnemyArchetypes.Strong.Build(Ctx()).MoveCount);
        Assert.Equal(4, EnemyArchetypes.Boss.Build(Ctx()).MoveCount);
    }

    [Fact]
    public void For_MapsEncounterTierIntentToArchetype()
    {
        // 3c-1: the core passes a generation-agnostic EncounterTier per node; the web maps it to a concrete
        // archetype. Normal ≈ a plain wild encounter (the default Medium); Elite/Boss climb.
        Assert.Same(EnemyArchetypes.Medium, EnemyArchetypes.For(EncounterTier.Normal));
        Assert.Same(EnemyArchetypes.Strong, EnemyArchetypes.For(EncounterTier.Elite));
        Assert.Same(EnemyArchetypes.Boss, EnemyArchetypes.For(EncounterTier.Boss));
    }

    [Fact]
    public void For_WithRng_RollsWeakOrMediumForNormal_ButKeepsEliteBossFixed()
    {
        // A plain Normal wild encounter varies in strength (Weak vs Medium) so wild fights aren't all the same,
        // while presenting identically to the player. Elite/Boss stay a fixed mapping regardless of the roll.
        var rng = new SeededRandomSource(1234);
        int weak = 0,
            medium = 0;
        for (int i = 0; i < 400; i++)
        {
            var a = EnemyArchetypes.For(EncounterTier.Normal, rng);
            if (ReferenceEquals(a, EnemyArchetypes.Weak))
                weak++;
            else if (ReferenceEquals(a, EnemyArchetypes.Medium))
                medium++;
            else
                Assert.Fail("Normal must map to Weak or Medium only");
        }

        // Both tiers appear, at roughly equal frequency (undifferentiated, ~50/50).
        Assert.True(weak > 100, $"expected a meaningful share of Weak, got {weak}/400");
        Assert.True(medium > 100, $"expected a meaningful share of Medium, got {medium}/400");

        // Elite/Boss ignore the roll entirely.
        Assert.Same(EnemyArchetypes.Strong, EnemyArchetypes.For(EncounterTier.Elite, rng));
        Assert.Same(EnemyArchetypes.Boss, EnemyArchetypes.For(EncounterTier.Boss, rng));
    }

    [Fact]
    public void Medium_IsTheDepthBaseline()
    {
        var spec = EnemyArchetypes.Medium.Build(Ctx());
        Assert.Equal(EncounterFactory.ScaleTargetBst(400, 0), spec.TargetBst); // exactly the baseline
        Assert.Same(EnemyArchetypes.Medium, EnemyArchetypes.Default); // and it's the default tier
    }

    [Fact]
    public void TargetBst_ClimbsWeakToBoss()
    {
        int weak = EnemyArchetypes.Weak.Build(Ctx()).TargetBst;
        int medium = EnemyArchetypes.Medium.Build(Ctx()).TargetBst;
        int strong = EnemyArchetypes.Strong.Build(Ctx()).TargetBst;
        int boss = EnemyArchetypes.Boss.Build(Ctx()).TargetBst;

        Assert.True(
            weak < medium && medium < strong && strong < boss,
            $"{weak} < {medium} < {strong} < {boss}"
        );
    }

    [Fact]
    public void Level_ClimbsWeakToBoss_AtTheSameSeed()
    {
        // Same seed ⇒ the same baseline roll, so the tier offsets order the levels strictly.
        int weak = EnemyArchetypes.Weak.Build(Ctx(99)).Level;
        int medium = EnemyArchetypes.Medium.Build(Ctx(99)).Level;
        int strong = EnemyArchetypes.Strong.Build(Ctx(99)).Level;
        int boss = EnemyArchetypes.Boss.Build(Ctx(99)).Level;

        Assert.True(
            weak < medium && medium < strong && strong < boss,
            $"{weak} < {medium} < {strong} < {boss}"
        );
    }
}
