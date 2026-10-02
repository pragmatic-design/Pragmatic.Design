using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Traits.Transforms;

/// <summary>
///     Detects a trait attribute applied to a class that is not an entity.
/// </summary>
/// <remarks>
///     Every trait transform gives up and returns <c>null</c> when the annotated type has no
///     <c>[Entity]</c>, because there is no id type to hang the child entity off. Left alone, that
///     path is silent: the attribute compiles, nothing is generated, and nothing says why.
///     The trait transforms cannot report it themselves — they return no model, so there is nothing
///     left to iterate over downstream — hence this separate pass, whose only job is to find the
///     types the others dropped.
/// </remarks>
internal static class MissingEntityTraitTransform
{
    /// <summary>The annotated type plus the trait that was applied to it, for diagnostic reporting.</summary>
    /// <param name="TypeName">The annotated type.</param>
    /// <param name="TraitName">The trait attribute that was applied.</param>
    /// <param name="Location">Where the type is declared.</param>
    /// <param name="CollidingMember">
    ///     Set when the parent already declares the member the trait's navigation would add. Null when
    ///     the problem is the missing [Entity] attribute instead.
    /// </param>
    internal readonly record struct MissingEntity(
        string TypeName, string TraitName, LocationInfo? Location, string? CollidingMember = null);

    /// <summary>Navigation property each trait adds to the annotated type.</summary>
    private static string NavigationFor(string traitName) => traitName switch
    {
        "HasComments" => "Comments",
        "HasTags" => "Tags",
        "HasNotes" => "Notes",
        "HasAttachments" => "Attachments",
        _ => "",
    };

    public static MissingEntity? Transform(
        GeneratorAttributeSyntaxContext context, string traitName, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
            return null;

        var location = LocationInfo.From(type.Locations.Length > 0 ? type.Locations[0] : null);

        if (!IsEntity(type, context.SemanticModel.Compilation))
            return new MissingEntity(type.Name, traitName, location);

        // The navigation is added to a partial of the annotated type, so an existing member of the
        // same name is a CS0102 raised inside generated code — which names neither the trait nor the
        // member the developer has to rename.
        var navigation = NavigationFor(traitName);
        if (navigation.Length > 0 && type.GetMembers(navigation).Length > 0)
            return new MissingEntity(type.Name, traitName, location, navigation);

        return null;
    }

    /// <summary>
    ///     The same question the trait transforms ask, through the same function, so the two cannot
    ///     disagree. Two separate questions can disagree in both directions: a type the trait
    ///     transforms accept could still fail the build on PRAG2600, and a type they reject could
    ///     silence the diagnostic while producing nothing.
    /// </summary>
    private static bool IsEntity(INamedTypeSymbol type, Compilation compilation)
        => Actions.Transforms.EntityTypeHelpers.GetEntityKeyType(type, compilation) is not null;
}
