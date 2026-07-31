using System.Text.Json;
using creaturegame.DB;
using creaturegame.Generations;
using creaturegame.Tests.TestSupport;
using creaturegame.Tests.Unit;
using creaturegame.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The starter picker's dex read (Stage 3 of <c>docs/GENERATION_PROFILE.md</c>): the one species read that
/// answers <i>before</i> a run exists, which is why Stage 2b had to leave it unscoped — there was no profile to
/// ask. The request now names the generation, so the picker is server-authoritative: which starters are
/// offerable is decided here by the profile's content scope, not by whatever the client happens to render.
/// <para>Runs against the live <c>pokemon.db</c>, like the sibling <c>EncounterFactory</c> probes.</para>
/// </summary>
public class SpeciesControllerTests
{
    private static SpeciesController Build() =>
        new(new LiveDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()));

    [Fact]
    public async Task DexFor_DrawsThroughTheProfilesContentScope_SoThePickerIsServerAuthoritative()
    {
        // The falsification leg (§4.2): Gen 1's scope is an identity, so a read that skipped it entirely would
        // be indistinguishable from one that asks — only the alt profile's restrictive scope can observe it.
        var alt = await Build().DexForAsync(TestAltProfile.Instance);

        Assert.NotEmpty(alt); // narrowed, not emptied — vacuity guard
        Assert.All(alt, s => Assert.True(s.Id <= TestAltProfile.MaxContentId));

        // Control: the Gen 1 dex still spans the whole table, exactly as the picker always showed.
        var gen1 = await Build().DexForAsync(Gen1Profile.Instance);
        Assert.Contains(gen1, s => s.Id > TestAltProfile.MaxContentId);
    }

    [Fact]
    public async Task GetAll_ServesTheDexInIdOrder_WithTheContractTheClientRenders()
    {
        // The action itself: parse → profile → scoped read. With only Gen 1 registered the parse cannot be
        // falsified end-to-end here (ParseGeneration has its own boundary tests); what this pins is the
        // response contract the picker renders — an OK list in dex id order with the card fields populated.
        var result = await Build().GetAll(generation: null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dex = Assert.IsAssignableFrom<IReadOnlyList<SpeciesSummaryDto>>(ok.Value);
        Assert.NotEmpty(dex);
        Assert.Equal(dex.Select(s => s.Id).Order(), dex.Select(s => s.Id));
        Assert.All(dex, s => Assert.False(string.IsNullOrEmpty(s.Name)));
        Assert.All(dex, s => Assert.False(string.IsNullOrEmpty(s.Type1)));
        Assert.All(
            dex,
            s =>
                Assert.Equal(
                    s.BaseHp + s.BaseAttack + s.BaseDefense + s.BaseSpecial + s.BaseSpeed,
                    s.BaseStatTotal
                )
        );
    }

    /// <summary>
    /// The DTO's serialized key set is exactly what the client's <c>Species</c> type reads.
    /// </summary>
    /// <remarks>
    /// The old anonymous object hardcoded the wire names (<c>baseHp</c>, <c>baseStatTotal</c>, …); the named
    /// <see cref="SpeciesSummaryDto"/> gets its casing from the serializer's camelCase policy, so a
    /// "consistency" rename of a property (e.g. <c>BaseHp</c> → <c>BaseHP</c>, to match
    /// <c>PokemonSpecies.BaseHP</c>) would silently ship <c>baseHP</c> and NaN the TS client's <c>baseHp</c>
    /// read — and the DTO-property-reading contract test above could not see it. Serializing through
    /// <see cref="JsonSerializerDefaults.Web"/> (the same camelCase policy ASP.NET applies) and pinning the
    /// exact key set is what makes the rename fail a test instead of the picker. (`pr-review`, 2026-07-31.)
    /// </remarks>
    [Fact]
    public void SpeciesSummaryDto_SerializesToExactlyTheKeysTheClientReads()
    {
        var dto = SpeciesSummaryDto.From(
            new PokemonSpecies
            {
                Id = 1,
                Name = "bulbasaur",
                Type1 = creaturegame.Attacks.DamageType.Grass,
                Type2 = creaturegame.Attacks.DamageType.Poison,
                BaseHP = 45,
                BaseAttack = 49,
                BaseDefense = 49,
                BaseSpecial = 65,
                BaseSpeed = 45,
            }
        );

        var json = JsonSerializer.Serialize(
            dto,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        // Order-sensitive on purpose: it is the DTO's declaration order, so an inserted/renamed/reordered
        // property shows up as a readable diff of the whole wire shape, not a Contains miss.
        Assert.Equal(
            new[]
            {
                "id",
                "name",
                "type1",
                "type2",
                "baseHp",
                "baseAttack",
                "baseDefense",
                "baseSpecial",
                "baseSpeed",
                "baseStatTotal",
            },
            keys
        );
    }
}
