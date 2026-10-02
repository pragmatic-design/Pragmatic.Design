namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as scope-based access controlled.
///     The source generator will add an <c>AccessScopes</c> property (if not manually declared),
///     implement <see cref="IScopedEntity"/>, and generate a scoped data query filter.
/// </summary>
/// <remarks>
///     Named for what it adds to the entity, like the other trait markers. A name like
///     <c>[ScopedEntity]</c> would read as a variant of <c>[Entity]</c> rather than as a trait.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HasAccessScopesAttribute : Attribute;
