namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing an endpoint group.
/// </summary>
internal sealed record EndpointGroupModel
{
    /// <summary>
    ///     The fully qualified type name of the group class.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The route prefix for the group.
    /// </summary>
    public string? RoutePrefix { get; init; }

    /// <summary>
    ///     The OpenAPI tag for the group.
    /// </summary>
    public string? Tag { get; init; }

    /// <summary>
    ///     The API version for the group.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    ///     Parent group if nested.
    /// </summary>
    public EndpointGroupModel? Parent { get; init; }

    /// <summary>
    ///     True when the name given as the group did not resolve to any type (PRAG0507).
    /// </summary>
    /// <remarks>
    ///     Reported for a type in any assembly: the symbol the attribute carries either exists or is an
    ///     error symbol, and there is no third case to be careful about.
    /// </remarks>
    public bool NotFound { get; init; }

    /// <summary>
    ///     True when the referenced group type exists but is not decorated with [EndpointGroup]
    ///     (PRAG0507).
    /// </summary>
    public bool TypeExistsButNotGroup { get; init; }

    /// <summary>Whether PRAG0507 has something to say about this group.</summary>
    public bool IsUnusable => NotFound || TypeExistsButNotGroup;
}
