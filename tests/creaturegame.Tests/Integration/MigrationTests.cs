using System.Collections.Generic;
using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.DB;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Verifies that EnsureDatabaseCreated() correctly applies EF Core migrations to a
/// fresh SQLite database, producing a schema that can round-trip all model fields —
/// including the columns (Priority, EffectChance, CatchRate, etc.) that were
/// previously added via raw ALTER TABLE hacks.
/// </summary>
public class MigrationTests : IDisposable
{
    private readonly string _movesDb  = Path.ChangeExtension(Path.GetTempFileName(), ".db");
    private readonly string _pokemonDb = Path.ChangeExtension(Path.GetTempFileName(), ".db");

    // --- MovesDbContext ---

    [Fact]
    public void MovesDb_EnsureDatabaseCreated_CanRoundTripAttackWithAllFields()
    {
        using var context = BuildMovesContext();
        context.EnsureDatabaseCreated();

        var attack = new Attack
        {
            Name              = "Flamethrower",
            BaseDamage        = 95,
            Accuracy          = 100,
            PowerPointsMax    = 15,
            AttackType        = AttackType.Special,
            DamageType        = DamageType.Fire,
            Priority          = 1,
            EffectChance      = 10,
            StatusEffect      = StatusCondition.Burn,
            IsHighCrit        = false,
            StatEffectStat    = StageStat.Special,
            StatEffectDelta   = -1,
            StatEffectTarget  = StageTarget.Foe,
            StatEffectChance  = 10,
            Effect            = MoveEffect.None,
            DamageCategory    = DamageCategory.Standard,
            FixedDamageValue  = null,
            DrainPercent      = 50,
            NeverMisses       = false,
        };
        context.Moves.Add(attack);
        context.SaveChanges();

        var loaded = context.Moves.AsNoTracking().Single(m => m.Name == "Flamethrower");
        Assert.Equal(95,                       loaded.BaseDamage);
        Assert.Equal(DamageType.Fire,          loaded.DamageType);
        Assert.Equal(1,                        loaded.Priority);
        Assert.Equal(10,                       loaded.EffectChance);
        Assert.Equal(StatusCondition.Burn,     loaded.StatusEffect);
        Assert.False(loaded.IsHighCrit);
        Assert.Equal(StageStat.Special,        loaded.StatEffectStat);
        Assert.Equal(-1,                       loaded.StatEffectDelta);
        Assert.Equal(StageTarget.Foe,          loaded.StatEffectTarget);
        Assert.Equal(10,                       loaded.StatEffectChance);
        Assert.Equal(MoveEffect.None,          loaded.Effect);
        Assert.Equal(DamageCategory.Standard,  loaded.DamageCategory);
        Assert.Null(loaded.FixedDamageValue);
        Assert.Equal(50,                       loaded.DrainPercent);
        Assert.False(loaded.NeverMisses);
    }

