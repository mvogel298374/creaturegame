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
    /// <summary>
    /// The 15 types Red/Blue/Yellow shipped with. Steel and Dark arrived in Gen 2, Fairy in Gen 6 — all three are
    /// members of <see cref="DamageType"/> (which spans every generation on purpose) but none exists here.
    /// </summary>
    /// <remarks>
    /// Listed in <see cref="DamageType"/> declaration order rather than the in-game Pokédex order, so a reader
    /// diffing this against the enum can see at a glance that exactly three members are missing.
    /// <para>This is now the <b>single source of truth</b> for the roster. It previously existed only as a
    /// hardcoded array in <c>BiomeTests</c> — a second source of truth of exactly the kind Stage 1b deleted when
    /// it removed <c>EncounterFactory.ActiveGeneration</c>.</para>
    ///
    /// <para><b>A get-only property, not a <c>static readonly</c> field.</b> Static <i>field</i> initializers run
    /// in textual order, so a roster field declared below the <see cref="Instance"/> that reads it would be
    /// <c>null</c> at the moment of use. A property getter runs when <see cref="Instance"/>'s initializer actually
    /// reads it, so position stops mattering — which matters because this file is documented above as <b>the
    /// template for a new generation</b>, and an author who copies it and appends a Stage 3/4 slice <i>below</i>
    /// <see cref="Instance"/> should not have to know this rule.
    /// <para><b>Verified, not assumed:</b> writing the field form below <see cref="Instance"/> does <i>not</i>
    /// fail silently here — the compiler raises <c>CS8601 Possible null reference assignment</c>, which the repo's
    /// <c>TreatWarningsAsErrors</c> (<c>Directory.Build.props</c>) turns into a build error. So this shape is
    /// belt-and-braces over a guarantee the build already gives, chosen because it removes the trap rather than
    /// reporting it. Note <c>{ get; } = …</c> would <b>not</b> do that — an auto-property initializer compiles to
    /// the same order-dependent static field. The expression body is the point; one read site, so re-evaluating
    /// it is a non-issue.</para></para>
    /// </remarks>
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
        // Seeded per run — matches EncounterFactory.BuildCreature's existing `new Gen1StatCalculator(rng)`, so a
        // fixed seed keeps reproducing the same DVs.
        BuildStatCalculator = rng => new Gen1StatCalculator(rng),
        EvolutionRules = Gen1EvolutionRules.Instance,
        // Matches the web layer's existing construction exactly: the brain takes the run's seeded RNG and
        // otherwise keeps its defaults (CompositeEvaluator.CreateDefault, intelligence 0.7).
        BuildAi = rng => new Gen1TrainerAi(rng: rng),
    };
}
