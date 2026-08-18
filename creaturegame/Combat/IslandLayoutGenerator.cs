using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>A single grid cell on the Town Map's rigid orthogonal grid.</summary>
public readonly record struct GridPoint(int X, int Y);

/// <summary>
/// A collision-free, axis-aligned route between two biomes' grid cells — <see cref="Cells"/> is the full
/// path including both endpoints (the two biome cells themselves), consecutive cells always exactly one
/// step apart on a single axis (never a diagonal or a curve).
/// </summary>
public sealed record IslandRoute(
    string FromBiomeId,
    string ToBiomeId,
    IReadOnlyList<GridPoint> Cells
);

/// <summary>
/// The generated Town Map layout for one run's biome subgraph: a grid position per biome and a route per
/// neighbour edge, all within a <see cref="Width"/> x <see cref="Height"/> canvas sized to fit exactly what
/// was placed (plus a small border margin).
/// </summary>
public sealed record IslandLayout(
    int Width,
    int Height,
    IReadOnlyDictionary<string, GridPoint> Positions,
    IReadOnlyList<IslandRoute> Routes,
    bool UsedFallback
);

/// <summary>
/// Procedurally lays out an already-selected, connected biome subgraph (see
/// <see cref="Biomes.RandomConnectedMap"/>) onto a rigid orthogonal grid — a position per biome and a
/// collision-free, axis-aligned route per neighbour edge within the subgraph.
///
/// <para>This is a purely geometric layer on top of the existing run-graph selection — it does not choose
/// which biomes are in a run or which are neighbours (that stays <see cref="Biomes.RandomConnectedMap"/>'s
/// job, untouched); it only decides where the already-chosen graph sits on the Town Map grid. See
/// <c>docs/GENERATION_PROFILE.md §7.4</c> for the design record — this supersedes the section's original
/// "authored Kanto grid" plan (revised 2026-08-18): a fresh, sparse per-run island is a smaller layout
/// problem than the full 18-biome registry the original plan considered and rejected procedural routing
/// for.</para>
///
/// <para><b>Determinism.</b> Draws only from the supplied <see cref="IRandomSource"/>, in an order derived
/// solely from the input list's own order (biome/neighbour iteration, never unordered-collection
/// enumeration) — so the same seed and the same biome subgraph always produce the same layout, matching
/// every other per-run RNG draw in this codebase (<c>docs/GAME_LOOP.md</c>: "same state + same seed ⇒ same
/// event sequence"). Callers must pass the *same* shared <see cref="IRandomSource"/> instance the rest of
/// the run draws from, not a fresh one — see <see cref="Combat.SeededRandomSource"/>.</para>
///
/// <para><b>Safety net.</b> A tight, fast primary placement is tried a few times first; if that doesn't pan
/// out, a fallback placement (an exhaustive ring search — see <see cref="FallbackPlacement"/> — that can
/// never itself fail to find a free cell) is retried across a bounded number of spacing levels and
/// reshuffled placement attempts, paired with a routing pass that does real local backtracking (see
/// <see cref="TryRouteAll"/>) rather than just failing on the first stuck edge. Only if every one of those
/// combinations is exhausted does this throw — see <c>IslandLayoutGeneratorTests</c> for the fuzz coverage
/// that exercises this path directly and has never needed to fall back that far.
/// <see cref="IslandLayout.UsedFallback"/> reports whether the primary or the fallback placement ran.</para>
/// </summary>
public static class IslandLayoutGenerator
{
    private const int PlacementSpacing = 2;
    private const int PlacementMaxSpacing = 4;
    private const int PrimaryAttempts = 3;
    private const int FallbackSpacing = 4;
    private const int FallbackSpacingLevels = 5; // 4, 8, 16, 32, 64
    private const int FallbackPlacementAttemptsPerSpacing = 16;
    private const int Margin = 1;
    private const int RoutingSearchMargin = 4;

    // Fixed, not scaled with spacing: the backtracking router (see TryRouteAll) resolves the vast majority
    // of cases at the base spacing with room to spare, so a detour only ever needs to clear a handful of
    // obstacle cells, not scale with the whole canvas's area. An earlier version scaled this with spacing
    // (multiplicatively, then linearly) specifically to fix cases that turned out to be a *different* bug
    // (a placement ring-search bias, and a router with no real backtracking) — once those were fixed, this
    // fixed margin plus a few independently-reshuffled placement attempts (below) resolved everything the
    // fuzz suite could find, at a small fraction of the cost: a scaled margin makes the BFS search box grow
    // with spacing, which made failing high-spacing attempts prohibitively expensive.
    private const int FallbackRoutingSearchMargin = 40;

