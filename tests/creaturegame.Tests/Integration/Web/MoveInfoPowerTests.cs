using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The move menu's strength cue: <see cref="MoveInfo.Power"/> on the <see cref="TurnStarted"/> event carries a
/// move's raw base power so the UI can render a strength pill without knowing any move data. It is plain move
/// data (no gen-variable rule), so a damaging move projects its base power straight through, and a
/// fixed-damage / status move (BaseDamage 0) projects 0 — the UI's "no cue", mirroring STAB/effectiveness.
/// </summary>
public class MoveInfoPowerTests
{
    private static int _nextId = 1;

    // Distinct Id per move — AddAttack dedupes on Attack.Id, so default-0 ids would collide.
    private static Attack Move(string name, DamageType type, int power) =>
        new(name, "test move")
        {
            Id = _nextId++,
            DamageType = type,
            BaseDamage = power,
            Accuracy = 100,
            PowerPointsMax = 30,
        };

    [Fact]
    public async Task TurnStarted_ProjectsBasePower_AndReportsZeroForFixedDamageMoves()
    {
        // A weak damaging move (40) keeps its power, a strong one (150) keeps its, and a status move
        // (BaseDamage 0) reports 0 so the client shows no strength pill.
        var player = TestCreatures.Make("PLAYER", type1: DamageType.Normal);
        player.AddAttack(Move("tackle", DamageType.Normal, 40)); // weak damaging → 40
        player.AddAttack(Move("hyper-beam", DamageType.Normal, 150)); // strong damaging → 150
        player.AddAttack(Move("growl", DamageType.Normal, 0)); // status → 0 (no cue)

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(TestCreatures.Make("ENEMY", type1: DamageType.Water))
            .PlayerUses("tackle")
            .Seed(1)
            .RunAsync();

        var moves = result.First<TurnStarted>()!.PlayerMoves;
        int PowerOf(string name) => moves.Single(m => m.Name == name).Power;

        Assert.Equal(40, PowerOf("tackle"));
        Assert.Equal(150, PowerOf("hyper-beam"));
        Assert.Equal(0, PowerOf("growl"));
    }
}
