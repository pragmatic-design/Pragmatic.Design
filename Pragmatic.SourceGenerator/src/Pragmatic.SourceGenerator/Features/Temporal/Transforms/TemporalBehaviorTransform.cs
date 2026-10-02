using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Temporal.Models;

namespace Pragmatic.SourceGenerator.Features.Temporal.Transforms;

/// <summary>
///     Extracts a <see cref="TemporalBehaviorPropertyModel" /> from a property annotated with
///     one of the six timezone conversion attributes. Parameter targets are handled at runtime
///     by the model binder and are not part of this pipeline.
/// </summary>
internal static class TemporalBehaviorTransform
{
    public static TemporalBehaviorPropertyModel? Transform(GeneratorAttributeSyntaxContext context, string behavior)
    {
        if (context.TargetSymbol is not IPropertySymbol property)
            return null;

        var containingType = property.ContainingType;
        if (containingType is null)
            return null;

        var type = property.Type;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        var isSupported = type.SpecialType == SpecialType.System_DateTime
                          || (type.Name == "DateTimeOffset"
                              && type.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true });

        return new TemporalBehaviorPropertyModel
        {
            ContainingTypeFqn = containingType.ToDisplayString(),
            ContainingNamespace = containingType.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : "",
            PropertyName = property.Name,
            Behavior = behavior,
            IsSupportedPropertyType = isSupported,
            PropertyTypeDisplay = property.Type.ToDisplayString(),
            // The attribute's own position, not the property's: it is what the author would
            // delete or move, and it is where the IDE should land.
            Location = LocationInfo.From(
                context.Attributes[0].ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? property.Locations.FirstOrDefault())
        };
    }
}
