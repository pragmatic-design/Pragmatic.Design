using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Analyzers;

/// <summary>
///     Shared entity detection logic used by all Pragmatic.Persistence analyzers.
///     Determines whether a type is decorated with [Entity] from the Pragmatic.Persistence.Entity
///     namespace, including inherited attributes.
/// </summary>
internal static class EntityFilter
{
    private const string EntityAttributeNamespace = "Pragmatic.Persistence.Entity";
    private const string EntityAttributeName = "EntityAttribute";

    /// <summary>
    ///     Checks whether the type or any of its base types has [Entity].
    ///     Walks the inheritance chain to handle TPH and derived entity classes.
    /// </summary>
    public static bool IsTarget(INamedTypeSymbol? type)
    {
        var current = type;
        while (current != null)
        {
            foreach (var attribute in current.GetAttributes())
            {
                var attrClass = attribute.AttributeClass;
                if (attrClass is null)
                    continue;

                if (attrClass.Name != EntityAttributeName)
                    continue;

                var ns = attrClass.ContainingNamespace?.ToDisplayString();
                if (ns == EntityAttributeNamespace)
                    return true;
            }

            current = current.BaseType;
        }

        return false;
    }
}
