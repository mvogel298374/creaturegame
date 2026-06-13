using creaturegame.Attacks;
using creaturegame.Combat;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Guards the two effect-strategy registries — <see cref="MoveEffects"/> (post-damage effects) and
/// <see cref="LockInMechanics"/> (multi-turn lock-ins). Both resolve a <see cref="MoveEffect"/> to a single
/// handler via <c>For(effect)</c>, derived from an <c>All</c> list. These assert the derivation is sound
/// (every handler round-trips, keys are unique) and that effects handled elsewhere in the pipeline aren't
/// claimed by a registry (so <c>For</c> returns null and the caller's inline path runs).
/// </summary>
public class EffectRegistryTests
{
    // ── MoveEffects (post-damage effect registry) ────────────────────────────

    [Fact]
    public void MoveEffects_EveryEntryRoundTripsThroughFor()
    {
        foreach (var effect in MoveEffects.All)
            Assert.Same(effect, MoveEffects.For(effect.Effect));
    }

    [Fact]
    public void MoveEffects_KeysAreUnique()
    {
        var keys = MoveEffects.All.Select(e => e.Effect).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Theory]
    [InlineData(MoveEffect.Haze)]
    [InlineData(MoveEffect.Counter)]
    [InlineData(MoveEffect.Substitute)]
    [InlineData(MoveEffect.Transform)]
    public void MoveEffects_For_ReturnsHandlerWhoseEffectMatches(MoveEffect effect)
    {
        var handler = MoveEffects.For(effect);
        Assert.NotNull(handler);
        Assert.Equal(effect, handler!.Effect);
    }

    [Theory]
    [InlineData(MoveEffect.None)] // no post-damage effect
    [InlineData(MoveEffect.Recharge)] // handled inline in AttackAction.ExecuteAsync
    [InlineData(MoveEffect.MultiHit)] // resolved in the Standard damage loop
    [InlineData(MoveEffect.Metronome)] // calls another move inline
    [InlineData(MoveEffect.MirrorMove)] // calls another move inline
    [InlineData(MoveEffect.DreamEater)] // a Drain move with a sleep precondition
    [InlineData(MoveEffect.Crash)] // miss/immunity crash, handled inline
    [InlineData(MoveEffect.TwoTurn)] // a lock-in mechanic, not a post-damage effect
    public void MoveEffects_For_ReturnsNull_ForEffectsHandledElsewhere(MoveEffect effect)
    {
        Assert.Null(MoveEffects.For(effect));
    }

    // ── LockInMechanics (multi-turn lock-in registry, same shape) ────────────

    [Fact]
    public void LockInMechanics_EveryEntryRoundTripsThroughFor()
    {
        foreach (var mechanic in LockInMechanics.All)
            Assert.Same(mechanic, LockInMechanics.For(mechanic.Effect));
    }

    [Fact]
    public void LockInMechanics_KeysAreUnique()
    {
        var keys = LockInMechanics.All.Select(m => m.Effect).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Theory]
    [InlineData(MoveEffect.None)]
    [InlineData(MoveEffect.Haze)] // a post-damage effect, not a lock-in
    [InlineData(MoveEffect.Counter)]
    public void LockInMechanics_For_ReturnsNull_ForNonLockInEffects(MoveEffect effect)
    {
        Assert.Null(LockInMechanics.For(effect));
    }

    [Fact]
    public void TheTwoRegistries_DoNotClaimTheSameEffect()
    {
        // A move effect routes to at most one registry — overlap would mean an effect is both a post-damage
        // effect and a lock-in mechanic, which the pipeline never expects. (Binding lives only in
        // LockInMechanics for its forced-move re-use; its on-hit trap-start is the BindingEffect... so it is
        // the one deliberate exception — assert exactly that, so a NEW accidental overlap still fails.)
        var moveEffectKeys = MoveEffects.All.Select(e => e.Effect).ToHashSet();
        var lockInKeys = LockInMechanics.All.Select(m => m.Effect).ToHashSet();
        var overlap = moveEffectKeys.Intersect(lockInKeys).ToList();
        Assert.Equal(new[] { MoveEffect.Binding }, overlap);
    }
}