    private static readonly (int Dx, int Dy)[] Directions = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    public static IslandLayout Generate(IReadOnlyList<BiomeDefinition> biomes, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(rng);

        if (biomes.Count == 0)
        {
            return new IslandLayout(
                0,
                0,
                new Dictionary<string, GridPoint>(),
                [],
                UsedFallback: false
            );
        }

        var nodeIds = new HashSet<string>(biomes.Select(b => b.Id));
        var edges = BuildEdgeList(biomes, nodeIds);
        var adjacency = BuildAdjacency(biomes, edges);

        if (!IsConnected(biomes, adjacency))
        {
            throw new ArgumentException(
                "IslandLayoutGenerator requires a connected biome subgraph — pass the result of "
                    + "Biomes.RandomConnectedMap (or an equivalently connected subset), not an arbitrary list.",
                nameof(biomes)
            );
        }

        if (biomes.Count == 1)
        {
            var solo = new Dictionary<string, GridPoint> { [biomes[0].Id] = new GridPoint(0, 0) };
            return Finalize(solo, [], usedFallback: false);
        }

        for (int attempt = 0; attempt < PrimaryAttempts; attempt++)
        {
            if (
                TryPrimaryPlacement(biomes, adjacency, rng, out var positions)
                && TryRouteAll(positions, edges, RoutingSearchMargin, out var routes)
            )
            {
                return Finalize(positions, routes, usedFallback: false);
            }
        }

        // The backtracking router (TryRouteAll) resolves the large majority of graphs on the very first
        // placement at the base spacing. For the rest, re-rolling placement a few times (PlaceNearExhaustive
        // draws from the same shared rng, so each attempt is a genuinely different layout, not just a
        // rescaled one) resolves nearly everything else — only escalating spacing itself as a last resort,
        // since a bigger canvas is the most expensive lever to pull.
        int spacing = FallbackSpacing;
        for (int level = 0; level < FallbackSpacingLevels; level++, spacing *= 2)
        {
            for (int attempt = 0; attempt < FallbackPlacementAttemptsPerSpacing; attempt++)
            {
                var fallbackPositions = FallbackPlacement(biomes, adjacency, spacing, rng);
                if (
                    TryRouteAll(
                        fallbackPositions,
                        edges,
                        FallbackRoutingSearchMargin,
                        out var fallbackRoutes
                    )
                )
                {
                    return Finalize(fallbackPositions, fallbackRoutes, usedFallback: true);
                }
            }
        }

        // Only reachable if routing still fails at the widest escalation step — see
        // IslandLayoutGeneratorTests's fuzz coverage, which exercises this path directly across many real
        // seeds/sizes without ever reaching here. Fail loudly rather than silently ship a broken map.
        throw new InvalidOperationException(
            $"IslandLayoutGenerator: fallback routing failed for a {biomes.Count}-biome graph even after "
                + $"{FallbackSpacingLevels} spacing levels × {FallbackPlacementAttemptsPerSpacing} placement "
                + "attempts each — investigate the input graph shape."
        );
    }

    // ---- edge / adjacency bookkeeping -------------------------------------------------------------

    private static List<(string A, string B)> BuildEdgeList(
        IReadOnlyList<BiomeDefinition> biomes,
        HashSet<string> nodeIds
    )
    {
        var seen = new HashSet<string>();
        var edges = new List<(string, string)>();
        foreach (var biome in biomes)
        {
            foreach (var neighbourId in biome.Neighbours)
            {
                if (!nodeIds.Contains(neighbourId) || neighbourId == biome.Id)
                {
                    continue;
                }

                string key =
                    string.CompareOrdinal(biome.Id, neighbourId) < 0
                        ? $"{biome.Id}|{neighbourId}"
                        : $"{neighbourId}|{biome.Id}";
                if (seen.Add(key))
                {
                    edges.Add((biome.Id, neighbourId));
                }
            }
        }

        return edges;
    }

