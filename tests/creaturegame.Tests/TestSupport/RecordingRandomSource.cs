using creaturegame.Combat;

namespace creaturegame.Tests.TestSupport;

/// <summary>Logs every draw, in call order, then forwards it to an inner source.</summary>
public sealed class RecordingRandomSource(IRandomSource inner) : IRandomSource
{
    public const string Crit = "NextDouble";
    public const string DamageRoll = "Next(217,256)";
    public const string Accuracy = "Next(256)";
    public const string MultiHitCount = "Next(8)";

    private readonly List<string> _draws = [];

    public IReadOnlyList<string> Draws => _draws;

    public int Next(int maxExclusive)
    {
        _draws.Add($"Next({maxExclusive})");
        return inner.Next(maxExclusive);
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        _draws.Add($"Next({minInclusive},{maxExclusive})");
        return inner.Next(minInclusive, maxExclusive);
    }

    public double NextDouble()
    {
        _draws.Add(Crit);
        return inner.NextDouble();
    }
}

/// <summary>Every draw returns its lowest value: a crit whenever the chance is non-zero, a hit, the lowest damage roll.</summary>
public sealed class LowestRollSource : IRandomSource
{
    public int Next(int maxExclusive) => 0;

    public int Next(int minInclusive, int maxExclusive) => minInclusive;

    public double NextDouble() => 0.0;
}

/// <summary>Every draw returns its highest value: no crit, a miss, the highest damage roll.</summary>
public sealed class HighestRollSource : IRandomSource
{
    public int Next(int maxExclusive) => maxExclusive - 1;

    public int Next(int minInclusive, int maxExclusive) => maxExclusive - 1;

    public double NextDouble() => 0.999;
}
