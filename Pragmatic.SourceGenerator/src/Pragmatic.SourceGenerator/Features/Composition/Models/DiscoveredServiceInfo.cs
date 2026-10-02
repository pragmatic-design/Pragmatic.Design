// Pragmatic.SourceGenerator - Composition - Discovered Service Info
// Models for services/decorators discovered from referenced assembly metadata

using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     A service registration discovered from a referenced assembly's enriched metadata.
/// </summary>
internal sealed record DiscoveredServiceInfo
{
    /// <summary>Gets the service interface type name.</summary>
    public required string Interface { get; init; }

    /// <summary>Gets the implementation type name.</summary>
    public required string Implementation { get; init; }

    /// <summary>Gets the service lifetime (Singleton, Scoped, Transient).</summary>
    public required string Lifetime { get; init; }

    /// <summary>Gets the keyed service key (null if not keyed).</summary>
    public string? Key { get; init; }

    /// <summary>Gets whether this is an open generic registration.</summary>
    public bool IsOpenGeneric { get; init; }

    /// <summary>Gets the open generic interface type expression (e.g., "typeof(IRepository&lt;&gt;)").</summary>
    public string? OpenGenericInterface { get; init; }

    /// <summary>Gets the open generic implementation type expression (e.g., "typeof(Repository&lt;&gt;)").</summary>
    public string? OpenGenericImplementation { get; init; }

    /// <summary>Gets factory info when the service requires property/method injection.</summary>
    public DiscoveredFactoryInfo? Factory { get; init; }

    /// <summary>Gets the source assembly name for grouping/comments.</summary>
    public required string SourceAssembly { get; init; }
}

/// <summary>
///     A decorator registration discovered from a referenced assembly's enriched metadata.
/// </summary>
internal sealed record DiscoveredDecoratorInfo
{
    /// <summary>Gets the interface being decorated.</summary>
    public required string Interface { get; init; }

    /// <summary>Gets the decorator implementation type name.</summary>
    public required string Implementation { get; init; }

    /// <summary>Gets the decorator order.</summary>
    public required int Order { get; init; }

    /// <summary>Gets the source assembly name for grouping/comments.</summary>
    public required string SourceAssembly { get; init; }
}

/// <summary>
///     Factory registration info for services requiring property/method injection.
/// </summary>
internal sealed record DiscoveredFactoryInfo
{
    /// <summary>Gets the property injections.</summary>
    public required EquatableArray<DiscoveredPropertyInjection> PropertyInjections { get; init; }

    /// <summary>Gets the method injections.</summary>
    public required EquatableArray<DiscoveredMethodInjection> MethodInjections { get; init; }
}

/// <summary>
///     A property injection discovered from metadata.
/// </summary>
internal sealed record DiscoveredPropertyInjection
{
    /// <summary>Gets the property name.</summary>
    public required string PropertyName { get; init; }

    /// <summary>Gets the property type name.</summary>
    public required string PropertyType { get; init; }

    /// <summary>Gets whether the property is required.</summary>
    public required bool IsRequired { get; init; }

    /// <summary>Gets the keyed service key (null if not keyed).</summary>
    public string? Key { get; init; }
}

/// <summary>
///     A method injection discovered from metadata.
/// </summary>
internal sealed record DiscoveredMethodInjection
{
    /// <summary>Gets the method name.</summary>
    public required string MethodName { get; init; }

    /// <summary>Gets the method parameters.</summary>
    public required EquatableArray<DiscoveredMethodParameter> Parameters { get; init; }
}

/// <summary>
///     A method parameter for a discovered method injection.
/// </summary>
internal sealed record DiscoveredMethodParameter
{
    /// <summary>Gets the parameter type name.</summary>
    public required string Type { get; init; }

    /// <summary>Gets whether the parameter is optional.</summary>
    public required bool IsOptional { get; init; }

    /// <summary>Gets the keyed service key (null if not keyed).</summary>
    public string? Key { get; init; }
}
