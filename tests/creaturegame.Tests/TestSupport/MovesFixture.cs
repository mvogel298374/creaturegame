using creaturegame.Attacks;
using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.TestSupport;

/// <summary>
/// Loads real Gen 1 moves from the live <c>moves.db</c> (resolved at the repo root by
/// <see cref="DbPathHelper"/>) so attack-behaviour tests run the actually-imported rows through
/// the engine. Opened once per test collection and cached.
/// <para>
/// Requires the database to be populated — run <c>PokeApiConnector</c> on a fresh checkout. A
/// missing row fails fast with a clear message rather than a confusing null.
/// </para>
/// </summary>
public sealed class MovesFixture : IDisposable
{
    private readonly MovesDbContext _ctx = new();
    private readonly Dictionary<string, Attack> _byName;
    private readonly Dictionary<int, Attack> _byId;

    public MovesFixture()
    {
        List<Attack> moves;
        try
        {
            moves = _ctx.Moves.AsNoTracking().ToList();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not read moves.db. Run `dotnet run --project PokeApiConnector` to create and populate it.", ex);
        }

        if (moves.Count == 0)
            throw new InvalidOperationException(
                "moves.db is empty. Run `dotnet run --project PokeApiConnector` to populate it.");

        _byName = moves.Where(m => m.Name != null).ToDictionary(m => m.Name!, StringComparer.OrdinalIgnoreCase);
        _byId   = moves.ToDictionary(m => m.Id);
    }

    /// <summary>Fresh <see cref="PokemonAttack"/>-ready copy of the move by its PokeAPI name (e.g. "fire-punch").</summary>
    public Attack Get(string name) =>
        _byName.TryGetValue(name, out var move)
            ? move
            : throw new InvalidOperationException($"No move named '{name}' in moves.db (re-run PokeApiConnector?).");

    public Attack Get(int id) =>
        _byId.TryGetValue(id, out var move)
            ? move
            : throw new InvalidOperationException($"No move with id {id} in moves.db.");

    public void Dispose() => _ctx.Dispose();
}

/// <summary>xUnit collection so the live-DB fixture is shared across the attack-behaviour tests.</summary>
[CollectionDefinition(Name)]
public sealed class MovesCollection : ICollectionFixture<MovesFixture>
{
    public const string Name = "Gen1 moves (live db)";
}
