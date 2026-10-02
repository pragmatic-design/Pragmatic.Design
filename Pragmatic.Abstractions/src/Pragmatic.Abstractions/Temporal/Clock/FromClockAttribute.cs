namespace Pragmatic.Temporal.Clock;

/// <summary>
///     Fills a property of a declared query, an action or a mutation from the application's clock: the
///     generated invoker writes it after validation and authorization, before the read or the body.
/// </summary>
/// <remarks>
///     <para>
///         A <see cref="DateOnly" /> gets <see cref="IClock.UtcToday" /> — the date a mutation compares
///         against when it reads <c>IClock.UtcNow</c> — and a <see cref="DateTimeOffset" /> gets
///         <see cref="IClock.UtcNow" />. The clock is the registered <see cref="IClock" />, so a clock a
///         host or a test injects reaches the read as it reaches the writes.
///     </para>
///     <para>
///         The property is written <c>{ get; private set; }</c>. It is not a parameter anywhere — not a
///         query-string or route parameter, not in the OpenAPI document, not settable by an in-process
///         caller — and not a filter: a specification property of the query reads it, typically passing
///         it to a <c>[ComputedFilter]</c> method of the entity. It is still a property, so it is part of
///         the cache key and of the serialized query.
///     </para>
///     <para>
///         On an action or a mutation the body reads it — the instant a decision is taken, the day a
///         request is withdrawn — instead of an injected <see cref="IClock" />. On a mutation it is
///         written to the entity when the entity has a member of the same name, and is otherwise no input.
///     </para>
///     <para><c>PRAG0734</c> reports a binding the invoker cannot write.</para>
/// </remarks>
/// <example>
///     <code>
/// [FromClock]
/// public DateOnly Today { get; private set; }
///
/// public bool? AwayToday { get; init; }
///
/// public Specification&lt;Employee&gt;? Away =&gt;
///     AwayToday == true ? EmployeeComputedFilters.IsAwayOnSpec(Today) : null;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FromClockAttribute : Attribute;
