namespace Pragmatic.Actions.Mutation;

/// <summary>
///     Specifies what the mutation returns after successful execution: what its boundary member returns
///     in process, and what its endpoint answers with.
/// </summary>
public enum MutationReturnType
{
    /// <summary>
    ///     Returns the entity's key: the boundary member returns the <c>Guid</c>, and the endpoint answers
    ///     <c>{"id": "…"}</c>.
    /// </summary>
    Id = 0,

    /// <summary>
    ///     Returns the entity's <c>[LogicKey]</c>: the boundary member returns a record the generator
    ///     emits on the mutation, <c>{Mutation}.LogicalKey</c>, with one property per part, and the
    ///     endpoint answers with the same record under the parts' wire names.
    /// </summary>
    LogicalKey = 1,

    /// <summary>
    ///     Returns the entity. This is the default. The endpoint answers with the DTO the mutation declares
    ///     with <c>[ReturnsDto&lt;T&gt;]</c> — which only this value reads: beside <see cref="Id" /> or
    ///     <see cref="LogicalKey" /> the DTO is reported as PRAG0535.
    /// </summary>
    /// <remarks>
    ///     Written explicitly and without a DTO, the endpoint answers with the entity. Left as the default
    ///     and without a DTO, it does not: a create of an entity answers its id, anything else
    ///     answers 204. The boundary member returns the entity either way.
    /// </remarks>
    Entity = 2
}
