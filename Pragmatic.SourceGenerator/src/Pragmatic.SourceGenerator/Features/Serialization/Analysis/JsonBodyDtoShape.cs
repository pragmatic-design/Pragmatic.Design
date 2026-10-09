using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Serialization.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Serialization.Analysis;

/// <summary>
///     The JSON shape of the request bodies an endpoint generates.
/// </summary>
/// <remarks>
///     <para>
///         Without this the generated context covered the mapping DTOs and nothing else, so every
///         request body fell back to reflection. With <c>DisableReflectionFallback()</c> — the condition
///         of a real AOT publish — every POST would have thrown, while the build said nothing. Measured
///         on a lab app: 2 types covered, 5 request bodies not.
///     </para>
///     <para>
///         Which records exist and what they carry comes from <see cref="EndpointModel.BodyDtoVariants" />,
///         so the rule lives in one place and cannot drift from the templates that emit them.
///     </para>
/// </remarks>
internal static class JsonBodyDtoShape
{
    /// <summary>
    ///     From an action read out of source, where the property symbols are in hand.
    /// </summary>
    /// <param name="action">The endpoint/action type whose properties the bodies mirror.</param>
    /// <param name="endpoint">The endpoint model, which names the records and their property sets.</param>
    public static JsonRootContribution? For(INamedTypeSymbol action, EndpointModel endpoint)
    {
        var declared = PropertiesOf(action);

        return Build(endpoint, (name, _) => declared.TryGetValue(name, out var p) ? p.Type : null);
    }

    /// <summary>
    ///     From an endpoint the generator assembled itself — Resource CRUD, traits — where the body is
    ///     described as type text and the symbols have to be resolved back.
    /// </summary>
    /// <param name="endpoint">The endpoint model.</param>
    /// <param name="resolver">Resolves a written type expression to its symbol.</param>
    public static JsonRootContribution? For(EndpointModel endpoint, JsonTypeExpressionResolver resolver)
        => Build(endpoint, (_, property) => resolver.Resolve(property.TypeName));

    /// <summary>
    ///     Builds the contribution covering every record the endpoint generates, or <c>null</c> when
    ///     there are none — or when no shape could be expressed, in which case the context stays silent
    ///     rather than emitting partial metadata.
    /// </summary>
    private static JsonRootContribution? Build(
        EndpointModel endpoint,
        System.Func<string, BodyPropertyModel, ITypeSymbol?> typeOf)
    {
        var variants = endpoint.BodyDtoVariants;
        if (variants.IsDefaultOrEmpty)
            return null;

        var objects = ImmutableArray.CreateBuilder<JsonObjectModel>();
        var leaves = ImmutableArray.CreateBuilder<JsonLeafModel>();
        var collections = ImmutableArray.CreateBuilder<JsonCollectionModel>();

        foreach (var variant in variants)
        {
            var properties = new List<(string Name, string WireName, ITypeSymbol Type)>(variant.Properties.Length);
            foreach (var property in variant.Properties)
            {
                if (typeOf(property.Name, property) is not { } type)
                    break;

                // The record carries [JsonPropertyName] when the operation's property did, so the
                // context has to agree with it. Without this the two disagree and the payload depends on
                // whether the application was published AOT.
                properties.Add((property.Name, property.JsonName ?? JsonWireNames.CamelCase(property.Name), type));
            }

            // All or nothing per record: a body missing a field is worse than a body the context does
            // not know about, because the first one deserializes and silently drops it.
            if (properties.Count != variant.Properties.Length)
                continue;

            // The generated record reproduces `required`, which makes `new T()` a compile error
            // (CS9035) even though the deserializer is about to set every one of them.
            var needsUnsafeConstructor = variant.Properties.Any(static p => p.IsRequired);

            if (!JsonShapeExtractor.TryExtractProjection(
                    $"global::{endpoint.Namespace}.{variant.Name}", properties, needsUnsafeConstructor,
                    out var variantObjects, out var variantLeaves, out var variantCollections))
            {
                continue;
            }

            objects.AddRange(variantObjects);
            leaves.AddRange(variantLeaves);
            collections.AddRange(variantCollections);
        }

        return objects.Count > 0
            ? new JsonRootContribution(objects.ToImmutable(), leaves.ToImmutable(), collections.ToImmutable())
            : null;
    }

    /// <summary>
    ///     The action's properties by name. Most-derived wins: a <c>new</c> or overriding property
    ///     shadows the base one the caller never sees.
    /// </summary>
    private static Dictionary<string, IPropertySymbol> PropertiesOf(INamedTypeSymbol action)
    {
        var byName = new Dictionary<string, IPropertySymbol>(System.StringComparer.Ordinal);

        for (var current = action; current is not null; current = current.BaseType)
            foreach (var member in current.GetMembers())
                if (member is IPropertySymbol property && !byName.ContainsKey(property.Name))
                    byName[property.Name] = property;

        return byName;
    }
}
