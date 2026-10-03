using creaturegame.DB;
using creaturegame.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Pins the importer's <b>Gen 1 species numbers</b> in the live <c>pokemon.db</c> against an independent
/// pret/pokered snapshot (<see cref="PokeredGen1Species"/>) — the species analogue of
/// <c>SecondaryChanceDataContractTests</c> and <c>PokemonEvolutionDataContractTests</c>. PokeAPI serves the
/// <i>modern</i> base stats (Sp.Atk where Gen 1 had Special, Gen 6/7 buffs) and base experience, so the importer's
/// <c>past_stats</c> resolution and curated base-experience table are doing real work; without this pin a re-import
/// or an upstream change could move the data while every seam test (hand-built creatures) stays green.
/// <para>Requires <c>pokemon.db</c> populated — run <c>PokeApiConnector</c> on a fresh checkout.</para>
/// </summary>
public class Gen1SpeciesDataContractTests
{
    private static Dictionary<int, PokemonSpecies> LoadSpecies()
    {
        using var ctx = new PokemonDbContext();
        var rows = ctx.Species.AsNoTracking().ToDictionary(s => s.Id);
        if (rows.Count == 0)
            throw new InvalidOperationException(
                "pokemon.db has no species. Run `dotnet run --project PokeApiConnector -- species`."
            );
        return rows;
    }

    [Fact]
    public void EveryGen1SpeciesMatchesPokeredStatsBaseExperienceAndCatchRate()
    {
        var species = LoadSpecies();
        var mismatches = new List<string>();

        foreach (var want in PokeredGen1Species.All)
        {
            if (!species.TryGetValue(want.Id, out var got))
            {
                mismatches.Add($"#{want.Id} {want.Name}: missing from pokemon.db");
                continue;
            }

            var expected = (
                want.Hp,
                want.Attack,
                want.Defense,
                want.Speed,
                want.Special,
                want.BaseExperience,
                want.CatchRate
            );
            var actual = (
                got.BaseHP,
                got.BaseAttack,
                got.BaseDefense,
                got.BaseSpeed,
                got.BaseSpecial,
                got.BaseExperience,
                got.CatchRate
            );
            if (expected != actual)
                mismatches.Add(
                    $"#{want.Id} {want.Name}: hp/atk/def/spd/spc/exp/catch expected {expected}, got {actual}"
                );
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void TheDatabaseHoldsExactlyTheGen1Dex()
    {
        var species = LoadSpecies();

        Assert.Equal(151, species.Count);
        Assert.Equal(PokeredGen1Species.All.Select(r => r.Id).Order(), species.Keys.Order());
    }

    [Theory]
    [InlineData("chansey", 250, 5, 5, 50, 105)] // modern Sp.Atk 35 — Gen 1 Special 105
    [InlineData("gyarados", 95, 125, 79, 81, 100)] // modern Sp.Atk 60
    [InlineData("pikachu", 35, 55, 30, 90, 50)] // modern Defense 40 (a Gen 6 buff)
    public void SpeciesWhoseModernStatsDiffer_HaveTheirGen1BaseStats(
        string name,
        int hp,
        int atk,
        int def,
        int spd,
        int spc
    )
    {
        var s = LoadSpecies().Values.Single(x => x.Name == name);

        Assert.Equal(
            (hp, atk, def, spd, spc),
            (s.BaseHP, s.BaseAttack, s.BaseDefense, s.BaseSpeed, s.BaseSpecial)
        );
    }
}
