using System.Linq;

namespace Pragmatic.Persistence.Documents.Generator;

/// <summary>
///     An ordering composed at runtime (<c>OrderedBy(DocOrdering&lt;T&gt;)</c>, a declared query's or a grid's sorts): kept in a slot, in place of
///     the chain's OrderBy (one or the other), on the documents themselves (before a Select, never with a GroupBy, a SelectMany or a Join, whose
///     reads would not see it). MongoDB's sort is written from it per call; Cosmos DB's text carries an <c>{order}</c> marker it fills.
/// </summary>
internal sealed partial class DocPlanner
{
    /// <summary>The slot of the chain's <c>OrderedBy</c> ordering; -1 when it has none.</summary>
    public int OrderingSlot { get; private set; } = -1;

    private void OrderedBy(DocStep step)
    {
        if (OrderingSlot >= 0 || _sort.Count > 0)
        {
            Fail(step.Invocation, "one ordering per chain: an OrderedBy, or an OrderBy and its ThenBys");
            return;
        }

        OrderingSlot = _slot++;
    }

    /// <summary>An OrderedBy beside an OrderBy, or before a GroupBy, a SelectMany or a Join: refused, not left unread.</summary>
    private void RefuseOrderedByWhereUnread()
    {
        if (OrderingSlot < 0)
            return;
        if (_sort.Count > 0)
            Fail(_call.Invocation, "one ordering per chain: an OrderedBy, or an OrderBy and its ThenBys");
        else if (_groupKey is not null || _unwind is not null || JoinInitializer is not null)
            Fail(_call.Invocation, "an OrderedBy with a GroupBy, a SelectMany or a Join is not translated: order those with OrderBy");
    }

    /// <summary>The chain's OrderBy keys as a Cosmos DB document holds them (<c>_id</c> is <c>id</c>), for a merge across partitions; "null" when there is none.</summary>
    private string SortKeysInitializer()
        => _sort.Count == 0
            ? "null"
            : "new (string[], bool)[] { " + string.Join(", ", _sort.Select(k => $"(new string[] {{ {string.Join(", ", CosmosSegments(k.Path).Select(DocTypeEmitter.Literal))} }}, {(k.Descending ? "true" : "false")})")) + " }";

    private static string[] CosmosSegments(string path)
    {
        var segments = path.Split('.');
        if (segments[0] == "_id")
            segments[0] = "id";
        return segments;
    }
}
