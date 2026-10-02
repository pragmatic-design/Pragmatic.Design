namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Fills a property of a DomainAction or a Mutation with the result of a declared query, before the body
///     runs: <c>[LoadFrom&lt;GetMyBalancesQuery&gt;] public IReadOnlyList&lt;BalanceDto&gt; Balances { get; private set; } = [];</c>
/// </summary>
/// <remarks>
///     <para>
///         The invoker builds the query, binding each of its inputs <b>by name</b>, ignoring case, from the
///         operation's properties, and runs it through the query's own invoker — its validation, its
///         permission, its read — after the operation's authorization. A query that fails fails the operation
///         with the same error: a 403 for a caller without the query's permission, a 404 for a <c>Single</c>
///         query that finds nothing.
///     </para>
///     <para>
///         The result is read-only data — the query's DTOs; rows the operation changes are loaded with
///         <c>[LoadEntity]</c> / <c>[LoadEntities]</c>. The property is not an input: it is excluded from the
///         request body, the OpenAPI document and the boundary signature. Its type is the query's result —
///         the DTO for a <c>Single</c> query, <c>PagedResult&lt;TDto&gt;</c> for a paged one,
///         <c>IReadOnlyList&lt;TDto&gt;</c> otherwise (<c>PRAG0458</c>); a <c>required</c> input of the query
///         with no property of its name is <c>PRAG0459</c>.
///     </para>
/// </remarks>
/// <typeparam name="TQuery">The declared <c>[Query]</c> to run.</typeparam>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LoadFromAttribute<TQuery> : Attribute where TQuery : class;
