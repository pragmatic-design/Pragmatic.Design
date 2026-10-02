// Pragmatic.SourceGenerator - Composition - Metadata Reader (Endpoints)

using System.Collections.Immutable;
using System.Text.Json;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Endpoint extraction methods for MetadataReader.
///     Parses enriched Endpoints metadata from referenced assemblies so the host can generate
///     route mapping directly (without calling library-side extension methods).
/// </summary>
internal static partial class MetadataReader
{
    /// <summary>
    ///     Extracts endpoint and group registrations from enriched Endpoints metadata entries.
    /// </summary>
    public static (ImmutableArray<DiscoveredEndpointRouteInfo> Endpoints,
        ImmutableArray<DiscoveredEndpointGroupInfo> Groups,
        bool HasAspVersioning)
        ExtractEndpointRegistrations(ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var endpoints = ImmutableArray.CreateBuilder<DiscoveredEndpointRouteInfo>();
        var groups = ImmutableArray.CreateBuilder<DiscoveredEndpointGroupInfo>();
        var hasAspVersioning = false;

        foreach (var assembly in assemblies)
            foreach (var entry in assembly.Entries)
            {
                // MetadataCategory.Endpoints = 5
                if (entry.Category != MetadataCategoryIds.Endpoints)
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(entry.JsonData);
                    if (!doc.RootElement.TryGetProperty("data", out var data))
                        continue;

                    // HasAspVersioning flag
                    if (data.TryGetProperty("hasAspVersioning", out var versioningProp) &&
                        versioningProp.ValueKind == JsonValueKind.True)
                        hasAspVersioning = true;

                    // Parse endpoints
                    if (data.TryGetProperty("endpoints", out var endpointsArray) &&
                        endpointsArray.ValueKind == JsonValueKind.Array)
                        foreach (var endpoint in endpointsArray.EnumerateArray())
                        {
                            var parsed = ParseEndpointFromJson(endpoint, assembly.AssemblyName);
                            if (parsed is not null)
                                endpoints.Add(parsed);
                        }

                    // Parse groups
                    if (data.TryGetProperty("groups", out var groupsArray) &&
                        groupsArray.ValueKind == JsonValueKind.Array)
                        foreach (var group in groupsArray.EnumerateArray())
                        {
                            var parsed = ParseGroupFromJson(group, assembly.AssemblyName);
                            if (parsed is not null)
                                groups.Add(parsed);
                        }
                }
                catch (Exception ex)
                {
                    // Malformed metadata JSON — skip but log for debugging
                    System.Diagnostics.Debug.WriteLine($"[Pragmatic.SG] Failed to parse endpoints metadata from {assembly.AssemblyName}: {ex.Message}");
                }
            }

        return (endpoints.ToImmutable(), groups.ToImmutable(), hasAspVersioning);
    }

    private static DiscoveredEndpointRouteInfo? ParseEndpointFromJson(JsonElement element, string assemblyName)
    {
        var type = GetStringOrNull(element, "type");
        if (type is null)
            return null;

        return new DiscoveredEndpointRouteInfo
        {
            EndpointType = type,
            GroupType = GetStringOrNull(element, "group"),
            Verb = GetStringOrNull(element, "verb"),
            Route = GetStringOrNull(element, "route"),
            DerivedPermission = GetStringOrNull(element, "derivedPermission"),
            SourceAssembly = assemblyName,
            RateLimitRequests = element.TryGetProperty("rateLimitRequests", out var permits)
                                && permits.ValueKind == JsonValueKind.Number
                ? permits.GetInt32()
                : null,
            RateLimitWindow = GetStringOrNull(element, "rateLimitWindow"),
            Processors = ParseProcessors(element)
        };
    }

    /// <summary>
    ///     The processor types declared on the endpoint, which the host has to register itself: the
    ///     assembly's own <c>AddPragmaticEndpoints</c> registers them and the host never calls it.
    /// </summary>
    private static EquatableArray<string> ParseProcessors(JsonElement element)
    {
        if (!element.TryGetProperty("processors", out var processors) ||
            processors.ValueKind != JsonValueKind.Array)
            return EquatableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var processor in processors.EnumerateArray())
            if (processor.ValueKind == JsonValueKind.String && processor.GetString() is { Length: > 0 } type)
                builder.Add(type);

        return builder.ToImmutable();
    }

    private static DiscoveredEndpointGroupInfo? ParseGroupFromJson(JsonElement element, string assemblyName)
    {
        var type = GetStringOrNull(element, "type");
        var routePrefix = GetStringOrNull(element, "routePrefix");
        if (type is null || routePrefix is null)
            return null;

        return new DiscoveredEndpointGroupInfo
        {
            GroupType = type,
            RoutePrefix = routePrefix,
            ParentGroupType = GetStringOrNull(element, "parent"),
            SourceAssembly = assemblyName
        };
    }
}
