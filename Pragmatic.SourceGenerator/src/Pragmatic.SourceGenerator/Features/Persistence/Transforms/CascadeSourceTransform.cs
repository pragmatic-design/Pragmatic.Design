using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transforms a property with [CascadeSource] into a <see cref="CascadeSourceInfo"/>.
///     Used for cross-project cascade scenarios where the source entity and cascade target
///     live in different assemblies.
/// </summary>
internal static class CascadeSourceTransform
{
    public const string CascadeSourceAttributeName = "Pragmatic.Persistence.Entity.CascadeSourceAttribute";

    public static CascadeSourceInfo? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetNode is not PropertyDeclarationSyntax)
            return null;

        if (context.TargetSymbol is not IPropertySymbol property)
            return null;

        var containingType = property.ContainingType;
        if (containingType is null)
            return null;

        return new CascadeSourceInfo
        {
            // Must match EntityMetadataModel.FullTypeName format (no global:: prefix)
            EntityFullTypeName = containingType.ToDisplayString(),
            PropertyName = property.Name
        };
    }
}
