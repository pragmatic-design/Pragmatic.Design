using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Transforms;

/// <summary>
///     The <c>[PartitionKey]</c> declarations of the message types <b>this compilation declares</b> —
///     read from the type's property symbols, so the positional and the body-declared forms are the
///     same thing.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Why a symbol scan and not <c>ForAttributeWithMetadataName</c>.</b> FAWMN does not
///         surface <c>[property: PartitionKey]</c> on a positional record parameter — measured with the
///         predicate widened to <c>ParameterSyntax</c> and the transform taught to map a parameter
///         symbol back to its property, and neither reached it. The synthesized property carries the
///         attribute, so <c>GetMembers()</c> sees it; this is the technique <see cref="SagaTransform"/>
///         uses for <c>[CorrelationKey]</c>, so two attributes written on one parameter behave alike.
///     </para>
///     <para>
///         ⚠️ <b>Declared here, not visible from here.</b> The scan is deliberately over this
///         compilation's own declarations: <c>TransportAwareMessageBus</c> resolves
///         <c>IPartitionKeyResolver</c> from the container of the host that <em>publishes</em>, and a
///         contract lives in an assembly the publisher references. A scan from a <c>[MessageHandler]</c>
///         fires only in the assembly that <em>consumes</em> — it would generate the resolver in the one
///         place that cannot use it, and not in the one that needs it.
///     </para>
///     <para>
///         The syntax pre-filter asks only for an attribute written somewhere a partition key can be
///         written, so the semantic model is never consulted for the types that carry no attributes at
///         all — the same trade <see cref="DeclaredDomainEventTransform"/> makes with its base list.
///     </para>
/// </remarks>
internal static class DeclaredPartitionKeyTransform
{
    /// <summary>The predicate: a type declaration with an attribute on a parameter or on a property.</summary>
    public static bool CouldDeclareAPartitionKey(SyntaxNode node, CancellationToken _)
    {
        if (node is not TypeDeclarationSyntax declaration)
            return false;

        if (declaration is RecordDeclarationSyntax { ParameterList: { } parameters }
            && parameters.Parameters.Any(p => p.AttributeLists.Count > 0))
            return true;

        return declaration.Members.Any(m => m is PropertyDeclarationSyntax { AttributeLists.Count: > 0 });
    }

    /// <summary>
    ///     Every partition key the type declares, or an empty array when it declares none. All of them
    ///     and not the first: a type with two is a mistake the generator reports (PRAG0819), and it can
    ///     only report what it was told about.
    /// </summary>
    public static EquatableArray<PartitionKeyModel> Transform(GeneratorSyntaxContext context, CancellationToken _)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol messageType)
            return EquatableArray<PartitionKeyModel>.Empty;

        var keys = ImmutableArray.CreateBuilder<PartitionKeyModel>();
        foreach (var property in messageType.GetMembers().OfType<IPropertySymbol>())
        {
            // The one spelling of the name, from AttributeNames, so a grep for the attribute finds
            // this reader too.
            if (!property.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == AttributeNames.PartitionKey))
                continue;

            var propertyType = property.Type;
            keys.Add(new PartitionKeyModel
            {
                MessageTypeFqn = messageType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                PropertyName = property.Name,
                IsString = propertyType.SpecialType == SpecialType.System_String,
                IsNullable = propertyType.NullableAnnotation == NullableAnnotation.Annotated
                    || propertyType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T,
                AssemblyName = messageType.ContainingAssembly?.Name ?? "",
                LocationInfo = LocationInfo.From(property.Locations.Length > 0 ? property.Locations[0] : null),
            });
        }

        return keys.ToImmutable();
    }
}
