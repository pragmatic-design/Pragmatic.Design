using Conformance.Sales.Entities;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     A query that answers with the <b>entity</b>, not with a DTO.
/// </summary>
/// <remarks>
///     <para>
///         Not a shape to imitate — an API's response is a DTO — but it is a shape the framework allows,
///         and one that exists in real applications.
///     </para>
///     <para>
///         ⚠️ It is here as a <b>control case for the published document</b>. Unreachable schemas must be
///         removed from <c>components.schemas</c>, and the shortcut would be to exclude entities: it
///         would work on every application in which nobody answers with one, and leave a <c>$ref</c>
///         pointing at nothing exactly in the ones where someone does. This query makes that shortcut
///         impossible to write without turning a test red.
///     </para>
/// </remarks>
[Query<Order, Order>(Single = true)]
[AllowAnonymous]
[Endpoint(HttpVerb.Get, "api/orders/{id}/raw")]
public partial class GetRawOrderQuery
{
    [Filter(MapTo = "PersistenceId")]
    public Guid Id { get; init; }
}
