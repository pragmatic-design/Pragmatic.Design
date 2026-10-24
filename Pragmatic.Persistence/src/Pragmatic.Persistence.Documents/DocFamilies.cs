using System.Text;
using System.Text.Json;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     What a read adds at runtime to the conditions its chain was compiled with: a Pragmatic entity's filter families
///     (<see cref="IDocumentEntity{TSelf}.Families" />), each one's state asked of the caller (<see cref="IDocFilters" />), and the chain's
///     <c>Matching</c> predicate (a specification's compiled leaves). A closed family makes the read match nothing. The conditions are a tree
///     on MongoDB and a text on Cosmos DB, so a combination is written per call: there are no variants to keep.
/// </summary>
public static partial class DocFamilies
{
    /// <summary>Where a Cosmos DB query's text takes them: after the chain's own WHERE (<see cref="AndMarker" />) or as the WHERE (<see cref="WhereMarker" />).</summary>
    public const string AndMarker = "{families:and}";

    public const string WhereMarker = "{families:where}";

    /// <summary>
    ///     The chain's conditions, the predicate and the active families under <c>$and</c>, into the open filter document; false (nothing written)
    ///     when there is neither a predicate nor a family that applies, the chain's conditions then written as they are.
    /// </summary>
    internal static bool WriteMongo<TEntity>(BsonWriter w, IDocFilters? filters, DocFilterWriter? chain, ReadOnlySpan<object?> slots, DocPredicate<TEntity>? predicate = null) where TEntity : IDocumentEntity<TEntity>
    {
        var families = TEntity.Families;
        var applies = Applies(families, filters, out var closed);
        if (!applies && predicate is null)
            return false;
        w.StartArray("$and");
        var item = 0;
        if (chain is not null)
        {
            w.StartDocument(item++);
            chain(w, slots);
            w.EndDocument();
        }

        if (predicate is not null)
        {
            w.StartDocument(item++);
            predicate.WriteMongo(w);
            w.EndDocument();
        }

        if (closed)
        {
            w.StartDocument(item);
            w.StartDocument("_id");
            w.Write("$exists", false); // every document has its _id: none matches
            w.EndDocument();
            w.EndDocument();
        }
        else if (applies)
        {
            foreach (var family in families)
            {
                if (StateOf(family, filters) != DocFilterState.Active)
                    continue;
                w.StartDocument(item++);
                family.Mongo(w, filters);
                w.EndDocument();
            }
        }

        w.EndArray();
        return true;
    }

    /// <summary>
    ///     A Cosmos DB query's text with its marker resolved (the predicate and the active families; <c>false</c> when one is closed; nothing when
    ///     none applies), the predicate's leaves collected for <see cref="CosmosParameters{TEntity}" />.
    /// </summary>
    internal static string CosmosText<TEntity>(string text, IDocFilters? filters, DocPredicate<TEntity>? predicate, List<(DocCompiledPredicate Leaf, object?[] Values, string Prefix)> leaves) where TEntity : IDocumentEntity<TEntity>
    {
        var and = text.IndexOf(AndMarker, StringComparison.Ordinal);
        var where = and >= 0 ? -1 : text.IndexOf(WhereMarker, StringComparison.Ordinal);
        if (and < 0 && where < 0)
            return text;
        var families = TEntity.Families;
        var parts = new StringBuilder();
        if (predicate is not null)
            predicate.WriteCosmos(parts, leaves);
        if (Applies(families, filters, out var closed))
        {
            if (closed)
                parts.Append(parts.Length == 0 ? "false" : " AND false");
            else
            {
                foreach (var family in families)
                {
                    if (StateOf(family, filters) == DocFilterState.Active)
                        parts.Append(parts.Length == 0 ? "(" : " AND (").Append(family.Cosmos).Append(')');
                }
            }
        }

        var marker = and >= 0 ? AndMarker : WhereMarker;
        var replacement = parts.Length == 0 ? "" : (and >= 0 ? " AND " : " WHERE ") + parts;
        return text.Replace(marker, replacement);
    }

    /// <summary>The predicate's and the active families' parameters, into the query's open <c>parameters</c> array.</summary>
    internal static void CosmosParameters<TEntity>(Utf8JsonWriter w, IDocFilters? filters, List<(DocCompiledPredicate Leaf, object?[] Values, string Prefix)> leaves) where TEntity : IDocumentEntity<TEntity>
    {
        DocPredicate<TEntity>.WriteCosmosParameters(w, leaves);
        var families = TEntity.Families;
        if (families.Length == 0 || !Applies(families, filters, out var closed) || closed)
            return;
        foreach (var family in families)
        {
            if (family.CosmosParameters is not null && StateOf(family, filters) == DocFilterState.Active)
                family.CosmosParameters(w, filters);
        }
    }

    /// <summary>The partition an active tenant family names (the tenant, as the partition key header's array); null when none does.</summary>
    internal static string? Partition<TEntity>(IDocFilters? filters) where TEntity : IDocumentEntity<TEntity>
    {
        foreach (var family in TEntity.Families)
        {
            if (family.PartitionsByTenant && filters?.TenantId is { } tenant && StateOf(family, filters) == DocFilterState.Active)
                return "[\"" + JsonEncodedText.Encode(tenant).ToString() + "\"]";
        }

        return null;
    }

    /// <summary>True when the entity's reads carry a condition of the families (an active or a closed one): a point read would skip them.</summary>
    internal static bool AnyApplies<TEntity>(IDocFilters? filters) where TEntity : IDocumentEntity<TEntity> => Applies(TEntity.Families, filters, out _);

    private static bool Applies(DocFamily[] families, IDocFilters? filters, out bool closed)
    {
        closed = false;
        var applies = false;
        foreach (var family in families)
        {
            var state = StateOf(family, filters);
            closed |= state == DocFilterState.Closed;
            applies |= state != DocFilterState.Lifted;
        }

        return applies;
    }

    private static DocFilterState StateOf(DocFamily family, IDocFilters? filters) => filters?.StateOf(family.Slot) ?? DocFilterState.Active;
}
