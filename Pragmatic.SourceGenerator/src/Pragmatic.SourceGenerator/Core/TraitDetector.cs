using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
/// Scans all attributes on an entity <see cref="INamedTypeSymbol"/> and produces
/// a <see cref="TraitSet"/> snapshot. Each feature calls this independently.
/// </summary>
internal static class TraitDetector
{
    private const string TenantEntityInterface = "Pragmatic.MultiTenancy.ITenantEntity";

    /// <summary>
    /// Detects all traits on the given entity type symbol.
    /// </summary>
    public static TraitSet Detect(INamedTypeSymbol entitySymbol)
    {
        var isAuditable = false;
        var isSoftDelete = false;
        var isSoftDeleteCascade = false;
        var isConcurrencyAware = false;
        var isOwnedEntity = false;
        var isScopedEntity = false;
        var hasComments = false;
        var hasTags = false;
        var hasNotes = false;
        var hasAttachments = false;

        foreach (var attr in entitySymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null) continue;

            // HasNotesAttribute is generic — match by name + namespace instead of ToDisplayString()
            if (attrClass.Name == "HasNotesAttribute" &&
                attrClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Notes")
            {
                hasNotes = true;
                continue;
            }

            var fqn = attrClass.ToDisplayString();

            switch (fqn)
            {
                case AttributeNames.PersistenceAuditable:
                    isAuditable = true;
                    break;

                case AttributeNames.SoftDelete:
                    isSoftDelete = true;
                    isSoftDeleteCascade = GetBoolNamedArg(attr, "Cascade");
                    break;

                case AttributeNames.ConcurrencyAware:
                    isConcurrencyAware = true;
                    break;

                case AttributeNames.HasOwner:
                    isOwnedEntity = true;
                    break;

                case AttributeNames.HasAccessScopes:
                    isScopedEntity = true;
                    break;

                case AttributeNames.HasComments:
                    hasComments = true;
                    break;

                case AttributeNames.HasTags:
                    hasTags = true;
                    break;

                case AttributeNames.HasAttachments:
                    hasAttachments = true;
                    break;
            }
        }

        // Multi-tenant detection via interface implementation
        var isMultiTenant = ImplementsInterface(entitySymbol, TenantEntityInterface);

        return new TraitSet
        {
            IsAuditable = isAuditable,
            IsSoftDelete = isSoftDelete,
            IsSoftDeleteCascade = isSoftDeleteCascade,
            IsConcurrencyAware = isConcurrencyAware,
            IsMultiTenant = isMultiTenant,
            IsOwnedEntity = isOwnedEntity,
            IsScopedEntity = isScopedEntity,
            HasComments = hasComments,
            HasTags = hasTags,
            HasNotes = hasNotes,
            HasAttachments = hasAttachments,
        };
    }





    private static bool GetBoolNamedArg(AttributeData attr, string name)
    {
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key == name && namedArg.Value.Value is bool value)
                return value;
        }

        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol symbol, string interfaceFqn)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.ToDisplayString() == interfaceFqn)
                return true;
        }

        return false;
    }
}
