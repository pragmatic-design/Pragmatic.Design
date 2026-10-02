using Conformance.Split.Entities;
using Pragmatic.Actions.Mutation;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;

namespace Conformance.Split.Mutations;

/// <summary>
///     The twin, on the other entity, which must end up on the <b>other</b> facade.
/// </summary>
/// <remarks>
///     Same operations folder, same namespace, same assembly as its twin: the only thing that separates
///     them is whose entity they write. Without this one, «the operation is on the right facade» would
///     be satisfied by a generator that puts every operation on every facade.
/// </remarks>
[Mutation(Mode = MutationMode.Create)]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/journals")]
public partial class OpenJournalMutation : Mutation<Journal>
{
    public required string Name { get; init; }
}
