// Pragmatic.SourceGenerator - Composition - Metadata Reader (Service Extraction)

using System.Collections.Immutable;
using System.Text.Json;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Service and decorator extraction methods for MetadataReader.
///     Parses enriched DI metadata from referenced assemblies so the host can generate
///     DI registrations directly (without calling library-side extension methods).
/// </summary>
internal static partial class MetadataReader
{
    /// <summary>
    ///     Extracts service and decorator registrations from enriched DI metadata entries.
    /// </summary>
    public static (ImmutableArray<DiscoveredServiceInfo> Services, ImmutableArray<DiscoveredDecoratorInfo> Decorators)
        ExtractServiceRegistrations(ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var services = ImmutableArray.CreateBuilder<DiscoveredServiceInfo>();
        var decorators = ImmutableArray.CreateBuilder<DiscoveredDecoratorInfo>();

        foreach (var assembly in assemblies)
            foreach (var entry in assembly.Entries)
            {
                // MetadataCategory.DI = 0
                if (entry.Category != MetadataCategoryIds.DI)
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(entry.JsonData);
                    if (!doc.RootElement.TryGetProperty("data", out var data))
                        continue;

                    // Parse services
                    if (data.TryGetProperty("services", out var servicesArray) &&
                        servicesArray.ValueKind == JsonValueKind.Array)
                        foreach (var svc in servicesArray.EnumerateArray())
                        {
                            var parsed = ParseServiceFromJson(svc, assembly.AssemblyName);
                            if (parsed is not null)
                                services.Add(parsed);
                        }

                    // Parse decorators
                    if (data.TryGetProperty("decorators", out var decoratorsArray) &&
                        decoratorsArray.ValueKind == JsonValueKind.Array)
                        foreach (var dec in decoratorsArray.EnumerateArray())
                        {
                            var parsed = ParseDecoratorFromJson(dec, assembly.AssemblyName);
                            if (parsed is not null)
                                decorators.Add(parsed);
                        }
                }
                catch (Exception ex)
                {
                    // Malformed metadata JSON — skip but log for debugging
                    System.Diagnostics.Debug.WriteLine($"[Pragmatic.SG] Failed to parse DI metadata from {assembly.AssemblyName}: {ex.Message}");
                }
            }

