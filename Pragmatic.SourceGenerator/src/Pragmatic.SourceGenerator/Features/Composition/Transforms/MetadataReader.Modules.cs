// Pragmatic.SourceGenerator - Composition - Metadata Reader (Module Extraction)

using System.Collections.Immutable;
using System.Text.Json;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Module extraction methods for MetadataReader.
/// </summary>
internal static partial class MetadataReader
{
    /// <summary>
    ///     Extracts module information from metadata entries.
    /// </summary>
    public static ImmutableArray<DiscoveredModuleInfo> ExtractModules(
        ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var modules = ImmutableArray.CreateBuilder<DiscoveredModuleInfo>();

        foreach (var assembly in assemblies)
            foreach (var entry in assembly.Entries)
            {
                // Module category = 8
                if (entry.Category != MetadataCategoryIds.Module)
                    continue;

                var module = ParseModuleJson(assembly.AssemblyName, entry.JsonData);
                if (module is not null)
                    modules.Add(module);
            }

        return modules.ToImmutable();
    }

    private static DiscoveredModuleInfo? ParseModuleJson(string assemblyName, string jsonData)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? assemblyName
                : assemblyName;

            var version = root.TryGetProperty("version", out var versionProp)
                ? versionProp.GetString()
                : null;

            var description = root.TryGetProperty("description", out var descProp)
                ? descProp.GetString()
                : null;

            var packageRoutePrefix = root.TryGetProperty("packageRoutePrefix", out var prpProp) &&
                                     prpProp.ValueKind == JsonValueKind.String
                ? prpProp.GetString()
                : null;

            var dependsOn = ImmutableArray<string>.Empty;
            if (root.TryGetProperty("dependsOn", out var depsProp) && depsProp.ValueKind == JsonValueKind.Array)
            {
                var deps = ImmutableArray.CreateBuilder<string>();
                foreach (var dep in depsProp.EnumerateArray())
                {
                    var depName = dep.GetString();
                    if (!string.IsNullOrEmpty(depName))
                        deps.Add(depName!);
                }

                dependsOn = deps.ToImmutable();
            }

            // Package imports — supports both legacy (string[]) and new (object[]) formats
            var packageAssemblyNames = ImmutableArray<string>.Empty;
            var packageRoutePrefixes = ImmutableArray<PackageRoutePrefixInfo>.Empty;
            if (root.TryGetProperty("packages", out var pkgsProp) && pkgsProp.ValueKind == JsonValueKind.Array)
            {
                var pkgs = ImmutableArray.CreateBuilder<string>();
                var prefixes = ImmutableArray.CreateBuilder<PackageRoutePrefixInfo>();
                foreach (var pkg in pkgsProp.EnumerateArray())
                {
                    if (pkg.ValueKind == JsonValueKind.String)
                    {
                        // Legacy format: plain assembly name string
                        var pkgName = pkg.GetString();
                        if (!string.IsNullOrEmpty(pkgName))
                            pkgs.Add(pkgName!);
                    }
                    else if (pkg.ValueKind == JsonValueKind.Object)
                    {
                        // New format: { "assembly": "...", "routePrefix": "..." }
                        var asmName = pkg.TryGetProperty("assembly", out var asmProp)
                            ? asmProp.GetString()
                            : null;
                        if (!string.IsNullOrEmpty(asmName))
                        {
                            pkgs.Add(asmName!);
                            var rp = pkg.TryGetProperty("routePrefix", out var rpProp) &&
                                     rpProp.ValueKind == JsonValueKind.String
                                ? rpProp.GetString()
                                : null;
                            prefixes.Add(new PackageRoutePrefixInfo
                            {
                                AssemblyName = asmName!,
                                RoutePrefix = rp
                            });
                        }
                    }
                }

                packageAssemblyNames = pkgs.ToImmutable();
                packageRoutePrefixes = prefixes.ToImmutable();
            }

