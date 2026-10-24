using System.Collections.Concurrent;
using System.Linq.Expressions;
using Pragmatic.Persistence.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>
///     The shapes translated so far, per row type: bounded, cleared when full (a runtime chain built from a grid has
///     a few dozen shapes). Looked up by the key's characters, so a hit allocates no string.
/// </summary>
internal static class ShapeCache<T>
{
    private const int Limit = 1024;

    public static readonly ConcurrentDictionary<string, ShapeEntry<T>> Entries = new(StringComparer.Ordinal);

    public static readonly ConcurrentDictionary<string, ShapeEntry<T>>.AlternateLookup<ReadOnlySpan<char>> Lookup = Entries.GetAlternateLookup<ReadOnlySpan<char>>();

    public static void Add(string key, ShapeEntry<T> entry)
    {
        if (Entries.Count >= Limit)
            Entries.Clear();
        Entries[key] = entry;
    }
}
