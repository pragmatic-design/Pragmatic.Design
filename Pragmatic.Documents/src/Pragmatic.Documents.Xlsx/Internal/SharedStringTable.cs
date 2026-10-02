namespace Pragmatic.Documents.Xlsx.Internal;

/// <summary>
/// Collects unique strings and assigns indices for sharedStrings.xml.
/// XLSX de-duplicates string values across all cells.
/// </summary>
internal sealed class SharedStringTable
{
    private readonly Dictionary<string, int> _map = new(StringComparer.Ordinal);
    private readonly List<string> _strings = [];
    private int _totalCount;

    /// <summary>Get or add a string, returning its zero-based index.</summary>
    internal int GetOrAdd(string value)
    {
        _totalCount++;

        if (_map.TryGetValue(value, out var index))
            return index;

        index = _strings.Count;
        _strings.Add(value);
        _map[value] = index;
        return index;
    }

    /// <summary>Total number of string-cell usages (the OOXML <c>count</c> attribute).</summary>
    internal int TotalCount => _totalCount;

    /// <summary>Number of distinct strings (the OOXML <c>uniqueCount</c> attribute).</summary>
    internal int UniqueCount => _strings.Count;

    internal int Count => _strings.Count;
    internal IReadOnlyList<string> Strings => _strings;
}
