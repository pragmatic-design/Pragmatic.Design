using System.Text.Json;

namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     Cosmos DB's order of values, which a merge across partitions must follow, each key ascending or descending. ASSUMPTION, not checked
///     against the service's documentation in this step: undefined, then null, false, true, numbers, strings, by type first; strings by
///     ordinal (UTF-16) comparison, which differs from a byte order only past the BMP. The emulator's single range cannot tell (INVESTIGATE).
/// </summary>
public sealed class CosmosOrder(IReadOnlyList<(IReadOnlyList<string> Segments, bool Descending)> keys) : IComparer<JsonElement>
{
    public int Compare(JsonElement x, JsonElement y)
    {
        foreach (var (segments, descending) in keys)
        {
            var compared = CompareValues(At(x, segments), At(y, segments));
            if (compared != 0)
                return descending ? -compared : compared;
        }

        return 0;
    }

    /// <summary>The value at a path; null (undefined) when a segment is missing.</summary>
    private static JsonElement? At(JsonElement document, IReadOnlyList<string> segments)
    {
        var current = document;
        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;
        }

        return current;
    }

    internal static int CompareValues(JsonElement? x, JsonElement? y)
    {
        var (rx, ry) = (Rank(x), Rank(y));
        if (rx != ry)
            return rx.CompareTo(ry);
        return rx switch
        {
            2 => x!.Value.GetBoolean().CompareTo(y!.Value.GetBoolean()),
            3 => x!.Value.TryGetDecimal(out var a) && y!.Value.TryGetDecimal(out var b) ? a.CompareTo(b) : x.Value.GetDouble().CompareTo(y!.Value.GetDouble()),
            4 => string.CompareOrdinal(x!.Value.GetString(), y!.Value.GetString()),
            _ => 0, // undefined with undefined, null with null; an array or an object is not an ordering key
        };
    }

    private static int Rank(JsonElement? value) => value?.ValueKind switch
    {
        null or JsonValueKind.Undefined => 0,
        JsonValueKind.Null => 1,
        JsonValueKind.False or JsonValueKind.True => 2,
        JsonValueKind.Number => 3,
        JsonValueKind.String => 4,
        _ => 5,
    };
}
