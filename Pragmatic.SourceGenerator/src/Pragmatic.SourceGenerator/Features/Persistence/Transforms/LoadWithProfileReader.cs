using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Answers one question about a type: which generated loading profile does its own
///     <c>[LoadWith&lt;T&gt;]</c> produce?
/// </summary>
/// <remarks>
///     <para>
///         Its own file rather than a member of <see cref="LoadingProfileTransform" /> because it is
///         not part of that transform's pipeline: the profile is produced from the attribute, and this
///         is read by <c>QueryTransform</c> so an entity-shaped query can compose the profile's paths
///         into the <c>IncludePaths</c> the query executor applies. Two features, one reader.
///     </para>
/// </remarks>
internal static class LoadWithProfileReader
{
    private const string LoadWithAttributeDisplayName = "Pragmatic.Persistence.Query.LoadWithAttribute<T>";

    /// <summary>
    ///     The fully qualified name of the generated profile class, or <c>null</c> when there is none
    ///     to name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Naming a static of a type this same generator writes in this same compilation is safe;
    ///         naming one it will not write is not. <c>LoadingProfileTemplate.Validate</c> emits nothing
    ///         when the profile would carry no paths, so that case answers <c>null</c> — otherwise the
    ///         query would reference a class that does not exist, a CS0103 inside a file its author
    ///         cannot edit.
    ///     </para>
    ///     <para>
    ///         The paths are counted with the same function the profile renders from, rather than
    ///         re-derived: the two have to agree on emptiness or the guard above is worthless.
    ///     </para>
    /// </remarks>
    public static string? ProfileFor(INamedTypeSymbol declaringType)
    {
        foreach (var attribute in declaringType.GetAttributes())
        {
            if (attribute.AttributeClass is not { IsGenericType: true } attributeClass
                || attributeClass.ConstructedFrom.ToDisplayString() != LoadWithAttributeDisplayName
                || attributeClass.TypeArguments.Length != 1)
            {
                continue;
            }

            var maxDepth = 1;
            foreach (var named in attribute.NamedArguments)
                if (named is { Key: "MaxDepth", Value.Value: int declared })
                    maxDepth = declared;

            if (LoadingProfileTransform.GetAllEntityNavigations(attributeClass.TypeArguments[0], maxDepth).Length == 0)
                return null;

            var profileName = NamingHelper.AppendSuffix(declaringType.Name, "LoadingProfile");
            return declaringType.ContainingNamespace.IsGlobalNamespace
                ? $"global::{profileName}"
                : $"global::{declaringType.ContainingNamespace.ToDisplayString()}.{profileName}";
        }

        return null;
    }
}
