using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Immutable model representing an [Autocomplete] property for endpoint generation.
///     Captures entity metadata needed to generate an autocomplete endpoint.
/// </summary>
internal sealed record AutocompleteModel
{
    /// <summary>
    ///     Namespace of the containing entity class.
    /// </summary>
    public required string EntityNamespace { get; init; }

    /// <summary>
    ///     Simple name of the containing entity class.
    /// </summary>
    public required string EntityTypeName { get; init; }

    /// <summary>
    ///     Fully qualified name of the entity (e.g., "TestApp.Entities.Customer").
    /// </summary>
    public required string EntityTypeFullName { get; init; }

    /// <summary>
    ///     The property name to search on (e.g., "Name").
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The fully qualified type name of the key property (e.g., "global::System.Guid").
    /// </summary>
    public required string KeyPropertyTypeName { get; init; }

    /// <summary>
    ///     The name of the key property (e.g., "Id").
    /// </summary>
    public required string KeyPropertyName { get; init; }

    /// <summary>
    ///     Route template for the endpoint.
    /// </summary>
    public required string Route { get; init; }

    /// <summary>
    ///     Default result limit.
    /// </summary>
    public required int DefaultLimit { get; init; }

    /// <summary>
    ///     Accessibility of the containing entity class.
    /// </summary>
    public required string Accessibility { get; init; }

    /// <summary>
    ///     Fully qualified boundary type from [BelongsTo&lt;T&gt;] if present.
    ///     Used for keyed DbContext resolution.
    /// </summary>
    public string? BoundaryTypeFullName { get; init; }

    /// <summary>
    ///     Fully qualified DTO type name when using <c>[Autocomplete&lt;TDto&gt;]</c>.
    ///     Null for default mode (returns AutocompleteItem&lt;TKey&gt;).
    /// </summary>
    public string? DtoTypeFullName { get; init; }

    /// <summary>
    ///     Simple DTO type name (e.g., "PropertySearchResult").
    /// </summary>
    public string? DtoTypeName { get; init; }

    /// <summary>
    ///     Whether this autocomplete uses a custom DTO projection.
    /// </summary>
    public bool HasCustomDto => DtoTypeFullName is not null;

    /// <summary>
    ///     The generated endpoint class name.
    /// </summary>
    public string EndpointClassName => $"{EntityTypeName}{PropertyName}AutocompleteEndpoint";

    /// <summary>
    ///     The fully qualified endpoint class name.
    /// </summary>
    public string EndpointFullName =>
        string.IsNullOrEmpty(EntityNamespace) ? EndpointClassName : $"{EntityNamespace}.{EndpointClassName}";

    /// <summary>
    ///     The endpoint name for route generation.
    /// </summary>
    public string EndpointName => $"{EntityTypeName}{PropertyName}Autocomplete";

    /// <summary>
    ///     The CRUD read permission derived from boundary + entity (e.g., "booking.reservation.read").
    ///     Null if entity has no boundary.
    /// </summary>
    public string? EntityReadPermission { get; init; }

    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
    public AutocompleteInvalidReason InvalidReason { get; init; } = AutocompleteInvalidReason.None;
    public bool IsValid => InvalidReason == AutocompleteInvalidReason.None;

    /// <summary>
    ///     Converts to a minimal EndpointModel for registration purposes.
    /// </summary>
    public EndpointModel ToEndpointModelForRegistration()
    {
        return new EndpointModel
        {
            Namespace = EntityNamespace,
            TypeName = EndpointClassName,
            FullTypeName = $"global::{EndpointFullName}",
            Accessibility = Accessibility,
            HttpMethod = "Get",
            Route = Route,
            Name = EndpointName,
            IsVoid = false,
            IsDomainAction = false,
            IsVoidDomainAction = false,
            // Must match what AutocompleteEndpointTemplate declares with builder.Produces<>: without it the
            // manifest states the endpoint returns something without saying what, and a generated client can
            // only expose it as object.
            ResponseType = HasCustomDto
                ? $"System.Collections.Generic.List<{DtoTypeFullName}>"
                : $"System.Collections.Generic.List<global::Pragmatic.Endpoints.Responses.AutocompleteItem<{KeyPropertyTypeName}>>",
            // The permission this route gates on without anybody asking for it, carried so the host can
            // say whether it is holdable at all.
            DerivedPermission = EntityReadPermission
        };
    }
}

/// <summary>
///     Reason why an AutocompleteModel is invalid.
/// </summary>
internal enum AutocompleteInvalidReason
{
    None,
    MissingKeyProperty,
    NotStringProperty
}
