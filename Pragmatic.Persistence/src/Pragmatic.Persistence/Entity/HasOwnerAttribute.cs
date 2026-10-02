namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as owned by its creator.
///     The source generator will add an <c>OwnerId</c> property (if not manually declared),
///     implement <see cref="IOwnedEntity"/>, and generate an ownership query filter.
/// </summary>
/// <remarks>
///     Named for what it adds to the entity, like the other trait markers — <c>[SoftDelete]</c>,
///     <c>[Auditable]</c>, <c>[ConcurrencyAware]</c>. A name like <c>[OwnedEntity]</c> would read as
///     a variant of <c>[Entity]</c> and collide with EF Core's <c>[Owned]</c> — a different concept
///     entirely, a value object mapped into its parent's table.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HasOwnerAttribute : Attribute;
