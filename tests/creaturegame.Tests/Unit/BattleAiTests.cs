using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The Gen 1 AI brain (<see cref="Gen1TrainerAi"/>), its input adapter (<see cref="AiBattleInput"/>), and the
/// deterministic damage estimate the brain scores from (<see cref="DamageCalculator.EstimateDamage"/>).
/// </summary>
public class BattleAiTests
{
    // A brain scores moves from a TurnContext; these tests don't need real battle state on it.
    private static TurnContext Context() =>
        new()
        {
            Attacker = TestCreatures.Make("A"),
            Defender = TestCreatures.Make("D"),
            TypeChart = Gen1TypeChart.Instance,
            Rules = Gen1BattleRules.Instance,
            TurnNumber = 1,
        };

    private static PokemonAttack Move(string name) => new(new Attack(name, name));

    /// <summary>Scores a move by a fixed table keyed on its name — lets a test fix the scoring landscape.</summary>
    private sealed class FixedEvaluator(Dictionary<string, double> scores) : IMoveEvaluator
    {
        public double Score(PokemonAttack move, TurnContext context) =>
            scores.TryGetValue(move.Base.Name ?? "", out var s) ? s : 0;
    }

    // ── Gen1TrainerAi selection ──────────────────────────────────────────────

    [Fact]
    public void Ai_HighIntelligenceAlmostAlwaysPicksTheBestMove()
    {
        var best = Move("Best");
        var candidates = new[] { Move("Weak1"), best, Move("Weak2") };
        var evaluator = new FixedEvaluator(
            new()
            {
                ["Best"] = 10.0,
                ["Weak1"] = 0.0,
                ["Weak2"] = 0.0,
            }
        );
        var ai = new Gen1TrainerAi(evaluator, new SeededRandomSource(1), intelligence: 1.0);

        // With a large score gap and a near-greedy temperature, the softmax weight on the others is
        // vanishing — every draw should land on the best move.
        for (int i = 0; i < 100; i++)
            Assert.Same(best, ai.ChooseMove(candidates, Context()));
    }

    [Fact]
    public void Ai_LowIntelligenceSometimesPicksAWorseMove()
    {
        var candidates = new[] { Move("Good"), Move("Bad") };
        var evaluator = new FixedEvaluator(new() { ["Good"] = 1.0, ["Bad"] = 0.0 });
        var ai = new Gen1TrainerAi(evaluator, new SeededRandomSource(7), intelligence: 0.0);

        var chosen = new HashSet<string>();
        for (int i = 0; i < 200; i++)
            chosen.Add(ai.ChooseMove(candidates, Context()).Base.Name!);

        // The whole point of the fallible brain: a near-random tier visits the worse move too.
        Assert.Contains("Good", chosen);
        Assert.Contains("Bad", chosen);
    }

    [Fact]
    public void Ai_SingleCandidateIsReturnedDirectly()
    {
        var only = Move("Only");
        var ai = new Gen1TrainerAi();
        Assert.Same(only, ai.ChooseMove(new[] { only }, Context()));
    }

    [Fact]
    public void Ai_NoCandidatesThrows()
    {
        var ai = new Gen1TrainerAi();
        Assert.Throws<InvalidOperationException>(() =>
            ai.ChooseMove(Array.Empty<PokemonAttack>(), Context())
        );
    }

    // ── AiBattleInput candidate filtering ────────────────────────────────────

    /// <summary>Records the candidate list it is handed, then returns the first.</summary>
    private sealed class RecordingBrain : IBattleAi
    {
        public IReadOnlyList<PokemonAttack>? Seen { get; private set; }

        public PokemonAttack ChooseMove(
            IReadOnlyList<PokemonAttack> candidates,
            TurnContext context
        )
        {
            Seen = candidates;
            return candidates[0];
        }
    }

    private static Creature WithMoves(params string[] names)
    {
        var c = TestCreatures.Make("Mon");
        int id = 1;
        foreach (var n in names)
            c.AddAttack(new Attack(n, n) { Id = id++, PowerPointsMax = 10 });
        return c;
    }

    [Fact]
    public async Task Input_ExcludesOutOfPpAndDisabledMoves()
    {
        var attacker = WithMoves("Usable", "Empty", "Locked");
        attacker.MoveSet[1].PowerPointsCurrent = 0; // out of PP
        var disabled = attacker.MoveSet[2];

        var brain = new RecordingBrain();
        var input = new AiBattleInput(brain);
        var context = new TurnContext
        {
            Attacker = attacker,
            Defender = TestCreatures.Make("D"),
            TypeChart = Gen1TypeChart.Instance,
            Rules = Gen1BattleRules.Instance,
            DisabledMove = disabled,
        };

        await input.ChooseMoveAsync(context);

        Assert.NotNull(brain.Seen);
        Assert.Single(brain.Seen!);
        Assert.Equal("Usable", brain.Seen![0].Base.Name);
    }

    [Fact]
    public async Task Input_ThrowsWhenNoMoveIsSelectable()
    {
        var attacker = WithMoves("Empty");
        attacker.MoveSet[0].PowerPointsCurrent = 0;
        var input = new AiBattleInput(new RecordingBrain());
        var context = new TurnContext
        {
            Attacker = attacker,
            Defender = TestCreatures.Make("D"),
            TypeChart = Gen1TypeChart.Instance,
            Rules = Gen1BattleRules.Instance,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => input.ChooseMoveAsync(context));
    }

    // ── EstimateDamage (the brain's deterministic damage signal) ──────────────

    [Fact]
    public void Estimate_MatchesLiveCalcWithNoCritAndNoVariance()
    {
        var attacker = TestCreatures.Make("A");
        var defender = TestCreatures.Make("D");
        var move = new Attack("Hit", "Hit") { BaseDamage = 80, DamageType = DamageType.Normal };
        var rules = new ScriptableRules().NoCrit().NoVariance();

        int live = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            Gen1TypeChart.Instance,
            rules,
            out bool crit,
            new SeededRandomSource(3)
        );
        int estimate = DamageCalculator.EstimateDamage(
            attacker,
            defender,
            move,
            Gen1TypeChart.Instance,
            rules
        );

        Assert.False(crit);
        Assert.Equal(live, estimate);
    }

    [Fact]
    public void Estimate_IsZeroForANonDamagingMove()
    {
        var estimate = DamageCalculator.EstimateDamage(
            TestCreatures.Make("A"),
            TestCreatures.Make("D"),
            new Attack("Growl", "Growl") { BaseDamage = 0 },
            Gen1TypeChart.Instance
        );
        Assert.Equal(0, estimate);
    }
}
