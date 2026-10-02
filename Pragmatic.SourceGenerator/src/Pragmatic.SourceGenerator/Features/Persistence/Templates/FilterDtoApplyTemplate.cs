using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating FilterDto ApplyFilter extension method
///     and BuildFilterExpression helper. Supports nested AND/OR groups.
/// </summary>
internal sealed class FilterDtoApplyTemplate : CSharpTemplate
{
    private readonly FilterDtoModel _model;
    private string _entityParam = null!;

    public FilterDtoApplyTemplate(FilterDtoModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[FilterDto] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "FilterDto", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");

        AppendNamespace(_model.Namespace);
        AppendLine();

        _entityParam = $"global::{_model.EntityTypeFullName}";

        XmlSummary($"Extension methods for applying {_model.TypeName} filters.");

        Class($"{_model.TypeName}Extensions", RenderExtensionBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    private void RenderExtensionBody()
    {
        RenderApplyFilterMethod();
        AppendLine();
        RenderBuildExpressionMethod();
    }

    private void RenderApplyFilterMethod()
    {
        var filterType = $"global::{_model.FullTypeName}";

        XmlSummary($"Applies {_model.TypeName} filters to the query. Null filter returns the query unchanged.");
        XmlParam("query", "The source query.");
        XmlParam("filter", "The filter DTO. When null, query is returned as-is.");
        XmlReturns("The filtered query.");

        var parameters = new List<MethodParameter>
        {
            new($"global::System.Linq.IQueryable<{_entityParam}>", "query") { IsExtension = true },
            new($"{filterType}?", "filter")
        };

        Method("ApplyFilter", () =>
        {
            AppendLine("if (filter is null) return query;");
            AppendLine();
            AppendLine("var spec = ToSpecification(filter);");
            AppendLine("return spec is null ? query : global::Pragmatic.Specification.SpecificationExtensions.Where(query, spec);");
        }, $"global::System.Linq.IQueryable<{_entityParam}>", parameters, AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }

    private void RenderBuildExpressionMethod()
    {
        var filterType = $"global::{_model.FullTypeName}";
        var specType = $"global::Pragmatic.Specification.Specification<{_entityParam}>";

        XmlSummary("Builds the combined filter specification. Used internally and by parent filter groups.");
        XmlParam("filter", "The filter DTO.");
        XmlParam("useOrLogic", "When true, combines filters with OR instead of AND.");
        XmlReturns("The combined specification, or null if no filters are active.");

        var parameters = new List<MethodParameter>
        {
            new(filterType, "filter"),
            new("bool", "useOrLogic") { DefaultValue = "false" }
        };

        Method("ToSpecification", () =>
        {
            AppendLine($"{specType}? result = null;");
            AppendLine();

            // Render each [Filter] property
            foreach (var filter in _model.Filters)
            {
                RenderFilterProperty(filter);
            }

            // Render each [FilterGroup] property
            foreach (var group in _model.Groups)
            {
                RenderGroupProperty(group);
            }

            AppendLine("return result;");
        }, $"{specType}?", parameters, AccessModifier.Internal,
            new MethodModifiers { IsStatic = true });
    }

    private void RenderFilterProperty(FilterDtoPropertyModel filter)
    {
        var entityPath = BuildEntityPath(filter.EntityPropertyPath);
        var filterAccess = $"filter.{filter.PropertyName}";
        var valueAccess = filter is { IsNullable: true, IsString: false, IsCollection: false }
            ? $"{filterAccess}.Value"
            : filterAccess;

        // Null guard
        AppendLine($"if ({filterAccess} is not null)");
        Block(() =>
        {
            var expression = BuildFilterExpression(filter, entityPath, valueAccess);
            AppendLine($"var f = global::Pragmatic.Specification.Spec<{_entityParam}>.Where(e => {expression});");
            AppendLine("result = result is null ? f : (useOrLogic ? (result | f) : (result & f));");
        });
        AppendLine();
    }

    private void RenderGroupProperty(FilterDtoGroupModel group)
    {
        var filterAccess = $"filter.{group.PropertyName}";
        var extensionsClass = $"global::{group.GroupTypeFullName}Extensions";
        var useOr = group.UseOrLogic ? "true" : "false";

        if (group.IsNullable)
        {
            AppendLine($"if ({filterAccess} is not null)");
            Block(() =>
            {
                AppendLine($"var groupSpec = {extensionsClass}.ToSpecification({filterAccess}, useOrLogic: {useOr});");
                AppendLine("if (groupSpec is not null)");
                IncreaseIndent();
                AppendLine("result = result is null ? groupSpec : (useOrLogic ? (result | groupSpec) : (result & groupSpec));");
                DecreaseIndent();
            });
        }
        else
        {
            var varName = ToCamelCase(group.PropertyName) + "Spec";
            AppendLine($"var {varName} = {extensionsClass}.ToSpecification({filterAccess}, useOrLogic: {useOr});");
            AppendLine($"if ({varName} is not null)");
            IncreaseIndent();
            AppendLine($"result = result is null ? {varName} : (useOrLogic ? (result | {varName}) : (result & {varName}));");
            DecreaseIndent();
        }

        AppendLine();
    }

    private string BuildFilterExpression(
        FilterDtoPropertyModel filter,
        string entityPath,
        string valueAccess)
    {
        var ep = entityPath;
        var va = valueAccess;

        // Apply IgnoreCase for string operations
        if (filter is { IgnoreCase: true, IsString: true })
        {
            ep = $"{entityPath}.ToLower()";
            va = $"{valueAccess}.ToLower()";
        }

        return filter.Operator switch
        {
            "Equals" => $"e.{ep} == {va}",
            "NotEquals" => $"e.{ep} != {va}",
            "Contains" when filter.IsString => $"e.{ep}.Contains({va})",
            "StartsWith" when filter.IsString => $"e.{ep}.StartsWith({va})",
            "EndsWith" when filter.IsString => $"e.{ep}.EndsWith({va})",
            "GreaterThan" => $"e.{ep} > {va}",
            "GreaterOrEqual" => $"e.{ep} >= {va}",
            "LessThan" => $"e.{ep} < {va}",
            "LessOrEqual" => $"e.{ep} <= {va}",
            "In" when filter.IsCollection => $"{valueAccess}.Contains(e.{entityPath})",
            _ => $"e.{ep} == {va}"
        };
    }

    private static string BuildEntityPath(string propertyPath)
    {
        // Supports nested paths like "Customer.Name"
        return propertyPath;
    }

    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

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