    [Fact]
    public void MovesDb_EnsureDatabaseCreated_IsIdempotent()
    {
        using var context = BuildMovesContext();
        var ex = Record.Exception(() =>
        {
            context.EnsureDatabaseCreated();
            context.EnsureDatabaseCreated();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void MovesDb_Schema_HasAllExpectedColumns()
    {
        using var context = BuildMovesContext();
        context.EnsureDatabaseCreated();

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection($"Data Source={_movesDb}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Moves)";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1)); // column 1 = name

        Assert.Contains("Id",            columns);
        Assert.Contains("Name",          columns);
        Assert.Contains("BaseDamage",    columns);
        Assert.Contains("DamageType",    columns);
        Assert.Contains("AttackType",    columns);
        Assert.Contains("Accuracy",      columns);
        Assert.Contains("PowerPointsMax", columns);
        Assert.Contains("Priority",      columns);
        Assert.Contains("EffectChance",  columns);
        Assert.Contains("StatusEffect",    columns);
        Assert.Contains("IsHighCrit",      columns);
        Assert.Contains("StatEffectStat",    columns);
        Assert.Contains("StatEffectDelta",  columns);
        Assert.Contains("StatEffectTarget", columns);
        Assert.Contains("StatEffectChance", columns);
        Assert.Contains("Effect",           columns);
        Assert.Contains("DamageCategory",   columns);
        Assert.Contains("FixedDamageValue", columns);
        Assert.Contains("DrainPercent",     columns);
        Assert.Contains("NeverMisses",      columns);
    }

    // --- PokemonDbContext ---

    [Fact]
    public void PokemonDb_EnsureDatabaseCreated_CanRoundTripSpeciesWithAllFields()
    {
        using var context = BuildPokemonContext();
        context.EnsureDatabaseCreated();

        var species = new PokemonSpecies
        {
            Id             = 6,
            Name           = "charizard",
            BaseHP         = 78,
            BaseAttack     = 84,
            BaseDefense    = 78,
            BaseSpecial    = 85,
            BaseSpeed      = 100,
            Type1          = DamageType.Fire,
            Type2          = DamageType.Flying,
            GrowthRate     = GrowthRate.MediumSlow,
            CatchRate      = 45,
            BaseExperience = 240,
            PokedexEntry   = "Spits fire that is hot enough to melt boulders.",
        };
        context.Species.Add(species);
        context.SaveChanges();

        var loaded = context.Species.AsNoTracking().Single(s => s.Name == "charizard");
        Assert.Equal(45,                  loaded.CatchRate);
        Assert.Equal(240,                 loaded.BaseExperience);
        Assert.Equal(GrowthRate.MediumSlow, loaded.GrowthRate);
        Assert.Equal("Spits fire that is hot enough to melt boulders.", loaded.PokedexEntry);
        Assert.Equal(DamageType.Flying,   loaded.Type2);
    }

    [Fact]
    public void PokemonDb_EnsureDatabaseCreated_IsIdempotent()
    {
        using var context = BuildPokemonContext();
        var ex = Record.Exception(() =>
        {
            context.EnsureDatabaseCreated();
            context.EnsureDatabaseCreated();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void PokemonDb_Schema_HasAllExpectedColumns()
    {
        using var context = BuildPokemonContext();
        context.EnsureDatabaseCreated();

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection($"Data Source={_pokemonDb}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(PokemonSpecies)";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        Assert.Contains("Id",             columns);
        Assert.Contains("Name",           columns);
        Assert.Contains("BaseHP",         columns);
        Assert.Contains("BaseAttack",     columns);
        Assert.Contains("BaseDefense",    columns);
        Assert.Contains("BaseSpecial",    columns);
        Assert.Contains("BaseSpeed",      columns);
        Assert.Contains("Type1",          columns);
        Assert.Contains("Type2",          columns);
        Assert.Contains("GrowthRate",     columns);
        Assert.Contains("CatchRate",      columns);
        Assert.Contains("BaseExperience", columns);
        Assert.Contains("PokedexEntry",   columns);
    }

    // --- Helpers ---

    private MovesDbContext BuildMovesContext()
    {
        var options = new DbContextOptionsBuilder<MovesDbContext>()
            .UseSqlite($"Data Source={_movesDb}")
            .Options;
        return new MovesDbContext(options);
    }

    private PokemonDbContext BuildPokemonContext()
    {
        var options = new DbContextOptionsBuilder<PokemonDbContext>()
            .UseSqlite($"Data Source={_pokemonDb}")
            .Options;
        return new PokemonDbContext(options);
    }

    public void Dispose()
    {
        // Release any pooled SQLite connections before deleting the temp files,
        // otherwise the connection pool keeps the file handle open just long enough
        // to cause an IOException on Windows.
        SqliteConnection.ClearAllPools();
        if (File.Exists(_movesDb))   File.Delete(_movesDb);
        if (File.Exists(_pokemonDb)) File.Delete(_pokemonDb);
    }
}
