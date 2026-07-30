using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Generations;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// A deliberately fake second <see cref="GenerationProfile"/> — the falsification harness from
/// <c>docs/GENERATION_PROFILE.md</c> §3.
/// </summary>
/// <remarks>
/// <para><b>⚠️ THIS IS NOT GEN 2, AND IT MAKES NO FIDELITY CLAIM.</b> Its values are chosen to be <i>different
/// from Gen 1</i>, not to be correct for any real generation. Do not cite it as a reference for anything, do not
/// grow it into a real profile, and never register it in <see cref="GenerationProfiles"/>. When a real second
/// generation is built, the correct move is to <b>delete this file</b>, not to promote it.</para>
///
/// <para><b>Why it has to exist.</b> Every seam consumer in the engine defaults with <c>?? Gen1*.Instance</c>
/// (nine such fallbacks, plus <see cref="Creature.StatCalculator"/>'s property default). With only one profile
/// in the codebase, a composition path that <i>forgets</i> to thread the profile is indistinguishable from one
/// that threads it correctly — both produce Gen 1, and every test stays green. A second profile is the only
/// context in which "we got Gen 1" is a <i>wrong</i> answer, and therefore the only thing that can detect a
/// silent fallback.</para>
///
/// <para>That is <c>GENERATION_SEAMS.md §5.0.1</c>'s lesson at architecture scale: the two leaks recorded there
/// passed review <i>and</i> tests, because no test exercised the generation-variable bit.</para>
///
/// <para><b>Growing it:</b> each stage of the feature adds its slice here — Stage 2a's 17-type roster is in
/// (<see cref="AltTypes"/>); still to come are Stage 3's small fake region and Stage 4's distinct theme id. Keep
/// every value obviously synthetic.</para>
/// </remarks>
internal static class TestAltProfile
{
    /// <summary>The DV this profile's stat calculator stamps on every stat — the marker a test reads to prove a
    /// creature was built with <b>this</b> profile's stat seam and not a leaked Gen 1 one. Deliberately outside
    /// Gen 1's 0–15 DV range so the assertion cannot pass by coincidence. See <see cref="AltStatCalculator"/>.</summary>
    public const int SentinelDv = 99;

    /// <summary>
    /// A 17-type roster: Gen 1's 15 plus Dark and Steel. Stage 2a's falsification leg.
    /// </summary>
    /// <remarks>
    /// <para><b>The two extras are what make it a probe.</b> No biome in the authored Kanto roster lists Dark or
    /// Steel, so <see cref="Biomes.UnhomedTypes"/> must report exactly those two under this profile and nothing
    /// under Gen 1. A check that re-hardcoded "the 15" instead of reading the roster would return empty in both
    /// cases and fail here — which is the only way to observe that the roster is genuinely consulted.</para>
    /// <para>Deliberately built <i>by adding to</i> Gen 1's set rather than re-listing 17 types by hand: the
    /// probe is "this generation has types Gen 1 lacks", and spelling the whole list out again would let the two
    /// rosters drift into an accidental difference that has nothing to do with what is being tested.</para>
    /// <para>As everywhere in this file, the values make no fidelity claim — Gen 2 added Dark and Steel, but this
    /// is not Gen 2 and nothing else here resembles it.</para>
    /// <para>An expression-bodied property rather than a <c>static readonly</c> field for the same reason
    /// <c>Gen1Profile.Gen1Types</c> is one: a field would be order-dependent against the <see cref="Instance"/>
    /// that reads it. This file mirrors that file's shape on purpose.</para>
    /// </remarks>
    private static IReadOnlySet<DamageType> AltTypes =>
        Gen1Profile.Instance.TypeRoster.Concat([DamageType.Dark, DamageType.Steel]).ToHashSet();

    public static readonly GenerationProfile Instance = new()
    {
        // Reuses Generation.One as a label because the enum has no second member yet, and adding a fake one to
        // the production enum to serve a test would be worse. Nothing reads this field to make a decision.
        Generation = Generation.One,
        TypeChart = new AltTypeChart(),
        TypeRoster = AltTypes,
        BattleRules = new AltBattleRules(),
        BuildStatCalculator = rng => new AltStatCalculator(),
        EvolutionRules = new AltEvolutionRules(),
        BuildAi = rng => new Gen1TrainerAi(rng: rng),
    };

    /// <summary>Flat 1.0 for everything — deliberately unlike Gen 1, whose chart is full of quirks. The
    /// Ghost→Psychic immunity becoming neutral is the specific difference the tests observe.</summary>
    private sealed class AltTypeChart : ITypeChart
    {
        public double GetMultiplier(DamageType attackType, DamageType defenderType) => 1.0;
    }

    /// <summary>Delegates every rule to Gen 1 except one — reusing <see cref="DelegatingBattleRules"/>, the base
    /// that exists so a new <c>IBattleRules</c> member is a one-line change rather than an edit to every shim.
    /// <para>The varied member is the accuracy roll bound: Gen 1's internal 0–255 scale (with its 1/256 miss bug)
    /// versus a 0–100 one. That is the real Gen 2+ change, which makes it a realistic probe — but it is chosen
    /// because it is <i>observably different</i>, not as a claim about any generation.</para></summary>
    private sealed class AltBattleRules : DelegatingBattleRules
    {
        public override int AccuracyRollBound => 100;
    }

    /// <summary>
    /// Stat math that is <b>observably not Gen 1</b>: <see cref="RandomiseDvs"/> ignores its quality band and
    /// stamps a fixed sentinel DV on every stat, and the two stat formulas return a constant.
    /// </summary>
    /// <remarks>
    /// Stage 1b's falsification leg. Until that stage, this slot held <c>new Gen1StatCalculator(rng)</c> — which
    /// made it useless as a probe: <c>EncounterFactory.BuildCreature</c> hardcoded its own
    /// <c>new Gen1StatCalculator(rng)</c>, so threading the profile and forgetting to thread it produced
    /// identical creatures. Sentinel DVs are what make the difference visible: Gen 1's Average band draws each of
    /// the four independent DVs from 0–15 and derives HP from their low bits, so it cannot land on
    /// <see cref="SentinelDv"/> across the board by chance in a seeded test. Deliberately <b>ignores the rng</b>
    /// — determinism is the point, and it also proves the factory's argument isn't what makes it work.
    /// </remarks>
    private sealed class AltStatCalculator : IStatCalculator
    {
        public int CalculateHP(int baseStat, int dv, int statExp, int level) => 111;

        public int CalculateOtherStat(int baseStat, int dv, int statExp, int level) => 22;

        public void RandomiseDvs(Creature creature, DvQuality quality)
        {
            creature.DvHP = SentinelDv;
            creature.DvAttack = SentinelDv;
            creature.DvDefense = SentinelDv;
            creature.DvSpecial = SentinelDv;
            creature.DvSpeed = SentinelDv;
        }

        public void AwardStatExp(Creature victor, Creature defeated) { }
    }

    /// <summary>Never evolves anything — the simplest possible difference from Gen 1's real edge logic.</summary>
    private sealed class AltEvolutionRules : IEvolutionRules
    {
        public EvolutionResult? CheckEvolution(
            Creature creature,
            EvolutionContext context,
            IReadOnlyList<PokemonEvolution> edges
        ) => null;
    }
}
