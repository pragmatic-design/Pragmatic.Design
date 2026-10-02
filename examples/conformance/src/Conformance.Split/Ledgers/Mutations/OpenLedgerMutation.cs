using Conformance.Split.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;

namespace Conformance.Split.Mutations;

/// <summary>
///     Writes a <c>Ledger</c>, and ends up on the facade of whoever owns it.
/// </summary>
/// <remarks>
///     The operation names no boundary: the generator derives it from the entity the mutation writes,
///     and from there from <c>[Owns&lt;Ledger&gt;]</c>. It is the second thing that declaration decides
///     — the first is which <c>DbContext</c> the table is in — and the two together are its whole
///     effect.
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/ledgers")]
public partial class OpenLedgerMutation : Mutation<Ledger>
{
    public required string Name { get; init; }
}
