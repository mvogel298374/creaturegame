using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Exercises the real runtime path the web layer uses: learnset rows persisted to (and
/// queried back from) a SQLite <see cref="PokemonDbContext"/> via EF Core, then fed through
/// <see cref="LearnsetMoveSelector"/>. Proves the DB round-trip + the generation filter +
/// move selection compose correctly — the slice unit tests don't cover end to end.
/// </summary>
public class LearnsetIntegrationTests : IDisposable
{
    private readonly string _pokemonDb = Path.ChangeExtension(Path.GetTempFileName(), ".db");

    private PokemonDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<PokemonDbContext>()
            .UseSqlite($"Data Source={_pokemonDb}")
            .Options;
        return new PokemonDbContext(options);
    }

    // Bulbasaur's real Gen 1 level-up learnset (move id, learn level).
    private static readonly (int MoveId, int Level)[] BulbasaurLearnset =
    [
        (33, 1), (45, 1), (73, 7), (22, 13), (77, 20), (75, 27), (74, 34), (79, 41), (76, 48),
    ];

    private static void SeedBulbasaur(PokemonDbContext ctx)
    {
        ctx.Species.Add(new PokemonSpecies
        {
            Id = 1, Name = "bulbasaur", Type1 = DamageType.Grass, Type2 = DamageType.Poison,
            GrowthRate = GrowthRate.MediumSlow,
        });
        ctx.SaveChanges();

        ctx.Learnsets.AddRange(BulbasaurLearnset.Select(r => new PokemonLearnset
        {
            SpeciesId = 1, MoveId = r.MoveId, LearnLevel = r.Level, Generation = 1,
        }));
        ctx.SaveChanges();
    }

    // In-memory Attack table keyed by the same move ids the learnset references.
    private static IReadOnlyDictionary<int, Attack> BulbasaurMoves() => new[]
    {
        new Attack("tackle",       "") { Id = 33, BaseDamage = 40,  DamageType = DamageType.Normal },
        new Attack("growl",        "") { Id = 45, BaseDamage = 0,   DamageType = DamageType.Normal },
        new Attack("leech-seed",   "") { Id = 73, BaseDamage = 0,   DamageType = DamageType.Grass },
        new Attack("vine-whip",    "") { Id = 22, BaseDamage = 45,  DamageType = DamageType.Grass },
        new Attack("poisonpowder", "") { Id = 77, BaseDamage = 0,   DamageType = DamageType.Poison },
        new Attack("razor-leaf",   "") { Id = 75, BaseDamage = 55,  DamageType = DamageType.Grass },
        new Attack("growth",       "") { Id = 74, BaseDamage = 0,   DamageType = DamageType.Normal },
        new Attack("sleep-powder", "") { Id = 79, BaseDamage = 0,   DamageType = DamageType.Grass },
        new Attack("solar-beam",   "") { Id = 76, BaseDamage = 120, DamageType = DamageType.Grass },
    }.ToDictionary(a => a.Id);

    private static List<PokemonLearnset> LoadGen1Learnset(PokemonDbContext ctx, int speciesId) =>
        ctx.Learnsets.AsNoTracking()
            .Where(l => l.SpeciesId == speciesId && l.Generation == 1)
            .ToList();

    [Fact]
    public void Learnset_FromDb_CanonicalSelection_IsSpeciesLegalAndMostRecent()
    {
        using var ctx = BuildContext();
        ctx.EnsureDatabaseCreated();
        SeedBulbasaur(ctx);

        var species  = ctx.Species.AsNoTracking().Single(s => s.Id == 1);
        var learnset = LoadGen1Learnset(ctx, 1);

        var moves = LearnsetMoveSelector.Select(
            MoveSelectionStrategy.CanonicalLatest, learnset, BulbasaurMoves(),
            level: 50, species.Type1, species.Type2);

        // The 4 highest-level moves ≤ 50: Razor Leaf(27), Growth(34), Sleep Powder(41),
        // Solar Beam(48) — returned in ascending learn-level order.
        Assert.Equal(new[] { 75, 74, 79, 76 }, moves.Select(m => m.Id).ToArray());

        // Every chosen move is genuinely in the species' learnset.
        var legalMoveIds = learnset.Select(l => l.MoveId).ToHashSet();
        Assert.All(moves, m => Assert.Contains(m.Id, legalMoveIds));
    }

    [Fact]
    public void Learnset_FromDb_LowLevel_ExcludesUnlearnedMoves()
    {
        using var ctx = BuildContext();
        ctx.EnsureDatabaseCreated();
        SeedBulbasaur(ctx);

        var species  = ctx.Species.AsNoTracking().Single(s => s.Id == 1);
        var learnset = LoadGen1Learnset(ctx, 1);

        var moves = LearnsetMoveSelector.Select(
            MoveSelectionStrategy.CanonicalLatest, learnset, BulbasaurMoves(),
            level: 10, species.Type1, species.Type2);

        // At L10 only Tackle(1), Growl(1), Leech Seed(7) are learnable → all 3 returned.
        Assert.Equal(new[] { 33, 45, 73 }, moves.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Learnset_FromDb_GenerationFilter_IsolatesGenerations()
    {
        using var ctx = BuildContext();
        ctx.EnsureDatabaseCreated();
        SeedBulbasaur(ctx);

        // A later-generation row for the same species must never leak into a Gen 1 query.
        ctx.Learnsets.Add(new PokemonLearnset { SpeciesId = 1, MoveId = 999, LearnLevel = 1, Generation = 2 });
        ctx.SaveChanges();

        var gen1 = LoadGen1Learnset(ctx, 1);

        Assert.Equal(BulbasaurLearnset.Length, gen1.Count);
        Assert.DoesNotContain(gen1, l => l.MoveId == 999);
    }

    [Fact]
    public void Learnset_FromDb_WeightedSmart_IsLegalAndAlwaysHasAnAttack()
    {
        using var ctx = BuildContext();
        ctx.EnsureDatabaseCreated();
        SeedBulbasaur(ctx);

        var species  = ctx.Species.AsNoTracking().Single(s => s.Id == 1);
        var learnset = LoadGen1Learnset(ctx, 1);
        var movesById = BulbasaurMoves();
        var legalMoveIds = learnset.Select(l => l.MoveId).ToHashSet();

        for (int seed = 0; seed < 25; seed++)
        {
            var moves = LearnsetMoveSelector.Select(
                MoveSelectionStrategy.WeightedSmart, learnset, movesById,
                level: 50, species.Type1, species.Type2, new SeededRandomSource(seed));

            Assert.Equal(4, moves.Count);
            Assert.All(moves, m => Assert.Contains(m.Id, legalMoveIds));
            Assert.Contains(moves, m => m.BaseDamage > 0);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_pokemonDb)) File.Delete(_pokemonDb);
    }
}
