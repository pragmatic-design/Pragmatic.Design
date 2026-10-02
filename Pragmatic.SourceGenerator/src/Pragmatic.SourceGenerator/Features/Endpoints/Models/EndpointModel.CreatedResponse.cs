using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>What a mutation endpoint answers with, when it is not the entity, and where its 201 points.</summary>
internal sealed partial record EndpointModel
{
    /// <summary>
    ///     The record a mutation answers with in place of the entity, when its <c>ReturnType</c> is
    ///     <c>Id</c> (<c>{Mutation}.IdResponse</c>, emitted by the handler template) or <c>LogicalKey</c>
    ///     (<c>{Mutation}.LogicalKey</c>, emitted by Actions). <c>null</c> for the entity.
    /// </summary>
    public string? MutationKeyResponseType { get; init; }

    /// <summary>
    ///     Whether <see cref="MutationKeyResponseType" /> is the Id record; otherwise it is the logical key.
    /// </summary>
    public bool MutationReturnsId { get; init; }

    /// <summary>
    ///     The properties of <see cref="MutationKeyResponseType" />, for the manifest: the generated record
    ///     has no symbol the manifest could walk.
    /// </summary>
    public EquatableArray<MutationKeyPartModel> MutationKeyResponseProperties { get; init; } =
        EquatableArray<MutationKeyPartModel>.Empty;

    /// <summary>
    ///     A <c>[ReturnsDto&lt;T&gt;]</c> declared beside a key <c>ReturnType</c>, for PRAG0535: the DTO's
    ///     name. Null when the mutation declares one or the other, not both.
    /// </summary>
    /// <remarks>
    ///     The key answers and the DTO is read by nothing — the handler builds the key record and never
    ///     reaches <c>FromEntity</c>. When this is set the DTO's own fields stay unset, so PRAG0531 and
    ///     PRAG0533 do not ask the author to fix a declaration nothing reads.
    /// </remarks>
    public string? ReturnsDtoBesideKeyResponse { get; init; }

    /// <summary>
    ///     A Create whose 201 carries a <c>Location</c> built from its request path: a <c>Single</c> query
    ///     on the same entity answers at this endpoint's route plus <c>/{id}</c>. Set once every endpoint
    ///     of the compilation is known; never by the transform, which sees one endpoint.
    /// </summary>
    public bool LocationFromReadRoute { get; init; }
}
