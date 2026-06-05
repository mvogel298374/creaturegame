using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="LearnsetMoveSelector"/> — the gen-agnostic initial-moveset
/// picker. WeightedSmart is driven by a <see cref="SeededRandomSource"/> so its randomness
/// is reproducible.
/// </summary>
public class LearnsetMoveSelectorTests
{
    // --- Fixture helpers -----------------------------------------------------

    private static Attack Move(
        int id,
        string name,
        int power,
        DamageType type,
        DamageCategory cat = DamageCategory.Standard
    ) =>
        new(name, name)
        {
            Id = id,
            BaseDamage = power,
            DamageType = type,
            DamageCategory = cat,
        };

    private static PokemonLearnset Entry(int moveId, int level) =>
        new()
        {
            MoveId = moveId,
            LearnLevel = level,
            Generation = 1,
        };

    private static IReadOnlyDictionary<int, Attack> Dict(params Attack[] moves) =>
        moves.ToDictionary(m => m.Id);

    // --- CanonicalLatest -----------------------------------------------------

    [Fact]
    public void Learnset_CanonicalLatest_GivesMostRecentFourMovesAtLevel()
    {
        var moves = Dict(
            Move(1, "Tackle", 40, DamageType.Normal),
            Move(2, "Growl", 0, DamageType.Normal),
            Move(3, "Vine Whip", 45, DamageType.Grass),
            Move(4, "Poisonpowder", 0, DamageType.Poison),
            Move(5, "Razor Leaf", 55, DamageType.Grass),
            Move(6, "Growth", 0, DamageType.Normal)
        );
        var learnset = new[]
        {
            Entry(1, 1),
            Entry(2, 1),
            Entry(3, 13),
            Entry(4, 20),
            Entry(5, 27),
            Entry(6, 34),
        };

        var result = LearnsetMoveSelector.Select(
            MoveSelectionStrategy.CanonicalLatest,
            learnset,
            moves,
            level: 50,
            DamageType.Grass,
            DamageType.Poison
        );

        // The 4 highest learn levels: 34, 27, 20, 13 — returned in ascending level order.
        Assert.Equal(new[] { 3, 4, 5, 6 }, result.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Learnset_CanonicalLatest_ExcludesMovesAboveLevel()
    {
        var moves = Dict(
            Move(1, "Tackle", 40, DamageType.Normal),
            Move(2, "Vine Whip", 45, DamageType.Grass),
            Move(3, "Solar Beam", 120, DamageType.Grass)
        );
        var learnset = new[] { Entry(1, 1), Entry(2, 13), Entry(3, 48) };

        var result = LearnsetMoveSelector.Select(
            MoveSelectionStrategy.CanonicalLatest,
            learnset,
            moves,
            level: 20,
            DamageType.Grass,
            null
        );

        Assert.DoesNotContain(result, m => m.Id == 3); // Solar Beam not yet learnable
        Assert.Equal(2, result.Count);
    }

    // --- Fewer than four candidates -----------------------------------------

    [Fact]
    public void Learnset_FewerThanFourCandidates_ReturnsAll()
    {
        var moves = Dict(
            Move(1, "Tackle", 40, DamageType.Normal),
            Move(2, "Growl", 0, DamageType.Normal),
            Move(3, "Vine Whip", 45, DamageType.Grass)
        );
        var learnset = new[] { Entry(1, 1), Entry(2, 1), Entry(3, 13) };

        var result = LearnsetMoveSelector.Select(
            MoveSelectionStrategy.WeightedSmart,
            learnset,
            moves,
            level: 50,
            DamageType.Grass,
            null,
            new SeededRandomSource(1)
        );

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Learnset_IgnoresLearnsetMovesMissingFromMoveTable()
    {
        var moves = Dict(Move(1, "Tackle", 40, DamageType.Normal));
        var learnset = new[] { Entry(1, 1), Entry(999, 5) }; // 999 not in the move table

        var result = LearnsetMoveSelector.Select(
            MoveSelectionStrategy.CanonicalLatest,
            learnset,
            moves,
            level: 50,
            DamageType.Normal,
            null
        );

        Assert.Equal(new[] { 1 }, result.Select(m => m.Id).ToArray());
    }

    // --- WeightedSmart -------------------------------------------------------

    [Fact]
    public void Learnset_WeightedSmart_AlwaysIncludesADamagingMove()
    {
        // One attack among many status moves; the result must never be all-status.
        var moves = Dict(
            Move(1, "Tackle", 40, DamageType.Normal),
            Move(2, "Growl", 0, DamageType.Normal),
            Move(3, "Tail Whip", 0, DamageType.Normal),
            Move(4, "Leer", 0, DamageType.Normal),
            Move(5, "Poisonpowder", 0, DamageType.Poison),
            Move(6, "String Shot", 0, DamageType.Bug)
        );
        var learnset = new[]
        {
            Entry(1, 5),
            Entry(2, 1),
            Entry(3, 1),
            Entry(4, 9),
            Entry(5, 13),
            Entry(6, 17),
        };

        for (int seed = 0; seed < 50; seed++)
        {
            var result = LearnsetMoveSelector.Select(
                MoveSelectionStrategy.WeightedSmart,
                learnset,
                moves,
                level: 50,
                DamageType.Normal,
                null,
                new SeededRandomSource(seed)
            );

            Assert.Equal(4, result.Count);
            Assert.Contains(result, m => m.BaseDamage > 0);
        }
    }

    [Fact]
    public void Learnset_WeightedSmart_SameSeed_PicksSameMoveset()
    {
        var moves = Dict(
            Move(1, "Tackle", 40, DamageType.Normal),
            Move(2, "Ember", 40, DamageType.Fire),
            Move(3, "Scratch", 40, DamageType.Normal),
            Move(4, "Growl", 0, DamageType.Normal),
            Move(5, "Leer", 0, DamageType.Normal),
            Move(6, "Flamethrower", 95, DamageType.Fire)
        );
        var learnset = new[]
        {
            Entry(1, 1),
            Entry(2, 9),
            Entry(3, 1),
            Entry(4, 5),
            Entry(5, 13),
            Entry(6, 38),
        };

        var a = LearnsetMoveSelector.Select(
            MoveSelectionStrategy.WeightedSmart,
            learnset,
            moves,
            50,
            DamageType.Fire,
            null,
            new SeededRandomSource(42)
        );
        var b = LearnsetMoveSelector.Select(
            MoveSelectionStrategy.WeightedSmart,
            learnset,
            moves,
            50,
            DamageType.Fire,
            null,
            new SeededRandomSource(42)
        );

        Assert.Equal(a.Select(m => m.Id), b.Select(m => m.Id));
    }

    [Fact]
    public void Learnset_WeightedSmart_FavorsStabAndPower_OverManyTrials()
    {
        // A: the guaranteed strong STAB attack (always force-picked, excluded from the contest).
        // B: a decent STAB attack; C: a weak off-type attack. B should be chosen far more
        // often than C across the random fill slots.
        var moves = Dict(
            Move(1, "Fire Blast", 120, DamageType.Fire), // A — guaranteed
            Move(2, "Ember", 40, DamageType.Fire), // B — decent STAB
            Move(3, "Gust", 10, DamageType.Flying), // C — weak off-type
            Move(4, "Growl", 0, DamageType.Normal),
            Move(5, "Leer", 0, DamageType.Normal),
            Move(6, "Tail Whip", 0, DamageType.Normal)
        );
        var learnset = new[]
        {
            Entry(1, 38),
            Entry(2, 9),
            Entry(3, 5),
            Entry(4, 1),
            Entry(5, 13),
            Entry(6, 17),
        };

        int countB = 0,
            countC = 0;
        for (int seed = 0; seed < 400; seed++)
        {
            var result = LearnsetMoveSelector.Select(
                MoveSelectionStrategy.WeightedSmart,
                learnset,
                moves,
                level: 50,
                DamageType.Fire,
                null,
                new SeededRandomSource(seed)
            );
            if (result.Any(m => m.Id == 2))
                countB++;
            if (result.Any(m => m.Id == 3))
                countC++;
        }

        Assert.True(
            countB > countC,
            $"Expected the decent STAB move (B={countB}) to be picked more often than the weak off-type move (C={countC})."
        );
    }

    // --- SelectWithFallback --------------------------------------------------

    [Fact]
    public void Learnset_SelectWithFallback_NoLearnset_ReturnsFourRandomFromPool()
    {
        var pool = new[]
        {
            Move(1, "A", 40, DamageType.Normal),
            Move(2, "B", 40, DamageType.Fire),
            Move(3, "C", 0, DamageType.Normal),
            Move(4, "D", 40, DamageType.Water),
            Move(5, "E", 40, DamageType.Grass),
        };

        var result = LearnsetMoveSelector.SelectWithFallback(
            MoveSelectionStrategy.CanonicalLatest,
            Array.Empty<PokemonLearnset>(),
            pool,
            level: 50,
            DamageType.Normal,
            null,
            new SeededRandomSource(1)
        );

        Assert.Equal(4, result.Count);
        Assert.All(result, m => Assert.Contains(m, pool));
    }

    [Fact]
    public void Learnset_SelectWithFallback_WithLearnset_UsesLearnsetNotFallback()
    {
        var pool = new[]
        {
            Move(1, "Tackle", 40, DamageType.Normal),
            Move(2, "Vine Whip", 45, DamageType.Grass),
            Move(9, "Hydro Pump", 110, DamageType.Water), // in the pool but NOT learnable
        };
        var learnset = new[] { Entry(1, 1), Entry(2, 13) };

        var result = LearnsetMoveSelector.SelectWithFallback(
            MoveSelectionStrategy.CanonicalLatest,
            learnset,
            pool,
            level: 50,
            DamageType.Grass,
            null,
            new SeededRandomSource(1)
        );

        Assert.Equal(new[] { 1, 2 }, result.Select(m => m.Id).OrderBy(x => x).ToArray());
        Assert.DoesNotContain(result, m => m.Id == 9); // fallback (which could include it) was not used
    }
}
