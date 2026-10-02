using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates a compile-time bridge from canonical GridFilterRequest → typed IQueryable operations.
///     Each entity field gets a switch/case with type-appropriate filter operators.
/// </summary>
internal sealed class GridFilterBridgeTemplate : CSharpTemplate
{
    private readonly GridFilterBridgeModel _model;

    public GridFilterBridgeTemplate(GridFilterBridgeModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.EntityTypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[GenerateGridBridge] on {_model.EntityTypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType($"{_model.EntityTypeName}GridFilterBridge", "GridBridge", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");
        AddUsing("Pragmatic.Persistence.Query");
        AddUsing("Pragmatic.Persistence.Query.Adapters");

        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary(
            $"Compile-time bridge that converts canonical <see cref=\"GridFilterRequest\"/> to typed <c>IQueryable&lt;{_model.EntityTypeName}&gt;</c> operations.");

        Class($"{_model.EntityTypeName}GridFilterBridge", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        RenderDeclaration();

        AppendLine();
        RenderApplyCanonicalMethod();

        AppendLine();
        RenderPredicateForMethod();

        // Per-field typed predicate helpers
        foreach (var prop in _model.Properties)
        {
            AppendLine();
            RenderFieldFilterHelper(prop);
        }
    }

    /// <summary>
    ///     Hands the entity's filterable fields to <c>GridFieldRegistry</c>, so the runtime adapters
    ///     answer on the same list this bridge switches on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A <c>[ModuleInitializer]</c> because the adapters are static and the entity may live in a
    ///         referenced module: the declaration has to be there before anything can be asked of it,
    ///         and there is no host call in between.
    ///     </para>
    ///     <para>
    ///         ⚠️ Without it the two halves disagreed: this bridge named only the properties carrying
    ///         <c>[Filterable]</c>, while <c>AdapterFieldPolicy</c> resolved any public scalar not on a
    ///         denylist. The client supplies the field name on that path, so the wider half was the one
    ///         a caller could reach.
    ///     </para>
    /// </remarks>
    private void RenderDeclaration()
    {
        var entityType = $"global::{_model.EntityFullTypeName}";
        var names = string.Join(", ", _model.Properties.Select(p => $"\"{p.Name}\""));

        XmlSummary("Publishes the fields this entity offers to a grid, for the runtime adapters.");
        AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        Method("DeclareGridFields", () =>
            AppendLine($"GridFieldRegistry.Declare<{entityType}>(new[] {{ {names} }});"),
            "void", [],
            accessModifier: AccessModifier.Internal,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderApplyCanonicalMethod()
    {
        XmlSummary("Applies a canonical grid filter request to the query.");
        XmlParam("query", "The source query.");
        XmlParam("request", "The canonical grid filter request.");
        XmlReturns("The transformed query with filters, sorts, and paging applied.");

        var entityParam = $"global::{_model.EntityFullTypeName}";
        var parameters = new List<MethodParameter>
        {
            new($"this IQueryable<{entityParam}>", "query"),
            new("GridFilterRequest", "request")
        };

        Method("ApplyCanonical", RenderApplyCanonicalBody, $"IQueryable<{entityParam}>", parameters,
            accessModifier: AccessModifier.Public,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderApplyCanonicalBody()
    {
        var entityParam = $"global::{_model.EntityFullTypeName}";

        Comment("Filters, joined by the connector each clause declares (Logic applies to the NEXT clause)");
        AppendLine($"global::System.Linq.Expressions.Expression<global::System.Func<{entityParam}, bool>>? combined = null;");
        AppendLine("var pending = FilterLogic.And;");
        AppendLine();
        AppendLine("foreach (var filter in request.Filters)");
        Block(() =>
        {
            AppendLine("var predicate = PredicateFor(filter);");
            AppendLine("if (predicate is null)");
            Block(() => AppendLine("continue;"));
            AppendLine();
            AppendLine("combined = combined is null");
            AppendLine("    ? predicate");
            AppendLine("    : pending == FilterLogic.Or");
            AppendLine("        ? global::Pragmatic.Persistence.Query.Adapters.GridPredicate.Or(combined, predicate)");
            AppendLine("        : global::Pragmatic.Persistence.Query.Adapters.GridPredicate.And(combined, predicate);");
            AppendLine();
            AppendLine("pending = filter.Logic;");
        });
        AppendLine();
        AppendLine("if (combined is not null)");
        Block(() => AppendLine("query = query.Where(combined);"));

        AppendLine();

        // Sorts
        Comment("Apply sorts (multi-sort)");
        AppendLine($"IOrderedQueryable<{entityParam}>? ordered = null;");
        AppendLine("foreach (var sort in request.Sorts)");
        Block(() =>
        {
            Switch("sort.Field", () =>
            {
                foreach (var prop in _model.Properties)
                {
                    Case($"\"{prop.Name}\"", () =>
                    {
                        AppendLine("if (sort.Direction == SortDirection.Ascending)");
                        Block(() =>
                        {
                            AppendLine(
                                $"ordered = ordered is not null ? ordered.ThenBy(e => e.{prop.Name}) : query.OrderBy(e => e.{prop.Name});");
                        });
                        AppendLine("else");
                        Block(() =>
                        {
                            AppendLine(
                                $"ordered = ordered is not null ? ordered.ThenByDescending(e => e.{prop.Name}) : query.OrderByDescending(e => e.{prop.Name});");
                        });
                        Break();
                    });
                }
            });
        });
        AppendLine("if (ordered is not null) query = ordered;");

        AppendLine();

        // Paging
        Comment("Apply paging");
        AppendLine("if (request.Page is > 0 && request.PageSize is > 0)");
        Block(() =>
        {
            AppendLine("query = query.Skip((request.Page.Value - 1) * request.PageSize.Value).Take(request.PageSize.Value);");
        });

        AppendLine();
        Return("query");
    }

    /// <summary>
    ///     Renders the switch that turns one clause into a typed predicate, or refuses the field.
    /// </summary>
    private void RenderPredicateForMethod()
    {
        var entityParam = $"global::{_model.EntityFullTypeName}";
        var returnType =
            $"global::System.Linq.Expressions.Expression<global::System.Func<{entityParam}, bool>>?";

        XmlSummary("The predicate one clause stands for, or null when the clause carries no usable value.");
        XmlParam("filter", "The clause as the grid sent it.");

        var parameters = new List<MethodParameter> { new("FilterClause", "filter") };

        Method("PredicateFor", () =>
        {
            Switch("filter.Field", () =>
            {
                foreach (var prop in _model.Properties)
                {
                    Case($"\"{prop.Name}\"", () => AppendLine($"return {prop.Name}Predicate(filter);"));
                }

                // A property the entity has and the bridge withholds. Named separately from the unknown
                // ones so a caller can tell "you may not filter on that" from "you spelled it wrong",
                // and neither falls through the switch leaving the query silently unchanged.
                foreach (var withheld in _model.WithheldProperties)
                {
                    Case($"\"{withheld}\"", () => AppendLine(
                        "throw new global::Pragmatic.Persistence.Query.Adapters.GridFieldRejectedException(" +
                        $"filter.Field, global::Pragmatic.Persistence.Query.Adapters.GridFieldRejection.Withheld);"));
                }

                Default(() => AppendLine(
                    "throw new global::Pragmatic.Persistence.Query.Adapters.GridFieldRejectedException(" +
                    "filter.Field, global::Pragmatic.Persistence.Query.Adapters.GridFieldRejection.Unknown);"));
            });
        },
        returnType,
        parameters,
        accessModifier: AccessModifier.Private,
        modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderFieldFilterHelper(GridFilterBridgePropertyModel prop)
    {
        var entityParam = $"global::{_model.EntityFullTypeName}";
        var parameters = new List<MethodParameter> { new("FilterClause", "filter") };

        Method($"{prop.Name}Predicate", () => RenderFieldFilterBody(prop),
            $"global::System.Linq.Expressions.Expression<global::System.Func<{entityParam}, bool>>?",
            parameters,
            accessModifier: AccessModifier.Private,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderFieldFilterBody(GridFilterBridgePropertyModel prop)
    {
        var entityPath = $"e.{prop.Name}";

        if (prop.IsString)
        {
            RenderStringFilterBody(entityPath, prop.TypeName);
        }
        else if (prop.IsBool || prop.IsEnum)
        {
            RenderEqualityOnlyFilterBody(entityPath, prop.TypeName);
        }
        else if (prop.IsComparable)
        {
            RenderComparableFilterBody(entityPath, prop.TypeName);
        }
        else
        {
            RenderEqualityOnlyFilterBody(entityPath, prop.TypeName);
        }
    }

    /// <summary>
    ///     Reads the clause value as the field's own type, once, into a local.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not a direct cast — <c>(string)filter.Value!</c>. <c>FilterClause.Value</c> is an
    ///     <c>object?</c>, so a request that arrives as JSON puts a <c>JsonElement</c> there and a cast
    ///     would throw inside the LINQ expression: the canonical type would not survive its own round
    ///     trip, and posting one would answer 500. Reading it into a local also lets EF parameterise the
    ///     value instead of embedding it in the SQL.
    /// </remarks>
    private void RenderValueLocal(string typeName)
    {
        AppendLine($"var value = global::Pragmatic.Persistence.Query.Adapters.GridValue.As<{typeName}?>(filter.Value);");
        AppendLine("if (value is null)");
        Block(() => AppendLine("return null;"));
        AppendLine();
    }

    private void RenderStringFilterBody(string entityPath, string typeName)
    {
        RenderValueLocal("string");
        AppendLine("return filter.Operator switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"FilterOperator.Contains => e => {entityPath}.Contains(value),");
        AppendLine($"FilterOperator.StartsWith => e => {entityPath}.StartsWith(value),");
        AppendLine($"FilterOperator.EndsWith => e => {entityPath}.EndsWith(value),");
        AppendLine($"FilterOperator.Equals => e => {entityPath} == value,");
        AppendLine($"FilterOperator.NotEquals => e => {entityPath} != value,");
        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderEqualityOnlyFilterBody(string entityPath, string typeName)
    {
        RenderValueLocal(typeName);
        AppendLine("return filter.Operator switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"FilterOperator.Equals => e => {entityPath} == value,");
        AppendLine($"FilterOperator.NotEquals => e => {entityPath} != value,");
        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderComparableFilterBody(string entityPath, string typeName)
    {
        RenderValueLocal(typeName);
        AppendLine("return filter.Operator switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"FilterOperator.Equals => e => {entityPath} == value,");
        AppendLine($"FilterOperator.NotEquals => e => {entityPath} != value,");
        AppendLine($"FilterOperator.GreaterThan => e => {entityPath} > value,");
        AppendLine($"FilterOperator.GreaterOrEqual => e => {entityPath} >= value,");
        AppendLine($"FilterOperator.LessThan => e => {entityPath} < value,");
        AppendLine($"FilterOperator.LessOrEqual => e => {entityPath} <= value,");
        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }
}
