using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The move menu's STAB hint: <see cref="MoveInfo.Stab"/> on the <see cref="TurnStarted"/> event flags a
/// damaging move whose type matches one of the player's current types (Same-Type Attack Bonus). Drives the
/// gold highlight in the move menu. Verifies the engine sets the flag correctly so the UI doesn't re-derive
/// the rule: matching-type damaging move = STAB; off-type damaging move and matching-type *status* move = not.
/// </summary>
public class MoveInfoStabTests
{
    private static int _nextId = 1;

    // Distinct Id per move — AddAttack dedupes on Attack.Id, so default-0 ids would collide and only the
    // first move would actually be added.
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
    public async Task TurnStarted_FlagsStab_OnlyForMatchingTypeDamagingMoves()
    {
        // A Fire-type player carrying a Fire damaging move (STAB), a Normal damaging move (off-type),
        // and a Fire *status* move (no damage → no STAB even though the type matches).
        var player = TestCreatures.Make("PLAYER", type1: DamageType.Fire);
        player.AddAttack(Move("flame-jab", DamageType.Fire, 80)); // matching type, damaging → STAB
        player.AddAttack(Move("body-slam", DamageType.Normal, 85)); // off-type damaging → no STAB
        player.AddAttack(Move("fire-glare", DamageType.Fire, 0)); // matching type, status → no STAB

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(TestCreatures.Make("ENEMY", type1: DamageType.Water))
            .PlayerUses("flame-jab")
            .Seed(1)
            .RunAsync();

        var moves = result.First<TurnStarted>()!.PlayerMoves;
        bool StabOf(string name) => moves.Single(m => m.Name == name).Stab;

        Assert.True(StabOf("flame-jab"));
        Assert.False(StabOf("body-slam"));
        Assert.False(StabOf("fire-glare"));
    }

    [Fact]
    public async Task TurnStarted_FlagsStab_OnEitherOfTheUsersTwoTypes()
    {
        // A dual-type (Water/Ice) player: a move of either type gets STAB.
        var player = TestCreatures.Make("PLAYER", type1: DamageType.Water, type2: DamageType.Ice);
        player.AddAttack(Move("surf", DamageType.Water, 90)); // type1 match → STAB
        player.AddAttack(Move("ice-beam", DamageType.Ice, 90)); // type2 match → STAB
        player.AddAttack(Move("tackle", DamageType.Normal, 40)); // neither → no STAB

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(TestCreatures.Make("ENEMY", type1: DamageType.Grass))
            .PlayerUses("surf")
            .Seed(1)
            .RunAsync();

        var moves = result.First<TurnStarted>()!.PlayerMoves;
        bool StabOf(string name) => moves.Single(m => m.Name == name).Stab;

        Assert.True(StabOf("surf"));
        Assert.True(StabOf("ice-beam"));
        Assert.False(StabOf("tackle"));
    }
}
