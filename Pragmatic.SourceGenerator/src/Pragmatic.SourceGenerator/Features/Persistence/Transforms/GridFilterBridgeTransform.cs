using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Extracts entity property metadata for GridFilterBridge generation.
///     Triggered by [GenerateGridBridge] on an entity class.
/// </summary>
internal static class GridFilterBridgeTransform
{
    /// <summary>
    ///     Transforms a [GenerateGridBridge]-decorated class into a bridge model.
    /// </summary>
    public static GridFilterBridgeModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol targetSymbol)
            return null;

        var properties = CollectBridgeProperties(targetSymbol, ct);
        var withheld = CollectWithheldProperties(targetSymbol, ct);

        return new GridFilterBridgeModel
        {
            Namespace = targetSymbol.ContainingNamespace.ToDisplayString(),
            EntityTypeName = targetSymbol.Name,
            EntityFullTypeName = targetSymbol.ToDisplayString(),
            Properties = properties,
            WithheldProperties = withheld
        };
    }

    private const string FilterableAttributeName = "Pragmatic.Persistence.Query.Attributes.FilterableAttribute";
    private const string GridExcludeAttributeName = "Pragmatic.Persistence.Query.Attributes.GridExcludeAttribute";

    /// <summary>
    ///     The properties the bridge may name: the ones the entity declares, minus the ones it takes
    ///     back, minus the ones the framework never exposes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An <b>allowlist</b>, and it was a denylist: every scalar minus a fixed list of sensitive
    ///         names. The client chooses the field name, so a list of what is forbidden covers whatever
    ///         somebody remembered to put in it and exposes the rest — and sorting or filtering on a
    ///         column makes it talk without reading it. On a consumer entity <c>SourceRef</c> was
    ///         nameable and there was no declaration that could take it out.
    ///     </para>
    ///     <para>
    ///         <c>[Filterable]</c> on the property is the declaration. The sensitive list stays behind
    ///         it as a backstop rather than as the policy: declaring <c>[Filterable]</c> on
    ///         <c>TenantId</c> is a mistake an author can make, and the answer must not depend on their
    ///         having read which names are reserved.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<GridFilterBridgePropertyModel> CollectBridgeProperties(
        INamedTypeSymbol entitySymbol,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<GridFilterBridgePropertyModel>();
        var excluded = CollectExcludedNames(entitySymbol);

        // Walk up the type hierarchy to include inherited properties
        var current = entitySymbol;
        while (current is not null)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic || member.IsIndexer || member.GetMethod is null)
                    continue;

                // The allowlist: nothing reaches the client-driven grid without saying so.
                if (!HasFilterable(member))
                    continue;

                // ...and [GridExclude("Name")] on the entity takes one back.
                if (excluded.Contains(member.Name))
                    continue;

                // Skip infrastructure properties
                if (IsExcludedProperty(member.Name))
                    continue;

                // Skip navigation properties (collections and reference types with Id)
                if (IsNavigationProperty(member))
                    continue;

                var model = CreatePropertyModel(member);
                if (model is not null)
                    builder.Add(model);
            }

            current = current.BaseType;
        }

        return builder.ToImmutable();
    }

    private static bool HasFilterable(IPropertySymbol property)
        => property.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == FilterableAttributeName);

    /// <summary>The property names the entity's <c>[GridExclude]</c> attributes take back.</summary>
    private static HashSet<string> CollectExcludedNames(INamedTypeSymbol entitySymbol)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in entitySymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != GridExcludeAttributeName)
                continue;
            // Not a list pattern: it lowers through System.Index, which netstandard2.0 does not have.
            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string property)
            {
                names.Add(property);
            }
        }

        return names;
    }

    private static GridFilterBridgePropertyModel? CreatePropertyModel(IPropertySymbol property)
    {
        var type = property.Type;

        // Unwrap nullable
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        var isString = type.SpecialType == SpecialType.System_String;
        var isBool = type.SpecialType == SpecialType.System_Boolean;
        var isEnum = type.TypeKind == TypeKind.Enum;
        var isComparable = IsComparableType(type);

        // Skip types we can't filter on (complex objects, byte arrays, etc.)
        if (!isString && !isBool && !isEnum && !isComparable)
            return null;

        return new GridFilterBridgePropertyModel
        {
            Name = property.Name,
            TypeName = type.ToDisplayString(),
            IsString = isString,
            IsComparable = isComparable,
            IsEnum = isEnum,
            IsBool = isBool
        };
    }

    private static bool IsComparableType(ITypeSymbol type)
    {
        // Numeric types, dates, GUIDs are comparable
        return type.SpecialType is
                   SpecialType.System_Int16 or
                   SpecialType.System_Int32 or
                   SpecialType.System_Int64 or
                   SpecialType.System_Single or
                   SpecialType.System_Double or
                   SpecialType.System_Decimal or
                   SpecialType.System_Byte or
                   SpecialType.System_DateTime ||
               type.ToDisplayString() is
                   "System.DateTimeOffset" or
                   "System.DateOnly" or
                   "System.TimeOnly" or
                   "System.TimeSpan" or
                   "System.Guid";
    }

    private static bool IsNavigationProperty(IPropertySymbol property)
    {
        var type = property.Type;

        // Collection types
        if (type is INamedTypeSymbol named &&
            named.AllInterfaces.Any(i =>
                i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>" &&
                i.TypeArguments.Length == 1 && (i.TypeArguments[0].TypeKind == TypeKind.Class || i.TypeArguments[0].TypeKind == TypeKind.Interface)))
        {
            return true;
        }

        // Reference navigation: class with Id property
        if (type.TypeKind == TypeKind.Class &&
            type.SpecialType != SpecialType.System_String &&
            type.GetMembers("Id").OfType<IPropertySymbol>().Any())
        {
            return true;
        }

        return false;
    }

    /// <summary>
    ///     The property names the denylist holds back, so the generated switch can name them.
    /// </summary>
    /// <remarks>
    ///     Only the sensitive ones. The audit and soft-delete columns are excluded for tidiness rather
    ///     than for safety, and telling a client "you may not filter on CreatedAt" would be a lie about
    ///     why — they belong with the unknown fields.
    /// </remarks>
    private static ImmutableArray<string> CollectWithheldProperties(
        INamedTypeSymbol entitySymbol,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        var current = entitySymbol;
        while (current is not null)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic || member.IsIndexer || member.GetMethod is null)
                    continue;

                if (SensitiveFieldNames.IsSensitive(member.Name) && !builder.Contains(member.Name))
                    builder.Add(member.Name);
            }

            current = current.BaseType;
        }

        return builder.ToImmutable();
    }

    private static bool IsExcludedProperty(string name)
    {
        // Framework-reserved sensitive columns (credentials / authz such as OwnerId, TenantId,
        // AccessScopes) must never reach the client-driven bridge — this path is the canonical,
        // documented one, so a leak here would be the most exposed. Audit/soft-delete columns are
        // excluded for cleanliness, not security.
        return SensitiveFieldNames.IsSensitive(name) ||
            name is "CreatedAt" or "CreatedBy" or
            "UpdatedAt" or "UpdatedBy" or
            "DeletedAt" or "DeletedBy" or
            "IsDeleted";
    }
}
