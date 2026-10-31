using creaturegame.Attacks;
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
    /// <summary>The 15 types Red/Blue/Yellow shipped with; <see cref="DamageType"/> has three more that Gen 1 lacks.</summary>
    // A property, not a field, for initializer order (GENERATION_PROFILE.md §5(a)).
    private static IReadOnlySet<DamageType> Gen1Types =>
        new HashSet<DamageType>
        {
            DamageType.Normal,
            DamageType.Fighting,
            DamageType.Psychic,
            DamageType.Electric,
            DamageType.Water,
            DamageType.Flying,
            DamageType.Poison,
            DamageType.Ground,
            DamageType.Rock,
            DamageType.Bug,
            DamageType.Ghost,
            DamageType.Fire,
            DamageType.Grass,
            DamageType.Ice,
            DamageType.Dragon,
        };

    /// <summary>Gen 1's five HMs, by their real move id — Cut/Fly/Surf/Strength/Flash. Hand-verified domain
    /// knowledge, the same pattern <c>DATA_IMPORT.md</c> uses everywhere PokeAPI can't express a Gen 1 fact
    /// (<c>ItemMapper.Gen1BattleItemNames</c>, <c>MoveImport.ApplyGen1Corrections</c>) — PokeAPI's own "machine"
    /// learn method doesn't separate TM from HM, so this list is the only place that distinction is recorded.</summary>
    private static IReadOnlySet<int> Gen1HmMoveIds => new HashSet<int> { 15, 19, 57, 70, 148 };

    /// <summary>The Gen 1 profile. Stateless and shared, like the seam singletons it composes.</summary>
    public static readonly GenerationProfile Instance = new()
    {
        Generation = Generation.One,
        TypeChart = Gen1TypeChart.Instance,
        TypeRoster = Gen1Types,
        // Everything in the databases IS Gen 1's content — enforced by the importer's rosters, not assumed (see
        // Gen1ContentScope, which records the one item that used to break this) — so this identity stub is
        // correct, and it is where the real GenerationIntroduced filter goes the day that stops being true.
        ContentScope = Gen1ContentScope.Instance,
        Region = Region.Kanto,
        // Through Biomes.For — the one door to the authored registry (GenerationProfile.BiomeRoster's rule).
        // A new generation authors its own roster in Biomes and reads it out the same way.
        BiomeRoster = Biomes.For(Region.Kanto),
        BattleRules = Gen1BattleRules.Instance,
        BuildBattleRules = rng => new Gen1BattleRules(rng),
        // Seeded per run — matches EncounterFactory.BuildCreature's existing `new Gen1StatCalculator(rng)`, so a
        // fixed seed keeps reproducing the same DVs.
        BuildStatCalculator = rng => new Gen1StatCalculator(rng),
        EvolutionRules = Gen1EvolutionRules.Instance,
        // Matches the web layer's existing construction exactly: the brain takes the run's seeded RNG and
        // otherwise keeps its defaults (CompositeEvaluator.CreateDefault, intelligence 0.7).
        BuildAi = rng => new Gen1TrainerAi(rng: rng),
        HmMoveIds = Gen1HmMoveIds,
    };
}
