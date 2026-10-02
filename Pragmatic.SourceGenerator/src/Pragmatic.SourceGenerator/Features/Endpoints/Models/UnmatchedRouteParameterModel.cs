namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a route parameter that could not be matched to a public property.
/// </summary>
internal sealed record UnmatchedRouteParameterModel
{
    /// <summary>
    ///     The parameter name as it appears in the route (e.g., "amenityId").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The expected PascalCase property name (e.g., "AmenityId").
    /// </summary>
    public required string ExpectedPropertyName { get; init; }
}
