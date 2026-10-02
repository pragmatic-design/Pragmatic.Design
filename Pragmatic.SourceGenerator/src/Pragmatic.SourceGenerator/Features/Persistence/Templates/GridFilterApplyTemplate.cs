using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating the GridFilter Apply method.
///     Generates filtering, sorting, and paging logic with dynamic operator support.
/// </summary>
internal sealed class GridFilterApplyTemplate : CSharpTemplate
{
    private readonly GridFilterModel _model;

    public GridFilterApplyTemplate(GridFilterModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[GridFilter] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "GridFilter", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");
        AddUsing("Pragmatic.Persistence.Query");

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
        RenderApplyMethod();

        if (_model.HasFilterable || _model.FilterGroups.Length > 0)
        {
            AppendLine();
            RenderToSpecificationMethod();
        }
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

        Method("Apply", RenderApplyBody, $"IQueryable<{entityParam}>", parameters);
    }

    private void RenderApplyBody()
    {
        // Generate filter conditions
        if (_model.HasFilterable)
        {
            Comment("Apply filters");
            foreach (var prop in _model.FilterableProperties)
            {
                RenderFilterCondition(prop);
            }

            AppendLine();
        }

        // Generate filter groups (OR/AND)
        if (_model.FilterGroups.Length > 0)
        {
            Comment("Apply filter groups");
            foreach (var group in _model.FilterGroups)
            {
                RenderFilterGroup(group);
            }

            AppendLine();
        }

        // Generate sorting
        if (_model.HasSortable)
        {
            Comment("Apply sorting");
            RenderSortingLogic();
            AppendLine();
        }

        // Generate paging
        if (_model.HasPaging)
        {
            Comment("Apply paging");
            RenderPagingLogic();
            AppendLine();
        }

        Return("query");
    }

    private void RenderFilterCondition(GridFilterPropertyModel prop)
    {
        // Cross-property search: OR across multiple entity properties
        if (prop.IsSearchAcross && prop.SearchAcrossPaths.Length > 0)
        {
            RenderSearchAcrossFilter(prop);
            return;
        }

        var entityPath = $"e.{prop.EffectivePropertyPath}";
        var propAccess = $"this.{prop.PropertyName}";

        // Check if property has value
        var nullCheck = prop.IsNullable
            ? $"{propAccess} is not null"
            : $"{propAccess} != default";

        If(nullCheck, () =>
        {
            var valueAccess = GetValueAccess(prop, propAccess);

            if (prop.HasOperatorProperty)
            {
                // Dynamic operator based on companion property
                RenderDynamicOperatorFilter(prop, entityPath, valueAccess);
            }
            else
            {
                // Fixed operator (Contains for strings, Equals for others)
                RenderFixedOperatorFilter(prop, entityPath, valueAccess);
            }
        });
    }

    /// <summary>
    ///     Generates a cross-property search filter with OR logic.
    ///     Example: query.Where(e => e.Name.Contains(v) || e.Email.Contains(v))
    /// </summary>
    private void RenderSearchAcrossFilter(GridFilterPropertyModel prop)
    {
        var propAccess = $"this.{prop.PropertyName}";
        var nullCheck = prop.IsNullable
            ? $"{propAccess} is not null"
            : $"!string.IsNullOrEmpty({propAccess})";

        If(nullCheck, () =>
        {
            var valueAccess = GetValueAccess(prop, propAccess);
            AppendLine($"query = query.Where(e => {SearchAcrossExpression(prop, valueAccess)});");
        });
    }

    /// <summary>
    ///     One value against several columns, joined by <c>||</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Shared by <c>Apply</c> and <c>ToSpecification</c>, so the two cannot drift: a
    ///     specification written separately can end up naming the filter's own property looked up on
    ///     the entity — <c>e.Search.Contains(…)</c>, a <c>CS1061</c> — and then the type does not
    ///     compile and <b>neither</b> half runs, while tests that assert over the whole rendered text
    ///     all pass.
    /// </remarks>
    private static string SearchAcrossExpression(GridFilterPropertyModel prop, string valueAccess)
        => SearchAcrossCondition.Render(prop.SearchAcrossPaths, valueAccess, prop.SearchIgnoresCase);

