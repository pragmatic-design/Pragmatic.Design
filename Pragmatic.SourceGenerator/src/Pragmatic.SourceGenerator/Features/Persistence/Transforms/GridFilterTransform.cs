using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transform logic for [GridFilter] attribute.
/// </summary>
internal static class GridFilterTransform
{
    private const string FilterableAttributeName = "Pragmatic.Persistence.Query.Attributes.FilterableAttribute";
    private const string SortAttributeName = "Pragmatic.Persistence.Query.Attributes.SortAttribute";

    /// <summary>
    ///     Transforms [GridFilter] attribute to GridFilterModel.
    /// </summary>
    public static GridFilterModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol targetSymbol)
            return null;

        // Get entity type from attribute
        var attribute = context.Attributes.FirstOrDefault();
        if (attribute?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType)
            return null;

        var entityType = attrType.TypeArguments[0] as INamedTypeSymbol;
        if (entityType is null)
            return null;

        // Check if partial
        if (!IsPartialType(context.TargetNode))
            return null;

        // Extract properties
        var properties = ExtractProperties(targetSymbol, ct);

        return new GridFilterModel
        {
            Namespace = targetSymbol.ContainingNamespace.ToDisplayString(),
            TypeName = targetSymbol.Name,
            Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            TypeKind = GetTypeKind(targetSymbol),
            IsRecord = targetSymbol.IsRecord,
            EntityTypeFullName = entityType.ToDisplayString(),
            EntityTypeName = entityType.Name,
            Properties = properties
        };
    }

    private static ImmutableArray<GridFilterPropertyModel> ExtractProperties(
        INamedTypeSymbol symbol,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<GridFilterPropertyModel>();
        var allProperties = symbol.GetMembers().OfType<IPropertySymbol>().ToList();

        foreach (var property in allProperties)
        {
            ct.ThrowIfCancellationRequested();

            // Skip indexers and static properties
            if (property.IsIndexer || property.IsStatic)
                continue;

            // Skip properties without getter
            if (property.GetMethod is null)
                continue;

            var model = CreatePropertyModel(property, allProperties);
            if (model is not null)
                builder.Add(model);
        }

        return builder.ToImmutable();
    }

    private static GridFilterPropertyModel? CreatePropertyModel(
        IPropertySymbol property,
        List<IPropertySymbol> allProperties)
    {
        var propertyName = property.Name;
        var propertyType = property.Type.ToDisplayString();
        var isNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                         property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

        // Check for [Filterable] attribute
        var filterableAttr = property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FilterableAttributeName);

        // [SearchAcross]: the columns, and whether case matters — read as the query reads them
        var searchAcross = SearchAcrossReader.Read(property);

        var sortableAttr = property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SortAttributeName);

        // Determine if this is a paging property by convention
        var isPageProperty = propertyName.Equals("Page", StringComparison.OrdinalIgnoreCase);
        var isPageSizeProperty = propertyName.Equals("PageSize", StringComparison.OrdinalIgnoreCase);

        // Skip operator properties (they're handled with their main property)
        if (propertyName.EndsWith("Operator", StringComparison.Ordinal))
            return null;

        var isFilterable = filterableAttr is not null;
        var isSearchAcross = searchAcross is not null;
        var isSortable = sortableAttr is not null;

        // If neither filterable nor sortable nor searchAcross, and not a paging property, skip
        if (!isFilterable && !isSearchAcross && !isSortable && !isPageProperty && !isPageSizeProperty)
            return null;

        // Parse filterable attributes
        var allowedOperators = FilterOpsKind.All;
        string? mapTo = null;
        string? handlerTypeName = null;
        string? handlerArgs = null;

        if (filterableAttr is not null)
        {
            foreach (var arg in filterableAttr.NamedArguments)
            {
                switch (arg.Key)
                {
                    case "Operators" when arg.Value.Value is int opsValue:
                        allowedOperators = (FilterOpsKind)opsValue;
                        break;
                    case "MapTo" when arg.Value.Value is string mapToValue:
                        mapTo = mapToValue;
                        break;
                    case "Handler" when arg.Value.Value is INamedTypeSymbol handlerType:
                        handlerTypeName = handlerType.ToDisplayString();
                        break;
                    case "HandlerArgs" when arg.Value.Value is string args:
                        handlerArgs = args;
                        break;
                }
            }
        }

        // Parse the [Sort] attribute
        var sortPriority = 0;

        if (sortableAttr is not null)
        {
            foreach (var arg in sortableAttr.NamedArguments)
            {
                switch (arg.Key)
                {
                    case "MapTo" when arg.Value.Value is string sortMapTo:
                        mapTo = sortMapTo;
                        break;
                    case "Priority" when arg.Value.Value is int priority:
                        sortPriority = priority;
                        break;
                    case "DefaultDirection" when arg.Value.Value is int _:
                        // A SortDirection? arrives as its underlying int. Read and ignored here: the
                        // grid's default ordering is not this transform's to decide.
                        break;
                }
            }
        }

        var searchAcrossPaths = searchAcross?.Paths ?? ImmutableArray<string>.Empty;

        // Check for companion operator property
        var operatorPropertyName = $"{propertyName}Operator";
        var hasOperatorProperty = allProperties.Any(p =>
            p.Name.Equals(operatorPropertyName, StringComparison.Ordinal));

        return new GridFilterPropertyModel
        {
            PropertyName = propertyName,
            PropertyType = propertyType,
            IsNullable = isNullable,
            MapTo = mapTo,
            IsFilterable = isFilterable || isSearchAcross,
            IsSortable = isSortable,
            IsSearchAcross = isSearchAcross,
            SearchAcrossPaths = searchAcrossPaths,
            SearchIgnoresCase = searchAcross?.IgnoreCase ?? false,
            AllowedOperators = allowedOperators,
            SortPriority = sortPriority,
            IsPageProperty = isPageProperty,
            IsPageSizeProperty = isPageSizeProperty,
            OperatorPropertyName = hasOperatorProperty ? operatorPropertyName : null,
            HasOperatorProperty = hasOperatorProperty,
            HandlerTypeName = handlerTypeName,
            HandlerArgs = handlerArgs
        };
    }

    private static bool IsPartialType(SyntaxNode node)
    {
        return node switch
        {
            ClassDeclarationSyntax c => c.Modifiers.Any(SyntaxKind.PartialKeyword),
            RecordDeclarationSyntax r => r.Modifiers.Any(SyntaxKind.PartialKeyword),
            _ => false
        };
    }

    private static string GetTypeKind(INamedTypeSymbol symbol)
    {
        if (symbol.IsRecord)
            return "record";
        return "class";
    }
}
