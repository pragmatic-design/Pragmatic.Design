using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;
using Pragmatic.SourceGenerator.Features.Serialization.Analysis;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

internal static partial class EndpointTransform
{
    /// <summary>The record the handler template emits for a <c>ReturnType = Id</c> mutation.</summary>
    internal const string IdResponseRecordName = "IdResponse";

    /// <summary>What a mutation endpoint answers with in place of the entity.</summary>
    /// <param name="TypeName">The record's fully qualified name.</param>
    /// <param name="ReturnsId">Whether it is the Id record rather than the logical key.</param>
    /// <param name="Properties">Its properties, for the manifest.</param>
    /// <param name="Json">Its shape, for the generated JSON context.</param>
    private sealed record MutationKeyResponse(
        string TypeName,
        bool ReturnsId,
        ImmutableArray<MutationKeyPartModel> Properties,
        JsonRootContribution? Json);

    /// <summary>
    ///     Reads <c>[Mutation(ReturnType = …)]</c>: <c>null</c> for the entity, which is the default.
    /// </summary>
    /// <remarks>
    ///     A <c>LogicalKey</c> whose parts cannot all be typed answers with the entity, exactly as the
    ///     Actions side generates it: the mutation reports PRAG0403 there, and the build stops on that
    ///     rather than on a record this side would name and nobody emitted.
    /// </remarks>
    private static MutationKeyResponse? ReadMutationKeyResponse(INamedTypeSymbol mutation, Compilation compilation)
    {
        var declared = mutation.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Actions.Mutation.MutationAttribute")
            ?.NamedArguments.FirstOrDefault(a => a.Key == "ReturnType").Value.Value as int?;

        // MutationReturnType: Id = 0, LogicalKey = 1, Entity = 2 — and Entity when nothing is declared.
        var mutationType = mutation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (declared)
        {
            case 0:
                return IdResponseOf(mutation, compilation);
            case 1:
            {
                if (EntityOfMutation(mutation) is not { } entity)
                    return null;

                var parts = MutationLogicalKeyReader.ReadTyped(entity, compilation);
                if (parts.IsEmpty || parts.Any(p => p.Type is null))
                    return null;

                var typeName = $"{mutationType}.{Actions.Templates.MutationLogicalKeyTemplate.RecordName}";
                return new MutationKeyResponse(typeName, false,
                    parts.Select(p => p.Part).ToImmutableArray(),
                    Shape(typeName, parts.Select(p => (p.Part.Name, p.Part.JsonName ?? ToCamelCase(p.Part.Name), p.Type!))));
            }
            default:
                return null;
        }
    }

    /// <summary>The <c>{"id": …}</c> record a mutation answers with — declared by <c>ReturnType = Id</c>, or a create's default.</summary>
    private static MutationKeyResponse IdResponseOf(INamedTypeSymbol mutation, Compilation compilation)
    {
        var guid = compilation.GetTypeByMetadataName("System.Guid");
        var typeName = $"{mutation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{IdResponseRecordName}";
        var properties = ImmutableArray.Create(new MutationKeyPartModel("Id", "global::System.Guid"));
        return new MutationKeyResponse(typeName, true, properties,
            guid is null ? null : Shape(typeName, [("Id", "id", (ITypeSymbol)guid)]));
    }

    /// <summary>Whether the mutation says <c>ReturnType = Entity</c> in so many words.</summary>
    private static bool DeclaresEntityReturn(INamedTypeSymbol mutation)
        => mutation.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Pragmatic.Actions.Mutation.MutationAttribute")
            ?.NamedArguments.FirstOrDefault(a => a.Key == "ReturnType").Value.Value as int? == 2;

    private static JsonRootContribution? Shape(
        string typeName, IEnumerable<(string Name, string WireName, ITypeSymbol Type)> properties)
        // The records declare `required` members, so the context cannot build one with `new T()`.
        => JsonShapeExtractor.TryExtractProjection(typeName, properties, needsUnsafeConstructor: true,
            out var objects, out var leaves, out var collections)
            ? new JsonRootContribution(objects, leaves, collections)
            : null;

    private static INamedTypeSymbol? EntityOfMutation(INamedTypeSymbol mutation)
    {
        for (var current = mutation.BaseType; current is not null; current = current.BaseType)
            if (current.OriginalDefinition.ToDisplayString().StartsWith(EndpointShapes.Mutation.MetadataPrefix, StringComparison.Ordinal)
                && current.TypeArguments.Length > 0)
                return current.TypeArguments[0] as INamedTypeSymbol;

        return null;
    }
}
