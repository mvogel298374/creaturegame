using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.TestSupport;

/// <summary>
/// Test factory over the live SQLite DBs — mirrors the production composition (parameterless context ctors
/// resolve them).
/// </summary>
/// <remarks>
/// The shared home for what was previously a file-scoped copy per integration suite (five by 2026-07-31 —
/// `pr-review`'s count when it flagged the duplication). New suites use this; the older file-scoped copies are
/// pre-existing and may be backfilled opportunistically (`file` classes shadow this one within their own file,
/// so coexistence is safe).
/// </remarks>
internal sealed class LiveDbContextFactory<TContext>(Func<TContext> create)
    : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
}
