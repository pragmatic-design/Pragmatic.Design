using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating QueryView Build method (aggregation logic).
/// </summary>
internal sealed class QueryViewBuildTemplate : CSharpTemplate
{
    private readonly QueryViewModel _model;

    public QueryViewBuildTemplate(QueryViewModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[QueryView] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "QueryView", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");

        AppendNamespace(_model.Namespace);
        AppendLine();

        RenderPartialType();
    }

    private void RenderPartialType()
    {
        var accessibility = ParseAccessibility(_model.Accessibility);
        var mods = new ClassModifiers { Partial = true };

        if (_model.IsRecord)
        {
            Record(_model.TypeName, RenderBody,
                parameters: null,
                interfaces: null,
                accessModifier: accessibility,
                modifiers: mods);
        }
        else
        {
            Class(_model.TypeName, RenderBody,
                baseType: null,
                interfaces: null,
                accessModifier: accessibility,
                modifiers: mods);
        }
    }

    private void RenderBody()
    {
        RenderBuildMethod();
    }

    private void RenderBuildMethod()
    {
        XmlSummary("Builds the query view projection.");
        XmlParam("query", "The source query.");
        XmlReturns("The projected query view results.");

        var entityType = $"global::{_model.RootEntityTypeFullName}";
        var viewType = $"global::{_model.FullTypeName}";
        var parameters = new List<MethodParameter>
        {
            new($"IQueryable<{entityType}>", "query")
        };

        var returnType = _model.HasGroupBy
            ? $"IQueryable<{viewType}>"
            : $"IQueryable<{viewType}>";

        Method("Build", () => RenderBuildBody(entityType, viewType), returnType, parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderBuildBody(string entityType, string viewType)
    {
        if (_model.HasGroupBy)
        {
            RenderGroupByQuery(entityType, viewType);
        }
        else
        {
            RenderSimpleProjection(entityType, viewType);
        }
    }

    private void RenderGroupByQuery(string entityType, string viewType)
    {
        // Build group by key. Each key member maps to a navigation-aware access path on the entity
        // parameter `e` (e.g. `e.Status` for a root property, `e.Customer.Country` for a [GroupBy<Customer>(Via="Customer")]),
        // or to the body of a [Projectable] member, which the database can compute and its getter it cannot.
        var groupByExpressions = _model.GroupByProperties
            .Select(g => $"{GetKeyMemberName(g)} = {g.KeyExpression}")
            .ToList();

        AppendLine("return query");
        IncreaseIndent();

        // GroupBy anonymous object
        AppendLine(".GroupBy(e => new {");
        IncreaseIndent();
        for (var i = 0; i < groupByExpressions.Count; i++)
        {
            var expr = groupByExpressions[i];
            var suffix = i < groupByExpressions.Count - 1 ? "," : "";
            AppendLine($"{expr}{suffix}");
        }
        DecreaseIndent();
        AppendLine("})");

        // Select with object initializer. Each view property must be initialized at most once, so we
        // track the members already emitted: the group key is the authoritative source for a grouping
        // column (read from `g.Key`), and any aggregate/[From] property that names the same view member
        // is skipped — otherwise the same member is initialized twice (CS1912 duplicate initialization),
        // e.g. a `Status` that is both a [GroupBy] key and a [From<Order>] source property.
        var emittedMembers = new HashSet<string>(StringComparer.Ordinal);

        AppendLine($".Select(g => new {viewType}");
        AppendLine("{");
        IncreaseIndent();

        // Group by properties — only those that map to a view property are projected here;
        // navigation-only keys participate in the GROUP BY but are surfaced via [From]/source props.
        foreach (var groupBy in _model.GroupByProperties)
        {
            if (!groupBy.IsProjected)
                continue;

            if (!emittedMembers.Add(groupBy.PropertyName))
                continue;

            AppendLine($"{groupBy.PropertyName} = g.Key.{GetKeyMemberName(groupBy)},");
        }

        // Aggregate properties
        foreach (var agg in _model.AggregateProperties)
        {
            if (!emittedMembers.Add(agg.PropertyName))
                continue;

            RenderAggregateExpression(agg);
        }

        // Source properties (from first)
        foreach (var source in _model.SourceProperties)
        {
            if (!emittedMembers.Add(source.PropertyName))
                continue;

            AppendLine($"{source.PropertyName} = g.First().{source.EntityPropertyPath},");
        }

        DecreaseIndent();
        AppendLine("});");
        DecreaseIndent();
    }

    private void RenderSimpleProjection(string entityType, string viewType)
    {
        // Aggregate properties over the whole query have no meaning in a per-row projection. Leaving
        // the aggregate members at their default (0/null) would make a report silently show zeros
        // with no error, so fail loud instead: a QueryView that declares aggregates must group.
        if (_model.AggregateProperties.Any())
        {
            AppendLine("throw new global::System.NotSupportedException(");
            AppendLine($"    \"QueryView '{_model.TypeName}' declares aggregate properties without a [GroupBy]. \" +");
            AppendLine("    \"Aggregates require grouping — add a [GroupBy] property, or remove the aggregate.\");");
            return;
        }

        AppendLine($"return query.Select(e => new {viewType}");
        AppendLine("{");
        IncreaseIndent();

        // Source properties
        foreach (var source in _model.SourceProperties)
        {
            AppendLine($"{source.PropertyName} = e.{source.EntityPropertyPath},");
        }

        DecreaseIndent();
        AppendLine("});");
    }

    private void RenderAggregateExpression(AggregatePropertyModel agg)
    {
        var aggregateCall = agg.Kind switch
        {
            AggregateKind.Sum => $"g.Sum(x => {agg.RowExpression})",
            AggregateKind.Count when string.IsNullOrEmpty(agg.WhereClause) => "g.Count()",
            AggregateKind.Count => $"g.Count(x => {agg.WhereClause})",
            AggregateKind.Average => $"g.Average(x => {agg.RowExpression})",
            AggregateKind.Min => $"g.Min(x => {agg.RowExpression})",
            AggregateKind.Max => $"g.Max(x => {agg.RowExpression})",
            _ => "0"
        };

        AppendLine($"{agg.PropertyName} = {aggregateCall},");
    }

    /// <summary>
    ///     Computes the anonymous group-key member name for a grouping key.
    ///     Root-entity keys keep their bare property name (preserving existing output);
    ///     navigation-path keys are prefixed with the sanitized path so distinct paths that share
    ///     a property name (e.g. <c>e.Status</c> vs <c>e.Customer.Status</c>) never collide.
    /// </summary>
    private static string GetKeyMemberName(GroupByModel groupBy)
    {
        if (string.IsNullOrEmpty(groupBy.NavigationPath))
            return groupBy.PropertyName;

        var prefix = groupBy.NavigationPath.Replace('.', '_');
        return $"{prefix}_{groupBy.EntityProperty}";
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }
}