    private void RenderFilterGroup(FilterGroupModel group)
    {
        var groupAccess = $"this.{group.PropertyName}";
        var filterableProps = group.Properties.Where(p => p.IsFilterable).ToList();

        if (filterableProps.Count == 0)
            return;

        If($"{groupAccess} is not null", () =>
        {
            var logicalOp = group.Logic == "Or" ? " ||" : " &&";

            AppendLine("query = query.Where(e =>");
            IncreaseIndent();

            for (var i = 0; i < filterableProps.Count; i++)
            {
                var prop = filterableProps[i];
                var entityPath = $"e.{prop.EffectivePropertyPath}";
                var propAccess = $"{groupAccess}.{prop.PropertyName}";

                var predicate = BuildGroupPredicate(prop, entityPath, propAccess);
                var prefix = i == 0 ? "(" : "";
                var suffix = i < filterableProps.Count - 1 ? logicalOp : "";
                var close = i == filterableProps.Count - 1 ? ")" : "";

                AppendLine($"{prefix}{predicate}{suffix}{close}");
            }

            DecreaseIndent();
            AppendLine(");");
        });
    }

    private static string BuildGroupPredicate(GridFilterPropertyModel prop, string entityPath, string propAccess)
    {
        var nullCheck = prop.IsNullable
            ? $"{propAccess} is not null && "
            : "";

        var valueAccess = prop.IsNullable && !IsReferenceType(prop.PropertyType)
            ? $"{propAccess}.Value"
            : propAccess;

        if (IsStringType(prop.PropertyType))
            return $"({nullCheck}{entityPath}.Contains({valueAccess}))";

        return $"({nullCheck}{entityPath} == {valueAccess})";
    }

