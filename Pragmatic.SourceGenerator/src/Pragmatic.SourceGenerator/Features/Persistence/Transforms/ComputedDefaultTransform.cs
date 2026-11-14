using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads <c>[ComputedDefault&lt;TEntity, TValue, TGenerator&gt;]</c> on an entity property into a <see cref="ComputedDefaultModel" />.
/// </summary>
/// <remarks>
///     Driven by the property, as <c>[GeneratedValue]</c> is (#740): it was read by the create mutation's invoker, so an entity added any other
///     way (a domain action, an import, a handler) reached the database with the property empty, and the generator, registered as a singleton,
///     could not take a scoped service.
/// </remarks>
internal static class ComputedDefaultTransform
{
    public const string ComputedDefaultAttributeName = "Pragmatic.Persistence.Entity.ComputedDefaultAttribute`3";

    public static ComputedDefaultModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not IPropertySymbol { ContainingType: { } entity } property
            || context.Attributes.Length == 0
            || context.Attributes[0].AttributeClass is not { TypeArguments.Length: 3 } attribute)
            return null;

        return new ComputedDefaultModel
        {
            EntityFqn = entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace = entity.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty,
            PropertyName = property.Name,
            SetterName = property.SetMethod?.DeclaredAccessibility == Accessibility.Public ? property.Name : $"Set{property.Name}",
            ValueTypeFqn = attribute.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            GeneratorTypeFqn = attribute.TypeArguments[2].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsString = attribute.TypeArguments[1].SpecialType == SpecialType.System_String,
        };
    }
}
