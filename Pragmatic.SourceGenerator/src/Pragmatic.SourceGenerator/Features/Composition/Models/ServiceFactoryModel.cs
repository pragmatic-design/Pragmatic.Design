// Pragmatic.SourceGenerator - Composition - ServiceFactory model

using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     A <c>[ServiceFactory]</c> class: the class is registered as a singleton and each of its
///     <c>[Factory]</c> methods registers its return type via a factory that calls the method.
///     Value-equatable for incremental caching (uses <see cref="EquatableArray{T}" />, never raw ImmutableArray).
/// </summary>
internal sealed record ServiceFactoryModel
{
    public required string Namespace { get; init; }

    /// <summary>Fully-qualified name of the factory class (registered as a singleton).</summary>
    public required string FactoryClassFullName { get; init; }

    public required EquatableArray<FactoryMethodModel> Methods { get; init; }

    public LocationInfo? Location { get; init; }

    /// <summary>Only the factory class + at least one valid [Factory] method make it worth registering.</summary>
    public bool IsValid => !Methods.IsDefaultOrEmpty;
}

/// <summary>A single <c>[Factory]</c> method: its return type becomes a registered service.</summary>
internal sealed record FactoryMethodModel
{
    public required string MethodName { get; init; }

    /// <summary>Fully-qualified return type — the registered service type.</summary>
    public required string ReturnTypeFullName { get; init; }

    /// <summary>Service lifetime ("Singleton" / "Scoped" / "Transient").</summary>
    public required string Lifetime { get; init; }

    public required EquatableArray<FactoryParameterModel> Parameters { get; init; }
}

/// <summary>A parameter of a <c>[Factory]</c> method, resolved from DI.</summary>
internal sealed record FactoryParameterModel
{
    public required string FullTypeName { get; init; }
    public string? Key { get; init; }
    public bool IsOptional { get; init; }
}
