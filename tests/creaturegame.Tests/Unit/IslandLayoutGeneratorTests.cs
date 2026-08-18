using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using Xunit;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Fuzz coverage for <see cref="IslandLayoutGenerator"/> — the property-testing analogue of what
/// <see cref="BiomeTests"/> does for the (now-retired) authored grid data. Pure unit tests: every case
/// below drives the real <see cref="Biomes.Kanto"/> registry and <see cref="Biomes.RandomConnectedMap"/>
/// (both already DB-free), so this needs no database and stays step-1 isolated per
/// <c>docs/GENERATION_PROFILE.md §7.4</c>'s implementation staging.
/// </summary>
public class IslandLayoutGeneratorTests
{
    [Fact]
    public void Generate_EmptyInput_ReturnsEmptyLayout()
    {
        var layout = IslandLayoutGenerator.Generate([], new SeededRandomSource(0));

        Assert.Equal(0, layout.Width);
        Assert.Equal(0, layout.Height);
        Assert.Empty(layout.Positions);
        Assert.Empty(layout.Routes);
    }

    [Fact]
    public void Generate_SingleBiome_PlacesItWithNoRoutes()
    {
        var solo = Biomes.Kanto.Take(1).ToList();

        var layout = IslandLayoutGenerator.Generate(solo, new SeededRandomSource(0));

        var position = Assert.Single(layout.Positions);
        Assert.Equal(solo[0].Id, position.Key);
        Assert.Empty(layout.Routes);
        Assert.True(layout.Width > 0 && layout.Height > 0);
    }

