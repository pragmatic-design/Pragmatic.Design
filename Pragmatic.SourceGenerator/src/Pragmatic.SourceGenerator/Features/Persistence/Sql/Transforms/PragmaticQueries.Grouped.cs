using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A declared query whose result is a <c>[QueryView]</c> with <c>[GroupBy]</c> and aggregates (#507): the query filters, the view groups, as
///     EF Core's <c>Build</c> does (<c>QueryViewBuildTemplate</c>), from the same model (<see cref="QueryViewTransform.Of" />). The Sql engine groups in
///     the statement (<see cref="GroupedChain" />); the documents engine groups the matched documents in .NET (<see cref="GroupedInMemory" />).
/// </summary>
internal sealed partial class PragmaticQuery
{
    /// <summary>The view the query answers with, when its result is a grouped <c>[QueryView]</c>; null for any other result.</summary>
    public QueryViewModel? View { get; private set; }

    /// <summary>Reads the result as a grouped view, refusing what the engines do not compile yet, each with its reason.</summary>
    internal void ReadView(Compilation compilation)
    {
        if (QueryViewTransform.Of(Result, compilation) is not { } view)
            return;
        View = view;
        if (!view.HasGroupBy)
            Unsupported ??= $"the view {Result.Name} aggregates without a [GroupBy], which EF Core's Build refuses too";
        // A [From] property that is also a key is the key (the Showcase's Status), initialized once from it, as EF Core's Build does.
        else if (view.SourceProperties.FirstOrDefault(s => view.GroupByProperties.All(g => g.PropertyName != s.PropertyName)) is { } source)
            Unsupported ??= $"the view {Result.Name} reads {source.PropertyName} with [From] from a group's first row, which is not compiled yet";
        else if (view.GroupByProperties.Any(g => !g.IsProjected))
            Unsupported ??= $"the view {Result.Name} groups by a key it does not project, which is not compiled yet";
        else if (Sorts.Count > 0)
            Unsupported ??= $"a grouped view is ordered by its keys or aggregates, not by the sorts of the query ({Sorts[0].Name}), which is not compiled yet";
        else if (Paged)
            Unsupported ??= "a grouped view is paged, which is not compiled yet";
    }

    /// <summary>The grouping and the view's initializer as Sql chain operators, after the query's filtered chain.</summary>
    internal string GroupedChain()
        => $".GroupBy(e => new {{ {string.Join(", ", Keys(View!))} }}).Select(g => new {ResultName} {{ {string.Join(", ", Members(View!, sql: true))} }})";

    /// <summary>The view's rows from the matched documents in <paramref name="documents" />, grouped in .NET (no using of System.Linq there: every call qualified).</summary>
    internal string GroupedInMemory(string documents)
    {
        const string Linq = "global::System.Linq.Enumerable";
        return $"{Linq}.ToList({Linq}.Select({Linq}.GroupBy({documents}, e => new {{ {string.Join(", ", Keys(View!))} }}), g => new {ResultName} {{ {string.Join(", ", Members(View!, sql: false))} }}))";
    }

    /// <summary>A key through a navigation: the documents engine keeps a relation as its key, so there is no navigation to group through.</summary>
    internal string? NavigationKey => View?.GroupByProperties.FirstOrDefault(g => g.NavigationPath.Length > 0)?.KeyAccessPath;

    private static IEnumerable<string> Keys(QueryViewModel view) => view.GroupByProperties.Select((g, i) => $"K{i} = {g.KeyExpression}");

    private static IEnumerable<string> Members(QueryViewModel view, bool sql)
    {
        const string Linq = "global::System.Linq.Enumerable";
        foreach (var (key, i) in view.GroupByProperties.Select((g, i) => (g, i)))
            yield return $"{key.PropertyName} = g.Key.K{i}";
        foreach (var aggregate in view.AggregateProperties.Where(a => view.GroupByProperties.All(g => g.PropertyName != a.PropertyName)))
        {
            var (name, row) = aggregate.Kind switch
            {
                AggregateKind.Count => ("Count", aggregate.WhereClause),
                AggregateKind.Sum => ("Sum", aggregate.RowExpression),
                AggregateKind.Average => ("Average", aggregate.RowExpression),
                AggregateKind.Min => ("Min", aggregate.RowExpression),
                _ => ("Max", aggregate.RowExpression),
            };
            var call = sql
                ? row is null ? $"g.{name}()" : $"g.{name}(x => {row})"
                : row is null ? $"{Linq}.{name}(g)" : $"{Linq}.{name}(g, x => {row})";
            yield return $"{aggregate.PropertyName} = {call}";
        }
    }
}
