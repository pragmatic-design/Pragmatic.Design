using System.Collections.Concurrent;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     Huffman tables keyed by the bytes that define them. Nearly every JPEG carries the standard
///     tables of Annex K, and a table costs a lookup of 4,096 entries plus the fast AC table to build:
///     built once per distinct definition, shared by every decode after. Bounded, so a stream of files
///     with fitted tables cannot grow it without limit. The key is a 64-bit FNV-1a of the definition
///     plus its length; a collision would hand out the wrong table, and 2^-64 is accepted.
/// </summary>
internal static class JpegHuffmanTableCache
{
    private const int Limit = 64;

    private static readonly ConcurrentDictionary<Key, JpegHuffmanTable> Tables = new();

    public static JpegHuffmanTable Get(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
    {
        var key = new Key(Fnv1a(counts), Fnv1a(values), values.Length);
        if (Tables.TryGetValue(key, out var table))
            return table;

        table = new JpegHuffmanTable(counts, values);
        if (Tables.Count < Limit)
            Tables.TryAdd(key, table);
        return table;
    }

    private static ulong Fnv1a(ReadOnlySpan<byte> data)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in data)
            hash = (hash ^ b) * 1099511628211UL;
        return hash;
    }

    private readonly record struct Key(ulong CountsHash, ulong ValuesHash, int ValuesLength);
}