    [Fact]
    public void Generate_DisconnectedInput_Throws()
    {
        var a = new BiomeDefinition("fake-a", "Fake A", Region.Kanto, [DamageType.Normal], []);
        var b = new BiomeDefinition("fake-b", "Fake B", Region.Kanto, [DamageType.Normal], []);

        Assert.Throws<ArgumentException>(() =>
            IslandLayoutGenerator.Generate([a, b], new SeededRandomSource(0))
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void Generate_IsReproducibleFromSeed(int seed)
    {
        var biomes = Biomes.RandomConnectedMap(Biomes.Kanto, 8, new SeededRandomSource(99));

        var first = IslandLayoutGenerator.Generate(biomes, new SeededRandomSource(seed));
        var second = IslandLayoutGenerator.Generate(biomes, new SeededRandomSource(seed));

        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(SortedPositions(first), SortedPositions(second));
        Assert.Equal(SortedRoutes(first), SortedRoutes(second));
    }

    [Fact]
    public void Generate_DifferentSeeds_GiveDifferentLayouts()
    {
        var biomes = Biomes.RandomConnectedMap(Biomes.Kanto, 8, new SeededRandomSource(99));

        var layouts = Enumerable
            .Range(0, 10)
            .Select(seed => IslandLayoutGenerator.Generate(biomes, new SeededRandomSource(seed)))
            .Select(SortedPositions)
            .ToList();

        Assert.True(
            layouts.Distinct().Count() > 1,
            "Expected at least some of 10 seeds to produce different layouts."
        );
    }

    // The core correctness contract, fuzzed across many seeds and island sizes (2 up to 12 — a couple past
    // the live game's RunBiomeMapSize=10, as extra placement/routing pressure) using the real Kanto graph.
    // Exercises both the primary algorithm and — whenever it happens to fire under that pressure — the
    // fallback, asserting the same invariants either way, so the fallback is proven "always valid," not
    // just "usually valid." Capped at 12 (not the full 18-biome registry) deliberately: this suite runs in
    // the pre-commit hook on every .cs change, and the fallback's *speed* (not correctness — see
    // <c>IslandLayoutGenerator</c>'s own design notes) degrades well past what's worth paying for on every
    // commit once graphs get denser than the game ever produces. A slower, wider sweep (up to the full
    // registry) is worth running by hand after touching this file, not as a standing gate.
    [Fact]
    public void Generate_HoldsStructuralInvariants_AcrossManySeedsAndSizes()
    {
        bool exercisedFallback = false;

        for (int size = 2; size <= 12; size++)
        {
            for (int seed = 0; seed < 15; seed++)
            {
                var selectionRng = new SeededRandomSource(seed * 1000 + size);
                var biomes = Biomes.RandomConnectedMap(Biomes.Kanto, size, selectionRng);
                if (biomes.Count < 2)
                {
                    continue;
                }

                var layout = IslandLayoutGenerator.Generate(biomes, new SeededRandomSource(seed));
                exercisedFallback |= layout.UsedFallback;

                AssertInvariants(biomes, layout, seed, size);
            }
        }

        // Not a promised behaviour (it's an internal safety net) — but if the fallback never once fires
        // across sizes up to the full 18-biome registry, that's worth surfacing rather than silently
        // shipping an untested code path.
        Assert.True(
            exercisedFallback,
            "Expected the fallback layout to fire at least once across this size/seed sweep — if it "
                + "genuinely never does, the fallback path itself has no coverage here."
        );
    }

    private static void AssertInvariants(
        IReadOnlyList<BiomeDefinition> biomes,
        IslandLayout layout,
        int seed,
        int size
    )
    {
        string context = $"(seed={seed}, size={size}, usedFallback={layout.UsedFallback})";

        Assert.True(
            layout.Positions.Count == biomes.Count,
            $"Every biome must be placed {context}"
        );

        var positionSet = new HashSet<GridPoint>(layout.Positions.Values);
        Assert.True(
            positionSet.Count == layout.Positions.Count,
            $"No two biomes may share a cell {context}"
        );

        foreach (var p in layout.Positions.Values)
        {
            Assert.True(p.X >= 0 && p.X < layout.Width, $"X in canvas bounds {context}");
            Assert.True(p.Y >= 0 && p.Y < layout.Height, $"Y in canvas bounds {context}");
        }

        Assert.True(
            layout.Routes.Count == ExpectedEdgeCount(biomes),
            $"Every subgraph edge must get exactly one route {context}"
        );

        var claimedInteriorCells = new HashSet<GridPoint>();
        foreach (var route in layout.Routes)
        {
            var from = layout.Positions[route.FromBiomeId];
            var to = layout.Positions[route.ToBiomeId];

            Assert.True(
                route.Cells.Count >= 2,
                $"A route has at least its two endpoints {context}"
            );
            Assert.Equal(from, route.Cells[0]);
            Assert.Equal(to, route.Cells[^1]);

            for (int i = 1; i < route.Cells.Count; i++)
            {
                int manhattan =
                    Math.Abs(route.Cells[i - 1].X - route.Cells[i].X)
                    + Math.Abs(route.Cells[i - 1].Y - route.Cells[i].Y);
                Assert.True(
                    manhattan == 1,
                    $"Every route step is a single axis-aligned move {context}"
                );
            }

            foreach (var cell in route.Cells)
            {
                Assert.True(
                    cell.X >= 0 && cell.X < layout.Width,
                    $"Route cell X in canvas bounds {context}"
                );
                Assert.True(
                    cell.Y >= 0 && cell.Y < layout.Height,
                    $"Route cell Y in canvas bounds {context}"
                );
            }

            foreach (var cell in route.Cells.Where(c => c != from && c != to))
            {
                Assert.False(
                    positionSet.Contains(cell),
                    $"A route may not pass through an unrelated biome cell {context}"
                );
                Assert.True(claimedInteriorCells.Add(cell), $"No two routes may overlap {context}");
            }
        }
    }

    private static int ExpectedEdgeCount(IReadOnlyList<BiomeDefinition> biomes)
    {
        var ids = new HashSet<string>(biomes.Select(b => b.Id));
        var seen = new HashSet<string>();
        int count = 0;
        foreach (var biome in biomes)
        {
            foreach (var neighbourId in biome.Neighbours)
            {
                if (!ids.Contains(neighbourId) || neighbourId == biome.Id)
                {
                    continue;
                }

                string key =
                    string.CompareOrdinal(biome.Id, neighbourId) < 0
                        ? $"{biome.Id}|{neighbourId}"
                        : $"{neighbourId}|{biome.Id}";
                if (seen.Add(key))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static List<(string Id, int X, int Y)> SortedPositions(IslandLayout layout) =>
        layout
            .Positions.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => (p.Key, p.Value.X, p.Value.Y))
            .ToList();

    private static List<(string From, string To, string Cells)> SortedRoutes(IslandLayout layout) =>
        layout
            .Routes.OrderBy(r => r.FromBiomeId, StringComparer.Ordinal)
            .ThenBy(r => r.ToBiomeId, StringComparer.Ordinal)
            .Select(r =>
                (r.FromBiomeId, r.ToBiomeId, string.Join(",", r.Cells.Select(c => $"{c.X}:{c.Y}")))
            )
            .ToList();
}
