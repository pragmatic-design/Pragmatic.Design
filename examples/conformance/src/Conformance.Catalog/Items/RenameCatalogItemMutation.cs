using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;

namespace Conformance.Catalog.Entities;

/// <summary>
///     A catalog operation, with a route of its own.
/// </summary>
/// <remarks>
///     ⚠️ The route is deliberately <b>different</b> from Sales' ones. It serves the
///     <c>TheSameAddressTwice</c> case, which makes it collide temporarily to measure that the host's
///     check catches it: <c>PRAG0529</c> sees the routes of a single compilation, and two different
///     libraries do not see each other.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
[AllowAnonymous]
[Endpoint(HttpVerb.Put, "api/catalog-items/{id}")]
public partial class RenameCatalogItemMutation : Mutation<CatalogItem>
{
    public required Guid Id { get; init; }

    public decimal ListPrice { get; init; }
}
