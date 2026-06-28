using creaturegame.Combat;
using creaturegame.Creatures;

namespace creaturegame.Web.Battle;

/// <summary>The run context an <see cref="IEnemyArchetype"/> scales an enemy against.</summary>
public sealed record EnemyContext(int PlayerLevel, int PlayerBst, int Depth, IRandomSource Rng);

/// <summary>
/// The resolved levers for one enemy: the BST to band around (<see cref="EncounterSelector.PickByBst"/>), the
/// level, the DV quality (<see cref="DvQuality"/>), and the moveset strategy + count. A plain value object the
/// factory turns into a <c>Creature</c> — see <c>ENCOUNTER_DESIGN.md §3</c>.
/// </summary>
public sealed record EnemyTierSpec(
    int TargetBst,
    int Level,
    DvQuality Dvs,
    MoveSelectionStrategy Moves,
    int MoveCount
);

/// <summary>
/// A strength tier that composes an enemy's levers from the run context. Implementations (Weak / Medium /
/// Strong / Boss) each decide what tools they pull; the depth baseline lives in
/// <see cref="EncounterFactory.ScaleTargetBst"/> / <see cref="EncounterFactory.ScaleWildLevel"/> and each tier
/// shifts it. Pure (DB-free) so tiers are unit-testable; <see cref="EncounterFactory"/> builds the creature
/// from the returned <see cref="EnemyTierSpec"/>. Tier <em>selection</em> per encounter is Phase 3.
/// </summary>
public interface IEnemyArchetype
{
    EnemyTierSpec Build(EnemyContext ctx);
}

/// <summary>The Gen 1 strength tiers + the default. Stateless singletons.</summary>
public static class EnemyArchetypes
{
    public static readonly IEnemyArchetype Weak = new WeakArchetype();
    public static readonly IEnemyArchetype Medium = new MediumArchetype();
    public static readonly IEnemyArchetype Strong = new StrongArchetype();
    public static readonly IEnemyArchetype Boss = new BossArchetype();

    /// <summary>The tier used when an encounter doesn't specify one (reproduces the pre-tier behaviour).</summary>
    public static readonly IEnemyArchetype Default = Medium;

    /// <summary>
    /// Maps the core's generation-agnostic <see cref="EncounterTier"/> intent (which node the run director is
    /// running) to a concrete archetype — the web-layer half of the intent/mapping split
    /// (<c>ENCOUNTER_DESIGN.md §3.1</c>). Normal ≈ a plain wild encounter (Medium); Elite/Boss climb. Weak is
    /// not currently selected by a node kind.
    /// </summary>
    public static IEnemyArchetype For(EncounterTier tier) =>
        tier switch
        {
            EncounterTier.Elite => Strong,
            EncounterTier.Boss => Boss,
            _ => Medium,
        };
}

// The depth baseline (EncounterFactory.ScaleTargetBst / ScaleWildLevel) is the Medium tier; the others shift
// it. Offsets are run-layer tuning, not Gen 1 mechanics. Boss is a deliberate placeholder — a stronger Strong;
// its distinctive ceiling design is revisited in a later phase (ENCOUNTER_DESIGN.md §3.6).

internal sealed class WeakArchetype : IEnemyArchetype
{
    public EnemyTierSpec Build(EnemyContext c) =>
        new(
            TargetBst: (int)(EncounterFactory.ScaleTargetBst(c.PlayerBst, c.Depth) * 0.85),
            Level: Math.Max(2, EncounterFactory.ScaleWildLevel(c.PlayerLevel, c.Depth, c.Rng) - 3),
            Dvs: DvQuality.Poor,
            Moves: MoveSelectionStrategy.WeightedSmart,
            MoveCount: 3 // fewer moves than the higher tiers
        );
}

internal sealed class MediumArchetype : IEnemyArchetype
{
    public EnemyTierSpec Build(EnemyContext c) =>
        new(
            TargetBst: EncounterFactory.ScaleTargetBst(c.PlayerBst, c.Depth),
            Level: EncounterFactory.ScaleWildLevel(c.PlayerLevel, c.Depth, c.Rng),
            Dvs: DvQuality.Average,
            Moves: MoveSelectionStrategy.WeightedSmart,
            MoveCount: 4
        );
}

internal sealed class StrongArchetype : IEnemyArchetype
{
    public EnemyTierSpec Build(EnemyContext c) =>
        new(
            TargetBst: (int)(EncounterFactory.ScaleTargetBst(c.PlayerBst, c.Depth) * 1.10),
            Level: EncounterFactory.ScaleWildLevel(c.PlayerLevel, c.Depth, c.Rng) + 3,
            Dvs: DvQuality.High,
            Moves: MoveSelectionStrategy.TmEnhanced, // level-up + TM/HM, strongest legal
            MoveCount: 4
        );
}

internal sealed class BossArchetype : IEnemyArchetype
{
    public EnemyTierSpec Build(EnemyContext c) =>
        new(
            TargetBst: (int)(EncounterFactory.ScaleTargetBst(c.PlayerBst, c.Depth) * 1.20),
            Level: EncounterFactory.ScaleWildLevel(c.PlayerLevel, c.Depth, c.Rng) + 6,
            Dvs: DvQuality.Perfect,
            Moves: MoveSelectionStrategy.Optimal, // best of any move
            MoveCount: 4
        );
}
