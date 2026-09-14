using creaturegame.DB;
using creaturegame.Web.Battle;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.TestSupport;

/// <summary>An <see cref="EncounterFactory"/> whose every DB touch parks on a gate — used to stall a spawned
/// run task deterministically at its first database read (the enemy build), so a battle stays in
/// <c>GameSessionManager</c>'s active set for a reconnect leg a test wants to exercise, and the run emits
/// nothing that could interleave with the test's assertions. Once the gate is set the factory throws, letting
/// the parked task die through the session's normal failure path — the deterministic alternative to
/// <see cref="NoDbEncounterFactory"/>, whose spawned task instead faults (and removes itself from the active
/// set) on its own schedule, racing any assertion that needs the battle to still be there.</summary>
public static class BlockedEncounterFactory
{
    public static EncounterFactory Create(ManualResetEventSlim gate) =>
        new(
            new BlockingDbContextFactory<PokemonDbContext>(gate),
            new BlockingDbContextFactory<MovesDbContext>(gate),
            new BlockingDbContextFactory<ItemsDbContext>(gate)
        );

    private sealed class BlockingDbContextFactory<TContext>(ManualResetEventSlim gate)
        : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext()
        {
            gate.Wait();
            throw new InvalidOperationException(
                "gate released — the parked run task ends here (post-assertion cleanup)."
            );
        }
    }
}
