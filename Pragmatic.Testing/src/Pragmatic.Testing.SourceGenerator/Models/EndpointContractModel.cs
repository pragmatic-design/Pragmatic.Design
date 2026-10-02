namespace Pragmatic.Testing.SourceGenerator.Models;

/// <summary>
///     One endpoint's data for generating its authorization contract test (#7, phase 1): the route to call, the
///     verb, the permission it requires (if any), and the boundary it belongs to (for grouping into a class).
/// </summary>
internal sealed record EndpointContractModel
{
    public required string Boundary { get; init; }
    public required string ActionName { get; init; }
    public required string HttpMethod { get; init; }
    public required string Route { get; init; }

    /// <summary>The permission the endpoint requires, or null when it is anonymous.</summary>
    public required string? Permission { get; init; }

    /// <summary>
    ///     Whether this endpoint answers with a <b>collection</b>: a declared read that is neither
    ///     <c>Single</c> nor a page of one thing.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It decides whether the "unknown id is 404" contract is emitted. A route with a parameter is
    ///     not the same thing as a get-by-id: <c>GET /api/invoices/{id}/payments</c> is a sub-collection,
    ///     and the right answer for an id that matches nothing is <b>200 with an empty list</b>. Measured
    ///     against the first multi-tenant consumer to run these tests, where that one
    ///     generated test was the only red in a suite of 136.
    /// </remarks>
    public bool AnswersWithACollection { get; init; }

    public bool RequiresPermission => !string.IsNullOrEmpty(Permission);
}
