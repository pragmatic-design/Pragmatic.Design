// Pragmatic.SourceGenerator - Composition - Metadata Reader (Actions)

using System.Collections.Immutable;
using System.Text.Json;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Action and mutation extraction methods for MetadataReader.
///     Parses enriched Actions metadata from referenced assemblies so the host can generate
///     DI registrations directly (without calling library-side extension methods).
/// </summary>
internal static partial class MetadataReader
{
    /// <summary>
    ///     Extracts action and mutation registrations from enriched Actions metadata entries.
    ///     When <paramref name="malformedAssemblies"/> is supplied, assembly names that
    ///     produced a JSON parse error are reported into it so the caller can surface a
    ///     PRAG1688 diagnostic — silent swallowing hides host-breaking metadata drift.
    /// </summary>
    public static (ImmutableArray<DiscoveredActionInfo> Actions, ImmutableArray<DiscoveredMutationInfo> Mutations)
        ExtractActionRegistrations(
            ImmutableArray<AssemblyMetadataModel> assemblies,
            IList<(string AssemblyName, string Error)>? malformedAssemblies = null)
    {
        var actions = ImmutableArray.CreateBuilder<DiscoveredActionInfo>();
        var mutations = ImmutableArray.CreateBuilder<DiscoveredMutationInfo>();

        foreach (var assembly in assemblies)
            foreach (var entry in assembly.Entries)
            {
                // MetadataCategory.Actions = 2
                if (entry.Category != MetadataCategoryIds.Actions)
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(entry.JsonData);
                    if (!doc.RootElement.TryGetProperty("data", out var data))
                        continue;

                    // Parse actions
                    if (data.TryGetProperty("actions", out var actionsArray) &&
                        actionsArray.ValueKind == JsonValueKind.Array)
                        foreach (var action in actionsArray.EnumerateArray())
                        {
                            var parsed = ParseActionFromJson(action, assembly.AssemblyName);
                            if (parsed is not null)
                                actions.Add(parsed);
                        }

                    // Parse mutations
                    if (data.TryGetProperty("mutations", out var mutationsArray) &&
                        mutationsArray.ValueKind == JsonValueKind.Array)
                        foreach (var mutation in mutationsArray.EnumerateArray())
                        {
                            var parsed = ParseMutationFromJson(mutation, assembly.AssemblyName);
                            if (parsed is not null)
                                mutations.Add(parsed);
                        }
                }
                catch (Exception ex)
                {
                    // Malformed metadata JSON — surface via caller so the host
                    // can emit a diagnostic instead of silently skipping the module.
                    malformedAssemblies?.Add((assembly.AssemblyName, ex.Message));
                }
            }

        return (actions.ToImmutable(), mutations.ToImmutable());
    }

    private static DiscoveredActionInfo? ParseActionFromJson(JsonElement element, string assemblyName)
    {
        var type = GetStringOrNull(element, "type");
        var invokerType = GetStringOrNull(element, "invokerType");

        if (type is null || invokerType is null)
            return null;

        return new DiscoveredActionInfo
        {
            ActionType = type,
            IsVoid = GetBoolOrDefault(element, "isVoid"),
            ReturnType = GetStringOrNull(element, "returnType"),
            InvokerType = invokerType,
            CompensatorType = GetStringOrNull(element, "compensatorType"),
            SourceAssembly = assemblyName,
            PolicyTypeFqn = GetStringOrNull(element, "policyType"),
            RequireAllPermissions = GetStringArrayOrEmpty(element, "requireAllPermissions"),
            RequireAnyPermissions = GetStringArrayOrEmpty(element, "requireAnyPermissions"),
            CompositeStepInvokers = GetStringArrayOrEmpty(element, "compositeStepInvokers"),
            HasCompositeSteps = GetBoolOrDefault(element, "hasCompositeSteps"),
            BoundaryKeyedServices = GetStringArrayOrEmpty(element, "boundaryKeyedServices")
        };
    }

    private static DiscoveredMutationInfo? ParseMutationFromJson(JsonElement element, string assemblyName)
    {
        var type = GetStringOrNull(element, "type");
        var entityType = GetStringOrNull(element, "entityType");
        var invokerType = GetStringOrNull(element, "invokerType");

        if (type is null || entityType is null || invokerType is null)
            return null;

        // Parse computed default generators if present
        var computedDefaults = ImmutableArray<DiscoveredComputedDefaultInfo>.Empty;
        if (element.TryGetProperty("computedDefaults", out var cdArray) &&
            cdArray.ValueKind == JsonValueKind.Array)
        {
            var builder = ImmutableArray.CreateBuilder<DiscoveredComputedDefaultInfo>();
            foreach (var cd in cdArray.EnumerateArray())
            {
                var cdEntityType = GetStringOrNull(cd, "entityType");
                var cdValueType = GetStringOrNull(cd, "valueType");
                var cdGeneratorType = GetStringOrNull(cd, "generatorType");
                var cdRequiresScope = GetStringOrNull(cd, "requiresScope") == "true";
                if (cdEntityType is not null && cdValueType is not null && cdGeneratorType is not null)
                {
                    builder.Add(new DiscoveredComputedDefaultInfo
                    {
                        EntityTypeFqn = cdEntityType,
                        ValueTypeFqn = cdValueType,
                        GeneratorTypeFqn = cdGeneratorType,
                        RequiresScope = cdRequiresScope
                    });
                }
            }

            computedDefaults = builder.ToImmutable();
        }

        return new DiscoveredMutationInfo
        {
            MutationType = type,
            EntityType = entityType,
            InvokerType = invokerType,
            ReturnKind = GetStringOrNull(element, "returnKind"),
            CompensatorType = GetStringOrNull(element, "compensatorType"),
            PresetProviders = GetStringArrayOrEmpty(element, "presetProviders"),
            SourceAssembly = assemblyName,
            ComputedDefaults = computedDefaults,
            PolicyTypeFqn = GetStringOrNull(element, "policyType"),
            RequireAllPermissions = GetStringArrayOrEmpty(element, "requireAllPermissions"),
            RequireAnyPermissions = GetStringArrayOrEmpty(element, "requireAnyPermissions"),
            BoundaryKeyedServices = GetStringArrayOrEmpty(element, "boundaryKeyedServices")
        };
    }
}
