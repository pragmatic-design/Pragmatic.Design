using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model representing a mutation DTO for ApplyAsync generation.
///     This is read from DTOs decorated with [Mutation&lt;TEntity&gt;].
/// </summary>
internal sealed record MutationMetadataModel
{
    /// <summary>
    ///     The namespace of the DTO.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    ///     The name of the DTO type.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The full type name including namespace.
    /// </summary>
    public string FullTypeName => string.IsNullOrEmpty(Namespace) || Namespace == "<global namespace>"
        ? TypeName
        : $"{Namespace}.{TypeName}";

    /// <summary>
    ///     The accessibility modifier (public, internal, etc.).
    /// </summary>
    public required string Accessibility { get; init; }

    /// <summary>
    ///     The fully qualified name of the target entity type.
    /// </summary>
    public required string EntityFullTypeName { get; init; }

    /// <summary>
    ///     The simple name of the target entity type.
    /// </summary>
    public required string EntityTypeName { get; init; }

    /// <summary>
    ///     The ID type of the entity (from [Entity]).
    /// </summary>
    public string EntityIdType { get; init; } = "System.Guid";

    /// <summary>
    ///     The name of the DTO property that maps to PersistenceId/Id, if any.
    ///     If null, ApplyAsync will require an ID parameter.
    /// </summary>
    public string? IdPropertyOnDto { get; init; }

    /// <summary>
    ///     Navigation properties that need Include() for proper loading.
    /// </summary>
    public EquatableArray<string> RequiredIncludes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Properties to include in ApplyTo generation.
    /// </summary>
    public EquatableArray<MutationPropertyModel> Properties { get; init; } = EquatableArray<MutationPropertyModel>.Empty;

    /// <summary>
    ///     Location for diagnostics.
    /// </summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();

    /// <summary>
    ///     Whether this model is valid for code generation.
    /// </summary>

    /// <summary>
    ///     Whether the DTO has [MapTo&lt;TEntity&gt;] attribute, meaning ApplyTo(entity) is generated.
    ///     When true, Mutation can delegate simple property assignments to ApplyTo().
    /// </summary>
    public bool HasMapToAttribute { get; init; }

    /// <summary>
    ///     Lifecycle hooks found as nested classes inside the mutation DTO.
    /// </summary>
    public EquatableArray<MutationHookModel> Hooks { get; init; } = EquatableArray<MutationHookModel>.Empty;

    public bool IsValid => !string.IsNullOrEmpty(TypeName) && !string.IsNullOrEmpty(EntityTypeName);
}
