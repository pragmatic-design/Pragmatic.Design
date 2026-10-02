using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating the Query Apply method.
///     Generates filtering, sorting, and paging logic from declarative properties.
/// </summary>
internal sealed partial class QueryApplyTemplate : CSharpTemplate
{
    private readonly QueryModel _model;

    public QueryApplyTemplate(QueryModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Query] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Query", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");
        AddUsing("Pragmatic.Persistence.Query");
        AddUsing("Pragmatic.Persistence.Query.Interfaces");


        AppendNamespace(_model.Namespace);
        AppendLine();

        RenderPartialType();
    }

    private void RenderPartialType()
    {
        var accessibility = ParseAccessibility(_model.Accessibility);
        var mods = new ClassModifiers { Partial = true };

        // Add interface implementation
        var interfaces = new List<string> { _model.InterfaceToImplement };

        if (_model.NeedsEagerLoading)
            interfaces.Add($"IIncludableQuery<global::{_model.EntityTypeFullName}>");

        // The executor hands over the sets a key join reads: Apply's parameter is the root's set, and
        // no navigation leads to the target.
        if (_model.GeneratesJoinedProjection)
            interfaces.Add("global::Pragmatic.Persistence.Query.Interfaces.IJoiningQuery");

        if (_model.IsRecord)
        {
            Record(_model.TypeName, RenderBody,
                parameters: null,
                interfaces: interfaces,
                accessModifier: accessibility,
                modifiers: mods);
        }
        else
        {
            Class(_model.TypeName, RenderBody,
                baseType: null,
                interfaces: interfaces,
                accessModifier: accessibility,
                modifiers: mods);
        }
    }

    private void RenderBody()
    {
        if (_model.GeneratesPaging)
        {
            RenderPagingSurface();
            AppendLine();
        }

        // One or the other, never both: IQuery's contract says the executor prefers Projection, so
        // a query offering both would be answering the same question twice.
        // ⚠️ A refused key join generates no result member at all. The declaration is already an error
        // that says what is wrong; falling back to Projection would add a CS0117 inside this file for
        // a member the author never asked for.
        if (!_model.IsSameEntityAndResult && !_model.HasRefusedKeyJoin)
        {
            // A key join reaches columns a Projection cannot carry, so it owns the step instead —
            // and it comes first, because a query that declared one and also got a Projection would
            // have the executor prefer the projection and drop every joined column in silence.
            if (_model.GeneratesJoinedProjection)
            {
                RenderJoinSourceBinding();
                AppendLine();
                RenderJoinedAggregate();
            }
            else if (_model.ResultIsAggregateView)
            {
                RenderAggregateProperty();
            }
            else if (_model.MapsInMemory)
            {
                RenderInMemoryMapper();
            }
            else
            {
                RenderProjectionProperty();
            }

            AppendLine();
        }

        if (_model.NeedsEagerLoading)
        {
            RenderIncludePaths();
            AppendLine();
        }

        // Generate Apply method
        RenderApplyMethod();

        // Generate ToSpecification method (if Pragmatic.Specification is available)
        AppendLine();
        RenderToSpecificationMethod();
    }

    /// <summary>
    ///     The two properties every paged query repeats, written here instead of by the author.
    /// </summary>
    /// <remarks>
    ///     The defaults are the ones the hand-written queries in this repository already use: page 1,
    ///     twenty rows. They are part of the surface — a paged query with no default page size answers
    ///     zero rows to a caller who sends neither.
    /// </remarks>
    private void RenderPagingSurface()
    {
        XmlSummary("The page to return, 1-based.");
        AppendLine("public int Page { get; init; } = 1;");
        AppendLine();
        XmlSummary("How many rows the page holds.");
        AppendLine("public int PageSize { get; init; } = 20;");
    }

    /// <summary>
    ///     The mapper the executor runs after the rows arrive, for a result type that does not
    ///     project into SQL.
    /// </summary>
    /// <remarks>
    ///     <c>Selector</c> is what <c>[MapFrom&lt;T&gt;]</c> generates on its own — a <c>Func</c>,
    ///     not an <c>Expression</c> — so this names a member that exists precisely when the author's
    ///     declaration is honest.
    /// </remarks>
    private void RenderInMemoryMapper()
    {
        var entityType = $"global::{_model.EntityTypeFullName}";
        var resultType = $"global::{_model.ResultTypeFullName}";

        XmlSummary("Maps each row after it arrives; this query does not project into SQL.");
        AppendLine($"public System.Func<{entityType}, {resultType}>? MapEach => {resultType}.Selector;");
    }

    /// <summary>
    ///     The whole step, for a read whose rows are not rows of the entity.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A grouping cannot be a <c>Projection</c>: that is one entity in and one result out. The
    ///     view's generated <c>Build</c> already has the right shape — filtered set in, projected set
    ///     out — so the query names it rather than restating a grouping that is declared next to the
    ///     aggregates it feeds.
    ///     <para>
    ///         Emitting <c>Projection =&gt; TView.Projection</c> instead would name a member a
    ///         <c>[QueryView]</c> never generates: the read could be declared and the file it produced
    ///         would not compile.
    ///     </para>
    /// </remarks>
    private void RenderAggregateProperty()
    {
        var entityType = $"global::{_model.EntityTypeFullName}";
        var resultType = $"global::{_model.ResultTypeFullName}";

        XmlSummary("Groups the filtered set; the view declares the grouping and the aggregates.");
        AppendLine(
            $"public System.Func<IQueryable<{entityType}>, IQueryable<{resultType}>>? Aggregate "
            + $"=> {resultType}.Build;");
    }

    private void RenderProjectionProperty()
    {
        var entityType = $"global::{_model.EntityTypeFullName}";
        var resultType = $"global::{_model.ResultTypeFullName}";

        XmlSummary("Gets the projection expression from the result type.");
        AppendLine(
            $"public System.Linq.Expressions.Expression<System.Func<{entityType}, {resultType}>>? Projection => {resultType}.Projection;");
    }

    /// <summary>
    ///     The navigations this query has to bring back with the entity.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>IIncludableQuery</c> and the executor's <c>Include</c> loop are the eager-loading path,
    ///         and a generated query is its producer: without this, a query answering with the entity
    ///         would come back with every navigation empty, and the caller that walks one would get a
    ///         null.
    ///     </para>
    ///     <para>
    ///         The declared paths come first because the author wrote them; the response DTO's own
    ///         requirements are appended from <c>RequiredNavigations</c>, which Mapping already works
    ///         out from explicit paths, flattening, nested DTOs and collections. Deriving them a second
    ///         time here would be a poorer copy of a rule that exists.
    ///     </para>
    /// </remarks>
    private void RenderIncludePaths()
    {
        XmlSummary("Navigation paths loaded with the entity.");

        // Three sources, in the order the author would expect to read them: the paths written on the
        // query, then "every navigation to depth N" from [LoadWith], then what the response DTO needs.
        // Spread rather than concatenated so a duplicate is a repeated Include, which EF collapses.
        var parts = new List<string>();

        if (_model.EagerLoadPaths.Length > 0)
            parts.AddRange(_model.EagerLoadPaths.Select(p => $"\"{p}\""));

        // A [Join<T>(Via = …)] whose path resolves says the same thing as an [EagerLoad], so it arrives
        // in the same list rather than as a second Include inside Apply. One that does not
        // resolve is PRAG0737 and contributes nothing: the name the author got wrong never reaches a
        // generated file.
        parts.AddRange(_model.Joins
            .Where(j => j.IsResolvedNavigationJoin)
            .Select(j => $"\"{j.Via}\""));

        // Already fully qualified by the transform — prefixing produced "global::global::".
        if (_model.LoadingProfileFullTypeName is { } profile)
            parts.Add($".. {profile}.IncludePaths");

        if (_model.ResponseDtoFullTypeName is { } dto)
            parts.Add($".. {dto}.RequiredNavigations");

        AppendLine(
            "public System.Collections.Generic.IReadOnlyList<string> IncludePaths => "
            + $"[{string.Join(", ", parts)}];");
    }

    private void RenderApplyMethod()
    {
        XmlSummary("Applies filters, sorting, and paging to the query.");
        XmlParam("query", "The source query.");
        XmlReturns("The transformed query.");

        var entityParam = $"global::{_model.EntityTypeFullName}";
        var parameters = new List<MethodParameter>
        {
            new($"IQueryable<{entityParam}>", "query")
        };

        var returnType = _model.HasPaging
            ? $"IQueryable<{entityParam}>"
            : $"IQueryable<{entityParam}>";

        Method("Apply", RenderApplyBody, returnType, parameters);
    }

    private void RenderApplyBody()
    {
        // Call base.Apply() if inheriting from another query
        if (_model.HasBaseQuery)
        {
            AppendLine("query = base.Apply(query);");
            AppendLine();
        }

        // ⚠️ Nothing from [Join] is applied here, and both halves of that are deliberate. A resolved
        // Via is an include path, which the executor applies before Apply runs; a key join
        // is the whole step and generates Aggregate.

        // Generate filter conditions
        if (_model.HasFilters)
        {
            Comment("Apply filters");
            foreach (var prop in _model.FilterProperties)
            {
                RenderFilterCondition(prop);
            }

            AppendLine();
        }

        // Apply complex filter groups (JSON-deserialized FilterDto objects)
        if (_model.HasComplexFilters)
        {
            Comment("Apply complex filter groups (JSON-deserialized FilterDto objects)");
            foreach (var cf in _model.ComplexFilters)
                AppendLine($"query = global::{cf.PropertyTypeFullName}Extensions.ApplyFilter(query, this.{cf.PropertyName});");
            AppendLine();
        }

        // Apply the specifications this query composes
        if (_model.HasSpecifications)
        {
            Comment("Apply specifications (rules named once and reused here)");
            foreach (var spec in _model.Specifications)
            {
                // ANDed like every other contribution: each one is its own Where. An alternative goes
                // inside a single property — a | b — where the reader can see it.
                var access = spec.AccessFrom(_model.TypeName);
                if (spec.IsNullable)
                {
                    If($"{access} is not null", () =>
                        AppendLine($"query = Pragmatic.Specification.SpecificationExtensions.Where(query, {access});"));
                }
                else
                {
                    AppendLine($"query = Pragmatic.Specification.SpecificationExtensions.Where(query, {access});");
                }
            }

            AppendLine();
        }

        // What the grid asked for, through the bridge the entity generates. Filters, sorts and paging
        // all at once: the request carries the three together and the bridge applies them as one, which
        // is what makes the rows identical to the hand-written form this replaces.
        if (_model.HasGridRequests)
        {
            Comment("Apply the canonical grid request (fields, sorts and page, through the entity's bridge)");
            foreach (var grid in _model.GridRequests)
            {
                var access = $"this.{grid.PropertyName}";
                var call = $"query = global::{_model.GridBridgeTypeName}.ApplyCanonical(query, {access});";

                if (grid.IsNullable)
                    If($"{access} is not null", () => AppendLine(call));
                else
                    AppendLine(call);
            }

            AppendLine();
        }

        // Generate sorting
        if (_model.HasSorting)
        {
            Comment("Apply sorting");
            RenderSortingLogic();
            AppendLine();
        }

        // Paging is NOT applied here — IPagedQuery exposes Page/PageSize/Skip/Take,
        // and the IQueryExecutor handles paging separately to get correct total count.

        Return("query");
    }

    private void RenderFilterCondition(QueryPropertyModel prop)
    {
        var entityPath = $"e.{prop.EffectivePropertyPath}";
        var propAccess = $"this.{prop.PropertyName}";

        // Collection-of-scalars filter (In): skip when the collection is null OR empty, otherwise
        // an empty collection would filter out every row. Applies regardless of required-ness.
        if (prop.IsCollection)
        {
            If($"{propAccess} != null && System.Linq.Enumerable.Any({propAccess})", () =>
            {
                var condition = GenerateFilterExpression(entityPath, propAccess, prop);
                AppendLine($"query = query.Where(e => {condition});");
            });
            return;
        }

        if (prop.IsAlwaysApplied)
        {
            // Required property: always apply
            var condition = GenerateFilterExpression(entityPath, propAccess, prop);
            AppendLine($"query = query.Where(e => {condition});");
        }
        else
        {
            // Optional property: apply only if not null
            var nullCheck = prop.PropertyType.EndsWith("?")
                ? $"{propAccess} is not null"
                : $"{propAccess} != default";

            If(nullCheck, () =>
            {
                var valueAccess = prop.PropertyType.EndsWith("?") && !IsReferenceType(prop.PropertyType)
                    ? $"{propAccess}.Value"
                    : propAccess;

                // For nullable reference types, we need to handle the null-check differently
                if (IsReferenceType(prop.PropertyType) && prop.PropertyType.EndsWith("?"))
                    valueAccess = $"{propAccess}!";

                var condition = GenerateFilterExpression(entityPath, valueAccess, prop);
                AppendLine($"query = query.Where(e => {condition});");
            });
        }
    }

    /// <summary>
    ///     What a join still writes into <c>Apply</c>: nothing, unless it is a key join.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A navigation join emits no `query.Include(e => e.X)` here, which would be wrong
    ///         twice. It would be a second copy of <c>IncludePaths</c>, which every generated
    ///         query already carries and which <c>EfCoreQueryExecutor.PrepareSource</c> applies to the
    ///         queryable <b>before</b> <c>Apply</c> runs — a channel the executor knows about, where
    ///         this one is not. And EF Core drops an <c>Include</c> as soon as the query no longer
    ///         returns instances of the entity, which is what a projecting query does: on the normal
    ///         case it could not change an answer at all.
    ///     </para>
    ///     <para>
    ///         The path goes to <c>IncludePaths</c> — see <see cref="RenderIncludePaths" /> — after
    ///         being resolved against the entity by the transform.
    ///     </para>
    /// </remarks>
    private static string GenerateFilterExpression(string entityPath, string value, QueryPropertyModel prop)
    {
        // A search matches its columns, not the one named after the property: the entity has no Search.
        if (prop.SearchAcrossPaths.Length > 0)
            return SearchAcrossCondition.Render(prop.SearchAcrossPaths, value, prop.SearchIgnoresCase);

        // Lower both sides, the same shape FilterDtoApplyTemplate uses. The null guard below still keys
        // off the bare path: ToLower() on a null column would throw before the comparison runs.
        var expression = prop.IgnoreCase
            ? GenerateFilterExpression($"{entityPath}.ToLower()", $"{value}.ToLower()", prop.Operator)
            : GenerateFilterExpression(entityPath, value, prop.Operator);

        // A string operator dereferences the column. On a nullable column that is fine once the provider
        // translates it to SQL, but throws as soon as the same expression runs over objects (in-memory
        // executor, unit tests) — and it does not compile clean under a nullable context either.
        // IgnoreCase dereferences it too, through ToLower(), and does so for every operator — including
        // Equals, which on its own needs no guard.
        var dereferencesColumn = prop.IgnoreCase || prop.Operator
            is FilterOperatorKind.Contains or FilterOperatorKind.StartsWith or FilterOperatorKind.EndsWith;

        return prop.EntityPathIsNullable && dereferencesColumn
            ? $"{entityPath} != null && {expression}"
            : expression;
    }

    private static string GenerateFilterExpression(string entityPath, string value, FilterOperatorKind op)
    {
        return op switch
        {
            FilterOperatorKind.Equals => $"{entityPath} == {value}",
            FilterOperatorKind.NotEquals => $"{entityPath} != {value}",
            FilterOperatorKind.Contains => $"{entityPath}.Contains({value})",
            FilterOperatorKind.StartsWith => $"{entityPath}.StartsWith({value})",
            FilterOperatorKind.EndsWith => $"{entityPath}.EndsWith({value})",
            FilterOperatorKind.GreaterThan => $"{entityPath} > {value}",
            FilterOperatorKind.GreaterOrEqual => $"{entityPath} >= {value}",
            FilterOperatorKind.LessThan => $"{entityPath} < {value}",
            FilterOperatorKind.LessOrEqual => $"{entityPath} <= {value}",
            FilterOperatorKind.In => $"{value}.Contains({entityPath})",
            _ => $"{entityPath} == {value}"
        };
    }

    private void RenderSortingLogic()
    {
        var sortProps = _model.SortProperties.ToList();
        if (sortProps.Count == 0)
            return;

        AppendLine("var isFirstSort = true;");

        foreach (var prop in sortProps)
        {
            var entityPath = prop.EffectivePropertyPath;
            var propAccess = $"this.{prop.PropertyName}";

            // Check if sort property has a value or has a default
            var hasDefault = prop.DefaultSortDirection.HasValue;
            var defaultDir = prop.DefaultSortDirection == SortDirectionKind.Descending
                ? "SortDirection.Descending"
                : "SortDirection.Ascending";

            if (hasDefault)
            {
                // Always apply with default fallback
                AppendLine($"var {ToCamelCase(prop.PropertyName)}Dir = {propAccess} ?? {defaultDir};");
                RenderSortApplication(entityPath, $"{ToCamelCase(prop.PropertyName)}Dir");
            }
            else
            {
                // Only apply if specified
                If($"{propAccess} is not null", () =>
                {
                    RenderSortApplication(entityPath, $"{propAccess}.Value");
                });
            }
        }
    }

    private void RenderSortApplication(string entityPath, string directionExpr)
    {
        If("isFirstSort", () =>
        {
            If($"{directionExpr} == SortDirection.Ascending", () =>
            {
                AppendLine($"query = query.OrderBy(e => e.{entityPath});");
            });
            Else(() => { AppendLine($"query = query.OrderByDescending(e => e.{entityPath});"); });
            AppendLine("isFirstSort = false;");
        });
        Else(() =>
        {
            If($"{directionExpr} == SortDirection.Ascending", () =>
            {
                AppendLine($"query = ((IOrderedQueryable<global::{_model.EntityTypeFullName}>)query).ThenBy(e => e.{entityPath});");
            });
            Else(() =>
            {
                AppendLine(
                    $"query = ((IOrderedQueryable<global::{_model.EntityTypeFullName}>)query).ThenByDescending(e => e.{entityPath});");
            });
        });
    }

    private void RenderToSpecificationMethod()
    {
        XmlSummary("Converts this query to a Specification for use with repositories.");
        XmlReturns("A Specification representing the filters in this query.");

        var entityParam = $"global::{_model.EntityTypeFullName}";

        Method("ToSpecification", RenderToSpecificationBody, $"Pragmatic.Specification.Specification<{entityParam}>");
    }

    private void RenderToSpecificationBody()
    {
        if (!_model.HasFilters && !_model.HasSpecifications)
        {
            Return($"Pragmatic.Specification.Spec<global::{_model.EntityTypeFullName}>.True");
            return;
        }

        AppendLine($"var spec = Pragmatic.Specification.Spec<global::{_model.EntityTypeFullName}>.True;");
        AppendLine();

        foreach (var prop in _model.FilterProperties)
        {
            RenderSpecificationCondition(prop);
        }

        // The same specifications Apply() composes. Left out here, the one query would answer
        // differently through the runner and through the repository — and only one of the two would be
        // the query the author wrote.
        foreach (var contribution in _model.Specifications)
        {
            var access = contribution.AccessFrom(_model.TypeName);
            if (contribution.IsNullable)
                If($"{access} is not null", () => AppendLine($"spec = spec & {access};"));
            else
                AppendLine($"spec = spec & {access};");
        }

        AppendLine();
        Return("spec");
    }

    private void RenderSpecificationCondition(QueryPropertyModel prop)
    {
        var entityPath = $"e.{prop.EffectivePropertyPath}";
        var propAccess = $"this.{prop.PropertyName}";

        // Collection-of-scalars filter (In): skip when null OR empty.
        if (prop.IsCollection)
        {
            If($"{propAccess} != null && System.Linq.Enumerable.Any({propAccess})", () =>
            {
                var condition = GenerateFilterExpression(entityPath, propAccess, prop);
                AppendLine($"spec = spec & Pragmatic.Specification.Spec<global::{_model.EntityTypeFullName}>.Where(e => {condition});");
            });
            return;
        }

        if (prop.IsAlwaysApplied)
        {
            // Required property: always apply
            var condition = GenerateFilterExpression(entityPath, propAccess, prop);
            AppendLine($"spec = spec & Pragmatic.Specification.Spec<global::{_model.EntityTypeFullName}>.Where(e => {condition});");
        }
        else
        {
            // Optional property: apply only if not null
            var nullCheck = prop.PropertyType.EndsWith("?")
                ? $"{propAccess} is not null"
                : $"{propAccess} != default";

            If(nullCheck, () =>
            {
                var valueAccess = prop.PropertyType.EndsWith("?") && !IsReferenceType(prop.PropertyType)
                    ? $"{propAccess}.Value"
                    : propAccess;

                if (IsReferenceType(prop.PropertyType) && prop.PropertyType.EndsWith("?"))
                    valueAccess = $"{propAccess}!";

                var condition = GenerateFilterExpression(entityPath, valueAccess, prop);
                AppendLine($"spec = spec & Pragmatic.Specification.Spec<global::{_model.EntityTypeFullName}>.Where(e => {condition});");
            });
        }
    }

    private static readonly HashSet<string> KnownValueTypes =
    [
        "int", "Int32", "System.Int32",
        "long", "Int64", "System.Int64",
        "short", "Int16", "System.Int16",
        "byte", "Byte", "System.Byte",
        "bool", "Boolean", "System.Boolean",
        "decimal", "Decimal", "System.Decimal",
        "double", "Double", "System.Double",
        "float", "Single", "System.Single",
        "DateTime", "System.DateTime",
        "DateTimeOffset", "System.DateTimeOffset",
        "DateOnly", "System.DateOnly",
        "TimeOnly", "System.TimeOnly",
        "TimeSpan", "System.TimeSpan",
        "Guid", "System.Guid"
    ];

    private static bool IsReferenceType(string typeName)
    {
        var baseType = typeName.TrimEnd('?');
        return !KnownValueTypes.Contains(baseType);
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
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
