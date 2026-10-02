// Pragmatic.SourceGenerator - Composition - Discovered Endpoint Route Info

using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Discovered endpoint type and its group assignment from enriched metadata.
/// </summary>
internal sealed record DiscoveredEndpointRouteInfo
{
    /// <summary>Gets the fully qualified endpoint type name (with global:: prefix).</summary>
    public required string EndpointType { get; init; }

    /// <summary>Gets the fully qualified group type name (with global:: prefix), or null if ungrouped.</summary>
    public string? GroupType { get; init; }

    /// <summary>The HTTP verb, as the attribute spelled it.</summary>
    public string? Verb { get; init; }

    /// <summary>
    ///     The route as declared, without the group's prefix.
    /// </summary>
    /// <remarks>
    ///     The prefix lives on the group and is carried separately, because two endpoints can share a
    ///     route and differ by group — and that is not a collision.
    /// </remarks>
    public string? Route { get; init; }

    /// <summary>Gets the source assembly name.</summary>
    public required string SourceAssembly { get; init; }

    /// <summary>Permits per window from an inline <c>[RateLimit]</c>, or <c>null</c> when there is none.</summary>
    public int? RateLimitRequests { get; init; }

    /// <summary>The window from an inline <c>[RateLimit]</c> ("1s", "1m"), or <c>null</c>.</summary>
    public string? RateLimitWindow { get; init; }

    /// <summary>
    ///     A permission this route enforces that its author never declared and cannot switch off —
    ///     <c>[Autocomplete]</c> derives one from the boundary and the entity. Null for every route
    ///     whose permissions are the author's own choice.
    /// </summary>
    /// <remarks>
    ///     The host is the only place the question has an answer: under <c>[AnonymousHost]</c> nobody
    ///     will ever hold it, so that route answers 403 for the life of the application (PRAG1692).
    ///     The module cannot answer it, and asking it would pin <c>Identity.AspNetCore</c> to boundary
    ///     libraries that need nothing from it.
    /// </remarks>
    public string? DerivedPermission { get; init; }

    /// <summary>
    ///     Route prefix from [UsePackage] for package endpoints. Null for non-package endpoints.
    ///     Used to generate MapGroup(routePrefix) in the host.
    /// </summary>
    public string? PackageRoutePrefix { get; init; }

    /// <summary>
    ///     The processor types declared on the endpoint, which the host must register so the generated
    ///     handler can resolve them from the request services.
    /// </summary>
    public EquatableArray<string> Processors { get; init; } = EquatableArray<string>.Empty;
}

/// <summary>
///     Discovered endpoint group with its resolved route prefix from enriched metadata.
/// </summary>
internal sealed record DiscoveredEndpointGroupInfo
{
    /// <summary>Gets the fully qualified group type name (with global:: prefix).</summary>
    public required string GroupType { get; init; }

    /// <summary>Gets the full resolved route prefix (e.g., "/api/v1/invoices").</summary>
    public required string RoutePrefix { get; init; }

    /// <summary>Gets the fully qualified parent group type name, or null if top-level.</summary>
    public string? ParentGroupType { get; init; }

    /// <summary>Gets the source assembly name.</summary>
    public required string SourceAssembly { get; init; }
}
