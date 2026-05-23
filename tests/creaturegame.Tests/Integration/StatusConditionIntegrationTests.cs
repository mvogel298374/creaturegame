using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Verifies status condition application end-to-end: DB → AttackService → Creature → AttackAction.
/// Seeds a temp moves.db with a paralysis move, loads it via AttackService, arms a Creature,
/// then runs combat actions until the status lands — confirming the full pipeline works.
/// </summary>
public class StatusConditionIntegrationTests : IDisposable
{
    private readonly string _movesDb = Path.ChangeExtension(Path.GetTempFileName(), ".db");

    private MovesDbContext BuildMovesContext()
    {
        var options = new DbContextOptionsBuilder<MovesDbContext>()
            .UseSqlite($"Data Source={_movesDb}")
            .Options;
        return new MovesDbContext(options);
    }

    [Fact]
    public async Task StatusMove_LoadedFromDb_AppliesStatusInCombat()
    {
        // --- Seed DB ---
        using (var ctx = BuildMovesContext())
        {
            ctx.EnsureDatabaseCreated();
            ctx.Moves.Add(new Attack
            {
                Id            = 1,
                Name          = "Thunder Wave",
                BaseDamage    = 0,
                Accuracy      = 100,
                PowerPointsMax = 20,
                AttackType    = AttackType.Undefined,
                DamageType    = DamageType.Electric,
                StatusEffect  = StatusCondition.Paralysis,
                EffectChance  = 100,
            });
            await ctx.SaveChangesAsync();
        }

        // --- Load via AttackService ---
        using var context = BuildMovesContext();
        var service = new AttackService(context);
        var thunderWave = await service.GetAttackByNameAsync("thunder wave");
        Assert.NotNull(thunderWave);
        Assert.Equal(StatusCondition.Paralysis, thunderWave.StatusEffect);
        Assert.Equal(100, thunderWave.EffectChance);

        // --- Build creatures ---
        var attacker = new Creature("Raichu") { Level = 50 };
        attacker.CalculateStats();
        attacker.AddAttack(thunderWave);

        var defender = new Creature("Snorlax") { Level = 50 };
        defender.CalculateStats();

        // --- Run combat actions until status applied (max 20 turns as safety net) ---
        var chart = new Gen1TypeChart();
        int turns = 0;
        while (defender.Status == StatusCondition.None && turns < 20)
        {
            var action = new AttackAction(attacker, defender, attacker.MoveSet[0], chart);
            await action.ExecuteAsync();
            turns++;
        }

        Assert.Equal(StatusCondition.Paralysis, defender.Status);
    }

    [Fact]
    public async Task StatusMove_WithLowEffectChance_NeverAppliesAtZeroPercent()
    {
        // --- Seed DB ---
        using (var ctx = BuildMovesContext())
        {
            ctx.EnsureDatabaseCreated();
            ctx.Moves.Add(new Attack
            {
                Id            = 1,
                Name          = "Tackle",
                BaseDamage    = 40,
                Accuracy      = 100,
                PowerPointsMax = 35,
                AttackType    = AttackType.Physical,
                DamageType    = DamageType.Normal,
                StatusEffect  = StatusCondition.Burn,
                EffectChance  = 0,
            });
            await ctx.SaveChangesAsync();
        }

        using var context = BuildMovesContext();
        var service = new AttackService(context);
        var tackle = await service.GetAttackByNameAsync("tackle");
        Assert.NotNull(tackle);

        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.AddAttack(tackle);

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.HP = 9999;
        defender.Attributes.MaxHP = 9999;

        var chart = new Gen1TypeChart();
        for (int i = 0; i < 30; i++)
        {
            var action = new AttackAction(attacker, defender, attacker.MoveSet[0], chart);
            await action.ExecuteAsync();
        }

        Assert.Equal(StatusCondition.None, defender.Status);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_movesDb)) File.Delete(_movesDb);
    }
}