        return (services.ToImmutable(), decorators.ToImmutable());
    }

    /// <summary>
    ///     The contracts the referenced assemblies say they register, with the lifetime each names — what
    ///     the dependency validator needs to stop refusing a contract another module registers.
    /// </summary>
    /// <remarks>
    ///     A projection of <see cref="ExtractServiceRegistrations" />, not a second parser: the validator
    ///     needs two fields of it, and a provider carrying only those two re-runs the stage that consumes
    ///     it only when they change.
    /// </remarks>
    public static ImmutableArray<RegisteredElsewhere> ExtractRegisteredContracts(
        ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var (services, _) = ExtractServiceRegistrations(assemblies);

        var contracts = ImmutableArray.CreateBuilder<RegisteredElsewhere>(services.Length);
        foreach (var service in services)
            contracts.Add(new RegisteredElsewhere
            {
                Contract = service.Interface,
                Lifetime = service.Lifetime
            });

        return contracts.ToImmutable();
    }

    /// <summary>
    ///     Extracts <c>[ServiceFactory]</c> declarations from DI metadata (emitted by referenced boundary
    ///     libraries) so the host can register cross-assembly factories alongside its own.
    /// </summary>
    public static ImmutableArray<ServiceFactoryModel> ExtractServiceFactories(
        ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var factories = ImmutableArray.CreateBuilder<ServiceFactoryModel>();

        foreach (var assembly in assemblies)
            foreach (var entry in assembly.Entries)
            {
                if (entry.Category != MetadataCategoryIds.DI)
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(entry.JsonData);
                    if (!doc.RootElement.TryGetProperty("data", out var data) ||
                        !data.TryGetProperty("serviceFactories", out var arr) ||
                        arr.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var f in arr.EnumerateArray())
                    {
                        var parsed = ParseServiceFactoryFromJson(f);
                        if (parsed is not null)
                            factories.Add(parsed);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Pragmatic.SG] Failed to parse ServiceFactory metadata from {assembly.AssemblyName}: {ex.Message}");
                }
            }

        return factories.ToImmutable();
    }

    private static ServiceFactoryModel? ParseServiceFactoryFromJson(JsonElement element)
    {
        var className = GetStringOrNull(element, "class");
        if (className is null ||
            !element.TryGetProperty("methods", out var methodsArr) ||
            methodsArr.ValueKind != JsonValueKind.Array)
            return null;

        var methods = ImmutableArray.CreateBuilder<FactoryMethodModel>();
        foreach (var m in methodsArr.EnumerateArray())
        {
            var methodName = GetStringOrNull(m, "method");
            var returnType = GetStringOrNull(m, "returnType");
            var lifetime = GetStringOrNull(m, "lifetime") ?? "Scoped";
            if (methodName is null || returnType is null)
                continue;

            var parameters = ImmutableArray.CreateBuilder<FactoryParameterModel>();
            if (m.TryGetProperty("parameters", out var paramsArr) && paramsArr.ValueKind == JsonValueKind.Array)
                foreach (var p in paramsArr.EnumerateArray())
                {
                    var type = GetStringOrNull(p, "type");
                    if (type is null)
                        continue;
                    parameters.Add(new FactoryParameterModel { FullTypeName = type, IsOptional = GetBoolOrDefault(p, "optional") });
                }

            methods.Add(new FactoryMethodModel
            {
                MethodName = methodName,
                ReturnTypeFullName = returnType,
                Lifetime = lifetime,
                Parameters = parameters.ToImmutable()
            });
        }

        if (methods.Count == 0)
            return null;

        return new ServiceFactoryModel
        {
            Namespace = string.Empty,
            FactoryClassFullName = className,
            Methods = methods.ToImmutable(),
            Location = null
        };
    }

    private static DiscoveredServiceInfo? ParseServiceFromJson(JsonElement element, string assemblyName)
    {
        var iface = GetStringOrNull(element, "interface");
        var impl = GetStringOrNull(element, "implementation");
        var lifetime = GetStringOrNull(element, "lifetime");

        if (iface is null || impl is null || lifetime is null)
            return null;

        var isOpenGeneric = GetBoolOrDefault(element, "isOpenGeneric");

        return new DiscoveredServiceInfo
        {
            Interface = iface,
            Implementation = impl,
            Lifetime = lifetime,
            Key = GetStringOrNull(element, "key"),
            IsOpenGeneric = isOpenGeneric,
            OpenGenericInterface = isOpenGeneric ? GetStringOrNull(element, "openGenericInterface") : null,
            OpenGenericImplementation = isOpenGeneric ? GetStringOrNull(element, "openGenericImplementation") : null,
            Factory = ParseFactoryFromJson(element),
            SourceAssembly = assemblyName
        };
    }

    private static DiscoveredFactoryInfo? ParseFactoryFromJson(JsonElement element)
    {
        if (!element.TryGetProperty("factory", out var factory) || factory.ValueKind != JsonValueKind.Object)
            return null;

        var propertyInjections = ImmutableArray.CreateBuilder<DiscoveredPropertyInjection>();
        var methodInjections = ImmutableArray.CreateBuilder<DiscoveredMethodInjection>();

        if (factory.TryGetProperty("propertyInjections", out var props) && props.ValueKind == JsonValueKind.Array)
            foreach (var prop in props.EnumerateArray())
            {
                var name = GetStringOrNull(prop, "propertyName");
                var type = GetStringOrNull(prop, "propertyType");
                if (name is null || type is null)
                    continue;

                propertyInjections.Add(new DiscoveredPropertyInjection
                {
                    PropertyName = name,
                    PropertyType = type,
                    IsRequired = GetBoolOrDefault(prop, "isRequired"),
                    Key = GetStringOrNull(prop, "key")
                });
            }

        if (factory.TryGetProperty("methodInjections", out var methods) && methods.ValueKind == JsonValueKind.Array)
            foreach (var method in methods.EnumerateArray())
            {
                var methodName = GetStringOrNull(method, "methodName");
                if (methodName is null)
                    continue;

                var parameters = ImmutableArray.CreateBuilder<DiscoveredMethodParameter>();
                if (method.TryGetProperty("parameters", out var paramsArray) &&
                    paramsArray.ValueKind == JsonValueKind.Array)
                    foreach (var param in paramsArray.EnumerateArray())
                    {
                        var type = GetStringOrNull(param, "type");
                        if (type is null)
                            continue;

                        parameters.Add(new DiscoveredMethodParameter
                        {
                            Type = type,
                            IsOptional = GetBoolOrDefault(param, "isOptional"),
                            Key = GetStringOrNull(param, "key")
                        });
                    }

                methodInjections.Add(new DiscoveredMethodInjection
                {
                    MethodName = methodName,
                    Parameters = parameters.ToImmutable()
                });
            }

        if (propertyInjections.Count == 0 && methodInjections.Count == 0)
            return null;

        return new DiscoveredFactoryInfo
        {
            PropertyInjections = propertyInjections.ToImmutable(),
            MethodInjections = methodInjections.ToImmutable()
        };
    }

    private static DiscoveredDecoratorInfo? ParseDecoratorFromJson(JsonElement element, string assemblyName)
    {
        var iface = GetStringOrNull(element, "interface");
        var impl = GetStringOrNull(element, "implementation");

        if (iface is null || impl is null)
            return null;

        return new DiscoveredDecoratorInfo
        {
            Interface = iface,
            Implementation = impl,
            Order = GetIntOrDefault(element, "order"),
            SourceAssembly = assemblyName
        };
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static bool GetBoolOrDefault(JsonElement element, string propertyName, bool defaultValue = false)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True)
                return true;
            if (prop.ValueKind == JsonValueKind.False)
                return false;
        }

        return defaultValue;
    }

    private static int GetIntOrDefault(JsonElement element, string propertyName, int defaultValue = 0)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : defaultValue;
    }

    private static ImmutableArray<string> GetStringArrayOrEmpty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (value is not null)
                    builder.Add(value);
            }
        }

        return builder.ToImmutable();
    }
}