    private void RenderDynamicOperatorFilter(GridFilterPropertyModel prop, string entityPath, string valueAccess)
    {
        var operatorAccess = $"this.{prop.OperatorPropertyName}";

        // Check if string type
        if (IsStringType(prop.PropertyType))
        {
            Switch(operatorAccess, () =>
            {
                if (prop.AllowedOperators.HasFlag(FilterOpsKind.String))
                {
                    Case("StringOperator.Contains", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath}.Contains({valueAccess}));");
                        Break();
                    });
                    Case("StringOperator.StartsWith", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath}.StartsWith({valueAccess}));");
                        Break();
                    });
                    Case("StringOperator.EndsWith", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath}.EndsWith({valueAccess}));");
                        Break();
                    });
                }

                if (prop.AllowedOperators.HasFlag(FilterOpsKind.Equality))
                {
                    Case("StringOperator.Equals", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath} == {valueAccess});");
                        Break();
                    });
                    Case("StringOperator.NotEquals", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath} != {valueAccess});");
                        Break();
                    });
                }

                Default(() =>
                {
                    // Default to Contains for strings
                    AppendLine($"query = query.Where(e => {entityPath}.Contains({valueAccess}));");
                    Break();
                });
            });
        }
        else
        {
            // For non-string types, use FilterOperator
            Switch(operatorAccess, () =>
            {
                if (prop.AllowedOperators.HasFlag(FilterOpsKind.Equality))
                {
                    Case("FilterOperator.Equals", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath} == {valueAccess});");
                        Break();
                    });
                    Case("FilterOperator.NotEquals", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath} != {valueAccess});");
                        Break();
                    });
                }

                if (prop.AllowedOperators.HasFlag(FilterOpsKind.Compare))
                {
                    Case("FilterOperator.GreaterThan", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath} > {valueAccess});");
                        Break();
                    });
                    Case("FilterOperator.GreaterOrEqual", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath} >= {valueAccess});");
                        Break();
                    });
                    Case("FilterOperator.LessThan", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath} < {valueAccess});");
                        Break();
                    });
                    Case("FilterOperator.LessOrEqual", () =>
                    {
                        AppendLine($"query = query.Where(e => {entityPath} <= {valueAccess});");
                        Break();
                    });
                }

                Default(() =>
                {
                    AppendLine($"query = query.Where(e => {entityPath} == {valueAccess});");
                    Break();
                });
            });
        }
    }

    private void RenderFixedOperatorFilter(GridFilterPropertyModel prop, string entityPath, string valueAccess)
    {
        // Use Contains for strings, Equals for others
        if (IsStringType(prop.PropertyType))
        {
            AppendLine($"query = query.Where(e => {entityPath}.Contains({valueAccess}));");
        }
        else
        {
            AppendLine($"query = query.Where(e => {entityPath} == {valueAccess});");
        }
    }

    private void RenderToSpecificationMethod()
    {
        XmlSummary("Converts the filter conditions to a Specification. Sorting and paging are not included.");
        XmlReturns("A Specification representing the filters in this grid filter.");

        var entityParam = $"global::{_model.EntityTypeFullName}";
        var specType = $"Pragmatic.Specification.Specification<{entityParam}>";

        Method("ToSpecification", RenderToSpecificationBody, specType);
    }

    private void RenderToSpecificationBody()
    {
        var entityParam = $"global::{_model.EntityTypeFullName}";
        var specPrefix = $"Pragmatic.Specification.Spec<{entityParam}>";

        AppendLine($"var spec = {specPrefix}.True;");
        AppendLine();

        // Filterable properties
        if (_model.HasFilterable)
        {
            foreach (var prop in _model.FilterableProperties)
            {
                RenderSpecCondition(prop, entityParam);
            }

            AppendLine();
        }

        // Filter groups
        if (_model.FilterGroups.Length > 0)
        {
            foreach (var group in _model.FilterGroups)
            {
                RenderSpecGroup(group, entityParam);
            }

            AppendLine();
        }

        Return("spec");
    }

    private void RenderSpecCondition(GridFilterPropertyModel prop, string entityParam)
    {
        var entityPath = $"e.{prop.EffectivePropertyPath}";
        var propAccess = $"this.{prop.PropertyName}";
        var specPrefix = $"Pragmatic.Specification.Spec<{entityParam}>";

        var nullCheck = prop.IsNullable
            ? $"{propAccess} is not null"
            : $"{propAccess} != default";

        If(nullCheck, () =>
        {
            var valueAccess = GetValueAccess(prop, propAccess);

            // The same declaration Apply honours. Without this branch the condition below would read
            // the filter's own property off the entity, which is not a member of it: CS1061, and the
            // type would not compile — so neither this member nor the correct one would ever run.
            if (prop.IsSearchAcross && prop.SearchAcrossPaths.Length > 0)
            {
                AppendLine(
                    $"spec = spec & {specPrefix}.Where(e => {SearchAcrossExpression(prop, valueAccess)});");
                return;
            }

            if (prop.HasOperatorProperty)
            {
                RenderSpecDynamicOperator(prop, entityPath, valueAccess, specPrefix);
            }
            else
            {
                // Fixed operator
                var condition = IsStringType(prop.PropertyType)
                    ? $"{entityPath}.Contains({valueAccess})"
                    : $"{entityPath} == {valueAccess}";

                AppendLine($"spec = spec & {specPrefix}.Where(e => {condition});");
            }
        });
    }

    private void RenderSpecDynamicOperator(GridFilterPropertyModel prop, string entityPath, string valueAccess,
        string specPrefix)
    {
        var operatorAccess = $"this.{prop.OperatorPropertyName}";

        if (IsStringType(prop.PropertyType))
        {
            AppendLine($"spec = {operatorAccess} switch");
            AppendLine("{");
            IncreaseIndent();

            if (prop.AllowedOperators.HasFlag(FilterOpsKind.String))
            {
                AppendLine(
                    $"StringOperator.Contains => spec & {specPrefix}.Where(e => {entityPath}.Contains({valueAccess})),");
                AppendLine(
                    $"StringOperator.StartsWith => spec & {specPrefix}.Where(e => {entityPath}.StartsWith({valueAccess})),");
                AppendLine(
                    $"StringOperator.EndsWith => spec & {specPrefix}.Where(e => {entityPath}.EndsWith({valueAccess})),");
            }

            if (prop.AllowedOperators.HasFlag(FilterOpsKind.Equality))
            {
                AppendLine(
                    $"StringOperator.Equals => spec & {specPrefix}.Where(e => {entityPath} == {valueAccess}),");
                AppendLine(
                    $"StringOperator.NotEquals => spec & {specPrefix}.Where(e => {entityPath} != {valueAccess}),");
            }

            AppendLine($"_ => spec & {specPrefix}.Where(e => {entityPath}.Contains({valueAccess})),");

            DecreaseIndent();
            AppendLine("};");
        }
        else
        {
            AppendLine($"spec = {operatorAccess} switch");
            AppendLine("{");
            IncreaseIndent();

            if (prop.AllowedOperators.HasFlag(FilterOpsKind.Equality))
            {
                AppendLine(
                    $"FilterOperator.Equals => spec & {specPrefix}.Where(e => {entityPath} == {valueAccess}),");
                AppendLine(
                    $"FilterOperator.NotEquals => spec & {specPrefix}.Where(e => {entityPath} != {valueAccess}),");
            }

            if (prop.AllowedOperators.HasFlag(FilterOpsKind.Compare))
            {
                AppendLine(
                    $"FilterOperator.GreaterThan => spec & {specPrefix}.Where(e => {entityPath} > {valueAccess}),");
                AppendLine(
                    $"FilterOperator.GreaterOrEqual => spec & {specPrefix}.Where(e => {entityPath} >= {valueAccess}),");
                AppendLine(
                    $"FilterOperator.LessThan => spec & {specPrefix}.Where(e => {entityPath} < {valueAccess}),");
                AppendLine(
                    $"FilterOperator.LessOrEqual => spec & {specPrefix}.Where(e => {entityPath} <= {valueAccess}),");
            }

            AppendLine($"_ => spec & {specPrefix}.Where(e => {entityPath} == {valueAccess}),");

            DecreaseIndent();
            AppendLine("};");
        }
    }

    private void RenderSpecGroup(FilterGroupModel group, string entityParam)
    {
        var groupAccess = $"this.{group.PropertyName}";
        var specPrefix = $"Pragmatic.Specification.Spec<{entityParam}>";
        var filterableProps = group.Properties.Where(p => p.IsFilterable).ToList();

        if (filterableProps.Count == 0)
            return;

        var isOr = group.Logic == "Or";
        var identity = isOr ? "False" : "True";
        var combineOp = isOr ? "|" : "&";

        If($"{groupAccess} is not null", () =>
        {
            AppendLine($"var groupSpec = {specPrefix}.{identity};");

            foreach (var prop in filterableProps)
            {
                var entityPath = $"e.{prop.EffectivePropertyPath}";
                var propAccess = $"{groupAccess}.{prop.PropertyName}";

                var nullCheck = prop.IsNullable
                    ? $"{propAccess} is not null"
                    : $"{propAccess} != default";

                var valueAccess = prop.IsNullable && !IsReferenceType(prop.PropertyType)
                    ? $"{propAccess}.Value"
                    : propAccess;

                if (IsReferenceType(prop.PropertyType) && prop.IsNullable)
                    valueAccess = $"{propAccess}!";

                var condition = IsStringType(prop.PropertyType)
                    ? $"{entityPath}.Contains({valueAccess})"
                    : $"{entityPath} == {valueAccess}";

                If(nullCheck, () =>
                {
                    AppendLine(
                        $"groupSpec = groupSpec {combineOp} {specPrefix}.Where(e => {condition});");
                });
            }

            AppendLine($"spec = spec & groupSpec;");
        });
    }

    private void RenderSortingLogic()
    {
        var sortProps = _model.SortableProperties.ToList();
        if (sortProps.Count == 0)
            return;

        AppendLine("var isFirstSort = true;");

        foreach (var prop in sortProps)
        {
            var entityPath = prop.EffectivePropertyPath;
            var propAccess = $"this.{prop.PropertyName}";

            If($"{propAccess} is not null", () =>
            {
                If("isFirstSort", () =>
                {
                    If($"{propAccess}.Value == SortDirection.Ascending", () =>
                    {
                        AppendLine($"query = query.OrderBy(e => e.{entityPath});");
                    });
                    Else(() => { AppendLine($"query = query.OrderByDescending(e => e.{entityPath});"); });
                    AppendLine("isFirstSort = false;");
                });
                Else(() =>
                {
                    If($"{propAccess}.Value == SortDirection.Ascending", () =>
                    {
                        AppendLine(
                            $"query = ((IOrderedQueryable<global::{_model.EntityTypeFullName}>)query).ThenBy(e => e.{entityPath});");
                    });
                    Else(() =>
                    {
                        AppendLine(
                            $"query = ((IOrderedQueryable<global::{_model.EntityTypeFullName}>)query).ThenByDescending(e => e.{entityPath});");
                    });
                });
            });
        }
    }

    private void RenderPagingLogic()
    {
        var pageProp = _model.PageProperty;
        var pageSizeProp = _model.PageSizeProperty;

        if (pageProp is null || pageSizeProp is null)
            return;

        AppendLine($"var page = Math.Max(1, this.{pageProp.PropertyName});");
        AppendLine($"var pageSize = Math.Max(1, this.{pageSizeProp.PropertyName});");
        AppendLine("var skip = (page - 1) * pageSize;");
        AppendLine("query = query.Skip(skip).Take(pageSize);");
    }

    private static string GetValueAccess(GridFilterPropertyModel prop, string propAccess)
    {
        if (prop.IsNullable && !IsReferenceType(prop.PropertyType))
            return $"{propAccess}.Value";

        if (IsReferenceType(prop.PropertyType) && prop.IsNullable)
            return $"{propAccess}!";

        return propAccess;
    }

    private static bool IsStringType(string typeName)
    {
        var baseType = typeName.TrimEnd('?');
        return baseType == "string" || baseType == "String" || baseType == "System.String";
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
