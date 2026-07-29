using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Evolution;

namespace creaturegame.Generations;

/// <summary>
/// Everything that makes a run "a generation": the battle/progression seams plus (in later stages) the content
/// scope, region and presentation theme. One coherent bundle, selected once at run start.
/// </summary>
/// <remarks>
/// <para><b>Why this type exists.</b> <c>GENERATION_SEAMS.md §7</c> anticipated it: <i>"a single composition
/// point (where Battle and Creature are built) will choose the implementation set — still no branching inside
/// the engine."</i> This is that composition point, hoisted to a value so it can be threaded like any other
/// run parameter.</para>
///
/// <para><b>⚠️ The hazard this type exists to close.</b> Every seam consumer in the engine defaults with
/// <c>?? Gen1*.Instance</c> (nine such fallbacks, plus <see cref="Creature.StatCalculator"/>'s property
/// default). That is deliberate — it is the "zero ceremony" ergonomics of
/// <c>GENERATION_SEAMS.md §4.3</c>, and it is what keeps every direct <see cref="Battle"/> caller and test
/// working. But it means a composition path that <i>forgets</i> to thread a seam does not crash and does not
/// fail a test: <b>it silently runs Gen 1</b>. So the web composition root must pass every slice on this
/// record <i>explicitly</i>, and only a second profile can prove that it did — see
/// <c>docs/GENERATION_PROFILE.md</c> §3 and §4.2.</para>
///
/// <para><b>Adding a slice (Stages 2–4).</b> New slices are added as <c>required</c> properties here, which
/// deliberately breaks every profile that does not supply them — a compile error is the cheapest possible
/// reminder that a new generation-variable surface exists. Planned: the type roster (Stage 2), region and
/// starters (Stage 3), presentation theme (Stage 4).</para>
///
/// <para><b>Instances, not factories — mostly.</b> The four seams are stateless singletons by design
/// (<c>GENERATION_SEAMS.md §4.3</c>), so they are held directly. <see cref="BuildAi"/> is the exception: the
/// enemy brain closes over the <i>run's</i> seeded RNG, so it must be built per run, which is exactly how the
/// web layer constructs it today.</para>
/// </remarks>
public sealed record GenerationProfile
{
    /// <summary>Which generation this profile implements. Identity/logging only — never branch on it.</summary>
    public required Generation Generation { get; init; }

    /// <summary>The type-effectiveness matrix, including that generation's quirks and bugs.</summary>
    public required ITypeChart TypeChart { get; init; }

    /// <summary>All generation-variable battle math: crit formula, damage variance, stat-stage tables,
    /// accuracy scale, freeze/thaw, status-damage rates, stat selection, the XP formula.</summary>
    public required IBattleRules BattleRules { get; init; }

    /// <summary>
    /// Builds the stat calculator for a run: HP/other-stat formulas, DV/IV randomisation, Stat-Exp/EV scaling
    /// and award.
    /// </summary>
    /// <remarks>
    /// A factory, not a singleton, because <b>DV randomisation must draw from the run's seeded RNG</b> — that is
    /// what makes a seeded run reproduce the same DVs. <c>EncounterFactory.BuildCreature</c> calls this once per
    /// creature for exactly that reason; the singleton on
    /// <see cref="Creature.StatCalculator"/> is only the unseeded fallback for direct callers and tests.
    /// <para><b>Consumed at the web composition point since Stage 1b</b> — <c>EncounterFactory.BuildCreature</c>
    /// takes the run's profile and calls this, replacing the <c>new Gen1StatCalculator(rng)</c> it used to
    /// hardcode. Pinned by <c>EncounterFactoryGenerationProfileTests</c>, which builds a creature under a profile
    /// whose calculator stamps a DV outside Gen 1's range.</para>
    /// </remarks>
    public required Func<IRandomSource, IStatCalculator> BuildStatCalculator { get; init; }

    /// <summary>Which evolution trigger fires, and how this generation interprets it.</summary>
    public required IEvolutionRules EvolutionRules { get; init; }

    /// <summary>
    /// Builds the enemy brain for a run, closing over that run's seeded RNG so its choices replay under the seed.
    /// </summary>
    /// <remarks>
    /// A factory rather than an instance because the brain is per-run (it holds the RNG), unlike the four
    /// stateless seams above.
    /// <para><b>Why the AI is on the profile at all</b> (decided 2026-07-29, the open question raised by
    /// <c>GENERATION_PROFILE.md</c> §4.3): the concrete brain is <i>named</i> <c>Gen1TrainerAi</c>, but its own
    /// documentation states it is "a thin, generation-blind selection policy" whose Gen 1 leanings live in the
    /// <i>evaluators</i> it scores with. So the generation-variable surface is the evaluator personality and the
    /// intelligence knob, not the selection policy. Exposing the whole construction as one factory puts that
    /// surface behind the profile without pretending the policy class itself is per-generation.</para>
    /// </remarks>
    public required Func<IRandomSource, IBattleAi> BuildAi { get; init; }
}
