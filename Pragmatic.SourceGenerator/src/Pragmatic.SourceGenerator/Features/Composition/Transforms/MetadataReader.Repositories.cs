// Pragmatic.SourceGenerator - Composition - Metadata Reader (Repositories)

using System.Collections.Immutable;
using System.Text.Json;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Repository extraction methods for MetadataReader.
///     Parses enriched Persistence metadata from referenced assemblies so the host can generate
///     DI registrations directly (without calling library-side extension methods).
/// </summary>
internal static partial class MetadataReader
{
    /// <summary>
    ///     Extracts repository registrations from enriched Persistence metadata entries.
    /// </summary>
    public static ImmutableArray<DiscoveredRepositoryInfo> ExtractRepositoryRegistrations(
        ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var repositories = ImmutableArray.CreateBuilder<DiscoveredRepositoryInfo>();

        foreach (var assembly in assemblies)
            foreach (var entry in assembly.Entries)
            {
                // MetadataCategory.Persistence = 10
                if (entry.Category != MetadataCategoryIds.Persistence)
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(entry.JsonData);
                    if (!doc.RootElement.TryGetProperty("data", out var data))
                        continue;

                    if (data.TryGetProperty("entities", out var entitiesArray) &&
                        entitiesArray.ValueKind == JsonValueKind.Array)
                        foreach (var entity in entitiesArray.EnumerateArray())
                        {
                            var parsed = ParseRepositoryFromJson(entity, assembly.AssemblyName);
                            if (parsed is not null)
                                repositories.Add(parsed);
                        }
                }
                catch (Exception ex)
                {
                    // Malformed metadata JSON — skip but log for debugging
                    System.Diagnostics.Debug.WriteLine($"[Pragmatic.SG] Failed to parse persistence metadata from {assembly.AssemblyName}: {ex.Message}");
                }
            }

        return repositories.ToImmutable();
    }

    /// <summary>
    ///     Extracts the lookup-cache registration method of every referenced assembly that has one.
    /// </summary>
    /// <remarks>
    ///     The host does not call <c>Add{Prefix}LookupCaches()</c> — it writes its container from these
    ///     documents — so the method existed and nothing invoked it. It is a separate field from
    ///     <c>registrationMethod</c>, which the query filters hold; both are read the same way, and the
    ///     host emits a call for each.
    /// </remarks>
    public static ImmutableArray<string> ExtractLookupRegistrations(
        ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var methods = ImmutableArray.CreateBuilder<string>();

        foreach (var assembly in assemblies)
            foreach (var entry in assembly.Entries)
            {
                if (entry.Category != MetadataCategoryIds.Persistence)
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(entry.JsonData);
                    var method = GetStringOrNull(doc.RootElement, "lookupRegistrationMethod");
                    if (!string.IsNullOrEmpty(method))
                        methods.Add(method!);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Pragmatic.SG] Failed to parse persistence metadata from {assembly.AssemblyName}: {ex.Message}");
                }
            }

        return methods.ToImmutable();
    }

    private static DiscoveredRepositoryInfo? ParseRepositoryFromJson(JsonElement element, string assemblyName)
    {
        var entityType = GetStringOrNull(element, "type");
        var idType = GetStringOrNull(element, "idType");
        var repositoryType = GetStringOrNull(element, "repositoryType");

        if (entityType is null || idType is null || repositoryType is null)
            return null;

        return new DiscoveredRepositoryInfo
        {
            EntityType = entityType,
            IdType = idType,
            RepositoryType = repositoryType,
            BoundaryName = GetStringOrNull(element, "boundaryName"),
            SourceAssembly = assemblyName
        };
    }
}
