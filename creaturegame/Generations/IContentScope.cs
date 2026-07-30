using creaturegame.Attacks;
using creaturegame.DB;
using creaturegame.Items;

namespace creaturegame.Generations;

/// <summary>
/// Which <b>catalog content</b> — species, moves, items — exists in this generation. The content-scope seam of
/// <c>docs/GENERATION_PROFILE.md</c> §5(b), Stage 2b.
/// </summary>
/// <remarks>
/// <para><b>The question this answers, and why it is not the type roster's question.</b>
/// <see cref="GenerationProfile.TypeRoster"/> (Stage 2a) says which <i>types</i> exist; this says which
/// <i>rows</i> the run may draw from. Gen 1 has no Steel type <i>and</i> no Steelix — two separate facts, and
/// only the first was stated before this stage. Everything else about the run's content was Gen 1 purely
/// because the databases hold nothing else, which is an assumption the code never made explicit anywhere.</para>
///
/// <para><b>Why a documented no-op today.</b> <c>GENERATION_SEAMS.md §5.0</c> rules this case directly: <i>"If
/// 'yes' but you're not building Gen 2 yet: still add the seam member, implement the Gen 1 value, and — when the
/// data layout is what differs — make the Gen 1 implementation a documented stub that shows the generic
/// shape."</i> Here the data layout <i>is</i> what differs: the filter a real second generation needs is a
/// <c>GenerationIntroduced</c> column that <b>does not exist yet</b> on any of the three tables. That column and
/// its import are schema work, deliberately still deferred to <c>TODO.md</c> → <i>Multi-Generation</i>. So this
/// seam is the socket that work plugs into, and <see cref="Gen1ContentScope"/> is the stub that shows the
/// shape. The alternative — nothing — means a future generation has to find every unfiltered
/// <c>ToListAsync()</c> by archaeology, which is the cost §5.0 says to pay down now.</para>
///
/// <para><b>Why <see cref="IQueryable{T}"/> and not a predicate.</b> A <c>Func&lt;PokemonSpecies, bool&gt;</c>
/// would force the whole table into memory before filtering, and would have to be re-plumbed the day the real
/// filter lands. Composing onto the query instead means a later
/// <c>all.Where(s =&gt; s.GenerationIntroduced &lt;= gen)</c> is translated to SQL by EF and never materialises
/// the out-of-scope rows at all. The seam is therefore already the right shape, not merely the right idea — the
/// implementation is the only thing left to write.</para>
///
/// <para><b>What this seam deliberately does NOT cover: learnsets and evolution edges.</b> Those tables carry an
/// explicit <c>Generation</c> column already, and Stage 1b wired their six queries to filter on
/// <c>(int)profile.Generation</c> directly. They need no scope member precisely <i>because</i> they solved the
/// problem the way this seam is a placeholder for. Nor does <c>PokemonGameAvailability</c>: it is keyed by
/// species id and is only ever intersected with an already-scoped species pool, so scoping the species scopes
/// the availability rows with it.</para>
///
/// <para><b>Implementing this for a real generation</b> means writing the sibling of
/// <see cref="Gen1ContentScope"/> — never adding a branch here, and never adding one at a call site.</para>
/// </remarks>
public interface IContentScope
{
    /// <summary>The species this generation's runs may encounter, draft, catch, evolve into or start as.</summary>
    /// <param name="all">The unfiltered species table, as an un-materialised query.</param>
    /// <returns>The in-generation subset — for Gen 1 today, <paramref name="all"/> unchanged.</returns>
    IQueryable<PokemonSpecies> Species(IQueryable<PokemonSpecies> all);

    /// <summary>The moves this generation's creatures may know.</summary>
    /// <remarks>Applied to the run's one-time move-pool load, so it reaches every moveset built from it —
    /// starter, wild enemy, draft offer and boss catch alike.</remarks>
    /// <param name="all">The unfiltered move table, as an un-materialised query.</param>
    /// <returns>The in-generation subset — for Gen 1 today, <paramref name="all"/> unchanged.</returns>
    IQueryable<Attack> Moves(IQueryable<Attack> all);

    /// <summary>The items this generation's runs may hold, buy or be rewarded.</summary>
    /// <remarks>Applied to the run's one-time item-catalog load, which seeds the starting bag and backs the
    /// shop and reward rolls.</remarks>
    /// <param name="all">The unfiltered item table, as an un-materialised query.</param>
    /// <returns>The in-generation subset — for Gen 1 today, <paramref name="all"/> unchanged.</returns>
    IQueryable<Item> Items(IQueryable<Item> all);
}