    private static Dictionary<string, List<string>> BuildAdjacency(
        IReadOnlyList<BiomeDefinition> biomes,
        List<(string A, string B)> edges
    )
    {
        var adjacency = biomes.ToDictionary(b => b.Id, _ => new List<string>());
        foreach (var (a, b) in edges)
        {
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        return adjacency;
    }

    private static bool IsConnected(
        IReadOnlyList<BiomeDefinition> biomes,
        Dictionary<string, List<string>> adjacency
    )
    {
        var visited = new HashSet<string> { biomes[0].Id };
        var stack = new Stack<string>();
        stack.Push(biomes[0].Id);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var neighbour in adjacency[current])
            {
                if (visited.Add(neighbour))
                {
                    stack.Push(neighbour);
                }
            }
        }

        return visited.Count == biomes.Count;
    }

    // ---- placement ----------------------------------------------------------------------------------

    private static bool TryPrimaryPlacement(
        IReadOnlyList<BiomeDefinition> biomes,
        Dictionary<string, List<string>> adjacency,
        IRandomSource rng,
        out Dictionary<string, GridPoint> positions
    )
    {
        positions = new Dictionary<string, GridPoint> { [biomes[0].Id] = new GridPoint(0, 0) };
        var occupied = new HashSet<GridPoint> { new GridPoint(0, 0) };

        var queue = new Queue<string>();
        queue.Enqueue(biomes[0].Id);
        var visited = new HashSet<string> { biomes[0].Id };

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var currentPos = positions[currentId];

            foreach (var neighbourId in adjacency[currentId])
            {
                if (!visited.Add(neighbourId))
                {
                    continue;
                }

                if (!TryPlaceNear(currentPos, occupied, rng, out var placed))
                {
                    return false;
                }

                positions[neighbourId] = placed;
                occupied.Add(placed);
                queue.Enqueue(neighbourId);
            }
        }

        return true;
    }

    private static bool TryPlaceNear(
        GridPoint from,
        HashSet<GridPoint> occupied,
        IRandomSource rng,
        out GridPoint placed
    )
    {
        var dirs = ShuffledDirections(rng);

        for (int dist = PlacementSpacing; dist <= PlacementMaxSpacing; dist++)
        {
            foreach (var dir in dirs)
            {
                var candidate = new GridPoint(from.X + dir.Dx * dist, from.Y + dir.Dy * dist);
                if (!occupied.Contains(candidate))
                {
                    placed = candidate;
                    return true;
                }
            }
        }

        placed = default;
        return false;
    }

    private static (int Dx, int Dy)[] ShuffledDirections(IRandomSource rng)
    {
        var dirs = ((int, int)[])Directions.Clone();
        for (int i = dirs.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
        }

        return dirs;
    }

    /// <summary>
    /// No randomness: the same BFS-outward-from-parent shape as <see cref="TryPrimaryPlacement"/>, but each
    /// node's cell is found via an <em>exhaustive, deterministic expanding-ring search</em> (see
    /// <see cref="RingCells"/>) instead of a small fixed set of candidate offsets — since the grid is
    /// unbounded and only finitely many cells are ever occupied, this is guaranteed to find a free cell at
    /// some ring radius, so placement itself can never fail.
    /// <para>Two earlier fallback designs were tried and rejected here, kept as history for whoever touches
    /// this next: (1) a depth-column grid (BFS depth = column, index-within-depth = row) could leave a
    /// same-depth edge's two endpoints separated by a third node sitting directly between them — no routing
    /// margin reliably out-searched that once other routes added congestion. (2) a fully-unique-row-and-
    /// column diagonal placement solved that specific pathology (a route can only ever be blocked by a
    /// <em>foreign route</em>, never a foreign node) but traded it for worse congestion: every node sitting
    /// on one exact diagonal line means several long edges all have to weave around <em>every</em>
    /// intermediate node <em>and each other</em> on that same narrow line. Spreading nodes across full 2D
    /// space (this version) avoids both failure modes — routes get room to route around each other because
    /// nodes aren't funneled onto one line or one column-block in the first place.</para>
    /// </summary>
    private static Dictionary<string, GridPoint> FallbackPlacement(
        IReadOnlyList<BiomeDefinition> biomes,
        Dictionary<string, List<string>> adjacency,
        int spacing,
        IRandomSource rng
    )
    {
        var positions = new Dictionary<string, GridPoint> { [biomes[0].Id] = new GridPoint(0, 0) };
        var occupied = new HashSet<GridPoint> { new GridPoint(0, 0) };

        var queue = new Queue<string>();
        queue.Enqueue(biomes[0].Id);
        var visited = new HashSet<string> { biomes[0].Id };

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var currentPos = positions[currentId];

            foreach (var neighbourId in adjacency[currentId])
            {
                if (!visited.Add(neighbourId))
                {
                    continue;
                }

                var placed = PlaceNearExhaustive(currentPos, occupied, spacing, rng);
                positions[neighbourId] = placed;
                occupied.Add(placed);
                queue.Enqueue(neighbourId);
            }
        }

        return positions;
    }

    /// <summary>
    /// Ring search with a randomized starting point every call — <b>critical</b>, not cosmetic. An earlier
    /// version always scanned each ring from the same corner, which on open (unoccupied) space always
    /// returns that same corner as the first free cell — so a chain of placements along a BFS path all drift
    /// in the same direction call after call, recreating the exact diagonal-pileup pathology this replaced
    /// (see <see cref="FallbackPlacement"/>'s design note), just reached by a subtler route. Rotating the
    /// scan start (via the shared, already-seeded <paramref name="rng"/> — so this stays fully deterministic
    /// per run seed) breaks that bias while keeping placement itself unconditionally guaranteed to succeed.
    /// </summary>
    private static GridPoint PlaceNearExhaustive(
        GridPoint from,
        HashSet<GridPoint> occupied,
        int startRadius,
        IRandomSource rng
    )
    {
        for (int radius = startRadius; ; radius++)
        {
            var ring = RingCells(from, radius);
            int offset = rng.Next(ring.Count);
            for (int i = 0; i < ring.Count; i++)
            {
                var candidate = ring[(offset + i) % ring.Count];
                if (!occupied.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
    }

    /// <summary>
    /// Every cell at exactly Chebyshev distance <paramref name="radius"/> from <paramref name="center"/>,
    /// in ring order (top edge left-to-right, right edge top-to-bottom, bottom edge right-to-left, left edge
    /// bottom-to-top) — a plain square-ring expansion. Returned as a list (not lazily yielded) because
    /// <see cref="PlaceNearExhaustive"/> needs to index into it at a randomized rotation.
    /// </summary>
    private static List<GridPoint> RingCells(GridPoint center, int radius)
    {
        var cells = new List<GridPoint>();
        foreach (var cell in RingCellsCore(center, radius))
        {
            cells.Add(cell);
        }

        return cells;
    }

    private static IEnumerable<GridPoint> RingCellsCore(GridPoint center, int radius)
    {
        if (radius <= 0)
        {
            yield return center;
            yield break;
        }

        for (int x = -radius; x <= radius; x++)
        {
            yield return new GridPoint(center.X + x, center.Y - radius);
        }

        for (int y = -radius + 1; y <= radius; y++)
        {
            yield return new GridPoint(center.X + radius, center.Y + y);
        }

        for (int x = radius - 1; x >= -radius; x--)
        {
            yield return new GridPoint(center.X + x, center.Y + radius);
        }

        for (int y = radius - 1; y >= -radius + 1; y--)
        {
            yield return new GridPoint(center.X - radius, center.Y + y);
        }
    }

    // ---- routing --------------------------------------------------------------------------------------

    /// <summary>
    /// Greedy sequential routing (carve one edge's path, permanently reserve its cells, move to the next)
    /// is inherently order-dependent — an early edge can wall off the only viable route for a later one even
    /// though a different processing order would have routed every edge cleanly on the exact same
    /// placement. Restarting the whole pass with a different *global* ordering (an earlier version of this
    /// tried several static orderings from scratch) works but is expensive — each failed attempt burns a
    /// full BFS sweep per edge, and it needed too many candidate orderings to reliably resolve every
    /// deadlock. Real local backtracking does much better: when an edge gets stuck, undo only the most
    /// recently committed edge, swap the two edges' processing order, and resume from there — a targeted,
    /// bounded repair instead of blindly retrying the entire sequence from scratch.
    /// </summary>
    private static bool TryRouteAll(
        Dictionary<string, GridPoint> positions,
        List<(string A, string B)> edges,
        int searchMargin,
        out List<IslandRoute> routes
    )
    {
        var order = edges.OrderBy(e => ManhattanDistance(positions[e.A], positions[e.B])).ToList();
        var blocked = new HashSet<GridPoint>(positions.Values);
        var committed = new List<IslandRoute>();
        int backtracksLeft = Math.Max(20, order.Count * 10);

        int index = 0;
        while (index < order.Count)
        {
            var (a, b) = order[index];
            var from = positions[a];
            var to = positions[b];

            if (TryFindOrthogonalPath(from, to, blocked, searchMargin, out var path))
            {
                committed.Add(new IslandRoute(a, b, path));
                foreach (var cell in path)
                {
                    if (cell != from && cell != to)
                    {
                        blocked.Add(cell);
                    }
                }

                index++;
                continue;
            }

            // Stuck: undo the most recently committed edge and swap it with the one that's stuck, then
            // resume from the freed-up slot — the stuck edge gets first crack at the space the undone one
            // was using, and the undone edge gets retried afterward against whatever the stuck edge left.
            if (committed.Count == 0 || backtracksLeft <= 0)
            {
                routes = [];
                return false;
            }

            backtracksLeft--;
            var undone = committed[^1];
            committed.RemoveAt(committed.Count - 1);
            var undonePositions = (positions[undone.FromBiomeId], positions[undone.ToBiomeId]);
            foreach (var cell in undone.Cells)
            {
                if (cell != undonePositions.Item1 && cell != undonePositions.Item2)
                {
                    blocked.Remove(cell);
                }
            }

            var stuck = order[index];
            order[index] = order[index - 1];
            order[index - 1] = stuck;
            index--;
        }

        routes = committed;
        return true;
    }

    private static int ManhattanDistance(GridPoint a, GridPoint b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static bool TryFindOrthogonalPath(
        GridPoint from,
        GridPoint to,
        HashSet<GridPoint> blocked,
        int searchMargin,
        out List<GridPoint> path
    )
    {
        int minX = Math.Min(from.X, to.X) - searchMargin;
        int maxX = Math.Max(from.X, to.X) + searchMargin;
        int minY = Math.Min(from.Y, to.Y) - searchMargin;
        int maxY = Math.Max(from.Y, to.Y) + searchMargin;

        var cameFrom = new Dictionary<GridPoint, GridPoint> { [from] = from };
        var queue = new Queue<GridPoint>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == to)
            {
                path = ReconstructPath(cameFrom, from, to);
                return true;
            }

            foreach (var dir in Directions)
            {
                var next = new GridPoint(current.X + dir.Dx, current.Y + dir.Dy);
                if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY)
                {
                    continue;
                }

                if (cameFrom.ContainsKey(next))
                {
                    continue;
                }

                // The destination is always reachable even though it's "occupied" by its own biome; every
                // other blocked cell (any biome, any already-carved route) is a hard obstacle.
                if (next != to && blocked.Contains(next))
                {
                    continue;
                }

                cameFrom[next] = current;
                queue.Enqueue(next);
            }
        }

        path = [];
        return false;
    }

    private static List<GridPoint> ReconstructPath(
        Dictionary<GridPoint, GridPoint> cameFrom,
        GridPoint from,
        GridPoint to
    )
    {
        var path = new List<GridPoint> { to };
        var current = to;
        while (current != from)
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    // ---- finalize: normalize to a 0-based, snugly-fit canvas -------------------------------------------

    private static IslandLayout Finalize(
        Dictionary<string, GridPoint> positions,
        List<IslandRoute> routes,
        bool usedFallback
    )
    {
        var allCells = positions.Values.Concat(routes.SelectMany(r => r.Cells)).ToList();
        int minX = allCells.Min(c => c.X);
        int minY = allCells.Min(c => c.Y);
        int maxX = allCells.Max(c => c.X);
        int maxY = allCells.Max(c => c.Y);

        int offsetX = Margin - minX;
        int offsetY = Margin - minY;

        var normalizedPositions = positions.ToDictionary(
            kv => kv.Key,
            kv => new GridPoint(kv.Value.X + offsetX, kv.Value.Y + offsetY)
        );

        var normalizedRoutes = routes
            .Select(r =>
                r with
                {
                    Cells = r
                        .Cells.Select(c => new GridPoint(c.X + offsetX, c.Y + offsetY))
                        .ToList(),
                }
            )
            .ToList();

        int width = maxX - minX + 1 + Margin * 2;
        int height = maxY - minY + 1 + Margin * 2;

        return new IslandLayout(width, height, normalizedPositions, normalizedRoutes, usedFallback);
    }
}
