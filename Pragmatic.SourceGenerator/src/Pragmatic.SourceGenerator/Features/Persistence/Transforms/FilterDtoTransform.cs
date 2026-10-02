using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transform logic for [FilterDto&lt;TEntity&gt;] attribute.
///     Reads filter properties and groups, produces a FilterDtoModel.
/// </summary>
internal static class FilterDtoTransform
{
    private const string FilterAttributeName = "Pragmatic.Persistence.Query.Attributes.FilterAttribute";
    private const string FilterGroupAttributeName = "Pragmatic.Persistence.Query.Attributes.FilterGroupAttribute";

    public static FilterDtoModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol targetSymbol)
            return null;

        var attribute = context.Attributes.FirstOrDefault();
        if (attribute?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType)
            return null;

        var entityType = attrType.TypeArguments[0] as INamedTypeSymbol;
        if (entityType is null)
            return null;

        if (!IsPartialType(context.TargetNode))
            return null;

        var (filters, groups) = ExtractMembers(targetSymbol, ct);

        return new FilterDtoModel
        {
            Namespace = targetSymbol.ContainingNamespace.ToDisplayString(),
            TypeName = targetSymbol.Name,
            Accessibility = targetSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            TypeKind = targetSymbol.IsRecord ? "record" : "class",
            IsRecord = targetSymbol.IsRecord,
            EntityTypeFullName = entityType.ToDisplayString(),
            EntityTypeName = entityType.Name,
            Filters = filters,
            Groups = groups
        };
    }

    private static (ImmutableArray<FilterDtoPropertyModel> filters, ImmutableArray<FilterDtoGroupModel> groups)
        ExtractMembers(INamedTypeSymbol symbol, CancellationToken ct)
    {
        var filters = ImmutableArray.CreateBuilder<FilterDtoPropertyModel>();
        var groups = ImmutableArray.CreateBuilder<FilterDtoGroupModel>();

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            ct.ThrowIfCancellationRequested();

            if (property.IsIndexer || property.IsStatic || property.GetMethod is null)
                continue;

            // Check [FilterGroup] first (takes precedence)
            var groupAttr = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FilterGroupAttributeName);

            if (groupAttr is not null)
            {
                var group = CreateGroupModel(property, groupAttr);
                if (group is not null)
                    groups.Add(group);
                continue;
            }

            // Check [Filter]
            var filterAttr = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FilterAttributeName);

            if (filterAttr is not null)
            {
                var filter = CreateFilterModel(property, filterAttr);
                filters.Add(filter);
            }
        }

        return (filters.ToImmutable(), groups.ToImmutable());
    }

    private static FilterDtoPropertyModel CreateFilterModel(
        IPropertySymbol property,
        AttributeData filterAttr)
    {
        var propertyName = property.Name;
        var propertyType = property.Type.ToDisplayString();
        var isNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                         property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

        var isString = IsStringType(property.Type);
        var isCollection = IsCollectionType(property.Type);

        // Parse attribute arguments
        var operatorValue = "Equals";
        string? mapTo = null;
        var ignoreCase = false;

        foreach (var arg in filterAttr.NamedArguments)
        {
            switch (arg.Key)
            {
                case "Operator" when arg.Value.Value is int opVal:
                    operatorValue = MapOperator(opVal);
                    break;
                case "MapTo" when arg.Value.Value is string mapToVal:
                    mapTo = mapToVal;
                    break;
                case "IgnoreCase" when arg.Value.Value is bool ignoreCaseVal:
                    ignoreCase = ignoreCaseVal;
                    break;
            }
        }

        // Default: Contains for strings, Equals for others
        if (!filterAttr.NamedArguments.Any(a => a.Key == "Operator") && isString)
            operatorValue = "Contains";

        return new FilterDtoPropertyModel
        {
            PropertyName = propertyName,
            PropertyType = propertyType,
            EntityPropertyPath = mapTo ?? propertyName,
            Operator = operatorValue,
            IgnoreCase = ignoreCase,
            IsNullable = isNullable,
            IsString = isString,
            IsCollection = isCollection
        };
    }

    private static FilterDtoGroupModel? CreateGroupModel(
        IPropertySymbol property,
        AttributeData groupAttr)
    {
        var propertyType = property.Type;

        // Unwrap Nullable<T> (value type)
        if (propertyType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } namedNullable)
            propertyType = namedNullable.TypeArguments[0];

        // Strip nullable annotation for reference types (DateRangeFilter? → DateRangeFilter)
        if (propertyType.NullableAnnotation == NullableAnnotation.Annotated)
            propertyType = propertyType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        var isNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                         property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

        // Parse logic from constructor argument (FilterLogic enum: 0=And, 1=Or)
        var useOrLogic = false;
        if (groupAttr.ConstructorArguments.Length > 0 &&
            groupAttr.ConstructorArguments[0].Value is int logicVal)
        {
            useOrLogic = logicVal == 1; // FilterLogic.Or = 1
        }

        return new FilterDtoGroupModel
        {
            PropertyName = property.Name,
            GroupTypeFullName = propertyType.ToDisplayString(),
            GroupTypeName = propertyType.Name,
            UseOrLogic = useOrLogic,
            IsNullable = isNullable
        };
    }

    private static string MapOperator(int value)
    {
        return value switch
        {
            0 => "Equals",
            1 => "NotEquals",
            2 => "Contains",
            3 => "StartsWith",
            4 => "EndsWith",
            5 => "GreaterThan",
            6 => "GreaterOrEqual",
            7 => "LessThan",
            8 => "LessOrEqual",
            9 => "In",
            10 => "Between",
            _ => "Equals"
        };
    }

    private static bool IsStringType(ITypeSymbol type)
    {
        var unwrapped = UnwrapNullable(type);
        return unwrapped.SpecialType == SpecialType.System_String;
    }

    private static bool IsCollectionType(ITypeSymbol type)
    {
        var unwrapped = UnwrapNullable(type);
        if (unwrapped is INamedTypeSymbol named)
        {
            return named.AllInterfaces.Any(i =>
                i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");
        }

        return false;
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return nullable.TypeArguments[0];
        return type;
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
}
