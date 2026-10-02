using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transforms [PolymorphicAttachment] + [Attachable&lt;T&gt;] on entity classes
///     into <see cref="PolymorphicAttachmentModel"/>.
/// </summary>
internal static class PolymorphicAttachmentTransform
{
    public const string PolymorphicAttachmentAttributeName =
        "Pragmatic.Persistence.Entity.PolymorphicAttachmentAttribute";

    private const string AttachableAttributeClassName = "AttachableAttribute";

    public static PolymorphicAttachmentModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var fullTypeName = $"global::{typeSymbol.ToDisplayString()}";

        // Find [Attachable<T>] attributes to discover owner types
        var ownerTypes = ImmutableArray.CreateBuilder<OwnerTypeModel>();

        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (attr.AttributeClass is not { IsGenericType: true })
                continue;

            var def = attr.AttributeClass.OriginalDefinition;
            if (def.Name != AttachableAttributeClassName)
                continue;

            var ownerType = attr.AttributeClass.TypeArguments[0];
            var ownerNs = ownerType is INamedTypeSymbol ownerNamed && !ownerNamed.ContainingNamespace.IsGlobalNamespace
                ? ownerNamed.ContainingNamespace.ToDisplayString()
                : null;

            // Try to find the Id type from the owner's Entity<T> attribute or PersistenceId property
            var ownerIdType = "System.Guid"; // default
            if (ownerType is INamedTypeSymbol ownerSymbol)
            {
                foreach (var ownerAttr in ownerSymbol.GetAttributes())
                {
                    if (ownerAttr.AttributeClass is { IsGenericType: true, OriginalDefinition.Name: "EntityAttribute", TypeArguments.Length: > 0 } ac)
                    {
                        ownerIdType = ac.TypeArguments[0].ToDisplayString();
                        break;
                    }
                }
            }

            ownerTypes.Add(new OwnerTypeModel
            {
                TypeName = ownerType.Name,
                FullTypeName = $"global::{ownerType.ToDisplayString()}",
                Namespace = ownerNs,
                IdType = ownerIdType
            });
        }

        return new PolymorphicAttachmentModel
        {
            Namespace = ns,
            TypeName = typeSymbol.Name,
            FullTypeName = fullTypeName,
            OwnerTypes = ownerTypes.ToImmutable()
        };
    }
}
