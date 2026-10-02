using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transforms [GridAdapter&lt;TEntity&gt;] declarations into GridAdapterModel.
/// </summary>
internal static class GridAdapterTransform
{
    private const string GridAdapterAttributeName = "Pragmatic.Persistence.Query.Attributes.GridAdapterAttribute`1";
    private const string GridFieldAttributeName = "Pragmatic.Persistence.Query.Attributes.GridFieldAttribute";
    private const string GridExcludeAttributeName = "Pragmatic.Persistence.Query.Attributes.GridExcludeAttribute";

    public static GridAdapterModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol classSymbol)
            return null;

        var classDeclaration = context.TargetNode as ClassDeclarationSyntax;
        if (classDeclaration == null)
            return null;

        // Get the GridAdapter attribute from the context (provided by ForAttributeWithMetadataName)
        var gridAdapterAttr = context.Attributes.FirstOrDefault();
        if (gridAdapterAttr?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType)
            return null;

        var entityType = attrType.TypeArguments[0] as INamedTypeSymbol;
        if (entityType is null)
            return null;

        // Parse attribute properties
        var framework = GetFramework(gridAdapterAttr);
        var supportNestedFilters = GetBoolProperty(gridAdapterAttr, "SupportNestedFilters", true);
        var supportGrouping = GetBoolProperty(gridAdapterAttr, "SupportGrouping", true);

        // Parse GridField attributes (custom mappings)
        var customFields = ParseGridFieldAttributes(classSymbol);

        // Parse GridExclude attributes
        var excludedProperties = ParseExcludedProperties(classSymbol);

        // Get all properties from entity
        var fields = GetEntityFields(entityType, customFields, excludedProperties, cancellationToken);

        return new GridAdapterModel
        {
            Namespace = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : classSymbol.ContainingNamespace.ToDisplayString(),
            TypeName = classSymbol.Name,
            Accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            EntityType = entityType.Name,
            EntityTypeFullName = entityType.ToDisplayString(),
            Framework = framework,
            SupportNestedFilters = supportNestedFilters,
            SupportGrouping = supportGrouping,
            Fields = fields,
            ExcludedProperties = excludedProperties
        };
    }

    private static GridFrameworkFlags GetFramework(AttributeData attr)
    {
        var value = attr.NamedArguments
            .FirstOrDefault(na => na.Key == "Framework")
            .Value.Value;

        if (value is int intValue)
            return (GridFrameworkFlags)intValue;

        return GridFrameworkFlags.Both;
    }

    private static bool GetBoolProperty(AttributeData attr, string name, bool defaultValue)
    {
        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == name);
        if (arg.Value.Value is bool boolValue)
            return boolValue;
        return defaultValue;
    }

    private static Dictionary<string, GridFieldAttributeData> ParseGridFieldAttributes(INamedTypeSymbol classSymbol)
    {
        var result = new Dictionary<string, GridFieldAttributeData>(StringComparer.OrdinalIgnoreCase);

        foreach (var attr in classSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == GridFieldAttributeName))
        {
            if (attr.ConstructorArguments.Length == 0)
                continue;

            var jsonField = attr.ConstructorArguments[0].Value?.ToString();
            if (string.IsNullOrEmpty(jsonField))
                continue;

            var data = new GridFieldAttributeData
            {
                JsonField = jsonField!,
                Property = GetStringNamedArg(attr, "Property"),
                Filterable = GetBoolNamedArg(attr, "Filterable", true),
                Sortable = GetBoolNamedArg(attr, "Sortable", true),
                Groupable = GetBoolNamedArg(attr, "Groupable", true),
                AllowedOperators = GetStringArrayNamedArg(attr, "AllowedOperators")
            };

            result[jsonField!] = data;
        }

        return result;
    }

    private static ImmutableArray<string> ParseExcludedProperties(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == GridExcludeAttributeName)
            .Select(a => a.ConstructorArguments.FirstOrDefault().Value?.ToString())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToImmutableArray()!;
    }

    private static ImmutableArray<GridFieldModel> GetEntityFields(
        INamedTypeSymbol entityType,
        Dictionary<string, GridFieldAttributeData> customFields,
        ImmutableArray<string> excludedProperties,
        CancellationToken cancellationToken)
    {
        var fields = new List<GridFieldModel>();
        var processedJsonFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ⚠️ And by property, not only by JSON name. The generated methods are named after the
        // PROPERTY — ApplySortStarRating, CreateStarRatingFilter — so a [GridField] that gives a column
        // a different name on the wire would produce the alias BESIDE the auto-discovered column and
        // the file would not compile: CS0111, the same member twice — on [GridField]'s main
        // documented use.
        var processedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First, add custom fields from attributes
        foreach (var kvp in customFields)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var jsonField = kvp.Key;
            var data = kvp.Value;
            var propertyPath = data.Property ?? ToPascalCase(jsonField);
            var propertyInfo = ResolvePropertyPath(entityType, propertyPath);

            if (propertyInfo == null)
                continue;

            // Never expose framework-reserved sensitive columns (credentials / authz) to a
            // client-driven grid, even when a [GridField] explicitly maps them.
            if (SensitiveFieldNames.IsSensitive(propertyInfo.Symbol.Name))
                continue;

            // A second alias for one property would generate its methods twice, so the first
            // declaration wins and the rest are dropped rather than producing a file that cannot
            // compile. One column, one pair of methods.
            if (!processedProperties.Add(propertyPath))
                continue;

            fields.Add(CreateFieldModel(jsonField, propertyPath, propertyInfo, data));
            processedJsonFields.Add(jsonField);
        }

        // Then, add all entity properties (unless excluded or already mapped)
        foreach (var property in GetAllProperties(entityType))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (excludedProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            // Never auto-expose framework-reserved sensitive columns (credentials / authz): a
            // filterable secret is a boolean oracle, answering yes/no to guesses at its value.
            if (SensitiveFieldNames.IsSensitive(property.Name))
                continue;

            // Skip if already mapped via custom field — by the name on the wire OR by the property,
            // because a [GridField] that renamed this column has already produced its methods.
            var jsonField = ToCamelCase(property.Name);
            if (processedJsonFields.Contains(jsonField) || processedProperties.Contains(property.Name))
                continue;

            // Skip navigation properties and complex types (unless explicitly mapped)
            if (IsNavigationProperty(property))
                continue;

            var propertyInfo = new PropertyInfo
            {
                Symbol = property,
                Type = property.Type,
                IsNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                             IsNullableValueType(property.Type)
            };

            fields.Add(CreateFieldModel(jsonField, property.Name, propertyInfo, null));
            processedJsonFields.Add(jsonField);
            processedProperties.Add(property.Name);
        }

        return fields.ToImmutableArray();
    }

    private static GridFieldModel CreateFieldModel(
        string jsonField,
        string propertyPath,
        PropertyInfo propertyInfo,
        GridFieldAttributeData? customData)
    {
        var typeCategory = GetTypeCategory(propertyInfo.Type);
        var underlyingType = GetUnderlyingType(propertyInfo.Type);
        var underlyingTypeFullName = GetUnderlyingTypeFullName(propertyInfo.Type);
        var propertyName = propertyPath.Replace(".", "");

        return new GridFieldModel
        {
            JsonField = jsonField,
            PropertyPath = propertyPath,
            PropertyName = propertyName,
            PropertyType = propertyInfo.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            PropertyTypeFullName = propertyInfo.Type.ToDisplayString(),
            IsNullable = propertyInfo.IsNullable,
            UnderlyingType = underlyingType,
            UnderlyingTypeFullName = underlyingTypeFullName,
            TypeCategory = typeCategory,
            IsNested = propertyPath.Contains('.'),
            Filterable = customData?.Filterable ?? true,
            Sortable = customData?.Sortable ?? true,
            Groupable = customData?.Groupable ?? true,
            AllowedOperators = customData?.AllowedOperators != null
                ? customData.AllowedOperators.ToImmutableArray()
                : null
        };
    }

    private static PropertyInfo? ResolvePropertyPath(INamedTypeSymbol type, string path)
    {
        var parts = path.Split('.');
        ITypeSymbol currentType = type;
        IPropertySymbol? lastProperty = null;

        foreach (var part in parts)
        {
            if (currentType is not INamedTypeSymbol namedType)
                return null;

            lastProperty = GetAllProperties(namedType)
                .FirstOrDefault(p => p.Name.Equals(part, StringComparison.OrdinalIgnoreCase));

            if (lastProperty == null)
                return null;

            currentType = lastProperty.Type;
        }

        if (lastProperty == null)
            return null;

        return new PropertyInfo
        {
            Symbol = lastProperty,
            Type = lastProperty.Type,
            IsNullable = lastProperty.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                         IsNullableValueType(lastProperty.Type)
        };
    }

    private static IEnumerable<IPropertySymbol> GetAllProperties(INamedTypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.DeclaredAccessibility == Accessibility.Public &&
                    member is { IsStatic: false, IsIndexer: false, GetMethod: not null })
                {
                    yield return member;
                }
            }
            current = current.BaseType;
        }
    }

    private static bool IsNavigationProperty(IPropertySymbol property)
    {
        var type = property.Type;

        // Skip collections (ICollection<T>, IEnumerable<T>, List<T>, etc.)
        if (type is INamedTypeSymbol namedType)
        {
            if (namedType.IsGenericType)
            {
                var typeName = namedType.OriginalDefinition.ToDisplayString();
                if (typeName.StartsWith("System.Collections") ||
                    typeName.Contains("IEnumerable") ||
                    typeName.Contains("ICollection") ||
                    typeName.Contains("IList"))
                {
                    return true;
                }
            }

            // Skip if type is a class with properties (likely a navigation property)
            // But allow if it's a known value type wrapper (Nullable<T>)
            if (namedType.TypeKind == TypeKind.Class &&
                !namedType.SpecialType.HasFlag(SpecialType.System_String) &&
                namedType.SpecialType == SpecialType.None &&
                !IsKnownValueType(namedType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownValueType(ITypeSymbol type)
    {
        var name = type.ToDisplayString();
        return name.StartsWith("System.Guid") ||
               name.StartsWith("System.DateTime") ||
               name.StartsWith("System.DateTimeOffset") ||
               name.StartsWith("System.TimeSpan") ||
               name.StartsWith("System.Decimal") ||
               name.StartsWith("NodaTime.");
    }

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsGenericType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    private static PropertyTypeCategory GetTypeCategory(ITypeSymbol type)
    {
        var unwrapped = UnwrapNullable(type);
        var name = unwrapped.ToDisplayString();

        if (unwrapped.SpecialType == SpecialType.System_String)
            return PropertyTypeCategory.String;

        if (unwrapped.SpecialType == SpecialType.System_Boolean)
            return PropertyTypeCategory.Boolean;

        if (IsNumericType(unwrapped))
            return PropertyTypeCategory.Numeric;

        if (name.StartsWith("System.DateTime") || name.StartsWith("System.DateTimeOffset") ||
            name.StartsWith("NodaTime.LocalDate") || name.StartsWith("NodaTime.Instant"))
            return PropertyTypeCategory.DateTime;

        if (name.StartsWith("System.Guid"))
            return PropertyTypeCategory.Guid;

        if (unwrapped.TypeKind == TypeKind.Enum)
            return PropertyTypeCategory.Enum;

        return PropertyTypeCategory.Unknown;
    }

    private static bool IsNumericType(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_Byte => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            SpecialType.System_Single => true,
            SpecialType.System_Double => true,
            SpecialType.System_Decimal => true,
            _ => false
        };
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } namedType)
        {
            return namedType.TypeArguments[0];
        }
        return type;
    }

    private static string GetUnderlyingType(ITypeSymbol type)
    {
        var unwrapped = UnwrapNullable(type);
        return unwrapped.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static string GetUnderlyingTypeFullName(ITypeSymbol type)
    {
        var unwrapped = UnwrapNullable(type);
        return unwrapped.ToDisplayString();
    }

    private static string? GetStringNamedArg(AttributeData attr, string name)
    {
        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == name);
        return arg.Value.Value?.ToString();
    }

    private static bool GetBoolNamedArg(AttributeData attr, string name, bool defaultValue)
    {
        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == name);
        if (arg.Value.Value is bool boolValue)
            return boolValue;
        return defaultValue;
    }

    private static string[]? GetStringArrayNamedArg(AttributeData attr, string name)
    {
        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == name);
        if (arg.Value.IsNull)
            return null;

        if (arg.Value.Kind == TypedConstantKind.Array)
        {
            return arg.Value.Values
                .Select(v => v.Value?.ToString())
                .Where(v => v != null)
                .ToArray()!;
        }

        return null;
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    private class PropertyInfo
    {
        public required IPropertySymbol Symbol { get; init; }
        public required ITypeSymbol Type { get; init; }
        public required bool IsNullable { get; init; }
    }

    private class GridFieldAttributeData
    {
        public required string JsonField { get; init; }
        public string? Property { get; init; }
        public bool Filterable { get; init; }
        public bool Sortable { get; init; }
        public bool Groupable { get; init; }
        public string[]? AllowedOperators { get; init; }
    }
}
