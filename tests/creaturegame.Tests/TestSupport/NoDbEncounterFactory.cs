using creaturegame.DB;
using creaturegame.Web.Battle;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.TestSupport;

/// <summary>An <see cref="EncounterFactory"/> whose DB factories throw if touched. Safe wherever a test only
/// <i>closes over</i> the factory without actually invoking a DB-touching path (e.g. <c>BuildRunOptions</c>'s
/// draft/boss-catch supplier lambdas, or <c>AttachConnection</c>'s first-attach path when the test returns
/// before its background run task reaches the database) — keeps the test DB-free and honest about why.</summary>
public static class NoDbEncounterFactory
{
    public static EncounterFactory Create() =>
        new(
            new UnusedDbContextFactory<PokemonDbContext>(),
            new UnusedDbContextFactory<MovesDbContext>(),
            new UnusedDbContextFactory<ItemsDbContext>()
        );

    private sealed class UnusedDbContextFactory<TContext> : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext() =>
            throw new InvalidOperationException(
                $"{typeof(TContext).Name} was created — this test is not supposed to touch the database."
            );
    }
}
