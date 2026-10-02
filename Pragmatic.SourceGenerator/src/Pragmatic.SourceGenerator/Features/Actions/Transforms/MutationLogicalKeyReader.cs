using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     The parts of an entity's <c>[LogicKey]</c>, typed, for a mutation that returns them.
/// </summary>
/// <remarks>
///     <para>
///         Which properties form the key, and in which order, is decided once, by
///         <c>EntityTransform.CollectLogicKeys</c> — the same reading the unique index and the
///         repository lookup come from. This only types each part for a record: a declared property by
///         its symbol, a key one of the entity's own relations generates by the type that relation
///         gives it.
///     </para>
///     <para>
///         A part that is neither — a key a relation declared on the other entity puts here — keeps an
///         empty type. Only the relation graph, which runs over every entity at once, knows it; the
///         mutation reports PRAG0403 rather than guess.
///     </para>
/// </remarks>
internal static class MutationLogicalKeyReader
{
    /// <summary>The parts, in key order, as the pipeline carries them.</summary>
    public static ImmutableArray<MutationKeyPartModel> Read(INamedTypeSymbol entity, Compilation compilation)
        => ReadTyped(entity, compilation).Select(p => p.Part).ToImmutableArray();

    /// <summary>
    ///     The parts with their symbols, for the analysis that needs them in the same pass (the JSON
    ///     shape). Never stored in a model: a symbol does not belong in the incremental pipeline.
    /// </summary>
    public static ImmutableArray<(MutationKeyPartModel Part, ITypeSymbol? Type)> ReadTyped(
        INamedTypeSymbol entity, Compilation compilation)
    {
        var parts = Persistence.Transforms.EntityTransform.CollectLogicKeys(entity);
        if (parts.Length == 0)
            return ImmutableArray<(MutationKeyPartModel, ITypeSymbol?)>.Empty;

        var builder = ImmutableArray.CreateBuilder<(MutationKeyPartModel, ITypeSymbol?)>(parts.Length);
        foreach (var part in parts)
        {
            if (DeclaredProperty(entity, part.Name) is { } property)
            {
                builder.Add((new MutationKeyPartModel(
                        part.Name,
                        property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        JsonNameOf(property)),
                    property.Type));
                continue;
            }

            var ownKey = Core.TraitPropertyResolver.GetRelationForeignKeys(entity)
                .FirstOrDefault(k => k.Name == part.Name);
            if (ownKey.Name is not null && ResolveKeyType(ownKey.TypeFullName, compilation) is { } keyType)
            {
                builder.Add((new MutationKeyPartModel(
                        part.Name, keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                    keyType));
                continue;
            }

            builder.Add((new MutationKeyPartModel(part.Name, ""), null));
        }

        return builder.ToImmutable();
    }

    private static string? JsonNameOf(IPropertySymbol property)
        => property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString()
                                 == "System.Text.Json.Serialization.JsonPropertyNameAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;

    private static IPropertySymbol? DeclaredProperty(INamedTypeSymbol entity, string name)
    {
        for (var current = entity; current is not null; current = current.BaseType)
            foreach (var member in current.GetMembers(name))
                if (member is IPropertySymbol { IsStatic: false, IsIndexer: false } property)
                    return property;

        return null;
    }

    /// <summary>A generated key's type: <c>System.Guid</c>, or <c>System.Guid?</c> for an optional relation.</summary>
    private static ITypeSymbol? ResolveKeyType(string typeName, Compilation compilation)
    {
        var nullable = typeName.EndsWith("?", System.StringComparison.Ordinal);
        var metadataName = nullable ? typeName.Substring(0, typeName.Length - 1) : typeName;
        if (compilation.GetTypeByMetadataName(metadataName) is not { } type)
            return null;

        return nullable
            ? compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(type)
            : type;
    }
}
