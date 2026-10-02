using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     What the generated local identity store needs to know about a user entity.
/// </summary>
internal static partial class EntityTransform
{
    private const string LocalIdentityTypeName = "Pragmatic.Identity.Local.LocalIdentity";
    private const string SelfRegisteringUserTypeName = "Pragmatic.Identity.Local.ISelfRegisteringUser<TSelf>";

    /// <summary>
    ///     The property of a <c>[PragmaticUser]</c> entity whose type is <c>LocalIdentity</c>, or null.
    /// </summary>
    /// <remarks>
    ///     Read here, by Persistence, and not by the Identity feature: the store it drives saves through the
    ///     boundary's unit of work, and the boundary is this transform's to resolve.
    /// </remarks>
    private static string? LocalIdentityPropertyOf(INamedTypeSymbol type)
    {
        if (!type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeNames.PragmaticUser))
            return null;

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic)
                continue;

            if (property.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString() == LocalIdentityTypeName)
                return property.Name;
        }

        return null;
    }

    /// <summary>The entity declares how a user is created from a local identity.</summary>
    private static bool RegistersFromLocalIdentity(INamedTypeSymbol type)
        => type.AllInterfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString() == SelfRegisteringUserTypeName
            && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type));
}
