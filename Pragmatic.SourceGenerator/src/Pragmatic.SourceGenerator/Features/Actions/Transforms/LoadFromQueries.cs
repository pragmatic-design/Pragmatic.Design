using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Each <c>[LoadFrom&lt;TQuery&gt;]</c> checked against the declared queries: the query is one of this
///     compilation, and the property is of what it answers.
/// </summary>
/// <remarks>
///     The answer is the query model's — single, paged or a list, as its invoker returns it — and the query
///     models are the persistence generator's, so they reach this one through the pipeline rather than
///     being re-read from the query's attribute: a second reading of <c>Single</c> and <c>Paged</c> is a
///     second rule, and the two would drift.
/// </remarks>
internal static class LoadFromQueries
{
    /// <summary>The properties, each resolved — with <see cref="LoadFromQueryModel.ResultProblem" /> set when it cannot be filled.</summary>
    public static EquatableArray<LoadFromQueryModel> Resolve(
        EquatableArray<LoadFromQueryModel> loads, ImmutableArray<QueryModel> queries)
    {
        if (loads.IsDefaultOrEmpty)
            return loads;

        var byName = new Dictionary<string, QueryModel>(StringComparer.Ordinal);
        foreach (var query in queries)
            byName["global::" + query.FullTypeName] = query;

        return loads
            .Select(load => load with
            {
                IsResolved = true,
                ResultProblem = !byName.TryGetValue(load.QueryTypeFullName, out var query)
                    ? $"'{load.QueryTypeFullName}' is no declared [Query] of this compilation"
                    : query.AnswerTypeFullName != load.PropertyTypeFullName
                        ? $"'{load.PropertyName}' is '{load.PropertyTypeFullName}', and the query answers '{query.AnswerTypeFullName}'"
                        : null
            })
            .ToImmutableArray();
    }
}
