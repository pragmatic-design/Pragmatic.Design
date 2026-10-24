using System.Text;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>
///     An ordering composed at runtime (a declared query's sorts, a grid's sort), the Sql engine's <c>SqlOrdering</c> for documents: its keys in
///     order, each ascending or descending, and the key it falls back to when none was chosen. Read by <c>DocQuery.OrderedBy</c>, which keeps it
///     in the chain's slots: MongoDB's <c>sort</c> written from it, Cosmos DB's <c>ORDER BY</c> put in the query's text.
/// </summary>
public sealed class DocOrdering<T>
{
    private readonly DocOrderKey _fallback;
    private readonly (DocOrderKey Key, bool Descending)[] _keys;

    private DocOrdering(DocOrderKey fallback, (DocOrderKey, bool)[] keys)
    {
        _fallback = fallback;
        _keys = keys;
    }

    /// <summary>No key yet: the fallback orders, ascending.</summary>
    public static DocOrdering<T> By(DocOrderKey fallback) => new(fallback, []);

    /// <summary>The ordering with one key more, after the ones it has.</summary>
    public DocOrdering<T> Then(DocOrderKey key, bool descending) => new(_fallback, [.. _keys, (key, descending)]);

    /// <summary>The keys the reads sort by: the ones chosen, or the fallback alone, as the Sql engine renders it.</summary>
    public IReadOnlyList<(DocOrderKey Key, bool Descending)> Keys => _keys.Length > 0 ? _keys : [(_fallback, false)];

    /// <summary>MongoDB: the sort document's elements.</summary>
    public void WriteMongo(BsonWriter w)
    {
        foreach (var (key, descending) in Keys)
            w.Write(key.Mongo, descending ? -1 : 1);
    }

    /// <summary>Cosmos DB: the query's <c>ORDER BY</c> clause, with its leading space.</summary>
    public string CosmosText()
    {
        var text = new StringBuilder(" ORDER BY ");
        var first = true;
        foreach (var (key, descending) in Keys)
        {
            if (!first)
                text.Append(", ");
            text.Append(key.Cosmos).Append(descending ? " DESC" : " ASC");
            first = false;
        }

        return text.ToString();
    }
}
