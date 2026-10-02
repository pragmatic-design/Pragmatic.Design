using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Reason why a service is invalid.
/// </summary>
internal enum InvalidReason
{
    None,
    NotClass,
    Abstract,
    NoInterface,
    NotStartupStep,
    NotEventHandler
}

/// <summary>
///     Model representing a service to be registered.
/// </summary>
internal sealed record ServiceModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string ServiceTypeName { get; init; }
    public required string Lifetime { get; init; }
    public required bool AsSelf { get; init; }
    public string? Key { get; init; }

    /// <summary>
    ///     <c>[Service(Multiple = true)]</c>: one implementation among several of the same service
    ///     (an <c>IPermissionProvider</c>, an <c>IQueryFilter</c>), registered with
    ///     <c>TryAddEnumerable</c>. A plain <c>TryAdd</c> would keep whichever registered first and drop
    ///     the rest, in silence and depending on the order.
    /// </summary>
    public bool IsMultiple { get; init; }
    public required EquatableArray<DependencyModel> Dependencies { get; init; }

    /// <summary>
    ///     Properties marked with [Inject] for property injection.
    /// </summary>
    public EquatableArray<PropertyInjectionModel> PropertyInjections { get; init; } =
        EquatableArray<PropertyInjectionModel>.Empty;

    /// <summary>
    ///     Methods marked with [Inject] for method injection.
    /// </summary>
    public EquatableArray<MethodInjectionModel> MethodInjections { get; init; } =
        EquatableArray<MethodInjectionModel>.Empty;

    /// <summary>
    ///     Whether this service requires factory registration (has [Inject] members).
    /// </summary>
    public bool RequiresFactory => !PropertyInjections.IsDefaultOrEmpty || !MethodInjections.IsDefaultOrEmpty;

    /// <summary>
    ///     Whether this is an open generic service (e.g., Repository&lt;T&gt; : IRepository&lt;T&gt;).
    /// </summary>
    public bool IsOpenGeneric { get; init; }

    /// <summary>
    ///     The open generic service type name (e.g., "typeof(IRepository&lt;&gt;)").
    ///     Only set when <see cref="IsOpenGeneric" /> is true.
    /// </summary>
    public string? OpenGenericServiceTypeName { get; init; }

    /// <summary>
    ///     The open generic implementation type name (e.g., "typeof(Repository&lt;&gt;)").
    ///     Only set when <see cref="IsOpenGeneric" /> is true.
    /// </summary>
    public string? OpenGenericImplementationTypeName { get; init; }

    /// <summary>
    ///     Location for diagnostic reporting.
    /// </summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();

    /// <summary>
    ///     If set, indicates this service is invalid and should not be registered.
    /// </summary>
    public InvalidReason InvalidReason { get; init; } = InvalidReason.None;

    /// <summary>
    ///     Whether this service is valid for registration.
    /// </summary>
    public bool IsValid => InvalidReason == InvalidReason.None;
    /// <summary>
    ///     The <c>[Service&lt;T&gt;]</c> type argument names something no generator has written yet,
    ///     so the emitted registration carries it unqualified.
    /// </summary>
    public string? UnresolvedTypeArgument { get; init; }
}

/// <summary>
///     Model representing a decorator to be applied.
/// </summary>
internal sealed record DecoratorModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string DecoratedInterface { get; init; }
    public required int Order { get; init; }

    /// <summary>
    ///     Location for diagnostic reporting.
    /// </summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();

    /// <summary>
    ///     Whether the decorator has a constructor parameter for the inner service.
    /// </summary>
    public bool HasInnerServiceParameter { get; init; }

    /// <summary>
    ///     Accessibility of the decorator class (e.g., "public", "internal").
    /// </summary>
    public string Accessibility { get; init; } = "public";

    /// <summary>
    ///     Interface methods for delegation stub generation.
    /// </summary>
    public EquatableArray<InterfaceMethodModel> InterfaceMethods { get; init; } = EquatableArray<InterfaceMethodModel>.Empty;

    /// <summary>If set, the decorator is invalid and must not be generated (only reported).</summary>
    public InvalidReason InvalidReason { get; init; } = InvalidReason.None;
    public bool IsValid => InvalidReason == InvalidReason.None;
}

/// <summary>
///     Model for an interface method — used by decorator delegation template.
/// </summary>
internal sealed record InterfaceMethodModel
{
    public required string Name { get; init; }
    public required string ReturnType { get; init; }
    public required string Parameters { get; init; }
    public required string ParameterNames { get; init; }
    public bool IsAsync { get; init; }
    public bool IsVoid { get; init; }
}

/// <summary>
///     Model representing a dependency of a service.
/// </summary>
internal sealed record DependencyModel
{
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required bool IsOptional { get; init; }
    public string? Key { get; init; }

    /// <summary>
    ///     The contract carries <c>[ProvidedByHost]</c>: the host registers it at runtime, where this
    ///     compilation cannot see it, so it is not reported as unregistered.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <see cref="Transforms.ServiceTransform" /> reads this off the contract's symbol. A feature
    ///     that describes a service the generator <b>writes</b> builds this model from names and has no
    ///     symbol to read, so it has to state it — <c>IdentityFeature.ResolverServices</c> and
    ///     <c>LocalIdentityStoreFeature</c> both do.
    /// </remarks>
    public bool IsProvidedByHost { get; init; }

    /// <summary>
    ///     The lifetime that declaration names — <c>"Singleton"</c>, <c>"Scoped"</c>,
    ///     <c>"Transient"</c> — or <c>null</c> when the contract does not say.
    /// </summary>
    /// <remarks>
    ///     Without it a singleton could capture a per-request contract unreported: the check ran off a
    ///     second hard-coded list of names, so a contract the generator had never heard of was exempt
    ///     from it as well as from PRAG1641.
    /// </remarks>
    public string? ProvidedByHostLifetime { get; init; }
}

/// <summary>
///     Model representing a property marked with [Inject].
/// </summary>
internal sealed record PropertyInjectionModel
{
    public required string PropertyName { get; init; }
    public required string PropertyTypeName { get; init; }
    public required bool IsRequired { get; init; }
    public string? Key { get; init; }

    /// <inheritdoc cref="DependencyModel.IsProvidedByHost" />
    public bool IsProvidedByHost { get; init; }

    /// <inheritdoc cref="DependencyModel.ProvidedByHostLifetime" />
    public string? ProvidedByHostLifetime { get; init; }
}

/// <summary>
///     Model representing a method marked with [Inject].
/// </summary>
internal sealed record MethodInjectionModel
{
    public required string MethodName { get; init; }
    public required EquatableArray<DependencyModel> Parameters { get; init; }
}

/// <summary>
///     Aggregated model for all registrations in an assembly.
/// </summary>
internal sealed record RegistrationModel
{
    public required EquatableArray<ServiceModel> Services { get; init; }
    public required EquatableArray<DecoratorModel> Decorators { get; init; }

}
