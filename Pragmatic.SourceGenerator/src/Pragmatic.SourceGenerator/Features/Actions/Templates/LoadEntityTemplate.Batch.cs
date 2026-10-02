using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Several <c>[LoadEntity]</c> of one entity by key, read in one query.
/// </summary>
/// <remarks>
///     <para>
///         Each load was a round trip of its own; two of one table are one <c>WHERE Id IN (…)</c>, then each field
///         taken from what it answered — through <c>FindAsync</c>, the repository's filtered, tracked set that
///         <c>GetByIdAsync</c> reads, so a row a filter hides is missing here as it is there.
///     </para>
///     <para>
///         Only the plain key form is merged: loads with the same <c>Include</c> paths (different paths are
///         different reads), not a rule, a logic key, an existence check or a list. Different entities stay apart:
///         one DbContext runs one query at a time, and a single query across tables was not worth building
///         without a measurement.
///     </para>
/// </remarks>
internal sealed partial class LoadEntityTemplate
{
    /// <summary>For each load that is merged, the loads it is read with — in declaration order.</summary>
    private static Dictionary<LoadEntityModel, List<LoadEntityModel>> Batches(EquatableArray<LoadEntityModel> loads)
    {
        var groups = new Dictionary<string, List<LoadEntityModel>>(StringComparer.Ordinal);
        foreach (var le in loads)
        {
            if (le.ExistsOnly || le.IsMany || le.IsBySpecification || le.LogicKeyMember is not null)
                continue;

            var key = le.EntityTypeFullName + "|" + string.Join(",", le.Includes);
            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = [];
            group.Add(le);
        }

        var batchOf = new Dictionary<LoadEntityModel, List<LoadEntityModel>>();
        foreach (var group in groups.Values.Where(g => g.Count > 1))
            foreach (var le in group)
                batchOf[le] = group;

        return batchOf;
    }

    /// <summary>
    ///     The keys the loads carry — a null optional key not among them — read in one query, then each load's
    ///     field taken by its key: a key that names nothing is a 404 naming it, in declaration order.
    /// </summary>
    private static IEnumerable<string> BatchStatements(string operation, List<LoadEntityModel> batch)
    {
        const string linq = "global::System.Linq.Enumerable";
        var first = batch[0];
        var keys = $"__{Stem(first)}BatchKeys";
        var byKey = $"__{Stem(first)}BatchByKey";

        yield return $"var {keys} = new global::System.Collections.Generic.List<{first.KeyTypeFullName}>({batch.Count});";
        foreach (var le in batch)
        {
            var given = $"{operation}.{le.IdPropertyName}";
            if (le.IsOptional)
            {
                yield return $"if ({given}.HasValue)";
                yield return $"    {keys}.Add({given}.Value);";
            }
            else
            {
                yield return $"{keys}.Add({given});";
            }
        }

        yield return $"var {byKey} = {keys}.Count == 0";
        yield return $"    ? new global::System.Collections.Generic.Dictionary<{first.KeyTypeFullName}, {first.EntityTypeFullName}>()";
        yield return $"    : {linq}.ToDictionary(await {Rows(first, keys)}.ConfigureAwait(false), __row => __row.PersistenceId);";

        foreach (var le in batch)
        {
            var variable = Variable(le);
            var notFound = $"return global::Pragmatic.Result.Http.NotFoundError.For(\"{le.EntityTypeShortName}\", ";
            var given = $"{operation}.{le.IdPropertyName}";
            if (le.IsOptional)
            {
                var key = $"__{Stem(le)}Key";
                var row = $"__{Stem(le)}Row";
                yield return $"{le.EntityTypeFullName}? {variable} = null;";
                yield return $"if ({given} is {{ }} {key})";
                yield return "{";
                yield return $"    if (!{byKey}.TryGetValue({key}, out var {row}))";
                yield return $"        {notFound}{key}.ToString());";
                yield return $"    {variable} = {row};";
                yield return "}";
                continue;
            }

            yield return $"if (!{byKey}.TryGetValue({given}, out var {variable}))";
            yield return $"    {notFound}{given}.ToString());";
        }
    }

    /// <summary>The rows whose id is among <paramref name="keys" />, with the loads' <c>Include</c> paths if any.</summary>
    private static string Rows(LoadEntityModel le, string keys)
    {
        var spec = $"global::Pragmatic.Specification.Spec<{le.EntityTypeFullName}>.Where(__row => global::System.Linq.Enumerable.Contains({keys}, __row.PersistenceId))";
        if (le.Includes.Count == 0)
            return $"{le.RepositoryFieldName}.FindAsync({spec}, ct)";

        const string ef = "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
        var query = $"{le.RepositoryFieldName}.Query()";
        foreach (var path in le.Includes)
            query = $"{ef}.Include({query}, \"{path}\")";

        return $"{ef}.ToListAsync(global::System.Linq.Queryable.Where({query}, ({spec}).ToExpression()), ct)";
    }
}
