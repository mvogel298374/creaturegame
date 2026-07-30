using creaturegame.Attacks;
using creaturegame.DB;
using creaturegame.Items;

namespace creaturegame.Generations;

/// <summary>
/// Red/Blue/Yellow's content scope — <b>the documented no-op stub</b> of <c>GENERATION_SEAMS.md §5.0</c>: every
/// accessor returns its query untouched.
/// </summary>
/// <remarks>
/// <para><b>Why returning everything is correct today, and exactly when it stops being.</b> The three catalogs
/// hold Gen 1 rows and nothing else — <c>PokeApiConnector</c> imports Gen 1 species, moves 1–165 and the
/// hand-curated Gen 1 item roster — so "all of it" and "Gen 1's share of it" are the same set. That equivalence
/// is a property of the <i>data</i>, not of this class. The moment <c>TODO.md</c> → <i>Multi-Generation</i>
/// imports a second generation's rows, this stub becomes <b>wrong</b>: it would hand a Gen 1 run species and
/// moves that generation never had. <b>This class is where that fix goes</b>, and it is the only place — every
/// consumer already asks the scope rather than the table.</para>
///
/// <para><b>That premise was not free, and it is enforced rather than assumed.</b> When this seam was written the
/// item catalog did <i>not</i> hold Gen 1 alone: Max Revive, a Gen 2 item, was imported as forward scaffolding
/// and then name-matched out of the reward and shop channels by <c>RewardCalculator</c> — so the identity below
/// was "correct" only by a second, unrelated mechanism, which is the kind of accident this whole feature exists
/// to delete. <c>requirements-review</c> caught it; the item was removed from the import roster and from
/// <c>items.db</c> (2026-07-30, user's call) so that the premise is literally true, and
/// <c>ItemImportTests.Gen1BattleItemNames_ExcludesMaxRevive_TheItemsCatalogIsOneGenerationsContent</c> pins it.
/// <b>Adding another generation's row to a catalog "as scaffolding" is what breaks this class</b> — the
/// scaffolding a future generation needs is the per-generation schema, not a stray row.</para>
///
/// <para><b>The shape the fix takes</b> (spelled out so it needs no rediscovery): the schema work adds a
/// <c>GenerationIntroduced</c> column to <see cref="PokemonSpecies"/>, <see cref="Attack"/> and
/// <see cref="Item"/>, and each accessor below becomes
/// <c>all.Where(x =&gt; x.GenerationIntroduced &lt;= 1)</c> — translated to SQL by EF, because the seam passes
/// the query along rather than a materialised list. Note <c>&lt;=</c>, not <c>==</c>: a generation inherits
/// everything introduced before it. Nothing else on the run path changes.</para>
///
/// <para><b>An identity function is still load-bearing.</b> It is what makes the call sites <i>ask</i>, and the
/// asking is the deliverable — the alternative is a future generation hunting every unfiltered
/// <c>ToListAsync()</c> by hand. That the identity is genuinely reached at each site is pinned against a second
/// scope in the test project, which is the only way to observe it (<c>docs/GENERATION_PROFILE.md</c> §3).</para>
/// </remarks>
public sealed class Gen1ContentScope : IContentScope
{
    /// <summary>The shared instance — stateless, like the seam singletons it sits beside on the profile.</summary>
    public static readonly Gen1ContentScope Instance = new();

    private Gen1ContentScope() { }

    /// <inheritdoc />
    public IQueryable<PokemonSpecies> Species(IQueryable<PokemonSpecies> all) => all;

    /// <inheritdoc />
    public IQueryable<Attack> Moves(IQueryable<Attack> all) => all;

    /// <inheritdoc />
    public IQueryable<Item> Items(IQueryable<Item> all) => all;
}
