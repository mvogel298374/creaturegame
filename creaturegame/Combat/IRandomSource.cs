namespace creaturegame.Combat;

/// <summary>
/// Source of randomness for the battle engine. Injected through the same seam
/// pattern as <see cref="IBattleRules"/> / <see cref="ITypeChart"/> so battles can
/// run on a deterministic, seeded stream (replays, reproducible tests) without the
/// engine reaching for the global <see cref="System.Random.Shared"/>.
/// <para>Implementations are not required to be thread-safe; a single battle runs
/// on one thread and pulls draws sequentially.</para>
/// </summary>
public interface IRandomSource
{
    /// <summary>Returns an int in [0, <paramref name="maxExclusive"/>).</summary>
    int Next(int maxExclusive);

    /// <summary>Returns an int in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>Returns a double in [0.0, 1.0).</summary>
    double NextDouble();
}

/// <summary>Default production source — delegates to the shared global RNG.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    public static readonly SystemRandomSource Instance = new();

    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);

    public int Next(int minInclusive, int maxExclusive) =>
        Random.Shared.Next(minInclusive, maxExclusive);

    public double NextDouble() => Random.Shared.NextDouble();
}

/// <summary>
/// Deterministic source backed by a seeded <see cref="System.Random"/>. Two sources
/// constructed with the same seed and driven through the same call sequence produce
/// identical draws — the basis for reproducible battles and seeded run replays.
/// </summary>
public sealed class SeededRandomSource(int seed) : IRandomSource
{
    private readonly Random _random = new(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    public double NextDouble() => _random.NextDouble();
}
