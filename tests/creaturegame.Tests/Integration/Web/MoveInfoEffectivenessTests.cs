using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The move menu's effectiveness pill: <see cref="MoveInfo.Effectiveness"/> on <see cref="TurnStarted"/>
/// reports a damaging move's type multiplier vs the *current* enemy (product over the enemy's types, via the
/// active <see cref="ITypeChart"/>) so the UI can show a ×N cue without knowing the chart. Verifies the Gen 1
/// values (incl. the dual-type product and a 0× immunity), and that non-damaging moves report neutral 1.0.
/// </summary>
public class MoveInfoEffectivenessTests
{
    private static int _nextId = 1;

    private static Attack Move(DamageType type, int power) =>
        new($"m{_nextId}", "test move")
        {
            Id = _nextId++,
            DamageType = type,
            BaseDamage = power,
            Accuracy = 100,
            PowerPointsMax = 30,
        };

    private static async Task<double> EffectivenessAsync(Attack move, Creature enemy)
    {
        var player = TestCreatures.Make("PLAYER", type1: DamageType.Normal);
        player.AddAttack(move);

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses(move.Name!)
            .Seed(1)
            .RunAsync();

        return result.First<TurnStarted>()!.PlayerMoves.Single().Effectiveness;
    }

    [Theory]
    [InlineData(DamageType.Fire, DamageType.Grass, null, 2.0)] // super-effective
    [InlineData(DamageType.Fire, DamageType.Water, null, 0.5)] // resisted
    [InlineData(DamageType.Fire, DamageType.Bug, DamageType.Grass, 4.0)] // double-weak → ×4
    [InlineData(DamageType.Fire, DamageType.Fire, DamageType.Water, 0.25)] // double-resist → ×¼
    [InlineData(DamageType.Normal, DamageType.Ghost, null, 0.0)] // Gen 1 immunity
    [InlineData(DamageType.Normal, DamageType.Water, null, 1.0)] // neutral
    public async Task TurnStarted_ReportsMoveEffectiveness_VsEnemyTypes(
        DamageType moveType,
        DamageType enemyType1,
        DamageType? enemyType2,
        double expected
    )
    {
        var enemy = TestCreatures.Make("ENEMY", type1: enemyType1, type2: enemyType2);
        Assert.Equal(expected, await EffectivenessAsync(Move(moveType, 80), enemy));
    }

    [Fact]
    public async Task TurnStarted_ReportsNeutralEffectiveness_ForNonDamagingMoves()
    {
        // A Fire status move (BaseDamage 0) vs a Water enemy reports 1.0, not 0.5 — fixed-damage/status moves
        // ignore the type chart, so the menu shows no effectiveness pill for them.
        var enemy = TestCreatures.Make("ENEMY", type1: DamageType.Water);
        Assert.Equal(1.0, await EffectivenessAsync(Move(DamageType.Fire, 0), enemy));
    }
}
