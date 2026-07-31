using creaturegame.Attacks;
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
/// reminder that a new generation-variable surface exists. <see cref="TypeRoster"/> landed that way in Stage 2a,
/// <see cref="ContentScope"/> in Stage 2b, and <see cref="Region"/> + <see cref="BiomeRoster"/> in Stage 3;
/// still planned: presentation theme (Stage 4).</para>
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

    /// <summary>
    /// Which types <b>exist</b> in this generation — Gen 1's 15, before Steel/Dark (Gen 2) and Fairy (Gen 6).
    /// </summary>
    /// <remarks>
    /// <para><b>Distinct from <see cref="TypeChart"/>, which answers a different question.</b> The chart says how
    /// two types interact; this says which types are in play at all. Nothing derived that from anything before
    /// Stage 2a — <see cref="DamageType"/> lists all 18 and stays deliberately generation-blind (it is a
    /// vocabulary, not a claim about any one generation, exactly like the client's <c>TypeBadge</c> palette and
    /// its boss-name pools), so "Gen 1 has 15 types" was a fact the codebase held only in a comment and a
    /// hardcoded array in <c>BiomeTests</c>.</para>
    ///
    /// <para><b>What reads it.</b> The authored region content: a region has to home every type its generation
    /// has, or species of the unhomed type can never be encountered (<c>ENCOUNTER_DESIGN.md §2.3</c> sized the
    /// Kanto roster to home all 15 for exactly this reason). <see cref="Biomes.UnhomedTypes"/> is that check,
    /// and it takes the roster as an argument rather than assuming 15 — which is the whole point, since a
    /// 17-type generation must re-derive the invariant, not inherit Gen 1's answer.</para>
    ///
    /// <para><b>A set, not a list</b> — this is a membership question and the enum already fixes an order.</para>
    /// </remarks>
    public required IReadOnlySet<DamageType> TypeRoster { get; init; }

    /// <summary>
    /// Which <b>catalog content</b> — species, moves, items — this generation's runs may draw from.
    /// </summary>
    /// <remarks>
    /// <para>The companion to <see cref="TypeRoster"/>, answering the other half of "what exists": the roster
    /// says which <i>types</i> a generation has, this says which <i>rows</i>. Before Stage 2b nothing said
    /// either — the run's content was Gen 1 solely because the databases hold nothing else, an assumption with
    /// no expression in code at all.</para>
    /// <para><b>A documented no-op for Gen 1</b>, deliberately: the <c>GenerationIntroduced</c> columns a real
    /// filter needs are schema work still deferred to <c>TODO.md</c> → <i>Multi-Generation</i>. What this slice
    /// buys now is that every catalog read on the run path <i>asks</i> — see <see cref="IContentScope"/> for the
    /// full rationale and <see cref="Gen1ContentScope"/> for where the eventual filter lands.</para>
    /// </remarks>
    public required IContentScope ContentScope { get; init; }

    /// <summary>
    /// The region this generation's runs are set in. <b>Identity and presentation only — never branch on it</b>,
    /// exactly like <see cref="Generation"/>: the region's <i>content</i> is <see cref="BiomeRoster"/>, and that
    /// is what the run setup consumes.
    /// </summary>
    /// <remarks>
    /// Kept alongside the roster (rather than derived from the biomes' own <see cref="BiomeDefinition.Region"/>
    /// tags) so the generation has one named answer to "where is this set" for logging and the Stage 4 client
    /// echo. The two cannot drift: <c>GenerationProfileTests</c> pins that every rostered biome carries this
    /// region.
    /// </remarks>
    public required Region Region { get; init; }

    /// <summary>
    /// The authored biome roster this generation's runs draw their map from — <see cref="Region"/>'s content.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the roster and not just the enum.</b> Before Stage 3 the run setup called
    /// <c>Biomes.For(Region.Kanto)</c> — a hardcoded region, the biome-layer sibling of the deleted
    /// <c>EncounterFactory.ActiveGeneration</c>. Putting the <i>roster</i> on the profile (rather than only the
    /// enum) is what makes the thread observable: <see cref="Region"/> has a single member today, so only a
    /// substituted biome list can prove the run setup asks the profile — <c>TestAltProfile</c> supplies a tiny
    /// fake roster for exactly that (<c>docs/GENERATION_PROFILE.md</c> §3, §6).</para>
    /// <para><b><see cref="Biomes.For"/> stays the only door to the authored registry</b> — a real profile builds
    /// its roster through it (see <c>Gen1Profile</c>); nothing else may reach into <see cref="Biomes.Kanto"/>
    /// directly on a run path.</para>
    /// <para><b>A thin roster is legal.</b> The run map is a connected subset of the roster's playable biomes,
    /// capped at <c>EncounterFactory.RunBiomeMapSize</c>; a roster smaller than the cap simply yields itself
    /// (<see cref="Biomes.RandomConnectedMap"/> returns everything when the count exceeds the pool) — pinned by
    /// the alternate profile's two-biome roster, per §6's watch note.</para>
    /// </remarks>
    public required IReadOnlyList<BiomeDefinition> BiomeRoster { get; init; }

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
