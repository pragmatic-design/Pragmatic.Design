using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Serialization.Analysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Transforms;

/// <summary>
///     Discovers boundary payload types and extracts their JSON shape. Covers message payloads
///     (<c>IMessageHandler&lt;T&gt;</c>), job parameters (<c>IJob&lt;TParams&gt;</c>), event payloads
///     (<c>IDomainEventHandler&lt;T&gt;</c>) and sagas (the persisted saga state + its compensation actions).
/// </summary>
internal static class SerializationTransform
{
    private const string MessageHandlerMetadataName = "Pragmatic.Messaging.IMessageHandler`1";
    private const string JobMetadataName = "Pragmatic.Jobs.IJob`1";
    private const string DomainEventHandlerMetadataName = "Pragmatic.Events.IDomainEventHandler`1";

    public static JsonRootContribution? FromMessageHandler(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        => FromInterfacePayload(context, MessageHandlerMetadataName, ct);

    public static JsonRootContribution? FromJob(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        => FromInterfacePayload(context, JobMetadataName, ct);

    public static JsonRootContribution? FromEventHandler(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        => FromInterfacePayload(context, DomainEventHandlerMetadataName, ct);

    /// <summary>
    ///     A mapping DTO (<c>[MapFrom&lt;T&gt;]</c> / <c>[MapTo&lt;T&gt;]</c>) is the declared request/response
    ///     shape crossing the HTTP boundary. The decorated type itself is the JSON root.
    /// </summary>
    public static JsonRootContribution? FromMappingDto(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol dto)
            return null;

        return JsonShapeExtractor.TryExtractClosure(dto, out var objects, out var leaves, out var collections)
            ? new JsonRootContribution(objects, leaves, collections)
            : null;
    }

    /// <summary>
    ///     An SSE streaming endpoint's item type (StreamingEndpoint&lt;TItem,…&gt; /
    ///     StreamingDomainAction&lt;TItem&gt;) crosses the HTTP boundary per event — the item
    ///     is a JSON root so the generated context covers it under DisableReflectionFallback.
    /// </summary>
    public static JsonRootContribution? FromStreamingEndpoint(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            ct.ThrowIfCancellationRequested();
            var baseName = baseType.OriginalDefinition.ToDisplayString();

            if ((baseName.StartsWith("Pragmatic.Endpoints.Base.StreamingEndpoint<") ||
                 baseName.StartsWith("Pragmatic.Actions.Abstractions.StreamingDomainAction<")) &&
                baseType.TypeArguments.Length > 0 &&
                baseType.TypeArguments[0] is INamedTypeSymbol { SpecialType: SpecialType.None } itemType)
            {
                return JsonShapeExtractor.TryExtractClosure(itemType, out var objects, out var leaves, out var collections)
                    ? new JsonRootContribution(objects, leaves, collections)
                    : null;
            }

            baseType = baseType.BaseType;
        }

        return null;
    }

    private static JsonRootContribution? FromInterfacePayload(
        GeneratorAttributeSyntaxContext context, string interfaceMetadataName, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var markerDefinition = context.SemanticModel.Compilation.GetTypeByMetadataName(interfaceMetadataName);
        if (markerDefinition is null)
            return null;

        INamedTypeSymbol? payload = null;
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.IsGenericType
                && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, markerDefinition)
                && iface.TypeArguments.Length == 1
                && iface.TypeArguments[0] is INamedTypeSymbol t)
            {
                payload = t;
                break;
            }
        }

        if (payload is null)
            return null;

        return JsonShapeExtractor.TryExtractClosure(payload, out var objects, out var leaves, out var collections)
            ? new JsonRootContribution(objects, leaves, collections)
            : null;
    }

    /// <summary>
    ///     A saga (<c>[Saga&lt;TState&gt;]</c>) is persisted as JSON by <c>EfCoreSagaRepository</c>
    ///     (the whole saga instance) and its compensation actions are round-tripped by the generated
    ///     <c>SagaOrchestrator</c>. Both must be covered so the persisted state and compensation payloads
    ///     serialize without reflection under AOT.
    /// </summary>
    public static JsonRootContribution? FromSaga(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol saga)
            return null;

        // The saga instance itself must be coverable, otherwise the persisted state stays on reflection —
        // defer the whole contribution (consistent with payload discovery).
        if (!JsonShapeExtractor.TryExtractClosure(saga, out var objects, out var leaves, out var collections))
            return null;

        var objMap = objects.ToDictionary(o => o.TypeExpr, System.StringComparer.Ordinal);
        var leafMap = leaves.ToDictionary(l => l.TypeExpr, System.StringComparer.Ordinal);
        var collMap = collections.ToDictionary(c => c.TypeExpr, System.StringComparer.Ordinal);

        // Compensation actions: [CompensateWith<T>] on saga methods → each T is serialized then
        // deserialized during compensation. Best-effort: merge every coverable action.
        foreach (var member in saga.GetMembers().OfType<IMethodSymbol>())
        {
            var compensateAttr = member.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass is { Name: "CompensateWithAttribute", IsGenericType: true });
            if (compensateAttr?.AttributeClass?.TypeArguments is not { Length: > 0 } args
                || args[0] is not INamedTypeSymbol action)
                continue;

            if (JsonShapeExtractor.TryExtractClosure(action, out var aObjs, out var aLeaves, out var aColls))
                Merge(aObjs, aLeaves, aColls, objMap, leafMap, collMap);
        }

        return new JsonRootContribution(
            objMap.Values.ToImmutableArray(),
            leafMap.Values.ToImmutableArray(),
            collMap.Values.ToImmutableArray());
    }

    private static void Merge(
        ImmutableArray<JsonObjectModel> objects,
        ImmutableArray<JsonLeafModel> leaves,
        ImmutableArray<JsonCollectionModel> collections,
        Dictionary<string, JsonObjectModel> objMap,
        Dictionary<string, JsonLeafModel> leafMap,
        Dictionary<string, JsonCollectionModel> collMap)
    {
        foreach (var o in objects) objMap[o.TypeExpr] = o;
        foreach (var l in leaves) leafMap[l.TypeExpr] = l;
        foreach (var c in collections) collMap[c.TypeExpr] = c;
    }
}
