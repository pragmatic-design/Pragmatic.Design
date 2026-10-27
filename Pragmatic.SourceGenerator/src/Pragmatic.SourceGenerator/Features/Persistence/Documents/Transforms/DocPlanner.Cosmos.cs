using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>The chain rendered for Cosmos DB: the query's text and parameters, the partition key's value, and a point read when the chain names one document.</summary>
internal sealed partial class DocPlanner
{
    /// <summary>The Cosmos DB query's initializer (<c>new DocCosmosQuery(text, parameters, partition, stateful)</c>).</summary>
    private string? _cosmos;

    private void Cosmos()
    {
        if (_groupKey is not null)
            return; // Cosmos DB's GROUP BY is not translated: the execution refuses it there
        var context = new DocCosmosText();
        var filter = DocCondition.All(_conditions);
        var families = _entity.IsPragmaticEntity || PredicateSlot >= 0; // the caller's filter families and the Matching predicate, resolved per execution (DocFamilies)
        string text;
        string? stateful = null; // what needs state across continuations, for the PRAG3106 note
        switch (_call.Name)
        {
            case "ExecuteDeleteAsync" or "ExecuteUpdateAsync":
                text = DocCosmosText.Select("*", filter, [], context, families: families); // the documents to write by id, read whole (DocExecution.CosmosCommandAsync)
                break;
            case "CountAsync":
                text = DocCosmosText.Select("VALUE COUNT(1)", filter, [], context, families: families);
                stateful = "a count";
                break;
            case "AnyAsync":
                text = DocCosmosText.Select("VALUE 1", filter, [], context, families: families);
                break;
            case "MaxAsync" or "MinAsync" or "SumAsync":
                text = DocCosmosText.Select($"VALUE {_call.Name.Substring(0, 3).ToUpperInvariant()}({DocCosmosText.Path(AggregateField!, "c")})", filter, [], context, families: families);
                stateful = "an aggregate";
                break;
            case "ToListAsync" when _unwind is not null:
                text = DocCosmosText.Select("VALUE u", filter, _sort, context, "c JOIN u IN " + DocCosmosText.Path(_unwind, "c"), families); // the elements of the list
                if (_sort.Count > 0)
                    stateful = "an ordering";
                break;
            default:
                // A merge across partitions reads the ordering's keys back from the documents: a projection's fields keep them (all of them for
                // an ordering chosen at runtime).
                var read = Fields is null || OrderingSlot >= 0 ? null : Fields.Union(_sort.Select(k => k.Path.Split('.')[0])).ToList();
                var selection = read is null ? "*" : string.Join(", ", read.Select(f => $"{DocCosmosText.Path(f, "c")} AS {(f == "_id" ? "id" : f)}"));
                text = DocCosmosText.Select(selection, filter, _sort, context, families: families) + (OrderingSlot >= 0 ? "{order}" : "");
                if (_sort.Count > 0 || OrderingSlot >= 0)
                    stateful = "an ordering";
                break;
        }

        // The partition key's value: an equality on it among the conditions every document must meet (the top-level $and).
        var partitionField = _entity.PartitionKey?.Name;
        var partition = partitionField is null ? null : Conjunction(_conditions).OfType<DocCondition.Compare>().Select(c => c.EqualityOn(partitionField)).FirstOrDefault(v => v is not null);
        if (stateful is not null && partition is null)
            _diagnostics.Add(Diagnostic.Create(DocDiagnostics.CosmosNeedsPartition, _call.Invocation.GetLocation(), _call.Name, stateful, _entity.PartitionKey?.Property.Name ?? "the key"));
        var parameters = context.HasParameters ? $"static (w, s) => {{ {context.Parameters}}}" : "null";
        var partitionWriter = partition is null ? "null" : $"static (w, s) => global::Pragmatic.Persistence.Documents.Conversion.DocJson.WriteValue(w, {partition.Code})";
        _cosmos = $"new global::Pragmatic.Persistence.Documents.Planning.DocCosmosQuery({DocTypeEmitter.Literal(text)}, {parameters}, {partitionWriter}, {(stateful is null ? "false" : "true")}, {PointId(partition)}, {PredicateSlot}, {OrderingSlot}, {SortKeysInitializer()})";
        CosmosText = text;
    }

    /// <summary>
    ///     The document's id as Cosmos DB names it when the chain is a read of one document by its key and partition key (their equalities and
    ///     no other condition, no ordering): the execution GETs that document (a point read) instead of querying. "null" for every other chain.
    /// </summary>
    private string PointId(DocValue? partition)
    {
        if (_entity.Key is not { } key || partition is null || _sort.Count > 0 || OrderingSlot >= 0 || _call.Name is "CountAsync" or "AnyAsync" or "MaxAsync" or "MinAsync" or "SumAsync")
            return "null";
        var conditions = Conjunction(_conditions).ToList();
        var id = conditions.OfType<DocCondition.Compare>().Select(c => c.EqualityOn(key.Name)).FirstOrDefault(v => v is not null);
        return id is not null && conditions.Count == (key == _entity.PartitionKey ? 1 : 2) && conditions.All(c => c is DocCondition.Compare)
            ? $"static s => {DocJsonEmitter.IdText(key, id.Code)}"
            : "null";
    }

    /// <summary>The Cosmos DB query's text, for the PRAG3199 note.</summary>
    public string? CosmosText { get; private set; }

    private static IEnumerable<DocCondition> Conjunction(IEnumerable<DocCondition> conditions)
    {
        foreach (var condition in conditions)
        {
            if (condition is DocLogical { Op: "$and" } and)
            {
                foreach (var inner in Conjunction(and.Items))
                    yield return inner;
            }
            else
            {
                yield return condition;
            }
        }
    }
}
