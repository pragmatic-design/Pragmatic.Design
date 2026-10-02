using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Resource.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Transforms;

/// <summary>
/// Extracts entity properties from an INamedTypeSymbol and builds a <see cref="ResourceCrudModel"/>.
/// </summary>
internal static class ResourceCrudTransform
{
    private static readonly HashSet<string> AuditPropertyNames = new(StringComparer.Ordinal)
    {
        "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy",
        "ModifiedAt", "ModifiedBy",
    };

    private static readonly HashSet<string> SoftDeletePropertyNames = new(StringComparer.Ordinal)
    {
        "IsDeleted", "DeletedAt", "DeletedBy",
    };

    private static readonly HashSet<string> OwnershipPropertyNames = new(StringComparer.Ordinal)
    {
        "OwnerId", "AccessScopes", "TenantId",
    };

    private static readonly HashSet<string> InfraPropertyNames = new(StringComparer.Ordinal)
    {
        "PersistenceId", "DomainEvents", "ModifiedProperties", "RowVersion",
    };

    public static ResourceCrudModel? Build(
        ResourceModel resource,
        Compilation compilation,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? operationNamespaceByEntity = null)
    {
        var entitySymbol = compilation.GetTypeByMetadataName(
            resource.FullTypeName.Replace("global::", ""));
        if (entitySymbol is null) return null;

        var properties = ExtractProperties(entitySymbol, resource.IdType);
        if (properties.IsEmpty) return null;

        var (logicKeyName, logicKeyType) = ExtractLogicKey(entitySymbol);

        return new ResourceCrudModel
        {
            Resource = resource,
            Properties = properties,
            LogicKeyName = logicKeyName,
            LogicKeyType = logicKeyType,
            IsSoftDelete = HasSoftDelete(entitySymbol),
            IsConcurrencyAware = HasConcurrencyToken(entitySymbol),
            GroupName = GroupOf(resource, operationNamespaceByEntity),
        };
    }

    /// <summary>
    ///     The group of the entity's hand-written operations, read through the same inference the
    ///     boundary uses for them.
    /// </summary>
    /// <remarks>
    ///     One rule, one place: the namespace of an operation the author wrote, put through
    ///     <c>SubBoundaryTransform</c> against the boundary's namespace. A second rule here — the
    ///     folder, the plural of the entity — would be a group that agrees today and diverges the first
    ///     time someone moves a file.
    /// </remarks>
    private static string? GroupOf(
        ResourceModel resource,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? operationNamespaceByEntity)
    {
        if (operationNamespaceByEntity is null)
            return null;

        var entity = resource.FullTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? resource.FullTypeName.Substring(8)
            : resource.FullTypeName;

        if (!operationNamespaceByEntity.TryGetValue(entity, out var operationNamespace))
            return null;

        var boundary = resource.BoundaryFullTypeName;
        if (string.IsNullOrEmpty(boundary))
            return null;

        var plain = boundary!.StartsWith("global::", StringComparison.Ordinal)
            ? boundary.Substring(8)
            : boundary;
        var lastDot = plain.LastIndexOf('.');
        if (lastDot <= 0)
            return null;

        return Actions.Transforms.SubBoundaryTransform.InferSubBoundary(
            plain.Substring(0, lastDot), operationNamespace);
    }

