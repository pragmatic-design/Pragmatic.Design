namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as concurrency-aware, adding optimistic concurrency control via row versioning.
/// </summary>
/// <remarks>
///     <para>
///         The source generator declares <c>public ulong RowVersion { get; internal set; }</c> on the entity, whichever engine
///         persists it; on EF Core the generated context maps it as the provider's row version.
///     </para>
///     <para>
///         A stale save is a <c>PersistenceRuleViolationException</c> carrying a <c>ConcurrencyError</c>, raised by the unit
///         of work on every engine. The generated repository returns that error through the Result pattern.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConcurrencyAwareAttribute : Attribute
{
}
