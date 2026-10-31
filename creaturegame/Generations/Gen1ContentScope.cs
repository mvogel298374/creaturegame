using creaturegame.Attacks;
using creaturegame.DB;
using creaturegame.Items;

namespace creaturegame.Generations;

/// <summary>Gen 1's content scope: an identity stub, correct only while the catalogs hold Gen 1 alone (GENERATION_PROFILE.md §5(b)).</summary>
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
