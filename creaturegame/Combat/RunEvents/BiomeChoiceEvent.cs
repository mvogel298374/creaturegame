using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// The route-choice node (interaction-event): offer the next leg of the run and await the player's pick. At run
/// start the options are any playable biome; afterwards they are the current biome's playable neighbours — so
/// the player charts a route through the authored biome graph (<c>ENCOUNTER_DESIGN.md §1</c>). The offered set
/// (and its order) is sampled on the run RNG, so a seed reproduces the same map. The chosen biome becomes the
/// theme for the next stretch of encounters via <see cref="RunState.CurrentBiome"/>.
/// <para>
/// The <em>opening</em> route choice is biased: it guarantees at least one offered biome is a favourable
/// matchup for the starter (a biome whose theme the player's type(s) hit super-effectively, per
/// <paramref name="typeChart"/>), so a run never opens with only unfavourable lanes. The bias applies only to
/// the first choice and only when such a biome exists in the pool; everything else is the plain seeded sample.
/// </para>
/// </summary>
internal sealed class BiomeChoiceEvent(
    IReadOnlyList<BiomeDefinition> playable,
    int optionCount,
    ITypeChart typeChart
) : IRunEvent
{
    private readonly Dictionary<string, BiomeDefinition> _byId = playable.ToDictionary(b => b.Id);

    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var options = PickOptions(ctx.State.CurrentBiome, ctx.State.Player, ctx.Rng);

        ctx.Emitter?.Emit(
            new BiomeChoiceOffered(
                options.Select(b => new BiomeOption(b.Id, b.Name, b.Types)).ToList()
            )
        );
        string chosenId = await ctx.PlayerInput.ChooseBiomeAsync(new BiomeChoiceContext(options));
        // An unknown id (stale / malformed pick) falls back to the first offered biome — mirrors the move-slot
        // fallback; the route is never left unset.
        var chosen = options.FirstOrDefault(b => b.Id == chosenId) ?? options[0];

        ctx.Emitter?.Emit(new BiomeEntered(chosen.Id, chosen.Name, chosen.Types));
        return new BiomeChoiceOutcome(chosen);
    }

    // The biomes to offer: at run start (no current biome) any playable biome; otherwise the current biome's
    // playable neighbours (charting a route through the authored graph). A dead-end with no playable neighbours
    // falls back to the whole playable set so the run never stalls. Up to optionCount, sampled on the run RNG.
    private IReadOnlyList<BiomeDefinition> PickOptions(
        BiomeDefinition? current,
        Creature player,
        IRandomSource? rng
    )
    {
        var r = rng ?? SystemRandomSource.Instance;
        IReadOnlyList<BiomeDefinition> pool = current is null
            ? playable
            : current.Neighbours.Where(_byId.ContainsKey).Select(id => _byId[id]).ToList();
        if (pool.Count == 0)
            pool = playable;

        // Opening choice only (no biome entered yet): guarantee a favourable lane. Skipped when every biome
        // would be offered anyway (pool ≤ offer) or the starter has no super-effective coverage at all (e.g. a
        // pure Normal type) — both fall through to the plain sample below.
        if (current is null && pool.Count > optionCount)
            return SampleEnsuringFavourableMatchup(pool, optionCount, player, r);

        return Sample(pool, optionCount, r);
    }

    // Like Sample, but reserves one biome the starter is strong into so the opening offer always has a viable
    // lane. Reserve a random favourable biome, fill the rest from the pool, then shuffle so the guaranteed pick
    // isn't always slot 0. Every draw is on the run RNG, so the offer still replays from the seed. Falls back to
    // a plain sample when no biome qualifies (the starter has no super-effective coverage in this pool).
    private IReadOnlyList<BiomeDefinition> SampleEnsuringFavourableMatchup(
        IReadOnlyList<BiomeDefinition> pool,
        int k,
        Creature player,
        IRandomSource rng
    )
    {
        var favourable = pool.Where(b => IsFavourableMatchup(player, b)).ToList();
        if (favourable.Count == 0)
            return Sample(pool, k, rng);

        var reserved = favourable[rng.Next(favourable.Count)];
        var rest = pool.Where(b => b.Id != reserved.Id).ToList();
        var result = new List<BiomeDefinition>(k) { reserved };
        result.AddRange(Sample(rest, k - 1, rng));
        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    // A biome is a favourable opener if any of the starter's types hits any of the biome's theme types
    // super-effectively (>1×) per the active type chart (the generation seam) — its STAB lands hard on that
    // biome's on-theme foes. Reads the chart, never a hardcoded matchup, so it stays gen-correct.
    private bool IsFavourableMatchup(Creature player, BiomeDefinition biome)
    {
        foreach (var atk in player.Types)
        foreach (var def in biome.Types)
            if (typeChart.GetMultiplier(atk, def) > 1.0)
                return true;
        return false;
    }

    // Up to k items in a seed-reproducible random order (partial Fisher–Yates over a copy), so the offered set
    // and its order replay from the run seed.
    private static IReadOnlyList<BiomeDefinition> Sample(
        IReadOnlyList<BiomeDefinition> pool,
        int k,
        IRandomSource rng
    )
    {
        var copy = pool.ToList();
        int take = Math.Min(k, copy.Count);
        for (int i = 0; i < take; i++)
        {
            int j = i + rng.Next(copy.Count - i);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy.GetRange(0, take);
    }
}
