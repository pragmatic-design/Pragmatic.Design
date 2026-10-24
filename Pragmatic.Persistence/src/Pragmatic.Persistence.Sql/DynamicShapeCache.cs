using System.Collections.Concurrent;
using System.Linq.Expressions;
using Pragmatic.Persistence.Expressions;

namespace Pragmatic.Persistence.Sql;

/// <summary>How a captured sub-tree becomes a parameter value: as it is, as a LIKE pattern, as an IN list, the constant true a bare boolean column compares to, or a literal kept with the shape (a collection include's filter).</summary>
internal enum SqlCaptureKind
{
    Value,
    Like,
    List,
    True,
    Constant,
}

/// <summary>Reads the value a captured node stands for (through the column's converter, when the value is compared with a converted column); the planner records where it came from so the next call with the same shape reads only that.</summary>
internal delegate object? SqlCapture(Expression? node, SqlCaptureKind kind, string prefix, string suffix, Func<object?, object?>? convert);

/// <summary>One parameter of a translated shape: which leaf of the tree it is read from, how, and through which converter; or the constant it always is.</summary>
internal readonly record struct CaptureSource(int Leaf, SqlCaptureKind Kind, string Prefix, string Suffix, object? Constant = null, Func<object?, object?>? Convert = null)
{
    public object? Read(List<Expression> leaves)
    {
        var value = Kind switch
        {
            SqlCaptureKind.True => true,
            SqlCaptureKind.Constant => Constant,
            SqlCaptureKind.Like => Captured.Like(leaves[Leaf], Prefix, Suffix),
            SqlCaptureKind.List => Captured.List(leaves[Leaf]),
            _ => Captured.Evaluate(leaves[Leaf]),
        };
        return Convert is null ? value : Convert(value);
    }

    /// <summary>The direct read, for a chain translated without the shape cache.</summary>
    public static object? Direct(Expression? node, SqlCaptureKind kind, string prefix, string suffix, Func<object?, object?>? convert)
    {
        var value = kind switch
        {
            SqlCaptureKind.True => true,
            SqlCaptureKind.Like => Captured.Like(node!, prefix, suffix),
            SqlCaptureKind.List => Captured.List(node!),
            _ => Captured.Evaluate(node!),
        };
        return convert is null ? value : convert(value);
    }
}

/// <summary>A shape already translated: its plan, and where each parameter's value sits in a tree of that shape.</summary>
internal sealed class ShapeEntry<T>(SqlPlan<T> plan, CaptureSource[] sources)
{
    public SqlPlan<T> Plan { get; } = plan;

    public object?[] Values(List<Expression> leaves)
    {
        var values = new object?[sources.Length];
        for (var i = 0; i < values.Length; i++)
            values[i] = sources[i].Read(leaves);
        return values;
    }
}

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