    /// <summary>
    ///     Whether the entity declares <c>[SoftDelete]</c>, which is what makes Restore meaningful.
    /// </summary>
    /// <remarks>
    ///     The <b>attribute</b>, not the <c>ISoftDelete</c> interface: that interface is added by a
    ///     partial this generator emits, so it does not exist in the compilation being analysed. Asking
    ///     for it instead is how <c>[SoftDelete(Cascade = true)]</c> stayed inert for months.
    /// </remarks>
    private static bool HasSoftDelete(INamedTypeSymbol entitySymbol)
    {
        for (var current = entitySymbol; current is not null; current = current.BaseType)
        {
            if (current.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == "Pragmatic.Persistence.Entity.SoftDeleteAttribute"))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether the entity carries <c>[ConcurrencyAware]</c>, walking the base chain like
    ///     <see cref="HasSoftDelete"/>.
    /// </summary>
    private static bool HasConcurrencyToken(INamedTypeSymbol entitySymbol)
    {
        for (var current = entitySymbol; current is not null; current = current.BaseType)
        {
            if (current.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString()
                    == "Pragmatic.Persistence.Entity.ConcurrencyAwareAttribute"))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The entity's domain key, when it is a single property and can therefore address a row
    ///     through one route segment.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[LogicKey]</c> only, not <c>[GeneratedValue]</c> as a synonym: they are not the same
    ///         thing. A generated value is one this generator <i>produces</i> from a format —
    ///         <c>ORD-{YYYY}{MM}-{SEQ:5}</c> — while the logic key is what the domain says makes a row
    ///         that row, and the logic key is the domain key.
    ///     </para>
    ///     <para>
    ///         A domain key spanning more than one property scaffolds no by-key read: there is no single
    ///         path segment to address it with, and inventing a multi-segment route shape is a decision
    ///         nobody has asked for. Write the query if you want it — the repository and the
    ///         specification address the composite key already.
    ///     </para>
    /// </remarks>
    private static (string? name, string? type) ExtractLogicKey(INamedTypeSymbol entitySymbol)
    {
        (string Name, string Type)? single = null;

        foreach (var member in entitySymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;

            foreach (var attr in prop.GetAttributes())
            {
                if (attr.AttributeClass?.Name != "LogicKeyAttribute")
                    continue;

                if (single is not null)
                    return (null, null);

                single = (prop.Name, SimplifyType(prop.Type.ToDisplayString()));
            }
        }

        return single is { } key ? (key.Name, key.Type) : (null, null);
    }

    private static ImmutableArray<ResourcePropertyInfo> ExtractProperties(
        INamedTypeSymbol entitySymbol, string idType)
    {
        var builder = ImmutableArray.CreateBuilder<ResourcePropertyInfo>();

        // De-duplicate by property name. Walking from the most-derived type upward means the
        // first occurrence is the most-derived declaration; overrides/new in derived types win,
        // and the same property re-declared on a base type is skipped.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Walk the type hierarchy to collect all properties
        var current = entitySymbol;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.IsStatic || prop.IsIndexer) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (InfraPropertyNames.Contains(prop.Name)) continue;
                if (!seen.Add(prop.Name)) continue;

                // Skip collections and complex navigation properties
                if (IsCollectionType(prop.Type)) continue;

                var typeName = prop.Type.ToDisplayString();
                var isNavigation = IsNavigationType(prop);
                if (isNavigation) continue; // Skip navigation properties entirely

                var isPk = prop.Name == "Id";
                var isAudit = AuditPropertyNames.Contains(prop.Name);
                var isSoftDelete = SoftDeletePropertyNames.Contains(prop.Name);
                var isOwnership = OwnershipPropertyNames.Contains(prop.Name);
                var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                                 prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

                var isRequired = !isNullable && !isPk && !isAudit && !isSoftDelete && !isOwnership
                                 && prop.SetMethod is not null;

                builder.Add(new ResourcePropertyInfo
                {
                    Name = prop.Name,
                    TypeName = SimplifyType(typeName),
                    IsNullable = isNullable,
                    IsRequired = isRequired,
                    IsPrimaryKey = isPk,
                    IsAudit = isAudit,
                    IsSoftDelete = isSoftDelete,
                    IsOwnership = isOwnership,
                    IsNavigation = false,
                });
            }

            current = current.BaseType;
            // Stop at framework base types
            if (current?.Name is "Object" or "DomainEventSource") break;
        }

        return builder.ToImmutable();
    }

    // Recognised by shape, not by a name whitelist: with a whitelist, Dictionary<,>, T[],
    // ImmutableArray<T>, IReadOnlySet<T> and custom collections would slip through and land in the
    // generated DTOs as if they were scalars.
    private static bool IsCollectionType(ITypeSymbol type) => CollectionTypeHelper.IsCollection(type);

    private static bool IsNavigationType(IPropertySymbol prop)
    {
        var type = prop.Type;
        if (type.TypeKind == TypeKind.Interface) return false;

        // If it's a class type (not string, not primitive), it's likely a navigation
        if (type is INamedTypeSymbol named && named.TypeKind == TypeKind.Class)
        {
            var specialType = named.SpecialType;
            if (specialType == SpecialType.System_String) return false;
            if (specialType == SpecialType.System_Object) return false;

            // Check if it looks like an entity (has Id property)
            foreach (var m in named.GetMembers())
            {
                if (m is IPropertySymbol { Name: "Id" or "PersistenceId" })
                    return true;
            }
        }

        return false;
    }

    private static string SimplifyType(string typeName)
    {
        return typeName
            .Replace("System.", "")
            .Replace("global::", "");
    }
}
