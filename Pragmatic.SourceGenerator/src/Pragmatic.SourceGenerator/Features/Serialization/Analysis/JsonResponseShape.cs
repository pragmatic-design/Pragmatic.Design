using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Analysis;

/// <summary>
///     The JSON shape of what an endpoint answers with.
/// </summary>
/// <remarks>
///     <para>
///         Measured under Native AOT on a one-endpoint app: with the reflection fallback disabled the
///         request failed; with it enabled the request succeeded and returned <c>{}</c> — the response
///         type's properties had been trimmed, so the caller got a well-formed empty object instead of
///         an error. Silent data loss is the worse of the two outcomes, and neither is acceptable for a
///         framework that claims AOT.
///     </para>
///     <para>
///         Only the type the action is generic over is taken. Wrappers the pipeline puts around it, and
///         the error payload, are separate concerns tracked separately — covering half of a shape and
///         calling it done is how the request bodies stayed uncovered for so long.
///     </para>
/// </remarks>
internal static class JsonResponseShape
{
    /// <summary>Base types whose single type argument is what goes on the wire.</summary>
    private static readonly string[] GenericResponseBases =
    [
        "Pragmatic.Endpoints.Base.Endpoint<",
        "Pragmatic.Actions.Abstractions.DomainAction<",
        "Pragmatic.Actions.Mutation.Mutation<",
    ];

    /// <summary>
    ///     The contribution covering the action's response type, or <c>null</c> when it returns nothing,
    ///     returns a type whose shape cannot be expressed, or returns a file.
    /// </summary>
    public static JsonRootContribution? For(INamedTypeSymbol action)
    {
        if (ResponseTypeOf(action) is not { } response)
            return null;

        // A file response is written as bytes with its own content type; it never becomes JSON.
        if (Core.FileResponseType.Is(response.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            return null;

        return JsonShapeExtractor.TryExtractClosure(response, out var objects, out var leaves, out var collections)
            ? new JsonRootContribution(objects, leaves, collections)
            : null;
    }

    private static INamedTypeSymbol? ResponseTypeOf(INamedTypeSymbol action)
    {
        for (var baseType = action.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            var name = baseType.OriginalDefinition.ToDisplayString();
            if (!Matches(name) || baseType.TypeArguments.Length == 0)
                continue;

            return baseType.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    private static bool Matches(string baseName)
    {
        foreach (var candidate in GenericResponseBases)
            if (baseName.StartsWith(candidate, System.StringComparison.Ordinal))
                return true;

        return false;
    }
}
