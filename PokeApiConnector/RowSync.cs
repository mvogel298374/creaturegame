using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector;

/// <summary>Brings tracked rows to a desired set by natural key, so an unchanged re-import saves nothing (DATA_IMPORT.md §5.3).</summary>
public static class RowSync
{
    public static void Apply<T, TKey>(
        DbContext context,
        IEnumerable<T> existing,
        IEnumerable<T> desired,
        Func<T, TKey> key
    )
        where T : class
        where TKey : notnull
    {
        var unmatched = existing.GroupBy(key).ToDictionary(g => g.Key, g => new Queue<T>(g));
        foreach (var row in desired)
        {
            if (unmatched.TryGetValue(key(row), out var current) && current.Count > 0)
                CopyValues(context, current.Dequeue(), row);
            else
                context.Add(row);
        }
        context.RemoveRange(unmatched.Values.SelectMany(q => q));
    }

    /// <summary>Copies every non-key value onto the tracked row; EF writes only the columns that changed.</summary>
    public static void CopyValues<T>(DbContext context, T current, T desired)
        where T : class
    {
        var entry = context.Entry(current);
        foreach (var keyProperty in entry.Metadata.FindPrimaryKey()!.Properties)
            keyProperty.PropertyInfo!.SetValue(
                desired,
                entry.Property(keyProperty.Name).CurrentValue
            );
        entry.CurrentValues.SetValues(desired);
    }
}
