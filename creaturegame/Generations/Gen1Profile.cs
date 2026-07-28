using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Evolution;

namespace creaturegame.Generations;

/// <summary>
/// Red/Blue/Yellow — the reference profile, and currently the only real one.
/// </summary>
/// <remarks>
/// <para>Every slice here is the value the engine <i>already</i> defaults to, so selecting this profile is a
/// true no-op against the pre-profile behaviour. That equivalence is pinned by
/// <c>GenerationProfileTests</c> and is the acceptance condition for Stage 1 — the same shape as
/// <c>DifficultyTests</c>' proof that the Normal preset reproduced the old hardcoded <c>RunTuning</c>.</para>
/// <para><b>This file is the template for a new generation.</b> Adding one means writing its sibling and adding
/// a line to <see cref="GenerationProfiles"/> — not editing this file and not editing the engine.</para>
/// </remarks>
public static class Gen1Profile
{
    /// <summary>The Gen 1 profile. Stateless and shared, like the seam singletons it composes.</summary>
    public static readonly GenerationProfile Instance = new()
    {
        Generation = Generation.One,
        TypeChart = Gen1TypeChart.Instance,
        BattleRules = Gen1BattleRules.Instance,
        // Seeded per run — matches EncounterFactory.BuildCreature's existing `new Gen1StatCalculator(rng)`, so a
        // fixed seed keeps reproducing the same DVs.
        BuildStatCalculator = rng => new Gen1StatCalculator(rng),
        EvolutionRules = Gen1EvolutionRules.Instance,
        // Matches the web layer's existing construction exactly: the brain takes the run's seeded RNG and
        // otherwise keeps its defaults (CompositeEvaluator.CreateDefault, intelligence 0.7).
        BuildAi = rng => new Gen1TrainerAi(rng: rng),
    };
}
