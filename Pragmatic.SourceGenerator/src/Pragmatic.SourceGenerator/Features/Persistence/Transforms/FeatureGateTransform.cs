using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>Reads the <c>[RequiresFeature]</c> attributes of an operation, named and generic.</summary>
internal static class FeatureGateTransform
{
    /// <summary>Every flag the operation requires, in declaration order.</summary>
    public static ImmutableArray<FeatureGateModel> Extract(INamedTypeSymbol operation)
    {
        var gates = ImmutableArray.CreateBuilder<FeatureGateModel>();

        foreach (var attribute in operation.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass
                || attributeClass.OriginalDefinition.ToDisplayString() is not { } name
                || !name.StartsWith(Pragmatic.SourceGen.AttributeNames.RequiresFeature, System.StringComparison.Ordinal))
                continue;

            if (attributeClass.TypeArguments.Length == 1)
            {
                gates.Add(new FeatureGateModel
                {
                    FlagTypeFullName = attributeClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                });
            }
            else if (attribute.ConstructorArguments.Length == 1
                     && attribute.ConstructorArguments[0].Value is string { Length: > 0 } flagName)
            {
                gates.Add(new FeatureGateModel { FlagName = flagName });
            }
        }

        return gates.ToImmutable();
    }
}
