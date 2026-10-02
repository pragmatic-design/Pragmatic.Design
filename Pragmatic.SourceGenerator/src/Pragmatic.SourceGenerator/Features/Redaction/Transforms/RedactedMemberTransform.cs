using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Redaction.Models;

namespace Pragmatic.SourceGenerator.Features.Redaction.Transforms;

/// <summary>
///     Reads one marked property into a <see cref="RedactedMemberModel" />.
/// </summary>
/// <remarks>
///     Driven by the attribute, not by the kind of type that carries it. That is the whole point of
///     this feature: the previous map was emitted for message types only, so a mutation input with a
///     marked member was never covered, and enumerating the kinds — message, action, entity — would
///     only postpone the same gap to the next kind.
/// </remarks>
internal static class RedactedMemberTransform
{
    /// <summary>Reads a <c>[NotLogged]</c> property.</summary>
    public static RedactedMemberModel? NotLogged(GeneratorAttributeSyntaxContext ctx, CancellationToken _)
        => Read(ctx, "global::Pragmatic.Serialization.RedactionReason.NotLogged", category: null);

    /// <summary>Reads a <c>[PersonalData(category)]</c> property, keeping the category.</summary>
    public static RedactedMemberModel? PersonalData(GeneratorAttributeSyntaxContext ctx, CancellationToken _)
    {
        var category = ctx.Attributes
            .Where(a => a.ConstructorArguments.Length > 0)
            .Select(a => CategoryName(a.ConstructorArguments[0]))
            .FirstOrDefault(v => v is not null);

        return Read(ctx, "global::Pragmatic.Serialization.RedactionReason.PersonalData", category);
    }

    /// <summary>The category name, from the spelling both passes of this feature share.</summary>
    private static string? CategoryName(TypedConstant constant) => RedactionNaming.CategoryName(constant);

    private static RedactedMemberModel? Read(GeneratorAttributeSyntaxContext ctx, string reason, string? category)
    {
        var property = AsProperty(ctx.TargetSymbol);
        if (property is null || property.ContainingType is not { } owner)
            return null;

        return new RedactedMemberModel(
            owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SerializedName(property),
            reason,
            category);
    }

    /// <summary>
    ///     Every marked property of a positional record, read from the type symbol.
    /// </summary>
    /// <remarks>
    ///     The declaration syntax route misses these; see the note in RedactionFeature. Reading the
    ///     type gives the properties the compiler generated from the parameters, and those DO carry
    ///     the attribute — <c>[property: ...]</c> is what puts it there.
    /// </remarks>
    public static ImmutableArray<RedactedMemberModel> FromRecordDeclaration(
        GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol type)
            return ImmutableArray<RedactedMemberModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<RedactedMemberModel>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            foreach (var attribute in property.GetAttributes())
            {
                var name = attribute.AttributeClass?.Name;
                var ns = attribute.AttributeClass?.ContainingNamespace?.ToDisplayString();

                if (name == "NotLoggedAttribute" && ns == "Pragmatic")
                {
                    builder.Add(Model(type, property, "global::Pragmatic.Serialization.RedactionReason.NotLogged", null));
                }
                else if (name == "PersonalDataAttribute" && ns == "Pragmatic.Privacy")
                {
                    var category = attribute.ConstructorArguments.Length > 0
                        ? CategoryName(attribute.ConstructorArguments[0])
                        : null;
                    builder.Add(Model(type, property, "global::Pragmatic.Serialization.RedactionReason.PersonalData", category));
                }
            }
        }

        return builder.ToImmutable();
    }

    private static RedactedMemberModel Model(INamedTypeSymbol owner, IPropertySymbol property, string reason, string? category)
        => new(owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), SerializedName(property), reason, category);

    /// <summary>
    ///     The property the attribute ends up on.
    /// </summary>
    /// <remarks>
    ///     A positional record declares its properties as primary-constructor parameters, and
    ///     <c>[property: NotLogged]</c> there is reported with the PARAMETER as the target symbol,
    ///     not the property it generates. Resolving it by name off the containing type is what makes
    ///     <c>record LoginAttempted(string Email, [property: NotLogged] string Password)</c> work —
    ///     the shape an existing test caught when this feature replaced the message-type scan.
    /// </remarks>
    private static IPropertySymbol? AsProperty(ISymbol symbol) => symbol switch
    {
        IPropertySymbol property => property,
        IParameterSymbol { ContainingType: { } owner } parameter =>
            owner.GetMembers(parameter.Name).OfType<IPropertySymbol>().FirstOrDefault(),
        _ => null,
    };

    /// <summary>The serialized name, from the spelling both passes of this feature share.</summary>
    private static string SerializedName(IPropertySymbol property) => RedactionNaming.SerializedName(property);
}
