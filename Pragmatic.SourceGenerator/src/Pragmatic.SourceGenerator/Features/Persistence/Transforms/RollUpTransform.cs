using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads a <c>[RollUp&lt;TChild&gt;(childProperty)]</c> on a parent property and produces the model used to
///     emit the parent's internal apply method and the typed roll-up rule registration (#2). The child's
///     foreign key is inferred by convention as <c>{Parent}Id</c>.
/// </summary>
internal static class RollUpTransform
{
    private const string RollUpAttributeName = "RollUpAttribute";

    public static RollUpModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not IPropertySymbol property ||
            property.ContainingType is not { } parent)
            return null;

        var attr = context.Attributes.FirstOrDefault(a =>
            a.AttributeClass is { Name: RollUpAttributeName, IsGenericType: true, TypeArguments.Length: 1 });
        if (attr?.AttributeClass?.TypeArguments[0] is not INamedTypeSymbol childType)
            return null;

        if (attr.ConstructorArguments.Length < 1 || attr.ConstructorArguments[0].Value is not string childProperty)
            return null;

        // The second argument: RollUpAggregation.Count = 1.
        // Reading it is the difference between an enum with two members and a feature with two.
        var isCount = attr.ConstructorArguments.Length > 1
                      && attr.ConstructorArguments[1].Value is int aggregation
                      && aggregation == (int)RollUpAggregationKind.Count;

        var ns = parent.ContainingNamespace.IsGlobalNamespace ? "" : parent.ContainingNamespace.ToDisplayString();

        return new RollUpModel
        {
            IsCount = isCount,
            RollupPropertyTypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ParentFullName = parent.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ParentShortName = parent.Name,
            ParentNamespace = ns,
            RollupProperty = property.Name,
            ChildFullName = childType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ChildShortName = childType.Name,
            ChildAmountProperty = childProperty,
            ChildForeignKeyProperty = $"{parent.Name}Id"
        };
    }
}