            // Exposed endpoints (from [ExposeEndpoint<T>])
            var exposedEndpoints = ImmutableArray<ExposedEndpointModel>.Empty;
            if (root.TryGetProperty("exposedEndpoints", out var epsProp) && epsProp.ValueKind == JsonValueKind.Array)
            {
                var eps = ImmutableArray.CreateBuilder<ExposedEndpointModel>();
                foreach (var ep in epsProp.EnumerateArray())
                {
                    if (ep.ValueKind != JsonValueKind.Object) continue;

                    var actionType = ep.TryGetProperty("actionType", out var atProp) ? atProp.GetString() : null;
                    var actionSimpleName = ep.TryGetProperty("actionSimpleName", out var asnProp) ? asnProp.GetString() : null;
                    var httpVerb = ep.TryGetProperty("httpVerb", out var hvProp) ? hvProp.GetString() : "Post";
                    var route = ep.TryGetProperty("route", out var rtProp) ? rtProp.GetString() : string.Empty;
                    var actionAssembly = ep.TryGetProperty("actionAssembly", out var aaProp) ? aaProp.GetString() : assemblyName;
                    var allowAnonymous = ep.TryGetProperty("allowAnonymous", out var anProp) && anProp.ValueKind == JsonValueKind.True;

                    if (string.IsNullOrEmpty(actionType) || string.IsNullOrEmpty(actionSimpleName))
                        continue;

                    string? epName = ep.TryGetProperty("name", out var nmProp) && nmProp.ValueKind == JsonValueKind.String
                        ? nmProp.GetString() : null;

                    string? groupType = ep.TryGetProperty("groupType", out var gtProp) && gtProp.ValueKind == JsonValueKind.String
                        ? gtProp.GetString() : null;

                    var additionalPerms = ImmutableArray<string>.Empty;
                    if (ep.TryGetProperty("additionalPermissions", out var apProp) && apProp.ValueKind == JsonValueKind.Array)
                    {
                        var perms = ImmutableArray.CreateBuilder<string>();
                        foreach (var perm in apProp.EnumerateArray())
                        {
                            var permStr = perm.GetString();
                            if (!string.IsNullOrEmpty(permStr))
                                perms.Add(permStr!);
                        }
                        additionalPerms = perms.ToImmutable();
                    }

                    // What a bodyless verb binds from the query string. Absent for POST/PUT/PATCH,
                    // and absent from a module compiled by an older generator — which reads as "bind
                    // the body", the behaviour that is right for those verbs.
                    var inputs = ImmutableArray<ExposedInputModel>.Empty;
                    if (ep.TryGetProperty("inputs", out var inProp) && inProp.ValueKind == JsonValueKind.Array)
                    {
                        var read = ImmutableArray.CreateBuilder<ExposedInputModel>();
                        foreach (var input in inProp.EnumerateArray())
                        {
                            if (input.ValueKind != JsonValueKind.Object) continue;

                            var inputName = input.TryGetProperty("name", out var inNameProp) ? inNameProp.GetString() : null;
                            var inputType = input.TryGetProperty("type", out var inTypeProp) ? inTypeProp.GetString() : null;
                            if (string.IsNullOrEmpty(inputName) || string.IsNullOrEmpty(inputType))
                                continue;

                            read.Add(new ExposedInputModel(
                                inputName!,
                                inputType!,
                                input.TryGetProperty("nullable", out var nu) && nu.ValueKind == JsonValueKind.True,
                                input.TryGetProperty("required", out var rq) && rq.ValueKind == JsonValueKind.True));
                        }
                        inputs = read.ToImmutable();
                    }

                    eps.Add(new ExposedEndpointModel
                    {
                        Inputs = inputs,
                        ActionTypeName = actionType!,
                        ActionSimpleName = actionSimpleName!,
                        HttpVerb = httpVerb ?? "Post",
                        Route = route ?? string.Empty,
                        Name = epName,
                        GroupTypeName = groupType,
                        AdditionalPermissions = additionalPerms,
                        AllowAnonymous = allowAnonymous,
                        ActionAssemblyName = actionAssembly ?? assemblyName,
                        HostBoundaryName = name
                    });
                }
                exposedEndpoints = eps.ToImmutable();
            }

            string? diMethod = null;
            string? startupMethod = null;

            if (root.TryGetProperty("registrations", out var regProp))
            {
                if (regProp.TryGetProperty("di", out var diProp))
                    diMethod = diProp.GetString();
                if (regProp.TryGetProperty("startup", out var startupProp))
                    startupMethod = startupProp.GetString();
            }

            return new DiscoveredModuleInfo
            {
                Name = name,
                AssemblyName = assemblyName,
                DependsOn = dependsOn,
                Version = version,
                Description = description,
                DiRegistrationMethod = diMethod,
                StartupRegistrationMethod = startupMethod,
                PackageAssemblyNames = packageAssemblyNames,
                PackageRoutePrefixes = packageRoutePrefixes,
                PackageRoutePrefix = packageRoutePrefix,
                ExposedEndpoints = exposedEndpoints
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Pragmatic.SG] Failed to parse module metadata from {assemblyName}: {ex.Message}");
            return null;
        }
    }
}
