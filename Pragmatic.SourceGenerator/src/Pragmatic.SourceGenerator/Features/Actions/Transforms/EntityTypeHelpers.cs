using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static class EntityTypeHelpers
{
    private const string EntityAttributes = "Pragmatic.Persistence.Entity";

    /// <summary>
    ///     The type of the entity's identifier.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>IEntity</c> is accepted too because an entity from a referenced assembly
    ///         carries it: the partial that adds it was compiled there.
    ///     </para>
    ///     <para>
    ///         In the compilation that declares the entity it is not there yet — the generator adds it
    ///         — so reading only the interface would answer null for every entity whose author has not
    ///         also written <c>: IEntity</c> by hand. The consequence would be silent and total:
    ///         <c>LoadEntityAsync</c> generated as <c>Task.FromResult&lt;TEntity?&gt;(null)</c>, so
    ///         the operation matches nothing on every call, and <c>PRAG0435</c> standing down precisely
    ///         because it has no id type to check against. Falling back to the presence of
    ///         <c>[Entity]</c> answers it — the same reason the soft-delete cascade reads
    ///         <c>[SoftDelete]</c> and not <c>ISoftDelete</c>.
    ///     </para>
    /// </remarks>
    public static ITypeSymbol? GetEntityKeyType(ITypeSymbol entityType, Compilation compilation)
    {
        var isEntity = IsEntity(entityType)
                       || entityType.AllInterfaces.Any(i => i.Name == "IEntity");

        return isEntity ? compilation.GetTypeByMetadataName("System.Guid") : null;
    }

    /// <summary>
    ///     Whether the type — or a base of it — is marked <c>[Entity]</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It reads no identifier out of the attribute: every entity is keyed by a <c>Guid</c>. What
    ///     it answers is the question the caller actually needs answered — is this an entity — because
    ///     <see cref="GetEntityKeyType" /> returning null is not "unknown key type", it is "not an
    ///     entity", and the difference is load-bearing: a null answer for a real entity would generate
    ///     <c>LoadEntityAsync</c> as <c>Task.FromResult&lt;TEntity?&gt;(null)</c>, matching nothing on
    ///     every call, with <c>PRAG0435</c> standing down because it has no type to check.
    /// </remarks>
    private static bool IsEntity(ITypeSymbol entityType)
    {
        for (var current = entityType as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass is { Name: "EntityAttribute" } declaration
                    && declaration.ContainingNamespace?.ToDisplayString() == EntityAttributes)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
