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

        AppendLine();
        RenderRefusalHelpers();

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
            AppendLine("// A predicate or a refusal: a clause left out answers with more rows than the client asked for.");
            AppendLine("var predicate = PredicateFor(filter);");
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
            $"global::System.Linq.Expressions.Expression<global::System.Func<{entityParam}, bool>>";

        XmlSummary("The predicate one clause stands for; a clause it cannot stand for is refused.");
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
            $"global::System.Linq.Expressions.Expression<global::System.Func<{entityParam}, bool>>",
            parameters,
            accessModifier: AccessModifier.Private,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     A clause on one field: a predicate, or a refusal (#325).
    /// </summary>
    /// <remarks>
    ///     A filter restricts, so a clause left out answers with more rows than the client asked for: the body returned
    ///     null for a value it could not read and for an operator the column's type does not support, and
    ///     <c>ApplyCanonical</c> skipped it. In the order the grid adapters read a clause: <c>null</c>, then the lists
    ///     of <c>In</c> and <c>Between</c>, then the one value.
    /// </remarks>
    private void RenderFieldFilterBody(GridFilterBridgePropertyModel prop)
    {
        var entityPath = $"e.{prop.Name}";
        var typeName = prop.IsString ? "string" : prop.TypeName;

        var orders = prop.IsComparable && !prop.IsString && !prop.IsBool && !prop.IsEnum;
        var arms = new List<(string Operator, string Predicate)>();
        if (prop.IsString)
        {
            arms.Add(("Contains", $"e => {entityPath}.Contains(value)"));
            arms.Add(("StartsWith", $"e => {entityPath}.StartsWith(value)"));
            arms.Add(("EndsWith", $"e => {entityPath}.EndsWith(value)"));
        }
        arms.Add(("Equals", $"e => {entityPath} == value"));
        arms.Add(("NotEquals", $"e => {entityPath} != value"));
        if (orders)
        {
            arms.Add(("GreaterThan", $"e => {entityPath} > value"));
            arms.Add(("GreaterOrEqual", $"e => {entityPath} >= value"));
            arms.Add(("LessThan", $"e => {entityPath} < value"));
            arms.Add(("LessOrEqual", $"e => {entityPath} <= value"));
        }

        RenderNullClause(prop, entityPath);
        RenderInClause(entityPath, typeName);
        if (orders)
            RenderBetweenClause(entityPath, typeName);

        // The operator before the value, as the grid adapters read a clause: a Between on a text column is an
        // operator the column does not support, whatever its value holds.
        var supported = string.Join(" or ", arms.Select(a => $"FilterOperator.{a.Operator}"));
        AppendLine($"if (filter.Operator is not ({supported}))");
        IncreaseIndent();
        AppendLine("throw Unsupported(filter);");
        DecreaseIndent();
        AppendLine();
        RenderValueLocal(typeName);

        AppendLine("return filter.Operator switch");
        AppendLine("{");
        IncreaseIndent();
        foreach (var (op, predicate) in arms)
            AppendLine($"FilterOperator.{op} => {predicate},");
        AppendLine("_ => throw Unsupported(filter),");
        DecreaseIndent();
        AppendLine("};");
    }

    /// <summary>
    ///     A clause with no value: <c>FilterClause.Value</c> "may be null for is-null checks", and equality and inequality
    ///     against null are comparisons on a column that can hold it. Anything else against null is a value the column
    ///     cannot be compared with.
    /// </summary>
    private void RenderNullClause(GridFilterBridgePropertyModel prop, string entityPath)
    {
        AppendLine("if (global::Pragmatic.Persistence.Query.Adapters.GridValue.IsNull(filter.Value))");
        if (!prop.IsString && !prop.IsNullable)
        {
            IncreaseIndent();
            AppendLine("throw Unusable(filter);");
            DecreaseIndent();
            AppendLine();
            return;
        }

        IncreaseIndent();
        AppendLine("return filter.Operator switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"FilterOperator.Equals => e => {entityPath} == null,");
        AppendLine($"FilterOperator.NotEquals => e => {entityPath} != null,");
        AppendLine("_ => throw Unusable(filter),");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
        AppendLine();
    }

    /// <summary>An <c>In</c>: one equality per value, each read as the field's type, joined by OR.</summary>
    private void RenderInClause(string entityPath, string typeName)
    {
        var entityParam = $"global::{_model.EntityFullTypeName}";

        AppendLine("if (filter.Operator == FilterOperator.In)");
        Block(() =>
        {
            AppendLine("if (global::Pragmatic.Persistence.Query.Adapters.GridValue.Elements(filter.Value) is not { Count: > 0 } values)");
            IncreaseIndent();
            AppendLine("throw Unusable(filter);");
            DecreaseIndent();
            AppendLine();
            AppendLine($"global::System.Linq.Expressions.Expression<global::System.Func<{entityParam}, bool>>? any = null;");
            AppendLine("foreach (var item in values)");
            Block(() =>
            {
                AppendLine($"if (!global::Pragmatic.Persistence.Query.Adapters.GridValue.TryAs<{typeName}?>(item, out var element) || element is null)");
                IncreaseIndent();
                AppendLine("throw Unusable(filter);");
                DecreaseIndent();
                AppendLine();
                AppendLine($"global::System.Linq.Expressions.Expression<global::System.Func<{entityParam}, bool>> equal = e => {entityPath} == element;");
                AppendLine("any = any is null ? equal : global::Pragmatic.Persistence.Query.Adapters.GridPredicate.Or(any, equal);");
            });
            AppendLine();
            AppendLine("return any!;");
        });
        AppendLine();
    }

    /// <summary>A <c>Between</c>: <c>[min, max]</c>, both ends included, on a field whose type orders.</summary>
    private void RenderBetweenClause(string entityPath, string typeName)
    {
        AppendLine("if (filter.Operator == FilterOperator.Between)");
        Block(() =>
        {
            AppendLine("if (global::Pragmatic.Persistence.Query.Adapters.GridValue.Elements(filter.Value) is not { Count: 2 } bounds");
            AppendLine($"    || !global::Pragmatic.Persistence.Query.Adapters.GridValue.TryAs<{typeName}?>(bounds[0], out var min) || min is null");
            AppendLine($"    || !global::Pragmatic.Persistence.Query.Adapters.GridValue.TryAs<{typeName}?>(bounds[1], out var max) || max is null)");
            IncreaseIndent();
            AppendLine("throw Unusable(filter);");
            DecreaseIndent();
            AppendLine();
            AppendLine($"return e => {entityPath} >= min && {entityPath} <= max;");
        });
        AppendLine();
    }

    /// <summary>
    ///     Reads the clause value as the field's own type, once, into a local, or refuses one that is not.
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
        AppendLine($"if (!global::Pragmatic.Persistence.Query.Adapters.GridValue.TryAs<{typeName}?>(filter.Value, out var value) || value is null)");
        IncreaseIndent();
        AppendLine("throw Unusable(filter);");
        DecreaseIndent();
        AppendLine();
    }

    /// <summary>The refusals of a clause on a field the bridge does filter on, as the grid adapters throw them.</summary>
    private void RenderRefusalHelpers()
    {
        const string adapters = "global::Pragmatic.Persistence.Query.Adapters";
        AppendLine($"private static global::System.Exception Unusable(FilterClause filter) => new {adapters}.GridFilterRejectedException(filter.Field, {adapters}.GridFilterRejection.UnusableValue);");
        AppendLine();
        AppendLine($"private static global::System.Exception Unsupported(FilterClause filter) => new {adapters}.GridFilterRejectedException(filter.Field, {adapters}.GridFilterRejection.UnsupportedOperator, filter.Operator.ToString());");
    }
}
