using Conformance.Sales.Dtos;
using Conformance.Sales.Entities;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     The route that serves <see cref="OrderWireShapeDto" />: the document and the wire, comparable.
/// </summary>
/// <remarks>
///     It exists because the rule «what the serializer strips is not in the contract» cannot be measured
///     without a real response to read next to the document. The two halves come from different code —
///     the schema from Pragmatic's metadata, the body from the serializer — and their agreement is
///     something to check, not to assume.
/// </remarks>
[Query<Order, OrderWireShapeDto>(Single = true)]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/orders/{id}/wire-shape")]
public partial class GetOrderWireShapeQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
