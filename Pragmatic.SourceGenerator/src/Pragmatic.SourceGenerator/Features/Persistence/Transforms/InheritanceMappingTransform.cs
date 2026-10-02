using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transforms [Inheritance(Strategy, DiscriminatorColumn)] on entity classes
///     into <see cref="InheritanceMappingModel"/>, discovering derived types in the compilation.
/// </summary>
internal static class InheritanceMappingTransform
{
    public const string InheritanceAttributeName = "Pragmatic.Persistence.Entity.InheritanceAttribute";

    public static InheritanceMappingModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var attr = context.Attributes.FirstOrDefault();
        if (attr is null)
            return null;

        // Strategy is the first constructor argument (enum value)
        if (attr.ConstructorArguments.Length == 0)
            return null;

        var strategyValue = attr.ConstructorArguments[0].Value;
        if (strategyValue is null)
            return null;

        // Map enum ordinal to string
        var strategy = (int)strategyValue switch
        {
            0 => "TPH",
            1 => "TPT",
            2 => "TPC",
            _ => "TPH"
        };

        // DiscriminatorColumn from named argument
        var discriminatorColumn = "Discriminator";
        foreach (var named in attr.NamedArguments)
        {
            if (named is { Key: "DiscriminatorColumn", Value.Value: string col })
                discriminatorColumn = col;
        }

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var baseFullTypeName = $"global::{typeSymbol.ToDisplayString()}";

        // Discover derived types in the compilation
        var derivedTypes = FindDerivedTypes(typeSymbol, context.SemanticModel.Compilation, ct);

        return new InheritanceMappingModel
        {
            Namespace = ns,
            BaseTypeName = typeSymbol.Name,
            BaseFullTypeName = baseFullTypeName,
            Strategy = strategy,
            DiscriminatorColumn = discriminatorColumn,
            DerivedTypes = derivedTypes
        };
    }

    /// <summary>
    ///     Scans the compilation for classes that directly or indirectly derive from the base type.
    /// </summary>
    private static ImmutableArray<DerivedTypeModel> FindDerivedTypes(
        INamedTypeSymbol baseType,
        Compilation compilation,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<DerivedTypeModel>();
        var baseMetadataName = baseType.ToDisplayString();

        // Scan all types in the compilation's global namespace recursively
        ScanNamespace(compilation.GlobalNamespace, baseType, baseMetadataName, builder, ct);

        return builder.ToImmutable();
    }

    private static void ScanNamespace(
        INamespaceSymbol ns,
        INamedTypeSymbol baseType,
        string baseMetadataName,
        ImmutableArray<DerivedTypeModel>.Builder builder,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var member in ns.GetTypeMembers())
        {
            if (member.TypeKind == TypeKind.Class && !SymbolEqualityComparer.Default.Equals(member, baseType))
            {
                if (DerivesFrom(member, baseType))
                {
                    // Check for [Inheritance] DiscriminatorValue on derived type
                    string? discriminatorValue = null;
                    foreach (var attr in member.GetAttributes())
                    {
                        if (attr.AttributeClass?.ToDisplayString() == InheritanceAttributeName)
                        {
                            foreach (var named in attr.NamedArguments)
                            {
                                if (named is { Key: "DiscriminatorValue", Value.Value: string val })
                                    discriminatorValue = val;
                            }
                        }
                    }

                    builder.Add(new DerivedTypeModel
                    {
                        TypeName = member.Name,
                        FullTypeName = $"global::{member.ToDisplayString()}",
                        DiscriminatorValue = discriminatorValue
                    });
                }
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            ScanNamespace(childNs, baseType, baseMetadataName, builder, ct);
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }
}
