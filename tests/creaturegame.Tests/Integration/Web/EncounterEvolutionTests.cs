using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Web.Battle;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// End-to-end of the evolution data path the run loop uses: <see cref="EncounterFactory.ResolvePlayerEvolutionAsync"/>
/// over the live <c>pokemon.db</c> — DB edges → the Gen 1 <c>IEvolutionRules</c> decision → resolved evolved
/// species. Proves the three trigger interpretations against real data: level evolutions fire at their
/// threshold, the trade lines fire at the converted level 37, and stone lines stay dormant on a level-up.
/// <para>Requires the databases populated — run <c>PokeApiConnector</c> on a fresh checkout.</para>
/// </summary>
public class EncounterEvolutionTests
{
    private static EncounterFactory BuildFactory() =>
        new(
            new LiveEvoDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()),
            new LiveEvoDbContextFactory<MovesDbContext>(() => new MovesDbContext())
        );

    private const int Bulbasaur = 1;
    private const int Pikachu = 25;
    private const int Machoke = 67;

    private static async Task<EvolutionOutcomeView> ResolveAsync(int speciesId, int level)
    {
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(
            speciesId,
            level,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);
        var outcome = await factory.ResolvePlayerEvolutionAsync(setup!.Player, setup.AllMoves);
        return new EvolutionOutcomeView(
            outcome?.NewForm.Id,
            outcome?.NewForm.Name,
            outcome?.NewLearnset.Count
        );
    }

    [Fact]
    public async Task LevelEvolution_FiresAtThreshold()
    {
        var atThreshold = await ResolveAsync(Bulbasaur, 16); // Bulbasaur → Ivysaur @16
        Assert.Equal(2, atThreshold.ToSpeciesId);
        Assert.Equal("ivysaur", atThreshold.ToName);
        Assert.True(atThreshold.LearnsetCount > 0); // evolved form's learnset is seated
    }

    [Fact]
    public async Task LevelEvolution_DoesNotFireBelowThreshold()
    {
        var below = await ResolveAsync(Bulbasaur, 15);
        Assert.Null(below.ToSpeciesId);
    }

    [Fact]
    public async Task TradeEvolution_FiresAtConvertedLevel37()
    {
        var at37 = await ResolveAsync(Machoke, 37); // Machoke → Machamp, trade → level 37
        Assert.Equal(68, at37.ToSpeciesId);
        Assert.Equal("machamp", at37.ToName);

        var at36 = await ResolveAsync(Machoke, 36);
        Assert.Null(at36.ToSpeciesId);
    }

    [Fact]
    public async Task StoneEvolution_StaysDormantOnLevelUp()
    {
        // Pikachu → Raichu is a Thunder Stone evolution; with no bag, a level-up never triggers it.
        var high = await ResolveAsync(Pikachu, 100);
        Assert.Null(high.ToSpeciesId);
    }

    private readonly record struct EvolutionOutcomeView(
        int? ToSpeciesId,
        string? ToName,
        int? LearnsetCount
    );
}

/// <summary>Minimal <see cref="IDbContextFactory{TContext}"/> over a context ctor, for building an
/// <see cref="EncounterFactory"/> against the live databases (mirrors the helper in the seed-repro tests).</summary>
file sealed class LiveEvoDbContextFactory<TContext>(Func<TContext> create)
    : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
}
