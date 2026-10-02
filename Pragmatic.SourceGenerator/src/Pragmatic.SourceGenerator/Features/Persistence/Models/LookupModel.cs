namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for a [Lookup] entity — small reference table preloaded into cache.
/// </summary>
internal sealed record LookupModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string IdType { get; init; }

    public bool IsValid => !string.IsNullOrEmpty(TypeName) && !string.IsNullOrEmpty(IdType);
}

/// <summary>
///     Model for an entity that consumes a lookup via a FK property ({LookupType}Id).
/// </summary>
internal sealed record LookupConsumerModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }

    /// <summary>The FK property name (e.g. "CountryId").</summary>
    public required string FkPropertyName { get; init; }

    /// <summary>The FK property type (e.g. "int", "System.Guid").</summary>
    public required string FkPropertyType { get; init; }

    /// <summary>Whether the FK property is nullable (e.g. Guid?).</summary>
    public bool IsFkNullable { get; init; }

    /// <summary>The lookup entity info.</summary>
    public required LookupModel Lookup { get; init; }
}
